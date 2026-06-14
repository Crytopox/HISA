namespace Hisa.Core.Models;

public enum MiningStatsRangeMode
{
    CurrentSession,
    Last1Hour,
    Last2Hours,
    Last4Hours,
    Last6Hours,
    Last8Hours,
    Last12Hours,
    Last24Hours,
    Last3Days,
    Last7Days
}

public enum MiningOverlayRangeMode
{
    UseSelectedRange,
    Rolling5Minutes,
    Rolling10Minutes,
    Rolling15Minutes,
    Rolling30Minutes
}

public enum MiningLogEventKind
{
    Yield,
    Residue,
    SiteEfficiencyChanged
}

public sealed class MiningLogEvent
{
    public required MiningLogEventKind Kind { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public string OreName { get; init; } = string.Empty;
    public long Units { get; init; }
    public bool IsCriticalBonus { get; init; }
    public int? EfficiencyPercent { get; init; }
}

public sealed class MiningOreTotals
{
    public required string OreName { get; init; }
    public long MinedUnits { get; init; }
    public long BonusUnits { get; init; }
    public long WasteUnits { get; init; }
    public DateTime LastMinedUtc { get; init; }
    public int? LastKnownEfficiencyPercent { get; init; }
}

public sealed class MiningSessionSnapshot
{
    public required int CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public required DateTime SessionStartedUtc { get; init; }
    public required DateTime FirstActivityUtc { get; init; }
    public required DateTime LastActivityUtc { get; init; }
    public required string SourceFilePath { get; init; }
    public int? CurrentEfficiencyPercent { get; init; }
    public required IReadOnlyList<MiningOreTotals> Ores { get; init; }
}

public sealed class MiningCharacterStatsSnapshot
{
    public required int CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public required DateTime SessionStartedUtc { get; init; }
    public required DateTime FirstActivityUtc { get; init; }
    public required DateTime LastActivityUtc { get; init; }
    public required string SourceFilePath { get; init; }
    public string PrimaryOreName { get; init; } = string.Empty;
    public int? CurrentEfficiencyPercent { get; init; }
    public double TotalRegularYieldVolumeM3 { get; init; }
    public double TotalCritVolumeM3 { get; init; }
    public double TotalMinedVolumeM3 { get; init; }
    public double TotalWasteVolumeM3 { get; init; }
    public double TotalMiningVolumeM3 { get; init; }
    public double YieldPercent { get; init; }
    public double CritPercent { get; init; }
    public double WastePercent { get; init; }
    public double EfficiencyPercent { get; init; }
    public double MiningRateM3PerHour { get; init; }
    public double WasteRateM3PerHour { get; init; }
    public double DepletionRateM3PerHour { get; init; }
    public double TotalMiningRateM3PerHour { get; init; }
    public decimal TotalEstimatedIsk { get; init; }
    public decimal TotalWasteEstimatedIsk { get; init; }
    public decimal EstimatedIskPerHour { get; init; }
    public decimal WasteEstimatedIskPerHour { get; init; }
    public double EfficiencyRatio { get; init; }
    public required IReadOnlyList<MiningOreStatsSnapshot> Ores { get; init; }
}

public sealed class MiningOreStatsSnapshot
{
    public required string OreName { get; init; }
    public long MinedUnits { get; init; }
    public long BonusUnits { get; init; }
    public long WasteUnits { get; init; }
    public double VolumePerUnitM3 { get; init; }
    public decimal EstimatedIskPerUnit { get; init; }
    public double TotalRegularYieldVolumeM3 { get; init; }
    public double TotalCritVolumeM3 { get; init; }
    public double TotalMinedVolumeM3 { get; init; }
    public double TotalWasteVolumeM3 { get; init; }
    public decimal TotalEstimatedIsk { get; init; }
    public int? LastKnownEfficiencyPercent { get; init; }
}
