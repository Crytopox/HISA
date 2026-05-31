namespace Hisa.Core.Models;

public sealed class SystemKillInfo
{
    public required int SolarSystemId { get; init; }
    public required int ShipKills { get; init; }
    public required int PodKills { get; init; }
    public required int NpcKills { get; init; }
}

public sealed class SystemJumpInfo
{
    public required int SolarSystemId { get; init; }
    public required int ShipJumps { get; init; }
}

public sealed class SystemActivitySnapshot
{
    public static SystemActivitySnapshot Empty { get; } = new()
    {
        FetchedAtUtc = DateTimeOffset.MinValue,
        Source = "none",
        KillsBySystemId = new Dictionary<int, SystemKillInfo>(),
        JumpsBySystemId = new Dictionary<int, SystemJumpInfo>()
    };

    public required DateTimeOffset FetchedAtUtc { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyDictionary<int, SystemKillInfo> KillsBySystemId { get; init; }
    public required IReadOnlyDictionary<int, SystemJumpInfo> JumpsBySystemId { get; init; }
}
