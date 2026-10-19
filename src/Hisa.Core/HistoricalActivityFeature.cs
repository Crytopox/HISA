using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Core;

/// <summary>UI-independent historical map feature. Setters and notifications run on the supplied UI dispatcher.
/// Queries, debounce waits and snapshot preparation run outside it. The owner supplies map context only;
/// this feature never mutates the graph, viewport, selection, ingestion or live settings.</summary>
public sealed class HistoricalActivityFeature : INotifyPropertyChanged, IAsyncDisposable
{
    private const string SettingsKey = "Map.Historical.Preferences";
    private readonly IIntelligenceActivityRepository _repository;
    private readonly ISettingsService _settings;
    private readonly Func<Action, Task> _dispatch;
    private readonly Func<IReadOnlyList<int>, CancellationToken, Task<IReadOnlyDictionary<int, string>>> _shipNames;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _retention;
    private readonly LatestMapRequestGate _mapGate = new(), _detailGate = new(), _shipGate = new(), _settingsGate = new();
    private readonly SemaphoreSlim _mapSerial = new(1, 1), _detailSerial = new(1, 1), _shipSerial = new(1, 1);
    private readonly SemaphoreSlim _settingsSerial = new(1, 1);
    private bool _enabled, _initialized, _disposed, _loading, _detailLoading, _shipLoading;
    private int _preset = 1, _offset;
    private HistoricalActivityMetric _metric;
    private DateTimeOffset _latest;
    private ActivityResult? _cached;
    private HistoricalMapSnapshot? _snapshot;
    private MapGraph? _graph;
    private MapViewMode _mode;
    private long? _selected;
    private string _status = "Historical activity is off.", _detail = "Select a solar system.", _shipStatus = "Load ship losses on demand.";
    private IReadOnlyList<HistoricalTimelineBin> _timeline = [];
    private IReadOnlyList<HistoricalShipLoss> _ships = [];

    public HistoricalActivityFeature(IIntelligenceActivityRepository repository, ISettingsService settings,
        Func<Action, Task> dispatch, TimeSpan retention,
        Func<IReadOnlyList<int>, CancellationToken, Task<IReadOnlyDictionary<int, string>>>? shipNames = null,
        TimeProvider? clock = null)
    {
        _repository = repository; _settings = settings; _dispatch = dispatch;
        _retention = retention > TimeSpan.Zero ? retention : TimeSpan.FromDays(30);
        _clock = clock ?? TimeProvider.System;
        Combat = new CombatAnalysisFeature(repository as ICombatAnalysisRepository, dispatch, _clock);
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Snapshot) or nameof(IsEnabled) or nameof(RangeText)) UpdateCombatContext();
        };
        _shipNames = shipNames ?? ((_, _) => Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>()));
        _latest = ActivityRange.BucketStart(_clock.GetUtcNow());
        PreviousCommand = new Command(() => OffsetBuckets++);
        NextCommand = new Command(() => OffsetBuckets--);
        LatestCommand = new Command(() => { if (_disposed) return; _latest = ActivityRange.BucketStart(_clock.GetUtcNow()); _offset = 0; _cached = null; RangeChanged(); });
        LiveCommand = new Command(() => IsEnabled = false);
        LoadShipsCommand = new Command(LoadShips);
    }

    public static IReadOnlyList<HistoricalOption> Windows { get; } = new[] { "Last 15 minutes", "Last hour", "Last 6 hours", "Last 24 hours", "Last 7 days", "Last 30 days" }
        .Select((name, index) => new HistoricalOption(name, index)).ToArray();
    public static IReadOnlyList<HistoricalOption> Metrics { get; } = new[] { "Killmail losses", "Confirmed PvP kills", "Known ISK destroyed", "Attacker entries", "Loss concentration (%)" }
        .Select((name, index) => new HistoricalOption(name, index)).ToArray();
    public ICommand PreviousCommand { get; }
    public CombatAnalysisFeature Combat { get; }
    public ICommand NextCommand { get; }
    public ICommand LatestCommand { get; }
    public ICommand LiveCommand { get; }
    public ICommand LoadShipsCommand { get; }
    public Task PendingMapTask { get; private set; } = Task.CompletedTask;
    public Task PendingDetailTask { get; private set; } = Task.CompletedTask;
    public Task PendingShipTask { get; private set; } = Task.CompletedTask;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_disposed || !Set(ref _enabled, value)) return;
            Changed(nameof(CanLoadShips));
            if (value) { _latest = ActivityRange.BucketStart(_clock.GetUtcNow()); _offset = 0; RangeChanged(); }
            else
            {
                Cancel(_mapGate); Cancel(_detailGate); Cancel(_shipGate);
                _cached = null; Snapshot = null; IsLoading = false; ClearDetail(); Status = "Historical activity is off.";
            }
            SaveLater();
        }
    }
    public int PresetIndex
    {
        get => _preset;
        set { if (!_disposed && Set(ref _preset, Math.Clamp(value, 0, 5))) { _offset = Math.Min(_offset, MaxOffsetBuckets); RangeChanged(); SaveLater(); } }
    }
    public int MetricIndex
    {
        get => (int)_metric;
        set { var next = (HistoricalActivityMetric)Math.Clamp(value, 0, 4); if (!_disposed && Set(ref _metric, next)) { Changed(nameof(MetricIndex)); ScheduleMap(); SaveLater(); } }
    }
    public int OffsetBuckets
    {
        get => _offset;
        set { if (!_disposed && Set(ref _offset, Math.Clamp(value, 0, MaxOffsetBuckets))) RangeChanged(); }
    }
    private int RetainedBuckets => Math.Max(1, (int)Math.Min(int.MaxValue, _retention.Ticks / ActivityRange.BucketSize.Ticks));
    private int WindowBuckets => Math.Min(RetainedBuckets, (int)(ActivityRange.CommonWindows[_preset].Ticks / ActivityRange.BucketSize.Ticks));
    public int MaxOffsetBuckets => Math.Max(0, RetainedBuckets - WindowBuckets);
    public ActivityRange Range => new(_latest.AddTicks(-((long)_offset + WindowBuckets) * ActivityRange.BucketSize.Ticks), _latest.AddTicks(-(long)_offset * ActivityRange.BucketSize.Ticks));
    public string RangeText => $"UTC {Range.FromUtc:yyyy-MM-dd HH:mm} → {Range.ToUtc:yyyy-MM-dd HH:mm}";
    public string LocalRangeText => $"Local {Range.FromUtc.ToLocalTime():MMM dd HH:mm zzz} → {Range.ToUtc.ToLocalTime():MMM dd HH:mm zzz}";
    public string WindowNote => WindowBuckets * ActivityRange.BucketSize.Ticks < ActivityRange.CommonWindows[_preset].Ticks ? "Window limited to configured aggregate retention." : "5-minute steps · most recent completed buckets";
    public string ContextNote => _mode == MapViewMode.UniverseRegions ? "Regions overview: historical system colors are hidden. Open Universe or a region to inspect systems." : "Historical node colors take precedence; live alerts and tactical markers remain active.";
    public bool IsRegionsOverview => _mode == MapViewMode.UniverseRegions;
    public bool CanLoadShips => !_disposed && _enabled && Snapshot?.Range == Range && !IsShipLoading && _mode != MapViewMode.UniverseRegions &&
        _selected is not null && _graph?.Nodes.Any(node => node.Id == _selected) == true;
    public string Legend => "Gray: observed zero · blue-gray: unavailable/uncertain · yellow → orange → pink: increasing recorded activity (fixed log scale).";
    public string MetricNote => _metric switch
    {
        HistoricalActivityMetric.KnownIsk => "Known ISK excludes unknown-value killmails; blue-gray also means values are unknown.",
        HistoricalActivityMetric.AttackerEntries => "Participant entries, not unique attackers.",
        HistoricalActivityMetric.LossConcentration => "Percentage of all locally recorded killmail losses in this window attributed to each system.",
        _ => "Local killmail observations only; ESI kill statistics are not included."
    };
    public HistoricalMapSnapshot? Snapshot { get => _snapshot; private set { if (Set(ref _snapshot, value)) { Changed(nameof(DisplayedRangeText)); Changed(nameof(CoverageText)); Changed(nameof(CanLoadShips)); } } }
    public string DisplayedRangeText => Snapshot is null ? "No historical result displayed." : $"Showing {Metrics[(int)Snapshot.Metric].Label} · UTC {Snapshot.Range.FromUtc:MMM dd HH:mm} → {Snapshot.Range.ToUtc:MMM dd HH:mm}";
    public string CoverageText => Snapshot is null ? "Coverage: Unknown — no result loaded." : CoverageLabel(Snapshot.Coverage);
    public const string CollectionNote = "Locally collected data. HISA does not backfill offline periods. Observed never guarantees complete zKillboard coverage. Gaps describe collection evidence, not precise system activity.";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }
    public bool IsDetailLoading { get => _detailLoading; private set => Set(ref _detailLoading, value); }
    public bool IsShipLoading { get => _shipLoading; private set { if (Set(ref _shipLoading, value)) Changed(nameof(CanLoadShips)); } }
    public string DetailText { get => _detail; private set => Set(ref _detail, value); }
    public string ShipStatus { get => _shipStatus; private set => Set(ref _shipStatus, value); }
    public IReadOnlyList<HistoricalTimelineBin> Timeline { get => _timeline; private set => Set(ref _timeline, value); }
    public IReadOnlyList<HistoricalShipLoss> Ships { get => _ships; private set => Set(ref _ships, value); }

    public async Task InitializeAsync()
    {
        try
        {
            var preferences = await _settings.GetAsync<HistoricalPreferences>(SettingsKey).ConfigureAwait(false);
            await _dispatch(() =>
            {
                if (_disposed) return;
                _preset = Math.Clamp(preferences?.Preset ?? 1, 0, 5);
                _metric = Enum.IsDefined(preferences?.Metric ?? default) ? preferences?.Metric ?? default : default;
                _initialized = true;
                Changed(nameof(PresetIndex)); Changed(nameof(MetricIndex));
                IsEnabled = preferences?.Enabled ?? false;
                RangeNotifications();
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { await _dispatch(() => { if (!_disposed) { _initialized = true; Status = "Historical preferences unavailable: " + ex.Message; } }); }
    }

    public void SetMapContext(MapGraph? graph, MapViewMode mode, long? selectedSystem)
    {
        if (_disposed) return;
        var changed = !ReferenceEquals(_graph, graph) || _mode != mode || _selected != selectedSystem;
        _graph = graph; _mode = mode; _selected = selectedSystem;
        UpdateCombatContext();
        Changed(nameof(ContextNote)); Changed(nameof(IsRegionsOverview)); Changed(nameof(CanLoadShips));
        if (changed) ScheduleDetail(); // All-system map snapshots remain valid across graph/coordinate changes.
    }

    private void UpdateCombatContext() => Combat.SetContext(_enabled && _mode != MapViewMode.UniverseRegions && Snapshot?.Range == Range ? Range : null,
        _selected, id => _graph?.Nodes.FirstOrDefault(n => n.Id == id)?.Name ?? $"System {id}");

    private void RangeNotifications()
    {
        Changed(nameof(OffsetBuckets)); Changed(nameof(MaxOffsetBuckets)); Changed(nameof(RangeText)); Changed(nameof(LocalRangeText));
        Changed(nameof(WindowNote)); Changed(nameof(MetricNote));
    }
    private void RangeChanged() { RangeNotifications(); ScheduleMap(); ScheduleDetail(); }
    private void ScheduleMap()
    {
        Changed(nameof(MetricNote));
        if (!_enabled || _disposed) return;
        var request = _mapGate.Begin();
        var range = Range; var metric = _metric;
        var cached = _cached?.EffectiveRange == range ? _cached : null;
        IsLoading = true; Status = Snapshot is null ? "Loading locally collected historical activity…" : "Loading historical activity… previous result remains usable.";
        PendingMapTask = QueryMapAsync(request, range, metric, cached);
    }
    private async Task QueryMapAsync(LatestMapRequestGate.Request request, ActivityRange range, HistoricalActivityMetric metric, ActivityResult? cached)
    {
        try
        {
            await Task.Delay(200, request.Token).ConfigureAwait(false);
            await _mapSerial.WaitAsync(request.Token).ConfigureAwait(false);
            ActivityResult result;
            try { result = cached ?? await _repository.GetMapActivityAsync(range, request.Token).ConfigureAwait(false); }
            finally { _mapSerial.Release(); }
            var visual = await Task.Run(() => HistoricalMapSnapshot.Create(result, metric), request.Token).ConfigureAwait(false);
            if (!Current(_mapGate, request)) return;
            await _dispatch(() =>
            {
                if (!Current(_mapGate, request) || Range != range || _metric != metric) return;
                _cached = result; Snapshot = visual; IsLoading = false;
                ScheduleDetail();
                Status = result.Coverage.AggregateHistoryExpired ? "Requested history has expired; counts may be incomplete." :
                    result.Total.KillmailCount == 0 && result.Coverage.State != ActivityCoverageState.Observed ? "No recorded events; collection history is unavailable or incomplete, not confirmed inactivity." :
                    $"All systems: {result.Total.KillmailCount:N0} recorded killmails in {result.Systems.Count:N0} systems.";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (OperationCanceledException) { if (Current(_mapGate, request)) await MapErrorAsync(request, "Historical query timed out. Use Latest to retry; the prior result is retained."); }
        catch (Exception ex) { if (Current(_mapGate, request)) await MapErrorAsync(request, "Historical query failed: " + ex.Message); }
        finally { _mapGate.Complete(request); }
    }
    private Task MapErrorAsync(LatestMapRequestGate.Request request, string? message) => _dispatch(() =>
    {
        if (!Current(_mapGate, request)) return;
        IsLoading = false; if (message is not null) Status = message;
    });

    private void ClearDetail()
    {
        DetailText = _mode == MapViewMode.UniverseRegions ? "Select a system in Universe or Region view." : "Select a solar system to inspect this window.";
        Timeline = []; Ships = []; ShipStatus = "Load ship losses on demand."; IsDetailLoading = false; IsShipLoading = false;
    }
    private void ScheduleDetail()
    {
        Cancel(_shipGate);
        var request = _detailGate.Begin(); ClearDetail();
        var node = _mode == MapViewMode.UniverseRegions ? null : _graph?.Nodes.FirstOrDefault(n => n.Id == _selected);
        if (!_enabled || _disposed || node is null || Snapshot?.Range != Range)
        {
            if (_enabled && node is not null) DetailText = "Details are waiting for the map's selected range to load. Previous map colors use the displayed range above.";
            _detailGate.Complete(request); return;
        }
        IsDetailLoading = true; DetailText = $"{node.Name} — loading historical details…";
        PendingDetailTask = QueryDetailAsync(request, node, Range);
    }
    private async Task QueryDetailAsync(LatestMapRequestGate.Request request, MapNode node, ActivityRange range)
    {
        try
        {
            await Task.Delay(200, request.Token).ConfigureAwait(false);
            await _detailSerial.WaitAsync(request.Token).ConfigureAwait(false);
            ActivityResult result;
            try { result = await _repository.GetTimeSeriesAsync(node.Id, range, request.Token).ConfigureAwait(false); }
            finally { _detailSerial.Release(); }
            var timeline = HistoricalMapSnapshot.Timeline(range, result.Buckets);
            if (!Current(_detailGate, request)) return;
            await _dispatch(() =>
            {
                if (!Current(_detailGate, request) || _selected != node.Id || Range != range || _mode == MapViewMode.UniverseRegions) return;
                var m = result.Total;
                DetailText = $"{node.Name}\nUTC {range.FromUtc:yyyy-MM-dd HH:mm} → {range.ToUtc:yyyy-MM-dd HH:mm}\n" +
                    $"Killmail losses: {m.KillmailCount:N0} · confirmed PvP: {m.ConfirmedPvpKills:N0}\nKnown ISK: {m.KnownIskDestroyed:N2} (unknown values excluded)\n" +
                    $"Unknown-value kills: {m.UnknownValueCount:N0}\nAttacker entries (not unique): {m.AttackerEntries:N0}\n" +
                    $"First: {Occurrence(m.FirstOccurrenceUtc)}\nLast: {Occurrence(m.LastOccurrenceUtc)}\n{CoverageLabel(result.Coverage)}";
                Timeline = timeline; IsDetailLoading = false;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!Current(_detailGate, request)) return;
            await _dispatch(() => { if (Current(_detailGate, request)) { IsDetailLoading = false; DetailText = ex is OperationCanceledException ? "Historical details timed out; reselect the system to retry." : "Historical detail unavailable: " + ex.Message; } });
        }
        finally { _detailGate.Complete(request); }
    }
    private void LoadShips()
    {
        if (!CanLoadShips || _selected is not long id) return;
        var request = _shipGate.Begin(); IsShipLoading = true; ShipStatus = "Loading ship losses…";
        PendingShipTask = QueryShipsAsync(request, id, Range);
    }
    private async Task QueryShipsAsync(LatestMapRequestGate.Request request, long id, ActivityRange range)
    {
        try
        {
            await _shipSerial.WaitAsync(request.Token).ConfigureAwait(false);
            ShipLossActivityResult result;
            try { result = await _repository.GetShipLossesAsync(range, [id], request.Token).ConfigureAwait(false); }
            finally { _shipSerial.Release(); }
            var leading = result.ShipLosses.OrderByDescending(s => s.LossCount).ThenBy(s => s.VictimShipTypeId).Take(8).ToArray();
            IReadOnlyDictionary<int, string> names;
            try { names = await _shipNames(leading.Select(s => s.VictimShipTypeId).ToArray(), request.Token).ConfigureAwait(false); }
            catch (Exception) when (!request.Token.IsCancellationRequested) { names = new Dictionary<int, string>(); }
            var ships = leading.Select(s => new HistoricalShipLoss(names.GetValueOrDefault(s.VictimShipTypeId, $"Type {s.VictimShipTypeId}"), s.LossCount)).ToArray();
            if (!Current(_shipGate, request)) return;
            await _dispatch(() =>
            {
                if (!Current(_shipGate, request) || _selected != id || Range != range) return;
                Ships = ships; IsShipLoading = false; ShipStatus = ships.Length > 0 ? "Leading victim ship types (recorded losses)" : "No recorded ship losses; interpret with coverage above.";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception ex) { if (Current(_shipGate, request)) await _dispatch(() => { if (Current(_shipGate, request)) { IsShipLoading = false; ShipStatus = ex is OperationCanceledException ? "Ship-loss query timed out; retry when ready." : "Ship losses unavailable: " + ex.Message; } }); }
        finally { _shipGate.Complete(request); }
    }

    public static string CoverageLabel(ActivityCoverage coverage) => $"Coverage: {coverage.State}" +
        (coverage.AggregateHistoryExpired ? " · aggregate history expired" : "") + (coverage.GapHistoryExpired ? " · gap evidence expired" : "") +
        (coverage.CatchUpPending ? " · historical aggregation pending" : "") + (coverage.KnownGapCount > 0 ? $" · {coverage.KnownGapCount:N0} known collection losses" : "") + " · local evidence only";
    private static string Occurrence(DateTimeOffset? time) => time is { } utc ? $"{utc:yyyy-MM-dd HH:mm:ss} UTC" : "No recorded occurrence";
    private bool Current(LatestMapRequestGate gate, LatestMapRequestGate.Request request) => !_disposed && _enabled && gate.IsCurrent(request);
    private static void Cancel(LatestMapRequestGate gate) { var request = gate.Begin(); gate.Complete(request); }
    private HistoricalPreferences Preferences => new(_enabled, _metric, _preset);
    private void SaveLater()
    {
        if (!_initialized || _disposed) return;
        var request = _settingsGate.Begin(); var preferences = Preferences;
        _ = SaveAsync(request, preferences);
    }
    private async Task SaveAsync(LatestMapRequestGate.Request request, HistoricalPreferences preferences)
    {
        try
        {
            await Task.Delay(500, request.Token).ConfigureAwait(false);
            await _settingsSerial.WaitAsync(request.Token).ConfigureAwait(false);
            try { await _settings.SetAsync(SettingsKey, preferences, request.Token).ConfigureAwait(false); }
            finally { _settingsSerial.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Preferences must not interrupt live gameplay. Final disposal retries the save. */ }
        finally { _settingsGate.Complete(request); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var preferences = Preferences; // Closing releases UI state without persisting a synthetic disabled preference.
        _disposed = true;
        _enabled = false; _loading = false; _detailLoading = false; _shipLoading = false;
        Cancel(_mapGate); Cancel(_detailGate); Cancel(_shipGate); Cancel(_settingsGate);
        _snapshot = null; _cached = null; _timeline = []; _ships = [];
        await Combat.DisposeAsync().ConfigureAwait(false);
        // Drain active operations before returning; superseded requests have cancelled tokens,
        // and all repository readers have their own bounded execution deadline.
        foreach (var serial in new[] { _mapSerial, _detailSerial, _shipSerial, _settingsSerial })
        {
            await serial.WaitAsync().ConfigureAwait(false);
            serial.Release();
        }
        await _settingsSerial.WaitAsync().ConfigureAwait(false);
        try { if (_initialized) await _settings.SetAsync(SettingsKey, preferences).ConfigureAwait(false); }
        catch (Exception) { }
        finally { _settingsSerial.Release(); }
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private sealed class Command(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
