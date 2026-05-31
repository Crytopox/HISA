using System.Text.Json.Serialization;

namespace Hisa.Esi.Models;

internal sealed class EsiSystemKillDto
{
    [JsonPropertyName("system_id")]
    public required int SolarSystemId { get; init; }

    [JsonPropertyName("ship_kills")]
    public required int ShipKills { get; init; }

    [JsonPropertyName("pod_kills")]
    public required int PodKills { get; init; }

    [JsonPropertyName("npc_kills")]
    public required int NpcKills { get; init; }
}

internal sealed class EsiSystemJumpDto
{
    [JsonPropertyName("system_id")]
    public required int SolarSystemId { get; init; }

    [JsonPropertyName("ship_jumps")]
    public required int ShipJumps { get; init; }
}
