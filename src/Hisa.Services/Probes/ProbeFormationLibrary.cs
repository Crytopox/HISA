using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.Services.Probes;

public sealed class ProbeFormationLibrary(ISettingsService settings) : IProbeFormationLibrary
{
    private const string Key = "probes.library.v1";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, MaxDepth = 32 };
    public async Task<FormationLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        var library = await settings.GetAsync<FormationLibrary>(Key, cancellationToken) ?? new(1, "meters", []);
        Validate(library); return library;
    }
    public Task SaveAsync(FormationLibrary library, CancellationToken cancellationToken = default) { Validate(library); return settings.SetAsync(Key, library, cancellationToken); }
    public string Export(IEnumerable<ProbeFormation> formations)
    {
        var library = new FormationLibrary(1, "meters", formations.ToArray()); Validate(library);
        return JsonSerializer.Serialize(library, Json);
    }
    public FormationLibrary Import(string json, FormationLibrary current, FormationImportAction conflicts)
    {
        if (json.Length > 8 * 1024 * 1024) throw new ArgumentException("Formation import is too large.");
        var incoming = JsonSerializer.Deserialize<FormationLibrary>(json, Json) ?? throw new ArgumentException("Empty formation file.");
        Validate(incoming); Validate(current);
        var list = current.Formations.ToList();
        foreach (var f in incoming.Formations)
        {
            var ix = list.FindIndex(e => string.Equals(e.Name, f.Name, StringComparison.OrdinalIgnoreCase));
            if (ix >= 0 && conflicts == FormationImportAction.Skip) continue;
            if (ix >= 0 && conflicts == FormationImportAction.Replace) list[ix] = f with { Id = list[ix].Id };
            else {
                var name = f.Name; var suffix = 2;
                while (list.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"{f.Name} ({suffix++})";
                var entry = f with { Id = Guid.NewGuid(), Name = name }; ProbeFormationMath.Validate(entry); list.Add(entry);
            }
        }
        var result = current with { Formations = list.ToArray() }; Validate(result); return result;
    }
    private static void Validate(FormationLibrary library)
    {
        if (library.Version != 1 || library.Units != "meters" || library.Formations is null || library.Formations.Count > 4096)
            throw new ArgumentException("Unsupported formation library version, units, or size.");
        if (library.Formations.Any(f => f is null || f.Id == Guid.Empty) || library.Formations.Select(f => f.Id).Distinct().Count() != library.Formations.Count)
            throw new ArgumentException("Formation IDs must be unique and nonempty.");
        foreach (var f in library.Formations) ProbeFormationMath.Validate(f);
    }
}
