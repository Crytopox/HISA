using System.Text.Json;

namespace Hisa.Core.Models;

public static class EveUniverseIdsParsing
{
    public static long? MatchCharacterId(string? json, string? characterName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(characterName))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return MatchCharacterId(document.RootElement, characterName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static long? MatchCharacterId(JsonElement root, string characterName)
    {
        var name = characterName.Trim();
        if (name.Length == 0 || root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("characters", out var characters) ||
            characters.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in characters.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idProp) ||
                !item.TryGetProperty("name", out var nameProp))
                continue;
            var id = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64() : 0;
            var resolved = nameProp.GetString();
            if (id <= 0 || string.IsNullOrWhiteSpace(resolved)) continue;
            if (resolved.Equals(name, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return null;
    }
}
