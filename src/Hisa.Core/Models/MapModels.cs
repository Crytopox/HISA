using System.ComponentModel;
using System.Runtime.CompilerServices;

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
    Hostiles = 11,
    Security = 1,
    Region = 2,
    Star = 3,
    NullsecTrueSec = 4,
    JoveObservatory = 5,
    IceBelts = 6,
    Storms = 7,
    Wormholes = 8,
    SovUpgrades = 9,
    Incursions = 10,
    SystemJumps = 12,
    ShipKills = 13,
    PodKills = 14,
    NpcKills = 15
}

public sealed class HostileColorSettings
{
    public int LowMaxHostiles { get; init; } = 5;
    public int MediumMaxHostiles { get; init; } = 15;
    public int HighMaxHostiles { get; init; } = 25;
    public string LowColorHex { get; init; } = "#E6D86C";
    public string MediumColorHex { get; init; } = "#EE8639";
    public string HighColorHex { get; init; } = "#D90F13";
    public string AboveHighColorHex { get; init; } = "#DD008C";
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
    public double? PositionX { get; init; }
    public double? PositionY { get; init; }
    public double? PositionZ { get; init; }
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
    public int SystemJumps { get; init; }
    public int ShipKills { get; init; }
    public int PodKills { get; init; }
    public int NpcKills { get; init; }
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

public sealed class AnsiblexLinkEntry
{
    public required int FromSolarSystemId { get; init; }
    public required int ToSolarSystemId { get; init; }
}

public sealed class AnsiblexImportResult
{
    public required int ParsedLinks { get; init; }
    public required int TotalLinksAfterImport { get; init; }
    public required int DuplicateOrInvalidLinksSkipped { get; init; }
    public required int UnresolvedSystemNamesCount { get; init; }
    public required IReadOnlyList<string> UnresolvedSystemNames { get; init; }
}

public sealed class AnsiblexLinkRecord
{
    public required int FromSolarSystemId { get; init; }
    public required string FromSolarSystemName { get; init; }
    public required int ToSolarSystemId { get; init; }
    public required string ToSolarSystemName { get; init; }
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
    public string? RegionName { get; init; }
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

        return Kind switch
        {
            MapSearchKind.SolarSystem when !string.IsNullOrWhiteSpace(RegionName) => $"{prefix}: {Name} | {RegionName}",
            MapSearchKind.Constellation when !string.IsNullOrWhiteSpace(RegionName) => $"{prefix}: {Name} | {RegionName}",
            _ => $"{prefix}: {Name}"
        };
    }
}

public sealed class MapSearchFocus
{
    public MapSearchKind Kind { get; init; }
    public int? RegionId { get; init; }
    public int? ConstellationId { get; init; }
    public long? SolarSystemId { get; init; }
    public bool PreferPreserveZoom { get; init; }
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

public sealed class MapSystemMetadata
{
    public required long SolarSystemId { get; init; }
    public required string SolarSystemName { get; init; }
    public int? ConstellationId { get; init; }
    public string? ConstellationName { get; init; }
    public int? RegionId { get; init; }
    public string? RegionName { get; init; }
}

public sealed class MapSystemPosition
{
    public required long SolarSystemId { get; init; }
    public required string SolarSystemName { get; init; }
    public string? ConstellationName { get; init; }
    public string? RegionName { get; init; }
    public required double PositionX { get; init; }
    public required double PositionY { get; init; }
    public required double PositionZ { get; init; }
}

public sealed class JumpRangeOriginDisplay
{
    public required long NodeId { get; init; }
    public required string SystemName { get; init; }
    public required double RangeLy { get; init; }
    public required uint ColorArgb { get; init; }
    public required string ColorHex { get; init; }
}

public sealed class JumpRangeOriginSettings
{
    public required long SolarSystemId { get; init; }
    public required string SolarSystemName { get; init; }
    public required double PositionX { get; init; }
    public required double PositionY { get; init; }
    public required double PositionZ { get; init; }
    public required double RangeLy { get; init; }
}

public sealed class JumpRangeDistanceDisplay
{
    public required long OriginNodeId { get; init; }
    public required string OriginSystemName { get; init; }
    public required double DistanceLy { get; init; }
    public required double MaxLy { get; init; }
    public required bool IsInRange { get; init; }
}

public sealed class RegionOption : INotifyPropertyChanged
{
    private bool _isFavorite;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required int RegionId { get; init; }
    public required string RegionName { get; init; }
    public RegionOptionKind Kind { get; init; } = RegionOptionKind.Regular;
    public bool IsHeader { get; init; }
    public bool CanToggleFavorite => !IsHeader;
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string FavoriteButtonToolTip => IsFavorite ? "Remove from favorites" : "Add to favorites";
    public string FavoriteGlyphColor => IsFavorite ? "#E7C85A" : "#8FA5C4";
    public int FavoriteGlyphFontSize => IsFavorite ? 14 : 12;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteButtonToolTip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteGlyphColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteGlyphFontSize)));
        }
    }

    public override string ToString() => RegionName;
}

public enum RegionOptionKind
{
    Regular = 0,
    Combined = 1,
    Custom = 2
}
