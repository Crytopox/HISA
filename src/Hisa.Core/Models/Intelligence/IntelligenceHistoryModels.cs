namespace Hisa.Core.Models.Intelligence;

public static class IntelligenceCollectionGapKind
{
    public const string QueueOverflow = "QueueOverflow";
    public const string WriteFailure = "WriteFailure";
    public const string ShutdownUndelivered = "ShutdownUndelivered";
    public const string SkippedSequence = "SkippedSequence";
    public const string ObservationRejected = "ObservationRejected";
    public const string ObservationUndelivered = "ObservationUndelivered";
    public const string NormalizationRejected = "NormalizationRejected";
    public const string RecoveryUnusable = "RecoveryUnusable";
}

public sealed record IntelligenceCollectionGap
{
    public required string Kind { get; init; }
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Source { get; init; }
    public long? SourceSequence { get; init; }
    public long? KillmailId { get; init; }
    public int Count { get; init; } = 1;
    public string? Detail { get; init; }
}

public sealed record KillmailHistoryRecord
{
    public required long KillmailId { get; init; }
    public required string Source { get; init; }
    public long? SourceSequence { get; init; }
    public required long SolarSystemId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public string? Hash { get; init; }
    public int? VictimCharacterId { get; init; }
    public int? VictimCorporationId { get; init; }
    public int? VictimAllianceId { get; init; }
    public required int VictimShipTypeId { get; init; }
    public long? VictimDamageTaken { get; init; }
    public decimal? TotalValue { get; init; }
    public required int AttackerCount { get; init; }
}

public sealed record KillmailParticipantRecord
{
    public required int ParticipantIndex { get; init; }
    public int? CharacterId { get; init; }
    public int? CorporationId { get; init; }
    public int? AllianceId { get; init; }
    public int? ShipTypeId { get; init; }
    public int? WeaponTypeId { get; init; }
    public bool? FinalBlow { get; init; }
    public long? DamageDone { get; init; }
}

public sealed record KillmailHistoryDetail
{
    public required KillmailHistoryRecord Killmail { get; init; }
    public required IReadOnlyList<KillmailParticipantRecord> Participants { get; init; }
}

public readonly record struct KillmailHistoryCursor(DateTimeOffset OccurredAtUtc, long KillmailId);

public sealed record KillmailHistoryPage(
    IReadOnlyList<KillmailHistoryRecord> Records,
    KillmailHistoryCursor? NextCursor);

public sealed record KillmailWriteResult(int Inserted, int Duplicates, int Failed);

public sealed record IntelligenceHistoryHealth(
    long KillmailCount,
    long ParticipantCount,
    long CollectionGapCount,
    int SchemaVersion,
    bool WalMode,
    long DatabaseSizeBytes,
    string DatabasePath);
