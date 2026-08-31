using System.IO;
using GameTimeRecord.Core;
using Microsoft.Data.Sqlite;

namespace GameTimeRecord.App.Data;

public sealed class SqliteGameRepository(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        ForeignKeys = true,
        Pooling = false,
    }.ToString();

    public async Task InitializeAsync()
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS games (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                alias TEXT NOT NULL,
                platform TEXT NOT NULL,
                notes TEXT NOT NULL,
                created_at_utc_seconds INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS play_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                game_id INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS play_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL REFERENCES play_sessions(id) ON DELETE CASCADE,
                event_type INTEGER NOT NULL,
                timestamp_utc_seconds INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_play_sessions_game_id
                ON play_sessions(game_id);
            CREATE INDEX IF NOT EXISTS ix_play_events_session_id
                ON play_events(session_id);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Game>> GetGamesAsync()
    {
        var games = new List<Game>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, alias, platform, notes, created_at_utc_seconds
            FROM games
            ORDER BY name COLLATE NOCASE, id;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            games.Add(new Game(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5)));
        }

        return games;
    }

    public async Task<long> AddGameAsync(
        string name,
        string alias,
        string platform,
        string notes,
        long createdAtUtcSeconds)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO games (name, alias, platform, notes, created_at_utc_seconds)
            VALUES ($name, $alias, $platform, $notes, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$alias", alias);
        command.Parameters.AddWithValue("$platform", platform);
        command.Parameters.AddWithValue("$notes", notes);
        command.Parameters.AddWithValue("$createdAt", createdAtUtcSeconds);
        return (long)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("游戏记录未能保存，请重试。"));
    }

    public async Task UpdateGameAsync(Game game)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE games
            SET name = $name, alias = $alias, platform = $platform, notes = $notes
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$name", game.Name);
        command.Parameters.AddWithValue("$alias", game.Alias);
        command.Parameters.AddWithValue("$platform", game.Platform);
        command.Parameters.AddWithValue("$notes", game.Notes);
        command.Parameters.AddWithValue("$id", game.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteGameAsync(long gameId)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM games WHERE id = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<PlaySession>> GetSessionsAsync(long gameId)
    {
        var sessions = new List<PlaySession>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, e.id, e.event_type, e.timestamp_utc_seconds
            FROM play_sessions s
            INNER JOIN play_events e ON e.session_id = s.id
            WHERE s.game_id = $gameId
            ORDER BY s.id, e.id;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);

        await using var reader = await command.ExecuteReaderAsync();
        long? sessionId = null;
        List<PlayEvent>? events = null;
        while (await reader.ReadAsync())
        {
            var currentSessionId = reader.GetInt64(0);
            if (sessionId != currentSessionId)
            {
                if (sessionId.HasValue && events is not null)
                {
                    sessions.Add(new PlaySession(sessionId.Value, gameId, events));
                }

                sessionId = currentSessionId;
                events = [];
            }

            events!.Add(new PlayEvent(
                reader.GetInt64(1),
                currentSessionId,
                (PlayEventType)reader.GetInt32(2),
                reader.GetInt64(3)));
        }

        if (sessionId.HasValue && events is not null)
        {
            sessions.Add(new PlaySession(sessionId.Value, gameId, events));
        }

        return sessions;
    }

    public async Task StartSessionAsync(long gameId, long timestampUtcSeconds)
    {
        var sessions = await GetSessionsAsync(gameId);
        if (sessions.Any(session =>
                PlaySessionRules.GetStatus(session.Events) != PlaySessionStatus.Ended))
        {
            throw new InvalidOperationException("这个游戏已有未结束的游玩，请先继续当前记录。");
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();

        var sessionCommand = connection.CreateCommand();
        sessionCommand.Transaction = transaction;
        sessionCommand.CommandText = """
            INSERT INTO play_sessions (game_id) VALUES ($gameId);
            SELECT last_insert_rowid();
            """;
        sessionCommand.Parameters.AddWithValue("$gameId", gameId);
        var sessionId = (long)(await sessionCommand.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("游玩记录未能开始，请重试。"));

        await InsertEventAsync(
            connection,
            transaction,
            sessionId,
            PlayEventType.Start,
            timestampUtcSeconds);
        await transaction.CommitAsync();
    }

    public async Task AddEventAsync(
        long gameId,
        PlayEventType eventType,
        long timestampUtcSeconds)
    {
        var sessions = await GetSessionsAsync(gameId);
        var activeSession = sessions.LastOrDefault(session =>
            PlaySessionRules.GetStatus(session.Events) != PlaySessionStatus.Ended)
            ?? throw new InvalidOperationException("没有可以继续操作的游玩记录。请先开始游戏。");
        var status = PlaySessionRules.GetStatus(activeSession.Events);
        var allowed = (status, eventType) switch
        {
            (PlaySessionStatus.Playing, PlayEventType.Pause or PlayEventType.End) => true,
            (PlaySessionStatus.Paused, PlayEventType.Resume or PlayEventType.End) => true,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException("当前状态不能执行这个操作。请刷新后重试。");
        }

        var candidate = activeSession.Events
            .Append(new PlayEvent(0, activeSession.Id, eventType, timestampUtcSeconds))
            .ToArray();
        PlaySessionRules.Validate(candidate);

        await using var connection = await OpenConnectionAsync();
        await InsertEventAsync(
            connection,
            transaction: null,
            activeSession.Id,
            eventType,
            timestampUtcSeconds);
    }

    public async Task UpdateEventTimestampAsync(long gameId, long eventId, long timestampUtcSeconds)
    {
        var sessions = await GetSessionsAsync(gameId);
        var session = sessions.SingleOrDefault(item => item.Events.Any(itemEvent => itemEvent.Id == eventId))
            ?? throw new InvalidOperationException("找不到这条游玩记录，请刷新后重试。");
        var updatedEvents = session.Events
            .Select(item => item.Id == eventId
                ? item with { TimestampUtcSeconds = timestampUtcSeconds }
                : item)
            .ToArray();
        PlaySessionRules.Validate(updatedEvents);

        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE play_events
            SET timestamp_utc_seconds = $timestamp
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$timestamp", timestampUtcSeconds);
        command.Parameters.AddWithValue("$id", eventId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task InsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long sessionId,
        PlayEventType eventType,
        long timestampUtcSeconds)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO play_events (session_id, event_type, timestamp_utc_seconds)
            VALUES ($sessionId, $eventType, $timestamp);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$eventType", (int)eventType);
        command.Parameters.AddWithValue("$timestamp", timestampUtcSeconds);
        await command.ExecuteNonQueryAsync();
    }
}
