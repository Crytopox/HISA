using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class MiningSessionLogFeedHostedServiceTests
{
    private const string OreCacheKey = "Mining.OreReferenceCache";
    private const string GasCacheKey = "Mining.GasPriceCache";

    [Fact]
    public async Task GetSnapshotAsync_ConcurrentCallsOnlyRefreshOreValuesOnce()
    {
        var settings = new InMemorySettingsService();
        var handler = new CountingOreHttpMessageHandler(delayPerRequest: TimeSpan.FromMilliseconds(50));
        var logger = new TestLogger<MiningSessionLogFeedHostedService>();
        using var service = CreateService(settings, new TestHttpClientFactory(handler), logger);

        var tasks = Enumerable.Range(0, 6)
            .Select(_ => service.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession))
            .ToArray();

        await Task.WhenAll(tasks);

        var state = GetOreCacheState(service);
        Assert.True(logger.LastException is null, $"Unexpected refresh exception: {logger.LastException}");
        Assert.Equal(5, handler.RequestCount);
        Assert.Equal(1, handler.GasRequestCount);
        Assert.True(state.OreCount > 0, $"Expected ore cache entries after refresh, but count was {state.OreCount} and refresh due was {state.RefreshDueUtc:o}.");
        Assert.True(state.RefreshDueUtc > DateTime.UtcNow, $"Expected ore cache to remain fresh after refresh, but refresh due was {state.RefreshDueUtc:o}.");
    }

    [Fact]
    public async Task GetSnapshotAsync_LoadsFreshOreValuesFromSettingsCacheOnStartup()
    {
        var settings = new InMemorySettingsService();
        var seedHandler = new CountingOreHttpMessageHandler();
        var seedLogger = new TestLogger<MiningSessionLogFeedHostedService>();

        using (var seedingService = CreateService(settings, new TestHttpClientFactory(seedHandler), seedLogger))
        {
            await seedingService.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession);
            Assert.True(seedLogger.LastException is null, $"Unexpected refresh exception while seeding cache: {seedLogger.LastException}");
            var seededState = GetOreCacheState(seedingService);
            Assert.True(seededState.OreCount > 0, $"Seeded service did not retain ore cache entries. Refresh due: {seededState.RefreshDueUtc:o}.");
        }

        Assert.Equal(5, seedHandler.RequestCount);

        var throwingFactory = new TrackingThrowingHttpClientFactory();
        using var cachedService = CreateService(settings, throwingFactory);
        await cachedService.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession);

        Assert.Equal(0, throwingFactory.CreateClientCalls);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReusesFreshOreValuesWithinSameSession()
    {
        var settings = new InMemorySettingsService();
        var handler = new CountingOreHttpMessageHandler();
        var logger = new TestLogger<MiningSessionLogFeedHostedService>();
        using var service = CreateService(settings, new TestHttpClientFactory(handler), logger);

        await service.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession);
        await service.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession);

        var state = GetOreCacheState(service);
        Assert.True(logger.LastException is null, $"Unexpected refresh exception: {logger.LastException}");
        Assert.Equal(5, handler.RequestCount);
        Assert.True(state.OreCount > 0, $"Expected ore cache entries after refresh, but count was {state.OreCount} and refresh due was {state.RefreshDueUtc:o}.");
        Assert.True(state.RefreshDueUtc > DateTime.UtcNow, $"Expected ore cache to remain fresh after refresh, but refresh due was {state.RefreshDueUtc:o}.");
    }

    [Fact]
    public async Task GetSnapshotAsync_FetchesGasWhenOnlyThePreUpgradeOreCacheExists()
    {
        var settings = new InMemorySettingsService();
        using var seedHandler = new CountingOreHttpMessageHandler();
        using (var service = CreateService(settings, new TestHttpClientFactory(seedHandler)))
        {
            await service.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession);
        }

        settings.Remove(GasCacheKey);
        using var handler = new CountingOreHttpMessageHandler();
        using var upgraded = CreateService(settings, new TestHttpClientFactory(handler));

        await upgraded.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession);
        await upgraded.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(1, handler.GasRequestCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_GasOutageKeepsCachedPricesAndAllowsOreRefresh()
    {
        var now = DateTime.UtcNow.AddMinutes(-2);
        using var logs = new TestMiningLogs();
        await logs.WriteAsync(now, 1234, Yield(now, 4, "Fullerite-C32"), Residue(now, 2, "Fullerite-C32"),
            Yield(now, 100, "Veldspar"));
        using var seedHandler = new CountingOreHttpMessageHandler();
        using (var service = CreateService(logs.Settings, new TestHttpClientFactory(seedHandler)))
        {
            await service.GetSnapshotAsync(MiningStatsRangeMode.Last1Hour);
        }

        logs.Settings.SetCacheTimestamp(OreCacheKey, "RefreshDueUtc", now);
        logs.Settings.SetCacheTimestamp(GasCacheKey, "RefreshedAtUtc", now.AddDays(-1));
        using var handler = new CountingOreHttpMessageHandler { GasUnavailable = true, VeldsparReferenceValue = 2000 };
        using var serviceDuringOutage = CreateService(logs.Settings, new TestHttpClientFactory(handler));

        var snapshot = (await serviceDuringOutage.GetSnapshotAsync(MiningStatsRangeMode.Last1Hour))[1234];
        var ores = snapshot.Ores.ToDictionary(x => x.OreName);
        Assert.Equal(48000m, ores["Fullerite-C32"].TotalEstimatedIsk);
        Assert.Equal(24000m, snapshot.TotalWasteEstimatedIsk);
        Assert.Equal(2000m * 0.9063m, ores["Veldspar"].TotalEstimatedIsk);

        await serviceDuringOutage.GetSnapshotAsync(MiningStatsRangeMode.Last1Hour);
        Assert.Equal(5, handler.RequestCount);
        Assert.Equal(1, handler.GasRequestCount);

        var gasStateField = typeof(MiningSessionLogFeedHostedService).GetField(
            "_gasPrices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var gasState = gasStateField.GetValue(serviceDuringOutage)!;
        var dueProperty = gasState.GetType().GetProperty("RefreshDueUtc")!;
        Assert.InRange((DateTime)dueProperty.GetValue(gasState)!, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(5));

        dueProperty.SetValue(gasState, now);
        handler.GasUnavailable = false;
        handler.GasUnitPrice = 13000;
        var recovered = (await serviceDuringOutage.GetSnapshotAsync(MiningStatsRangeMode.Last1Hour))[1234];
        Assert.Equal(6, handler.RequestCount);
        Assert.Equal(2, handler.GasRequestCount);
        Assert.Equal(26000m, recovered.TotalWasteEstimatedIsk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MiningSnapshots_CountResidueAndUseLocalVolumes(bool pricesUnavailable)
    {
        var now = DateTime.UtcNow;
        var start = now.AddMinutes(-5);
        var cutoff = now.AddMinutes(-3);
        using var logs = new TestMiningLogs();
        await logs.WriteAsync(start, 1234,
            Yield(start, 100, "Veldspar"),
            $"[ {start:yyyy.MM.dd HH:mm:ss} ] (notify) Phased Prismaticite has Phased-Out and can be mined at 10% efficiency.",
            $"[ {start:yyyy.MM.dd HH:mm:ss} ] (mining) Critical mining success! You mined an additional <font size=12>25<font size=10> units of <font size=12>Prismaticite",
            Residue(cutoff, 10, "Prismaticite"),
            Residue(cutoff, 5, "asteroid"),
            Yield(cutoff, 4, "Fullerite-C32"),
            Residue(cutoff, 2, "Fullerite-C32"),
            Yield(cutoff, 1, "Fullerite-C70"),
            Yield(cutoff, 1, "Fullerite-C72"),
            Yield(cutoff, 1, "Glacial Mass IV-Grade"),
            Yield(cutoff, 2, "Brimful Bitumens"),
            Yield(cutoff, 1, "Blue Ice"));
        await logs.WriteAsync(start.AddMinutes(-1), 1234, Residue(cutoff, 900, "asteroid"));

        using var handler = new CountingOreHttpMessageHandler();
        IHttpClientFactory prices = pricesUnavailable
            ? new TrackingThrowingHttpClientFactory()
            : new TestHttpClientFactory(handler);
        using var service = CreateService(logs.Settings, prices);
        var live = new TaskCompletionSource<MiningCharacterStatsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SnapshotUpdated += (_, snapshots) =>
        {
            if (snapshots.TryGetValue(1234, out var snapshot))
            {
                live.TrySetResult(snapshot);
            }
        };

        await service.StartAsync(CancellationToken.None);
        try
        {
            var published = await live.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var current = (await service.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession))[1234];
            var recent = (await service.GetSnapshotAsync(MiningStatsRangeMode.Last1Hour))[1234];
            var rolling = (await service.GetRollingSnapshotAsync(TimeSpan.FromMinutes(4)))[1234];

            foreach (var snapshot in new[] { published, current, recent, rolling })
            {
                var ores = snapshot.Ores.ToDictionary(x => x.OreName);
                Assert.Equal(10, ores["Prismaticite"].WasteUnits);
                Assert.Equal(400, ores["Prismaticite"].TotalWasteVolumeM3);
                Assert.Equal(5, ores["Veldspar"].WasteUnits);
                Assert.Equal(20, ores["Fullerite-C32"].TotalMinedVolumeM3);
                Assert.Equal(10, ores["Fullerite-C32"].TotalWasteVolumeM3);
                Assert.Equal(pricesUnavailable ? 0m : 12000m, ores["Fullerite-C32"].EstimatedIskPerUnit);
                Assert.Equal(pricesUnavailable ? 0m : 48000m, ores["Fullerite-C32"].TotalEstimatedIsk);
                Assert.Equal(pricesUnavailable ? 0d : 2d, ores["Fullerite-C70"].TotalMinedVolumeM3);
                Assert.Equal(2d, ores["Fullerite-C72"].TotalMinedVolumeM3);
                Assert.Equal(0m, ores["Fullerite-C72"].EstimatedIskPerUnit);
                Assert.Equal(1000, ores["Glacial Mass IV-Grade"].TotalMinedVolumeM3);
                Assert.Equal(20, ores["Brimful Bitumens"].TotalMinedVolumeM3);
                Assert.Equal(pricesUnavailable ? 0 : 1000, ores["Blue Ice"].TotalMinedVolumeM3);
                Assert.Equal(pricesUnavailable ? 0m : 12345m * 0.9063m, ores["Prismaticite"].EstimatedIskPerUnit);
                Assert.Equal(410.5, snapshot.TotalWasteVolumeM3);
            }

            if (!pricesUnavailable)
            {
                await logs.Settings.SetAsync<decimal?>("Mining.RefineYieldPercent", 50m);
                var adjusted = (await service.GetSnapshotAsync(MiningStatsRangeMode.CurrentSession))[1234];
                var ores = adjusted.Ores.ToDictionary(x => x.OreName);
                Assert.Equal(48000m, ores["Fullerite-C32"].TotalEstimatedIsk);
                Assert.Equal(500m, ores["Veldspar"].TotalEstimatedIsk);
                Assert.Equal(24000m + 10 * 12345m * 0.5m + 5 * 5m, adjusted.TotalWasteEstimatedIsk);
            }

            Assert.Equal(100, current.Ores.Single(x => x.OreName == "Veldspar").MinedUnits);
            Assert.Equal(25, recent.Ores.Single(x => x.OreName == "Prismaticite").BonusUnits);
            Assert.True(current.EfficiencyPercent > 100);
            Assert.Equal(0, rolling.Ores.Single(x => x.OreName == "Veldspar").MinedUnits);
            Assert.Equal(0, rolling.Ores.Single(x => x.OreName == "Prismaticite").BonusUnits);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static string Yield(DateTime timestamp, int units, string ore) =>
        $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] (mining) You mined <color=#ff8dc169>{units}<font size=10> units of <font size=12>{ore}";

    private static string Residue(DateTime timestamp, int units, string source) =>
        $"[ {timestamp:yyyy.MM.dd HH:mm:ss} ] (mining) Additional <color=#ffff454b>{units}<font size=10> units depleted from {source} as residue";

    private sealed class TestMiningLogs : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("hisa-mining-").FullName;
        public InMemorySettingsService Settings { get; } = new();

        public TestMiningLogs()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Gamelogs"));
            Directory.CreateDirectory(Path.Combine(_root, "ChatLogs"));
            File.WriteAllText(Path.Combine(_root, "ChatLogs", "Local_20260613_120000_1234.txt"), "");
            Settings.SetAsync("Tracking.LogsRootPath", _root).GetAwaiter().GetResult();
            Settings.SetAsync<bool?>("Mining.Enabled", true).GetAwaiter().GetResult();
        }

        public Task WriteAsync(DateTime start, int characterId, params string[] entries) =>
            File.WriteAllLinesAsync(Path.Combine(_root, "Gamelogs", $"{start:yyyyMMdd_HHmmss}_{characterId}.txt"),
                ["------------------------------------------------------------", "Listener: Test Miner", "------------------------------------------------------------", .. entries]);

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private static MiningSessionLogFeedHostedService CreateService(
        ISettingsService settingsService,
        IHttpClientFactory httpClientFactory,
        ILogger<MiningSessionLogFeedHostedService>? logger = null)
    {
        return new MiningSessionLogFeedHostedService(
            settingsService,
            new TestSdeDatabase(),
            httpClientFactory,
            logger ?? NullLogger<MiningSessionLogFeedHostedService>.Instance);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly ConcurrentDictionary<string, JsonElement> _values = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(key, out var value))
            {
                return Task.FromResult(value.Deserialize<T>());
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = JsonSerializer.SerializeToElement(value);
            return Task.CompletedTask;
        }

        public void Remove(string key) => _values.TryRemove(key, out _);

        public void SetCacheTimestamp(string key, string propertyName, DateTime timestamp)
        {
            var payload = _values[key].Deserialize<JsonObject>()!;
            payload[propertyName] = timestamp;
            _values[key] = JsonSerializer.SerializeToElement(payload);
        }
    }

    private sealed class TestSdeDatabase : ISdeDatabase
    {
        private readonly string _connectionString;

        public TestSdeDatabase()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"hisa-mining-test-{Guid.NewGuid():N}.sqlite");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            }.ToString();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE invTypes (
                    typeName TEXT NOT NULL,
                    volume REAL NULL
                );

                INSERT INTO invTypes (typeName, volume) VALUES
                    ('Veldspar', 0.1),
                    ('Bitumens', 10.0),
                    ('Brimful Bitumens', 10.0),
                    ('Glacial Mass IV-Grade', 1000.0),
                    ('Fullerite-C32', 5.0),
                    ('Fullerite-C72', 2.0),
                    ('Prismaticite', 40.0);
                """;
            command.ExecuteNonQuery();
        }

        public SqliteConnection CreateConnection() => new(_connectionString);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://haakario.com/")
            };
        }
    }

    private sealed class TrackingThrowingHttpClientFactory : IHttpClientFactory
    {
        private int _createClientCalls;

        public int CreateClientCalls => Volatile.Read(ref _createClientCalls);

        public HttpClient CreateClient(string name)
        {
            Interlocked.Increment(ref _createClientCalls);
            throw new InvalidOperationException("HTTP should not be used.");
        }
    }

    private sealed class CountingOreHttpMessageHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delayPerRequest;
        private int _requestCount;
        private int _gasRequestCount;

        public CountingOreHttpMessageHandler(TimeSpan? delayPerRequest = null)
        {
            _delayPerRequest = delayPerRequest ?? TimeSpan.Zero;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);
        public int GasRequestCount => Volatile.Read(ref _gasRequestCount);
        public bool GasUnavailable { get; set; }
        public int GasUnitPrice { get; set; } = 12000;
        public int VeldsparReferenceValue { get; init; } = 1000;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);

            if (_delayPerRequest > TimeSpan.Zero)
            {
                await Task.Delay(_delayPerRequest, cancellationToken);
            }

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/api/public/v1/ores/gas")
            {
                Interlocked.Increment(ref _gasRequestCount);
                if (GasUnavailable)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
            }

            var generatedAt = DateTime.UtcNow.ToString("O");
            var payload = path switch
            {
                "/api/public/v1/ores/standard" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":[{{\"name\":\"Veldspar\",\"volume\":0.1,\"unitsToReprocess\":100,\"refinedValueToday\":{VeldsparReferenceValue}}}]}}",
                "/api/public/v1/ores/moon" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":[{{\"name\":\"Bitumens\",\"volume\":10.0,\"unitsToReprocess\":1,\"refinedValueToday\":5000.0}}]}}",
                "/api/public/v1/ores/ice" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":[{{\"name\":\"Blue Ice\",\"volume\":1000.0,\"unitsToReprocess\":1,\"refinedValueToday\":250000.0}}]}}",
                "/api/public/v1/ores/prismaticite" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":{{\"oreName\":\"Prismaticite\",\"oreVolume\":1.0,\"expectedRandomValuePerOre\":12345.0}}}}",
                "/api/public/v1/ores/gas" => $$"""
                    {"generatedAt":"{{generatedAt}}","data":[
                      {"typeId":1,"name":"fullerite-c32","family":"Fullerite","volume":99,"unitPriceToday":{{GasUnitPrice}},"unitPriceYesterday":11000,"unitPriceWeek":10000},
                      {"typeId":2,"name":"Fullerite-C70","family":"Fullerite","volume":2,"unitPriceToday":4000,"unitPriceYesterday":3900,"unitPriceWeek":3800}
                    ]}
                    """,
                _ => throw new InvalidOperationException($"Unexpected ore endpoint: {path}")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                LastException = exception;
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }

    private static (int OreCount, DateTime RefreshDueUtc) GetOreCacheState(MiningSessionLogFeedHostedService service)
    {
        var oreValuesField = typeof(MiningSessionLogFeedHostedService).GetField("_oreValuesByName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var refreshDueField = typeof(MiningSessionLogFeedHostedService).GetField("_oreValuesRefreshDueUtc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var oreValues = Assert.IsAssignableFrom<System.Collections.IDictionary>(oreValuesField?.GetValue(service));
        var refreshDueUtc = Assert.IsType<DateTime>(refreshDueField?.GetValue(service));
        return (oreValues.Count, refreshDueUtc);
    }
}
