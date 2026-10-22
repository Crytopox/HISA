using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Hisa.App;

public partial class FleetBoostTrackerWindow : Window
{
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _shutdown;
    private bool _ready;
    private FleetBoostTrackerViewModel? Vm => DataContext as FleetBoostTrackerViewModel;
    public event EventHandler? SettingsRequested;
    public FleetBoostTrackerWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await SaveAsync(); };
        Closing += async (_, e) => { if (!_shutdown) { e.Cancel = true; Hide(); await SaveAsync(); } };
        PositionChanged += (_, _) => QueueSave();
        SizeChanged += (_, _) => QueueSave();
        Closed += (_, _) => { _saveTimer.Stop(); Vm?.Dispose(); };
    }
    public FleetBoostTrackerWindow(FleetBoostTrackerViewModel vm) : this()
    {
        DataContext = vm;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is "Pinned" or "" or "SelectedChannel") QueueSave(); };
    }
    public async Task InitializeAsync()
    {
        if (Vm is null || _ready) return;
        await Vm.InitializeAsync();
        var p = Vm.Preferences;
        // Migrate the original default size while retaining explicitly resized windows.
        if (p.Width == 800 && p.Height == 330) { p.Width = 720; p.Height = 240; }
        Width = double.IsFinite(p.Width) ? Math.Clamp(p.Width, MinWidth, 2400) : 720;
        Height = double.IsFinite(p.Height) ? Math.Clamp(p.Height, MinHeight, 1600) : 240;
        if (p.X is { } x && p.Y is { } y && Screens.All.Any(s => s.WorkingArea.Intersects(new PixelRect(x, y, 100, 60))))
            Position = new(x, y);
        _ready = true;
    }
    private void QueueSave() { if (!_ready || _shutdown) return; _saveTimer.Stop(); _saveTimer.Start(); }
    private async Task SaveAsync()
    {
        if (Vm is null || !_ready) return;
        var p = Vm.Preferences;
        p.Width = Bounds.Width; p.Height = Bounds.Height; p.X = Position.X; p.Y = Position.Y;
        await Vm.SavePreferencesAsync();
    }
    private void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            !(e.Source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is Button)))
            BeginMoveDrag(e);
    }
    private void OnResize(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthEast, e);
    private async void OnHide(object? sender, RoutedEventArgs e) { Hide(); await SaveAsync(); }
    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private async void OnStart(object? sender, RoutedEventArgs e) { if (Vm is not null) await Vm.StartOrResetAsync(); }
    private void OnSettings(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        // Hiding the flyout detaches its data context, including the button's bound Tag.
        if (Vm is not { } vm || sender is not Button { Tag: string pilot }) return;
        foreach (var button in this.GetVisualDescendants().OfType<Button>()) button.Flyout?.Hide();
        await vm.RemovePilotAsync(pilot);
    }
    public void Shutdown() { _shutdown = true; _saveTimer.Stop(); Close(); }
}
