using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace Hisa.App;

public partial class MainWindow
{
    private TrayIcon? _trayIcon;
    private bool _hiddenToTray;

    internal void InitializeTray(Application app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        void CreateTray()
        {
            var menu = new NativeMenu();
            void Add(string label, Action action)
            {
                var item = new NativeMenuItem { Header = label };
                item.Click += (_, _) => Dispatcher.UIThread.Post(action);
                menu.Items.Add(item);
            }

            Add("Open HISA", RestoreFromTray);
            menu.Items.Add(new NativeMenuItemSeparator());
            Add("Jabber", () => ShowXmppWindow());
            Add("Intel History", OpenTrayIntelHistory);
            Add("Fleet Boost Tracker", () => OnOpenFleetBoostTrackerClicked(this, new()));
            Add("Mining Sites Tracker", () => OpenMiningSitesWindow());
            Add("Mining Stats", () => OnOpenMiningStatsWindowClicked(this, new()));
            Add("Preferences", () => OnOpenPreferencesClicked(this, new()));
            menu.Items.Add(new NativeMenuItemSeparator());
            Add("Quit", () => desktop.Shutdown());

            var icon = new TrayIcon { Icon = Icon, ToolTipText = "HISA", Menu = menu };
            if (icon.NativeMenuExporter is null)
            {
                icon.Dispose();
                return;
            }

            icon.Clicked += (_, _) => Dispatcher.UIThread.Post(RestoreFromTray);
            _trayIcon = icon;
            TrayIcon.SetIcons(app, new TrayIcons { icon });
        }

        if (!OperatingSystem.IsLinux())
        {
            CreateTray();
            return;
        }

        // A Linux tray implementation can exist even when the desktop has no
        // active host. Restore the main window if the host vanishes.
        var trayMonitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        var probing = false;
        string? activeHost = null;
        async void RefreshTray(object? sender, EventArgs args)
        {
            if (probing || _isClosingApp) return;
            probing = true;
            try
            {
                var host = await GetLinuxTrayHostAsync();
                if (_isClosingApp) return;

                if (_trayIcon is not null && host == activeHost) return;
                if (_trayIcon is not null)
                {
                    TrayIcon.SetIcons(app, null);
                    _trayIcon = null;
                    if (_hiddenToTray) RestoreFromTray();
                }

                if (host is not null) CreateTray();
                activeHost = host;
            }
            finally { probing = false; }
        }

        trayMonitor.Tick += RefreshTray;
        desktop.Exit += (_, _) => trayMonitor.Stop();
        trayMonitor.Start();
        RefreshTray(null, EventArgs.Empty);
    }

    private static async Task<string?> GetLinuxTrayHostAsync()
    {
        const string watcher = "org.kde.StatusNotifierWatcher";
        try
        {
            if (Address.Session is not { Length: > 0 } address) return null;
            using var connection = new Connection(address);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await connection.ConnectAsync().AsTask().WaitAsync(timeout.Token);

            MessageBuffer ownerMessage;
            {
                using var ownerRequest = connection.GetMessageWriter();
                ownerRequest.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus",
                    "org.freedesktop.DBus", "GetNameOwner", "s");
                ownerRequest.WriteString(watcher);
                ownerMessage = ownerRequest.CreateMessage();
            }
            var owner = await connection.CallMethodAsync(ownerMessage,
                static (message, _) => message.GetBodyReader().ReadString()).WaitAsync(timeout.Token);

            MessageBuffer hostMessage;
            {
                using var hostRequest = connection.GetMessageWriter();
                hostRequest.WriteMethodCallHeader(owner, "/StatusNotifierWatcher", "org.freedesktop.DBus.Properties", "Get", "ss");
                hostRequest.WriteString(watcher);
                hostRequest.WriteString("IsStatusNotifierHostRegistered");
                hostMessage = hostRequest.CreateMessage();
            }
            var available = await connection.CallMethodAsync(hostMessage, static (message, _) =>
            {
                var reader = message.GetBodyReader();
                reader.ReadSignature("b");
                return reader.ReadBool();
            }).WaitAsync(timeout.Token);
            return available ? owner : null;
        }
        catch (Exception ex) when (ex is DBusException or ConnectException or DisconnectedException or ProtocolException
            or IOException or OperationCanceledException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    private async void OpenTrayIntelHistory()
    {
        await ShowIntelHistoryAsync();
        if (_intelHistoryWindow is { IsVisible: true } history) ShowAndActivate(history);
    }

    private void RestoreFromTray()
    {
        if (_isClosingApp) return;
        _hiddenToTray = false;
        ShowAndActivate(this);
    }

    private static void ShowAndActivate(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
        window.Activate();
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isClosingApp && !e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing &&
            _trayIcon is not null && _boundVm?.KeepRunningInTray == true)
        {
            e.Cancel = true;
            _hiddenToTray = true;
            Hide();
            return;
        }

        if (_isClosingApp) return;
        _isClosingApp = true;
        _boundVm?.BeginApplicationShutdown();
        SaveWindowPlacementNow();
        SaveViewportNow();
        SaveSelectedViewModeNow();
        DetachXmpp();
        CloseAuxiliaryWindows();
    }
}

