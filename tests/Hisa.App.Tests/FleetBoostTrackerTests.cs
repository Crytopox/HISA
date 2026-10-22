using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.App;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.App.Tests;

public class FleetBoostTrackerTests
{
    [AvaloniaTheory]
    [InlineData(720, 240)] [InlineData(680, 240)]
    public async Task CompactWindowShowsAllChargesCountsAndPilotFlyout(int width, int height)
    {
        var tracker = new FakeTracker();
        var settings = new MemorySettings();
        var vm = new FleetBoostTrackerViewModel(tracker, settings);
        var window = new FleetBoostTrackerWindow(vm);
        await window.InitializeAsync();
        window.Width = width; window.Height = height;
        window.Show();
        try
        {
            await vm.StartOrResetAsync();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains("Waiting for pilots", window.FindControl<TextBlock>("StatusFooter")!.Text);
            tracker.Report("Fleet", "Crytopox Haakario", ["shield-extension", "shield-harmonizing"], true);
            tracker.Report("Fleet", "Another pilot", ["shield-extension"], false);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(4, vm.Columns.Count);
            Assert.All(vm.Columns, c => Assert.Equal(3, c.Rows.Count));
            var row = vm.Columns.SelectMany(c => c.Rows).Single(r => r.Charge.Id == "shield-extension");
            Assert.Equal(2, row.Count); Assert.Equal("1 ML", row.MindlinkCount);
            Assert.NotNull(row.Icon);
            Assert.Contains("2 pilots", vm.Summary);
            Assert.Equal("Reset tracking", vm.ActionLabel);
            var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is FleetBoostChargeRow).ToArray();
            Assert.Equal(12, buttons.Length);
            Assert.True(window.ShowInTaskbar);
            Assert.Contains(window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>(), b => Equals(b.Content, "Always above"));
            Assert.All(buttons, b => { Assert.True(b.Bounds.Width > 100); Assert.InRange(b.Bounds.Height, 32, 40); });
            Assert.All(buttons, b => Assert.True(b.TranslatePoint(new Point(0, b.Bounds.Height), window)!.Value.Y < height - 24));
            var button = buttons.Single(b => b.DataContext is FleetBoostChargeRow r && r.Charge.Id == "shield-extension");
            button.Flyout!.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.Flyout.IsOpen);
            Assert.Equal("Crytopox Haakario", row.Pilots.Single(p => p.MindlinkLabel == "ML").Name);
            var content = Assert.IsAssignableFrom<Control>(((Flyout)button.Flyout).Content);
            Assert.Contains(content.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Crytopox Haakario");
            button.Flyout.Hide();
            using var image = new RenderTargetBitmap(new(width, height)); image.Render(window);
            image.Save(Path.Combine(AppContext.BaseDirectory, $"fleet-boost-{width}-{height}.png"));
            button.Flyout.ShowAt(button); Dispatcher.UIThread.RunJobs();
            var remove = content.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "Crytopox Haakario"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, vm.Columns[0].Rows.Single(r => r.Charge.Id == "shield-extension").Count);
            Assert.Equal(0, vm.Columns[0].Rows.Single(r => r.Charge.Id == "shield-harmonizing").Count);
        }
        finally { window.Shutdown(); }
    }

    [AvaloniaFact]
    public async Task ChannelSwitchAndHiddenWindowPreserveSessionsAndPersistPreferences()
    {
        var tracker = new FakeTracker(); var settings = new MemorySettings();
        var vm = new FleetBoostTrackerViewModel(tracker, settings);
        var window = new FleetBoostTrackerWindow(vm);
        await window.InitializeAsync(); window.Show();
        try
        {
            await vm.StartOrResetAsync(); tracker.Report("Fleet", "One", ["shield-extension"], true);
            vm.SelectedChannel = "Other";
            await vm.StartOrResetAsync(); tracker.Report("Other", "Two", ["rapid-repair"], false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Other", vm.SelectedChannel);
            Assert.Equal(1, vm.Columns[1].Rows.Single(r => r.Charge.Id == "rapid-repair").Count);
            window.Hide(); tracker.Report("Fleet", "Three", ["shield-extension"], false);
            Dispatcher.UIThread.RunJobs(); window.Show(); vm.SelectedChannel = "Fleet";
            Assert.Equal(2, vm.Columns[0].Rows.Single(r => r.Charge.Id == "shield-extension").Count);
            vm.Pinned = true; await vm.SavePreferencesAsync();
            var saved = (FleetBoostWindowPreferences)settings.Values[FleetBoostTrackerViewModel.PreferencesKey];
            Assert.True(saved.Pinned); Assert.Equal("Fleet", saved.SelectedChannel);
            await vm.StartOrResetAsync();
            Assert.All(vm.Columns.SelectMany(c => c.Rows), r => Assert.Equal(0, r.Count));
            vm.SelectedChannel = "Other";
            Assert.Equal(1, vm.Columns[1].Rows.Single(r => r.Charge.Id == "rapid-repair").Count);
        }
        finally { window.Shutdown(); }
    }

    [AvaloniaFact]
    public async Task EmptyConfigurationAndUnavailableLogsHaveClearStates()
    {
        var tracker = new FakeTracker { ConfiguredChannels = [] };
        using var vm = new FleetBoostTrackerViewModel(tracker, new MemorySettings());
        await vm.InitializeAsync();
        Assert.False(vm.HasChannel); Assert.Contains("settings", vm.EmptyMessage);
        tracker.ConfiguredChannels = ["Fleet"]; await tracker.ApplySettingsAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.HasChannel); Assert.Contains("Start tracking", vm.EmptyMessage);
        tracker.Status = "Logs unavailable — check Preferences"; await vm.StartOrResetAsync();
        Assert.Contains("Logs unavailable", vm.Summary);
    }

    [AvaloniaFact]
    public async Task HiddenWindowSavesBoundsAndNewWindowRestoresPreferencesWithoutStarting()
    {
        var tracker = new FakeTracker(); var settings = new MemorySettings();
        settings.Values[FleetBoostTrackerViewModel.PreferencesKey] = new FleetBoostWindowPreferences { Width = 800, Height = 330 };
        var vm = new FleetBoostTrackerViewModel(tracker, settings);
        var window = new FleetBoostTrackerWindow(vm);
        await window.InitializeAsync(); window.Show();
        Assert.Equal(720, window.Width); Assert.Equal(240, window.Height);
        window.FindControl<Button>("MinimizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Minimized, window.WindowState);
        window.WindowState = WindowState.Normal;
        window.Width = 920; window.Height = 390; window.Position = new(50, 50);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        vm.SelectedChannel = "Other"; vm.Pinned = true;
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
        var saved = (FleetBoostWindowPreferences)settings.Values[FleetBoostTrackerViewModel.PreferencesKey];
        Assert.Equal(920, saved.Width); Assert.Equal(390, saved.Height);
        Assert.Equal("Other", saved.SelectedChannel); Assert.True(saved.Pinned);
        window.Shutdown();
        var restoredVm = new FleetBoostTrackerViewModel(new FakeTracker(), settings);
        var restored = new FleetBoostTrackerWindow(restoredVm);
        try
        {
            await restored.InitializeAsync();
            Assert.Equal(920, restored.Width); Assert.Equal(390, restored.Height);
            Assert.True(restoredVm.Pinned); Assert.Equal("Other", restoredVm.SelectedChannel);
            Assert.Equal("Start tracking", restoredVm.ActionLabel);
        }
        finally { restored.Shutdown(); }
    }

    private sealed class FakeTracker : IFleetBoostTracker
    {
        public string[] ConfiguredChannels = ["Fleet", "Other"];
        public string Status = "Listening";
        private readonly Dictionary<string, FleetBoostSession> _sessions = [];
        public event EventHandler? Changed;
        public IReadOnlyList<string> Channels => ConfiguredChannels;
        public FleetBoostSnapshot GetSnapshot(string channel) => _sessions.TryGetValue(channel, out var session)
            ? new(channel, session.StartedUtc, session.Pilots, Status) : new(channel, null, [], "Not started");
        public Task ApplySettingsAsync(CancellationToken cancellationToken = default) { Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public Task StartOrResetAsync(string channel, CancellationToken cancellationToken = default)
        { _sessions[channel] = new(DateTime.UtcNow); Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public Task RemovePilotAsync(string channel, string pilot, CancellationToken cancellationToken = default)
        { _sessions[channel].Remove(pilot); Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public void Report(string channel, string pilot, string[] charges, bool ml)
        { _sessions[channel].Apply(new(DateTime.UtcNow, pilot, charges, ml)); Changed?.Invoke(this, EventArgs.Empty); }
    }
    private sealed class MemorySettings : ISettingsService
    {
        public readonly Dictionary<string, object> Values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(Values.TryGetValue(key, out var value) ? (T?)value : default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) { Values[key] = value!; return Task.CompletedTask; }
    }
}
