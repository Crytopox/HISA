using System.Net;
using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Esi.Models;
using Hisa.Esi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Esi.Clients;

public interface IEsiPublicClient
{
    Task<IReadOnlyList<IncursionInfo>> GetIncursionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, SystemKillInfo>> GetSystemKillsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, SystemJumpInfo>> GetSystemJumpsAsync(CancellationToken cancellationToken = default);
}

public sealed class EsiPublicClient : IEsiPublicClient
{
    private readonly HttpClient _httpClient;
    private readonly IEsiMetricsStore _metricsStore;
    private readonly IEsiAccessTokenProvider _accessTokenProvider;
    private readonly ILogger<EsiPublicClient> _logger;
    private readonly IOptionsMonitor<EsiOptions> _options;
    private readonly Queue<DateTimeOffset> _incursionRequests = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private string? _incursionsEtag;
    private DateTimeOffset _incursionsNextFetchAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<IncursionInfo> _cachedIncursions = [];
    private string? _systemKillsEtag;
    private DateTimeOffset _systemKillsNextFetchAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<int, SystemKillInfo> _cachedSystemKills = new Dictionary<int, SystemKillInfo>();
    private string? _systemJumpsEtag;
    private DateTimeOffset _systemJumpsNextFetchAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<int, SystemJumpInfo> _cachedSystemJumps = new Dictionary<int, SystemJumpInfo>();

    public EsiPublicClient(
        HttpClient httpClient,
        IOptionsMonitor<EsiOptions> options,
        IEsiMetricsStore metricsStore,
        IEsiAccessTokenProvider accessTokenProvider,
        ILogger<EsiPublicClient> logger)
    {
        _httpClient = httpClient;
        _metricsStore = metricsStore;
        _accessTokenProvider = accessTokenProvider;
        _logger = logger;
        _options = options;
    }

    public async Task<IReadOnlyList<IncursionInfo>> GetIncursionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (now < _incursionsNextFetchAtUtc)
            {
                _metricsStore.Add(new EsiRequestMetric
                {
                    TimestampUtc = now,
                    Route = "/incursions/",
                    RateLimitGroup = "incursions.public.local",
                    FromCache = true,
                    StatusCode = 200,
                    ErrorLimitRemain = null,
                    ErrorLimitResetSeconds = null,
                    RateLimitRemain = null,
                    RateLimitResetSeconds = null,
                    ETag = _incursionsEtag,
                    Duration = TimeSpan.Zero,
                    Message = "served-from-cache"
                });
                return _cachedIncursions;
            }

            while (_incursionRequests.Count > 0 && now - _incursionRequests.Peek() > TimeSpan.FromMinutes(15))
            {
                _incursionRequests.Dequeue();
            }

            var options = _options.CurrentValue;
            var limit = Math.Max(1, options.Incursions.TokenLimitPer15Minutes);
            if (_incursionRequests.Count >= limit)
            {
                var oldest = _incursionRequests.Peek();
                var next = oldest.AddMinutes(15);
                _metricsStore.UpdateRateState(new EsiRateState
                {
                    RateLimitGroup = "incursions.public.local",
                    LastRequestAtUtc = now,
                    NextAllowedAtUtc = next,
                    RequestsLast15Minutes = _incursionRequests.Count,
                    RouteTokenLimit15Minutes = limit,
                    HeaderRateLimitRemaining = null,
                    HeaderRateLimitResetSeconds = null
                });
                _logger.LogWarning("Incursion request skipped due to local token guard. Next attempt at {Next}.", next);
                return _cachedIncursions;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "incursions/");
            if (!string.IsNullOrWhiteSpace(_incursionsEtag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", _incursionsEtag);
            }
            var token = await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            sw.Stop();

            _incursionRequests.Enqueue(now);

            var errorRemain = ParseIntHeader(response, "X-ESI-Error-Limit-Remain");
            var errorReset = ParseIntHeader(response, "X-ESI-Error-Limit-Reset");
            var rateRemain = ParseIntHeader(response, "X-Ratelimit-Remaining");
            var rateReset = ParseIntHeader(response, "X-Ratelimit-Reset");
            _incursionsEtag = response.Headers.ETag?.Tag ?? response.Headers.ETag?.ToString() ?? _incursionsEtag;

            _metricsStore.UpdateRateState(new EsiRateState
            {
                RateLimitGroup = "incursions.public.local",
                LastRequestAtUtc = now,
                NextAllowedAtUtc = now.AddSeconds(Math.Max(1, options.Incursions.CacheSeconds)),
                RequestsLast15Minutes = _incursionRequests.Count,
                RouteTokenLimit15Minutes = limit,
                HeaderRateLimitRemaining = rateRemain,
                HeaderRateLimitResetSeconds = rateReset
            });

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _incursionsNextFetchAtUtc = now.AddSeconds(Math.Max(1, options.Incursions.CacheSeconds));
                _metricsStore.Add(new EsiRequestMetric
                {
                    TimestampUtc = now,
                    Route = "/incursions/",
                    RateLimitGroup = "incursions.public.local",
                    FromCache = true,
                    StatusCode = (int)response.StatusCode,
                    ErrorLimitRemain = errorRemain,
                    ErrorLimitResetSeconds = errorReset,
                    RateLimitRemain = rateRemain,
                    RateLimitResetSeconds = rateReset,
                    ETag = _incursionsEtag,
                    Duration = sw.Elapsed,
                    Message = "not-modified"
                });
                return _cachedIncursions;
            }

            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var dto = await JsonSerializer.DeserializeAsync<List<EsiIncursionDto>>(stream, _jsonOptions, cancellationToken) ?? [];
            _cachedIncursions = dto.Select(Map).ToList();
            _incursionsNextFetchAtUtc = now.AddSeconds(Math.Max(1, options.Incursions.CacheSeconds));

            _metricsStore.Add(new EsiRequestMetric
            {
                TimestampUtc = now,
                Route = "/incursions/",
                RateLimitGroup = "incursions.public.local",
                FromCache = false,
                StatusCode = (int)response.StatusCode,
                ErrorLimitRemain = errorRemain,
                ErrorLimitResetSeconds = errorReset,
                RateLimitRemain = rateRemain,
                RateLimitResetSeconds = rateReset,
                ETag = _incursionsEtag,
                Duration = sw.Elapsed,
                Message = $"fetched:{_cachedIncursions.Count}"
            });
            return _cachedIncursions;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static IncursionInfo Map(EsiIncursionDto dto)
    {
        return new IncursionInfo
        {
            ConstellationId = dto.ConstellationId,
            FactionId = dto.FactionId,
            HasBoss = dto.HasBoss,
            Influence = dto.Influence,
            StagingSolarSystemId = dto.StagingSolarSystemId,
            State = dto.State,
            Type = dto.Type,
            InfestedSolarSystems = dto.InfestedSolarSystems
        };
    }

    private static int? ParseIntHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    public async Task<IReadOnlyDictionary<int, SystemKillInfo>> GetSystemKillsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (now < _systemKillsNextFetchAtUtc)
            {
                _metricsStore.Add(new EsiRequestMetric
                {
                    TimestampUtc = now,
                    Route = "/universe/system_kills/",
                    RateLimitGroup = "universe.system_kills.public",
                    FromCache = true,
                    StatusCode = 200,
                    ErrorLimitRemain = null,
                    ErrorLimitResetSeconds = null,
                    RateLimitRemain = null,
                    RateLimitResetSeconds = null,
                    ETag = _systemKillsEtag,
                    Duration = TimeSpan.Zero,
                    Message = "served-from-cache"
                });
                return _cachedSystemKills;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "universe/system_kills/");
            if (!string.IsNullOrWhiteSpace(_systemKillsEtag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", _systemKillsEtag);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            sw.Stop();

            var errorRemain = ParseIntHeader(response, "X-ESI-Error-Limit-Remain");
            var errorReset = ParseIntHeader(response, "X-ESI-Error-Limit-Reset");
            var rateRemain = ParseIntHeader(response, "X-Ratelimit-Remaining");
            var rateReset = ParseIntHeader(response, "X-Ratelimit-Reset");
            _systemKillsEtag = response.Headers.ETag?.Tag ?? response.Headers.ETag?.ToString() ?? _systemKillsEtag;
            var ttl = ResolveTtlSeconds(response, 3600);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _systemKillsNextFetchAtUtc = now.AddSeconds(ttl);
                _metricsStore.Add(new EsiRequestMetric
                {
                    TimestampUtc = now,
                    Route = "/universe/system_kills/",
                    RateLimitGroup = "universe.system_kills.public",
                    FromCache = true,
                    StatusCode = (int)response.StatusCode,
                    ErrorLimitRemain = errorRemain,
                    ErrorLimitResetSeconds = errorReset,
                    RateLimitRemain = rateRemain,
                    RateLimitResetSeconds = rateReset,
                    ETag = _systemKillsEtag,
                    Duration = sw.Elapsed,
                    Message = "not-modified"
                });
                return _cachedSystemKills;
            }

            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var dto = await JsonSerializer.DeserializeAsync<List<EsiSystemKillDto>>(stream, _jsonOptions, cancellationToken) ?? [];
            _cachedSystemKills = dto.ToDictionary(
                x => x.SolarSystemId,
                x => new SystemKillInfo
                {
                    SolarSystemId = x.SolarSystemId,
                    ShipKills = x.ShipKills,
                    PodKills = x.PodKills,
                    NpcKills = x.NpcKills
                });
            _systemKillsNextFetchAtUtc = now.AddSeconds(ttl);

            _metricsStore.Add(new EsiRequestMetric
            {
                TimestampUtc = now,
                Route = "/universe/system_kills/",
                RateLimitGroup = "universe.system_kills.public",
                FromCache = false,
                StatusCode = (int)response.StatusCode,
                ErrorLimitRemain = errorRemain,
                ErrorLimitResetSeconds = errorReset,
                RateLimitRemain = rateRemain,
                RateLimitResetSeconds = rateReset,
                ETag = _systemKillsEtag,
                Duration = sw.Elapsed,
                Message = $"fetched:{_cachedSystemKills.Count}"
            });
            return _cachedSystemKills;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<int, SystemJumpInfo>> GetSystemJumpsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (now < _systemJumpsNextFetchAtUtc)
            {
                _metricsStore.Add(new EsiRequestMetric
                {
                    TimestampUtc = now,
                    Route = "/universe/system_jumps/",
                    RateLimitGroup = "universe.system_jumps.public",
                    FromCache = true,
                    StatusCode = 200,
                    ErrorLimitRemain = null,
                    ErrorLimitResetSeconds = null,
                    RateLimitRemain = null,
                    RateLimitResetSeconds = null,
                    ETag = _systemJumpsEtag,
                    Duration = TimeSpan.Zero,
                    Message = "served-from-cache"
                });
                return _cachedSystemJumps;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "universe/system_jumps/");
            if (!string.IsNullOrWhiteSpace(_systemJumpsEtag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", _systemJumpsEtag);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            sw.Stop();

            var errorRemain = ParseIntHeader(response, "X-ESI-Error-Limit-Remain");
            var errorReset = ParseIntHeader(response, "X-ESI-Error-Limit-Reset");
            var rateRemain = ParseIntHeader(response, "X-Ratelimit-Remaining");
            var rateReset = ParseIntHeader(response, "X-Ratelimit-Reset");
            _systemJumpsEtag = response.Headers.ETag?.Tag ?? response.Headers.ETag?.ToString() ?? _systemJumpsEtag;
            var ttl = ResolveTtlSeconds(response, 3600);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                _systemJumpsNextFetchAtUtc = now.AddSeconds(ttl);
                _metricsStore.Add(new EsiRequestMetric
                {
                    TimestampUtc = now,
                    Route = "/universe/system_jumps/",
                    RateLimitGroup = "universe.system_jumps.public",
                    FromCache = true,
                    StatusCode = (int)response.StatusCode,
                    ErrorLimitRemain = errorRemain,
                    ErrorLimitResetSeconds = errorReset,
                    RateLimitRemain = rateRemain,
                    RateLimitResetSeconds = rateReset,
                    ETag = _systemJumpsEtag,
                    Duration = sw.Elapsed,
                    Message = "not-modified"
                });
                return _cachedSystemJumps;
            }

            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var dto = await JsonSerializer.DeserializeAsync<List<EsiSystemJumpDto>>(stream, _jsonOptions, cancellationToken) ?? [];
            _cachedSystemJumps = dto.ToDictionary(
                x => x.SolarSystemId,
                x => new SystemJumpInfo
                {
                    SolarSystemId = x.SolarSystemId,
                    ShipJumps = x.ShipJumps
                });
            _systemJumpsNextFetchAtUtc = now.AddSeconds(ttl);

            _metricsStore.Add(new EsiRequestMetric
            {
                TimestampUtc = now,
                Route = "/universe/system_jumps/",
                RateLimitGroup = "universe.system_jumps.public",
                FromCache = false,
                StatusCode = (int)response.StatusCode,
                ErrorLimitRemain = errorRemain,
                ErrorLimitResetSeconds = errorReset,
                RateLimitRemain = rateRemain,
                RateLimitResetSeconds = rateReset,
                ETag = _systemJumpsEtag,
                Duration = sw.Elapsed,
                Message = $"fetched:{_cachedSystemJumps.Count}"
            });
            return _cachedSystemJumps;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static int ResolveTtlSeconds(HttpResponseMessage response, int fallbackSeconds)
    {
        if (response.Headers.CacheControl?.MaxAge is TimeSpan maxAge && maxAge.TotalSeconds > 0)
        {
            return Math.Max(1, (int)maxAge.TotalSeconds);
        }

        if (response.Content.Headers.Expires is DateTimeOffset expires)
        {
            var ttl = (int)Math.Round((expires - DateTimeOffset.UtcNow).TotalSeconds);
            if (ttl > 0)
            {
                return ttl;
            }
        }

        return Math.Max(1, fallbackSeconds);
    }
}
