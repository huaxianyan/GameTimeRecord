namespace GameTimeRecord.Core;

public static class PlaySessionRules
{
    public static PlaySessionStatus GetStatus(IReadOnlyList<PlayEvent> events)
    {
        Validate(events);

        return events[^1].Type switch
        {
            PlayEventType.Start or PlayEventType.Resume => PlaySessionStatus.Playing,
            PlayEventType.Pause => PlaySessionStatus.Paused,
            PlayEventType.End => PlaySessionStatus.Ended,
            _ => throw new InvalidOperationException("未知的游玩记录类型。"),
        };
    }

    public static long CalculateDurationSeconds(
        IReadOnlyList<PlayEvent> events,
        long nowUtcSeconds)
    {
        Validate(events);

        long total = 0;
        long? activeSince = null;

        foreach (var playEvent in events)
        {
            switch (playEvent.Type)
            {
                case PlayEventType.Start:
                case PlayEventType.Resume:
                    activeSince = playEvent.TimestampUtcSeconds;
                    break;
                case PlayEventType.Pause:
                case PlayEventType.End:
                    if (activeSince.HasValue)
                    {
                        total += playEvent.TimestampUtcSeconds - activeSince.Value;
                        activeSince = null;
                    }

                    break;
            }
        }

        if (activeSince.HasValue)
        {
            total += Math.Max(0, nowUtcSeconds - activeSince.Value);
        }

        return total;
    }

    public static void Validate(IReadOnlyList<PlayEvent> events)
    {
        if (events.Count == 0 || events[0].Type != PlayEventType.Start)
        {
            throw new InvalidOperationException("一次游玩必须从开始记录起算。请检查记录后重试。");
        }

        var expected = PlaySessionStatus.Playing;
        for (var index = 1; index < events.Count; index++)
        {
            var previous = events[index - 1];
            var current = events[index];
            if (current.TimestampUtcSeconds < previous.TimestampUtcSeconds)
            {
                throw new InvalidOperationException("记录时间必须按先后顺序排列。请调整时间后重试。");
            }

            expected = (expected, current.Type) switch
            {
                (PlaySessionStatus.Playing, PlayEventType.Pause) => PlaySessionStatus.Paused,
                (PlaySessionStatus.Playing, PlayEventType.End) => PlaySessionStatus.Ended,
                (PlaySessionStatus.Paused, PlayEventType.Resume) => PlaySessionStatus.Playing,
                (PlaySessionStatus.Paused, PlayEventType.End) => PlaySessionStatus.Ended,
                _ => throw new InvalidOperationException("开始、暂停、恢复和结束的顺序不正确。请检查记录后重试。"),
            };

            if (expected == PlaySessionStatus.Ended && index != events.Count - 1)
            {
                throw new InvalidOperationException("结束之后不能再添加其他记录。请检查记录后重试。");
            }
        }
    }
}
