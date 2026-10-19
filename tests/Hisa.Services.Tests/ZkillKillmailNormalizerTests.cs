using System.Globalization;
using System.Text.Json;
using Hisa.Core.Models.Intelligence;
using Hisa.Services.Intelligence;

namespace Hisa.Services.Tests;

public class ZkillKillmailNormalizerTests
{
    private static readonly DateTimeOffset ReceivedAtUtc = new(2026, 10, 18, 12, 5, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("esi")]
    [InlineData("killmail")]
    [InlineData("direct")]
    public void TryNormalize_SupportedEnvelopes_CopyKillmailIdentity(string envelope)
    {
        var normalized = Normalize(CreatePayload(envelope, "2026-10-18T12:00:00Z", totalValue: 1500.5m), sourceSequence: 44);

        Assert.Equal(1001, normalized.KillmailId);
        Assert.Equal(KillmailObservation.R2Z2SourceId, normalized.Source);
        Assert.Equal(44, normalized.SourceSequence);
        Assert.Equal(30000142, normalized.SolarSystemId);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero), normalized.OccurredAtUtc);
        Assert.Equal(TimeSpan.Zero, normalized.OccurredAtUtc.Offset);
        Assert.Equal(ReceivedAtUtc, normalized.ReceivedAtUtc);
        Assert.Equal("abc123", normalized.Hash);
        Assert.Equal(1500.5m, normalized.TotalValue);
        Assert.Equal("killmail:1001", normalized.DeduplicationKey);
    }

    [Fact]
    public void TryNormalize_CopiesVictimAndAttackers()
    {
        var normalized = Normalize(CreatePayload("esi", "2026-10-18T12:00:00Z", totalValue: 10m));

        Assert.Equal(9001, normalized.Victim.CharacterId);
        Assert.Equal(8001, normalized.Victim.CorporationId);
        Assert.Equal(7001, normalized.Victim.AllianceId);
        Assert.Equal(670, normalized.Victim.ShipTypeId);
        Assert.Equal(900, normalized.Victim.DamageTaken);
        Assert.Equal("Victim Pilot", normalized.Victim.CharacterName);

        Assert.Equal(2, normalized.Attackers.Count);
        Assert.Equal(9002, normalized.Attackers[0].CharacterId);
        Assert.Equal(8002, normalized.Attackers[0].CorporationId);
        Assert.Equal(7002, normalized.Attackers[0].AllianceId);
        Assert.Equal(587, normalized.Attackers[0].ShipTypeId);
        Assert.Equal(2456, normalized.Attackers[0].WeaponTypeId);
        Assert.True(normalized.Attackers[0].FinalBlow);
        Assert.Equal(120, normalized.Attackers[0].DamageDone);
        Assert.Equal("Attacker Pilot", normalized.Attackers[0].CharacterName);

        Assert.Null(normalized.Attackers[1].CharacterId);
        Assert.Null(normalized.Attackers[1].CharacterName);
        Assert.Equal(1000125, normalized.Attackers[1].CorporationId);
        Assert.Equal(3514, normalized.Attackers[1].ShipTypeId);
        Assert.Equal(2486, normalized.Attackers[1].WeaponTypeId);
        Assert.False(normalized.Attackers[1].FinalBlow);
        Assert.Equal(40, normalized.Attackers[1].DamageDone);
    }

    [Fact]
    public void TryNormalize_MissingAffiliation_StaysNull()
    {
        const string json = """
            {
              "killmail_id": 1001,
              "killmail_time": "2026-10-18T12:00:00Z",
              "solar_system_id": 30000142,
              "victim": { "ship_type_id": 670 },
              "attackers": [
                { "ship_type_id": 587, "damage_done": 5 }
              ]
            }
            """;

        var normalized = Normalize(json);

        Assert.Null(normalized.Victim.CharacterId);
        Assert.Null(normalized.Victim.CorporationId);
        Assert.Null(normalized.Victim.AllianceId);
        Assert.Null(normalized.Victim.CharacterName);
        Assert.Null(normalized.Victim.DamageTaken);
        Assert.Null(normalized.Attackers[0].CharacterId);
        Assert.Null(normalized.Attackers[0].CorporationId);
        Assert.Null(normalized.Attackers[0].AllianceId);
        Assert.Null(normalized.Attackers[0].FinalBlow);
        Assert.Null(normalized.Attackers[0].CharacterName);
        Assert.Null(normalized.Attackers[0].WeaponTypeId);
        Assert.Equal(587, normalized.Attackers[0].ShipTypeId);
    }

    [Fact]
    public void TryNormalize_MissingIskValue_IsNullRatherThanZero()
    {
        var missing = Normalize(CreatePayload("esi", "2026-10-18T12:00:00Z", totalValue: null));
        var zero = Normalize(CreatePayload("esi", "2026-10-18T12:00:00Z", totalValue: 0m));

        Assert.Null(missing.TotalValue);
        Assert.Equal(0m, zero.TotalValue);
    }

    [Fact]
    public void TryNormalize_StringIdentifiers_AreAccepted()
    {
        const string json = """
            {
              "killmail_id": "1001",
              "killmail_time": "2026-10-18T12:00:00Z",
              "solar_system_id": "30000142",
              "victim": { "ship_type_id": "670" }
            }
            """;

        var normalized = Normalize(json);

        Assert.Equal(1001, normalized.KillmailId);
        Assert.Equal(30000142, normalized.SolarSystemId);
        Assert.Equal(670, normalized.Victim.ShipTypeId);
    }

    [Fact]
    public void TryNormalize_SameKillmail_HasStableDeduplicationKey()
    {
        var json = CreatePayload("direct", "2026-10-18T12:00:00Z", totalValue: 10m);
        var first = Normalize(json, sourceSequence: 1, receivedAtUtc: ReceivedAtUtc);
        var second = Normalize(json, sourceSequence: 9, receivedAtUtc: ReceivedAtUtc.AddMinutes(30));

        Assert.Equal(first.DeduplicationKey, second.DeduplicationKey);
        Assert.Equal("killmail:1001", first.DeduplicationKey);
    }

    [Fact]
    public void TryNormalize_ReceivedTimestamp_IsStoredInUtc()
    {
        var received = new DateTimeOffset(2026, 10, 18, 14, 5, 0, TimeSpan.FromHours(2));
        var normalized = Normalize(CreatePayload("direct", "2026-10-18T12:00:00Z", totalValue: 1m), receivedAtUtc: received);

        Assert.Equal(TimeSpan.Zero, normalized.ReceivedAtUtc.Offset);
        Assert.Equal(received.ToUniversalTime(), normalized.ReceivedAtUtc);
    }

    [Fact]
    public void TryNormalize_ZeroCombatValues_ArePreserved()
    {
        const string json = """
            {
              "killmail_id": 1001,
              "killmail_time": "2026-10-18T12:00:00Z",
              "solar_system_id": 30000142,
              "victim": { "ship_type_id": 670, "damage_taken": 0 },
              "attackers": [ { "weapon_type_id": 0, "damage_done": 0 } ]
            }
            """;

        var normalized = Normalize(json);

        Assert.Equal(0, normalized.Victim.DamageTaken);
        Assert.Equal(0, normalized.Attackers[0].WeaponTypeId);
        Assert.Equal(0, normalized.Attackers[0].DamageDone);
    }

    [Theory]
    [InlineData("", KillmailNormalizationRejection.MalformedJson)]
    [InlineData("{", KillmailNormalizationRejection.MalformedJson)]
    [InlineData("null", KillmailNormalizationRejection.InvalidEnvelope)]
    [InlineData("[]", KillmailNormalizationRejection.InvalidEnvelope)]
    [InlineData("{}", KillmailNormalizationRejection.MissingKillmailId)]
    [InlineData("""{"esi":[]}""", KillmailNormalizationRejection.InvalidEnvelope)]
    [InlineData("""{"killmail_id":0,"killmail_time":"2026-10-18T12:00:00Z","solar_system_id":30000142,"victim":{"ship_type_id":670}}""", KillmailNormalizationRejection.MissingKillmailId)]
    [InlineData("""{"killmail_id":1001,"solar_system_id":30000142,"victim":{"ship_type_id":670}}""", KillmailNormalizationRejection.MissingTimestamp)]
    [InlineData("""{"killmail_id":1001,"killmail_time":"2026-10-18T12:00:00Z","victim":{"ship_type_id":670}}""", KillmailNormalizationRejection.MissingSolarSystem)]
    [InlineData("""{"killmail_id":1001,"killmail_time":"2026-10-18T12:00:00Z","solar_system_id":30000142}""", KillmailNormalizationRejection.MissingVictim)]
    [InlineData("""{"killmail_id":1001,"killmail_time":"2026-10-18T12:00:00Z","solar_system_id":30000142,"victim":{}}""", KillmailNormalizationRejection.MissingVictimShip)]
    public void TryNormalize_MalformedOrIncompletePayload_ReturnsFalse(string json, KillmailNormalizationRejection expected)
    {
        var normalized = ZkillKillmailNormalizer.TryNormalize(json, 1, ReceivedAtUtc, out var observation, out var rejection);

        Assert.False(normalized);
        Assert.Null(observation);
        Assert.Equal(expected, rejection);
    }

    [Fact]
    public void TryNormalize_NonObjectElement_ReturnsFalse()
    {
        using var document = JsonDocument.Parse("[]");

        var normalized = ZkillKillmailNormalizer.TryNormalize(document.RootElement, 1, ReceivedAtUtc, out var observation, out var rejection);

        Assert.False(normalized);
        Assert.Null(observation);
        Assert.Equal(KillmailNormalizationRejection.InvalidEnvelope, rejection);
    }

    private static KillmailObservation Normalize(
        string json,
        long? sourceSequence = 7,
        DateTimeOffset? receivedAtUtc = null)
    {
        var normalized = ZkillKillmailNormalizer.TryNormalize(
            json,
            sourceSequence,
            receivedAtUtc ?? ReceivedAtUtc,
            out var observation,
            out var rejection);

        Assert.True(normalized);
        Assert.NotNull(observation);
        Assert.Null(rejection);
        return observation;
    }

    private static string CreatePayload(string envelope, string killmailTime, decimal? totalValue)
    {
        var zkb = totalValue is null
            ? string.Empty
            : $",\"zkb\":{{\"totalValue\":{totalValue.Value.ToString(CultureInfo.InvariantCulture)}}}";
        var body = $$"""
            {
              "killmail_time": "{{killmailTime}}",
              "solar_system_id": 30000142,
              "victim": {
                "character_id": 9001,
                "corporation_id": 8001,
                "alliance_id": 7001,
                "ship_type_id": 670,
                "damage_taken": 900,
                "character_name": "Victim Pilot"
              },
              "attackers": [
                {
                  "character_id": 9002,
                  "corporation_id": 8002,
                  "alliance_id": 7002,
                  "ship_type_id": 587,
                  "weapon_type_id": 2456,
                  "final_blow": true,
                  "damage_done": 120,
                  "character_name": "Attacker Pilot"
                },
                {
                  "corporation_id": 1000125,
                  "ship_type_id": 3514,
                  "weapon_type_id": 2486,
                  "final_blow": false,
                  "damage_done": 40
                }
              ]
            }
            """;

        if (envelope == "direct")
        {
            return $$"""
                {
                  "killmail_id": 1001,
                  "hash": "abc123"{{zkb}},
                  "killmail_time": "{{killmailTime}}",
                  "solar_system_id": 30000142,
                  "victim": {
                    "character_id": 9001,
                    "corporation_id": 8001,
                    "alliance_id": 7001,
                    "ship_type_id": 670,
                    "damage_taken": 900,
                    "character_name": "Victim Pilot"
                  },
                  "attackers": [
                    {
                      "character_id": 9002,
                      "corporation_id": 8002,
                      "alliance_id": 7002,
                      "ship_type_id": 587,
                      "weapon_type_id": 2456,
                      "final_blow": true,
                      "damage_done": 120,
                      "character_name": "Attacker Pilot"
                    },
                    {
                      "corporation_id": 1000125,
                      "ship_type_id": 3514,
                      "weapon_type_id": 2486,
                      "final_blow": false,
                      "damage_done": 40
                    }
                  ]
                }
                """;
        }

        return $$"""
            {
              "killmail_id": 1001,
              "hash": "abc123"{{zkb}},
              "{{envelope}}": {{body}}
            }
            """;
    }
}
