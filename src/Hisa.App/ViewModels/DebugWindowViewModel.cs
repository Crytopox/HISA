using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hisa.App.Diagnostics;
using Hisa.Core.Abstractions;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Hisa.App;

public sealed class DebugWindowViewModel : INotifyPropertyChanged
{
    private readonly AppLogStore _store;
    private readonly IEsiMetricsStore _esiMetricsStore;
    private readonly IAppLogFileService _logFileService;
    private string _searchText = string.Empty;
    private string _categoryFilter = "All";
    private string _sourceFilter = "All";
    private LogLevel? _selectedLevel;
    private readonly DispatcherTimer _esiUiTimer;

    public DebugWindowViewModel(AppLogStore store, IEsiMetricsStore esiMetricsStore, IAppLogFileService logFileService)
    {
        _store = store;
        _esiMetricsStore = esiMetricsStore;
        _logFileService = logFileService;
        LevelOptions = new ObservableCollection<LogLevelOption>(
        [
            new(null, "All"),
            new(LogLevel.Trace, "Trace"),
            new(LogLevel.Debug, "Debug"),
            new(LogLevel.Information, "Information"),
            new(LogLevel.Warning, "Warning"),
            new(LogLevel.Error, "Error"),
            new(LogLevel.Critical, "Critical")
        ]);
        CategoryOptions = new ObservableCollection<string>(["All"]);
        SourceOptions = new ObservableCollection<string>(["All"]);
        Entries = [];
        EsiEntries = [];

        foreach (var entry in _store.Snapshot())
        {
            AddEntry(entry, refresh: false);
        }
        Refresh();

        _store.EntryAdded += OnEntryAdded;
        foreach (var metric in _esiMetricsStore.Snapshot())
        {
            AddEsiMetric(metric);
        }
        _esiMetricsStore.MetricAdded += OnEsiMetricAdded;

        _esiUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _esiUiTimer.Tick += (_, _) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiRateSummary)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiTokenBar)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiTokenStats)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiNextRefresh)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiRateGroup)));
        };
        _esiUiTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LogLevelOption> LevelOptions { get; }
    public ObservableCollection<string> CategoryOptions { get; }
    public ObservableCollection<string> SourceOptions { get; }
    public ObservableCollection<DisplayLogEntry> Entries { get; }
    public ObservableCollection<DisplayEsiMetric> EsiEntries { get; }
    public string EsiRateSummary => BuildEsiRateSummary();
    public string EsiTokenBar => BuildEsiTokenBar();
    public string EsiTokenStats => BuildEsiTokenStats();
    public string EsiNextRefresh => BuildEsiNextRefresh();
    public string EsiRateGroup => $"Group: {_esiMetricsStore.CurrentRateState.RateLimitGroup}";

    public LogLevelOption? SelectedLevelOption
    {
        get => LevelOptions.FirstOrDefault(o => o.Level == _selectedLevel) ?? LevelOptions[0];
        set
        {
            var level = value?.Level;
            if (SetProperty(ref _selectedLevel, level))
            {
                Refresh();
            }
        }
    }

    public string CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (SetProperty(ref _categoryFilter, value))
            {
                Refresh();
            }
        }
    }

    public string SourceFilter
    {
        get => _sourceFilter;
        set
        {
            if (SetProperty(ref _sourceFilter, value))
            {
                Refresh();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                Refresh();
            }
        }
    }

    private void OnEntryAdded(object? sender, AppLogEntry entry)
    {
        Dispatcher.UIThread.Post(() => AddEntry(entry, refresh: true));
    }

    private void AddEntry(AppLogEntry entry, bool refresh)
    {
        if (!CategoryOptions.Contains(entry.Category))
        {
            CategoryOptions.Add(entry.Category);
        }
        if (!SourceOptions.Contains(entry.SourceTag))
        {
            SourceOptions.Add(entry.SourceTag);
        }

        _allEntries.Add(new DisplayLogEntry(entry));
        if (_allEntries.Count > 5000)
        {
            _allEntries.RemoveAt(0);
        }

        if (refresh)
        {
            Refresh();
        }
    }

    private readonly List<DisplayLogEntry> _allEntries = [];
    private void OnEsiMetricAdded(object? sender, Hisa.Core.Models.EsiRequestMetric metric)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddEsiMetric(metric);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiRateSummary)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiTokenBar)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiTokenStats)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EsiNextRefresh)));
        });
    }

    private void AddEsiMetric(Hisa.Core.Models.EsiRequestMetric metric)
    {
        EsiEntries.Add(new DisplayEsiMetric(metric));
        while (EsiEntries.Count > 500)
        {
            EsiEntries.RemoveAt(0);
        }
    }

    private string BuildEsiRateSummary()
    {
        var rate = _esiMetricsStore.CurrentRateState;
        var next = rate.NextAllowedAtUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "-";
        return $"Last15m: {rate.RequestsLast15Minutes}/{rate.RouteTokenLimit15Minutes} | Next: {next}";
    }

    private string BuildEsiTokenBar()
    {
        var rate = _esiMetricsStore.CurrentRateState;
        var total = Math.Max(1, rate.RouteTokenLimit15Minutes);
        var remaining = rate.HeaderRateLimitRemaining is int hdrRemain
            ? Math.Clamp(hdrRemain, 0, total)
            : Math.Max(0, total - Math.Clamp(rate.RequestsLast15Minutes, 0, total));
        var used = Math.Clamp(total - remaining, 0, total);
        var width = 24;
        var usedSlots = (int)Math.Round((used / (double)total) * width, MidpointRounding.AwayFromZero);
        usedSlots = Math.Clamp(usedSlots, 0, width);
        var remainingSlots = width - usedSlots;
        return $"[{new string('#', usedSlots)}{new string('-', remainingSlots)}]";
    }

    private string BuildEsiTokenStats()
    {
        var rate = _esiMetricsStore.CurrentRateState;
        var total = Math.Max(1, rate.RouteTokenLimit15Minutes);
        var remaining = rate.HeaderRateLimitRemaining is int hdrRemain
            ? Math.Clamp(hdrRemain, 0, total)
            : Math.Max(0, total - Math.Clamp(rate.RequestsLast15Minutes, 0, total));
        var used = Math.Clamp(total - remaining, 0, total);
        var source = rate.HeaderRateLimitRemaining is null ? "local-fallback" : "esi-header";
        return $"Used {used} / Remaining {remaining} / Total {total} [{source}]";
    }

    private string BuildEsiNextRefresh()
    {
        var next = _esiMetricsStore.CurrentRateState.NextAllowedAtUtc;
        if (next is null)
        {
            return "Next refresh: -";
        }

        var local = next.Value.ToLocalTime().ToString("HH:mm:ss");
        var delta = next.Value - DateTimeOffset.UtcNow;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        return $"Next refresh: {local} (in {delta:mm\\:ss})";
    }

    private void Refresh()
    {
        var filtered = _allEntries.Where(Matches).TakeLast(1500).ToList();
        Entries.Clear();
        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }
    }

    private bool Matches(DisplayLogEntry entry)
    {
        if (_selectedLevel is not null && entry.Raw.Level < _selectedLevel)
        {
            return false;
        }

        if (!string.Equals(_categoryFilter, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Raw.Category, _categoryFilter, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.Equals(_sourceFilter, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Raw.SourceTag, _sourceFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_searchText)
            && entry.Line.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    public Task<string> ExportLogsAsync(CancellationToken cancellationToken = default)
    {
        return _logFileService.ExportSnapshotAsync(_store.Snapshot(), cancellationToken);
    }

    public void OpenLogsFolder()
    {
        _logFileService.OpenLogsFolder();
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record LogLevelOption(LogLevel? Level, string Name)
{
    public override string ToString() => Name;
}

public sealed record DisplayLogEntry(AppLogEntry Raw)
{
    public string Time => Raw.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    public LogLevel Level => Raw.Level;
    public string LevelText => Raw.Level.ToString().ToUpperInvariant();
    public string SourceTag => Raw.SourceTag;
    public string Category => Raw.Category;
    public string Message => string.IsNullOrWhiteSpace(Raw.Exception)
        ? Raw.Message
        : $"{Raw.Message} | {Raw.Exception}";
    public string Line => $"{Time} [{Raw.Level}] {Raw.Category}: {Message}";
}

public sealed record DisplayEsiMetric(Hisa.Core.Models.EsiRequestMetric Raw)
{
    public string Time => Raw.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    public string Route => Raw.Route;
    public string Group => Raw.RateLimitGroup;
    public string Source => Raw.FromCache ? "cache" : "network";
    public int Status => Raw.StatusCode;
    public string Limits => $"remain={Raw.RateLimitRemain?.ToString() ?? "-"} reset={Raw.RateLimitResetSeconds?.ToString() ?? "-"} err={Raw.ErrorLimitRemain?.ToString() ?? "-"}";
    public string Duration => $"{Raw.Duration.TotalMilliseconds:0}ms";
    public string Message => Raw.Message;
}
