namespace GameTimeRecord.Core;

public static class GameStatisticsCalculator
{
    public static GameStatistics Calculate(
        IReadOnlyList<PlaySession> sessions,
        long nowUtcSeconds)
    {
        if (sessions.Count == 0)
        {
            return new GameStatistics(0, 0, null, null);
        }

        long totalSeconds = 0;
        long? firstPlayedAt = null;
        long? lastPlayedAt = null;

        foreach (var session in sessions)
        {
            PlaySessionRules.Validate(session.Events);
            totalSeconds += PlaySessionRules.CalculateDurationSeconds(
                session.Events,
                nowUtcSeconds);

            var startedAt = session.Events[0].TimestampUtcSeconds;
            firstPlayedAt = firstPlayedAt.HasValue
                ? Math.Min(firstPlayedAt.Value, startedAt)
                : startedAt;

            var endedAt = session.Events
                .Where(playEvent => playEvent.Type == PlayEventType.End)
                .Select(playEvent => (long?)playEvent.TimestampUtcSeconds)
                .SingleOrDefault();
            if (endedAt.HasValue)
            {
                lastPlayedAt = lastPlayedAt.HasValue
                    ? Math.Max(lastPlayedAt.Value, endedAt.Value)
                    : endedAt;
            }
        }

        return new GameStatistics(
            totalSeconds,
            sessions.Count,
            firstPlayedAt,
            lastPlayedAt);
    }
}
