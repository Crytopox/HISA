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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChannelSourceDetectsGrowthWithDelayedLastWriteAndSurvivesSourceDeletion(bool watcherEvent)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-intel-growth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var now = DateTime.UtcNow;
        var active = Path.Combine(directory, $"Intel_{now.AddDays(-6):yyyyMMdd_HHmmss}_1.txt");
        var stopped = Path.Combine(directory, $"Intel_{now:yyyyMMdd_HHmmss}_2.txt");
        var oldWrite = now.AddDays(watcherEvent ? -3 : -1);
        try
        {
            await File.WriteAllTextAsync(active, $"Channel Name: Intel\n[ {now:yyyy.MM.dd HH:mm:ss} ] Scout > older client\n");
            await File.WriteAllTextAsync(stopped, $"Channel Name: Intel\n[ {now:yyyy.MM.dd HH:mm:ss} ] Scout > newer client\n");
            File.SetLastWriteTimeUtc(active, oldWrite);
            using var service = CreateServiceWithSystems();
            var messages = new List<IntelChatReport>();
            service.ChannelMessageReceived += (_, report) => messages.Add(report);
            Invoke("EnqueueAllKnownFiles", directory); await Flush();
            Assert.Equal(stopped, Assert.Single(messages).SourceFilePath);
            await File.AppendAllTextAsync(active, $"[ {now.AddSeconds(1):yyyy.MM.dd HH:mm:ss} ] Scout > active client still receiving\n");
            File.SetLastWriteTimeUtc(active, oldWrite); // Simulate an open writer with delayed metadata.
            if (watcherEvent)
                Invoke("OnWatcherChanged", service, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, Path.GetFileName(active)));
            else Invoke("EnqueueKnownActiveFiles");
            await Flush();
            Assert.Equal(active, Assert.Single(messages, message => message.MessageText == "active client still receiving").SourceFilePath);
            File.Delete(active);
            await File.AppendAllTextAsync(stopped, $"[ {now.AddSeconds(2):yyyy.MM.dd HH:mm:ss} ] Scout > remaining client\n");
            Invoke("EnqueueKnownActiveFiles"); await Flush();
            Assert.Equal(stopped, Assert.Single(messages, message => message.MessageText == "remaining client").SourceFilePath);

            void Invoke(string name, params object[] args) => typeof(IntelChatLogFeedHostedService).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, args);
            Task Flush() => (Task)typeof(IntelChatLogFeedHostedService).GetMethod("FlushDirtyFilesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [CancellationToken.None])!;
        }
        finally { File.Delete(active); File.Delete(stopped); Directory.Delete(directory); }
    }

    [Fact]
    public async Task ChannelSourceFollowsActualActivityAcrossAccountsAndPollsForMissedEvents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-intel-sources", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var now = DateTime.UtcNow;
        var olderSession = Path.Combine(directory, $"Intel_{now.AddDays(-6):yyyyMMdd_HHmmss}_1.txt");
        var newerSession = Path.Combine(directory, $"Intel_{now:yyyyMMdd_HHmmss}_2.txt");
        var otherChannel = Path.Combine(directory, $"Other_{now:yyyyMMdd_HHmmss}_1.txt");
        var initiallyMissing = Path.Combine(directory, $"Missing_{now:yyyyMMdd_HHmmss}_2.txt");
        try
        {
            await File.WriteAllTextAsync(olderSession, $"Channel Name: Intel\n{Line(now, "fresh from long running client")}");
            await File.WriteAllTextAsync(newerSession, $"Channel Name: Intel\n{Line(now.AddMinutes(-2), "stale newer session")}");
            await File.WriteAllTextAsync(otherChannel, $"Channel Name: Other\n{Line(now, "other channel")}");
            File.SetLastWriteTimeUtc(olderSession, now); File.SetLastWriteTimeUtc(newerSession, now.AddMinutes(-2));
            using var service = CreateServiceWithSystems();
            typeof(IntelChatLogFeedHostedService).GetField("_includeChannels", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, new HashSet<string>(["Intel", "Other", "Missing"], StringComparer.OrdinalIgnoreCase));
            var messages = new List<IntelChatReport>();
            service.ChannelMessageReceived += (_, report) => messages.Add(report);
            Invoke("EnqueueAllKnownFiles", directory);
            await Flush();
            Assert.Equal(2, messages.Count);
            Assert.DoesNotContain(messages, message => message.MessageText == "stale newer session");
            Assert.Equal(olderSession, messages.Single(message => message.ChannelName == "Intel").SourceFilePath);

            // The second client writes copies and a new report. Only its new content is delivered.
            await File.AppendAllTextAsync(newerSession, Line(now.AddSeconds(1), "fresh from long running client") + Line(now.AddSeconds(2), "new client report"));
            File.SetLastWriteTimeUtc(newerSession, now.AddSeconds(2));
            Invoke("OnWatcherChanged", service, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, Path.GetFileName(newerSession)));
            await Flush();
            Assert.Equal(4, messages.Count); // Catch up one previously unread historical line and the new report.
            Assert.Single(messages, message => message.MessageText == "fresh from long running client");
            Assert.Equal(newerSession, messages.Single(message => message.MessageText == "new client report").SourceFilePath);
            Assert.False(messages.Single(message => message.MessageText == "new client report").IsHistorical);

            // No watcher event: an older logged-in account takes over when the current client closes.
            await File.AppendAllTextAsync(olderSession, Line(now.AddSeconds(3), "new client report") + Line(now.AddSeconds(4), "failover report"));
            File.SetLastWriteTimeUtc(olderSession, now.AddSeconds(4));
            Invoke("EnqueueKnownActiveFiles");
            await Flush();
            Assert.Single(messages, message => message.MessageText == "new client report");
            Assert.Equal(olderSession, messages.Single(message => message.MessageText == "failover report").SourceFilePath);

            // A configured channel can appear later without blocking other channels or needing a restart.
            await File.WriteAllTextAsync(initiallyMissing, $"Channel Name: Missing\n{Line(now.AddSeconds(5), "joined later")}");
            Invoke("OnWatcherChanged", service, new FileSystemEventArgs(WatcherChangeTypes.Created, directory, Path.GetFileName(initiallyMissing)));
            await Flush();
            Assert.Equal("Missing", messages.Single(message => message.MessageText == "joined later").ChannelName);

            // All watcher events arriving together still select exactly one source per channel.
            await File.AppendAllTextAsync(newerSession, Line(now.AddSeconds(6), "simultaneous report"));
            await File.AppendAllTextAsync(olderSession, Line(now.AddSeconds(6), "simultaneous report"));
            File.SetLastWriteTimeUtc(newerSession, now.AddSeconds(7)); File.SetLastWriteTimeUtc(olderSession, now.AddSeconds(6));
            Invoke("OnWatcherChanged", service, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, Path.GetFileName(olderSession)));
            Invoke("OnWatcherChanged", service, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, Path.GetFileName(newerSession)));
            await Flush();
            Assert.Equal(newerSession, Assert.Single(messages, message => message.MessageText == "simultaneous report").SourceFilePath);

            void Invoke(string name, params object[] args) => typeof(IntelChatLogFeedHostedService).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, args);
            Task Flush() => (Task)typeof(IntelChatLogFeedHostedService).GetMethod("FlushDirtyFilesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [CancellationToken.None])!;
        }
        finally
        {
            File.Delete(olderSession); File.Delete(newerSession); File.Delete(otherChannel); File.Delete(initiallyMissing); Directory.Delete(directory);
        }
        static string Line(DateTime time, string text) => $"[ {time:yyyy.MM.dd HH:mm:ss} ] Scout > {text}\n";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialChannelLinesWaitForNewlineAndPreserveUnicodeOffsets(bool utf16)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-intel-lines", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Intel.txt");
        var encoding = utf16 ? System.Text.Encoding.Unicode : new System.Text.UTF8Encoding(false);
        try
        {
            var now = DateTime.UtcNow;
            await File.WriteAllTextAsync(path, $"Channel Name: Intel\r\n[ {now:yyyy.MM.dd HH:mm:ss} ] Scout > système Ω cle", encoding);
            using var service = CreateServiceWithSystems();
            var messages = new List<IntelChatReport>();
            service.ChannelMessageReceived += (_, report) => messages.Add(report);
            var process = typeof(IntelChatLogFeedHostedService).GetMethod("ProcessFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            Assert.Empty(messages);
            await File.AppendAllTextAsync(path, "ar\r\n", encoding);
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            Assert.Equal("système Ω clear", Assert.Single(messages).MessageText);
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            Assert.Single(messages);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public async Task ChannelHistoryIncludesPlainTextAndLinksButReportsStillRequireSystems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-intel-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Intel.txt");
        var duplicate = Path.Combine(directory, "Intel-copy.txt");
        var otherChannel = Path.Combine(directory, "Other.txt");
        try
        {
            var timestamp = DateTime.UtcNow.AddSeconds(-5);
            var content = $"Channel Name: Intel\n" +
                $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] Scout > status?\n" +
                $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] Scout > Kill: Rave Maulerant (Thorax)\n" +
                $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] Scout > E-JCUS clear\n" +
                $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] EVE System > Channel MOTD: E-JCUS\n" +
                $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] EVE System > Channel changed to Intel\n" +
                $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] Scout > https://example.com/report\n" +
                $"[ {timestamp.AddHours(-1):yyyy.MM.dd HH:mm:ss} ] Scout > too old\n";
            await File.WriteAllTextAsync(path, content);
            await File.WriteAllTextAsync(duplicate, content);
            using var service = CreateServiceWithSystems();
            var systems = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["E-JCUS"] = 1 };
            typeof(IntelChatLogFeedHostedService).GetField("_systemIdByName", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, systems);
            typeof(IntelChatLogFeedHostedService).GetField("_messageParser", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, new Hisa.Logs.IntelChatLogs.IntelChatMessageParser(systems));
            var messages = new List<IntelChatReport>();
            var reports = new List<IntelChatReport>();
            service.ChannelMessageReceived += (_, message) => messages.Add(message);
            service.ReportReceived += (_, report) => reports.Add(report);
            var process = typeof(IntelChatLogFeedHostedService).GetMethod("ProcessFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            await (Task)process.Invoke(service, [duplicate, CancellationToken.None])!;
            Assert.Equal(4, messages.Count);
            Assert.All(messages, message => Assert.True(message.IsHistorical));
            Assert.Equal("status?", messages[0].MessageText);
            Assert.Empty(messages[0].Systems);
            Assert.Equal("Kill: Rave Maulerant (Thorax)", messages[1].MessageText);
            Assert.Equal("https://example.com/report", messages[3].MessageText);
            Assert.True(Assert.Single(reports).IsClear);
            var live = DateTime.UtcNow.AddSeconds(2);
            await File.AppendAllTextAsync(path, $"[ {live:yyyy.MM.dd HH:mm:ss} ] Scout > still watching\n");
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            Assert.Equal(5, messages.Count);
            Assert.False(messages[^1].IsHistorical);
            Assert.Single(reports);
            // Identical content reported in another channel is a separate history entry.
            await File.WriteAllTextAsync(otherChannel, $"Channel Name: Other\n[ {timestamp:yyyy.MM.dd HH:mm:ss} ] Scout > status?\n");
            await (Task)process.Invoke(service, [otherChannel, CancellationToken.None])!;
            Assert.Equal(6, messages.Count);
            Assert.Equal("Other", messages[^1].ChannelName);
        }
        finally
        {
            File.Delete(path); File.Delete(duplicate); File.Delete(otherChannel); Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task ShipNameLookupPreservesCanonicalNamesEvenForUnclassifiedShipGroups()
    {
        var connectionString = $"Data Source=ship-names-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        await using var command = anchor.CreateCommand();
        command.CommandText = """
            CREATE TABLE invGroups (groupID INTEGER, groupName TEXT, categoryID INTEGER);
            CREATE TABLE invTypes (typeID INTEGER, typeName TEXT, groupID INTEGER);
            INSERT INTO invGroups VALUES (1, 'Interceptor', 6), (2, 'Combat Recon Ship', 6),
                                        (3, 'New Ship Group', 6), (4, 'Module', 7);
            INSERT INTO invTypes VALUES (100, 'Ares', 1), (200, 'Huginn', 2),
                                       (300, 'Special Ship', 3), (400, 'Module', 4);
            """;
        await command.ExecuteNonQueryAsync();
        var service = new IntelChatLogFeedHostedService(new NoopSettingsService(), new ShipNameTestDatabase(connectionString),
            new NoopHttpClientFactory(), NullLogger<IntelChatLogFeedHostedService>.Instance);
        var load = typeof(IntelChatLogFeedHostedService).GetMethod("LoadShipMapsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<(Dictionary<string, IntelShipClass>, Dictionary<string, int>, Dictionary<int, IntelShipClass>, Dictionary<int, string>)>)
            load.Invoke(service, [CancellationToken.None])!;
        var maps = await task;
        typeof(IntelChatLogFeedHostedService).GetField("_shipNameByTypeId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, maps.Item4);
        IIntelFeed feed = service;
        Assert.Equal("Ares", feed.GetShipTypeName(100));
        Assert.Equal("Huginn", feed.GetShipTypeName(200));
        Assert.Equal("Special Ship", feed.GetShipTypeName(300));
        Assert.Null(feed.GetShipTypeName(400));
        Assert.Null(feed.GetShipTypeName(999));
    }

    private sealed class ShipNameTestDatabase(string connectionString) : ISdeDatabase
    {
        public SqliteConnection CreateConnection() => new(connectionString);
    }

    [Fact]
    public async Task InitialFileHistoryIsMarkedHistoricalButNewAppendedReportsAreLive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-intel-popup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Intel.txt");
        try
        {
            var historicalTime = DateTime.UtcNow.AddSeconds(-5);
            await File.WriteAllTextAsync(path, $"Channel Name: Intel\n[ {historicalTime:yyyy.MM.dd HH:mm:ss} ] Scout > E-JCUS clear\n");
            var service = CreateServiceWithSystems();
            var systems = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["E-JCUS"] = 1, ["SHBF-V"] = 2 };
            typeof(IntelChatLogFeedHostedService).GetField("_systemIdByName", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, systems);
            typeof(IntelChatLogFeedHostedService).GetField("_messageParser", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, new Hisa.Logs.IntelChatLogs.IntelChatMessageParser(systems));
            var reports = new List<IntelChatReport>();
            service.ReportReceived += (_, report) => reports.Add(report);
            var process = typeof(IntelChatLogFeedHostedService).GetMethod("ProcessFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            Assert.True(Assert.Single(reports).IsHistorical);
            var liveTime = DateTime.UtcNow.AddSeconds(2);
            await File.AppendAllTextAsync(path, $"[ {liveTime:yyyy.MM.dd HH:mm:ss} ] Scout > SHBF-V clear\n");
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            Assert.Equal(2, reports.Count);
            Assert.False(reports[1].IsHistorical);
            Assert.Equal(new[] { "SHBF-V" }, reports[1].Systems);
            await (Task)process.Invoke(service, [path, CancellationToken.None])!;
            Assert.Equal(2, reports.Count);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

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
    public void TryRegisterReport_WhenSameLineIsWithinCrossClientSkew_RejectsDuplicate()
    {
        var service = CreateServiceWithSystems();
        var timestampUtc = DateTime.UtcNow;
        var first = CreateReport("Old System", timestampUtc, "Reporter", "N3-JBX* 10+");
        var skewedCopy = CreateReport("Old System", timestampUtc.AddSeconds(2), "Reporter", "N3-JBX* 10+");

        Assert.True(TryRegisterReport(service, first));
        Assert.False(TryRegisterReport(service, skewedCopy));
    }

    [Fact]
    public void TryRegisterReport_WhenSameLineIsOutsideCrossClientSkew_AllowsRepeat()
    {
        var service = CreateServiceWithSystems();
        var timestampUtc = DateTime.UtcNow;
        var first = CreateReport("Old System", timestampUtc, "Reporter", "N3-JBX* 10+");
        var later = CreateReport("Old System", timestampUtc.AddMinutes(2), "Reporter", "N3-JBX* 10+");

        Assert.True(TryRegisterReport(service, first));
        Assert.True(TryRegisterReport(service, later));
    }

    [Fact]
    public void TryRegisterReport_WhenLateCopyArrivesAfterNewerAcceptance_StillRejectsCopy()
    {
        var service = CreateServiceWithSystems();
        var timestampUtc = DateTime.UtcNow;
        var first = CreateReport("Old System", timestampUtc, "Reporter", "N3-JBX* 10+");
        var later = CreateReport("Old System", timestampUtc.AddMinutes(2), "Reporter", "N3-JBX* 10+");
        var lateCopyOfFirst = CreateReport("Old System", timestampUtc.AddSeconds(2), "Reporter", "N3-JBX* 10+");

        Assert.True(TryRegisterReport(service, first));
        Assert.True(TryRegisterReport(service, later));
        Assert.False(TryRegisterReport(service, lateCopyOfFirst));
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
        Assert.Empty(GetStringList(parsed, "FriendlyShipNames"));
    }

    [Fact]
    public void TryParseExternalIntelLinkData_DscanInfoHtml_IgnoresShipClassSummaryList()
    {
        const string html = """
            <div class="panel-heading lead headline">System: <b><a href="#">5C-RPA</a></b></div>
            <ul class="list-group" id="ships">
                <li class="list-group-item shipclass26" data-sclid="26"><span class="badge label label-default">2</span><b>Augoror</b></li>
                <li class="list-group-item shipclass25" data-sclid="25"><span class="badge label label-default">1</span><b>Astero</b></li>
            </ul>
            <ul class="list-group" id="shipclasses">
                <li class="list-group-item" data-sclid="26"><span class="badge label label-default">2</span><b>Cruiser</b></li>
                <li class="list-group-item" data-sclid="419"><span class="badge label label-default">1</span><b>Covert Ops</b></li>
                <li class="list-group-item" data-sclid="27"><span class="badge label label-default">3</span><b>Battlecruiser</b></li>
            </ul>
            """;

        var parsed = ParseExternalIntelLinkData("https://dscan.info/v/example", html);
        var shipNames = GetStringList(parsed, "ShipNames");

        Assert.Equal(3, shipNames.Count);
        Assert.Equal(2, shipNames.Count(x => x == "Augoror"));
        Assert.Contains("Astero", shipNames);
        Assert.DoesNotContain("Cruiser", shipNames);
        Assert.DoesNotContain("Covert Ops", shipNames);
        Assert.DoesNotContain("Battlecruiser", shipNames);
    }

    [Fact]
    public void TryParseExternalIntelLinkData_AdashboardHtml_ExtractsShipsAndSplitsFriendly()
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
              <tr data-race="1" style="background: #f5c6c6;">
                <td style="vertical-align: middle;" title="Frigate"><span data-typeID="33468">&nbsp;</span>&nbsp;Astero</td>
                <td style="text-align: right; width: 10%;"><span>1</span></td>
              </tr>
            </table>
            <table class="table table-condensed">
              <tr>
                <td style="vertical-align: middle;" title="Battlecruiser">&nbsp;Battlecruiser</td>
                <td style="text-align: right; width: 10%;"><span>4</span></td>
              </tr>
              <tr>
                <td style="vertical-align: middle;" title="Destroyer">&nbsp;Destroyer</td>
                <td style="text-align: right; width: 10%;"><span>2</span></td>
              </tr>
            </table>
            """;

        var parsed = ParseExternalIntelLinkData("https://adashboard.info/intel/dscan/view/example", html);
        var shipNames = GetStringList(parsed, "ShipNames");
        var friendlyShipNames = GetStringList(parsed, "FriendlyShipNames");
        var shipClasses = GetShipClassList(parsed, "ShipClasses");
        var friendlyShipClasses = GetShipClassList(parsed, "FriendlyShipClasses");

        Assert.Equal(2, shipNames.Count);
        Assert.Contains("Cynabal", shipNames);
        Assert.Contains("Astero", shipNames);
        Assert.Equal(2, friendlyShipNames.Count);
        Assert.All(friendlyShipNames, name => Assert.Equal("Stiletto", name));
        Assert.DoesNotContain("Battlecruiser", shipNames);
        Assert.DoesNotContain("Destroyer", shipNames);
        Assert.DoesNotContain("Battlecruiser", friendlyShipNames);
        Assert.Contains(IntelShipClass.Cruiser, shipClasses);
        Assert.Contains(IntelShipClass.Frigate, shipClasses);
        Assert.Contains(IntelShipClass.Frigate, friendlyShipClasses);
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

    [Fact]
    public async Task ResolveReportedCharacterNamesAsync_DropsKnownShipHullNames()
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            new StubHttpClientFactory("""
                {"characters":[{"id":123,"name":"Askulen Akasa Soikutsu"},{"id":124,"name":"Astero"},{"id":125,"name":"Nyx"}]}
                """),
            NullLogger<IntelChatLogFeedHostedService>.Instance);
        ApplyShipMaps(service);

        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "ResolveReportedCharacterNamesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task<IReadOnlyList<string>>)method!.Invoke(service,
            [new List<string> { "Astero", "Askulen Akasa Soikutsu", "Nyx" }, CancellationToken.None])!;
        var names = await task;

        Assert.Equal(["Askulen Akasa Soikutsu"], names);
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
        ApplyShipMaps(service);
        return service;
    }

    private static void ApplyShipMaps(IntelChatLogFeedHostedService service)
    {
        var shipField = typeof(IntelChatLogFeedHostedService).GetField(
            "_shipClassByName",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(shipField);
        shipField!.SetValue(service, new Dictionary<string, IntelShipClass>(StringComparer.OrdinalIgnoreCase)
        {
            ["Augoror"] = IntelShipClass.Cruiser,
            ["Astero"] = IntelShipClass.Frigate,
            ["Stiletto"] = IntelShipClass.Frigate,
            ["Cynabal"] = IntelShipClass.Cruiser,
            ["Nyx"] = IntelShipClass.Supercapital
        });
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
