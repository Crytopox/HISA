using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;

namespace Hisa.Services;

public sealed class SovUpgradeStateService : ISovUpgradeStateService
{
    private const string SovDataSettingsKey = "Map.SovUpgrades";
    private readonly ISettingsService _settingsService;
    private readonly ISdeDatabase _sdeDatabase;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private Dictionary<int, List<SovUpgradeEntry>> _currentBySystemId = [];

    public SovUpgradeStateService(ISettingsService settingsService, ISdeDatabase sdeDatabase)
    {
        _settingsService = settingsService;
        _sdeDatabase = sdeDatabase;
    }

    public event EventHandler? SnapshotUpdated;
    public IReadOnlyDictionary<int, IReadOnlyList<SovUpgradeEntry>> CurrentBySystemId =>
        _currentBySystemId.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<SovUpgradeEntry>)kvp.Value);

    public async Task<IReadOnlyList<SovSystemUpgradeRecord>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<int, List<SovUpgradeEntry>> snapshot;
        await _sync.WaitAsync(cancellationToken);
        try
        {
            snapshot = _currentBySystemId.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(x => new SovUpgradeEntry { UpgradeName = x.UpgradeName, Tier = x.Tier }).ToList());
        }
        finally
        {
            _sync.Release();
        }

        if (snapshot.Count == 0)
        {
            return [];
        }

        var nameById = await ResolveSystemNamesAsync(snapshot.Keys, cancellationToken);
        return snapshot
            .Select(kvp => new SovSystemUpgradeRecord
            {
                SolarSystemId = kvp.Key,
                SolarSystemName = nameById.TryGetValue(kvp.Key, out var name) ? name : kvp.Key.ToString(),
                Upgrades = kvp.Value
            })
            .OrderBy(x => x.SolarSystemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _currentBySystemId = await LoadFromSettingsAsync(cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<SovImportResult> ImportFromTextAsync(string rawText, SovImportMode mode, CancellationToken cancellationToken = default)
    {
        var parsed = ParseInput(rawText);
        var nameToId = await ResolveSystemIdsAsync(parsed.Keys, cancellationToken);
        var parsedById = new Dictionary<int, List<SovUpgradeEntry>>();

        foreach (var kvp in parsed)
        {
            if (!nameToId.TryGetValue(kvp.Key, out var id))
            {
                continue;
            }

            parsedById[id] = kvp.Value;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            Dictionary<int, List<SovUpgradeEntry>> merged = mode switch
            {
                SovImportMode.Replace => parsedById,
                SovImportMode.UpdateOnChange => MergeUpdate(_currentBySystemId, parsedById),
                SovImportMode.Append => MergeAppend(_currentBySystemId, parsedById),
                _ => parsedById
            };

            _currentBySystemId = merged;
            await SaveToSettingsAsync(_currentBySystemId, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }

        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
        return new SovImportResult
        {
            ParsedSystems = parsedById.Count,
            ParsedUpgrades = parsedById.Values.Sum(v => v.Count),
            TotalSystemsAfterImport = _currentBySystemId.Count
        };
    }

    public async Task AddOrUpdateUpgradeAsync(string systemName, string upgradeName, int tier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemName) || string.IsNullOrWhiteSpace(upgradeName))
        {
            return;
        }

        var nameToId = await ResolveSystemIdsAsync([systemName.Trim()], cancellationToken);
        if (!nameToId.TryGetValue(systemName.Trim(), out var systemId))
        {
            return;
        }

        tier = Math.Clamp(tier, 1, 3);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!_currentBySystemId.TryGetValue(systemId, out var list))
            {
                list = [];
                _currentBySystemId[systemId] = list;
            }

            var existing = list.FindIndex(x => x.UpgradeName.Equals(upgradeName.Trim(), StringComparison.OrdinalIgnoreCase));
            var entry = new SovUpgradeEntry { UpgradeName = upgradeName.Trim(), Tier = tier };
            if (existing >= 0)
            {
                list[existing] = entry;
            }
            else
            {
                list.Add(entry);
            }

            await SaveToSettingsAsync(_currentBySystemId, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }

        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveSystemAsync(string systemName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemName))
        {
            return;
        }

        var nameToId = await ResolveSystemIdsAsync([systemName.Trim()], cancellationToken);
        if (!nameToId.TryGetValue(systemName.Trim(), out var systemId))
        {
            return;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_currentBySystemId.Remove(systemId))
            {
                await SaveToSettingsAsync(_currentBySystemId, cancellationToken);
                SnapshotUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _sync.Release();
        }
    }

    private static Dictionary<int, List<SovUpgradeEntry>> MergeUpdate(
        Dictionary<int, List<SovUpgradeEntry>> current,
        Dictionary<int, List<SovUpgradeEntry>> incoming)
    {
        var merged = current.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => new SovUpgradeEntry { UpgradeName = x.UpgradeName, Tier = x.Tier }).ToList());
        foreach (var kvp in incoming)
        {
            merged[kvp.Key] = kvp.Value;
        }

        return merged;
    }

    private static Dictionary<int, List<SovUpgradeEntry>> MergeAppend(
        Dictionary<int, List<SovUpgradeEntry>> current,
        Dictionary<int, List<SovUpgradeEntry>> incoming)
    {
        var merged = current.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(x => new SovUpgradeEntry { UpgradeName = x.UpgradeName, Tier = x.Tier }).ToList());
        foreach (var kvp in incoming)
        {
            if (!merged.TryGetValue(kvp.Key, out var list))
            {
                merged[kvp.Key] = kvp.Value;
                continue;
            }

            foreach (var entry in kvp.Value)
            {
                if (list.Any(x => x.UpgradeName.Equals(entry.UpgradeName, StringComparison.OrdinalIgnoreCase) && x.Tier == entry.Tier))
                {
                    continue;
                }

                list.Add(entry);
            }
        }

        return merged;
    }

    private static Dictionary<string, List<SovUpgradeEntry>> ParseInput(string rawText)
    {
        var result = new Dictionary<string, List<SovUpgradeEntry>>(StringComparer.OrdinalIgnoreCase);
        var lines = rawText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var parts = line.Split("<-", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var systemName = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(systemName))
            {
                continue;
            }

            var upgrades = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseUpgrade)
                .Where(x => x is not null)
                .Select(x => x!)
                .ToList();

            if (upgrades.Count == 0)
            {
                continue;
            }

            result[systemName] = upgrades;
        }

        return result;
    }

    private static SovUpgradeEntry? ParseUpgrade(string raw)
    {
        var text = raw.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var tier = 1;
        var lastSpace = text.LastIndexOf(' ');
        if (lastSpace > 0 && int.TryParse(text[(lastSpace + 1)..], out var parsedTier))
        {
            tier = Math.Clamp(parsedTier, 1, 3);
            text = text[..lastSpace].Trim();
        }

        return string.IsNullOrWhiteSpace(text)
            ? null
            : new SovUpgradeEntry { UpgradeName = text, Tier = tier };
    }

    private async Task<Dictionary<string, int>> ResolveSystemIdsAsync(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        var list = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
        {
            return [];
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        const int chunkSize = 200;
        for (var offset = 0; offset < list.Count; offset += chunkSize)
        {
            var chunk = list.Skip(offset).Take(chunkSize).ToList();
            var cmd = connection.CreateCommand();
            var parameters = new List<string>(chunk.Count);
            for (var i = 0; i < chunk.Count; i++)
            {
                var p = $"$n{i}";
                parameters.Add(p);
                cmd.Parameters.AddWithValue(p, chunk[i]);
            }

            cmd.CommandText = $"""
                SELECT solarSystemID, solarSystemName
                FROM mapSolarSystems
                WHERE solarSystemName IN ({string.Join(", ", parameters)});
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetString(1)] = reader.GetInt32(0);
            }
        }

        return result;
    }

    private async Task<Dictionary<int, string>> ResolveSystemNamesAsync(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var result = new Dictionary<int, string>();

        const int chunkSize = 300;
        for (var offset = 0; offset < list.Count; offset += chunkSize)
        {
            var chunk = list.Skip(offset).Take(chunkSize).ToList();
            var cmd = connection.CreateCommand();
            var parameters = new List<string>(chunk.Count);
            for (var i = 0; i < chunk.Count; i++)
            {
                var p = $"$id{i}";
                parameters.Add(p);
                cmd.Parameters.AddWithValue(p, chunk[i]);
            }

            cmd.CommandText = $"""
                SELECT solarSystemID, solarSystemName
                FROM mapSolarSystems
                WHERE solarSystemID IN ({string.Join(", ", parameters)});
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetInt32(0)] = reader.GetString(1);
            }
        }

        return result;
    }

    private async Task<Dictionary<int, List<SovUpgradeEntry>>> LoadFromSettingsAsync(CancellationToken cancellationToken)
    {
        var stored = await _settingsService.GetAsync<Dictionary<int, List<SovUpgradeEntry>>>(SovDataSettingsKey, cancellationToken);
        return stored ?? [];
    }

    private Task SaveToSettingsAsync(Dictionary<int, List<SovUpgradeEntry>> data, CancellationToken cancellationToken)
    {
        return _settingsService.SetAsync(SovDataSettingsKey, data, cancellationToken);
    }
}
