using System.Text;
using Hisa.Core.Abstractions;
using Hisa.Services.Background;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public class FleetBoostTrackerTests
{
    [Theory]
    [InlineData("utf8")] [InlineData("utf16")] [InlineData("utf16be")]
    public async Task StartAndResetIgnoreHistoryAndPreexistingPartialLines(string encodingName)
    {
        using var f = new Fixture();
        var encoding = encodingName switch { "utf16" => Encoding.Unicode, "utf16be" => Encoding.BigEndianUnicode, _ => new UTF8Encoding(true) };
        var path = f.PathFor("Fleet");
        var now = DateTime.UtcNow;
        await File.WriteAllTextAsync(path, "Channel Name: Fleet\n" + Line(now, "Old", "Shield Extension Charge") +
            Line(now, "Partial", "Shield Extension Charge").TrimEnd('\n'), encoding);
        await f.Start();
        var start = f.Service.GetSnapshot("Fleet").StartedUtc!.Value;
        await File.AppendAllTextAsync(path, "\n" + Line(start, "Fresh", "Shield Extension Charge +ML"), encoding);
        await f.Service.ScanOnceAsync();
        Assert.Equal("Fresh", Assert.Single(f.Service.GetSnapshot("Fleet").Pilots).Name);
        Assert.True(f.Service.GetSnapshot("Fleet").Pilots[0].Mindlink);
        await f.Service.StartOrResetAsync("Fleet");
        await f.Service.ScanOnceAsync();
        Assert.Empty(f.Service.GetSnapshot("Fleet").Pilots);
        start = f.Service.GetSnapshot("Fleet").StartedUtc!.Value;
        await File.AppendAllTextAsync(path, Line(start, "After reset", "Rapid Deployment Charge"), encoding);
        await f.Service.ScanOnceAsync();
        Assert.Equal("After reset", Assert.Single(f.Service.GetSnapshot("Fleet").Pilots).Name);
    }

    [Fact]
    public async Task IncompleteWritesWaitForNewlineAndDuplicateClientsDoNotInflateCounts()
    {
        using var f = new Fixture();
        var path = f.PathFor("Fleet");
        await File.WriteAllTextAsync(path, "Channel Name: Fleet\n");
        await f.Start();
        var start = f.Service.GetSnapshot("Fleet").StartedUtc!.Value;
        var line = Line(start, "Pilot", "Shield Extension Charge");
        await File.AppendAllTextAsync(path, line.TrimEnd('\n'));
        await f.Service.ScanOnceAsync();
        Assert.Empty(f.Service.GetSnapshot("Fleet").Pilots);
        await File.AppendAllTextAsync(path, "\n");
        await File.WriteAllTextAsync(f.PathFor("Fleet", 2), "Channel Name: Fleet\n" + line);
        await f.Service.ScanOnceAsync();
        Assert.Single(f.Service.GetSnapshot("Fleet").Pilots);
        await f.Service.RemovePilotAsync("Fleet", "Pilot");
        await f.Service.ScanOnceAsync();
        Assert.Empty(f.Service.GetSnapshot("Fleet").Pilots);
        await File.AppendAllTextAsync(path, Line(start.AddSeconds(1), "Pilot", "Rapid Deployment Charge"));
        await f.Service.ScanOnceAsync();
        Assert.Equal("rapid-deployment", Assert.Single(Assert.Single(f.Service.GetSnapshot("Fleet").Pilots).ChargeIds));
    }

    [Fact]
    public async Task ChannelsRemainIndependentAndRotationCannotRestoreOldPilotState()
    {
        using var f = new Fixture();
        await f.Start();
        await f.Service.StartOrResetAsync("Other");
        var start = f.Service.GetSnapshot("Fleet").StartedUtc!.Value;
        var path = f.PathFor("Fleet");
        await File.WriteAllTextAsync(path, Line(start.AddSeconds(2), "Pilot", "Shield Extension Charge"));
        await File.WriteAllTextAsync(f.PathFor("Other"), Line(start.AddSeconds(2), "Other pilot", "Rapid Repair Charge"));
        await f.Service.ScanOnceAsync();
        Assert.Single(f.Service.GetSnapshot("Fleet").Pilots);
        Assert.Single(f.Service.GetSnapshot("Other").Pilots);
        // Truncate/rewrite with old content and a genuinely newer report.
        await File.WriteAllTextAsync(path, Line(start.AddMinutes(-1), "History", "Shield Extension Charge") +
            Line(start.AddSeconds(1), "Pilot", "Rapid Repair Charge") + Line(start.AddSeconds(3), "New", "Rapid Deployment Charge"));
        await f.Service.ScanOnceAsync();
        var pilots = f.Service.GetSnapshot("Fleet").Pilots;
        Assert.Equal(2, pilots.Count);
        Assert.Equal("shield-extension", Assert.Single(pilots.Single(p => p.Name == "Pilot").ChargeIds));
        await f.Service.StartOrResetAsync("Fleet");
        Assert.Empty(f.Service.GetSnapshot("Fleet").Pilots);
        Assert.Single(f.Service.GetSnapshot("Other").Pilots);
    }

    [Fact]
    public async Task MissingLogsRecoverAndSettingsNormalizeChannels()
    {
        using var f = new Fixture();
        f.Settings.Values[FleetBoostTrackerService.ChannelsKey] = new[] { " Fleet ", "fleet", "", "Other" };
        await f.Start();
        Assert.Equal(new[] { "Fleet", "Other" }, f.Service.Channels);
        await f.Service.ScanOnceAsync();
        Assert.Contains("Waiting", f.Service.GetSnapshot("Fleet").Status);
        var start = f.Service.GetSnapshot("Fleet").StartedUtc!.Value;
        await File.WriteAllTextAsync(f.PathFor("Fleet"), Line(start, "Pilot", "Shield Extension Charge"));
        await f.Service.ScanOnceAsync();
        Assert.Equal("Listening", f.Service.GetSnapshot("Fleet").Status);
        File.Delete(f.PathFor("Fleet"));
        await f.Service.ScanOnceAsync();
        Assert.Single(f.Service.GetSnapshot("Fleet").Pilots);
        f.Settings.Values[FleetBoostTrackerService.ChannelsKey] = new[] { "Other" };
        await f.Service.ApplySettingsAsync();
        Assert.Equal("Not configured", f.Service.GetSnapshot("Fleet").Status);
    }

    [Fact]
    public async Task NewlyDiscoveredClientCannotReplayABoundarySecondCopy()
    {
        using var f = new Fixture();
        var time = DateTime.UtcNow;
        var line = Line(time, "Old pilot", "Shield Extension Charge");
        await File.WriteAllTextAsync(f.PathFor("Fleet"), line);
        await f.Start();
        var start = f.Service.GetSnapshot("Fleet").StartedUtc!.Value;
        await File.WriteAllTextAsync(f.PathFor("Fleet", 2), line + Line(start, "New pilot", "Rapid Deployment Charge"));
        await f.Service.ScanOnceAsync();
        Assert.Equal("New pilot", Assert.Single(f.Service.GetSnapshot("Fleet").Pilots).Name);
    }

    [Fact]
    public async Task SameLengthRewriteAndDelayedWriteTimestampGrowthAreDetected()
    {
        using var f = new Fixture();
        await f.Start();
        var start = f.Service.GetSnapshot("Fleet").StartedUtc!.Value;
        var path = f.PathFor("Fleet");
        var first = Line(start, "Pilot One", "Shield Extension Charge");
        await File.WriteAllTextAsync(path, first);
        await f.Service.ScanOnceAsync();
        var written = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, first.Replace("Pilot One", "Pilot Two"));
        File.SetLastWriteTimeUtc(path, written.AddSeconds(1));
        await f.Service.ScanOnceAsync();
        Assert.Equal(2, f.Service.GetSnapshot("Fleet").Pilots.Count);
        written = File.GetLastWriteTimeUtc(path);
        await File.AppendAllTextAsync(path, Line(start.AddSeconds(1), "Pilot Three", "Rapid Deployment Charge"));
        File.SetLastWriteTimeUtc(path, written);
        await f.Service.ScanOnceAsync();
        Assert.Equal(3, f.Service.GetSnapshot("Fleet").Pilots.Count);
    }

    private static string Line(DateTime time, string pilot, string text) => $"[ {time:yyyy.MM.dd HH:mm:ss} ] {pilot} > {text}\n";
    private sealed class Fixture : IDisposable
    {
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "hisa-boost-" + Guid.NewGuid().ToString("N"));
        public readonly MemorySettings Settings = new();
        public readonly FleetBoostTrackerService Service;
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "ChatLogs"));
            Settings.Values["Tracking.LogsRootPath"] = DirectoryPath;
            Settings.Values[FleetBoostTrackerService.ChannelsKey] = new[] { "Fleet", "Other" };
            Service = new(Settings, NullLogger<FleetBoostTrackerService>.Instance);
        }
        public string PathFor(string channel, int account = 1) => Path.Combine(DirectoryPath, "ChatLogs", $"{channel}_20261021_233000_{account}.txt");
        public async Task Start() { await Service.ApplySettingsAsync(); await Service.StartOrResetAsync("Fleet"); }
        public void Dispose() { Service.Dispose(); Directory.Delete(DirectoryPath, true); }
    }
    private sealed class MemorySettings : ISettingsService
    {
        public readonly Dictionary<string, object> Values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(Values.TryGetValue(key, out var value) ? (T?)value : default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) { Values[key] = value!; return Task.CompletedTask; }
    }
}
