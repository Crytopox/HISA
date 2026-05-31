using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hisa.Core.Models;
using System.Collections.ObjectModel;

namespace Hisa.App;

public partial class SovUpgradesWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ObservableCollection<SovUpgradeListRow> _rows = [];
    private readonly List<ManualSovUpgradeOption> _manualOptions =
    [
        new() { UpgradeName = "Major Threat Detection Array", MaxTier = 3 },
        new() { UpgradeName = "Minor Threat Detection Array", MaxTier = 3 },
        new() { UpgradeName = "Exploration Detector", MaxTier = 3 },
        new() { UpgradeName = "Advanced Logistics Network", MaxTier = 1 },
        new() { UpgradeName = "Cynosural Navigation", MaxTier = 1 },
        new() { UpgradeName = "Cynosural Suppression", MaxTier = 1 },
        new() { UpgradeName = "Electric Stability Generator", MaxTier = 1 },
        new() { UpgradeName = "Exotic Stability Generator", MaxTier = 1 },
        new() { UpgradeName = "Gamma Stability Generator", MaxTier = 1 },
        new() { UpgradeName = "Plasma Stability Generator", MaxTier = 1 },
        new() { UpgradeName = "Supercapital Construction Facilities", MaxTier = 1 }
    ];

    public SovUpgradesWindow()
    {
        InitializeComponent();
        _vm = null!;
    }

    public SovUpgradesWindow(MainWindowViewModel vm) : this()
    {
        _vm = vm;
        SavedUpgradesList.ItemsSource = _rows;
        ManualUpgradeCombo.ItemsSource = _manualOptions;
        ManualUpgradeCombo.SelectedIndex = 0;
        ManualUpgradeCombo.SelectionChanged += OnManualUpgradeSelectionChanged;
        Opened += async (_, _) => await RefreshSavedListAsync();
    }

    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        var mode = ImportModeCombo.SelectedIndex switch
        {
            1 => SovImportMode.UpdateOnChange,
            2 => SovImportMode.Append,
            _ => SovImportMode.Replace
        };

        var result = await _vm.ImportSovUpgradesAsync(ImportTextBox.Text ?? string.Empty, mode);
        StatusText.Text = $"Imported systems: {result.ParsedSystems}, upgrades: {result.ParsedUpgrades}, total systems stored: {result.TotalSystemsAfterImport}.";
        await RefreshSavedListAsync();
    }

    private async void OnAddManualClicked(object? sender, RoutedEventArgs e)
    {
        var system = ManualSystemBox.Text?.Trim() ?? string.Empty;
        var upgrade = (ManualUpgradeCombo.SelectedItem as ManualSovUpgradeOption)?.UpgradeName ?? string.Empty;
        var tier = (ManualTierCombo.SelectedIndex + 1);
        await _vm.AddOrUpdateSovUpgradeAsync(system, upgrade, tier);
        StatusText.Text = $"Saved upgrade for system '{system}'.";
        await RefreshSavedListAsync();
    }

    private async void OnRemoveSystemClicked(object? sender, RoutedEventArgs e)
    {
        var system = RemoveSystemBox.Text?.Trim() ?? string.Empty;
        await _vm.RemoveSovSystemAsync(system);
        StatusText.Text = $"Removed SOV upgrades for system '{system}' (if it existed).";
        await RefreshSavedListAsync();
    }

    private async Task RefreshSavedListAsync()
    {
        var snapshot = await _vm.GetSovUpgradeSnapshotAsync();
        _rows.Clear();
        foreach (var system in snapshot)
        {
            foreach (var upgrade in system.Upgrades)
            {
                _rows.Add(new SovUpgradeListRow
                {
                    SystemName = system.SolarSystemName,
                    UpgradeName = upgrade.UpgradeName,
                    TierText = $"T{upgrade.Tier}",
                    Icon = LoadSovUpgradeIcon(upgrade)
                });
            }
        }
    }

    private static Bitmap? LoadSovUpgradeIcon(SovUpgradeEntry upgrade)
    {
        var fileName = IsSingleLevelSovUpgrade(upgrade.UpgradeName)
            ? $"{upgrade.UpgradeName}.png"
            : $"{upgrade.UpgradeName} {Math.Clamp(upgrade.Tier, 1, 3)}.png";
        try
        {
            var uri = new Uri($"avares://HISA/Assets/Icons/SOV Upgrades/{fileName}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSingleLevelSovUpgrade(string upgradeName)
    {
        return upgradeName is "Advanced Logistics Network"
            or "Cynosural Navigation"
            or "Cynosural Suppression"
            or "Electric Stability Generator"
            or "Exotic Stability Generator"
            or "Gamma Stability Generator"
            or "Plasma Stability Generator"
            or "Supercapital Construction Facilities";
    }

    private void OnManualUpgradeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = ManualUpgradeCombo.SelectedItem as ManualSovUpgradeOption;
        var maxTier = selected?.MaxTier ?? 3;
        for (var i = 0; i < ManualTierCombo.ItemCount; i++)
        {
            if (ManualTierCombo.ContainerFromIndex(i) is ComboBoxItem item)
            {
                item.IsEnabled = (i + 1) <= maxTier;
            }
        }

        if ((ManualTierCombo.SelectedIndex + 1) > maxTier)
        {
            ManualTierCombo.SelectedIndex = maxTier - 1;
        }
    }
}
