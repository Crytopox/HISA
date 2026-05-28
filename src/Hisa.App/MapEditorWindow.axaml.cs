using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Hisa.Core.Models;
using System;
using System.ComponentModel;
using System.Linq;

namespace Hisa.App;

public partial class MapEditorWindow : Window
{
    private bool _isDraggingNode;
    private Point? _lastDragWorldPoint;
    private bool _isBoxSelecting;
    private Point _boxSelectionStartPoint;
    private bool _boxSelectionAdditive;
    private double _snapResidualDx;
    private double _snapResidualDy;
    private bool _isRightPanning;
    private Point _rightPanLastPoint;
    private Point _rightPanStartPoint;
    private bool _rightPanMoved;
    private readonly ContextMenu _mapContextMenu;
    private bool _restoreViewportOnNextGraphChange;
    private MapViewportState? _pendingViewportState;

    public MapEditorWindow()
    {
        InitializeComponent();
        _mapContextMenu = BuildMapContextMenu();
    }

    public MapEditorWindow(MapEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm)
        {
            return;
        }

        await vm.InitialLoadTask;
        EditorMapControl.FitToView();
    }

    private async void OnCreateCustomRegionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm)
        {
            return;
        }

        await ExecuteWithPreservedViewportAsync(() => vm.CreateCustomRegionAsync());
    }

    private async void OnImportGameRegionsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm)
        {
            return;
        }

        var selectedRegionIds = ((GameRegionsList.SelectedItems?.Cast<object>()) ?? Enumerable.Empty<object>())
            .OfType<RegionOption>()
            .Select(r => r.RegionId)
            .Distinct()
            .ToList();
        if (selectedRegionIds.Count == 0)
        {
            return;
        }

        await ExecuteWithPreservedViewportAsync(() => vm.ImportGameRegionsAsync(selectedRegionIds));
    }

    private async void OnDeleteSelectedNodeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm)
        {
            await vm.DeleteSelectedNodeAsync();
        }
    }

    private async void OnSaveCurrentRegionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm)
        {
            return;
        }

        await vm.SaveCurrentRegionAsync();
    }

    private async void OnDeleteLayoutRegionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm)
        {
            return;
        }

        await ExecuteWithPreservedViewportAsync(() => vm.DeleteSelectedLayoutRegionAsync());
    }

    private async void OnExportSelectedRegionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || StorageProvider is null)
        {
            return;
        }

        var suggested = $"{(vm.SelectedLayoutRegion?.Name ?? "custom-region").Replace(' ', '_')}.hisa-region.json";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Custom Region",
            SuggestedFileName = suggested,
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("HISA Region JSON") { Patterns = ["*.hisa-region.json", "*.json"] }
            ]
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await vm.ExportSelectedRegionAsync(path);
    }

    private async void OnImportRegionJsonClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || StorageProvider is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import HISA Region JSON",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("HISA Region JSON") { Patterns = ["*.hisa-region.json", "*.json"] }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ExecuteWithPreservedViewportAsync(() => vm.ImportRegionJsonAsync(path));
    }

    private void OnLayoutRegionsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        PrepareViewportRestoreOnNextGraphChange();
    }

    private void OnEditorMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm)
        {
            return;
        }

        var props = e.GetCurrentPoint(EditorMapControl).Properties;
        var point = e.GetPosition(EditorMapControl);
        if (props.IsRightButtonPressed)
        {
            _isRightPanning = true;
            _rightPanMoved = false;
            _rightPanStartPoint = point;
            _rightPanLastPoint = point;
            _isDraggingNode = false;
            _isBoxSelecting = false;
            SelectionRect.IsVisible = false;

            var hitNodeIdForMenu = EditorMapControl.HitTestNode(point, 10.0);
            if (hitNodeIdForMenu is not null && !vm.IsNodeSelected(hitNodeIdForMenu.Value))
            {
                vm.SetSelectedNodes([hitNodeIdForMenu.Value]);
                EditorMapControl.InvalidateVisual();
            }

            e.Pointer.Capture(EditorMapControl);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed)
        {
            return;
        }

        var hitNodeId = EditorMapControl.HitTestNode(point, 10.0);
        if (hitNodeId is null)
        {
            _isBoxSelecting = true;
            _boxSelectionAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            _boxSelectionStartPoint = point;
            UpdateSelectionRectVisual(_boxSelectionStartPoint, point);
            e.Pointer.Capture(EditorMapControl);
            e.Handled = true;
            if (!_boxSelectionAdditive)
            {
                vm.SetSelectedNodes([]);
                EditorMapControl.InvalidateVisual();
            }
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            vm.ToggleSelection(hitNodeId.Value);
            EditorMapControl.InvalidateVisual();
            return;
        }
        
        // Clicking a non-selected node replaces selection with only that node.
        // Clicking an already selected node keeps the full selection for drag-move.
        if (!vm.IsNodeSelected(hitNodeId.Value))
        {
            vm.SetSelectedNodes([hitNodeId.Value]);
            EditorMapControl.InvalidateVisual();
        }

        if (!EditorMapControl.TryScreenToWorld(point, out var worldPoint))
        {
            return;
        }

        _isDraggingNode = true;
        _lastDragWorldPoint = worldPoint;
        _snapResidualDx = 0;
        _snapResidualDy = 0;
        e.Pointer.Capture(EditorMapControl);
        e.Handled = true;
    }

    private async void OnEditorMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isRightPanning)
        {
            var point = e.GetPosition(EditorMapControl);
            var delta = point - _rightPanLastPoint;
            if ((delta.X * delta.X) + (delta.Y * delta.Y) >= 0.25)
            {
                _rightPanMoved = _rightPanMoved ||
                    Math.Abs(point.X - _rightPanStartPoint.X) > 2.0 ||
                    Math.Abs(point.Y - _rightPanStartPoint.Y) > 2.0;
                EditorMapControl.PanBy(delta.X, delta.Y);
                _rightPanLastPoint = point;
            }

            return;
        }

        if (_isBoxSelecting)
        {
            UpdateSelectionRectVisual(_boxSelectionStartPoint, e.GetPosition(EditorMapControl));
            return;
        }

        if (!_isDraggingNode || DataContext is not MapEditorViewModel vm || _lastDragWorldPoint is null)
        {
            return;
        }

        var currentPoint = e.GetPosition(EditorMapControl);
        if (!EditorMapControl.TryScreenToWorld(currentPoint, out var worldPoint))
        {
            return;
        }

        var dx = worldPoint.X - _lastDragWorldPoint.Value.X;
        var dy = worldPoint.Y - _lastDragWorldPoint.Value.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return;
        }

        _lastDragWorldPoint = worldPoint;
        var freeMove = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (freeMove)
        {
            vm.MoveSelectedNodeAsync(dx, dy, freeMove: true);
            return;
        }

        _snapResidualDx += dx;
        _snapResidualDy += dy;
        var snapStep = vm.GetSnapGridStep();
        var applyDx = Math.Truncate(_snapResidualDx / snapStep) * snapStep;
        var applyDy = Math.Truncate(_snapResidualDy / snapStep) * snapStep;
        if (Math.Abs(applyDx) < 1e-12 && Math.Abs(applyDy) < 1e-12)
        {
            return;
        }

        _snapResidualDx -= applyDx;
        _snapResidualDy -= applyDy;
        vm.MoveSelectedNodeAsync(applyDx, applyDy, freeMove: false);
    }

    private void OnEditorMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isRightPanning)
        {
            _isRightPanning = false;
            e.Pointer.Capture(null);
            if (!_rightPanMoved)
            {
                _mapContextMenu.Placement = PlacementMode.AnchorAndGravity;
                _mapContextMenu.PlacementRect = new Rect(_rightPanStartPoint, new Size(1, 1));
                _mapContextMenu.HorizontalOffset = 2;
                _mapContextMenu.VerticalOffset = 2;
                _mapContextMenu.Open(EditorMapControl);
            }
            e.Handled = true;
            return;
        }

        if (_isBoxSelecting)
        {
            if (DataContext is MapEditorViewModel vm)
            {
                var endPoint = e.GetPosition(EditorMapControl);
                var rect = new Rect(_boxSelectionStartPoint, endPoint);
                var nodeIds = EditorMapControl.GetNodeIdsInScreenRect(rect);
                if (_boxSelectionAdditive)
                {
                    vm.AddToSelection(nodeIds);
                }
                else
                {
                    vm.SetSelectedNodes(nodeIds);
                }
                EditorMapControl.InvalidateVisual();
            }

            _isBoxSelecting = false;
            _boxSelectionAdditive = false;
            SelectionRect.IsVisible = false;
            e.Pointer.Capture(null);
            return;
        }

        if (!_isDraggingNode)
        {
            return;
        }

        _isDraggingNode = false;
        _lastDragWorldPoint = null;
        _snapResidualDx = 0;
        _snapResidualDy = 0;
        e.Pointer.Capture(null);
    }

    private void OnFitViewClicked(object? sender, RoutedEventArgs e)
    {
        EditorMapControl.FitToView();
    }

    private void OnEditorMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
    }

    private async void OnEditorMapKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            await vm.DeleteSelectedNodeAsync();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
        {
            await vm.SaveCurrentRegionAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F)
        {
            EditorMapControl.FitToView();
            e.Handled = true;
        }
    }

    private void OnEditorMapSizeChanged(object? sender, SizeChangedEventArgs e)
    {
    }

    private async void OnMapContextDeleteSelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm)
        {
            await vm.DeleteSelectedNodeAsync();
            EditorMapControl.InvalidateVisual();
        }
    }

    private async void OnMapContextAddMissingConnectedClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm)
        {
            await vm.AddMissingConnectedNodesForSelectionAsync();
            EditorMapControl.InvalidateVisual();
        }
    }

    private ContextMenu BuildMapContextMenu()
    {
        var menu = new ContextMenu
        {
            MinWidth = 0
        };
        var itemPadding = new Thickness(8, 4);
        var addConnected = new MenuItem { Header = "Add Missing Connected", Padding = itemPadding };
        addConnected.Click += OnMapContextAddMissingConnectedClicked;
        var deleteSelected = new MenuItem { Header = "Delete Selected", Padding = itemPadding };
        deleteSelected.Click += OnMapContextDeleteSelectedClicked;
        menu.ItemsSource = new object[] { addConnected, deleteSelected };
        return menu;
    }

    private void UpdateSelectionRectVisual(Point from, Point to)
    {
        var left = Math.Min(from.X, to.X);
        var top = Math.Min(from.Y, to.Y);
        var width = Math.Abs(to.X - from.X);
        var height = Math.Abs(to.Y - from.Y);

        Canvas.SetLeft(SelectionRect, left);
        Canvas.SetTop(SelectionRect, top);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
        SelectionRect.IsVisible = width > 2 && height > 2;
    }

    private async Task ExecuteWithPreservedViewportAsync(Func<Task> action)
    {
        PrepareViewportRestoreOnNextGraphChange();
        await action();
    }

    private void PrepareViewportRestoreOnNextGraphChange()
    {
        _pendingViewportState = EditorMapControl.GetViewportState();
        _restoreViewportOnNextGraphChange = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_restoreViewportOnNextGraphChange || e.PropertyName != nameof(MapEditorViewModel.CurrentGraph))
        {
            return;
        }

        _restoreViewportOnNextGraphChange = false;
        if (_pendingViewportState is { } viewport)
        {
            Dispatcher.UIThread.Post(() => EditorMapControl.SetViewportState(viewport), DispatcherPriority.Render);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MapEditorViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }
}
