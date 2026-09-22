using System.Text.RegularExpressions;
using Hisa.Core;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;

namespace Hisa.Services;

public sealed class AnsiblexNetworkStateService : IAnsiblexNetworkStateService
{
    private const string AnsiblexDataSettingsKey = "Map.AnsiblexNetwork";
    private const string AnsiblexCapitalSettingsKey = "Map.AnsiblexCapitalSystemId";
    private const string AnsiblexShipClassSettingsKey = "Map.AnsiblexShipClass";
    private readonly ISettingsService _settingsService;
    private readonly ISdeDatabase _sdeDatabase;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private List<AnsiblexLinkEntry> _currentLinks = [];
    private AnsiblexCapitalState? _capital;
    private string _shipClassName = AnsiblexZones.DefaultShipClassName;

    public AnsiblexNetworkStateService(ISettingsService settingsService, ISdeDatabase sdeDatabase)
    {
        _settingsService = settingsService;
        _sdeDatabase = sdeDatabase;
    }

    public event EventHandler? SnapshotUpdated;
    public IReadOnlyList<AnsiblexLinkEntry> CurrentLinks => _currentLinks.ToList();
    public AnsiblexCapitalState? Capital => _capital;
    public string SelectedShipClassName => _shipClassName;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        int? capitalId;
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _currentLinks = NormalizeLinks(await _settingsService.GetAsync<List<AnsiblexLinkEntry>>(AnsiblexDataSettingsKey) ?? []);
            capitalId = await _settingsService.GetAsync<int?>(AnsiblexCapitalSettingsKey);
            _shipClassName = AnsiblexZones.GetShipClassOrDefault(await _settingsService.GetAsync<string>(AnsiblexShipClassSettingsKey)).Name;
        }
        finally
        {
            _sync.Release();
        }

        _capital = capitalId is int id ? await LoadCapitalAsync(id, cancellationToken) : null;
    }

    public async Task<IReadOnlyList<AnsiblexLinkRecord>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        List<AnsiblexLinkEntry> snapshot;
        await _sync.WaitAsync(cancellationToken);
        try
        {
            snapshot = _currentLinks.ToList();
        }
        finally
        {
            _sync.Release();
        }

        if (snapshot.Count == 0)
        {
            return [];
        }

        var ids = snapshot.SelectMany(x => new[] { x.FromSolarSystemId, x.ToSolarSystemId }).Distinct().ToList();
        var namesById = await ResolveSystemNamesAsync(ids, cancellationToken);
        return snapshot
            .Select(x => new AnsiblexLinkRecord
            {
                FromSolarSystemId = x.FromSolarSystemId,
                ToSolarSystemId = x.ToSolarSystemId,
                FromSolarSystemName = namesById.TryGetValue(x.FromSolarSystemId, out var fromName) ? fromName : x.FromSolarSystemId.ToString(),
                ToSolarSystemName = namesById.TryGetValue(x.ToSolarSystemId, out var toName) ? toName : x.ToSolarSystemId.ToString()
            })
            .OrderBy(x => x.FromSolarSystemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ToSolarSystemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AnsiblexPlanningSnapshot> GetPlanningSnapshotAsync(CancellationToken cancellationToken = default)
    {
        AnsiblexCapitalState? capital;
        List<AnsiblexLinkEntry> links;
        string shipName;
        await _sync.WaitAsync(cancellationToken);
        try
        {
            capital = _capital;
            links = _currentLinks.ToList();
            shipName = _shipClassName;
        }
        finally
        {
            _sync.Release();
        }

        var ship = AnsiblexZones.GetShipClassOrDefault(shipName);
        if (links.Count == 0)
        {
            var emptySummary = capital is null
                ? AnsiblexZones.BuildPlanningSummary(null, ship, null)
                : $"{capital.SolarSystemName}. Capacity {AnsiblexZones.CapacityTj:0} TJ · ~{AnsiblexZones.AverageRechargeTjPerHour:0} TJ/hour · full recharge ~{AnsiblexZones.FullRechargeHours:0.##} h.";
            return new AnsiblexPlanningSnapshot
            {
                CapitalSystemName = capital?.SolarSystemName,
                ShipClassName = ship.Name,
                Summary = emptySummary,
                Links = []
            };
        }

        var ids = links.SelectMany(x => new[] { x.FromSolarSystemId, x.ToSolarSystemId }).ToList();
        if (capital is not null)
        {
            ids.Add(capital.SolarSystemId);
        }

        var systems = await LoadSystemsAsync(ids, cancellationToken);
        var zones = new Dictionary<int, AnsiblexSystemZone>();
        if (capital?.PositionX is double capitalX &&
            capital.PositionY is double capitalY &&
            capital.PositionZ is double capitalZ)
        {
            foreach (var (systemId, system) in systems)
            {
                if (system.X is not double x || system.Y is not double y || system.Z is not double z ||
                    !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                {
                    continue;
                }

                zones[systemId] = AnsiblexZones.ZoneFor(capitalX, capitalY, capitalZ, x, y, z);
            }
        }

        var endpointZones = links
            .SelectMany(link => new[] { link.FromSolarSystemId, link.ToSolarSystemId })
            .Distinct()
            .Select(id => zones.TryGetValue(id, out var zone) ? zone.Zone : (int?)null)
            .Where(zone => zone is not null)
            .Select(zone => zone!.Value)
            .ToList();
        int? highestZone = endpointZones.Count == 0 ? null : endpointZones.Max();
        var rows = links
            .Select(link =>
            {
                zones.TryGetValue(link.FromSolarSystemId, out var fromZone);
                zones.TryGetValue(link.ToSolarSystemId, out var toZone);
                var fromName = systems.TryGetValue(link.FromSolarSystemId, out var fromSystem)
                    ? fromSystem.Name
                    : link.FromSolarSystemId.ToString();
                var toName = systems.TryGetValue(link.ToSolarSystemId, out var toSystem)
                    ? toSystem.Name
                    : link.ToSolarSystemId.ToString();
                return new AnsiblexPlanningLink
                {
                    FromSystemName = fromName,
                    ToSystemName = toName,
                    FromZoneText = fromZone is null ? "—" : AnsiblexZones.FormatZone(fromZone),
                    ToZoneText = toZone is null ? "—" : AnsiblexZones.FormatZone(toZone),
                    TowardToText = toZone is null ? "—" : AnsiblexZones.FormatCostTj(AnsiblexZones.CostTj(ship.BaseCostTj, toZone.Zone)),
                    TowardFromText = fromZone is null ? "—" : AnsiblexZones.FormatCostTj(AnsiblexZones.CostTj(ship.BaseCostTj, fromZone.Zone))
                };
            })
            .OrderBy(x => x.FromSystemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ToSystemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AnsiblexPlanningSnapshot
        {
            CapitalSystemName = capital?.SolarSystemName,
            ShipClassName = ship.Name,
            Summary = AnsiblexZones.BuildPlanningSummary(capital?.SolarSystemName, ship, highestZone),
            Links = rows
        };
    }

    public async Task<bool> SetCapitalSystemAsync(string systemName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(systemName))
        {
            return false;
        }

        var name = systemName.Trim();
        var nameToId = await ResolveSystemIdsAsync([name], cancellationToken);
        return nameToId.TryGetValue(name, out var systemId) &&
               await SetCapitalSystemByIdAsync(systemId, cancellationToken);
    }

    public async Task<bool> SetCapitalSystemByIdAsync(int solarSystemId, CancellationToken cancellationToken = default)
    {
        var capital = await LoadCapitalAsync(solarSystemId, cancellationToken);
        if (capital is null)
        {
            return false;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            _capital = capital;
            await _settingsService.SetAsync(AnsiblexCapitalSettingsKey, capital.SolarSystemId);
        }
        finally
        {
            _sync.Release();
        }

        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task SetSelectedShipClassAsync(string shipClassName, CancellationToken cancellationToken = default)
    {
        if (!AnsiblexZones.TryGetShipClass(shipClassName, out var shipClass) ||
            string.Equals(_shipClassName, shipClass.Name, StringComparison.Ordinal))
        {
            return;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            _shipClassName = shipClass.Name;
            await _settingsService.SetAsync(AnsiblexShipClassSettingsKey, shipClass.Name);
        }
        finally
        {
            _sync.Release();
        }

        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
    }

    public async Task<AnsiblexImportResult> ImportFromTextAsync(string rawText, SovImportMode mode, CancellationToken cancellationToken = default)
    {
        var parsedPairs = ParseInput(rawText);
        var parsedNames = parsedPairs.SelectMany(x => new[] { x.From, x.To }).ToList();
        var distinctParsedNames = parsedNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var nameToId = await ResolveSystemIdsAsync(distinctParsedNames, cancellationToken);
        var unresolvedNames = distinctParsedNames
            .Where(x => !nameToId.ContainsKey(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parsedLinks = new List<AnsiblexLinkEntry>();
        foreach (var pair in parsedPairs)
        {
            if (!nameToId.TryGetValue(pair.From, out var fromId) || !nameToId.TryGetValue(pair.To, out var toId) || fromId == toId)
            {
                continue;
            }

            parsedLinks.Add(ToCanonicalLink(fromId, toId));
        }

        var normalizedIncoming = NormalizeLinks(parsedLinks);
        var skippedCount = Math.Max(0, parsedPairs.Count - normalizedIncoming.Count);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _currentLinks = mode switch
            {
                SovImportMode.Replace => normalizedIncoming,
                SovImportMode.UpdateOnChange => normalizedIncoming,
                SovImportMode.Append => NormalizeLinks(_currentLinks.Concat(normalizedIncoming)),
                _ => normalizedIncoming
            };

            await _settingsService.SetAsync(AnsiblexDataSettingsKey, _currentLinks);
        }
        finally
        {
            _sync.Release();
        }

        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
        return new AnsiblexImportResult
        {
            ParsedLinks = normalizedIncoming.Count,
            TotalLinksAfterImport = _currentLinks.Count,
            DuplicateOrInvalidLinksSkipped = skippedCount,
            UnresolvedSystemNamesCount = unresolvedNames.Count,
            UnresolvedSystemNames = unresolvedNames
        };
    }

    public async Task AddOrUpdateLinkAsync(string fromSystemName, string toSystemName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromSystemName) || string.IsNullOrWhiteSpace(toSystemName))
        {
            return;
        }

        var from = fromSystemName.Trim();
        var to = toSystemName.Trim();
        var nameToId = await ResolveSystemIdsAsync([from, to], cancellationToken);
        if (!nameToId.TryGetValue(from, out var fromId) || !nameToId.TryGetValue(to, out var toId) || fromId == toId)
        {
            return;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            _currentLinks = NormalizeLinks(_currentLinks.Concat([ToCanonicalLink(fromId, toId)]));
            await _settingsService.SetAsync(AnsiblexDataSettingsKey, _currentLinks);
        }
        finally
        {
            _sync.Release();
        }

        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveLinkAsync(string fromSystemName, string toSystemName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromSystemName) || string.IsNullOrWhiteSpace(toSystemName))
        {
            return;
        }

        var from = fromSystemName.Trim();
        var to = toSystemName.Trim();
        var nameToId = await ResolveSystemIdsAsync([from, to], cancellationToken);
        if (!nameToId.TryGetValue(from, out var fromId) || !nameToId.TryGetValue(to, out var toId))
        {
            return;
        }

        var canonical = ToCanonicalLink(fromId, toId);
        await _sync.WaitAsync(cancellationToken);
        try
        {
            _currentLinks = _currentLinks
                .Where(x => !(x.FromSolarSystemId == canonical.FromSolarSystemId && x.ToSolarSystemId == canonical.ToSolarSystemId))
                .ToList();
            await _settingsService.SetAsync(AnsiblexDataSettingsKey, _currentLinks);
        }
        finally
        {
            _sync.Release();
        }

        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
    }

    private static List<AnsiblexLinkEntry> NormalizeLinks(IEnumerable<AnsiblexLinkEntry> links)
    {
        return links
            .Where(x => x.FromSolarSystemId != x.ToSolarSystemId)
            .Select(x => ToCanonicalLink(x.FromSolarSystemId, x.ToSolarSystemId))
            .GroupBy(x => $"{x.FromSolarSystemId}:{x.ToSolarSystemId}", StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(x => x.FromSolarSystemId)
            .ThenBy(x => x.ToSolarSystemId)
            .ToList();
    }

    private static AnsiblexLinkEntry ToCanonicalLink(int fromId, int toId)
    {
        var a = Math.Min(fromId, toId);
        var b = Math.Max(fromId, toId);
        return new AnsiblexLinkEntry { FromSolarSystemId = a, ToSolarSystemId = b };
    }

    private static IReadOnlyList<(string From, string To)> ParseInput(string rawText)
    {
        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<(string From, string To)>();

        foreach (var line in lines)
        {
            if (TryParseGoonwikiLine(line, out var goonPair))
            {
                result.Add(goonPair);
                continue;
            }

            if (TryParseCanonicalLine(line, out var canonicalPair))
            {
                result.Add(canonicalPair);
            }
        }

        return result;
    }

    private static bool TryParseGoonwikiLine(string line, out (string From, string To) pair)
    {
        pair = default;
        if (string.IsNullOrWhiteSpace(line) || line.Contains("System / POS", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tabs = line.Split('\t', StringSplitOptions.TrimEntries);
        if (tabs.Length >= 3)
        {
            var from = ExtractSystemName(tabs[1]);
            var to = ExtractSystemName(tabs[2]);
            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
            {
                pair = (from, to);
                return true;
            }
        }

        var matches = Regex.Matches(line, @"\b([A-Z0-9][A-Z0-9\-]{1,11})\b\s*@");
        if (matches.Count >= 2)
        {
            pair = (matches[0].Groups[1].Value.Trim(), matches[1].Groups[1].Value.Trim());
            return true;
        }

        return false;
    }

    private static bool TryParseCanonicalLine(string line, out (string From, string To) pair)
    {
        pair = default;
        string[] separators = ["<->", "<=>", "->", "=>", ",", "|"];
        foreach (var separator in separators)
        {
            var parts = line.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var from = ExtractSystemName(parts[0]);
            var to = ExtractSystemName(parts[1]);
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            {
                continue;
            }

            pair = (from, to);
            return true;
        }

        return false;
    }

    private static string ExtractSystemName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();
        var at = text.IndexOf('@');
        if (at > 0)
        {
            text = text[..at].Trim();
        }

        return text;
    }

    private async Task<AnsiblexCapitalState?> LoadCapitalAsync(int solarSystemId, CancellationToken cancellationToken)
    {
        var systems = await LoadSystemsAsync([solarSystemId], cancellationToken);
        if (!systems.TryGetValue(solarSystemId, out var system))
        {
            return null;
        }

        return new AnsiblexCapitalState
        {
            SolarSystemId = solarSystemId,
            SolarSystemName = system.Name,
            PositionX = system.X,
            PositionY = system.Y,
            PositionZ = system.Z
        };
    }

    private async Task<Dictionary<int, (string Name, double? X, double? Y, double? Z)>> LoadSystemsAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var result = new Dictionary<int, (string Name, double? X, double? Y, double? Z)>();
        const int chunkSize = 200;
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
                SELECT solarSystemID, solarSystemName, x, y, z
                FROM mapSolarSystems
                WHERE solarSystemID IN ({string.Join(", ", parameters)});
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetInt32(0)] = (
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4));
            }
        }

        return result;
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
        const int chunkSize = 200;
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
}
