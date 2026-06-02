using System.Reflection;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public class IntelChatLogFeedHostedServiceTests
{
    [Theory]
    [InlineData("Kill: Rave Maulerant (Thorax)", true)]
    [InlineData("kill: Some Pilot (Drake Navy Issue)", true)]
    [InlineData("Kill: Rave Maulerant Thorax", false)]
    [InlineData("3 in local", false)]
    public void IsIgnoredIntelMessage_MatchesInGameKillmailFormat(string message, bool expectedIgnored)
    {
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "IsIgnoredIntelMessage",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = (bool)method!.Invoke(null, [message])!;
        Assert.Equal(expectedIgnored, result);
    }

    [Theory]
    [InlineData("Branch.Intel_20250601_153012_95465499.txt", true, "Branch.Intel", "2025-06-01T15:30:12Z")]
    [InlineData("Delve_20240115_080000_90000001.txt", true, "Delve", "2024-01-15T08:00:00Z")]
    [InlineData("Local_20250601_153012_95465499.txt", true, "Local", "2025-06-01T15:30:12Z")]
    [InlineData("notanintelfile.txt", false, null, null)]
    [InlineData("Channel_2025_153012_95465499.txt", false, null, null)]
    public void TryParseIntelFileName_ExtractsChannelAndSessionTimestamp(
        string fileName, bool expectedSuccess, string? expectedChannel, string? expectedSessionUtc)
    {
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "TryParseIntelFileName",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object?[] args = [fileName, null, null];
        var success = (bool)method!.Invoke(null, args)!;

        Assert.Equal(expectedSuccess, success);
        if (!expectedSuccess)
        {
            return;
        }

        Assert.Equal(expectedChannel, (string?)args[1]);
        var sessionStartedUtc = (DateTime)args[2]!;
        Assert.Equal(DateTimeKind.Utc, sessionStartedUtc.Kind);
        Assert.Equal(DateTimeOffset.Parse(expectedSessionUtc!).UtcDateTime, sessionStartedUtc);
    }

    [Fact]
    public void ShouldReadChannel_WhenIncludeListIsEmpty_ReturnsTrue()
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            new NoopHttpClientFactory(),
            NullLogger<IntelChatLogFeedHostedService>.Instance);

        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "ShouldReadChannel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = (bool)method!.Invoke(service, ["Any Intel Channel"])!;
        Assert.True(result);
    }

    [Fact]
    public void ApplyToSystemSnapshot_WhenNamedHostileMoves_RemovesOldSystemSnapshot()
    {
        var service = CreateServiceWithSystems();

        ApplyReport(service, CreateReport("Old System", "Pilot One"));
        ApplyReport(service, CreateReport("New System", "Pilot One"));

        Assert.False(service.Snapshot.ContainsKey(1));
        Assert.Equal(["Pilot One"], service.Snapshot[2].HostilePilotNames);
    }

    [Fact]
    public void ApplyToSystemSnapshot_WhenOneOfSeveralNamedHostilesMoves_KeepsUnmovedHostiles()
    {
        var service = CreateServiceWithSystems();

        ApplyReport(service, CreateReport("Old System", "Pilot One", "Pilot Two"));
        ApplyReport(service, CreateReport("New System", "Pilot One"));

        Assert.Equal(["Pilot Two"], service.Snapshot[1].HostilePilotNames);
        Assert.Equal(1, service.Snapshot[1].HostileScore);
        Assert.Equal(["Pilot One"], service.Snapshot[2].HostilePilotNames);
    }

    private static IntelChatLogFeedHostedService CreateServiceWithSystems()
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            new NoopHttpClientFactory(),
            NullLogger<IntelChatLogFeedHostedService>.Instance);
        var systemsField = typeof(IntelChatLogFeedHostedService).GetField(
            "_systemIdByName",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(systemsField);
        systemsField!.SetValue(service, new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["Old System"] = 1,
            ["New System"] = 2
        });
        return service;
    }

    private static void ApplyReport(IntelChatLogFeedHostedService service, IntelChatReport report)
    {
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "ApplyToSystemSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(service, [report]);
    }

    private static IntelChatReport CreateReport(string systemName, params string[] hostileNames)
    {
        return new IntelChatReport
        {
            TimestampUtc = DateTime.UtcNow,
            ChannelName = "Intel",
            ReporterName = "Reporter",
            MessageText = string.Join(", ", hostileNames),
            SourceFilePath = "test://intel",
            Systems = [systemName],
            ShipClasses = [],
            ReportedShipNames = [],
            ReportedShipTypeIds = [],
            Alerts = [],
            ReportedHostileNames = hostileNames,
            ReportedHostileCount = hostileNames.Length
        };
    }

    private sealed class NoopSettingsService : ISettingsService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoopSdeDatabase : ISdeDatabase
    {
        public SqliteConnection CreateConnection()
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
