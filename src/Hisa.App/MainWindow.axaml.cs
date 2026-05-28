using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Linq;

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
    private readonly ContextMenu _mapNodeContextMenu;
    private readonly MenuItem _copySystemNameMenuItem;
    private readonly MenuItem _openInViewMenuItem;
    private readonly MenuItem _openInDotlanMenuItem;
    private readonly MenuItem _openInZkillboardMenuItem;
    private Point? _mapRightPressPoint;
    private bool _mapRightMoved;
    private string? _contextSystemName;
    private long? _contextSystemId;
    private int? _contextRegionId;
    private int? _contextConstellationId;

    public MainWindow()
    {
        InitializeComponent();

        int subMenufontSize = 13;

        _copySystemNameMenuItem = new MenuItem
        {
            Header = "Copy System Name",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3)
        };
        _copySystemNameMenuItem.Classes.Add("map-node-menu-item");
        _copySystemNameMenuItem.Click += OnCopySystemNameClicked;
        _openInViewMenuItem = new MenuItem
        {
            Header = "Open in Universe",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3)
        };
        _openInViewMenuItem.Classes.Add("map-node-menu-item");
        _openInViewMenuItem.Click += OnOpenInViewClicked;
        _openInDotlanMenuItem = new MenuItem
        {
            Header = "Open in Dotlan",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3),
            Icon = BuildMenuIcon("dotlan.ico")
        };
        _openInDotlanMenuItem.Classes.Add("map-node-menu-item");
        _openInDotlanMenuItem.Click += OnOpenInDotlanClicked;
        _openInZkillboardMenuItem = new MenuItem
        {
            Header = "Open in zKillboard",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3),
            Icon = BuildMenuIcon("zkillboard.png")
        };
        _openInZkillboardMenuItem.Classes.Add("map-node-menu-item");
        _openInZkillboardMenuItem.Click += OnOpenInZkillboardClicked;
        _mapNodeContextMenu = new ContextMenu
        {
            MinWidth = 0,
            FontSize = subMenufontSize,
            ItemsSource = new object[] { _copySystemNameMenuItem, _openInViewMenuItem, new Separator(), _openInDotlanMenuItem, _openInZkillboardMenuItem }
        };
        _mapNodeContextMenu.Classes.Add("map-node-menu");
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
            _mapEditorWindow.Closed += async (_, _) =>
            {
                _mapEditorWindow = null;
                if (_boundVm is not null)
                {
                    await _boundVm.RefreshRegionOptionsAsync();
                }
            };
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

    private void OnMainMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(MainMapControl).Properties;
        if (!props.IsRightButtonPressed)
        {
            return;
        }

        _mapRightPressPoint = e.GetPosition(MainMapControl);
        _mapRightMoved = false;
    }

    private void OnMainMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mapRightPressPoint is null)
        {
            return;
        }

        var point = e.GetPosition(MainMapControl);
        var dx = point.X - _mapRightPressPoint.Value.X;
        var dy = point.Y - _mapRightPressPoint.Value.Y;
        if ((dx * dx) + (dy * dy) > 16.0)
        {
            _mapRightMoved = true;
        }
    }

    private void OnMainMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_mapRightPressPoint is null || _mapRightMoved)
            {
                return;
            }

            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (vm.SelectedViewMode is not (Hisa.Core.Models.MapViewMode.Universe or Hisa.Core.Models.MapViewMode.Region))
            {
                return;
            }

            var point = e.GetPosition(MainMapControl);
            var nodeId = MainMapControl.HitTestNode(point, 12.0);
            if (nodeId is null)
            {
                return;
            }

            var node = vm.CurrentGraph?.Nodes.FirstOrDefault(n => n.Id == nodeId.Value);
            if (node is null || string.IsNullOrWhiteSpace(node.Name))
            {
                return;
            }

            vm.SelectedNodeId = node.Id;
            _contextSystemName = node.Name.Trim();
            _contextSystemId = node.Id;
            _contextRegionId = node.RegionId;
            _contextConstellationId = node.ConstellationId;
            _openInViewMenuItem.Header = vm.SelectedViewMode == Hisa.Core.Models.MapViewMode.Universe
                ? "Open in Region"
                : "Open in Universe";
            _copySystemNameMenuItem.Header = $"Copy '{_contextSystemName}'";
            ConfigureMapNodeMenuPlacement(point);
            _mapNodeContextMenu.Open(MainMapControl);
            e.Handled = true;
        }
        finally
        {
            _mapRightPressPoint = null;
            _mapRightMoved = false;
        }
    }

    private async void OnCopySystemNameClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_contextSystemName))
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(_contextSystemName);
    }

    private void OnOpenInDotlanClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_contextSystemName))
        {
            return;
        }

        var escapedSystem = Uri.EscapeDataString(_contextSystemName.Trim());
        var url = $"https://evemaps.dotlan.net/system/{escapedSystem}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failures from shell/browser launch.
        }
    }

    private void OnOpenInZkillboardClicked(object? sender, RoutedEventArgs e)
    {
        if (_contextSystemId is null)
        {
            return;
        }

        var url = $"https://zkillboard.com/system/{_contextSystemId.Value}/";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failures from shell/browser launch.
        }
    }

    private async void OnOpenInViewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || _contextSystemId is null || string.IsNullOrWhiteSpace(_contextSystemName))
        {
            return;
        }

        var systemId = _contextSystemId.Value;
        if (vm.SelectedViewMode == Hisa.Core.Models.MapViewMode.Universe)
        {
            if (_contextRegionId is null)
            {
                return;
            }

            await vm.OpenRegionFromUniverseRegionsNodeAsync(_contextRegionId.Value);
        }
        else if (vm.SelectedViewMode == Hisa.Core.Models.MapViewMode.Region)
        {
            vm.SelectedViewMode = Hisa.Core.Models.MapViewMode.Universe;
            await WaitForNodeInGraphAsync(vm, systemId, 1200);
        }
        else
        {
            return;
        }

        var focus = new Hisa.Core.Models.MapSearchFocus
        {
            Kind = Hisa.Core.Models.MapSearchKind.SolarSystem,
            SolarSystemId = systemId,
            RegionId = _contextRegionId,
            ConstellationId = _contextConstellationId
        };
        vm.SelectedNodeId = systemId;
        MainMapControl.FocusOnSearch(focus);
        await FocusSelectedNodeNearCenterAsync(focus, systemId);
    }

    private void ConfigureMapNodeMenuPlacement(Point clickPoint)
    {
        const double estimatedMenuWidth = 210;
        const double estimatedMenuHeight = 120;
        const double offset = 3;
        const double margin = 10;

        var availableRight = MainMapControl.Bounds.Width - clickPoint.X;
        var availableBottom = MainMapControl.Bounds.Height - clickPoint.Y;
        var canOpenRight = availableRight >= estimatedMenuWidth + margin;
        var canOpenBottom = availableBottom >= estimatedMenuHeight + margin;

        var placement = canOpenRight
            ? (canOpenBottom ? PlacementMode.BottomEdgeAlignedLeft : PlacementMode.TopEdgeAlignedLeft)
            : (canOpenBottom ? PlacementMode.BottomEdgeAlignedRight : PlacementMode.TopEdgeAlignedRight);

        _mapNodeContextMenu.Placement = placement;
        _mapNodeContextMenu.PlacementRect = new Rect(clickPoint, new Size(1, 1));
        _mapNodeContextMenu.HorizontalOffset = offset;
        _mapNodeContextMenu.VerticalOffset = offset;
    }

    private static async Task WaitForNodeInGraphAsync(MainWindowViewModel vm, long nodeId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (vm.CurrentGraph?.Nodes.Any(n => n.Id == nodeId) == true)
            {
                return;
            }

            await Task.Delay(30);
        }
    }

    private async Task FocusSelectedNodeNearCenterAsync(Hisa.Core.Models.MapSearchFocus focus, long nodeId)
    {
        // Re-apply focus after UI/layout settles so the selected node lands near center reliably.
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(40);
                if (_boundVm?.CurrentGraph?.Nodes.Any(n => n.Id == nodeId) == true)
                {
                    MainMapControl.FocusOnSearch(focus);
                }
            });
        }
    }

    private static Control? BuildMenuIcon(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://Hisa.App/Assets/Icons/{fileName}");
            using var stream = AssetLoader.Open(uri);
            var bitmap = new Bitmap(stream);
            return new Image
            {
                Source = bitmap,
                Width = 12,
                Height = 12
            };
        }
        catch
        {
            return null;
        }
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
            if (_boundVm.SelectedViewMode == Hisa.Core.Models.MapViewMode.Region)
            {
                await Dispatcher.UIThread.InvokeAsync(() => MainMapControl.FitToView());
                return;
            }

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
