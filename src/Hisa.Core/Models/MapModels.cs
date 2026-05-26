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
    public int? RegionId { get; init; }
    public string? RegionName { get; init; }
    public int? ConstellationId { get; init; }
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

    public override string ToString() => RegionName;
}
