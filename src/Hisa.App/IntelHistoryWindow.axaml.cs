using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hisa.App;

public partial class IntelHistoryWindow : Window
{
    private readonly DispatcherTimer _ageTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private MainWindowViewModel? _main;
    private IntelHistoryViewModel? _subscribedModel;
    private bool _shutdown;
    private bool _applyingPlacement;
    private bool _restored;
    private bool _showing;
    private WindowPlacementState? _lastPlacement;
    private IntelHistoryRow? _readingAnchor;
    private double _anchorY;
    private HashSet<IntelHistoryRow> _previousRows = [];
    private int _scrollVersion;
    private sealed record ResizeGesture(WindowEdge Edge, Point PointerStart, PixelPoint WindowStart,
        Size WindowSize, double Scaling, Control Handle, IPointer Pointer);
    private ResizeGesture? _resizeGesture;
    public event Action<long>? SystemNavigationRequested;

    public IntelHistoryWindow()
    {
        InitializeComponent();
        AddHandler(PointerMovedEvent, OnResizePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnResizePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) => SubscribeToModel(DataContext as IntelHistoryViewModel);
        Screens.Changed += OnScreensChanged;
        _ageTimer.Tick += (_, _) => Model?.Tick(DateTime.UtcNow);
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SavePlacementAsync(); };
        Closing += (_, e) =>
        {
            EndResize();
            if (_shutdown) return;
            e.Cancel = true;
            Hide();
            _ = SavePlacementAsync();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                if (IsVisible) { Model?.Tick(DateTime.UtcNow); _ageTimer.Start(); }
                else _ageTimer.Stop();
                if (!_shutdown && _restored && _main is not null) _ = _main.SetIntelHistoryVisibleAsync(IsVisible);
            }
            if (e.Property == BoundsProperty && !_applyingPlacement && _restored && _main is not null)
            {
                _lastPlacement = CapturePlacement();
                ScheduleSave();
            }
        };
        PositionChanged += (_, _) =>
        {
            if (!_applyingPlacement && _restored) { _lastPlacement = CapturePlacement(); ScheduleSave(); }
        };
    }

    public IntelHistoryWindow(MainWindowViewModel main) : this()
    {
        _main = main;
        DataContext = main.IntelHistory;
        main.PropertyChanged += OnMainPropertyChanged;
    }

    private IntelHistoryViewModel? Model => DataContext as IntelHistoryViewModel;

    private void SubscribeToModel(IntelHistoryViewModel? model)
    {
        if (_subscribedModel is not null)
        {
            _subscribedModel.RowsChanging -= OnRowsChanging;
            _subscribedModel.RowsChanged -= OnRowsChanged;
        }
        _subscribedModel = model;
        if (model is not null)
        {
            model.RowsChanging += OnRowsChanging;
            model.RowsChanged += OnRowsChanged;
        }
    }

    public async Task ShowHistoryAsync()
    {
        if (_main is null || _shutdown || _showing) return;
        _showing = true;
        _main.ConfigureIntelHistory();
        _applyingPlacement = true;
        try
        {
            if (!_restored)
            {
                _lastPlacement = await _main.GetIntelHistoryPlacementAsync();
                if (_shutdown) return;
            }
            RecoverPlacement();
            if (_shutdown) return;
            _restored = true;
            Show();
            await SavePlacementAsync();
        }
        finally { _applyingPlacement = false; _showing = false; }
    }

    private void RecoverPlacement()
    {
        var size = _main?.IntelHistoryDefaultSize ?? (380, 520);
        var restored = IntelHistoryPlacement.Restore(_lastPlacement,
            Screens.All.Select(screen => new IntelHistoryScreen(screen.DisplayName, screen.WorkingArea, screen.Scaling, screen.IsPrimary)).ToList(),
            size.Item1, size.Item2);
        Width = restored.Width; Height = restored.Height;
        Position = new PixelPoint(restored.PositionX, restored.PositionY);
        _lastPlacement = restored;
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        if (_shutdown || !_restored) return;
        _applyingPlacement = true;
        try { RecoverPlacement(); }
        finally { _applyingPlacement = false; }
        ScheduleSave();
    }

    private WindowPlacementState CapturePlacement()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var area = screen?.WorkingArea;
        return new()
        {
            Width = Bounds.Width > 0 ? Bounds.Width : Width, Height = Bounds.Height > 0 ? Bounds.Height : Height,
            PositionX = Position.X, PositionY = Position.Y, WindowState = "Normal",
            ScreenDisplayName = screen?.DisplayName, ScreenScaling = screen?.Scaling,
            ScreenWorkingAreaX = area?.X, ScreenWorkingAreaY = area?.Y,
            ScreenWorkingAreaWidth = area?.Width, ScreenWorkingAreaHeight = area?.Height,
            ScreenOffsetX = area is { } bounds ? Position.X - bounds.X : null,
            ScreenOffsetY = area is { } rect ? Position.Y - rect.Y : null
        };
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_main is null) return;
        if (e.PropertyName is nameof(MainWindowViewModel.IntelEnabled) or nameof(MainWindowViewModel.IntelListExpiryMinutes) or nameof(MainWindowViewModel.IntelIncludeChannelsText))
            _main.ConfigureIntelHistory();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private async Task SavePlacementAsync()
    {
        if (_main is null || !_restored) return;
        _lastPlacement = CapturePlacement();
        await _main.SaveIntelHistoryPlacementAsync(_lastPlacement);
    }

    public void Shutdown()
    {
        _shutdown = true; _ageTimer.Stop(); _saveTimer.Stop();
        Screens.Changed -= OnScreensChanged;
        _ = SavePlacementAsync();
        if (_main is not null)
        {
            _main.PropertyChanged -= OnMainPropertyChanged;
        }
        SubscribeToModel(null);
        Close();
    }

    private ScrollViewer? Scroller => HistoryList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnRowsChanging(object? sender, EventArgs e)
    {
        _readingAnchor = null;
        if (!IsVisible || Scroller is not { Offset.Y: > 2 }) return;
        _previousRows = Model!.VisibleRows.ToHashSet();
        var item = HistoryList.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(item => item.DataContext is IntelHistoryRow && item.TranslatePoint(default, HistoryList) is { } point && point.Y + item.Bounds.Height > 0)
            .OrderBy(item => item.TranslatePoint(default, HistoryList)!.Value.Y).FirstOrDefault();
        if (item?.DataContext is IntelHistoryRow row && item.TranslatePoint(default, HistoryList) is { } top)
        {
            _readingAnchor = row; _anchorY = top.Y;
        }
    }

    private void OnRowsChanged(object? sender, EventArgs e)
    {
        var version = ++_scrollVersion;
        var anchor = _readingAnchor;
        var anchorY = _anchorY;
        if (anchor is not null && Model!.VisibleRows.Any(row => !_previousRows.Contains(row))) NewReportsButton.IsVisible = true;
        if (Model!.VisibleRows.Count == 0) NewReportsButton.IsVisible = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _scrollVersion || !IsVisible || Scroller is not { } scroll) return;
            if (anchor is null) { scroll.Offset = new Vector(0, 0); return; }
            if (!Model!.VisibleRows.Contains(anchor)) return;
            HistoryList.ScrollIntoView(anchor);
            HistoryList.UpdateLayout();
            var item = HistoryList.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, anchor));
            if (item?.TranslatePoint(default, HistoryList) is { } point)
                scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + point.Y - anchorY));
        }, DispatcherPriority.Loaded);
    }

    private void OnNewReportsClicked(object? sender, RoutedEventArgs e)
    {
        ++_scrollVersion;
        if (Model?.VisibleRows.FirstOrDefault() is { } first) HistoryList.ScrollIntoView(first);
        if (Scroller is { } scroll) scroll.Offset = new Vector(0, 0);
        NewReportsButton.IsVisible = false;
    }

    private void OnHideClicked(object? sender, RoutedEventArgs e) => Close();
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            e.Source is Visual visual && !visual.GetSelfAndVisualAncestors().OfType<Button>().Any())
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }
    private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || sender is not Control { Tag: string tag } handle ||
            !Enum.TryParse<WindowEdge>(tag, out var edge)) return;
        EndResize();
        _resizeGesture = new(edge, GetResizePointerPosition(e), Position, Bounds.Size, RenderScaling, handle, e.Pointer);
        handle.PointerCaptureLost += OnResizePointerCaptureLost;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnResizePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeGesture is not { } resize || e.Pointer != resize.Pointer) return;
        var point = GetResizePointerPosition(e);
        var dx = (point.X - resize.PointerStart.X) / resize.Scaling;
        var dy = (point.Y - resize.PointerStart.Y) / resize.Scaling;
        var west = resize.Edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
        var east = resize.Edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
        var north = resize.Edge is WindowEdge.North or WindowEdge.NorthEast or WindowEdge.NorthWest;
        var south = resize.Edge is WindowEdge.South or WindowEdge.SouthEast or WindowEdge.SouthWest;
        var width = Math.Max(MinWidth, resize.WindowSize.Width + (west ? -dx : east ? dx : 0));
        var height = Math.Max(MinHeight, resize.WindowSize.Height + (north ? -dy : south ? dy : 0));
        Width = width; Height = height;
        if (west || north)
            Position = new PixelPoint(resize.WindowStart.X + (west ? (int)Math.Round((resize.WindowSize.Width - width) * resize.Scaling) : 0),
                resize.WindowStart.Y + (north ? (int)Math.Round((resize.WindowSize.Height - height) * resize.Scaling) : 0));
        e.Handled = true;
    }

    private void OnResizePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizeGesture is null || e.Pointer != _resizeGesture.Pointer) return;
        EndResize(); e.Handled = true;
    }

    private void OnResizePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndResize();

    private Point GetResizePointerPosition(PointerEventArgs e)
    {
        var origin = this.PointToScreen(default);
        var point = e.GetPosition(this);
        // Preserve fractional pixels across the client origin and scaled monitors.
        return new Point(origin.X + point.X * RenderScaling, origin.Y + point.Y * RenderScaling);
    }

    private void EndResize()
    {
        if (_resizeGesture is not { } resize) return;
        _resizeGesture = null;
        resize.Handle.PointerCaptureLost -= OnResizePointerCaptureLost;
        if (resize.Pointer.Captured == resize.Handle) resize.Pointer.Capture(null);
        if (_restored && !_shutdown) { _lastPlacement = CapturePlacement(); ScheduleSave(); }
    }
    private async void OnSystemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelHistorySystem system }) return;
        try
        {
            var id = system.Id;
            if (id <= 0 && _main is not null) id = await _main.ResolveSystemIdByNameAsync(system.Name);
            if (id > 0 && !_shutdown) SystemNavigationRequested?.Invoke(id);
        }
        catch { /* A failed lookup must not interrupt reading the history. */ }
    }
    private void OnPilotClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard { CharacterId: > 0 } pilot }) return;
        try { Process.Start(new ProcessStartInfo($"https://zkillboard.com/character/{pilot.CharacterId}/") { UseShellExecute = true }); }
        catch { /* The history remains usable if the browser cannot open. */ }
    }
}
