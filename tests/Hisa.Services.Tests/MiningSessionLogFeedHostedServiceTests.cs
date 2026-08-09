using System.Net;
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
        Assert.Equal(4, handler.RequestCount);
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

        Assert.Equal(4, seedHandler.RequestCount);

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
        Assert.Equal(4, handler.RequestCount);
        Assert.True(state.OreCount > 0, $"Expected ore cache entries after refresh, but count was {state.OreCount} and refresh due was {state.RefreshDueUtc:o}.");
        Assert.True(state.RefreshDueUtc > DateTime.UtcNow, $"Expected ore cache to remain fresh after refresh, but refresh due was {state.RefreshDueUtc:o}.");
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
        private readonly Dictionary<string, object?> _values = new();

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
                    ('Blue Ice', 1000.0),
                    ('Prismaticite', 1.0);
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

        public CountingOreHttpMessageHandler(TimeSpan? delayPerRequest = null)
        {
            _delayPerRequest = delayPerRequest ?? TimeSpan.Zero;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);

            if (_delayPerRequest > TimeSpan.Zero)
            {
                await Task.Delay(_delayPerRequest, cancellationToken);
            }

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var generatedAt = DateTime.UtcNow.ToString("O");
            var payload = path switch
            {
                "/api/public/v1/ores/standard" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":[{{\"name\":\"Veldspar\",\"volume\":0.1,\"unitsToReprocess\":100,\"refinedValueToday\":1000.0}}]}}",
                "/api/public/v1/ores/moon" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":[{{\"name\":\"Bitumens\",\"volume\":10.0,\"unitsToReprocess\":1,\"refinedValueToday\":5000.0}}]}}",
                "/api/public/v1/ores/ice" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":[{{\"name\":\"Blue Ice\",\"volume\":1000.0,\"unitsToReprocess\":1,\"refinedValueToday\":250000.0}}]}}",
                "/api/public/v1/ores/prismaticite" => $"{{\"generatedAt\":\"{generatedAt}\",\"data\":{{\"oreName\":\"Prismaticite\",\"oreVolume\":1.0,\"expectedRandomValuePerOre\":12345.0}}}}",
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
