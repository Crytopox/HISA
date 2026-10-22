using Avalonia.Interactivity;
using Hisa.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Hisa.App;

public partial class MainWindow
{
    private FleetBoostTrackerWindow? _fleetBoostTrackerWindow;
    private FleetBoostSettingsWindow? _fleetBoostSettingsWindow;

    private async void OnOpenFleetBoostTrackerClicked(object? sender, RoutedEventArgs e)
    {
        if (Program.Host?.Services is not { } services) return;
        if (_fleetBoostTrackerWindow is null)
        {
            _fleetBoostTrackerWindow = new(new FleetBoostTrackerViewModel(
                services.GetRequiredService<IFleetBoostTracker>(), services.GetRequiredService<ISettingsService>()));
            _fleetBoostTrackerWindow.SettingsRequested += (_, _) => OnOpenFleetBoostSettingsClicked(this, new());
            _fleetBoostTrackerWindow.Closed += (_, _) => _fleetBoostTrackerWindow = null;
            Closed += (_, _) => _fleetBoostTrackerWindow?.Shutdown();
            await _fleetBoostTrackerWindow.InitializeAsync();
        }
        if (_isClosingApp || _fleetBoostTrackerWindow is null) return;
        if (_fleetBoostTrackerWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
            _fleetBoostTrackerWindow.WindowState = Avalonia.Controls.WindowState.Normal;
        _fleetBoostTrackerWindow.Show();
        _fleetBoostTrackerWindow.Activate();
    }

    private async void OnOpenFleetBoostSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (Program.Host?.Services is not { } services) return;
        if (_fleetBoostSettingsWindow is null)
        {
            _fleetBoostSettingsWindow = new(services.GetRequiredService<ISettingsService>(), services.GetRequiredService<IFleetBoostTracker>());
            _fleetBoostSettingsWindow.Closed += (_, _) => _fleetBoostSettingsWindow = null;
            Closed += (_, _) => _fleetBoostSettingsWindow?.Close();
            await _fleetBoostSettingsWindow.InitializeAsync();
        }
        if (_isClosingApp || _fleetBoostSettingsWindow is null) return;
        _fleetBoostSettingsWindow.Show();
        _fleetBoostSettingsWindow.Activate();
    }
}
