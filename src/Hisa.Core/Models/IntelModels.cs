namespace Hisa.Core.Models;

public enum IntelShipClass
{
    Unknown = 0,
    Frigate = 1,
    Destroyer = 2,
    Cruiser = 3,
    Battlecruiser = 4,
    Battleship = 5,
    Capital = 6,
    Supercapital = 7,
    Titan = 8,
    Industrial = 9,
    IndustrialCommand = 10,
    Freighter = 11,
    MiningFrigate = 12,
    MiningBarge = 13,
    Capsule = 14,
    Shuttle = 15,
    Rookie = 16
}

public enum IntelAlertType
{
    None = 0,
    Clear = 1,
    GateCamp = 2,
    Cyno = 3,
    Bubble = 4,
    Spike = 5,
    Wormhole = 6,
    Fight = 7
}

public sealed class IntelChatReport
{
    public required DateTime TimestampUtc { get; init; }
    public required string ChannelName { get; init; }
    public required string ReporterName { get; init; }
    public required string MessageText { get; init; }
    public required string SourceFilePath { get; init; }
    public required IReadOnlyList<string> Systems { get; init; }
    public required IReadOnlyList<IntelShipClass> ShipClasses { get; init; }
    public required IReadOnlyList<string> ReportedShipNames { get; init; }
    public required IReadOnlyList<int> ReportedShipTypeIds { get; init; }
    public required IReadOnlyList<IntelAlertType> Alerts { get; init; }
    public required IReadOnlyList<string> ReportedHostileNames { get; init; }
    public bool IsClear { get; init; }
    public int ReportedHostileCount { get; init; }
    public IntelKillmailDetails? Killmail { get; init; }
}

public sealed class IntelKillmailDetails
{
    public required long KillmailId { get; init; }
    public string Hash { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int? VictimCharacterId { get; init; }
    public int VictimCorporationId { get; init; }
    public int? VictimAllianceId { get; init; }
    public int? VictimShipTypeId { get; init; }
    public decimal TotalValue { get; init; }
    public string VictimName { get; init; } = string.Empty;
    public IReadOnlyList<IntelKillmailAttacker> Attackers { get; init; } = [];
}

public sealed class IntelKillmailAttacker
{
    public string Name { get; init; } = string.Empty;
    public int? CharacterId { get; init; }
    public int CorporationId { get; init; }
    public int? AllianceId { get; init; }
    public int? ShipTypeId { get; init; }
}

public sealed class IntelSystemSnapshot
{
    public required long SolarSystemId { get; init; }
    public required string SolarSystemName { get; init; }
    public required DateTime LastUpdatedUtc { get; init; }
    public required string LastChannelName { get; init; }
    public required string LastReporterName { get; init; }
    public required string LastMessageText { get; init; }
    public required IReadOnlyList<IntelShipClass> ShipClasses { get; init; }
    public required IReadOnlyList<string> ShipNames { get; init; }
    public required IReadOnlyList<int> ShipTypeIds { get; init; }
    public required IReadOnlyList<IntelAlertType> Alerts { get; init; }
    public required IReadOnlyList<string> HostilePilotNames { get; init; }
    public required IReadOnlyList<IntelRecentReport> RecentReports { get; init; }
    public int HostileScore { get; init; }
    public bool IsClear { get; init; }
}

public sealed class IntelRecentReport
{
    public required DateTime TimestampUtc { get; init; }
    public required string ReporterName { get; init; }
    public required string MessageText { get; init; }
}

public sealed class IntelMapHoverReport
{
    public required DateTime TimestampUtc { get; init; }
    public required string ReporterName { get; init; }
    public required string MessageText { get; init; }
    public required IReadOnlyList<IntelMapHoverShip> Ships { get; init; }
    public required IReadOnlyList<IntelMapHoverHostile> Hostiles { get; init; }
    public int HiddenHostileCount { get; init; }
    public int HostileCount { get; init; }
}

public sealed class IntelMapHoverKillmail
{
    public required DateTime TimestampUtc { get; init; }
    public required string MessageText { get; init; }
    public required string KillmailUrl { get; init; }
    public required string VictimName { get; init; }
    public required string VictimMembership { get; init; }
    public int? VictimCharacterId { get; init; }
    public int? VictimCorporationId { get; init; }
    public int? VictimAllianceId { get; init; }
    public required string VictimShipDisplayName { get; init; }
    public int? VictimShipTypeId { get; init; }
    public required IReadOnlyList<IntelMapHoverHostile> Attackers { get; init; }
    public int HiddenAttackerCount { get; init; }
    public required string IskLostLabel { get; init; }
}

public sealed class IntelMapHoverShip
{
    public required string ShipDisplayName { get; init; }
    public required string ShipIconKey { get; init; }
    public int? ShipTypeId { get; init; }
    public int Count { get; init; }
}

public sealed class IntelMapHoverHostile
{
    public required string Name { get; init; }
    public int? CharacterId { get; init; }
    public int? ShipTypeId { get; init; }
    public string ShipDisplayName { get; init; } = "Unknown";
    public string ShipIconKey { get; init; } = "unknown";
    public int? CorporationId { get; init; }
    public int? AllianceId { get; init; }
    public string CorporationTicker { get; init; } = string.Empty;
    public string AllianceTicker { get; init; } = string.Empty;
}
