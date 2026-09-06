using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Hisa.Core.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Hisa.App.Services;

namespace Hisa.App;

public partial class MainWindow : Window
{
    private const double CompactTopToolbarThreshold = 1180;
    private const double TwoLineTopToolbarThreshold = 660;
    private bool _clearSearchOnNextFocus;
    private bool _isApplyingWindowPlacement;
    private bool _isApplyingViewport;
    private bool _pendingFitToViewForRegionGraphChange;
    private bool _isClosingApp;
    private MainWindowViewModel? _boundVm;
    private Hisa.Core.Models.MapViewMode _lastKnownViewMode;
    private readonly DebugWindowViewModel? _debugWindowViewModel;
    private readonly GitHubUpdateService? _updateService;
    private bool _startupUpdateReminderShown;
    private DebugWindow? _debugWindow;
    private PreferencesWindow? _preferencesWindow;
    private HostileColorsSettingsWindow? _hostileColorsSettingsWindow;
    private IntelSettingsWindow? _intelSettingsWindow;
    private AlertsSettingsWindow? _alertsSettingsWindow;
    private AlertPopupSettingsWindow? _alertPopupSettingsWindow;
    private AlertPopupWindow? _alertPopupWindow;
    private AlertPopupSettings _alertPopupSettings = new();
    private bool _isAlertPopupDragMode;
    private CharactersWindow? _charactersWindow;
    private MapEditorWindow? _mapEditorWindow;
    private SovUpgradesWindow? _sovUpgradesWindow;
    private AnsiblexNetworkWindow? _ansiblexNetworkWindow;
    private LyCoveragePlannerWindow? _lyCoveragePlannerWindow;
    private JumpRouteOptimizerWindow? _jumpRouteOptimizerWindow;
    private MiningStatsWindow? _miningStatsWindow;
    private MiningSitesWindow? _miningSitesWindow;
    private ZkillmailsWindow? _zkillmailsWindow;
    private AboutWindow? _aboutWindow;
    private readonly ContextMenu _mapNodeContextMenu;
    private readonly ContextMenu _miningSiteContextMenu;
    private readonly MenuItem _copySystemNameMenuItem;
    private readonly MenuItem _openInViewMenuItem;
    private readonly MenuItem _openInDotlanMenuItem;
    private readonly MenuItem _openInZkillboardMenuItem;
    private readonly MenuItem _openInKillmailAppMenuItem;
    private readonly MenuItem _jumpRangeMenuItem;
    private readonly MenuItem _markSystemMenuItem;
    private readonly MenuItem _markSystemPresetsMenuItem;
    private readonly MenuItem _editMarkMenuItem;
    private readonly MenuItem _removeMarkMenuItem;
    private readonly MenuItem _manageMiningSitesMenuItem;
    private readonly KillmailAppService _killmailAppService;
    private Point? _mapRightPressPoint;
    private bool _mapRightMoved;
    private string? _contextSystemName;
    private long? _contextSystemId;
    private int? _contextRegionId;
    private int? _contextConstellationId;
    private MapSovUpgradeHit? _contextMiningSite;
    private readonly ObservableCollection<AlertPopupCard> _alertPopupCards = [];
    private readonly DispatcherTimer _alertPopupCleanupTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        _killmailAppService = new KillmailAppService();
        InitializeComponent();
        AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, true);
        SizeChanged += (_, _) => UpdateTopToolbarLayout();

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
        _openInKillmailAppMenuItem = new MenuItem
        {
            Header = "Open in killmail.app",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3),
            Icon = BuildKillmailAppMenuIcon()
        };
        _openInKillmailAppMenuItem.Classes.Add("map-node-menu-item");
        _openInKillmailAppMenuItem.Click += OnOpenInKillmailAppClicked;
        _jumpRangeMenuItem = BuildJumpRangeMenu(subMenufontSize);
        _markSystemMenuItem = new MenuItem
        {
            Header = "Mark System…",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3)
        };
        _markSystemMenuItem.Classes.Add("map-node-menu-item");
        _markSystemMenuItem.Click += OnMarkSystemClicked;
        _markSystemPresetsMenuItem = BuildSystemMarkPresetMenu(subMenufontSize);
        _editMarkMenuItem = new MenuItem
        {
            Header = "Edit Mark…",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3)
        };
        _editMarkMenuItem.Classes.Add("map-node-menu-item");
        _editMarkMenuItem.Click += OnMarkSystemClicked;
        _removeMarkMenuItem = new MenuItem
        {
            Header = "Remove Mark",
            FontSize = subMenufontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3)
        };
        _removeMarkMenuItem.Classes.Add("map-node-menu-item");
        _removeMarkMenuItem.Click += OnRemoveMarkClicked;
        _manageMiningSitesMenuItem = new MenuItem { Header = "Manage Mining Sites", FontSize = subMenufontSize, FontWeight = Avalonia.Media.FontWeight.SemiBold, Padding = new Thickness(8, 3) };
        _manageMiningSitesMenuItem.Classes.Add("map-node-menu-item");
        _manageMiningSitesMenuItem.Click += OnManageMiningSitesClicked;
        _mapNodeContextMenu = new ContextMenu
        {
            MinWidth = 0,
            FontSize = subMenufontSize,
            ItemsSource = new object[]
            {
                _copySystemNameMenuItem,
                _openInViewMenuItem,
                _jumpRangeMenuItem,
                _markSystemMenuItem,
                _markSystemPresetsMenuItem,
                _editMarkMenuItem,
                _removeMarkMenuItem,
                _manageMiningSitesMenuItem,
                new Separator(),
                _openInDotlanMenuItem,
                _openInKillmailAppMenuItem,
                _openInZkillboardMenuItem
            }
        };
        _mapNodeContextMenu.Classes.Add("map-node-menu");
        _miningSiteContextMenu = new ContextMenu
        {
            MinWidth = 140,
            FontSize = subMenufontSize,
            ItemsSource = new object[]
            {
                CreateMiningSiteMenuItem("Cleared", MarkMiningSiteClearedFromMapAsync),
                CreateMiningSiteMenuItem("Ready", MarkMiningSiteAvailableFromMapAsync),
                new MenuItem
                {
                    Header = "Missing",
                    ItemsSource = new object[]
                    {
                        CreateMiningSiteMenuItem("3 hours", () => MarkMiningSiteMissingFromMapAsync(TimeSpan.FromHours(3))),
                        CreateMiningSiteMenuItem("5 hours", () => MarkMiningSiteMissingFromMapAsync(TimeSpan.FromHours(5))),
                        CreateMiningSiteMenuItem("8 hours", () => MarkMiningSiteMissingFromMapAsync(TimeSpan.FromHours(8))),
                        CreateMiningSiteMenuItem("11 hours", () => MarkMiningSiteMissingFromMapAsync(TimeSpan.FromHours(11)))
                    }
                },
                CreateMiningSiteMenuItem("Custom…", OpenMiningSiteTrackerFromMapAsync)
            }
        };
        _miningSiteContextMenu.Classes.Add("map-node-menu");
        MainMapControl.UniverseRegionNodeDoubleClicked += OnUniverseRegionNodeClicked;
        Opened += OnOpened;
        Opened += (_, _) => UpdateTopToolbarLayout();
        Closing += (_, _) =>
        {
            _isClosingApp = true;
            _boundVm?.BeginApplicationShutdown();
            SaveWindowPlacementNow();
            SaveViewportNow();
            SaveSelectedViewModeNow();
            CloseAuxiliaryWindows();
        };
    }

    public MainWindow(
        MainWindowViewModel vm,
        DebugWindowViewModel debugWindowViewModel,
        GitHubUpdateService updateService,
        KillmailAppService killmailAppService) : this()
    {
        DataContext = vm;
        _boundVm = vm;
        _debugWindowViewModel = debugWindowViewModel;
        _updateService = updateService;
        _killmailAppService = killmailAppService;
        _lastKnownViewMode = vm.SelectedViewMode;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.AlertTriggered += OnAlertTriggered;
        vm.CharacterSystemChanged += OnCharacterSystemChanged;
        _alertPopupSettings = vm.GetAlertPopupSettingsSnapshot();
        _alertPopupCleanupTimer.Tick += (_, _) => CleanupExpiredAlertPopupCards();
        _alertPopupCleanupTimer.Start();
        Opened += OnCheckForUpdatesOnStartup;
    }

    private void UpdateTopToolbarLayout()
    {
        var width = ClientSize.Width;
        var useTwoLineLayout = width < TwoLineTopToolbarThreshold;
        var useOverflowMenu = width < CompactTopToolbarThreshold && !useTwoLineLayout;

        TopToolbarActionsPanel.IsVisible = !useOverflowMenu;
        TopToolbarOverflowButton.IsVisible = useOverflowMenu;

        Grid.SetRow(TopToolbarModesPanel, 0);
        Grid.SetColumn(TopToolbarModesPanel, 0);
        Grid.SetColumnSpan(TopToolbarModesPanel, 1);

        Grid.SetRow(TopToolbarActionsHost, 0);
        Grid.SetColumn(TopToolbarActionsHost, 2);
        Grid.SetColumnSpan(TopToolbarActionsHost, 1);

        if (useTwoLineLayout)
        {
            Grid.SetRow(TopToolbarInputsPanel, 1);
            Grid.SetColumn(TopToolbarInputsPanel, 0);
            Grid.SetColumnSpan(TopToolbarInputsPanel, 3);
            TopToolbarInputsPanel.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            Grid.SetRow(TopToolbarInputsPanel, 0);
            Grid.SetColumn(TopToolbarInputsPanel, 1);
            Grid.SetColumnSpan(TopToolbarInputsPanel, 1);
            TopToolbarInputsPanel.Margin = new Thickness(4, 0, 0, 0);
        }
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

    private void OnOpenHostileColorsSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (_hostileColorsSettingsWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _hostileColorsSettingsWindow = new HostileColorsSettingsWindow(_boundVm);
            _hostileColorsSettingsWindow.Closed += (_, _) => _hostileColorsSettingsWindow = null;
        }

        _hostileColorsSettingsWindow.Show();
        _hostileColorsSettingsWindow.Activate();
    }

    private void OnOpenAboutClicked(object? sender, RoutedEventArgs e)
    {
        if (_aboutWindow is null)
        {
            _aboutWindow = _updateService is null
                ? new AboutWindow()
                : new AboutWindow(_updateService);
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
            _aboutWindow.Show(this);
        }

        _aboutWindow.Activate();
    }

    private async void OnRegionFavoriteClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_boundVm is null || sender is not Control { DataContext: RegionOption region })
        {
            return;
        }

        await _boundVm.ToggleRegionFavoriteAsync(region);
    }

    private async void OnCheckForUpdatesOnStartup(object? sender, EventArgs e)
    {
        if (_startupUpdateReminderShown || _updateService is null)
        {
            return;
        }

        _startupUpdateReminderShown = true;
        var update = await _updateService.CheckForUpdatesAsync();
        if (update is not { IsUpdateAvailable: true } ||
            !IsVisible ||
            !await _updateService.ShouldShowStartupReminderAsync(update))
        {
            return;
        }

        var reminder = new UpdateAvailableWindow(update);
        reminder.Show(this);
    }

    private void OnOpenCharactersClicked(object? sender, RoutedEventArgs e)
    {
        if (_charactersWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _charactersWindow = new CharactersWindow(_boundVm);
            _charactersWindow.Closed += (_, _) => _charactersWindow = null;
        }

        _charactersWindow.Show();
        _charactersWindow.Activate();
    }

    private void OnOpenIntelSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (_intelSettingsWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _intelSettingsWindow = new IntelSettingsWindow(_boundVm);
            _intelSettingsWindow.Closed += (_, _) => _intelSettingsWindow = null;
        }

        _intelSettingsWindow.Show();
        _intelSettingsWindow.Activate();
    }

    private void OnOpenAlertsSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (_alertsSettingsWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _alertsSettingsWindow = new AlertsSettingsWindow(_boundVm);
            _alertsSettingsWindow.Closed += (_, _) =>
            {
                _isAlertPopupDragMode = false;
                _alertsSettingsWindow = null;
                if (_boundVm is not null)
                {
                    _alertPopupSettings = _boundVm.GetAlertPopupSettingsSnapshot();
                    ApplyAlertPopupWindowSettings();
                }
            };
        }

        _alertsSettingsWindow.Show();
        _alertsSettingsWindow.Activate();
    }

    private void OnOpenAlertPopupSettingsClicked(object? sender, RoutedEventArgs e)
    {
        OnOpenAlertPopupSettingsRequested(sender, EventArgs.Empty);
    }

    private void OnOpenAlertPopupSettingsRequested(object? sender, EventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        if (_alertPopupSettingsWindow is null)
        {
            _alertPopupSettingsWindow = new AlertPopupSettingsWindow(_boundVm);
            _alertPopupSettingsWindow.PlacementModeChanged += OnPopupPlacementModeChanged;
            _alertPopupSettingsWindow.SettingsSaved += OnAlertPopupSettingsSaved;
            _alertPopupSettingsWindow.Closed += (_, _) =>
            {
                _alertPopupSettingsWindow = null;
                _isAlertPopupDragMode = false;
                _alertPopupSettings = _boundVm.GetAlertPopupSettingsSnapshot();
                ApplyAlertPopupWindowSettings();
            };
        }

        _alertPopupSettingsWindow.Show();
        _alertPopupSettingsWindow.Activate();
        _alertPopupSettingsWindow.SetPlacementModeState(_isAlertPopupDragMode);
    }

    private void OnAlertPopupSettingsSaved(object? sender, EventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        _alertPopupSettings = _boundVm.GetAlertPopupSettingsSnapshot();
        ApplyAlertPopupWindowSettings();
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

    private void OnToggleTopNavigationClicked(object? sender, RoutedEventArgs e)
        => _boundVm?.ToggleTopNavigation();

    private void OnToggleBottomNavigationClicked(object? sender, RoutedEventArgs e)
        => _boundVm?.ToggleBottomNavigation();

    private void OnOpenMiningSitesClicked(object? sender, RoutedEventArgs e) => OpenMiningSitesWindow();

    private void OnManageMiningSitesClicked(object? sender, RoutedEventArgs e) => OpenMiningSitesWindow(_contextSystemId);

    private void OpenMiningSitesWindow(long? systemId = null)
    {
        if (_boundVm is null) return;
        if (_miningSitesWindow is null || systemId.HasValue)
        {
            _miningSitesWindow?.Close();
            _miningSitesWindow = new MiningSitesWindow(_boundVm, systemId);
            _miningSitesWindow.Closed += (_, _) => _miningSitesWindow = null;
        }
        _miningSitesWindow.Show();
        _miningSitesWindow.Activate();
    }

    private void OnOpenAnsiblexNetworkClicked(object? sender, RoutedEventArgs e)
    {
        if (_ansiblexNetworkWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _ansiblexNetworkWindow = new AnsiblexNetworkWindow(_boundVm);
            _ansiblexNetworkWindow.Closed += (_, _) => _ansiblexNetworkWindow = null;
        }

        _ansiblexNetworkWindow.Show();
        _ansiblexNetworkWindow.Activate();
    }

    private void OnOpenLyCoveragePlannerClicked(object? sender, RoutedEventArgs e)
    {
        if (_lyCoveragePlannerWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _lyCoveragePlannerWindow = new LyCoveragePlannerWindow(_boundVm);
            _lyCoveragePlannerWindow.Closed += (_, _) => _lyCoveragePlannerWindow = null;
        }

        _lyCoveragePlannerWindow.Show();
        _lyCoveragePlannerWindow.Activate();
    }

    private void OnOpenJumpRouteOptimizerClicked(object? sender, RoutedEventArgs e)
    {
        if (_jumpRouteOptimizerWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _jumpRouteOptimizerWindow = new JumpRouteOptimizerWindow(_boundVm);
            _jumpRouteOptimizerWindow.Closed += (_, _) => _jumpRouteOptimizerWindow = null;
        }

        _jumpRouteOptimizerWindow.Show();
        _jumpRouteOptimizerWindow.Activate();
    }

    private void OnOpenMiningStatsWindowClicked(object? sender, RoutedEventArgs e)
    {
        if (_miningStatsWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _miningStatsWindow = new MiningStatsWindow(_boundVm);
            _miningStatsWindow.Closed += (_, _) => _miningStatsWindow = null;
        }

        _miningStatsWindow.Show();
        _miningStatsWindow.Activate();
    }

    private void OnOpenZkillmailsWindowClicked(object? sender, RoutedEventArgs e)
    {
        if (_zkillmailsWindow is null)
        {
            if (_boundVm is null)
            {
                return;
            }

            _zkillmailsWindow = new ZkillmailsWindow(_boundVm);
            _zkillmailsWindow.Closed += (_, _) => _zkillmailsWindow = null;
        }

        _zkillmailsWindow.Show();
        _zkillmailsWindow.Activate();
    }

    private void OnAlertTriggered(object? sender, AlertTriggered alert)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (alert.Actions.Contains(AlertActionType.PlaySound))
            {
                AlertSoundPlayer.Play(alert.SoundFile, alert.SoundVolume);
            }

            if (_alertPopupSettings.Enabled && alert.Actions.Contains(AlertActionType.ShowPopup))
            {
                EnsureAlertPopupWindow();
                var zkillCard = TryFindPopupZkillmailCard(alert);
                var intelCard = zkillCard is null ? TryFindPopupIntelCard(alert) : null;
                var isMiningSiteAlert = alert.SourceEvent.EventType == AlertEventType.MiningSiteReady;
                var environmentalCard = BuildEnvironmentalAlertCard(alert);
                var card = new AlertPopupCard
                {
                    Title = $"{alert.SourceEvent.EventType}: {alert.RuleName}",
                    Details = alert.SourceEvent.Summary,
                    TimestampLabel = isMiningSiteAlert
                        ? FormatMiningSiteTimestampLabel(alert.SourceEvent.MiningSiteReadyAtUtc, alert.SourceEvent.MiningSiteWasOverdue)
                        : $"{alert.TriggeredAtUtc:HH:mm:ss} UTC",
                    IntelCard = intelCard,
                    ZkillmailCard = zkillCard,
                    EnvironmentalCard = environmentalCard,
                    IsMiningSiteAlert = isMiningSiteAlert,
                    MiningSiteSystemName = alert.SourceEvent.MiningSiteSystemName ?? string.Empty,
                    MiningSiteUpgradeLabel = alert.SourceEvent.MiningSiteUpgradeName is { Length: > 0 } name
                        ? $"{name} T{alert.SourceEvent.MiningSiteTier ?? 1}"
                        : string.Empty,
                    MiningSiteIcon = isMiningSiteAlert ? LoadMiningSiteAlertIcon(alert.SourceEvent.MiningSiteUpgradeName, alert.SourceEvent.MiningSiteTier) : null,
                    MiningSiteWasOverdue = alert.SourceEvent.MiningSiteWasOverdue,
                    JumpCount = alert.JumpCount,
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(_alertPopupSettings.AutoDismissSeconds)
                };
                _alertPopupCards.Insert(0, card);
                while (_alertPopupCards.Count > _alertPopupSettings.MaxCards)
                {
                    _alertPopupCards.RemoveAt(_alertPopupCards.Count - 1);
                }

                _alertPopupWindow!.Show();
                UpdateAlertPopupClickThrough();
            }
        });
    }

    private static EnvironmentalAlertPopupCard? BuildEnvironmentalAlertCard(AlertTriggered alert)
    {
        var source = alert.SourceEvent;
        var systemName = source.SystemName ?? $"System {source.SolarSystemId}";
        var constellationName = source.ConstellationName ?? "Unknown Constellation";
        var regionName = source.RegionName ?? "Unknown Region";
        var timestampLabel = $"{alert.TriggeredAtUtc:HH:mm:ss} UTC";

        if (source.EventType == AlertEventType.StormSpawn && source.StormType is { } stormType)
        {
            var (typeLabel, accent, accentBackground) = stormType switch
            {
                StormType.Electrical => ("Electrical", "#4AA8FF", "#172D45"),
                StormType.Gamma => ("Gamma", "#E69138", "#3A2B19"),
                StormType.Exotic => ("Exotic", "#CFD4DC", "#30363D"),
                StormType.Plasma => ("Plasma", "#DE5B52", "#3A2024"),
                _ => ("Unknown", "#9BA8B8", "#27313D")
            };

            return new EnvironmentalAlertPopupCard
            {
                SolarSystemId = source.SolarSystemId,
                SystemName = systemName,
                ConstellationName = constellationName,
                RegionName = regionName,
                CategoryLabel = "METALIMINAL STORM",
                Headline = $"{typeLabel} storm center detected",
                AccentHex = accent,
                AccentBackgroundHex = accentBackground,
                DetailOne = $"Coverage: {source.StormAffectedSystemCount ?? 0} systems affected",
                DetailTwo = $"Strength: {source.StormStrongSystemCount ?? 0} strong | {source.StormWeakSystemCount ?? 0} weak",
                DetailThree = "Center system",
                TimestampLabel = timestampLabel
            };
        }

        if (source.EventType == AlertEventType.HubWormholeSpawn && source.HubWormholeConnection is { } wormhole)
        {
            var isThera = wormhole.HubType == WormholeHubType.Thera;
            var accent = isThera ? "#44D19D" : "#FFB34D";
            var hubName = isThera ? "Thera" : "Turnur";
            var inSignature = string.IsNullOrWhiteSpace(wormhole.InSignature) ? "?" : wormhole.InSignature.Trim().ToUpperInvariant();
            var outSignature = string.IsNullOrWhiteSpace(wormhole.OutSignature) ? "?" : wormhole.OutSignature.Trim().ToUpperInvariant();
            var shipSize = string.IsNullOrWhiteSpace(wormhole.MaxShipSize) ? "Unknown" : wormhole.MaxShipSize.Trim().ToUpperInvariant();
            var expires = wormhole.ExpiresAtUtc is null
                ? "Expiry unknown"
                : wormhole.ExpiresAtUtc <= DateTimeOffset.UtcNow
                    ? "Expires now"
                    : $"Expires in {FormatDuration(wormhole.ExpiresAtUtc.Value - DateTimeOffset.UtcNow)}";

            return new EnvironmentalAlertPopupCard
            {
                SolarSystemId = source.SolarSystemId,
                SystemName = systemName,
                ConstellationName = constellationName,
                RegionName = regionName,
                CategoryLabel = $"{hubName.ToUpperInvariant()} WORMHOLE",
                Headline = $"New connection to {hubName}",
                AccentHex = accent,
                AccentBackgroundHex = isThera ? "#183A2B" : "#3A2C19",
                DetailOne = $"Signatures: In {inSignature} | Out {outSignature}",
                DetailTwo = $"Max ship size: {shipSize}",
                DetailThree = expires,
                TimestampLabel = timestampLabel
            };
        }

        if (source.EventType == AlertEventType.IncursionSpawn && source.Incursion is { } incursion)
        {
            var state = string.IsNullOrWhiteSpace(incursion.State) ? "Unknown" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(incursion.State);

            return new EnvironmentalAlertPopupCard
            {
                SolarSystemId = source.SolarSystemId,
                SystemName = systemName,
                ConstellationName = constellationName,
                RegionName = regionName,
                CategoryLabel = "SANSHA INCURSION",
                Headline = $"{incursion.Type} — {state}",
                AccentHex = "#A77BFF",
                AccentBackgroundHex = "#31223F",
                DetailOne = $"Staging system | Constellation: {constellationName}",
                DetailTwo = $"Influence: {incursion.Influence:P0} | Systems: {incursion.InfestedSolarSystems.Count}",
                DetailThree = incursion.HasBoss ? "Mothership present" : "Mothership not present",
                TimestampLabel = timestampLabel
            };
        }

        return null;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 24
            ? $"{(int)duration.TotalDays}d {duration.Hours}h"
            : duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                : $"{Math.Max(1, duration.Minutes)}m";
    }

    private IntelOverlayCard? TryFindPopupIntelCard(AlertTriggered alert)
    {
        if (_boundVm is null || alert.SourceEvent.EventType != AlertEventType.IntelReport)
        {
            return null;
        }

        var tolerance = TimeSpan.FromMinutes(5);
        return _boundVm.IntelCardsForView
            .Where(c => c.SolarSystemId == alert.SourceEvent.SolarSystemId)
            .OrderBy(c => Math.Abs((c.SortTimestampUtc - alert.SourceEvent.TimestampUtc).TotalSeconds))
            .FirstOrDefault(c => Math.Abs((c.SortTimestampUtc - alert.SourceEvent.TimestampUtc).TotalSeconds) <= tolerance.TotalSeconds)
            ?? _boundVm.IntelCardsForView.FirstOrDefault(c => c.SolarSystemId == alert.SourceEvent.SolarSystemId);
    }

    private ZkillmailOverlayCard? TryFindPopupZkillmailCard(AlertTriggered alert)
    {
        if (_boundVm is null || alert.SourceEvent.EventType != AlertEventType.Killmail)
        {
            return null;
        }

        if (alert.SourceEvent.KillmailId is { } killmailId)
        {
            var idToken = $"/kill/{killmailId}/";
            var byId = _boundVm.ZkillmailCardsForView.FirstOrDefault(c => c.KillmailUrl.Contains(idToken, StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId;
            }
        }

        var tolerance = TimeSpan.FromMinutes(10);
        return _boundVm.ZkillmailCardsForView
            .Where(c => c.SolarSystemId == alert.SourceEvent.SolarSystemId)
            .OrderBy(c => Math.Abs((c.TimestampUtc - alert.SourceEvent.TimestampUtc).TotalSeconds))
            .FirstOrDefault(c => Math.Abs((c.TimestampUtc - alert.SourceEvent.TimestampUtc).TotalSeconds) <= tolerance.TotalSeconds)
            ?? _boundVm.ZkillmailCardsForView.FirstOrDefault(c => c.SolarSystemId == alert.SourceEvent.SolarSystemId);
    }

    private void EnsureAlertPopupWindow()
    {
        if (_alertPopupWindow is not null)
        {
            return;
        }

        _alertPopupWindow = new AlertPopupWindow
        {
            Width = _alertPopupSettings.Width,
            Height = _alertPopupSettings.Height,
            ShowActivated = false,
            DataContext = _boundVm
        };
        _alertPopupWindow.DragPositionCommitted += OnAlertPopupDragPositionCommitted;
        _alertPopupWindow.SystemNavigationRequested += OnAlertPopupSystemNavigationRequested;
        _alertPopupWindow.Closed += (_, _) => _alertPopupWindow = null;
        var itemsControl = _alertPopupWindow.FindControl<ItemsControl>("AlertsItemsControl");
        if (itemsControl is not null)
        {
            itemsControl.ItemsSource = _alertPopupCards;
        }
        ApplyAlertPopupWindowSettings();
    }

    private async void OnAlertPopupSystemNavigationRequested(long systemId)
    {
        if (_boundVm is null || systemId <= 0)
        {
            return;
        }

        await NavigateAndCenterSystemFromReportAsync(_boundVm, systemId);
    }

    private void ApplyAlertPopupWindowSettings()
    {
        if (_alertPopupWindow is null)
        {
            return;
        }

        _alertPopupWindow.Opacity = _alertPopupSettings.Opacity;
        _alertPopupWindow.IsDragModeEnabled = _isAlertPopupDragMode;
        UpdateAlertPopupClickThrough();
        if (_isAlertPopupDragMode)
        {
            _alertPopupWindow.SizeToContent = SizeToContent.Manual;
            _alertPopupWindow.Width = _alertPopupSettings.Width;
            _alertPopupWindow.Height = _alertPopupSettings.Height;
            _alertPopupWindow.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            _alertPopupWindow.SizeToContent = SizeToContent.Height;
            _alertPopupWindow.Width = _alertPopupSettings.Width;
            _alertPopupWindow.Height = double.NaN;
            _alertPopupWindow.MaxHeight = _alertPopupSettings.Height;
        }

        var width = (int)Math.Max(0, _alertPopupSettings.Width);
        var height = (int)Math.Max(0, _alertPopupSettings.Height);
        var x = Position.X;
        var y = Position.Y;

        switch (_alertPopupSettings.Anchor)
        {
            case AlertPopupAnchor.TopLeft:
                x += _alertPopupSettings.OffsetX;
                y += _alertPopupSettings.OffsetY;
                break;
            case AlertPopupAnchor.BottomRight:
                x += ((int)Bounds.Width - width - _alertPopupSettings.OffsetX);
                y += ((int)Bounds.Height - height - _alertPopupSettings.OffsetY);
                break;
            case AlertPopupAnchor.BottomLeft:
                x += _alertPopupSettings.OffsetX;
                y += ((int)Bounds.Height - height - _alertPopupSettings.OffsetY);
                break;
            default:
                x += ((int)Bounds.Width - width - _alertPopupSettings.OffsetX);
                y += _alertPopupSettings.OffsetY;
                break;
        }

        _alertPopupWindow.Position = new PixelPoint(x, y);
        UpdateAlertPopupClickThrough();
    }

    private void UpdateAlertPopupClickThrough()
    {
        if (_alertPopupWindow is null)
        {
            return;
        }

        var clickThrough = !_isAlertPopupDragMode && _alertPopupCards.Count == 0;
        OverlayClickThrough.Set(_alertPopupWindow, clickThrough);
        if (_alertPopupWindow.IsVisible)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_alertPopupWindow is null)
                {
                    return;
                }

                OverlayClickThrough.Set(_alertPopupWindow, !_isAlertPopupDragMode && _alertPopupCards.Count == 0);
            }, DispatcherPriority.Background);
        }
    }

    private void OnEnterPopupDragModeRequested(object? sender, EventArgs e)
    {
        _isAlertPopupDragMode = true;
        EnsureAlertPopupWindow();
        ApplyAlertPopupWindowSettings();
        _alertPopupWindow!.Show();
        UpdateAlertPopupClickThrough();
        _alertPopupSettingsWindow?.SetPlacementModeState(true);
    }

    private void OnExitPopupDragModeRequested(object? sender, EventArgs e)
    {
        _isAlertPopupDragMode = false;
        ApplyAlertPopupWindowSettings();
        _alertPopupSettingsWindow?.SetPlacementModeState(false);
        if (_boundVm is not null)
        {
            _ = _boundVm.SaveAlertPopupSettingsAsync(_alertPopupSettings);
        }
    }

    private void OnPopupPlacementModeChanged(object? sender, bool enabled)
    {
        if (enabled)
        {
            OnEnterPopupDragModeRequested(sender, EventArgs.Empty);
        }
        else
        {
            OnExitPopupDragModeRequested(sender, EventArgs.Empty);
        }
    }

    private void OnAlertPopupDragPositionCommitted(object? sender, PixelPoint popupPosition)
    {
        if (!_isAlertPopupDragMode || _boundVm is null || _alertPopupWindow is null)
        {
            return;
        }

        var width = Math.Max(0, (int)_alertPopupWindow.Width);
        var height = Math.Max(0, (int)_alertPopupWindow.Height);
        var relX = popupPosition.X - Position.X;
        var relY = popupPosition.Y - Position.Y;
        var mainW = (int)Bounds.Width;
        var mainH = (int)Bounds.Height;

        var offsetX = _alertPopupSettings.OffsetX;
        var offsetY = _alertPopupSettings.OffsetY;
        switch (_alertPopupSettings.Anchor)
        {
            case AlertPopupAnchor.TopLeft:
                offsetX = relX;
                offsetY = relY;
                break;
            case AlertPopupAnchor.BottomRight:
                offsetX = mainW - width - relX;
                offsetY = mainH - height - relY;
                break;
            case AlertPopupAnchor.BottomLeft:
                offsetX = relX;
                offsetY = mainH - height - relY;
                break;
            default:
                offsetX = mainW - width - relX;
                offsetY = relY;
                break;
        }

        _alertPopupSettings = new AlertPopupSettings
        {
            Enabled = _alertPopupSettings.Enabled,
            MaxCards = _alertPopupSettings.MaxCards,
            AutoDismissSeconds = _alertPopupSettings.AutoDismissSeconds,
            Opacity = _alertPopupSettings.Opacity,
            Width = _alertPopupSettings.Width,
            Height = _alertPopupSettings.Height,
            Anchor = _alertPopupSettings.Anchor,
            OffsetX = offsetX,
            OffsetY = offsetY
        };
        _alertPopupSettingsWindow?.UpdateOffsetsFromPlacement(offsetX, offsetY);
    }

    private void CleanupExpiredAlertPopupCards()
    {
        if (_alertPopupCards.Count == 0)
        {
            if (!_isAlertPopupDragMode && _alertPopupWindow is { IsVisible: true })
            {
                _alertPopupWindow.Hide();
            }

            UpdateAlertPopupClickThrough();
            return;
        }

        var nowUtc = DateTime.UtcNow;
        for (var i = _alertPopupCards.Count - 1; i >= 0; i--)
        {
            if (_alertPopupCards[i].ExpiresAtUtc <= nowUtc)
            {
                _alertPopupCards.RemoveAt(i);
            }
        }

        if (_alertPopupCards.Count == 0 && !_isAlertPopupDragMode && _alertPopupWindow is { IsVisible: true })
        {
            _alertPopupWindow.Hide();
        }

        UpdateAlertPopupClickThrough();
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

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (!MapSearchBox.IsFocused && !vm.HasSearchSuggestions)
        {
            return;
        }

        if (e.Source is not Visual sourceVisual)
        {
            return;
        }

        if (IsVisualWithin(sourceVisual, MapSearchBox) ||
            IsVisualWithin(sourceVisual, MapSearchSuggestionsList))
        {
            return;
        }

        DismissMapSearch(vm);
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
        if (DataContext is MainWindowViewModel vm &&
            (MapSearchBox.IsFocused || vm.HasSearchSuggestions))
        {
            DismissMapSearch(vm);
        }

        var props = e.GetCurrentPoint(MainMapControl).Properties;
        if (props.IsLeftButtonPressed)
        {
            var point = e.GetPosition(MainMapControl);
            var miningSite = MainMapControl.HitTestSovUpgrade(point);
            if (miningSite is not null)
            {
                _contextMiningSite = miningSite;
                ConfigureMiningSiteMenuPlacement(point);
                _miningSiteContextMenu.Open(MainMapControl);
                e.Handled = true;
                return;
            }
        }
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
            _manageMiningSitesMenuItem.IsVisible = node.SovUpgrades.Any(x => x.UpgradeName.EndsWith(" Prospecting Array", StringComparison.OrdinalIgnoreCase));
            var isMarked = vm.HasUserSystemMark(node.Id);
            _markSystemMenuItem.IsVisible = !isMarked;
            _markSystemPresetsMenuItem.IsVisible = !isMarked;
            _editMarkMenuItem.IsVisible = isMarked;
            _removeMarkMenuItem.IsVisible = isMarked;
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

    private async void OnOpenInKillmailAppClicked(object? sender, RoutedEventArgs e)
    {
        if (_contextSystemId is null)
        {
            return;
        }

        try
        {
            var url = await _killmailAppService.CreateBattleReportLaunchTargetAsync(_contextSystemId.Value, DateTimeOffset.UtcNow);
            if (!string.IsNullOrWhiteSpace(url))
            {
                TryOpenUrl(url);
            }
        }
        catch
        {
            // Ignore failures from HTTP or shell/browser launch.
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

    private async void OnIntelCardSystemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var systemId = sender switch
        {
            Control { DataContext: IntelOverlayCard intelCard } => intelCard.SolarSystemId,
            Control { DataContext: ZkillmailOverlayCard zkillCard } => zkillCard.SolarSystemId,
            _ => 0
        };
        if (systemId <= 0)
        {
            return;
        }

        await NavigateAndCenterSystemFromReportAsync(vm, systemId);
    }

    private async void OnActivityCardSystemClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var systemId = sender switch
        {
            Control { DataContext: WormholeOverlayCard wormholeCard } => wormholeCard.SolarSystemId,
            Control { DataContext: IncursionOverlayCard incursionCard } => incursionCard.SolarSystemId,
            Control { DataContext: StormOverlayCard stormCard } => stormCard.SolarSystemId,
            Control { DataContext: SystemMarkOverlayCard markCard } => markCard.SolarSystemId,
            _ => 0
        };
        if (systemId <= 0)
        {
            return;
        }

        await NavigateAndCenterSystemFromReportAsync(vm, systemId);
    }

    private async void OnZkillmailCardSystemClicked(object? sender, RoutedEventArgs e)
    {
        OnIntelCardSystemClicked(sender, e);
    }

    private void OnIntelHostilePortraitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.CharacterId is null)
        {
            return;
        }

        var url = $"https://zkillboard.com/character/{hostile.CharacterId.Value}/";
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

    private void OnIntelHostileCorporationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.CorporationId is null)
        {
            return;
        }

        var url = $"https://zkillboard.com/corporation/{hostile.CorporationId.Value}/";
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

    private void OnIntelHostileAllianceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: IntelOverlayHostileCard hostile } || hostile.AllianceId is null)
        {
            return;
        }

        var url = $"https://zkillboard.com/alliance/{hostile.AllianceId.Value}/";
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
            // Ignore failures from shell/browser launch.
        }
    }

    private void OnZkillVictimPortraitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ZkillmailOverlayCard card } || card.Victim.CharacterId is null)
        {
            return;
        }

        TryOpenUrl($"https://zkillboard.com/character/{card.Victim.CharacterId.Value}/");
    }

    private void OnZkillVictimCorporationClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ZkillmailOverlayCard card } || card.Victim.CorporationId is null)
        {
            return;
        }

        TryOpenUrl($"https://zkillboard.com/corporation/{card.Victim.CorporationId.Value}/");
    }

    private void OnZkillVictimAllianceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ZkillmailOverlayCard card } || card.Victim.AllianceId is null)
        {
            return;
        }

        TryOpenUrl($"https://zkillboard.com/alliance/{card.Victim.AllianceId.Value}/");
    }

    private static void TryOpenUrl(string url)
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
            // Ignore failures from shell/browser launch.
        }
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

    private static Bitmap? LoadMiningSiteAlertIcon(string? upgradeName, int? tier)
    {
        if (string.IsNullOrWhiteSpace(upgradeName)) return null;
        try
        {
            var fileName = $"{upgradeName.Trim()} {Math.Clamp(tier ?? 1, 1, 3)}.png";
            using var stream = AssetLoader.Open(new Uri($"avares://HISA/Assets/Icons/SOV Upgrades/{fileName}"));
            return new Bitmap(stream);
        }
        catch { return null; }
    }

    private static string FormatMiningSiteTimestampLabel(DateTime? readyAtUtc, bool overdue)
    {
        if (!overdue || readyAtUtc is null) return "Just now";
        var age = DateTime.UtcNow - readyAtUtc.Value;
        return age.TotalHours >= 1
            ? $"{Math.Floor(age.TotalHours):0}h ago"
            : $"{Math.Max(1, Math.Floor(age.TotalMinutes)):0}m ago";
    }

    private void ConfigureMiningSiteMenuPlacement(Point clickPoint)
    {
        _miningSiteContextMenu.Placement = PlacementMode.BottomEdgeAlignedLeft;
        _miningSiteContextMenu.PlacementRect = new Rect(clickPoint, new Size(1, 1));
        _miningSiteContextMenu.HorizontalOffset = 3;
        _miningSiteContextMenu.VerticalOffset = 3;
    }

    private MenuItem CreateMiningSiteMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header, Padding = new Thickness(8, 4) };
        item.Classes.Add("map-node-menu-item");
        item.Click += async (_, _) => await action();
        return item;
    }

    private async Task MarkMiningSiteClearedFromMapAsync()
    {
        if (_boundVm is null || _contextMiningSite is null) return;
        await _boundVm.MarkMiningSiteClearedAsync((int)_contextMiningSite.SolarSystemId, _contextMiningSite.UpgradeName, _contextMiningSite.Tier);
    }

    private async Task MarkMiningSiteAvailableFromMapAsync()
    {
        if (_boundVm is null || _contextMiningSite is null) return;
        await _boundVm.MarkMiningSiteAvailableAsync((int)_contextMiningSite.SolarSystemId, _contextMiningSite.UpgradeName, _contextMiningSite.Tier);
    }

    private async Task MarkMiningSiteMissingFromMapAsync(TimeSpan delay)
    {
        if (_boundVm is null || _contextMiningSite is null) return;
        await _boundVm.MarkMiningSiteMissingAsync((int)_contextMiningSite.SolarSystemId, _contextMiningSite.UpgradeName, _contextMiningSite.Tier, delay);
    }

    private Task OpenMiningSiteTrackerFromMapAsync()
    {
        OpenMiningSitesWindow(_contextMiningSite?.SolarSystemId);
        return Task.CompletedTask;
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

    private async Task NavigateAndCenterSystemFromReportAsync(MainWindowViewModel vm, long systemId)
    {
        var zoom = MainMapControl.GetViewportState().Zoom;
        var preserveZoom = double.IsFinite(zoom) && zoom > 0;

        await vm.NavigateToSystemFromReportAsync(systemId);
        vm.SelectedNodeId = systemId;
        FocusSelectedNodeAtZoom(systemId, zoom, preserveZoom);
        await FocusSelectedNodeAtZoomAsync(systemId, zoom, preserveZoom);
    }

    private void FocusSelectedNodeAtZoom(long nodeId, double zoom, bool preserveZoom)
    {
        if (preserveZoom)
        {
            MainMapControl.FocusOnNodeWithZoom(nodeId, zoom);
            return;
        }

        // A comfortable overview when the viewport does not yet have a usable zoom level.
        MainMapControl.FocusOnNodeWithZoomPercent(nodeId, 0.3);
    }

    private async Task FocusSelectedNodeAtZoomAsync(long nodeId, double zoom, bool preserveZoom)
    {
        // Re-apply center/zoom after UI/layout settles to avoid fit-to-view overrides.
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(40);
                if (_boundVm?.CurrentGraph?.Nodes.Any(n => n.Id == nodeId) == true)
                {
                    FocusSelectedNodeAtZoom(nodeId, zoom, preserveZoom);
                }
            });
        }
    }

    private static Control? BuildMenuIcon(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://HISA/Assets/Icons/{fileName}");
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

    private static Control BuildKillmailAppMenuIcon()
    {
        var geometry = StreamGeometry.Parse("M92,104a28,28,0,1,0,28,28A28,28,0,0,0,92,104Zm0,40a12,12,0,1,1,12-12A12,12,0,0,1,92,144Zm72-40a28,28,0,1,0,28,28A28,28,0,0,0,164,104Zm0,40a12,12,0,1,1,12-12A12,12,0,0,1,164,144ZM128,16C70.65,16,24,60.86,24,116c0,34.1,18.27,66,48,84.28V216a16,16,0,0,0,16,16h80a16,16,0,0,0,16-16V200.28C213.73,182,232,150.1,232,116,232,60.86,185.35,16,128,16Zm44.12,172.69a8,8,0,0,0-4.12,7V216H152V192a8,8,0,0,0-16,0v24H120V192a8,8,0,0,0-16,0v24H88V195.69a8,8,0,0,0-4.12-7C56.81,173.69,40,145.84,40,116c0-46.32,39.48-84,88-84s88,37.68,88,84C216,145.83,199.19,173.69,172.12,188.69Z");

        return new PathIcon
        {
            Data = geometry,
            Width = 12,
            Height = 12,
            Foreground = new SolidColorBrush(Color.Parse("#C9D9EE"))
        };
    }

    private MenuItem BuildJumpRangeMenu(int subMenuFontSize)
    {
        MenuItem BuildJumpItem(string header, string tag, EventHandler<RoutedEventArgs> click)
        {
            var item = new MenuItem
            {
                Header = header,
                Tag = tag,
                FontSize = subMenuFontSize,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Padding = new Thickness(8, 3)
            };
            item.Classes.Add("map-node-menu-item");
            item.Click += click;
            return item;
        }

        return new MenuItem
        {
            Header = "Calculate Jump Range",
            FontSize = subMenuFontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3),
            ItemsSource = new object[]
            {
                BuildJumpItem("Clear All Jump Ranges", "clear-all", OnSetJumpRangeClicked),
                BuildJumpItem("Remove This Origin", "remove", OnSetJumpRangeClicked),
                new Separator(),
                BuildJumpItem("Titans / Supers (6.0 LY)", "6", OnSetJumpRangeClicked),
                BuildJumpItem("Carriers / Dreads / Fax (7.0 LY)", "7", OnSetJumpRangeClicked),
                BuildJumpItem("Black Ops (8.0 LY)", "8", OnSetJumpRangeClicked),
                BuildJumpItem("Jump Freighters / Rorquals (10.0 LY)", "10", OnSetJumpRangeClicked),
                BuildJumpItem("Custom... (LY)", "custom", OnSetJumpRangeClicked)
            }
        };
    }

    private MenuItem BuildSystemMarkPresetMenu(int subMenuFontSize)
    {
        MenuItem BuildPreset(string header, string tag)
        {
            var item = new MenuItem
            {
                Header = header,
                Tag = tag,
                FontSize = subMenuFontSize,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Padding = new Thickness(8, 3)
            };
            item.Classes.Add("map-node-menu-item");
            item.Click += OnSystemMarkPresetClicked;
            return item;
        }

        return new MenuItem
        {
            Header = "Quick Mark",
            FontSize = subMenuFontSize,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Thickness(8, 3),
            ItemsSource = new object[]
            {
                BuildPreset("Home", "home"),
                BuildPreset("Clone", "clone"),
                BuildPreset("Industry Hub", "industry"),
                BuildPreset("Market", "market"),
                BuildPreset("Staging", "staging"),
                BuildPreset("Mining", "mining")
            }
        };
    }

    private async void OnMarkSystemClicked(object? sender, RoutedEventArgs e)
    {
        await OpenSystemMarkEditorForContextAsync();
    }

    private void OnRemoveMarkClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || _contextSystemId is null)
        {
            return;
        }

        vm.RemoveUserSystemMark(_contextSystemId.Value);
    }

    private void OnSystemMarkPresetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not MenuItem { Tag: string tag } ||
            _contextSystemId is null ||
            string.IsNullOrWhiteSpace(_contextSystemName))
        {
            return;
        }

        var (kind, label, color) = tag switch
        {
            "home" => (SystemMarkIconKind.Home, "Home", "#5BA3F5"),
            "clone" => (SystemMarkIconKind.Clone, "Clone", "#2DD4BF"),
            "industry" => (SystemMarkIconKind.Industry, "Industry", "#F59E42"),
            "market" => (SystemMarkIconKind.Market, "Market", "#E7C85A"),
            "staging" => (SystemMarkIconKind.Staging, "Staging", "#EF4444"),
            "mining" => (SystemMarkIconKind.Mining, "Mining", "#4ADE80"),
            _ => (SystemMarkIconKind.Pin, "Mark", "#7AA5D6")
        };

        vm.ApplyUserSystemMarkPreset(
            _contextSystemId.Value,
            _contextSystemName,
            ResolveContextRegionName(vm),
            kind,
            label,
            color);
    }

    private async void OnEditSystemMarkClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Control { DataContext: SystemMarkOverlayCard card })
        {
            return;
        }

        await OpenSystemMarkEditorAsync(vm, card.SolarSystemId, card.SystemName, card.RegionName);
    }

    private void OnRemoveSystemMarkClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            sender is not Control { DataContext: SystemMarkOverlayCard card })
        {
            return;
        }

        vm.RemoveUserSystemMark(card.SolarSystemId);
    }

    private async Task OpenSystemMarkEditorForContextAsync()
    {
        if (DataContext is not MainWindowViewModel vm ||
            _contextSystemId is null ||
            string.IsNullOrWhiteSpace(_contextSystemName))
        {
            return;
        }

        await OpenSystemMarkEditorAsync(vm, _contextSystemId.Value, _contextSystemName, ResolveContextRegionName(vm));
    }

    private async Task OpenSystemMarkEditorAsync(
        MainWindowViewModel vm,
        long systemId,
        string systemName,
        string? regionName)
    {
        vm.TryGetUserSystemMark(systemId, out var existing);
        var editor = new SystemMarkEditorWindow(vm, systemId, systemName, regionName, existing);
        await editor.ShowDialog(this);
    }

    private string? ResolveContextRegionName(MainWindowViewModel vm)
    {
        var node = vm.CurrentGraph?.Nodes.FirstOrDefault(n => n.Id == _contextSystemId);
        return string.IsNullOrWhiteSpace(node?.RegionName) ? null : node.RegionName;
    }

    private async void OnSetJumpRangeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not MenuItem menuItem || _contextSystemId is null)
        {
            return;
        }

        var systemId = _contextSystemId.Value;
        var tag = menuItem.Tag as string;
        if (tag == "remove")
        {
            vm.RemoveJumpRangeOrigin(systemId);
            return;
        }

        if (tag == "clear-all")
        {
            vm.ClearJumpRangeOrigins();
            return;
        }

        double? range = tag switch
        {
            "6" => 6.0,
            "7" => 7.0,
            "8" => 8.0,
            "10" => 10.0,
            "custom" => await PromptForCustomJumpRangeLyAsync(),
            _ => null
        };

        if (range is null || range.Value <= 0)
        {
            return;
        }

        vm.TrySetJumpRangeOrigin(systemId, range.Value);
    }

    private async Task<double?> PromptForCustomJumpRangeLyAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner)
        {
            return null;
        }

        var dialog = new Window
        {
            Title = "Custom Jump Range",
            Width = 330,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var input = new TextBox { Text = "6.0", Width = 120 };
        var okButton = new Button { Content = "OK", MinWidth = 84 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        var tcs = new TaskCompletionSource<double?>();

        okButton.Click += (_, _) =>
        {
            var raw = input.Text?.Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                if (parsed > 0)
                {
                    tcs.TrySetResult(parsed);
                    dialog.Close();
                    return;
                }
            }

            tcs.TrySetResult(null);
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            tcs.TrySetResult(null);
            dialog.Close();
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(null);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Enter LY (e.g. 6.8):" },
                input,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { okButton, cancelButton }
                }
            }
        };

        await dialog.ShowDialog(owner);
        return await tcs.Task;
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

        if (_boundVm.FollowedCharacter?.CharacterId is int characterId &&
            _boundVm.TryGetCharacterLocation(characterId, out var location) &&
            location is not null)
        {
            await CenterMapOnCharacterLocationAsync(location);
        }

        Dispatcher.UIThread.Post(RestoreMiningOverlayOnStartup, DispatcherPriority.Background);
    }

    private void RestoreMiningOverlayOnStartup()
    {
        if (_isClosingApp || _boundVm is null || !_boundVm.ShouldRestoreMiningOverlayVisible)
        {
            return;
        }

        if (MiningOverlayWindow.Current is not null)
        {
            _boundVm.IsMiningOverlayVisible = true;
            return;
        }

        var overlayWindow = new MiningOverlayWindow(_boundVm);
        overlayWindow.Closed += (_, _) =>
        {
            if (_boundVm is not null)
            {
                _boundVm.SetMiningOverlayVisibility(false, persistPreference: !_isClosingApp);
            }
        };
        overlayWindow.Show();
        _boundVm.IsMiningOverlayVisible = true;
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
            _pendingFitToViewForRegionGraphChange = _boundVm.SelectedViewMode == Hisa.Core.Models.MapViewMode.Region;
            await RestoreViewportForCurrentModeAsync(fallbackToFit: true);
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedRegion))
        {
            if (_boundVm.SelectedViewMode == Hisa.Core.Models.MapViewMode.Region)
            {
                _pendingFitToViewForRegionGraphChange = true;
            }

            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentGraph))
        {
            if (_boundVm.SelectedViewMode == Hisa.Core.Models.MapViewMode.Region &&
                _pendingFitToViewForRegionGraphChange)
            {
                _pendingFitToViewForRegionGraphChange = false;
                await Dispatcher.UIThread.InvokeAsync(() => MainMapControl.FitToView());
            }
        }
    }

    private async void OnCharacterSystemChanged(object? sender, Hisa.Core.Models.LocalCharacterSystemChange change)
    {
        if (_boundVm?.FollowedCharacter?.CharacterId != change.CharacterId)
        {
            return;
        }

        await CenterMapOnCharacterLocationAsync(change);
    }

    private void OnFollowCharacterDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var scrollViewer = comboBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer is not null)
            {
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, 0);
            }
        }, DispatcherPriority.Background);
    }

    private async Task CenterMapOnCharacterLocationAsync(Hisa.Core.Models.LocalCharacterSystemChange location)
    {
        if (_boundVm?.FollowedCharacter?.CharacterId != location.CharacterId ||
            string.IsNullOrWhiteSpace(location.SolarSystemName))
        {
            return;
        }

        var systemId = await _boundVm.ResolveSystemIdByNameAsync(location.SolarSystemName);
        if (systemId <= 0)
        {
            return;
        }

        if (_boundVm.FollowedCharacter?.CharacterId != location.CharacterId)
        {
            return;
        }

        await NavigateAndCenterSystemFromReportAsync(_boundVm, systemId);
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

    private void DismissMapSearch(MainWindowViewModel vm)
    {
        vm.MapSearchText = string.Empty;
        vm.SelectedSearchSuggestion = null;
        vm.ClearSearchSuggestions();
        _clearSearchOnNextFocus = false;
        MainMapControl.Focus();
    }

    private static bool IsVisualWithin(Visual source, Visual target)
    {
        return source == target || source.GetSelfAndVisualAncestors().Contains(target);
    }

    private void CloseAuxiliaryWindows()
    {
        static void TryClose(Window? window)
        {
            if (window is null)
            {
                return;
            }

            try
            {
                window.Close();
            }
            catch
            {
            }
        }

        TryClose(_debugWindow);
        TryClose(_preferencesWindow);
        TryClose(_intelSettingsWindow);
        TryClose(_alertsSettingsWindow);
        TryClose(_alertPopupSettingsWindow);
        TryClose(_alertPopupWindow);
        TryClose(_charactersWindow);
        TryClose(_mapEditorWindow);
        TryClose(_sovUpgradesWindow);
        TryClose(_ansiblexNetworkWindow);
        TryClose(_lyCoveragePlannerWindow);
        TryClose(_jumpRouteOptimizerWindow);
        TryClose(_miningStatsWindow);
        TryClose(_miningSitesWindow);
        TryClose(MiningOverlayWindow.Current);
        TryClose(_zkillmailsWindow);
        TryClose(_aboutWindow);
    }
}
