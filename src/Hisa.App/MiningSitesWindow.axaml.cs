using Avalonia.Controls;
using Avalonia.Interactivity;
using Hisa.App.ViewModels;
using Hisa.Core.Models;
using System.Collections.ObjectModel;

namespace Hisa.App;

public partial class MiningSitesWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ObservableCollection<MiningSiteReportRow> _rows = [];
    private readonly List<MiningSiteReportRow> _allRows = [];
    private readonly long? _systemId;

    public MiningSitesWindow() { InitializeComponent(); _vm = null!; }
    public MiningSitesWindow(MainWindowViewModel vm, long? systemId = null) : this()
    {
        _vm = vm; _systemId = systemId; ReportsList.ItemsSource = _rows;
        Opened += async (_, _) => await RefreshAsync();
    }
    private async void OnRefreshClicked(object? sender, RoutedEventArgs e) => await RefreshAsync();
    private void OnStatusFilterChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();
    private async void OnClearedClicked(object? sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } row) return;
        await _vm.MarkMiningSiteClearedAsync(row.SolarSystemId, row.UpgradeName, row.Tier); StatusText.Text = "Site marked cleared; its normal respawn timer is running."; await RefreshAsync();
    }
    private async void OnMissingClicked(object? sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } row) return;
        await _vm.MarkMiningSiteMissingAsync(row.SolarSystemId, row.UpgradeName, row.Tier, SelectedDelay(row.Tier)); StatusText.Text = "Site marked missing; reminder scheduled in UTC."; await RefreshAsync();
    }
    private async void OnReadyClicked(object? sender, RoutedEventArgs e)
    {
        if (Row(sender) is not { } row) return;
        await _vm.MarkMiningSiteAvailableAsync(row.SolarSystemId, row.UpgradeName, row.Tier); StatusText.Text = "Site marked available."; await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        var reports = await _vm.GetMiningSiteReportsAsync();
        var upgrades = await _vm.GetSovUpgradeSnapshotAsync();
        var knownSites = upgrades.Where(system => !_systemId.HasValue || system.SolarSystemId == _systemId.Value)
            .SelectMany(system => system.Upgrades
                .Where(upgrade => upgrade.UpgradeName.EndsWith(" Prospecting Array", StringComparison.OrdinalIgnoreCase))
                .Select(upgrade => new { System = system, Upgrade = upgrade }));
        var reportByKey = reports.ToDictionary(x => $"{x.SolarSystemId}|{x.UpgradeName}|{x.Tier}", StringComparer.OrdinalIgnoreCase);
        _allRows.Clear();
        foreach (var item in knownSites)
        {
            var key = $"{item.System.SolarSystemId}|{item.Upgrade.UpgradeName}|{item.Upgrade.Tier}";
            reportByKey.TryGetValue(key, out var report);
            _allRows.Add(new MiningSiteReportRow { SolarSystemId = item.System.SolarSystemId, SystemName = item.System.SolarSystemName, UpgradeName = item.Upgrade.UpgradeName, Tier = item.Upgrade.Tier, Status = report?.Status ?? MiningSiteStatus.Available, AvailableAtUtc = report?.AvailableAtUtc });
        }
        foreach (var report in reports.Where(x => (!_systemId.HasValue || x.SolarSystemId == _systemId.Value) && !_allRows.Any(r => r.SolarSystemId == x.SolarSystemId && r.UpgradeName.Equals(x.UpgradeName, StringComparison.OrdinalIgnoreCase) && r.Tier == x.Tier)))
            _allRows.Add(new MiningSiteReportRow { SolarSystemId = report.SolarSystemId, SystemName = report.SolarSystemName, UpgradeName = report.UpgradeName, Tier = report.Tier, Status = report.Status, AvailableAtUtc = report.AvailableAtUtc });
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var requested = StatusFilterCombo?.SelectedIndex ?? 0;
        var filtered = _allRows.Where(row => requested switch
        {
            1 => row.Status == MiningSiteStatus.Available,
            2 => row.Status == MiningSiteStatus.Missing,
            3 => row.Status == MiningSiteStatus.Cleared,
            4 => row.Status is MiningSiteStatus.Missing or MiningSiteStatus.Cleared,
            _ => true
        }).OrderBy(row => row.SystemName, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.UpgradeName, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Tier).ToList();
        _rows.Clear();
        foreach (var row in filtered) _rows.Add(row);
        if (StatusText is not null)
        {
            StatusText.Text = _allRows.Count == 0
                ? "No mineral prospecting arrays have been found from SOV upgrades."
                : $"Showing {_rows.Count} of {_allRows.Count} mining site(s)";
        }
    }
    private TimeSpan SelectedDelay(int tier)
    {
        if (double.TryParse(CustomHoursBox.Text, out var custom) && custom > 0) return TimeSpan.FromHours(custom);
        return ReminderPresetCombo.SelectedIndex switch { 0 => TimeSpan.FromHours(3), 2 => TimeSpan.FromHours(8), 3 => TimeSpan.FromHours(11), _ => TimeSpan.FromHours(5) };
    }
    private static MiningSiteReportRow? Row(object? sender) => (sender as Control)?.DataContext as MiningSiteReportRow;
}
