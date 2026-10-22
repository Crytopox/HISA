using Avalonia.Controls;
using Avalonia.Interactivity;
using Hisa.Core.Abstractions;

namespace Hisa.App;

public partial class FleetBoostSettingsWindow : Window
{
    private readonly ISettingsService? _settings;
    private readonly IFleetBoostTracker? _tracker;
    public FleetBoostSettingsWindow() => InitializeComponent();
    public FleetBoostSettingsWindow(ISettingsService settings, IFleetBoostTracker tracker) : this()
    { _settings = settings; _tracker = tracker; }
    public async Task InitializeAsync()
    {
        if (_settings is not null) ChannelsText.Text = string.Join(Environment.NewLine,
            await _settings.GetAsync<string[]>("FleetBoost.Channels") ?? []);
    }
    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_settings is null || _tracker is null) return;
        SaveButton.IsEnabled = false;
        try
        {
            var channels = (ChannelsText.Text ?? "").Split('\n').Select(c => c.Trim())
                .Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            await _settings.SetAsync("FleetBoost.Channels", channels);
            await _tracker.ApplySettingsAsync();
            ChannelsText.Text = string.Join(Environment.NewLine, channels);
            Feedback.Text = "Saved. Channel choices have been updated.";
        }
        catch (Exception ex) { Feedback.Text = $"Could not save channels: {ex.Message}"; }
        finally { SaveButton.IsEnabled = true; }
    }
}
