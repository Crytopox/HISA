using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed record FleetBoostPilotRow(string Name, string MindlinkLabel, string TimeLabel);
public sealed record FleetBoostChargeRow(FleetBoostCharge Charge, IReadOnlyList<FleetBoostPilotRow> Pilots, Bitmap? Icon)
{
    public string Name => Charge.ShortName;
    public int Count => Pilots.Count;
    public string MindlinkCount => $"{Pilots.Count(p => p.MindlinkLabel == "ML")} ML";
    public double Emphasis => Count > 0 ? 1 : 0.48;
    public bool IsEmpty => Count == 0;
}
public sealed record FleetBoostColumn(string Name, string Accent, IReadOnlyList<FleetBoostChargeRow> Rows);
public sealed class FleetBoostWindowPreferences
{
    public string? SelectedChannel { get; set; }
    public bool Pinned { get; set; }
    public double Width { get; set; } = 720;
    public double Height { get; set; } = 240;
    public int? X { get; set; }
    public int? Y { get; set; }
}

public sealed class FleetBoostTrackerViewModel : INotifyPropertyChanged, IDisposable
{
    public const string PreferencesKey = "FleetBoost.Window";
    private readonly IFleetBoostTracker _tracker;
    private readonly ISettingsService _settings;
    private readonly Dictionary<string, Bitmap?> _icons = new();
    private readonly SemaphoreSlim _save = new(1, 1);
    private string? _selectedChannel;
    private bool _disposed;
    public FleetBoostWindowPreferences Preferences { get; private set; } = new();
    public IReadOnlyList<string> Channels { get; private set; } = [];
    public IReadOnlyList<FleetBoostColumn> Columns { get; private set; } = [];
    public string? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (value is null && Channels.Count > 0) return;
            if (_selectedChannel == value) return;
            _selectedChannel = value;
            Preferences.SelectedChannel = value;
            Refresh();
        }
    }
    public bool Pinned
    {
        get => Preferences.Pinned;
        set { Preferences.Pinned = value; Notify(); }
    }
    public bool HasChannel => SelectedChannel is not null;
    public string ActionLabel { get; private set; } = "Start tracking";
    public string Summary { get; private set; } = "";
    public string EmptyMessage { get; private set; } = "";
    public string FooterMessage { get; private set; } = "";
    public string Error { get; private set; } = "";
    public bool HasEmptyMessage => EmptyMessage.Length > 0;
    public bool HasError => Error.Length > 0;
    public event PropertyChangedEventHandler? PropertyChanged;

    public FleetBoostTrackerViewModel(IFleetBoostTracker tracker, ISettingsService settings)
    {
        _tracker = tracker;
        _settings = settings;
        tracker.Changed += OnChanged;
        Refresh();
    }
    public async Task InitializeAsync()
    {
        Preferences = await _settings.GetAsync<FleetBoostWindowPreferences>(PreferencesKey) ?? new();
        await _tracker.ApplySettingsAsync();
        _selectedChannel = Channels.FirstOrDefault(c => c.Equals(Preferences.SelectedChannel, StringComparison.OrdinalIgnoreCase)) ?? Channels.FirstOrDefault();
        Refresh();
    }
    public async Task StartOrResetAsync()
    {
        if (SelectedChannel is { } channel) await _tracker.StartOrResetAsync(channel);
        Refresh();
    }
    public async Task RemovePilotAsync(string pilot)
    {
        if (SelectedChannel is { } channel) await _tracker.RemovePilotAsync(channel, pilot);
        Refresh();
    }
    public async Task SavePreferencesAsync()
    {
        await _save.WaitAsync();
        try
        {
            Preferences.SelectedChannel = SelectedChannel;
            await _settings.SetAsync(PreferencesKey, Preferences);
            Error = "";
        }
        catch (Exception ex) { Error = $"Could not save window preferences: {ex.Message}"; }
        finally { _save.Release(); Notify(nameof(Error)); Notify(nameof(HasError)); }
    }
    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { if (!_disposed) Refresh(); });
    public void Refresh()
    {
        var channels = _tracker.Channels;
        if (!Channels.SequenceEqual(channels)) Channels = channels;
        if (!Channels.Contains(_selectedChannel, StringComparer.OrdinalIgnoreCase)) _selectedChannel = Channels.FirstOrDefault();
        var snapshot = _selectedChannel is null ? null : _tracker.GetSnapshot(_selectedChannel);
        ActionLabel = snapshot?.StartedUtc is null ? "Start tracking" : "Reset tracking";
        EmptyMessage = Channels.Count == 0 ? "Add fleet channels in Boost Tracker settings to begin."
            : snapshot?.StartedUtc is null ? "Choose a channel and click Start tracking."
            : snapshot.Pilots.Count == 0 ? "Waiting for pilots to post their boost charges…" : "";
        Summary = snapshot is null ? "No channels configured"
            : snapshot.StartedUtc is { } start ? $"{snapshot.Status}  ·  Since {start:HH:mm:ss} UTC  ·  {snapshot.Pilots.Count} pilots"
            : "Not started · Only messages posted after Start tracking will count";
        FooterMessage = snapshot?.StartedUtc is null ? EmptyMessage
            : HasEmptyMessage ? $"{EmptyMessage}  ·  {Summary}" : Summary;
        var colors = new[] { "#77BFFF", "#E3B987", "#A7A3F5", "#81D5B1" };
        Columns = Enum.GetValues<FleetBoostFamily>().Select(f => new FleetBoostColumn(f.ToString(), colors[(int)f],
            FleetBoostCatalog.Charges.Where(c => c.Family == f).Select(c => new FleetBoostChargeRow(c,
                (snapshot?.Pilots ?? []).Where(p => p.ChargeIds.Contains(c.Id)).Select(p => new FleetBoostPilotRow(
                    p.Name, p.Mindlink ? "ML" : "", p.LastReportUtc.ToString("HH:mm:ss 'UTC'"))).ToArray(), Icon(c.Id))).ToArray())).ToArray();
        Notify("");
    }
    private Bitmap? Icon(string id)
    {
        if (_icons.TryGetValue(id, out var icon)) return icon;
        var uri = new Uri($"avares://HISA/Assets/Icons/Boosts/{id}.png");
        if (AssetLoader.Exists(uri)) { using var stream = AssetLoader.Open(uri); icon = new Bitmap(stream); }
        _icons[id] = icon;
        return icon;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        _disposed = true;
        _tracker.Changed -= OnChanged;
        foreach (var icon in _icons.Values) icon?.Dispose();
    }
}
