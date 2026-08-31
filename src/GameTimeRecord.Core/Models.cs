namespace GameTimeRecord.Core;

public sealed record Game(
    long Id,
    string Name,
    string Alias,
    string Platform,
    string Notes,
    long CreatedAtUtcSeconds);

public enum PlayEventType
{
    Start = 0,
    Pause = 1,
    Resume = 2,
    End = 3,
}

public sealed record PlayEvent(
    long Id,
    long SessionId,
    PlayEventType Type,
    long TimestampUtcSeconds);

public sealed record PlaySession(
    long Id,
    long GameId,
    IReadOnlyList<PlayEvent> Events);

public enum PlaySessionStatus
{
    Playing,
    Paused,
    Ended,
}

public sealed record GameStatistics(
    long TotalSeconds,
    int PlayCount,
    long? FirstPlayedAtUtcSeconds,
    long? LastPlayedAtUtcSeconds);
