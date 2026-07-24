namespace Hisa.Core.Models;

public enum AlertEventType
{
    IntelReport = 0,
    Killmail = 1,
    HubWormholeSpawn = 2,
    IncursionSpawn = 3,
    StormSpawn = 4,
    IntelTextMatch = 5,
    MiningSiteReady = 6
}

public enum AlertLocationScopeMode
{
    Global = 0,
    AnyTrackedCharacter = 1,
    SpecificCharacters = 2,
    SelectedRegions = 3
}

public enum AlertDistanceMode
{
    Any = 0,
    MaxJumps = 1,
    CurrentRegion = 2
}

public enum AlertActionType
{
    ShowPopup = 0,
    PlaySound = 1
}

public sealed class AlertRule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public AlertEventType EventType { get; init; } = AlertEventType.IntelReport;
    public AlertLocationScopeMode ScopeMode { get; init; } = AlertLocationScopeMode.Global;
    public IReadOnlyList<int> CharacterIds { get; init; } = [];
    public IReadOnlyList<string> CharacterNames { get; init; } = [];
    public IReadOnlyList<int> RegionIds { get; init; } = [];
    public AlertDistanceMode DistanceMode { get; init; } = AlertDistanceMode.Any;
    public int MaxJumps { get; init; } = 0;
    public bool IncludeAnsiblexLinks { get; init; } = false;
    public bool ShowClearIntelReports { get; init; } = false;
    // Used only by IntelTextMatch rules. Literal matching is case-insensitive;
    // regex matching is opt-in so ordinary phrases stay simple and safe.
    public string TextPattern { get; init; } = string.Empty;
    public bool UseRegex { get; init; } = false;
    public int CooldownSeconds { get; init; } = 0;
    public string SoundFile { get; init; } = "default-alert.wav";
    public double SoundVolume { get; init; } = 1.0;
    public IReadOnlyList<AlertActionType> Actions { get; init; } = [];
}

public sealed class AlertSourceEvent
{
    public required AlertEventType EventType { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required long SolarSystemId { get; init; }
    public int? RegionId { get; init; }
    public long? KillmailId { get; init; }
    public bool IsClearIntelReport { get; init; }
    public string DedupeKey { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? MiningSiteSystemName { get; init; }
    public string? MiningSiteUpgradeName { get; init; }
    public int? MiningSiteTier { get; init; }
    public DateTime? MiningSiteReadyAtUtc { get; init; }
    public bool MiningSiteWasOverdue { get; init; }
}

public sealed class AlertTriggered
{
    public required string RuleId { get; init; }
    public required string RuleName { get; init; }
    public required AlertSourceEvent SourceEvent { get; init; }
    public required DateTime TriggeredAtUtc { get; init; }
    public required IReadOnlyList<AlertActionType> Actions { get; init; }
    public required string SoundFile { get; init; }
    public double SoundVolume { get; init; } = 1.0;
}

public sealed class AlertEvaluationRequest
{
    public required IReadOnlyList<AlertRule> Rules { get; init; }
    public required AlertSourceEvent SourceEvent { get; init; }
    public required MapGraph? Graph { get; init; }
    public required IReadOnlyDictionary<int, long> CharacterLocationsByCharacterId { get; init; }
    public IReadOnlyList<AnsiblexLinkEntry> AnsiblexLinks { get; init; } = [];
    public double AnsiblexCostMultiplier { get; init; } = 1.0;
}
