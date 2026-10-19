using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Core.Models.Intelligence;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Hisa.Services.Intelligence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public class ZkillObservationPipelineTests
{
    private const long JitaSystemId = 30000142;

    [Fact]
    public void RecentKillmail_PublishesObservationAndLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var harness = CreateHarness();
        var snapshotUpdates = 0;
        harness.Service.SnapshotUpdated += (_, _) => snapshotUpdates++;

        Process(harness, CreatePayload(1001, utcNow.AddMinutes(-2), 2500m), sequence: 8, utcNow);

        var observation = Assert.Single(harness.Observations);
        Assert.Equal(8, observation.SourceSequence);
        Assert.Equal(utcNow.AddMinutes(-2), observation.OccurredAtUtc);
        Assert.Equal(2500m, observation.TotalValue);

        var report = Assert.Single(harness.Reports);
        Assert.Equal("killmail:1001", report.DedupeKey);
        Assert.Equal([IntelAlertType.Fight], report.Alerts);
        Assert.Equal(["Jita"], report.Systems);
        Assert.Equal(["Capsule"], report.ReportedShipNames);
        Assert.Equal([670], report.ReportedShipTypeIds);
        Assert.Equal([IntelShipClass.Capsule], report.ShipClasses);
        Assert.False(report.IsClear);
        Assert.Equal(2, report.ReportedHostileCount);
        Assert.NotNull(report.Killmail);
        Assert.Equal(1001, report.Killmail.KillmailId);
        Assert.Equal("abc123", report.Killmail.Hash);
        Assert.Equal("https://zkillboard.com/kill/1001/", report.Killmail.Url);
        Assert.Equal(2500m, report.Killmail.TotalValue);
        Assert.Equal("Victim Pilot", report.Killmail.VictimName);
        Assert.Equal(9001, report.Killmail.VictimCharacterId);
        Assert.Equal(2, report.Killmail.Attackers.Count);
        Assert.Null(report.Killmail.Attackers[1].CharacterId);
        Assert.Equal(1000125, report.Killmail.Attackers[1].CorporationId);

        Assert.True(harness.Service.Snapshot.TryGetValue(JitaSystemId, out var snapshot));
        Assert.Equal("Jita", snapshot.SolarSystemName);
        Assert.Equal([IntelAlertType.Fight], snapshot.Alerts);
        Assert.False(snapshot.IsClear);
        Assert.Equal(report.MessageText, snapshot.LastMessageText);
        Assert.Equal(1, snapshotUpdates);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(360)]
    public void OlderKillmail_PublishesObservationWithoutLiveReportOrSnapshot(int ageMinutes)
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var harness = CreateHarness();
        var reports = 0;
        var snapshotUpdates = 0;
        harness.Service.ReportReceived += (_, _) => reports++;
        harness.Service.SnapshotUpdated += (_, _) => snapshotUpdates++;

        Process(harness, CreatePayload(1002, utcNow.AddMinutes(-ageMinutes), null), sequence: 9, utcNow);

        var observation = Assert.Single(harness.Observations);
        Assert.Equal(1002, observation.KillmailId);
        Assert.Null(observation.TotalValue);
        Assert.Equal(0, reports);
        Assert.Equal(0, snapshotUpdates);
        Assert.Empty(harness.Service.Snapshot);
    }

    [Fact]
    public void HistoricalKillmail_DoesNotOverwriteExistingSnapshot()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var harness = CreateHarness();

        Process(harness, CreatePayload(1001, utcNow.AddMinutes(-2), 10m), sequence: 1, utcNow);
        var liveMessage = Assert.Single(harness.Reports).MessageText;

        Process(harness, CreatePayload(1002, utcNow.AddHours(-6), 99m), sequence: 2, utcNow);

        Assert.Equal(2, harness.Observations.Count);
        Assert.Single(harness.Reports);
        Assert.Equal(liveMessage, harness.Service.Snapshot[JitaSystemId].LastMessageText);
        Assert.Equal([IntelAlertType.Fight], harness.Service.Snapshot[JitaSystemId].Alerts);
    }

    [Fact]
    public void UnknownSystem_PublishesObservationWithoutLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var harness = CreateHarness();
        var json = CreatePayload(1003, utcNow.AddMinutes(-1), 10m).Replace("30000142", "30009999", StringComparison.Ordinal);

        Process(harness, json, sequence: 3, utcNow);

        Assert.Equal(30009999, Assert.Single(harness.Observations).SolarSystemId);
        Assert.Empty(harness.Reports);
        Assert.Empty(harness.Service.Snapshot);
    }

    [Fact]
    public void FiveMinuteBoundary_FollowsExistingLiveWindow()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var harness = CreateHarness();

        Process(harness, CreatePayload(1004, utcNow.AddMinutes(-5), 10m), sequence: 4, utcNow);
        Process(harness, CreatePayload(1005, utcNow.AddMinutes(-5).AddTicks(-1), 10m), sequence: 5, utcNow);

        Assert.Equal([1004L, 1005L], harness.Observations.Select(x => x.KillmailId));
        Assert.Equal(1004, Assert.Single(harness.Reports).Killmail!.KillmailId);
    }

    [Fact]
    public void FaultyObservationSubscriber_DoesNotBlockLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var harness = CreateHarness();
        harness.Feed.ObservationReceived += (_, _) => throw new InvalidOperationException("subscriber failed");

        Process(harness, CreatePayload(1006, utcNow.AddMinutes(-1), 10m), sequence: 6, utcNow);

        Assert.Equal(1006, Assert.Single(harness.Observations).KillmailId);
        Assert.Equal(1006, Assert.Single(harness.Reports).Killmail!.KillmailId);
        Assert.True(harness.Service.Snapshot.ContainsKey(JitaSystemId));
    }

    [Fact]
    public void Publish_WithoutSubscribers_StillProducesLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(feed);

        using var document = JsonDocument.Parse(CreatePayload(1007, utcNow.AddMinutes(-1), 10m));
        service.ProcessZkillPayload(document.RootElement, 7, utcNow, utcNow);

        Assert.True(service.Snapshot.ContainsKey(JitaSystemId));
    }

    [Fact]
    public void Publish_IsolatesSubscriberFailures()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var received = new List<long>();
        feed.ObservationReceived += (_, _) => throw new InvalidOperationException("first subscriber");
        feed.ObservationReceived += (_, observation) => received.Add(observation.KillmailId);

        feed.Publish(CreateObservation(42));
        feed.Publish(CreateObservation(43));

        Assert.Equal([42L, 43L], received);
    }

    [Fact]
    public void IncompletePayload_DoesNotPublishOrReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var harness = CreateHarness();

        Process(harness, "{}", sequence: 1, utcNow);

        Assert.Empty(harness.Observations);
        Assert.Empty(harness.Reports);
        Assert.Empty(harness.Service.Snapshot);
    }

    [Fact]
    public async Task PollZkillAsync_WhenZkillDisabled_DoesNotRequestOrPublish()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var observations = 0;
        feed.ObservationReceived += (_, _) => observations++;
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(new Dictionary<string, object?>
            {
                ["Intel.Enabled"] = true,
                ["Intel.Zkill.Enabled"] = false
            }),
            new NoopSdeDatabase(),
            new ThrowingHttpClientFactory(),
            NullLogger<IntelChatLogFeedHostedService>.Instance,
            feed);

        await service.ApplySettingsAsync();
        SetField(service, "_nextZkillSequence", 10L);
        SetField(service, "_nextZkillPollAfterUtc", DateTime.MinValue);

        await InvokePollAsync(service);

        Assert.Equal(0, observations);
        Assert.Equal(10L, GetField<long>(service, "_nextZkillSequence"));
    }

    private static void Process(TestHarness harness, string json, long sequence, DateTimeOffset utcNow)
    {
        using var document = JsonDocument.Parse(json);
        harness.Service.ProcessZkillPayload(document.RootElement, sequence, utcNow, utcNow);
    }

    private static TestHarness CreateHarness()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(feed);
        var harness = new TestHarness(service, feed);
        feed.ObservationReceived += (_, observation) => harness.Observations.Add(observation);
        service.ReportReceived += (_, report) => harness.Reports.Add(report);
        return harness;
    }

    private static IntelChatLogFeedHostedService CreateService(IIntelligenceObservationFeed feed)
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            new ThrowingHttpClientFactory(),
            NullLogger<IntelChatLogFeedHostedService>.Instance,
            feed);
        SetField(service, "_systemIdByName", new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["Jita"] = JitaSystemId
        });
        SetField(service, "_systemNameById", new Dictionary<long, string>
        {
            [JitaSystemId] = "Jita"
        });
        SetField(service, "_shipNameByTypeId", new Dictionary<int, string>
        {
            [670] = "Capsule"
        });
        SetField(service, "_shipClassByTypeId", new Dictionary<int, IntelShipClass>
        {
            [670] = IntelShipClass.Capsule
        });
        return service;
    }

    private static string CreatePayload(long killmailId, DateTimeOffset occurredAtUtc, decimal? totalValue)
    {
        var zkb = totalValue is null
            ? string.Empty
            : $",\"zkb\":{{\"totalValue\":{totalValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        return $$"""
            {
              "killmail_id": {{killmailId}},
              "hash": "abc123"{{zkb}},
              "esi": {
                "killmail_time": "{{occurredAtUtc.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}}",
                "solar_system_id": 30000142,
                "victim": {
                  "character_id": 9001,
                  "corporation_id": 8001,
                  "alliance_id": 7001,
                  "ship_type_id": 670,
                  "character_name": "Victim Pilot"
                },
                "attackers": [
                  {
                    "character_id": 9002,
                    "corporation_id": 8002,
                    "ship_type_id": 587,
                    "final_blow": true,
                    "damage_done": 80,
                    "character_name": "Attacker Pilot"
                  },
                  {
                    "corporation_id": 1000125,
                    "ship_type_id": 3514,
                    "final_blow": false,
                    "damage_done": 20
                  }
                ]
              }
            }
            """;
    }

    private static KillmailObservation CreateObservation(long killmailId)
    {
        return new KillmailObservation
        {
            KillmailId = killmailId,
            Source = KillmailObservation.R2Z2SourceId,
            SolarSystemId = JitaSystemId,
            OccurredAtUtc = DateTimeOffset.UnixEpoch,
            ReceivedAtUtc = DateTimeOffset.UnixEpoch,
            Victim = new KillmailVictim { ShipTypeId = 670 },
            Attackers = []
        };
    }

    private static async Task InvokePollAsync(IntelChatLogFeedHostedService service)
    {
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "PollZkillAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(service, [CancellationToken.None])!;
        await task;
    }

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(target)!;
    }

    private sealed class TestHarness(IntelChatLogFeedHostedService service, IntelligenceObservationFeed feed)
    {
        public IntelChatLogFeedHostedService Service { get; } = service;
        public IntelligenceObservationFeed Feed { get; } = feed;
        public List<KillmailObservation> Observations { get; } = [];
        public List<IntelChatReport> Reports { get; } = [];
    }

    private sealed class NoopSettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values;

        public NoopSettingsService()
            : this(new Dictionary<string, object?>())
        {
        }

        public NoopSettingsService(IReadOnlyDictionary<string, object?> values)
        {
            _values = new Dictionary<string, object?>(values);
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopSdeDatabase : ISdeDatabase
    {
        public SqliteConnection CreateConnection() => throw new NotSupportedException();
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHttpMessageHandler());
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("zKill request was not expected.");
    }
}
