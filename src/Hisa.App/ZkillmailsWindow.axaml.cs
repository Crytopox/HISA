using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;

namespace Hisa.App;

public partial class ZkillmailsWindow : Window
{
    public ZkillmailsWindow()
    {
        InitializeComponent();
    }

    public ZkillmailsWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnZkillmailLinkClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ZkillmailOverlayCard card } || string.IsNullOrWhiteSpace(card.KillmailUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = card.KillmailUrl,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private async void OnZkillmailCardSystemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Control { DataContext: ZkillmailOverlayCard card } ||
            card.SolarSystemId <= 0)
        {
            return;
        }

        await vm.NavigateToSystemFromReportAsync(card.SolarSystemId);
    }

    private void OnIntelHostilePortraitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.CharacterId is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://zkillboard.com/character/{hostile.CharacterId.Value}/",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void OnIntelHostileCorporationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.CorporationId is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://zkillboard.com/corporation/{hostile.CorporationId.Value}/",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void OnIntelHostileAllianceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.AllianceId is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://zkillboard.com/alliance/{hostile.AllianceId.Value}/",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void OnZkillVictimPortraitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ZkillmailOverlayCard card } || card.Victim.CharacterId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/character/{card.Victim.CharacterId.Value}/");
    }

    private void OnZkillVictimCorporationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ZkillmailOverlayCard card } || card.Victim.CorporationId is null)
        {
            return;
        }

        OpenUrl($"https://zkillboard.com/corporation/{card.Victim.CorporationId.Value}/");
    }

    private void OnZkillVictimAllianceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ZkillmailOverlayCard card } || card.Victim.AllianceId is null)
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
}
