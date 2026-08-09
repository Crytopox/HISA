using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Media;
using Hisa.App.ViewModels;
using System.Diagnostics;

namespace Hisa.App;

public partial class AlertPopupWindow : Window
{
    private bool _isDragModeEnabled;

    public bool IsDragModeEnabled
    {
        get => _isDragModeEnabled;
        set
        {
            if (_isDragModeEnabled == value)
            {
                return;
            }

            _isDragModeEnabled = value;
            ApplyDragModeVisuals();
        }
    }
    public event EventHandler<PixelPoint>? DragPositionCommitted;
    public event Action<long>? SystemNavigationRequested;

    public AlertPopupWindow()
    {
        InitializeComponent();
        PositionChanged += OnWindowPositionChanged;
    }

    private void ApplyDragModeVisuals()
    {
        RootBorder.Background = IsDragModeEnabled
            ? Brush.Parse("#B01A2232")
            : null;
        RootBorder.BorderBrush = IsDragModeEnabled
            ? Brush.Parse("#5D83B5")
            : null;
        RootBorder.BorderThickness = IsDragModeEnabled
            ? new Thickness(1)
            : new Thickness(0);
        RootBorder.CornerRadius = IsDragModeEnabled
            ? new CornerRadius(6)
            : new CornerRadius(0);
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDragModeEnabled)
        {
            return;
        }

        try
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
        catch
        {
        }
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsDragModeEnabled)
        {
            return;
        }

        DragPositionCommitted?.Invoke(this, Position);
        e.Handled = true;
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!IsDragModeEnabled)
        {
            return;
        }

        DragPositionCommitted?.Invoke(this, e.Point);
    }

    private void OnZkillmailLinkClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertPopupCard { ZkillmailCard: { } card } } || string.IsNullOrWhiteSpace(card.KillmailUrl))
        {
            return;
        }

        OpenUrl(card.KillmailUrl);
    }

    private void OnIntelHostilePortraitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.CharacterId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/character/{hostile.CharacterId.Value}/");
    }

    private void OnIntelHostileCorporationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.CorporationId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/corporation/{hostile.CorporationId.Value}/");
    }

    private void OnIntelHostileAllianceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.AllianceId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/alliance/{hostile.AllianceId.Value}/");
    }

    private void OnZkillVictimPortraitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertPopupCard { ZkillmailCard: { } card } } || card.Victim.CharacterId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/character/{card.Victim.CharacterId.Value}/");
    }

    private void OnZkillVictimCorporationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertPopupCard { ZkillmailCard: { } card } } || card.Victim.CorporationId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/corporation/{card.Victim.CorporationId.Value}/");
    }

    private void OnZkillVictimAllianceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertPopupCard { ZkillmailCard: { } card } } || card.Victim.AllianceId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/alliance/{card.Victim.AllianceId.Value}/");
    }

    private static void OpenUrl(string url)
    {
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
        }
    }

    private void OnIntelCardSystemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertPopupCard { IntelCard: { } card } } ||
            card.SolarSystemId <= 0)
        {
            return;
        }

        SystemNavigationRequested?.Invoke(card.SolarSystemId);
    }

    private void OnZkillCardSystemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertPopupCard { ZkillmailCard: { } card } } ||
            card.SolarSystemId <= 0)
        {
            return;
        }

        SystemNavigationRequested?.Invoke(card.SolarSystemId);
    }

    private void OnEnvironmentalCardSystemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AlertPopupCard { EnvironmentalCard: { } card } } ||
            card.SolarSystemId <= 0)
        {
            return;
        }

        SystemNavigationRequested?.Invoke(card.SolarSystemId);
    }
}
