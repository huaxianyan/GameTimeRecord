using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GameTimeRecord.App.Data;
using GameTimeRecord.Core;

namespace GameTimeRecord.App.ViewModels;

public sealed class MainViewModel(SqliteGameRepository repository) : ObservableObject
{
    private IReadOnlyList<PlaySession> _sessions = [];
    private Game? _selectedGame;
    private string _totalSeconds = "0";
    private string _playCount = "0";
    private string _firstPlayedAt = "暂无记录";
    private string _lastPlayedAt = "暂无记录";
    private string _sessionStatus = "尚未开始";
    private bool _isPlaying;
    private bool _isPaused;

    public ObservableCollection<Game> Games { get; } = [];

    public ObservableCollection<PlayEventRow> PlayEvents { get; } = [];

    public Game? SelectedGame
    {
        get => _selectedGame;
        private set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                OnPropertyChanged(nameof(HasSelectedGame));
            }
        }
    }

    public bool HasSelectedGame => SelectedGame is not null;

    public string TotalSeconds
    {
        get => _totalSeconds;
        private set => SetProperty(ref _totalSeconds, value);
    }

    public string PlayCount
    {
        get => _playCount;
        private set => SetProperty(ref _playCount, value);
    }

    public string FirstPlayedAt
    {
        get => _firstPlayedAt;
        private set
        {
            if (SetProperty(ref _firstPlayedAt, value))
            {
                OnPropertyChanged(nameof(CanCopyFirstPlayedAt));
            }
        }
    }

    public string LastPlayedAt
    {
        get => _lastPlayedAt;
        private set
        {
            if (SetProperty(ref _lastPlayedAt, value))
            {
                OnPropertyChanged(nameof(CanCopyLastPlayedAt));
            }
        }
    }

    public string SessionStatus
    {
        get => _sessionStatus;
        private set => SetProperty(ref _sessionStatus, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set => SetProperty(ref _isPaused, value);
    }

    public bool CanStart => HasSelectedGame && !IsPlaying && !IsPaused;

    public bool CanEnd => IsPlaying || IsPaused;

    public bool CanCopyFirstPlayedAt => FirstPlayedAt != "暂无记录";

    public bool CanCopyLastPlayedAt => LastPlayedAt != "暂无记录";

    public async Task InitializeAsync()
    {
        await repository.InitializeAsync();
        await ReloadGamesAsync();
    }

    public async Task ReloadGamesAsync(long? selectedGameId = null)
    {
        selectedGameId ??= SelectedGame?.Id;
        var games = await repository.GetGamesAsync();
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(game);
        }

        var selected = games.FirstOrDefault(game => game.Id == selectedGameId)
            ?? games.FirstOrDefault();
        await SelectGameAsync(selected);
    }

    public async Task SelectGameAsync(Game? game)
    {
        SelectedGame = game;
        await ReloadSelectedGameAsync();
    }

    public async Task<long> AddGameAsync(string name, string alias, string platform, string notes)
    {
        EnsureName(name);
        return await repository.AddGameAsync(
            name.Trim(),
            alias.Trim(),
            platform.Trim(),
            notes.Trim(),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public async Task UpdateSelectedGameAsync(string name, string alias, string platform, string notes)
    {
        var game = SelectedGame
            ?? throw new InvalidOperationException("请先选择一个游戏。");
        EnsureName(name);
        await repository.UpdateGameAsync(game with
        {
            Name = name.Trim(),
            Alias = alias.Trim(),
            Platform = platform.Trim(),
            Notes = notes.Trim(),
        });
        await ReloadGamesAsync(game.Id);
    }

    public async Task DeleteSelectedGameAsync()
    {
        var game = SelectedGame
            ?? throw new InvalidOperationException("请先选择一个游戏。");
        await repository.DeleteGameAsync(game.Id);
        await ReloadGamesAsync();
    }

    public async Task StartAsync()
    {
        var game = RequireSelectedGame();
        await repository.StartSessionAsync(game.Id, UtcNowSeconds());
        await ReloadSelectedGameAsync();
    }

    public async Task PauseAsync()
    {
        await AddEventAsync(PlayEventType.Pause);
    }

    public async Task ResumeAsync()
    {
        await AddEventAsync(PlayEventType.Resume);
    }

    public async Task EndAsync()
    {
        await AddEventAsync(PlayEventType.End);
    }

    public async Task UpdateEventTimeAsync(PlayEventRow row, string localTimeText)
    {
        var game = RequireSelectedGame();
        if (!DateTime.TryParseExact(
                localTimeText,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localTime))
        {
            throw new InvalidOperationException("时间格式应为“年-月-日 时:分:秒”，请修改后重试。");
        }

        var localOffset = TimeZoneInfo.Local.GetUtcOffset(localTime);
        var timestamp = new DateTimeOffset(
            DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            localOffset).ToUnixTimeSeconds();
        await repository.UpdateEventTimestampAsync(game.Id, row.EventId, timestamp);
        await ReloadSelectedGameAsync();
    }

    public void RefreshLiveStatistics()
    {
        ApplyStatistics();
    }

    public async Task<IReadOnlyList<string>> GetPlayingGameNamesAsync()
    {
        var playingGames = new List<string>();
        foreach (var game in Games)
        {
            var sessions = await repository.GetSessionsAsync(game.Id);
            if (sessions.Any(session =>
                    PlaySessionRules.GetStatus(session.Events) == PlaySessionStatus.Playing))
            {
                playingGames.Add(game.Name);
            }
        }

        return playingGames;
    }

    private async Task AddEventAsync(PlayEventType eventType)
    {
        var game = RequireSelectedGame();
        await repository.AddEventAsync(game.Id, eventType, UtcNowSeconds());
        await ReloadSelectedGameAsync();
    }

    private async Task ReloadSelectedGameAsync()
    {
        _sessions = SelectedGame is null
            ? []
            : await repository.GetSessionsAsync(SelectedGame.Id);
        PlayEvents.Clear();

        for (var sessionIndex = _sessions.Count - 1; sessionIndex >= 0; sessionIndex--)
        {
            var session = _sessions[sessionIndex];
            foreach (var playEvent in session.Events.Reverse())
            {
                PlayEvents.Add(new PlayEventRow(
                    playEvent.Id,
                    sessionIndex + 1,
                    GetEventName(playEvent.Type),
                    FormatTimestamp(playEvent.TimestampUtcSeconds)));
            }
        }

        var unfinished = _sessions.LastOrDefault(session =>
            PlaySessionRules.GetStatus(session.Events) != PlaySessionStatus.Ended);
        var status = unfinished is null
            ? (PlaySessionStatus?)null
            : PlaySessionRules.GetStatus(unfinished.Events);
        IsPlaying = status == PlaySessionStatus.Playing;
        IsPaused = status == PlaySessionStatus.Paused;
        SessionStatus = status switch
        {
            PlaySessionStatus.Playing => "正在游玩",
            PlaySessionStatus.Paused => "已暂停",
            _ when _sessions.Count > 0 => "上次游玩已结束",
            _ => "尚未开始",
        };
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanEnd));
        ApplyStatistics();
    }

    private void ApplyStatistics()
    {
        var statistics = GameStatisticsCalculator.Calculate(_sessions, UtcNowSeconds());
        TotalSeconds = statistics.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        PlayCount = statistics.PlayCount.ToString(CultureInfo.InvariantCulture);
        FirstPlayedAt = FormatTimestamp(statistics.FirstPlayedAtUtcSeconds);
        LastPlayedAt = FormatTimestamp(statistics.LastPlayedAtUtcSeconds);
    }

    private Game RequireSelectedGame() => SelectedGame
        ?? throw new InvalidOperationException("请先选择一个游戏。");

    private static void EnsureName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("请填写游戏名称。");
        }
    }

    private static long UtcNowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string FormatTimestamp(long? timestamp) => timestamp.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(timestamp.Value)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        : "暂无记录";

    private static string GetEventName(PlayEventType eventType) => eventType switch
    {
        PlayEventType.Start => "开始",
        PlayEventType.Pause => "暂停",
        PlayEventType.Resume => "恢复",
        PlayEventType.End => "结束",
        _ => throw new InvalidOperationException("未知的游玩记录类型。"),
    };
}

public sealed record PlayEventRow(
    long EventId,
    int SessionNumber,
    string Action,
    string LocalTime);
