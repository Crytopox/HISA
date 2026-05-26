using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Hisa.App;

public partial class MainWindow : Window
{
    private bool _clearSearchOnNextFocus;

    public MainWindow()
    {
        InitializeComponent();
        MainMapControl.UniverseRegionNodeDoubleClicked += OnUniverseRegionNodeDoubleClicked;
    }

    public MainWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnFitCenterClicked(object? sender, RoutedEventArgs e)
    {
        MainMapControl.FitToView();
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await ExecuteSearchAsync();
            e.Handled = true;
        }
    }

    private async Task ExecuteSearchAsync(Hisa.Core.Models.MapSearchCandidate? explicitCandidate = null)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var focus = await vm.ExecuteSearchAsync(explicitCandidate);
        if (focus is not null)
        {
            MainMapControl.FocusOnSearch(focus);
        }

        vm.SelectedSearchSuggestion = null;
        vm.ClearSearchSuggestions();
    }

    private async void OnSearchSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedSearchSuggestion is null)
        {
            return;
        }

        var picked = vm.SelectedSearchSuggestion;
        vm.MapSearchText = picked.Name;
        _clearSearchOnNextFocus = true;
        await ExecuteSearchAsync(picked);
    }

    private void OnSearchBoxGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (!_clearSearchOnNextFocus || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.MapSearchText = string.Empty;
        _clearSearchOnNextFocus = false;
    }

    private async void OnUniverseRegionNodeDoubleClicked(object? sender, int regionId)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await vm.OpenRegionFromUniverseRegionsNodeAsync(regionId);
        MainMapControl.FitToView();
    }
}
