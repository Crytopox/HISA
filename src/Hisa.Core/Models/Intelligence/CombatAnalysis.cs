namespace Hisa.Core.Models.Intelligence;

public enum EngagementClassification { SmallEngagement, SignificantEngagement, SustainedBattle, PossibleCampActivity }
public enum EngagementConfidence { TemporalPattern, CorroboratedPvpPattern }
public sealed record CombatAnalysisEvidence(string RuleVersion, string Explanation, EngagementConfidence Strength,
    bool InputLimited, bool ParticipantRelationshipsAvailable = false);
public sealed record CombatLoss(int ShipTypeId, int Count);
public sealed record CombatEngagement(string Id, long SolarSystemId, DateTimeOffset FirstUtc, DateTimeOffset LastUtc,
    int KillmailCount, int ConfirmedPvpCount, decimal KnownIskDestroyed, int UnknownValueCount,
    EngagementClassification Classification, IReadOnlyList<CombatLoss> Ships, CombatAnalysisEvidence Evidence,
    ActivityCoverage Coverage, DateTimeOffset RevisionUtc, DateTimeOffset PartitionUtc)
{
    public IReadOnlyList<long> MemberKillmailIds { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => Classification switch
    {
        EngagementClassification.PossibleCampActivity => "Possible Camp Activity",
        EngagementClassification.SustainedBattle => "Sustained / large battle",
        EngagementClassification.SignificantEngagement => "Significant engagement",
        _ => "Small engagement"
    };
    // The late-arrival revision window follows detailed-record retention, independently of event recency.
    public bool IsPreliminary(DateTimeOffset now) => now < PartitionUtc.AddHours(48);
    public bool RawDetailsExpired(DateTimeOffset now) => FirstUtc < now.AddHours(-48);
    public bool IsCurrentCandidate(DateTimeOffset now) => Classification == EngagementClassification.PossibleCampActivity && LastUtc >= now.AddMinutes(-30) && LastUtc <= now;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Display => $"{FirstUtc:MM-dd HH:mm} UTC · {Label} · {KillmailCount} losses";
}
public sealed record CombatInput(long Id, long SystemId, DateTimeOffset OccurredUtc, int ShipTypeId, decimal? Value, bool ConfirmedPvp);
public sealed record CombatCursor(DateTimeOffset FirstUtc, string Id);
public sealed record CombatQuery(ActivityRange Range, long? SystemId = null, EngagementClassification? Classification = null,
    int MinimumKills = 2, decimal? MinimumKnownIsk = null, int Limit = 100, CombatCursor? After = null, bool BattlesOnly = false);
public sealed record CombatPage(IReadOnlyList<CombatEngagement> Events, CombatCursor? Next, bool Pending, ActivityCoverage Coverage);
public sealed record CombatSystemSummary(long SolarSystemId, int EventCount, long ClusteredLosses);
public sealed record CombatMapSnapshot(ActivityRange Range, System.Collections.Frozen.FrozenDictionary<long, byte> Markers, bool Incomplete);
public sealed record CombatDetectionOptions
{
    public TimeSpan InterKillGap { get; init; } = TimeSpan.FromMinutes(12);
    public TimeSpan MaximumEngagementDuration { get; init; } = TimeSpan.FromHours(2);
    public int SignificantLosses { get; init; } = 6;
    public int LargeBattleLosses { get; init; } = 20;
    public decimal SignificantKnownIsk { get; init; } = 1_000_000_000m;
    public TimeSpan CampDuration { get; init; } = TimeSpan.FromMinutes(45);
    public int CampIntervals { get; init; } = 4;
    public int CampPvpLosses { get; init; } = 6;
}
