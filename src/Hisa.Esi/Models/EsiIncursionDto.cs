using System.Text.Json.Serialization;

namespace Hisa.Esi.Models;

internal sealed class EsiIncursionDto
{
    [JsonPropertyName("constellation_id")]
    public int ConstellationId { get; init; }

    [JsonPropertyName("faction_id")]
    public int FactionId { get; init; }

    [JsonPropertyName("has_boss")]
    public bool HasBoss { get; init; }

    [JsonPropertyName("influence")]
    public double Influence { get; init; }

    [JsonPropertyName("staging_solar_system_id")]
    public int StagingSolarSystemId { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("infested_solar_systems")]
    public List<int> InfestedSolarSystems { get; init; } = [];
}
