using Hisa.Core.Models;
using Hisa.Logs.LocalChatLogs;

namespace Hisa.Logs.Tests;

public sealed class LocalChatLogTrackerTests
{
    [Fact]
    public async Task StartAsync_PublishesLatestKnownSystem_ForInitialSession()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "Local_20260606_033531_2114344545.txt");
            await File.WriteAllTextAsync(path, BuildLog(
                "Praefectus Manufactorum XV",
                "2026.06.06 03:35:31",
                ("2026.06.06 03:35:34", "GPLB-C"),
                ("2026.06.06 03:48:20", "U104-3"),
                ("2026.06.06 03:57:42", "GPLB-C")));

            using var tracker = new LocalChatLogTracker();
            await tracker.StartAsync(directory.FullName, TimeSpan.FromHours(24));

            try
            {
                var change = await WaitForSnapshotAsync(tracker, 2114344545, TimeSpan.FromSeconds(5));
                Assert.Equal("Praefectus Manufactorum XV", change.CharacterName);
                Assert.Equal("GPLB-C", change.SolarSystemName);
                Assert.Equal(new DateTime(2026, 06, 06, 03, 57, 42, DateTimeKind.Utc), change.TimestampUtc);
                Assert.Equal(path, change.SourceFilePath);
            }
            finally
            {
                await tracker.StopAsync();
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Tracker_SwitchesToNewerSessionFile_ForSameCharacterImmediately()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var oldPath = Path.Combine(directory.FullName, "Local_20260606_033531_2114344545.txt");
            await File.WriteAllTextAsync(oldPath, BuildLog(
                "Praefectus Manufactorum XV",
                "2026.06.06 03:35:31",
                ("2026.06.06 03:35:34", "GPLB-C"),
                ("2026.06.06 03:48:20", "U104-3")));

            using var tracker = new LocalChatLogTracker();
            await tracker.StartAsync(directory.FullName, TimeSpan.FromHours(24));

            try
            {
                var initial = await WaitForSnapshotAsync(tracker, 2114344545, TimeSpan.FromSeconds(5));
                Assert.Equal("U104-3", initial.SolarSystemName);
                Assert.Equal(oldPath, initial.SourceFilePath);

                var newPath = Path.Combine(directory.FullName, "Local_20260606_051000_2114344545.txt");
                await File.WriteAllTextAsync(newPath, BuildLog(
                    "Praefectus Manufactorum XV",
                    "2026.06.06 05:10:00",
                    ("2026.06.06 05:10:06", "R959-U"),
                    ("2026.06.06 05:50:24", "J7-BDX")));

                var updated = await WaitForConditionAsync(
                    () => tracker.Snapshot.TryGetValue(2114344545, out var change) &&
                          change.SourceFilePath == newPath &&
                          change.SolarSystemName == "J7-BDX"
                        ? change
                        : null,
                    TimeSpan.FromSeconds(5));

                Assert.Equal(new DateTime(2026, 06, 06, 05, 50, 24, DateTimeKind.Utc), updated.TimestampUtc);
                Assert.Equal("Praefectus Manufactorum XV", updated.CharacterName);
            }
            finally
            {
                await tracker.StopAsync();
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static async Task<LocalCharacterSystemChange> WaitForSnapshotAsync(
        LocalChatLogTracker tracker,
        int characterId,
        TimeSpan timeout)
    {
        return await WaitForConditionAsync(
            () => tracker.Snapshot.TryGetValue(characterId, out var change) ? change : null,
            timeout);
    }

    private static async Task<T> WaitForConditionAsync<T>(Func<T?> probe, TimeSpan timeout)
        where T : class
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (probe() is { } result)
            {
                return result;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for tracker state.");
    }

    private static string BuildLog(string listener, string sessionStarted, params (string Timestamp, string System)[] changes)
    {
        var lines = new List<string>
        {
            "\uFEFF------------------------------------------------------------",
            $"  Listener:        {listener}",
            $"  Session started: {sessionStarted}",
            "------------------------------------------------------------"
        };

        lines.AddRange(changes.Select(change =>
            $"[ {change.Timestamp} ] EVE System > Channel changed to Local : {change.System}"));

        return string.Join("\r\n", lines);
    }
}
