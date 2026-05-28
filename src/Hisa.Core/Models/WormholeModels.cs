namespace Hisa.Core.Models;

public enum WormholeHubType
{
    Unknown = 0,
    Thera = 1,
    Turnur = 2
}

public enum HubWormholeMarkerMode
{
    Badge = 0,
    Ring = 1,
    Halo = 2
}

public sealed class HubWormholeConnection
{
    public required long SolarSystemId { get; init; }
    public required WormholeHubType HubType { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public DateTimeOffset? ReportedAtUtc { get; init; }
    public DateTimeOffset? LastUpdatedAtUtc { get; init; }
    public string? OutSignature { get; init; }
    public string? InSignature { get; init; }
    public string? MaxShipSize { get; init; }
    public long? MaxJumpMassKg { get; init; }
    public long? MaxStableMassKg { get; init; }
}

public sealed class HubWormholeSnapshot
{
    public static HubWormholeSnapshot Empty { get; } = new()
    {
        FetchedAtUtc = DateTimeOffset.MinValue,
        Source = "none",
        ConnectionsBySystemId = new Dictionary<long, IReadOnlyList<HubWormholeConnection>>()
    };

    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyDictionary<long, IReadOnlyList<HubWormholeConnection>> ConnectionsBySystemId { get; init; }
}
