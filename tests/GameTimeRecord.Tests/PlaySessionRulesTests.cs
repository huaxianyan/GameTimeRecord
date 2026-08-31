using GameTimeRecord.Core;

namespace GameTimeRecord.Tests;

public sealed class PlaySessionRulesTests
{
    [Fact]
    public void PausingAndResumingCountsOnlyActiveIntervals()
    {
        var events = new[]
        {
            Event(1, PlayEventType.Start, 100),
            Event(2, PlayEventType.Pause, 130),
            Event(3, PlayEventType.Resume, 160),
            Event(4, PlayEventType.Pause, 220),
            Event(5, PlayEventType.End, 300),
        };

        var duration = PlaySessionRules.CalculateDurationSeconds(events, 500);

        Assert.Equal(90, duration);
    }

    [Fact]
    public void StatisticsUseFirstStartAndLastEnd()
    {
        var sessions = new[]
        {
            new PlaySession(1, 1, new[]
            {
                Event(1, PlayEventType.Start, 100, 1),
                Event(2, PlayEventType.End, 130, 1),
            }),
            new PlaySession(2, 1, new[]
            {
                Event(3, PlayEventType.Start, 200, 2),
                Event(4, PlayEventType.End, 260, 2),
            }),
        };

        var statistics = GameStatisticsCalculator.Calculate(sessions, 500);

        Assert.Equal(90, statistics.TotalSeconds);
        Assert.Equal(2, statistics.PlayCount);
        Assert.Equal(100, statistics.FirstPlayedAtUtcSeconds);
        Assert.Equal(260, statistics.LastPlayedAtUtcSeconds);
    }

    [Fact]
    public void EditingAnEventCannotReverseTheTimeline()
    {
        var events = new[]
        {
            Event(1, PlayEventType.Start, 200),
            Event(2, PlayEventType.End, 100),
        };

        Assert.Throws<InvalidOperationException>(() => PlaySessionRules.Validate(events));
    }

    private static PlayEvent Event(
        long id,
        PlayEventType type,
        long timestamp,
        long sessionId = 1) =>
        new(id, sessionId, type, timestamp);
}
