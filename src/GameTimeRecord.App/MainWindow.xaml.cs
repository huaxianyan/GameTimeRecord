using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using GameTimeRecord.App.Controls;
using GameTimeRecord.App.ViewModels;
using GameTimeRecord.App.Views;
using GameTimeRecord.Core;

namespace GameTimeRecord.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _copyResetTimer;
    private CopyStatisticButton? _notifiedCopyButton;
    private bool _selectionChanging;
    private bool _closeCheckRunning;
    private bool _closeAllowed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => _viewModel.RefreshLiveStatistics();
        _clockTimer.Start();

        // 「已复制」在按钮上停留几秒就收回去。
        _copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _copyResetTimer.Tick += (_, _) => ResetCopyNotice();
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await RunAsync(async () =>
        {
            await _viewModel.InitializeAsync();
            await SyncSelectionAsync();
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closeAllowed)
        {
            _clockTimer.Stop();
            _copyResetTimer.Stop();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);
        if (_closeCheckRunning)
        {
            return;
        }

        _closeCheckRunning = true;
        _ = Dispatcher.InvokeAsync(CheckBeforeClosingAsync, DispatcherPriority.Normal);
    }

    private async Task CheckBeforeClosingAsync()
    {
        try
        {
            var playingGames = await _viewModel.GetPlayingGameNamesAsync();
            if (playingGames.Count > 0)
            {
                AppDialog.ShowMessage(
                    this,
                    "仍有游戏正在计时",
                    $"请先暂停或结束以下游戏：\n\n{string.Join("\n", playingGames)}");
                return;
            }

            _closeAllowed = true;
            Close();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            _closeCheckRunning = false;
        }
    }

    private async void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectionChanging)
        {
            return;
        }

        await RunAsync(() => _viewModel.SelectGameAsync(GamesList.SelectedItem as Game));
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GameEditDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var gameId = await _viewModel.AddGameAsync(
                dialog.GameName,
                dialog.GameAlias,
                dialog.GamePlatform,
                dialog.GameNotes);
            await ReloadGamesAndSelectionAsync(gameId);
        });
    }

    private async void EditGame_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGame is not { } game)
        {
            return;
        }

        var dialog = new GameEditDialog(game) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _viewModel.UpdateSelectedGameAsync(
                dialog.GameName,
                dialog.GameAlias,
                dialog.GamePlatform,
                dialog.GameNotes);
            await SyncSelectionAsync();
        });
    }

    private async void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGame is not { } game)
        {
            return;
        }

        var confirmed = AppDialog.Confirm(
            this,
            "删除游戏",
            $"确定要删除《{game.Name}》及其全部游玩记录吗？",
            "删除");
        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _viewModel.DeleteSelectedGameAsync();
            await SyncSelectionAsync();
        });
    }

    private async void Start_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.StartAsync);

    private async void Pause_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.PauseAsync);

    private async void Resume_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.ResumeAsync);

    private async void End_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(_viewModel.EndAsync);

    private async void EditEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PlayEventRow row })
        {
            return;
        }

        var dialog = new EventTimeDialog(row.LocalTime) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RunAsync(() => _viewModel.UpdateEventTimeAsync(row, dialog.LocalTimeText));
        }
    }

    private void CopyTotal_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(sender as CopyStatisticButton, _viewModel.TotalSeconds);

    private void CopyCount_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(sender as CopyStatisticButton, _viewModel.PlayCount);

    private void CopyFirst_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(sender as CopyStatisticButton, _viewModel.FirstPlayedAt);

    private void CopyLast_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(sender as CopyStatisticButton, _viewModel.LastPlayedAt);

    private void CopyStatistic(CopyStatisticButton? button, string value)
    {
        if (button is null)
        {
            return;
        }

        // 上一次的「已复制」先收回去，免得两个按钮同时挂着提示。
        ResetCopyNotice();

        var owner = new WindowInteropHelper(this).Handle;
        var result = ClipboardWriter.Write(owner, value, out var failureDetail);

        if (result == ClipboardWriteResult.Failed)
        {
            AppDialog.ShowMessage(this, "复制失败", failureDetail, AppDialogKind.Error);
            return;
        }

        button.State = CopyButtonState.Done;
        _notifiedCopyButton = button;
        _copyResetTimer.Stop();
        _copyResetTimer.Start();
    }

    /// <summary>
    /// 把按钮上的「已复制」收回成普通的「复制」。提示按几秒的节奏自动收回，
    /// 用户不等提示结束就点下一个按钮时也会先走这里。
    /// </summary>
    private void ResetCopyNotice()
    {
        _copyResetTimer.Stop();
        if (_notifiedCopyButton is { } button)
        {
            button.State = CopyButtonState.Idle;
            _notifiedCopyButton = null;
        }
    }

    private async Task ReloadGamesAndSelectionAsync(long gameId)
    {
        await _viewModel.ReloadGamesAsync(gameId);
        await SyncSelectionAsync();
    }

    private Task SyncSelectionAsync()
    {
        _selectionChanging = true;
        GamesList.SelectedItem = _viewModel.SelectedGame;
        GamesList.ScrollIntoView(_viewModel.SelectedGame);
        _selectionChanging = false;
        return Task.CompletedTask;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void ShowError(Exception exception)
    {
        AppDialog.ShowMessage(
            this,
            "操作未完成",
            exception.Message,
            AppDialogKind.Error);
    }
}
