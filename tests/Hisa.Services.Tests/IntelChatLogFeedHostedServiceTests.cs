using System.Reflection;
using System.Net;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public class IntelChatLogFeedHostedServiceTests
{
    [Fact]
    public async Task ResolveChatLogsDirectoryAsync_WhenConfiguredRootContainsChatlogsDirectory_ReturnsActualDirectoryPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hisa-tests", Guid.NewGuid().ToString("N"));
        var logsRoot = Path.Combine(tempRoot, "logs");
        var chatlogsPath = Path.Combine(logsRoot, "Chatlogs");
        Directory.CreateDirectory(chatlogsPath);

        try
        {
            var service = new IntelChatLogFeedHostedService(
                new NoopSettingsService(new Dictionary<string, object?>
                {
                    ["Tracking.LogsRootPath"] = logsRoot
                }),
                new NoopSdeDatabase(),
                new NoopHttpClientFactory(),
                NullLogger<IntelChatLogFeedHostedService>.Instance);

            var method = typeof(IntelChatLogFeedHostedService).GetMethod(
                "ResolveChatLogsDirectoryAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = (Task<string?>)method!.Invoke(service, [CancellationToken.None])!;
            var resolved = await task;

            Assert.Equal(chatlogsPath, resolved);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

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
    public async Task ApplySettingsAsync_ReloadsEnabledChannelsAndExpiry()
    {
        var settings = new NoopSettingsService(new Dictionary<string, object?>
        {
            ["Intel.Enabled"] = true,
            ["Intel.Channels.Include"] = new List<string> { "alpha" },
            ["Intel.SystemExpiryMinutes"] = 15
        });
        var service = new IntelChatLogFeedHostedService(
            settings,
            new NoopSdeDatabase(),
            new NoopHttpClientFactory(),
            NullLogger<IntelChatLogFeedHostedService>.Instance);

        await service.ApplySettingsAsync();

        settings.Values["Intel.Enabled"] = false;
        settings.Values["Intel.Channels.Include"] = new List<string> { "beta" };
        settings.Values["Intel.SystemExpiryMinutes"] = 3;

        await service.ApplySettingsAsync();

        var shouldReadChannel = typeof(IntelChatLogFeedHostedService).GetMethod(
            "ShouldReadChannel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(shouldReadChannel);
        Assert.False((bool)shouldReadChannel!.Invoke(service, ["alpha"])!);
        Assert.True((bool)shouldReadChannel.Invoke(service, ["beta"])!);

        var enabledField = typeof(IntelChatLogFeedHostedService).GetField(
            "_enabled",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(enabledField);
        Assert.False((bool)enabledField!.GetValue(service)!);

        var expiryField = typeof(IntelChatLogFeedHostedService).GetField(
            "_systemExpiry",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(expiryField);
        Assert.Equal(TimeSpan.FromMinutes(3), (TimeSpan)expiryField!.GetValue(service)!);
    }

    [Fact]
    public async Task ApplySettingsAsync_PrunesSnapshotsFromExcludedChannels()
    {
        var settings = new NoopSettingsService(new Dictionary<string, object?>
        {
            ["Intel.Enabled"] = true,
            ["Intel.Channels.Include"] = new List<string> { "alpha" },
            ["Intel.SystemExpiryMinutes"] = 15
        });
        var service = CreateServiceWithSystems(settings);

        ApplyReport(service, CreateReport("Old System", DateTime.UtcNow, "Reporter", "hostile", "alpha", "Pilot One"));
        Assert.True(service.Snapshot.ContainsKey(1));

        settings.Values["Intel.Channels.Include"] = new List<string> { "beta" };
        await service.ApplySettingsAsync();

        Assert.False(service.Snapshot.ContainsKey(1));
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

    [Fact]
    public void TryRegisterReport_WhenTimestampReporterAndMessageMatch_RejectsDuplicate()
    {
        var service = CreateServiceWithSystems();
        var timestampUtc = DateTime.UtcNow;
        var first = CreateReport("Old System", timestampUtc, "Reporter", "N3-JBX* 10+");
        var duplicate = CreateReport("New System", timestampUtc, "Reporter", "N3-JBX* 10+");

        Assert.True(TryRegisterReport(service, first));
        Assert.False(TryRegisterReport(service, duplicate));
    }

    [Fact]
    public void TryRegisterReport_WhenReporterDiffers_AllowsSecondReport()
    {
        var service = CreateServiceWithSystems();
        var timestampUtc = DateTime.UtcNow;
        var first = CreateReport("Old System", timestampUtc, "Reporter One", "N3-JBX* 10+");
        var second = CreateReport("Old System", timestampUtc, "Reporter Two", "N3-JBX* 10+");

        Assert.True(TryRegisterReport(service, first));
        Assert.True(TryRegisterReport(service, second));
    }

    [Fact]
    public void TryParseExternalIntelLinkData_DscanInfoHtml_ExtractsShipsClassesAndSystem()
    {
        const string html = """
            <div class="panel-heading lead headline">System: <b><a href="#">5C-RPA</a></b></div>
            <ul class="list-group" id="ships">
                <li class="list-group-item shipclass26" data-sclid="26"><span class="badge label label-default">2</span><b>Augoror</b></li>
                <li class="list-group-item shipclass25" data-sclid="25"><span class="badge label label-default">1</span><b>Astero</b></li>
            </ul>
            """;

        var parsed = ParseExternalIntelLinkData("https://dscan.info/v/example", html);
        var systems = GetStringSet(parsed, "Systems");
        var shipNames = GetStringList(parsed, "ShipNames");
        var shipClasses = GetShipClassList(parsed, "ShipClasses");

        Assert.Contains("5C-RPA", systems, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3, shipNames.Count);
        Assert.Equal(2, shipNames.Count(x => x == "Augoror"));
        Assert.Contains(IntelShipClass.Cruiser, shipClasses);
        Assert.Contains(IntelShipClass.Frigate, shipClasses);
    }

    [Fact]
    public void TryParseExternalIntelLinkData_AdashboardHtml_ExtractsShipsAndClasses()
    {
        const string html = """
            <table class="table table-condensed">
              <tr data-race="2" style="background: #c7e8c7;">
                <td style="vertical-align: middle;" title="Interceptor"><span data-typeID="11198">&nbsp;</span>&nbsp;Stiletto</td>
                <td style="text-align: right; width: 10%;"><span>2</span></td>
              </tr>
              <tr data-race="2" style="background: #ededed;">
                <td style="vertical-align: middle;" title="Cruiser"><span data-typeID="17720">&nbsp;</span>&nbsp;Cynabal</td>
                <td style="text-align: right; width: 10%;"><span>1</span></td>
              </tr>
            </table>
            """;

        var parsed = ParseExternalIntelLinkData("https://adashboard.info/intel/dscan/view/example", html);
        var shipNames = GetStringList(parsed, "ShipNames");
        var shipClasses = GetShipClassList(parsed, "ShipClasses");

        Assert.Equal(3, shipNames.Count);
        Assert.Equal(2, shipNames.Count(x => x == "Stiletto"));
        Assert.Contains(IntelShipClass.Frigate, shipClasses);
        Assert.Contains(IntelShipClass.Cruiser, shipClasses);
    }

    [Fact]
    public async Task ResolveReportedCharacterNamesAsync_UsesOnlyExactEsiCharacterMatches()
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            new StubHttpClientFactory("""
                {"characters":[{"id":123,"name":"Askulen Akasa Soikutsu"},{"id":124,"name":"Askulen"},{"id":125,"name":"Akasa"},{"id":126,"name":"Soikutsu"},{"id":456,"name":"0314227"}]}
                """),
            NullLogger<IntelChatLogFeedHostedService>.Instance);
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "ResolveReportedCharacterNamesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<IReadOnlyList<string>>)method!.Invoke(service,
            [new List<string> { "Askulen Akasa Soikutsu", "Askulen", "Akasa", "Soikutsu", "C1-C3", "0314227", "not a pilot" }, CancellationToken.None])!;
        var names = await task;

        Assert.Equal(["Askulen Akasa Soikutsu", "0314227"], names);
    }

    private static IntelChatLogFeedHostedService CreateServiceWithSystems(NoopSettingsService? settings = null)
    {
        var service = new IntelChatLogFeedHostedService(
            settings ?? new NoopSettingsService(),
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

    private static IntelChatLogFeedHostedService CreateServiceWithSystemsAndShips()
    {
        var service = CreateServiceWithSystems();
        var shipField = typeof(IntelChatLogFeedHostedService).GetField(
            "_shipClassByName",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(shipField);
        shipField!.SetValue(service, new Dictionary<string, IntelShipClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["Augoror"] = IntelShipClass.Cruiser,
            ["Astero"] = IntelShipClass.Frigate,
            ["Stiletto"] = IntelShipClass.Frigate,
            ["Cynabal"] = IntelShipClass.Cruiser
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

    private static bool TryRegisterReport(IntelChatLogFeedHostedService service, IntelChatReport report)
    {
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "TryRegisterReport",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(service, [report])!;
    }

    private static object ParseExternalIntelLinkData(string url, string html)
    {
        var service = CreateServiceWithSystemsAndShips();
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "TryParseExternalIntelLinkData",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        object?[] args = [url, html, null];
        var success = (bool)method!.Invoke(service, args)!;
        Assert.True(success);
        Assert.NotNull(args[2]);
        return args[2]!;
    }

    private static HashSet<string> GetStringSet(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.IsAssignableFrom<HashSet<string>>(value);
        return (HashSet<string>)value!;
    }

    private static List<string> GetStringList(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.IsAssignableFrom<List<string>>(value);
        return (List<string>)value!;
    }

    private static List<IntelShipClass> GetShipClassList(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        Assert.IsAssignableFrom<List<IntelShipClass>>(value);
        return (List<IntelShipClass>)value!;
    }

    private static IntelChatReport CreateReport(string systemName, params string[] hostileNames)
    {
        return CreateReport(systemName, DateTime.UtcNow, "Reporter", string.Join(", ", hostileNames), hostileNames);
    }

    private static IntelChatReport CreateReport(string systemName, DateTime timestampUtc, string reporterName, string messageText, params string[] hostileNames)
    {
        return CreateReport(systemName, timestampUtc, reporterName, messageText, "Intel", hostileNames);
    }

    private static IntelChatReport CreateReport(string systemName, DateTime timestampUtc, string reporterName, string messageText, string channelName, params string[] hostileNames)
    {
        return new IntelChatReport
        {
            DedupeKey = $"intel:{timestampUtc:O}:{reporterName.Trim().ToUpperInvariant()}:{messageText.Trim().ToUpperInvariant()}",
            TimestampUtc = timestampUtc,
            ChannelName = channelName,
            ReporterName = reporterName,
            MessageText = messageText,
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
        public Dictionary<string, object?> Values { get; }

        public NoopSettingsService()
            : this(new Dictionary<string, object?>())
        {
        }

        public NoopSettingsService(IReadOnlyDictionary<string, object?> values)
        {
            Values = new Dictionary<string, object?>(values);
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (Values.TryGetValue(key, out var value) && value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            Values[key] = value;
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

    private sealed class StubHttpClientFactory(string responseBody) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler(responseBody));
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://esi.evetech.net/latest/universe/ids/?datasource=tranquility", request.RequestUri?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });
        }
    }
}
