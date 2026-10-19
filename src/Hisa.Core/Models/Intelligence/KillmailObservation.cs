namespace Hisa.Core.Models.Intelligence;

public sealed record KillmailVictim
{
    public int? CharacterId { get; init; }
    public int? CorporationId { get; init; }
    public int? AllianceId { get; init; }
    public required int ShipTypeId { get; init; }
    public long? DamageTaken { get; init; }
    public string? CharacterName { get; init; }
}

public sealed record KillmailAttacker
{
    public int? CharacterId { get; init; }
    public int? CorporationId { get; init; }
    public int? AllianceId { get; init; }
    public int? ShipTypeId { get; init; }
    public int? WeaponTypeId { get; init; }
    public bool? FinalBlow { get; init; }
    public long? DamageDone { get; init; }
    public string? CharacterName { get; init; }
}

public sealed record KillmailObservation
{
    public const string R2Z2SourceId = "R2Z2";

    public required long KillmailId { get; init; }
    public required string Source { get; init; }
    public long? SourceSequence { get; init; }
    public required long SolarSystemId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required KillmailVictim Victim { get; init; }
    public required IReadOnlyList<KillmailAttacker> Attackers { get; init; }
    public decimal? TotalValue { get; init; }
    public string? Hash { get; init; }

    public string DeduplicationKey => $"killmail:{KillmailId}";
}
