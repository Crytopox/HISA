using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Hisa.App;

public partial class MiningStatsWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(5);
    private MiningCharacterBreakdownWindow? _characterBreakdownWindow;
    private readonly DispatcherTimer _autoRefreshTimer;
    private bool _isApplyingWindowPlacement;

    public MiningStatsWindow()
    {
        InitializeComponent();
        _autoRefreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        PositionChanged += OnWindowPositionChanged;
    }

    public MiningStatsWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var placement = await vm.GetMiningStatsWindowPlacementAsync();
            if (placement is not null)
            {
                _isApplyingWindowPlacement = true;
                try
                {
                    Width = Math.Max(MinWidth, placement.Width);
                    Height = Math.Max(MinHeight, placement.Height);
                    Position = new Avalonia.PixelPoint(placement.PositionX, placement.PositionY);
                }
                finally
                {
                    _isApplyingWindowPlacement = false;
                }
            }

            await vm.RefreshMiningStatsForSelectedRangeAsync();
            if (!IsVisible)
            {
                return;
            }

            _autoRefreshTimer.Start();
            if (vm.ShouldRestoreMiningOverlayVisible)
            {
                MiningOverlayWindow.ShowFor(vm);
            }
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _autoRefreshTimer.Stop();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.FlushMiningOverlaySettingsAsync().GetAwaiter().GetResult();
        }
        SaveWindowPlacement(waitForCompletion: true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _autoRefreshTimer.Stop();
        if (_characterBreakdownWindow is not null)
        {
            _characterBreakdownWindow.Close();
            _characterBreakdownWindow = null;
        }
    }

    private async void OnOverlaySettingsClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.FlushMiningOverlaySettingsAsync();
        }
    }

    private async void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RefreshMiningStatsForSelectedRangeAsync();
        }
    }

    private async void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.IsMiningStatsLoading)
        {
            return;
        }

        await vm.RefreshMiningStatsForSelectedRangeAsync();
    }

    private async void OnApplyRefineYieldClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.SaveMiningRefineYieldAsync();
        }
    }

    private void OnToggleOverlayClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (MiningOverlayWindow.Current is { } overlay)
        {
            overlay.Close();
            return;
        }

        MiningOverlayWindow.ShowFor(vm);
    }

    private void OnResetOverlayPositionClicked(object? sender, RoutedEventArgs e)
    {
        MiningOverlayWindow.Current?.ResetPosition();
    }

    private void OnOpenCharacterBreakdownClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (_characterBreakdownWindow is null)
        {
            _characterBreakdownWindow = new MiningCharacterBreakdownWindow(vm);
            _characterBreakdownWindow.Closed += (_, _) => _characterBreakdownWindow = null;
            _characterBreakdownWindow.Show();
            return;
        }

        _characterBreakdownWindow.Show();
        _characterBreakdownWindow.Activate();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!_isApplyingWindowPlacement)
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

        var placement = new WindowPlacementState
        {
            Width = Width,
            Height = Height,
            PositionX = Position.X,
            PositionY = Position.Y,
            WindowState = WindowState.ToString()
        };

        if (waitForCompletion)
        {
            vm.SaveMiningStatsWindowPlacementAsync(placement).GetAwaiter().GetResult();
        }
        else
        {
            _ = vm.SaveMiningStatsWindowPlacementAsync(placement);
        }
    }
}
