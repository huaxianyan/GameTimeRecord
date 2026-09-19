using System.Windows;
using System.Windows.Controls;

namespace GameTimeRecord.App.Controls;

/// <summary>
/// 统计卡片上的「复制」按钮。外观全部由 <see cref="State"/> 驱动，样式写在
/// MainWindow 的资源里：空闲是普通的「复制」，成功后短暂变成绿色的「已复制」，
/// 几秒后由调用方复位。不区分写入走了哪条路，也不显示中间状态。
/// </summary>
public sealed class CopyStatisticButton : Button
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State),
            typeof(CopyButtonState),
            typeof(CopyStatisticButton),
            new PropertyMetadata(CopyButtonState.Idle));

    public CopyButtonState State
    {
        get => (CopyButtonState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }
}

public enum CopyButtonState
{
    /// <summary>普通的「复制」。</summary>
    Idle,

    /// <summary>写入成功，短暂显示「已复制」。</summary>
    Done,
}
