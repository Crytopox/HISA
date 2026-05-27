namespace Hisa.Core.Models;

public enum StormType
{
    Unknown = 0,
    Electrical = 1,
    Gamma = 2,
    Exotic = 3,
    Plasma = 4
}

public enum StormStrength
{
    Weak = 0,
    Strong = 1,
    Center = 2
}

public sealed class StormCenter
{
    public required long SolarSystemId { get; init; }
    public required StormType Type { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset? ReportedAtUtc { get; init; }
}

public sealed class StormEffect
{
    public required long CenterSolarSystemId { get; init; }
    public required StormType Type { get; init; }
    public required StormStrength Strength { get; init; }
}

public sealed class StormSnapshot
{
    public static StormSnapshot Empty { get; } = new()
    {
        FetchedAtUtc = DateTimeOffset.MinValue,
        Source = "none",
        Centers = [],
        EffectsBySystemId = new Dictionary<long, IReadOnlyList<StormEffect>>()
    };

    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<StormCenter> Centers { get; init; }
    public required IReadOnlyDictionary<long, IReadOnlyList<StormEffect>> EffectsBySystemId { get; init; }
}
