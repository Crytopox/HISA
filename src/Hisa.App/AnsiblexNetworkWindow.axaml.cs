using Avalonia.Controls;
using Avalonia.Interactivity;
using Hisa.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace Hisa.App;

public partial class AnsiblexNetworkWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ObservableCollection<AnsiblexLinkListRow> _rows = [];

    public AnsiblexNetworkWindow()
    {
        InitializeComponent();
        _vm = null!;
    }

    public AnsiblexNetworkWindow(MainWindowViewModel vm) : this()
    {
        _vm = vm;
        SavedLinksList.ItemsSource = _rows;
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

    private async Task RefreshSavedListAsync()
    {
        var snapshot = await _vm.GetAnsiblexSnapshotAsync();
        _rows.Clear();
        foreach (var link in snapshot)
        {
            _rows.Add(new AnsiblexLinkListRow
            {
                FromSystemName = link.FromSolarSystemName,
                ToSystemName = link.ToSolarSystemName
            });
        }
    }
}
