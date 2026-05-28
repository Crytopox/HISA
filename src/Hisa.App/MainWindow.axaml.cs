using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Hisa.App;

public partial class MainWindow : Window
{
    private bool _clearSearchOnNextFocus;
    private bool _isApplyingWindowPlacement;
    private bool _isApplyingViewport;
    private MainWindowViewModel? _boundVm;
    private Hisa.Core.Models.MapViewMode _lastKnownViewMode;
    private readonly DebugWindowViewModel? _debugWindowViewModel;
    private DebugWindow? _debugWindow;
    private PreferencesWindow? _preferencesWindow;
    private MapEditorWindow? _mapEditorWindow;
    private SovUpgradesWindow? _sovUpgradesWindow;

    public MainWindow()
    {
        InitializeComponent();
        MainMapControl.UniverseRegionNodeDoubleClicked += OnUniverseRegionNodeClicked;
        Opened += OnOpened;
        Closing += (_, _) =>
        {
            SaveWindowPlacementNow();
            SaveViewportNow();
            SaveSelectedViewModeNow();
        };
    }

    public MainWindow(MainWindowViewModel vm, DebugWindowViewModel debugWindowViewModel) : this()
    {
        DataContext = vm;
        _boundVm = vm;
        _debugWindowViewModel = debugWindowViewModel;
        _lastKnownViewMode = vm.SelectedViewMode;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnOpenDebugConsoleClicked(object? sender, RoutedEventArgs e)
    {
        if (_debugWindow is null)
        {
            if (_debugWindowViewModel is null)
            {
                return;
            }
            _debugWindow = new DebugWindow(_debugWindowViewModel);
            _debugWindow.Closed += (_, _) => _debugWindow = null;
        }

        _debugWindow.Show();
        _debugWindow.Activate();
    }

    private void OnOpenPreferencesClicked(object? sender, RoutedEventArgs e)
    {
        if (_preferencesWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _preferencesWindow = new PreferencesWindow(_boundVm);
            _preferencesWindow.Closed += (_, _) => _preferencesWindow = null;
        }

        _preferencesWindow.Show();
        _preferencesWindow.Activate();
    }

    private void OnOpenMapEditorClicked(object? sender, RoutedEventArgs e)
    {
        if (_mapEditorWindow is null)
        {
            var vm = Program.Host?.Services.GetRequiredService<MapEditorViewModel>();
            if (vm is null)
            {
                return;
            }

            _mapEditorWindow = new MapEditorWindow(vm);
            _mapEditorWindow.Closed += (_, _) => _mapEditorWindow = null;
        }

        _mapEditorWindow.Show();
        _mapEditorWindow.Activate();
    }

    private void OnOpenSovUpgradesClicked(object? sender, RoutedEventArgs e)
    {
        if (_sovUpgradesWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _sovUpgradesWindow = new SovUpgradesWindow(_boundVm);
            _sovUpgradesWindow.Closed += (_, _) => _sovUpgradesWindow = null;
        }

        _sovUpgradesWindow.Show();
        _sovUpgradesWindow.Activate();
    }

    private void OnFitCenterClicked(object? sender, RoutedEventArgs e)
    {
        MainMapControl.FitToView();
    }

    private async void OnSelectAllIndicatorSovFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        await _boundVm.SelectAllIndicatorSovFilterAsync();
    }

    private async void OnUnselectAllIndicatorSovFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        await _boundVm.UnselectAllIndicatorSovFilterAsync();
    }

    private async void OnSelectAllOverlaySovFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        await _boundVm.SelectAllOverlaySovFilterAsync();
    }

    private async void OnUnselectAllOverlaySovFilterClicked(object? sender, RoutedEventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        await _boundVm.UnselectAllOverlaySovFilterAsync();
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

    private async void OnUniverseRegionNodeClicked(object? sender, int regionId)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await vm.OpenRegionFromUniverseRegionsNodeAsync(regionId);
        MainMapControl.FitToView();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        await _boundVm.InitialLoadTask;
        await _boundVm.RestoreSelectedViewModeAsync();

        var placement = await _boundVm.GetWindowPlacementAsync();
        if (placement is not null)
        {
            _isApplyingWindowPlacement = true;
            try
            {
                Width = Math.Max(640, placement.Width);
                Height = Math.Max(420, placement.Height);
                Position = new Avalonia.PixelPoint(placement.PositionX, placement.PositionY);
                if (Enum.TryParse<WindowState>(placement.WindowState, out var parsedState))
                {
                    WindowState = parsedState;
                }
            }
            finally
            {
                _isApplyingWindowPlacement = false;
            }
        }

        await RestoreViewportForCurrentModeAsync(fallbackToFit: true);
    }

    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedViewMode))
        {
            SaveViewportForMode(_lastKnownViewMode);
            _lastKnownViewMode = _boundVm.SelectedViewMode;
            await RestoreViewportForCurrentModeAsync(fallbackToFit: true);
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentGraph))
        {
            await RestoreViewportForCurrentModeAsync(fallbackToFit: true);
        }
    }

    private async Task RestoreViewportForCurrentModeAsync(bool fallbackToFit)
    {
        if (_boundVm is null || _isApplyingViewport)
        {
            return;
        }

        _isApplyingViewport = true;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(30);
                var saved = await _boundVm.GetViewportAsync(_boundVm.SelectedViewMode);
                if (saved is not null)
                {
                    MainMapControl.SetViewportState(saved);
                }
                else if (fallbackToFit)
                {
                    MainMapControl.FitToView();
                }
            });
        }
        finally
        {
            _isApplyingViewport = false;
        }
    }

    private void SaveWindowPlacementNow()
    {
        if (_boundVm is null || _isApplyingWindowPlacement)
        {
            return;
        }

        var placement = new WindowPlacementState
        {
            Width = Width,
            Height = Height,
            PositionX = Position.X,
            PositionY = Position.Y,
            WindowState = WindowState.ToString()
        };

        _ = _boundVm.SaveWindowPlacementAsync(placement);
    }

    private void SaveViewportNow()
    {
        if (_boundVm is null || _isApplyingViewport)
        {
            return;
        }

        SaveViewportForMode(_boundVm.SelectedViewMode);
    }

    private void SaveViewportForMode(Hisa.Core.Models.MapViewMode mode)
    {
        if (_boundVm is null || _isApplyingViewport)
        {
            return;
        }

        var state = MainMapControl.GetViewportState();
        _ = _boundVm.SaveViewportAsync(mode, state);
    }

    private void SaveSelectedViewModeNow()
    {
        if (_boundVm is null)
        {
            return;
        }

        _boundVm.SaveSelectedViewModeAsync().GetAwaiter().GetResult();
    }
}
