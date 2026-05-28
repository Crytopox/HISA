namespace Hisa.Core.Models;

public enum MapViewMode
{
    Universe = 0,
    UniverseRegions = 1,
    Region = 2
}

public enum MapCoordinateMode
{
    ThreeDProjectedXZ = 0,
    SdePlanarXY = 1
}

public enum MapNodeColorMode
{
    None = 0,
    Security = 1,
    Region = 2,
    Star = 3,
    NullsecTrueSec = 4,
    JoveObservatory = 5,
    IceBelts = 6,
    Storms = 7,
    Wormholes = 8,
    SovUpgrades = 9,
    Incursions = 10
}

public sealed class MapGraph
{
    public required IReadOnlyList<MapNode> Nodes { get; init; }
    public required IReadOnlyList<MapLink> Links { get; init; }
}

public sealed class MapNode
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public double? Security { get; init; }
    public int? SunTypeId { get; init; }
    public string? StarTypeName { get; init; }
    public string? SpectralClass { get; init; }
    public bool HasJoveObservatory { get; init; }
    public int IceFieldCount { get; init; }
    public int? RegionId { get; init; }
    public string? RegionName { get; init; }
    public int? ConstellationId { get; init; }
    public string? ConstellationName { get; init; }
    public IReadOnlyList<StormEffect> StormEffects { get; init; } = [];
    public IReadOnlyList<HubWormholeConnection> HubWormholeConnections { get; init; } = [];
    public IReadOnlyList<SovUpgradeEntry> SovUpgrades { get; init; } = [];
    public bool HasActiveIncursion { get; init; }
}

public sealed class SovUpgradeEntry
{
    public required string UpgradeName { get; init; }
    public int Tier { get; init; }
}

public enum SovImportMode
{
    Replace = 0,
    UpdateOnChange = 1,
    Append = 2
}

public sealed class SovImportResult
{
    public required int ParsedSystems { get; init; }
    public required int ParsedUpgrades { get; init; }
    public required int TotalSystemsAfterImport { get; init; }
}

public sealed class SovSystemUpgradeRecord
{
    public required int SolarSystemId { get; init; }
    public required string SolarSystemName { get; init; }
    public required IReadOnlyList<SovUpgradeEntry> Upgrades { get; init; }
}

public enum MapSearchKind
{
    Region = 0,
    Constellation = 1,
    SolarSystem = 2
}

public sealed class MapSearchCandidate
{
    public required MapSearchKind Kind { get; init; }
    public required string Name { get; init; }
    public int? RegionId { get; init; }
    public int? ConstellationId { get; init; }
    public long? SolarSystemId { get; init; }

    public override string ToString()
    {
        var prefix = Kind switch
        {
            MapSearchKind.Region => "Region",
            MapSearchKind.Constellation => "Constellation",
            MapSearchKind.SolarSystem => "System",
            _ => "Result"
        };

        return $"{prefix}: {Name}";
    }
}

public sealed class MapSearchFocus
{
    public MapSearchKind Kind { get; init; }
    public int? RegionId { get; init; }
    public int? ConstellationId { get; init; }
    public long? SolarSystemId { get; init; }
}

public sealed class MapViewportState
{
    public required double Zoom { get; init; }
    public required double PanOffsetX { get; init; }
    public required double PanOffsetY { get; init; }
}

public sealed class MapLink
{
    public required long FromId { get; init; }
    public required long ToId { get; init; }
}

public sealed class RegionOption
{
    public required int RegionId { get; init; }
    public required string RegionName { get; init; }
    public RegionOptionKind Kind { get; init; } = RegionOptionKind.Regular;
    public bool IsHeader { get; init; }

    public override string ToString() => RegionName;
}

public enum RegionOptionKind
{
    Regular = 0,
    Combined = 1,
    Custom = 2
}
