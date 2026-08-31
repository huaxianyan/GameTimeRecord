using System.IO;
using GameTimeRecord.App.Data;
using GameTimeRecord.Core;

namespace GameTimeRecord.Tests;

public sealed class GameRecordingTests
{
    [Fact]
    public async Task StartingPausingResumingAndEndingPersistsTheCompleteRecord()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"game-time-record-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteGameRepository(databasePath);
            await repository.InitializeAsync();
            var gameId = await repository.AddGameAsync(
                "测试游戏",
                "",
                "Windows",
                "",
                50);

            await repository.StartSessionAsync(gameId, 100);
            await repository.AddEventAsync(gameId, PlayEventType.Pause, 130);
            await repository.AddEventAsync(gameId, PlayEventType.Resume, 160);
            await repository.AddEventAsync(gameId, PlayEventType.End, 220);

            var sessions = await repository.GetSessionsAsync(gameId);
            var statistics = GameStatisticsCalculator.Calculate(sessions, 300);

            var session = Assert.Single(sessions);
            Assert.Equal(
                new[]
                {
                    PlayEventType.Start,
                    PlayEventType.Pause,
                    PlayEventType.Resume,
                    PlayEventType.End,
                },
                session.Events.Select(playEvent => playEvent.Type));
            Assert.Equal(90, statistics.TotalSeconds);
            Assert.Equal(1, statistics.PlayCount);
            Assert.Equal(100, statistics.FirstPlayedAtUtcSeconds);
            Assert.Equal(220, statistics.LastPlayedAtUtcSeconds);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
