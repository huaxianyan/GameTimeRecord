using System.IO;
using System.Windows;
using System.Windows.Threading;
using GameTimeRecord.App.Data;
using GameTimeRecord.App.ViewModels;
using GameTimeRecord.App.Views;

namespace GameTimeRecord.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            AppDialog.ShowMessage(
                owner: null,
                "无法启动",
                "无法确定本地数据保存位置，请检查 Windows 用户配置后重试。",
                AppDialogKind.Error);
            Shutdown(1);
            return;
        }

        var databasePath = Path.Combine(
            localData,
            "GameTimeRecord",
            "game-time-record.db");
        var repository = new SqliteGameRepository(databasePath);
        var window = new MainWindow(new MainViewModel(repository));
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        AppDialog.ShowMessage(
            MainWindow,
            "出现未处理的错误",
            e.Exception.Message,
            AppDialogKind.Error);
    }
}
