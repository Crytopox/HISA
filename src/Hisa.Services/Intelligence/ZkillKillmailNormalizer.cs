using System.Globalization;
using System.Text.Json;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Services.Intelligence;

public static class ZkillKillmailNormalizer
{
    public static bool TryNormalize(
        string json,
        long? sourceSequence,
        DateTimeOffset receivedAtUtc,
        out KillmailObservation? observation,
        out KillmailNormalizationRejection? rejection)
    {
        observation = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            rejection = KillmailNormalizationRejection.MalformedJson;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryNormalize(document.RootElement, sourceSequence, receivedAtUtc, out observation, out rejection);
        }
        catch (JsonException)
        {
            rejection = KillmailNormalizationRejection.MalformedJson;
            return false;
        }
    }

    public static bool TryNormalize(
        JsonElement root,
        long? sourceSequence,
        DateTimeOffset receivedAtUtc,
        out KillmailObservation? observation,
        out KillmailNormalizationRejection? rejection)
    {
        observation = null;
        rejection = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            rejection = KillmailNormalizationRejection.InvalidEnvelope;
            return false;
        }

        var killmail = root.TryGetProperty("esi", out var esiKillmail)
            ? esiKillmail
            : (root.TryGetProperty("killmail", out var nestedKillmail) ? nestedKillmail : root);
        if (killmail.ValueKind != JsonValueKind.Object)
        {
            rejection = KillmailNormalizationRejection.InvalidEnvelope;
            return false;
        }

        var killmailId = ReadInt64(root, "killmail_id") ?? ReadInt64(killmail, "killmail_id");
        if (killmailId is null or <= 0)
        {
            rejection = KillmailNormalizationRejection.MissingKillmailId;
            return false;
        }

        var solarSystemId = ReadInt64(killmail, "solar_system_id");
        if (solarSystemId is null or <= 0)
        {
            rejection = KillmailNormalizationRejection.MissingSolarSystem;
            return false;
        }

        var occurredAtUtc = ReadDateTimeOffset(killmail, "killmail_time");
        if (occurredAtUtc is null)
        {
            rejection = KillmailNormalizationRejection.MissingTimestamp;
            return false;
        }

        if (!killmail.TryGetProperty("victim", out var victimNode) || victimNode.ValueKind != JsonValueKind.Object)
        {
            rejection = KillmailNormalizationRejection.MissingVictim;
            return false;
        }

        var victimShipTypeId = ReadInt32(victimNode, "ship_type_id");
        if (victimShipTypeId is null or <= 0)
        {
            rejection = KillmailNormalizationRejection.MissingVictimShip;
            return false;
        }

        var attackers = ReadAttackers(killmail);
        var hash = ReadString(root, "hash") ?? ReadString(killmail, "hash");
        observation = new KillmailObservation
        {
            KillmailId = killmailId.Value,
            Source = KillmailObservation.R2Z2SourceId,
            SourceSequence = sourceSequence,
            SolarSystemId = solarSystemId.Value,
            OccurredAtUtc = occurredAtUtc.Value,
            ReceivedAtUtc = receivedAtUtc.ToUniversalTime(),
            Victim = new KillmailVictim
            {
                CharacterId = ReadInt32(victimNode, "character_id"),
                CorporationId = ReadInt32(victimNode, "corporation_id"),
                AllianceId = ReadInt32(victimNode, "alliance_id"),
                ShipTypeId = victimShipTypeId.Value,
                DamageTaken = ReadInt64(victimNode, "damage_taken"),
                CharacterName = ReadOptionalName(victimNode, "character_name")
            },
            Attackers = attackers,
            TotalValue = ReadTotalValue(root),
            Hash = string.IsNullOrWhiteSpace(hash) ? null : hash
        };
        return true;
    }

    private static IReadOnlyList<KillmailAttacker> ReadAttackers(JsonElement killmail)
    {
        if (!killmail.TryGetProperty("attackers", out var attackersNode) || attackersNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var attackers = new List<KillmailAttacker>();
        foreach (var attacker in attackersNode.EnumerateArray())
        {
            if (attacker.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            attackers.Add(new KillmailAttacker
            {
                CharacterId = ReadInt32(attacker, "character_id"),
                CorporationId = ReadInt32(attacker, "corporation_id"),
                AllianceId = ReadInt32(attacker, "alliance_id"),
                ShipTypeId = ReadInt32(attacker, "ship_type_id"),
                WeaponTypeId = ReadInt32(attacker, "weapon_type_id"),
                FinalBlow = ReadBoolean(attacker, "final_blow"),
                DamageDone = ReadInt64(attacker, "damage_done"),
                CharacterName = ReadOptionalName(attacker, "character_name")
            });
        }

        return attackers;
    }

    private static decimal? ReadTotalValue(JsonElement root)
    {
        if (!root.TryGetProperty("zkb", out var zkb) || zkb.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadDecimal(zkb, "totalValue");
    }

    private static long? ReadInt64(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(node.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static int? ReadInt32(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(node.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(node.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = node.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return null;
        }

        return parsed.ToUniversalTime();
    }

    private static bool? ReadBoolean(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return node.GetString();
    }

    private static string? ReadOptionalName(JsonElement obj, string name)
    {
        var value = ReadString(obj, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
