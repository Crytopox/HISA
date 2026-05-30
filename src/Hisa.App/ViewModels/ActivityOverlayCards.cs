namespace Hisa.App;

public sealed class WormholeOverlayCard
{
    public required string SystemName { get; init; }
    public required string RegionName { get; init; }
    public required string ConstellationName { get; init; }
    public required string HubSummary { get; init; }
    public required string HubLabelColorHex { get; init; }
    public required string ShipSizeSummary { get; init; }
    public required string SignatureSummary { get; init; }
    public required string ReportedUpdatedSummary { get; init; }
    public required string ExpirySummary { get; init; }
    public required string ExpiryColorHex { get; init; }
    public required int ConnectionCount { get; init; }
    public required string AccentHex { get; init; }
}

public sealed class IncursionOverlayCard
{
    public required string StagingSystemName { get; init; }
    public required string ConstellationName { get; init; }
    public required string RegionName { get; init; }
    public required string TypeLabel { get; init; }
    public required string StateLabel { get; init; }
    public required string StateColorHex { get; init; }
    public required string FactionLabel { get; init; }
    public required string BossLabel { get; init; }
    public required string InfluenceLabel { get; init; }
    public required string AffectedSystemsLabel { get; init; }
    public required string TypeColorHex { get; init; }
    public required string BossColorHex { get; init; }
    public required string AccentHex { get; init; }
}

public sealed class StormOverlayCard
{
    public required string CenterSystemName { get; init; }
    public required string ConstellationName { get; init; }
    public required string RegionName { get; init; }
    public required string StormTypeLabel { get; init; }
    public required string StormTypeColorHex { get; init; }
    public required string CoverageSummary { get; init; }
    public required string StrengthSummary { get; init; }
    public required string ReportedSummary { get; init; }
    public required string AccentHex { get; init; }
}

public sealed class IntelOverlayCard
{
    public required DateTime SortTimestampUtc { get; init; }
    public required long SolarSystemId { get; init; }
    public required string SystemName { get; init; }
    public required string ConstellationName { get; init; }
    public required string RegionName { get; init; }
    public int? ConstellationId { get; init; }
    public int? RegionId { get; init; }
    public required string ChannelName { get; init; }
    public required string ReporterName { get; init; }
    public required string AgeSummary { get; init; }
    public required string MessageText { get; init; }
    public required string ShipClassSummary { get; init; }
    public required int HostileCount { get; init; }
    public required string AccentHex { get; init; }
}
