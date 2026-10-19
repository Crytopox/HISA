using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Core;

/// <summary>Optional bounded combat-summary queries; no clustering on the UI thread or renderer.</summary>
public sealed class CombatAnalysisFeature : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ICombatAnalysisRepository? _repository;
    private readonly Func<Action, Task> _dispatch;
    private readonly TimeProvider _clock;
    private readonly LatestMapRequestGate _gate = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private bool _enabled, _disposed, _loading;
    private ActivityRange? _range;
    private long? _system;
    private Func<long, string> _name = id => $"System {id}";
    private CombatMapSnapshot? _snapshot;
    private IReadOnlyList<CombatEngagement> _events = [];
    private CombatEngagement? _selected;
    private string _status = "Combat analysis is off.", _detail = "Select a finding to inspect its evidence.";
    private CombatCursor? _next;
    public CombatAnalysisFeature(ICombatAnalysisRepository? repository, Func<Action, Task> dispatch, TimeProvider clock)
    {
        _repository = repository; _dispatch = dispatch; _clock = clock;
        FocusCommand = new Command(() => { if (_selected is { } item) FocusRequested?.Invoke(item.SolarSystemId); });
        NextPageCommand = new Command(() => { if (_next is { } cursor) Schedule(cursor); });
        RefreshCommand = new Command(() => Schedule(null, refresh: true));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<long>? FocusRequested;
    public ICommand FocusCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand RefreshCommand { get; }
    public Task PendingTask { get; private set; } = Task.CompletedTask;
    public bool IsEnabled { get => _enabled; set { if (!_disposed && Set(ref _enabled, value)) Schedule(null); } }
    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }
    public CombatMapSnapshot? Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public IReadOnlyList<CombatEngagement> Events { get => _events; private set => Set(ref _events, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public bool HasNextPage => _next is not null;
    public CombatEngagement? SelectedEvent
    {
        get => _selected;
        set
        {
            if (_disposed || !Set(ref _selected, value)) return;
            if (value is null) { Detail = "Select a finding to inspect its evidence."; return; }
            var now = _clock.GetUtcNow();
            Detail = $"{_name(value.SolarSystemId)} · {value.Label}\nUTC {value.FirstUtc:yyyy-MM-dd HH:mm:ss} → {value.LastUtc:yyyy-MM-dd HH:mm:ss}\n" +
                $"Duration: {(value.LastUtc - value.FirstUtc).TotalMinutes:N1} min · recorded losses: {value.KillmailCount} · confirmed PvP: {value.ConfirmedPvpCount}\n" +
                $"Known ISK: {value.KnownIskDestroyed:N2} (unknown excluded) · unknown-value losses: {value.UnknownValueCount}\n" +
                $"Leading victim types: {string.Join(", ", value.Ships.Select(s => $"Type {s.ShipTypeId} × {s.Count}"))}\n" +
                $"{value.Evidence.Explanation}\nEvidence: {value.Evidence.Strength} (not a probability). " +
                (value.Evidence.Strength == EngagementConfidence.CorroboratedPvpPattern ? "Multiple identified PvP losses support the temporal pattern." : "Association by system and occurrence time only; participant information is insufficient.") +
                $"\n{HistoricalActivityFeature.CoverageLabel(value.Coverage)}\n" +
                (value.IsPreliminary(now) ? "Preliminary: retained detailed records can revise this grouping." : "Finalized summary: older participant relationships cannot be reconstructed.") +
                (value.RawDetailsExpired(now) ? " Raw details are partly or wholly expired." : " Raw records may remain within the 48-hour retention window.") +
                (value.Classification == EngagementClassification.PossibleCampActivity ? $"\nHistorical candidate. At inspection ({now:yyyy-MM-dd HH:mm} UTC), " +
                    (value.IsCurrentCandidate(now) ? "the last observation was within 30 min; present fleet presence remains unknown." : "supporting observations were older; no claim of a current camp.") : "");
        }
    }
    public void SetContext(ActivityRange? range, long? system, Func<long, string> name)
    {
        if (_disposed) return;
        _name = name;
        if (_range == range && _system == system) return;
        _range = range; _system = system; Schedule(null);
    }
    private void Schedule(CombatCursor? cursor, bool refresh = false)
    {
        if (_disposed) return;
        var request = _gate.Begin();
        if (_disposed || !_enabled || _repository is null || _range is not { } range)
        {
            Snapshot = null; Events = []; SelectedEvent = null; _next = null; Changed(nameof(HasNextPage)); IsLoading = false;
            Status = !_enabled ? "Combat analysis is off." : "Combat findings wait for a matching historical map range. Regions overview has no system markers.";
            _gate.Complete(request); return;
        }
        if (Snapshot?.Range != range) Snapshot = null;
        Events = []; SelectedEvent = null; _next = null; Changed(nameof(HasNextPage));
        IsLoading = true; Status = "Loading local combat findings…";
        PendingTask = QueryAsync(request, range, _system, cursor, refresh ? null : Snapshot);
    }
    private async Task QueryAsync(LatestMapRequestGate.Request request, ActivityRange range, long? system, CombatCursor? cursor, CombatMapSnapshot? cached)
    {
        try
        {
            await Task.Delay(200, request.Token).ConfigureAwait(false);
            await _serial.WaitAsync(request.Token).ConfigureAwait(false);
            CombatMapSnapshot map; CombatPage page;
            try
            {
                map = cached?.Range == range ? cached : await _repository!.GetCombatMapAsync(range, request.Token).ConfigureAwait(false);
                request.Token.ThrowIfCancellationRequested();
                page = await _repository!.GetCombatEventsAsync(new(range, system, Limit: 100, After: cursor), request.Token).ConfigureAwait(false);
                request.Token.ThrowIfCancellationRequested();
            }
            finally { _serial.Release(); }
            await _dispatch(() =>
            {
                if (!Current(request) || _range != range || _system != system) return;
                Snapshot = map; Events = page.Events; _next = page.Next; Changed(nameof(HasNextPage)); IsLoading = false;
                Status = $"{page.Events.Count} findings on this page · {(system is null ? "all systems" : _name(system.Value))}. " +
                    (map.Incomplete || page.Pending ? "Analysis pending, expired source or capacity limit: findings are incomplete. " : "") +
                    "Cyan ring: small engagement · violet: significant / large battle · amber square: possible camp pattern. No finding means no safety guarantee.";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await _dispatch(() => { if (Current(request)) { IsLoading = false; Snapshot = null; Events = []; SelectedEvent = null; Status = ex is OperationCanceledException ? "Combat query timed out; refresh to retry." : "Combat findings unavailable: " + ex.Message; } }).ConfigureAwait(false);
        }
        finally { _gate.Complete(request); }
    }
    private bool Current(LatestMapRequestGate.Request request) => !_disposed && _enabled && _gate.IsCurrent(request);
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; var request = _gate.Begin(); _gate.Complete(request);
        _enabled = false; _loading = false; _snapshot = null; _events = []; _selected = null; _next = null;
        await _serial.WaitAsync().ConfigureAwait(false); _serial.Release();
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
    private void Changed(string? name) => PropertyChanged?.Invoke(this, new(name));
    private sealed class Command(Action action) : ICommand
    { public event EventHandler? CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => action(); }
}
