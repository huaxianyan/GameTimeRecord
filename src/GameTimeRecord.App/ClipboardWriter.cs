using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace GameTimeRecord.App;

internal enum ClipboardWriteResult
{
    /// <summary>内容已写进剪贴板。不区分走的哪条路——调用方只看成功与否。</summary>
    Written,

    /// <summary>三条路都失败，调用方需要把 failureDetail 展示给用户。</summary>
    Failed,
}

/// <summary>
/// 把纯文本写入系统剪贴板。动手前先用窗口句柄形式探一次剪贴板（开合一次，微秒级），
/// 探不通就直接跳过第 1 条路——OLE 的 flush 内部也用句柄形式，探不通时它必定失败，
/// 还要白等约 1 秒的内部重试。之后三条路依次尝试：
///
/// 1. WPF 的 <see cref="Clipboard"/>（OLE 剪贴板）。要先和现有所有者协商，
///    谈不拢就抛 COMException 0x800401D0 (CLIPBRD_E_CANT_OPEN)，内部只重试约 1 秒。
/// 2. OpenClipboard(窗口句柄) / EmptyClipboard / SetClipboardData。直接丢弃旧内容，
///    不和任何进程协商，但要求剪贴板没有被别人打开。
/// 3. 同上的原生路径，改用 OpenClipboard(IntPtr.Zero)。窗口句柄形式和无窗口形式
///    互斥（同一进程内也是），所以形式 2 被拒时形式 3 仍可能成功——实测这种状态
///    确实出现过（见下面失败重试的说明）。
///
/// 三条路都失败后按退避间隔把三条路重走一遍。实测「第一次点复制失败、再点一次就成功」
/// 是真实存在的行为，所以重试要带上 OLE，只重试原生路径会一直撞在同一堵墙上；每轮试
/// OLE 之前同样先探一次，探不通就跳过。
///
/// 仍然失败时不再抛异常，而是返回一份现场报告——谁持有剪贴板、两种打开形式各自是否
/// 可用、里面是什么格式，用来定位是哪台机器上的哪个程序在干扰。
/// </summary>
internal static class ClipboardWriter
{
    private const int CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int MaxFormatsInReport = 12;

    // 首次三条路都失败后，按这个节奏把三条路重走一遍。
    private static readonly int[] RetryDelaysInMilliseconds = [150, 400, 800];

    private static int _writeCount;

    public static ClipboardWriteResult Write(
        IntPtr ownerWindow,
        string text,
        out string failureDetail)
    {
        _writeCount++;
        var firstWriteInProcess = _writeCount == 1;

        var oleFailure = string.Empty;
        var handleFailure = string.Empty;
        var nullFailure = string.Empty;
        var oleSkipped = false;
        var observedHolders = new List<string>();

        // 先用窗口句柄形式探一下（开合一次，不写内容，微秒级）。OLE 的 flush 内部
        // 走的就是句柄形式，所以这个探测能提前判出 OLE 会不会失败——实测句柄形式被拒
        // 时 OLE 必定失败，还要白等 1131ms 的内部重试；可用时只要 2–21ms。
        // 用户遇到的正是前一种状态，跳过 OLE 能把他感觉到的「卡一下」直接消掉。
        if (CanOpenClipboardWithHandle(ownerWindow))
        {
            if (TryOle(text, ref oleFailure))
            {
                failureDetail = string.Empty;
                return ClipboardWriteResult.Written;
            }
        }
        else
        {
            oleSkipped = true;
        }

        if (TryNative(ownerWindow, text, ref handleFailure)
            || TryNative(IntPtr.Zero, text, ref nullFailure))
        {
            failureDetail = string.Empty;
            return ClipboardWriteResult.Written;
        }

        // 之前某次失败可能留下一个没配对关闭的剪贴板打开状态，它会把窗口句柄形式
        // 彻底挡死（实测：留着无窗口打开时，OpenClipboard(句柄) 报系统错误 5，
        // 而 CloseClipboard 一次就能把它放掉）。对没打开的剪贴板调用它是安全失败，
        // 所以这里清一层不会影响别的进程。
        var releasedResidue = 0;
        while (releasedResidue < 2 && CloseClipboard())
        {
            releasedResidue++;
        }

        // 这段重试连同前面的三次尝试都在 UI 线程同步跑，最坏约 2.4 秒。
        // 期间窗口是无响应的，调用方不画中间状态，成功后直接切到「已复制」。
        for (var index = 0; index < RetryDelaysInMilliseconds.Length; index++)
        {
            Thread.Sleep(RetryDelaysInMilliseconds[index]);

            // 第一轮再给 OLE 一次机会，但同样先用预检判断值不值得试——它每次要搭进去
            // 1 秒。这里不能改用 GetOpenClipboardWindow 判断：用户那个场景里它返回 0，
            // 句柄形式却仍被拒，OLE 试了就是白等。
            if (index == 0 && CanOpenClipboardWithHandle(ownerWindow))
            {
                if (TryOle(text, ref oleFailure))
                {
                    failureDetail = string.Empty;
                    return ClipboardWriteResult.Written;
                }
            }

            if (TryNative(ownerWindow, text, ref handleFailure)
                || TryNative(IntPtr.Zero, text, ref nullFailure))
            {
                failureDetail = string.Empty;
                return ClipboardWriteResult.Written;
            }

            var holder = DescribeWindow(GetOpenClipboardWindow());
            if (holder is not null && !observedHolders.Contains(holder))
            {
                observedHolders.Add(holder);
            }
        }

        failureDetail = BuildFailureReport(
            ownerWindow,
            firstWriteInProcess,
            oleFailure,
            handleFailure,
            nullFailure,
            oleSkipped,
            releasedResidue,
            observedHolders);
        return ClipboardWriteResult.Failed;
    }

    /// <summary>
    /// 以窗口句柄形式试着打开剪贴板再立刻关闭，用来判断 OLE 值不值得试。
    ///
    /// OLE 的 flush 内部走的就是窗口句柄形式，所以这个探测的结果能提前预示 OLE 的成败：
    /// 句柄形式被拒时 OLE 必定失败，而且要在内部重试里白等约 1 秒；句柄形式可用时 OLE
    /// 只要几毫秒。用户那次「第一次点击卡一下」正是前者。
    ///
    /// 只做开合，不写内容也不清空，不影响剪贴板里原有的数据。
    /// </summary>
    private static bool CanOpenClipboardWithHandle(IntPtr ownerWindow)
    {
        if (ownerWindow == IntPtr.Zero)
        {
            // 窗口句柄还没建出来，这个形式探不了，交给 OLE 自己去试。
            return true;
        }

        if (!OpenClipboard(ownerWindow))
        {
            return false;
        }

        CloseClipboard();
        return true;
    }

    private static bool TryOle(string text, ref string failure)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception exception)
        {
            failure = Describe(exception);
            return false;
        }
    }

    /// <summary>
    /// 原生写入。<paramref name="openTarget"/> 传窗口句柄就是传统写法（成为剪贴板所有者），
    /// 传 <see cref="IntPtr.Zero"/> 就是无窗口写法——两者互斥，被拒的那个换另一种往往能成。
    /// </summary>
    private static bool TryNative(IntPtr openTarget, string text, ref string failure)
    {
        try
        {
            SetTextNative(openTarget, text);
            return true;
        }
        catch (Exception exception)
        {
            failure = Describe(exception);
            return false;
        }
    }

    private static void SetTextNative(IntPtr openTarget, string text)
    {
        var handle = IntPtr.Zero;
        try
        {
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "分配内存失败");
            }

            var target = GlobalLock(handle);
            if (target == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "锁定内存失败");
            }

            Marshal.Copy(bytes, 0, target, bytes.Length);
            GlobalUnlock(handle);

            if (!OpenClipboard(openTarget))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "打开剪贴板失败");
            }

            try
            {
                if (!EmptyClipboard())
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "清空剪贴板失败");
                }

                if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "写入剪贴板失败");
                }

                // 内存所有权已交给剪贴板，不能再释放。
                handle = IntPtr.Zero;
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                GlobalFree(handle);
            }
        }
    }

    private static string BuildFailureReport(
        IntPtr ownerWindow,
        bool firstWriteInProcess,
        string oleFailure,
        string handleFailure,
        string nullFailure,
        bool oleSkipped,
        int releasedResidue,
        List<string> observedHolders)
    {
        // 报告写在所有尝试之后，这里再探一次两种打开形式，结论才准。
        var windowFormOpen = OpenClipboard(ownerWindow);
        var windowFormError = Marshal.GetLastWin32Error();
        if (windowFormOpen)
        {
            CloseClipboard();
        }

        var nullFormOpen = OpenClipboard(IntPtr.Zero);
        var nullFormError = Marshal.GetLastWin32Error();
        if (nullFormOpen)
        {
            CloseClipboard();
        }

        var opener = DescribeWindow(GetOpenClipboardWindow());
        var owner = DescribeWindow(GetClipboardOwner());
        var retrySeconds = RetryDelaysInMilliseconds.Sum() / 1000.0;

        var report = new StringBuilder();
        report.AppendLine("剪贴板当前无法写入。");
        report.AppendLine();

        if (opener is not null)
        {
            report.AppendLine(
                $"重试约 {retrySeconds:0.#} 秒期间，剪贴板一直被 {opener} 占着，等它释放后再复制即可。");
        }
        else if (!windowFormOpen && nullFormOpen)
        {
            report.AppendLine(
                "剪贴板被以「无窗口」方式打开着：用窗口句柄的写法会被拒绝，只有无窗口写法能用，而这次无窗口写法也没成。");
            report.AppendLine("这种状态说明有个程序打开了剪贴板却没关掉。把这份报告发我，我照着这条线索查。");
        }
        else if (!windowFormOpen && !nullFormOpen)
        {
            report.AppendLine(
                "剪贴板现在两种打开方式都被拒绝，但探测不到占用窗口，说明占用它的程序没有暴露窗口。");
        }
        else
        {
            report.AppendLine(
                $"重试约 {retrySeconds:0.#} 秒期间没探测到占用者，说明那是一次已经结束的短时占用。若反复出现，留意剪贴板同步、远程控制这类会读写剪贴板的常驻程序。");
        }

        report.AppendLine();
        report.AppendLine(oleSkipped
            ? "· 常规写入（OLE）：未尝试——窗口句柄形式打不开剪贴板，OLE 必然同样失败，还要白等约 1 秒"
            : $"· 常规写入（OLE）：{oleFailure}");
        report.AppendLine($"· 备用写入（窗口句柄）：{handleFailure}");
        report.AppendLine($"· 备用写入（无窗口）：{nullFailure}");

        if (releasedResidue > 0)
        {
            report.AppendLine($"· 顺手清掉了 {releasedResidue} 层没配对关闭的剪贴板打开状态。");
        }

        report.AppendLine();
        report.AppendLine(
            $"剪贴板打开状态：窗口句柄形式={(windowFormOpen ? "可用" : $"被拒，系统错误 {windowFormError}")}，" +
            $"无窗口形式={(nullFormOpen ? "可用" : $"被拒，系统错误 {nullFormError}")}");
        report.AppendLine($"剪贴板打开者：{opener ?? "无"}");
        report.AppendLine($"剪贴板内容来自：{owner ?? "无"}");
        report.AppendLine($"剪贴板现有内容：{DescribeFormats()}");
        report.AppendLine();
        report.Append(
            $"本次运行第 {_writeCount} 次写入（首次写入：{(firstWriteInProcess ? "是" : "否")}）。");

        return report.ToString();
    }

    private static string? DescribeWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        var name = "未知进程";
        try
        {
            using var process = Process.GetProcessById((int)processId);
            name = process.ProcessName;
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return $"{name}（PID {processId}）";
    }

    private static string DescribeFormats()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return "（读不出来，剪贴板同样被占用）";
        }

        try
        {
            var names = new List<string>();
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                if (names.Count >= MaxFormatsInReport)
                {
                    names.Add("…");
                    break;
                }

                names.Add(FormatName(format));
            }

            return names.Count == 0 ? "（空）" : string.Join("、", names);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static string FormatName(uint format) => format switch
    {
        1 => "文本",
        2 => "位图",
        3 => "图元文件",
        8 => "设备无关位图",
        13 => "Unicode 文本",
        14 => "增强图元文件",
        15 => "文件列表",
        16 => "区域设置",
        17 => "设备无关位图 V5",
        _ => $"格式 {format}",
    };

    private static string Describe(Exception exception) => exception switch
    {
        // Win32Exception 的消息里没有错误码，而 COMException 的消息自带 HRESULT。
        Win32Exception win32 => $"{win32.Message}（系统错误 {win32.NativeErrorCode}）",
        _ => exception.Message,
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(int uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
