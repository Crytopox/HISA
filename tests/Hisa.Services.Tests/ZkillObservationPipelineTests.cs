using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
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
        using var harness = CreateHarness();
        var snapshotUpdates = 0;
        harness.Service.SnapshotUpdated += (_, _) => snapshotUpdates++;

        Process(harness, CreatePayload(1001, utcNow.AddMinutes(-2), 2500m), sequence: 8, utcNow);
        WaitForDelivery(harness);

        var observation = Assert.Single(harness.CopyObservations());
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
        using var harness = CreateHarness();
        var reports = 0;
        var snapshotUpdates = 0;
        harness.Service.ReportReceived += (_, _) => reports++;
        harness.Service.SnapshotUpdated += (_, _) => snapshotUpdates++;

        Process(harness, CreatePayload(1002, utcNow.AddMinutes(-ageMinutes), null), sequence: 9, utcNow);
        WaitForDelivery(harness);

        var observation = Assert.Single(harness.CopyObservations());
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
        using var harness = CreateHarness();

        Process(harness, CreatePayload(1001, utcNow.AddMinutes(-2), 10m), sequence: 1, utcNow);
        var liveMessage = Assert.Single(harness.Reports).MessageText;

        Process(harness, CreatePayload(1002, utcNow.AddHours(-6), 99m), sequence: 2, utcNow);
        WaitForDelivery(harness);

        Assert.Equal(2, harness.CopyObservations().Length);
        Assert.Single(harness.Reports);
        Assert.Equal(liveMessage, harness.Service.Snapshot[JitaSystemId].LastMessageText);
        Assert.Equal([IntelAlertType.Fight], harness.Service.Snapshot[JitaSystemId].Alerts);
    }

    [Fact]
    public void UnknownSystem_PublishesObservationWithoutLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        using var harness = CreateHarness();
        var json = CreatePayload(1003, utcNow.AddMinutes(-1), 10m).Replace("30000142", "30009999", StringComparison.Ordinal);

        Process(harness, json, sequence: 3, utcNow);
        WaitForDelivery(harness);

        Assert.Equal(30009999, Assert.Single(harness.CopyObservations()).SolarSystemId);
        Assert.Empty(harness.Reports);
        Assert.Empty(harness.Service.Snapshot);
    }

    [Fact]
    public void FiveMinuteBoundary_FollowsExistingLiveWindow()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        using var harness = CreateHarness();

        Process(harness, CreatePayload(1004, utcNow.AddMinutes(-5), 10m), sequence: 4, utcNow);
        Process(harness, CreatePayload(1005, utcNow.AddMinutes(-5).AddTicks(-1), 10m), sequence: 5, utcNow);
        WaitForDelivery(harness);

        Assert.Equal([1004L, 1005L], harness.CopyObservations().Select(x => x.KillmailId).OrderBy(x => x));
        Assert.Equal(1004, Assert.Single(harness.Reports).Killmail!.KillmailId);
    }

    [Fact]
    public void FaultyObservationSubscriber_DoesNotBlockLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        using var harness = CreateHarness();
        harness.Feed.ObservationReceived += (_, _) => throw new InvalidOperationException("subscriber failed");

        Process(harness, CreatePayload(1006, utcNow.AddMinutes(-1), 10m), sequence: 6, utcNow);

        Assert.Equal(1006, Assert.Single(harness.Reports).Killmail!.KillmailId);
        Assert.True(harness.Service.Snapshot.ContainsKey(JitaSystemId));
        WaitForDelivery(harness);
        Assert.Equal(1006, Assert.Single(harness.CopyObservations()).KillmailId);
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
    public void SlowObservationSubscriber_DoesNotDelayLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        using var harness = CreateHarness();
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        harness.Feed.ObservationReceived += (_, _) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };

        try
        {
            var startedAt = Environment.TickCount64;
            Process(harness, CreatePayload(1008, utcNow.AddMinutes(-1), 10m), sequence: 8, utcNow);
            var elapsed = Environment.TickCount64 - startedAt;

            Assert.Equal(1008, Assert.Single(harness.Reports).Killmail!.KillmailId);
            Assert.True(harness.Service.Snapshot.ContainsKey(JitaSystemId));
            Assert.True(elapsed < 500);
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            release.Set();
            WaitForDelivery(harness);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void RejectedPayload_IncrementsNormalizationCounter()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        using var harness = CreateHarness();

        Process(harness, "{}", sequence: 15, utcNow);
        WaitForDelivery(harness);

        Assert.Empty(harness.CopyObservations());
        Assert.Empty(harness.Reports);
        Assert.Equal(1, harness.Service.NormalizationDiagnostics.RejectionCount(KillmailNormalizationRejection.MissingKillmailId));
    }

    [Fact]
    public async Task Publish_IsolatesSubscriberFailures()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var received = new List<long>();
        feed.ObservationReceived += (_, _) => throw new InvalidOperationException("first subscriber");
        feed.ObservationReceived += (_, observation) => received.Add(observation.KillmailId);
        await feed.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(CreateObservation(42));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            feed.Publish(CreateObservation(43));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));

            Assert.Equal([42L, 43L], received);
            Assert.Equal(2, feed.DeliveryFailureCount);
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void IncompletePayload_DoesNotPublishOrReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        using var harness = CreateHarness();

        Process(harness, "{}", sequence: 1, utcNow);
        WaitForDelivery(harness);

        Assert.Empty(harness.CopyObservations());
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

    private static void WaitForDelivery(TestHarness harness)
    {
        Assert.True(harness.Feed.WaitForIdle(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void FullObservationQueue_DoesNotBlockLiveReport()
    {
        var utcNow = new DateTimeOffset(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);
        var feed = new IntelligenceObservationFeed(
            NullLogger<IntelligenceObservationFeed>.Instance,
            new IntelligenceObservationFeedOptions { Capacity = 1 });
        var service = CreateService(feed);
        var reports = new List<IntelChatReport>();
        service.ReportReceived += (_, report) => reports.Add(report);

        Process(service, CreatePayload(2001, utcNow.AddMinutes(-1), 10m), 1, utcNow);
        Process(service, CreatePayload(2002, utcNow.AddMinutes(-1), 11m), 2, utcNow);

        Assert.Equal([2001L, 2002L], reports.Select(report => report.Killmail!.KillmailId));
        Assert.Equal(1, feed.QueueDepth);
        Assert.Equal(1, feed.RejectedEnqueueCount);
        Assert.Equal(1, feed.Capacity);
    }

    [Fact]
    public async Task MalformedSequence_IsSkippedAfterBoundedRetriesAndNextSequenceIsProcessed()
    {
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
        var handler = new SequenceResponseHandler(malformedSequence: 10, validSequence: 11, validBody: CreatePayload(1100, occurred, 25m));
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(feed, new SharedHandlerFactory(handler));
        var reports = new List<IntelChatReport>();
        service.ReportReceived += (_, report) => reports.Add(report);
        SetField(service, "_enabled", true);
        SetField(service, "_zkillEnabled", true);
        SetField(service, "_nextZkillSequence", 10L);
        SetField(service, "_nextZkillPollAfterUtc", DateTime.MinValue);

        for (var attempt = 1; attempt < IntelChatLogFeedHostedService.ZkillMalformedMaxAttempts; attempt++)
        {
            var before = DateTime.UtcNow;
            await InvokePollAsync(service);

            Assert.Equal(10L, GetField<long>(service, "_nextZkillSequence"));
            var nextPoll = GetField<DateTime>(service, "_nextZkillPollAfterUtc");
            Assert.True(nextPoll > before.AddSeconds(1));
            Assert.Equal(0, service.NormalizationDiagnostics.DataGapCount);
            SetField(service, "_nextZkillPollAfterUtc", DateTime.MinValue);
        }

        await InvokePollAsync(service);

        Assert.Equal(IntelChatLogFeedHostedService.ZkillMalformedMaxAttempts, handler.MalformedRequests);
        Assert.Equal(1, handler.ValidRequests);
        Assert.Equal(IntelChatLogFeedHostedService.ZkillMalformedMaxAttempts, service.NormalizationDiagnostics.RejectionCount(KillmailNormalizationRejection.MalformedJson));
        Assert.Equal(1, service.NormalizationDiagnostics.DataGapCount);
        Assert.Equal(1100, Assert.Single(reports).Killmail!.KillmailId);
        Assert.Equal(12L, GetField<long>(service, "_nextZkillSequence"));
    }

    [Fact]
    public async Task NetworkFailure_DoesNotSkipSequenceOrRecordDataGap()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(feed, new SharedHandlerFactory(new NetworkFailureHandler()));
        SetField(service, "_enabled", true);
        SetField(service, "_zkillEnabled", true);
        SetField(service, "_nextZkillSequence", 10L);
        SetField(service, "_nextZkillPollAfterUtc", DateTime.MinValue);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => InvokePollAsync(service));

        Assert.Equal(10L, GetField<long>(service, "_nextZkillSequence"));
        Assert.Equal(0, service.NormalizationDiagnostics.DataGapCount);
        Assert.Equal(0, service.NormalizationDiagnostics.RejectionCount(KillmailNormalizationRejection.MalformedJson));
    }

    private static void Process(TestHarness harness, string json, long sequence, DateTimeOffset utcNow)
    {
        Process(harness.Service, json, sequence, utcNow);
    }

    private static void Process(IntelChatLogFeedHostedService service, string json, long sequence, DateTimeOffset utcNow)
    {
        using var document = JsonDocument.Parse(json);
        service.ProcessZkillPayload(document.RootElement, sequence, utcNow, utcNow);
    }

    private static TestHarness CreateHarness()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(feed);
        var harness = new TestHarness(service, feed);
        feed.ObservationReceived += (_, observation) => harness.AddObservation(observation);
        service.ReportReceived += (_, report) => harness.Reports.Add(report);
        feed.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return harness;
    }

    private static IntelChatLogFeedHostedService CreateService(
        IIntelligenceObservationFeed feed,
        IHttpClientFactory? httpClientFactory = null)
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            httpClientFactory ?? new ThrowingHttpClientFactory(),
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

    private sealed class TestHarness(IntelChatLogFeedHostedService service, IntelligenceObservationFeed feed) : IDisposable
    {
        public IntelChatLogFeedHostedService Service { get; } = service;
        public IntelligenceObservationFeed Feed { get; } = feed;
        public List<IntelChatReport> Reports { get; } = [];
        private readonly object _observationsGate = new();
        private readonly List<KillmailObservation> _observations = [];

        public void AddObservation(KillmailObservation observation)
        {
            lock (_observationsGate)
            {
                _observations.Add(observation);
            }
        }

        public KillmailObservation[] CopyObservations()
        {
            lock (_observationsGate)
            {
                return _observations.ToArray();
            }
        }

        public void Dispose()
        {
            Feed.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
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

    private sealed class NetworkFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }

    private sealed class SharedHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class SequenceResponseHandler(long malformedSequence, long validSequence, string validBody) : HttpMessageHandler
    {
        public int MalformedRequests { get; private set; }
        public int ValidRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith($"/{malformedSequence}.json", StringComparison.Ordinal))
            {
                MalformedRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{", Encoding.UTF8, "application/json")
                });
            }

            if (path.EndsWith($"/{validSequence}.json", StringComparison.Ordinal))
            {
                ValidRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(validBody, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
