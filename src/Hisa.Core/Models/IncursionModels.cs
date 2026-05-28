namespace Hisa.Core.Models;

public sealed class IncursionInfo
{
    public required int ConstellationId { get; init; }
    public required int FactionId { get; init; }
    public required bool HasBoss { get; init; }
    public required double Influence { get; init; }
    public required int StagingSolarSystemId { get; init; }
    public required string State { get; init; }
    public required string Type { get; init; }
    public required IReadOnlyList<int> InfestedSolarSystems { get; init; }
}

public sealed class IncursionSnapshot
{
    public static IncursionSnapshot Empty { get; } = new()
    {
        FetchedAtUtc = DateTimeOffset.MinValue,
        Source = "none",
        Incursions = [],
        ActiveSystemIds = new HashSet<int>()
    };

    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<IncursionInfo> Incursions { get; init; }
    public required IReadOnlySet<int> ActiveSystemIds { get; init; }
}

public sealed class EsiRequestMetric
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string Route { get; init; }
    public required bool FromCache { get; init; }
    public required int StatusCode { get; init; }
    public required int? ErrorLimitRemain { get; init; }
    public required int? ErrorLimitResetSeconds { get; init; }
    public required int? RateLimitRemain { get; init; }
    public required int? RateLimitResetSeconds { get; init; }
    public required string? ETag { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string Message { get; init; }
}

public sealed class EsiRateState
{
    public required DateTimeOffset LastRequestAtUtc { get; init; }
    public required DateTimeOffset? NextAllowedAtUtc { get; init; }
    public required int RequestsLast15Minutes { get; init; }
    public required int RouteTokenLimit15Minutes { get; init; }
}
