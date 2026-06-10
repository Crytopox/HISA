using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Linq;

namespace Hisa.App;

public partial class MiningOverlayWindow : Window
{
    public static MiningOverlayWindow? Current { get; private set; }

    private bool _isApplyingWindowPlacement;
    private bool _isRestoringInitialPlacement = true;

    public MiningOverlayWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        PositionChanged += OnWindowPositionChanged;
    }

    public MiningOverlayWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Current = this;

        if (DataContext is not MainWindowViewModel vm)
        {
            _isRestoringInitialPlacement = false;
            return;
        }

        var placement = await vm.GetMiningOverlayWindowPlacementAsync();
        if (placement is null)
        {
            _isRestoringInitialPlacement = false;
            return;
        }

        ApplyWindowPlacement(placement);
        Dispatcher.UIThread.Post(() =>
        {
            ApplyWindowPlacement(placement);
            _isRestoringInitialPlacement = false;
        }, DispatcherPriority.Background);
    }

    private void ApplyWindowPlacement(WindowPlacementState placement)
    {
        _isApplyingWindowPlacement = true;
        try
        {
            var restoredPosition = new Avalonia.PixelPoint(placement.PositionX, placement.PositionY);
            var mainWindow = GetMainWindow();
            if (mainWindow is not null &&
                placement.MainWindowOffsetX is int mainOffsetX &&
                placement.MainWindowOffsetY is int mainOffsetY)
            {
                restoredPosition = new Avalonia.PixelPoint(
                    mainWindow.Position.X + mainOffsetX,
                    mainWindow.Position.Y + mainOffsetY);
            }

            var matchedScreen = TryFindSavedScreen(placement);
            if (mainWindow is null &&
                matchedScreen is not null &&
                placement.ScreenOffsetX is int offsetX &&
                placement.ScreenOffsetY is int offsetY)
            {
                var workingArea = matchedScreen.WorkingArea;
                restoredPosition = new Avalonia.PixelPoint(
                    workingArea.X + offsetX,
                    workingArea.Y + offsetY);
            }

            Position = restoredPosition;
        }
        finally
        {
            _isApplyingWindowPlacement = false;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveWindowPlacement(waitForCompletion: true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
        catch
        {
        }
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!_isApplyingWindowPlacement && !_isRestoringInitialPlacement)
        {
            SaveWindowPlacement();
        }
    }

    private void SaveWindowPlacement(bool waitForCompletion = false)
    {
        if (_isApplyingWindowPlacement || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(this);
        var mainWindow = GetMainWindow();
        var placement = new WindowPlacementState
        {
            Width = Width,
            Height = Height,
            PositionX = Position.X,
            PositionY = Position.Y,
            WindowState = WindowState.ToString(),
            ScreenWorkingAreaX = screen?.WorkingArea.X,
            ScreenWorkingAreaY = screen?.WorkingArea.Y,
            ScreenWorkingAreaWidth = screen?.WorkingArea.Width,
            ScreenWorkingAreaHeight = screen?.WorkingArea.Height,
            ScreenOffsetX = screen is null ? null : Position.X - screen.WorkingArea.X,
            ScreenOffsetY = screen is null ? null : Position.Y - screen.WorkingArea.Y,
            MainWindowOffsetX = mainWindow is null ? null : Position.X - mainWindow.Position.X,
            MainWindowOffsetY = mainWindow is null ? null : Position.Y - mainWindow.Position.Y
        };

        if (waitForCompletion)
        {
            vm.SaveMiningOverlayWindowPlacementAsync(placement).GetAwaiter().GetResult();
        }
        else
        {
            _ = vm.SaveMiningOverlayWindowPlacementAsync(placement);
        }
    }

    private Screen? TryFindSavedScreen(WindowPlacementState placement)
    {
        if (Screens is null ||
            placement.ScreenWorkingAreaX is not int workingAreaX ||
            placement.ScreenWorkingAreaY is not int workingAreaY ||
            placement.ScreenWorkingAreaWidth is not int workingAreaWidth ||
            placement.ScreenWorkingAreaHeight is not int workingAreaHeight)
        {
            return null;
        }

        return Screens.All.FirstOrDefault(screen =>
        {
            var workingArea = screen.WorkingArea;
            return workingArea.X == workingAreaX &&
                   workingArea.Y == workingAreaY &&
                   workingArea.Width == workingAreaWidth &&
                   workingArea.Height == workingAreaHeight;
        });
    }

    private static Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }
}
