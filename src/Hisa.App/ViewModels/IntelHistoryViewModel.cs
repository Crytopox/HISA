using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class IntelHistoryChannel : INotifyPropertyChanged
{
    private bool _selected;
    public required string Name { get; init; }
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record IntelHistorySystem(string Name, long Id)
{
    // Recognized systems outside the current graph are resolved on click.
    public bool CanNavigate => !string.IsNullOrWhiteSpace(Name);
}

public sealed record IntelHistoryShipToken(IntelOverlayShipSummaryCard Ship, bool Friendly = false)
{
    public string Foreground => Friendly ? "#9ADAB0" : "#C7D7EB";
    public string Tooltip => Friendly ? $"Friendly: {Ship.ShipName}" : Ship.ShipName;
}
public sealed record IntelHistoryOverflowToken(string Label, string Tooltip);

public sealed class IntelHistoryRow : INotifyPropertyChanged
{
    private string _age = string.Empty;
    public required IntelChatReport Report { get; init; }
    public IntelOverlayCard? Details { get; init; }
    public required IReadOnlyList<IntelHistorySystem> Systems { get; init; }
    public bool HasSystems => Systems.Count > 0;
    public bool HasShips => Details?.ShipsSummary.Count > 0;
    public bool HasFriendlyShips => Details?.FriendlyShipsSummary.Count > 0;
    public bool HasPilots => Details?.Hostiles.Count > 0;
    private IReadOnlyList<object>? _metadata;
    public IReadOnlyList<object> Metadata => _metadata ??= BuildMetadata();
    public bool HasMetadata => Metadata.Count > 0;
    public string EventIcon => Report.IsClear ? "✓" : HasEvent ? "◆" : "·";
    public string TimestampLabel => Report.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
    public string EventLabel => Report.IsClear ? "✓ Clear" : Report.Alerts.Any(a => a != IntelAlertType.None)
        ? string.Join(" · ", Report.Alerts.Where(a => a != IntelAlertType.None).Select(a => a == IntelAlertType.GateCamp ? "Gate camp" : a.ToString()))
        : Report.ReportedHostileCount > 0 ? $"⚠ {Report.ReportedHostileCount} hostile" : string.Empty;
    public bool HasEvent => EventLabel.Length > 0;
    public string Accent => Report.IsClear ? "#6FE38E"
        : Report.Alerts.Any(a => a is IntelAlertType.Fight or IntelAlertType.GateCamp or IntelAlertType.Spike) ? "#F58181"
        : Report.Alerts.Any(a => a is IntelAlertType.Cyno or IntelAlertType.Bubble or IntelAlertType.Wormhole) ? "#7CB9EF"
        : Report.ReportedHostileCount > 0 ? "#FFB347" : "#8293AC";
    public string Age
    {
        get => _age;
        private set { if (_age == value) return; _age = value; PropertyChanged?.Invoke(this, new(nameof(Age))); }
    }
    public void UpdateAge(DateTime now)
    {
        var age = now - Report.TimestampUtc;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        Age = $"{(int)age.TotalMinutes:00}:{age.Seconds:00}";
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    private IReadOnlyList<object> BuildMetadata()
    {
        var tokens = new List<object>();
        tokens.AddRange(Systems.Take(3));
        tokens.AddRange((Details?.ShipsSummary ?? []).Take(3).Select(ship => new IntelHistoryShipToken(ship)));
        tokens.AddRange((Details?.FriendlyShipsSummary ?? []).Take(2).Select(ship => new IntelHistoryShipToken(ship, true)));
        tokens.AddRange((Details?.Hostiles ?? []).Take(3));
        var hidden = Math.Max(0, Systems.Count - 3) + Math.Max(0, (Details?.ShipsSummary.Count ?? 0) - 3) +
            Math.Max(0, (Details?.FriendlyShipsSummary.Count ?? 0) - 2) + Math.Max(0, (Details?.Hostiles.Count ?? 0) - 3);
        if (hidden > 0) tokens.Add(new IntelHistoryOverflowToken($"+{hidden}", string.Join("\n",
            Systems.Select(system => system.Name).Concat((Details?.ShipsSummary ?? []).Select(ship => $"{ship.Count}× {ship.ShipName}"))
                .Concat((Details?.FriendlyShipsSummary ?? []).Select(ship => $"Friendly: {ship.Count}× {ship.ShipName}"))
                .Concat((Details?.Hostiles ?? []).Select(pilot => pilot.Name)))));
        return tokens;
    }
}

public sealed class IntelHistoryPreferences
{
    public bool FollowAllChannels { get; set; } = true;
    public List<string> SelectedChannels { get; set; } = [];
}

/// <summary>UI-thread owned recent channel history. Filtering never changes the retained buffer.</summary>
public sealed class IntelHistoryViewModel : INotifyPropertyChanged
{
    private readonly List<IntelHistoryRow> _rows = [];
    private readonly HashSet<string> _observed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _configured = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private bool _followAll = true;
    private bool _changingChannels;
    private bool _enabled = true;
    private int _expiryMinutes = 30;
    public ObservableCollection<IntelHistoryRow> VisibleRows { get; } = [];
    public ObservableCollection<IntelHistoryChannel> Channels { get; } = [];
    public IReadOnlyList<IntelHistoryRow> Rows => _rows;
    public bool FollowAllChannels
    {
        get => _followAll;
        set
        {
            if (_followAll == value) return;
            _followAll = value;
            RebuildChannels(); RefreshVisible(); Changed(); PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool IsEmpty => VisibleRows.Count == 0;
    public string Summary => $"{VisibleRows.Count} messages";
    public string EmptyMessage => !_enabled ? "Intel parsing is disabled. Enable it in Intel Settings."
        : !_followAll && !Channels.Any(c => c.Selected) ? "Select channels to show their messages."
        : "No recent messages in the selected channels.";
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? PreferencesChanged;
    // Raised around collection updates so the view can preserve a scrolled reading anchor.
    public event EventHandler? RowsChanging;
    public event EventHandler? RowsChanged;

    public IntelHistoryPreferences GetPreferences() => new() { FollowAllChannels = _followAll, SelectedChannels = _selected.Order(StringComparer.OrdinalIgnoreCase).ToList() };

    public void LoadPreferences(IntelHistoryPreferences preferences)
    {
        _followAll = preferences.FollowAllChannels;
        _selected = preferences.SelectedChannels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        RebuildChannels(); RefreshVisible(); Changed(nameof(FollowAllChannels));
    }

    public void Configure(IEnumerable<string> channels, bool enabled, int expiryMinutes, DateTime now)
    {
        _configured = channels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _enabled = enabled; _expiryMinutes = Math.Clamp(expiryMinutes, 1, 240);
        // Removed channels must not remain selectable or reappear from retained messages.
        if (_configured.Count > 0)
        {
            _rows.RemoveAll(r => !_configured.Contains(r.Report.ChannelName));
            _selected.IntersectWith(_configured);
        }
        Tick(now); RebuildChannels(); RefreshVisible();
    }

    public bool Add(IntelHistoryRow row, DateTime now)
    {
        if (!_enabled || row.Report.TimestampUtc < now.AddMinutes(-_expiryMinutes) ||
            (_configured.Count > 0 && !_configured.Contains(row.Report.ChannelName)) ||
            _rows.Any(r => r.Report.DedupeKey == row.Report.DedupeKey && string.Equals(r.Report.ChannelName, row.Report.ChannelName, StringComparison.OrdinalIgnoreCase))) return false;
        _rows.Add(row);
        _rows.Sort((a, b) =>
        {
            var timestamp = b.Report.TimestampUtc.CompareTo(a.Report.TimestampUtc);
            if (timestamp != 0) return timestamp;
            var channel = StringComparer.OrdinalIgnoreCase.Compare(a.Report.ChannelName, b.Report.ChannelName);
            return channel != 0 ? channel : StringComparer.Ordinal.Compare(a.Report.DedupeKey, b.Report.DedupeKey);
        });
        if (_rows.Count > 350) _rows.RemoveRange(350, _rows.Count - 350);
        row.UpdateAge(now);
        if (_observed.Add(row.Report.ChannelName) && _configured.Count == 0) RebuildChannels();
        Tick(now); RefreshVisible();
        return true;
    }

    public void Tick(DateTime now)
    {
        if (_rows.RemoveAll(r => r.Report.TimestampUtc < now.AddMinutes(-_expiryMinutes)) > 0) RefreshVisible();
        foreach (var row in VisibleRows) row.UpdateAge(now);
    }

    public void Clear()
    {
        _rows.Clear(); RefreshVisible();
    }

    private void RebuildChannels()
    {
        _changingChannels = true;
        foreach (var channel in Channels) channel.PropertyChanged -= OnChannelChanged;
        Channels.Clear();
        foreach (var name in (_configured.Count > 0 ? _configured : _observed).Order(StringComparer.OrdinalIgnoreCase))
        {
            var channel = new IntelHistoryChannel { Name = name, Selected = _followAll || _selected.Contains(name) };
            channel.PropertyChanged += OnChannelChanged;
            Channels.Add(channel);
        }
        _changingChannels = false;
    }

    private void OnChannelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_changingChannels) return;
        _followAll = false;
        _selected = Channels.Where(c => c.Selected).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Changed(nameof(FollowAllChannels)); RefreshVisible(); PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshVisible()
    {
        var wanted = _enabled ? _rows.Where(r => _followAll || _selected.Contains(r.Report.ChannelName)).ToList() : [];
        if (!VisibleRows.SequenceEqual(wanted))
        {
            RowsChanging?.Invoke(this, EventArgs.Empty);
            // Keep row identities and only issue actual adds/removes (no per-tick list reset).
            var retained = wanted.ToHashSet();
            for (var i = VisibleRows.Count - 1; i >= 0; i--) if (!retained.Contains(VisibleRows[i])) VisibleRows.RemoveAt(i);
            for (var i = 0; i < wanted.Count; i++)
            {
                if (i < VisibleRows.Count && ReferenceEquals(VisibleRows[i], wanted[i])) continue;
                var existingIndex = VisibleRows.IndexOf(wanted[i]);
                if (existingIndex >= 0) VisibleRows.Move(existingIndex, i);
                else VisibleRows.Insert(i, wanted[i]);
            }
            RowsChanged?.Invoke(this, EventArgs.Empty);
        }
        Changed(nameof(IsEmpty)); Changed(nameof(EmptyMessage)); Changed(nameof(Summary));
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
