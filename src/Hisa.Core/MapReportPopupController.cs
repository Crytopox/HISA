namespace Hisa.Core;

public sealed record MapReportPopupRequest(string ReportId, string SystemName, DateTimeOffset ReceivedAtUtc,
    bool IsHistorical = false, bool IsKillmail = false);

public sealed class MapReportPopup
{
    public required string SystemName { get; init; }
    public HashSet<string> ReportIds { get; } = new(StringComparer.Ordinal);
    public long LatestArrivalSequence { get; internal set; }
    public TimeSpan Remaining { get; internal set; }
    internal DateTimeOffset LastTick { get; set; }
    internal bool Paused { get; set; }
    internal HashSet<string> KillmailReportIds { get; } = new(StringComparer.Ordinal);
    internal Queue<string> ArrivalOrder { get; } = [];
}

// Arrival identity is independent of presentation updates and report enrichment.
public sealed class MapReportPopupController(Func<DateTimeOffset>? clock = null)
{
    private long _arrivalSequence;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly HashSet<(string Id, string System)> _seen = [];
    private readonly Queue<(string Id, string System)> _seenOrder = [];
    private readonly Dictionary<string, MapReportPopup> _active = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<MapReportPopup> Active { get { Tick(); return _active.Values; } }

    public void Receive(IEnumerable<MapReportPopupRequest> requests, bool enabled, int seconds, bool showKillmails = true)
    {
        Tick();
        if (!enabled) _active.Clear();
        if (!showKillmails)
        {
            foreach (var popup in _active.Values)
            {
                popup.ReportIds.ExceptWith(popup.KillmailReportIds);
                popup.KillmailReportIds.Clear();
            }
            foreach (var key in _active.Where(p => p.Value.ReportIds.Count == 0).Select(p => p.Key).ToArray())
                _active.Remove(key);
        }
        var now = _clock();
        var duration = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));
        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.ReportId) || string.IsNullOrWhiteSpace(request.SystemName)) continue;
            var key = (request.ReportId, request.SystemName.ToUpperInvariant());
            if (!_seen.Add(key)) continue;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > 4096) _seen.Remove(_seenOrder.Dequeue());
            if (!enabled || request.IsHistorical || request.IsKillmail && !showKillmails) continue;
            var age = now - request.ReceivedAtUtc;
            if (age >= duration) continue;
            if (!_active.TryGetValue(request.SystemName, out var popup))
                _active[request.SystemName] = popup = new MapReportPopup { SystemName = request.SystemName };
            if (popup.ReportIds.Add(request.ReportId)) popup.ArrivalOrder.Enqueue(request.ReportId);
            if (request.IsKillmail) popup.KillmailReportIds.Add(request.ReportId);
            while (popup.ArrivalOrder.Count > 350)
            {
                var expired = popup.ArrivalOrder.Dequeue();
                popup.ReportIds.Remove(expired);
                popup.KillmailReportIds.Remove(expired);
            }
            popup.Remaining = duration - (age < TimeSpan.Zero ? TimeSpan.Zero : age);
            popup.LastTick = now;
            popup.LatestArrivalSequence = ++_arrivalSequence;
        }
    }

    public bool Dismiss(string systemName) => _active.Remove(systemName);

    public void SetHovered(string? systemName)
    {
        Tick();
        foreach (var popup in _active.Values)
            popup.Paused = string.Equals(popup.SystemName, systemName, StringComparison.OrdinalIgnoreCase);
    }

    public void Tick()
    {
        var now = _clock();
        foreach (var popup in _active.Values)
        {
            if (!popup.Paused) popup.Remaining -= now > popup.LastTick ? now - popup.LastTick : TimeSpan.Zero;
            popup.LastTick = now;
        }
        foreach (var key in _active.Where(p => p.Value.Remaining <= TimeSpan.Zero).Select(p => p.Key).ToArray())
            _active.Remove(key);
    }

    public void Clear()
    {
        _active.Clear();
        _seen.Clear();
        _seenOrder.Clear();
    }
}
