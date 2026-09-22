using Avalonia.Controls;
using Avalonia.Interactivity;
using Hisa.Core;
using Hisa.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace Hisa.App;

public partial class AnsiblexNetworkWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ObservableCollection<AnsiblexLinkListRow> _rows = [];
    private bool _suppressShipClassChange;

    public AnsiblexNetworkWindow()
    {
        InitializeComponent();
        _vm = null!;
    }

    public AnsiblexNetworkWindow(MainWindowViewModel vm) : this()
    {
        _vm = vm;
        SavedLinksList.ItemsSource = _rows;
        ShipClassCombo.ItemsSource = AnsiblexZones.ShipClasses.ToList();
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

        var result = await _vm.ImportAnsiblexNetworkAsync(ImportTextBox.Text ?? string.Empty, mode);
        var unresolvedPreview = result.UnresolvedSystemNamesCount > 0
            ? $" Unresolved systems ({result.UnresolvedSystemNamesCount}): {string.Join(", ", result.UnresolvedSystemNames.Take(8))}{(result.UnresolvedSystemNamesCount > 8 ? "..." : string.Empty)}."
            : string.Empty;
        StatusText.Text =
            $"Imported links: {result.ParsedLinks}, skipped duplicates/invalid: {result.DuplicateOrInvalidLinksSkipped}, total links stored: {result.TotalLinksAfterImport}.{unresolvedPreview}";
        await RefreshSavedListAsync();
    }

    private async void OnAddManualClicked(object? sender, RoutedEventArgs e)
    {
        var from = ManualFromSystemBox.Text?.Trim() ?? string.Empty;
        var to = ManualToSystemBox.Text?.Trim() ?? string.Empty;
        await _vm.AddOrUpdateAnsiblexLinkAsync(from, to);
        StatusText.Text = $"Saved Ansiblex link: {from} <-> {to}.";
        await RefreshSavedListAsync();
    }

    private async void OnRemoveLinkClicked(object? sender, RoutedEventArgs e)
    {
        var from = ManualFromSystemBox.Text?.Trim() ?? string.Empty;
        var to = ManualToSystemBox.Text?.Trim() ?? string.Empty;
        await _vm.RemoveAnsiblexLinkAsync(from, to);
        StatusText.Text = $"Removed Ansiblex link: {from} <-> {to} (if it existed).";
        await RefreshSavedListAsync();
    }

    private async void OnSetCapitalClicked(object? sender, RoutedEventArgs e)
    {
        var name = CapitalSystemBox.Text?.Trim() ?? string.Empty;
        var saved = await _vm.SetAnsiblexCapitalAsync(name);
        StatusText.Text = saved
            ? $"Capital set to {name}."
            : $"Unknown system: {name}.";
        await RefreshSavedListAsync();
    }

    private async void OnShipClassChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressShipClassChange || _vm is null || ShipClassCombo.SelectedItem is not AnsiblexShipClass ship)
        {
            return;
        }

        await _vm.SetAnsiblexShipClassAsync(ship.Name);
        await RefreshSavedListAsync();
    }

    private async Task RefreshSavedListAsync()
    {
        var snapshot = await _vm.GetAnsiblexPlanningSnapshotAsync();
        CapitalStatusText.Text = string.IsNullOrWhiteSpace(snapshot.CapitalSystemName)
            ? "No capital set"
            : $"Capital: {snapshot.CapitalSystemName}";
        SummaryText.Text = snapshot.Summary;
        _suppressShipClassChange = true;
        ShipClassCombo.SelectedItem = AnsiblexZones.ShipClasses.FirstOrDefault(x =>
            string.Equals(x.Name, snapshot.ShipClassName, StringComparison.OrdinalIgnoreCase));
        _suppressShipClassChange = false;
        _rows.Clear();
        foreach (var link in snapshot.Links)
        {
            _rows.Add(new AnsiblexLinkListRow
            {
                FromSystemName = link.FromSystemName,
                FromZoneText = link.FromZoneText,
                ToSystemName = link.ToSystemName,
                ToZoneText = link.ToZoneText,
                TowardToText = link.TowardToText,
                TowardFromText = link.TowardFromText
            });
        }
    }
}
