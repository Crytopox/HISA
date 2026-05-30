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
    public required IReadOnlyList<IntelAlertType> Alerts { get; init; }
    public required IReadOnlyList<string> ReportedHostileNames { get; init; }
    public bool IsClear { get; init; }
    public int ReportedHostileCount { get; init; }
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
