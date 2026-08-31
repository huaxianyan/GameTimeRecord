using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameTimeRecord.App.ViewModels;
using GameTimeRecord.App.Views;
using GameTimeRecord.Core;

namespace GameTimeRecord.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _noticeTimer;
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

        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _noticeTimer.Tick += (_, _) =>
        {
            NoticeText.Text = string.Empty;
            _noticeTimer.Stop();
        };
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

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeAllowed)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_closeCheckRunning)
        {
            return;
        }

        _closeCheckRunning = true;
        try
        {
            var playingGames = await _viewModel.GetPlayingGameNamesAsync();
            if (playingGames.Count > 0)
            {
                MessageBox.Show(
                    this,
                    $"请先暂停或结束以下游戏：\n\n{string.Join("\n", playingGames)}",
                    "仍有游戏正在计时",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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

        var result = MessageBox.Show(
            this,
            $"确定要删除《{game.Name}》及其全部游玩记录吗？",
            "删除游戏",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
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

    private void ToggleHistory_Click(object sender, RoutedEventArgs e)
    {
        var showHistory = HistoryPanel.Visibility != Visibility.Visible;
        HistoryPanel.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Content = showHistory ? "收起游玩记录" : "游玩记录详情";
    }

    private async void EditEvent_Click(object sender, RoutedEventArgs e)
    {
        if (EventsGrid.SelectedItem is not PlayEventRow row)
        {
            MessageBox.Show(
                this,
                "请先选择一条游玩记录。",
                "修改记录时间",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new EventTimeDialog(row.LocalTime) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RunAsync(() => _viewModel.UpdateEventTimeAsync(row, dialog.LocalTimeText));
        }
    }

    private void CopyTotal_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(_viewModel.TotalSeconds);

    private void CopyCount_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(_viewModel.PlayCount);

    private void CopyFirst_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(_viewModel.FirstPlayedAt);

    private void CopyLast_Click(object sender, RoutedEventArgs e) =>
        CopyStatistic(_viewModel.LastPlayedAt);

    private void CopyStatistic(string value)
    {
        Clipboard.SetText(value);
        NoticeText.Text = "已复制";
        _noticeTimer.Stop();
        _noticeTimer.Start();
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
        MessageBox.Show(
            this,
            exception.Message,
            "操作未完成",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
