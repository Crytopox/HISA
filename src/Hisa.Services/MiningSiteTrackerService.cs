using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;

namespace Hisa.Services;

public sealed class MiningSiteTrackerService : IMiningSiteTrackerService
{
    private const string SettingsKey = "Mining.SiteReports";
    private readonly ISettingsService _settings;
    private readonly ISdeDatabase _sde;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private Dictionary<string, MiningSiteReport> _reports = new(StringComparer.OrdinalIgnoreCase);

    public MiningSiteTrackerService(ISettingsService settings, ISdeDatabase sde) { _settings = settings; _sde = sde; }
    public event EventHandler? ReportsUpdated;
    public IReadOnlyDictionary<string, MiningSiteReport> CurrentReports => _reports;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try { _reports = await _settings.GetAsync<Dictionary<string, MiningSiteReport>>(SettingsKey, cancellationToken) ?? new(StringComparer.OrdinalIgnoreCase); }
        finally { _sync.Release(); }
    }

    public Task SetClearedAsync(int id, string name, int tier, CancellationToken ct = default) => SetAsync(id, name, tier, MiningSiteStatus.Cleared, RespawnDelay(tier), ct);
    public Task SetMissingAsync(int id, string name, int tier, TimeSpan delay, CancellationToken ct = default) => SetAsync(id, name, tier, MiningSiteStatus.Missing, delay, ct);

    public async Task MarkAvailableAsync(int id, string name, int tier, CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct);
        try { _reports.Remove(Key(id, name, tier)); await SaveAsync(ct); }
        finally { _sync.Release(); }
        ReportsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<MiningSiteReportRecord>> GetSnapshotAsync(CancellationToken ct = default)
    {
        Dictionary<string, MiningSiteReport> snapshot;
        await _sync.WaitAsync(ct);
        try { snapshot = _reports.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase); }
        finally { _sync.Release(); }
        var names = await ResolveNamesAsync(snapshot.Values.Select(x => x.SolarSystemId), ct);
        var now = DateTime.UtcNow;
        return snapshot.Values.Select(x => new MiningSiteReportRecord { SolarSystemId = x.SolarSystemId, SolarSystemName = names.GetValueOrDefault(x.SolarSystemId, x.SolarSystemId.ToString()), UpgradeName = x.UpgradeName, Tier = x.Tier, Status = x.AvailableAtUtc <= now ? MiningSiteStatus.Available : x.Status, ReportedAtUtc = x.ReportedAtUtc, AvailableAtUtc = x.AvailableAtUtc }).OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.SolarSystemName).ToList();
    }

    public async Task<IReadOnlyList<MiningSiteReport>> GetDueReportsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        List<MiningSiteReport> due;
        await _sync.WaitAsync(ct);
        try
        {
            due = _reports.Values.Where(x => x.AvailableAtUtc <= nowUtc && x.AlertEmittedAtUtc is null).ToList();
            foreach (var item in due)
                _reports[Key(item.SolarSystemId, item.UpgradeName, item.Tier)] = new MiningSiteReport { SolarSystemId = item.SolarSystemId, UpgradeName = item.UpgradeName, Tier = item.Tier, Status = item.Status, ReportedAtUtc = item.ReportedAtUtc, AvailableAtUtc = item.AvailableAtUtc, AlertEmittedAtUtc = nowUtc };
            if (due.Count > 0) await SaveAsync(ct);
        }
        finally { _sync.Release(); }
        if (due.Count > 0) ReportsUpdated?.Invoke(this, EventArgs.Empty);
        return due;
    }

    private async Task SetAsync(int id, string name, int tier, MiningSiteStatus status, TimeSpan delay, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        tier = Math.Clamp(tier, 1, 3);
        await _sync.WaitAsync(ct);
        try { _reports[Key(id, name, tier)] = new MiningSiteReport { SolarSystemId = id, UpgradeName = name.Trim(), Tier = tier, Status = status, ReportedAtUtc = now, AvailableAtUtc = now.Add(delay) }; await SaveAsync(ct); }
        finally { _sync.Release(); }
        ReportsUpdated?.Invoke(this, EventArgs.Empty);
    }
    private Task SaveAsync(CancellationToken ct) => _settings.SetAsync(SettingsKey, _reports, ct);
    private static TimeSpan RespawnDelay(int tier) => Math.Clamp(tier, 1, 3) switch { 3 => TimeSpan.FromHours(10), 2 => TimeSpan.FromHours(4) + TimeSpan.FromMinutes(20), _ => TimeSpan.FromHours(1) };
    public static string Key(int id, string name, int tier) => $"{id}|{name.Trim()}|{Math.Clamp(tier, 1, 3)}";
    private async Task<Dictionary<int, string>> ResolveNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var result = new Dictionary<int, string>(); var list = ids.Distinct().ToList(); if (list.Count == 0) return result;
        await using var connection = _sde.CreateConnection(); await connection.OpenAsync(ct);
        var cmd = connection.CreateCommand(); var placeholders = new List<string>();
        for (var i = 0; i < list.Count; i++) { var p = "$p" + i; placeholders.Add(p); cmd.Parameters.AddWithValue(p, list[i]); }
        cmd.CommandText = $"SELECT solarSystemID, solarSystemName FROM mapSolarSystems WHERE solarSystemID IN ({string.Join(",", placeholders)})";
        await using var reader = await cmd.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) result[reader.GetInt32(0)] = reader.GetString(1);
        return result;
    }
}
