using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hisa.App.ViewModels;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Logs.LocalChatLogs;

namespace Hisa.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private sealed class CharacterTrackingPreference
    {
        public required int CharacterId { get; init; }
        public string CharacterName { get; set; } = string.Empty;
        public int Priority { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public sealed class CharacterTrackingCardViewModel : INotifyPropertyChanged
    {
        private static readonly HttpClient PortraitHttpClient = new();
        private Bitmap? _portrait;
        private Bitmap? _grayscalePortrait;
        private string _name = string.Empty;
        private string _lastLocation = "Unknown";
        private string _lastUpdated = "Never";
        private bool _isEnabled = true;
        private bool _isDragging;
        private int _priority;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int CharacterId { get; init; }
        public Bitmap? Portrait
        {
            get => _portrait;
            private set
            {
                if (!ReferenceEquals(_portrait, value))
                {
                    _portrait = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Portrait)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayPortrait)));
                }
            }
        }

        public Bitmap? GrayscalePortrait
        {
            get => _grayscalePortrait;
            private set
            {
                if (!ReferenceEquals(_grayscalePortrait, value))
                {
                    _grayscalePortrait = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GrayscalePortrait)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayPortrait)));
                }
            }
        }

        public Bitmap? DisplayPortrait => IsEnabled ? Portrait : GrayscalePortrait ?? Portrait;

        public string Name
        {
            get => _name;
            set
            {
                if (!string.Equals(_name, value, StringComparison.Ordinal))
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                }
            }
        }

        public string LastLocation
        {
            get => _lastLocation;
            set
            {
                if (!string.Equals(_lastLocation, value, StringComparison.Ordinal))
                {
                    _lastLocation = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastLocation)));
                }
            }
        }

        public string LastUpdated
        {
            get => _lastUpdated;
            set
            {
                if (!string.Equals(_lastUpdated, value, StringComparison.Ordinal))
                {
                    _lastUpdated = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastUpdated)));
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayPortrait)));
                }
            }
        }

        public int Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Priority)));
                }
            }
        }

        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging != value)
                {
                    _isDragging = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragging)));
                }
            }
        }

        public string PortraitUrl => $"https://images.evetech.net/characters/{CharacterId}/portrait?tenant=tranquility&size=256";

        public async Task EnsurePortraitLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (Portrait is not null)
            {
                return;
            }

            try
            {
                using var stream = await PortraitHttpClient.GetStreamAsync(PortraitUrl, cancellationToken).ConfigureAwait(false);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
                memory.Position = 0;
                var bitmap = new Bitmap(memory);
                var grayscale = CreateGrayscaleBitmap(bitmap);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Portrait = bitmap;
                    GrayscalePortrait = grayscale;
                });
            }
            catch
            {
                // Keep placeholder when portrait can't be loaded.
            }
        }

        private static Bitmap? CreateGrayscaleBitmap(Bitmap source)
        {
            var size = source.PixelSize;
            var stride = size.Width * 4;
            var totalBytes = stride * size.Height;
            var buffer = new byte[totalBytes];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                source.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), ptr, totalBytes, stride);
                for (var i = 0; i < totalBytes; i += 4)
                {
                    var b = buffer[i];
                    var g = buffer[i + 1];
                    var r = buffer[i + 2];
                    var gray = (byte)((r * 77 + g * 150 + b * 29) >> 8);
                    buffer[i] = gray;
                    buffer[i + 1] = gray;
                    buffer[i + 2] = gray;
                }

                return new WriteableBitmap(
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul,
                    ptr,
                    size,
                    new Vector(96, 96),
                    stride);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
    }

    private sealed class SavedRegionToken
    {
        public required string RegionName { get; init; }
        public required RegionOptionKind Kind { get; init; }
    }

    public sealed class FollowCharacterOption
    {
        public required int? CharacterId { get; init; }
        public required string DisplayName { get; init; }
    }

    private readonly IMapDataService _mapDataService;
    private readonly ISettingsService _settingsService;
    private readonly IStormStateService _stormStateService;
    private readonly IHubWormholeStateService _hubWormholeStateService;
    private readonly ISovUpgradeStateService _sovUpgradeStateService;
    private readonly IMiningSiteTrackerService _miningSiteTrackerService;
    private readonly IAnsiblexNetworkStateService _ansiblexNetworkStateService;
    private readonly IIncursionStateService _incursionStateService;
    private readonly ISystemActivityStateService _systemActivityStateService;
    private readonly ILocalCharacterLocationFeed _localCharacterLocationFeed;
    private readonly IIntelFeed _intelFeed;
    private readonly IMiningSessionFeed _miningSessionFeed;
    private readonly IAlertRuleEngine _alertRuleEngine;
    private List<RegionOption> _allRegions = [];
    private readonly HashSet<string> _favoriteRegionKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressSelectedRegionReload;
    private bool _isBusy;
    private MapViewMode _selectedViewMode;
    private MapCoordinateMode _selectedCoordinateMode;
    private RegionOption? _selectedRegion;
    private MapGraph? _currentGraph;
    private MapGraph? _systemJumpGraph;
    private long? _selectedNodeId;
    private string _mapSearchText = string.Empty;
    private MapSearchCandidate? _selectedSearchSuggestion;
    private string _regionSearchText = string.Empty;
    private string _statusText = "Loading map...";
    private bool _stretchMapToWindow;
    private bool _autoHideNavigation;
    private bool _isTopNavigationCollapsed;
    private bool _isBottomNavigationCollapsed;
    private bool _isDisplaySettingsOpen;
    private MapNodeColorMode _nodeColorMode = MapNodeColorMode.None;
    private MapNodeColorMode _nodeBackgroundColorMode = MapNodeColorMode.None;
    private HostileColorSettings _hostileColorSettings = new();
    private bool _showIndicatorRegion;
    private bool _showIndicatorConstellation;
    private bool _showIndicatorSecurityStatus;
    private bool _showIndicatorStarClass;
    private bool _showIndicatorA0StarIcon = true;
    private bool _showIndicatorJoveObservatoryIcon = true;
    private bool _showIndicatorIceBeltsIcon = true;
    private bool _showIndicatorStormIcon = true;
    private bool _showIndicatorWormholeIcon = true;
    private bool _showIndicatorSovUpgradeIcon = true;
    private bool _showIndicatorIncursionIcon = true;
    private bool _showIndicatorSystemJumps = true;
    private bool _showIndicatorShipKills = true;
    private bool _showIndicatorPodKills = true;
    private bool _showIndicatorNpcKills = true;
    private bool _showIndicatorJumpRangeLy = true;
    private bool _rememberJumpRangeOrigins;
    private bool _enableLinkAnimations = true;
    private bool _enableIntelReportAnimations = true;
    private bool _showAnsiblexNetwork = true;
    private bool _infoBoxShowRegion = true;
    private bool _infoBoxShowConstellation = true;
    private bool _infoBoxShowSecurityStatus = true;
    private bool _infoBoxShowStarClass;
    private bool _infoBoxShowA0StarIcon = true;
    private bool _infoBoxShowJoveObservatoryIcon = true;
    private bool _infoBoxShowIceBeltsIcon = true;
    private bool _infoBoxShowStormIcon = true;
    private bool _infoBoxShowWormholeIcon = true;
    private bool _infoBoxShowSovUpgradeIcon = true;
    private bool _infoBoxShowIncursionIcon = true;
    private bool _infoBoxShowSystemJumps = true;
    private bool _infoBoxShowShipKills = true;
    private bool _infoBoxShowPodKills = true;
    private bool _infoBoxShowNpcKills = true;
    private bool _infoBoxShowJumpRangeLy = true;
    private bool _alwaysShowHubWormholes = true;
    private bool _alwaysShowIncursions = true;
    private bool _showMissingConnectionMarkers = true;
    private bool _isHubWormholesOverlayOpen;
    private bool _isIncursionsOverlayOpen;
    private bool _isStormsOverlayOpen;
    private bool _isIntelOverlayOpen;
    private bool _isZkillmailsOverlayOpen;
    private HubWormholeMarkerMode _hubWormholeMarkerMode = HubWormholeMarkerMode.Badge;
    private readonly Dictionary<long, JumpRangeOriginSettings> _jumpRangeOriginsLyByNodeId = [];
    private readonly Dictionary<long, uint> _jumpRangeOriginColorByNodeId = [];
    private readonly SemaphoreSlim _jumpRangeOriginsPersistenceGate = new(1, 1);
    private List<long> _jumpRangeInRangeNodeIdsForView = [];
    private IReadOnlyList<JumpRangeOriginDisplay> _jumpRangeOriginsDisplayForView = [];
    private List<long> _lyCoverageCoveredNodeIdsForView = [];
    private List<long> _lyCoverageUncoveredNodeIdsForView = [];
    private List<long> _jumpRouteNodeIdsForView = [];
    private List<long> _jumpRouteSkippedNodeIdsForView = [];
    private IReadOnlyList<WormholeOverlayCard> _hubWormholeCardsForView = [];
    private IReadOnlyList<IncursionOverlayCard> _incursionCardsForView = [];
    private IReadOnlyList<StormOverlayCard> _stormCardsForView = [];
    private IReadOnlyList<IntelOverlayCard> _intelCardsForView = [];
    private IReadOnlyList<ZkillmailOverlayCard> _zkillmailCardsForView = [];
    private readonly Dictionary<int, LocalCharacterSystemChange> _localCharacterLocationsByCharacterId = [];
    private readonly Dictionary<int, CharacterTrackingPreference> _characterTrackingPreferencesById = [];
    private readonly ObservableCollection<CharacterTrackingCardViewModel> _characterTrackingCards = [];
    private readonly ObservableCollection<CharacterTrackingCardViewModel> _enabledCharacterTrackingCards = [];
    private readonly ObservableCollection<CharacterTrackingCardViewModel> _disabledCharacterTrackingCards = [];
    private readonly ObservableCollection<FollowCharacterOption> _followCharacterOptions = [];
    private FollowCharacterOption? _followedCharacter;
    private int? _followedCharacterId;
    private bool _isRebuildingFollowCharacterOptions;
    private IReadOnlyDictionary<long, int> _characterPresenceCountsByNodeId = new Dictionary<long, int>();
    private IReadOnlyDictionary<long, IReadOnlyList<string>> _characterPresenceNamesByNodeId = new Dictionary<long, IReadOnlyList<string>>();
    private IReadOnlyDictionary<long, IReadOnlyList<int>> _characterPresenceCharacterIdsByNodeId = new Dictionary<long, IReadOnlyList<int>>();
    private IReadOnlyDictionary<long, DateTime> _characterPresenceLastUpdatedUtcByNodeId = new Dictionary<long, DateTime>();
    private bool _showIndicatorCharacterPresence = true;
    private bool _showInfoBoxCharacterPresence = true;
    private int _characterPresenceHoverMaxNames = 6;
    private string _logsRootPath = string.Empty;
    private string _logsPathValidationStatus = "Not validated.";
    private bool _isLogsPathValid;
    private readonly Dictionary<long, List<long>> _jumpRangeMembershipByNodeId = [];
    private readonly Dictionary<long, List<JumpRangeDistanceDisplay>> _jumpRangeDistancesByNodeId = [];
    private readonly Dictionary<long, IntelSystemSnapshot> _intelSnapshotsBySystemId = [];
    private readonly Dictionary<int, MiningCharacterStatsSnapshot> _miningStatsByCharacterId = [];
    private readonly Dictionary<long, (string RegionName, string ConstellationName)> _systemLocationById = [];
    private readonly object _systemLocationGate = new();
    private readonly List<IntelChatReport> _intelReportHistory = [];
    private IReadOnlyList<AlertRule> _alertRules = [];
    private readonly Dictionary<AlertEventType, HashSet<string>> _observedSpawnAlertKeysByEventType = [];
    private readonly object _observedSpawnAlertGate = new();
    private AlertPopupSettings _alertPopupSettings = new();
    private readonly Dictionary<string, int> _characterIdByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _invalidHostilePilotNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _characterIdLookupInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _characterIdLookupGate = new();
    private readonly DispatcherTimer _intelOverlayAgeTimer;
    private readonly DispatcherTimer _activityCardsRebuildDebounceTimer;
    private readonly DispatcherTimer _overlayCardUiRefreshDebounceTimer;
    private bool _pendingIntelCardsUiRefresh;
    private bool _pendingZkillCardsUiRefresh;
    private int _activityCardsRebuildVersion;
    private int _activityCardsRebuildRunningVersion;
    private bool _activityCardsRebuildInFlight;
    private static readonly HttpClient IntelPortraitHttpClient = new();
    private static readonly ConcurrentDictionary<int, Bitmap> IntelPortraitBitmapCache = new();
    private static readonly ConcurrentDictionary<int, Bitmap> IntelCorporationBitmapCache = new();
    private static readonly ConcurrentDictionary<int, Bitmap> IntelAllianceBitmapCache = new();
    private static readonly ConcurrentDictionary<int, Bitmap> IntelShipBitmapCache = new();
    private static readonly ConcurrentDictionary<int, (int CorpId, int? AllianceId)> IntelAffiliationsByCharacterId = new();
    private static readonly ConcurrentDictionary<int, string> IntelCharacterNamesById = new();
    private static readonly ConcurrentDictionary<int, string> IntelCorporationTickersById = new();
    private static readonly ConcurrentDictionary<int, string> IntelAllianceTickersById = new();
    private static readonly HashSet<string> ReconPriorityShipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Arazu", "Lachesis", "Falcon", "Rook", "Huginn", "Rapier", "Pilgrim", "Curse"
    };
    private static readonly HashSet<string> InterdictionPriorityShipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sabre", "Flycatcher", "Heretic", "Eris", "Broadsword", "Onyx", "Devoter", "Phobos"
    };
    private const int MaxIntelHoverShipRows = 8;
    private static readonly ConcurrentDictionary<int, byte> IntelImageLoadingByCharacterId = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> IntelBitmapLoadLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string IntelImageCacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HISA",
        "IntelImageCache");
    private IReadOnlyDictionary<long, IReadOnlyList<string>> _intelIconKeysByNodeId = new Dictionary<long, IReadOnlyList<string>>();
    private IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverReport>> _intelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>();
    private IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverKillmail>> _zkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>>();
    private IReadOnlyDictionary<long, int> _intelHostileScoresByNodeId = new Dictionary<long, int>();
    private bool _limitHubWormholesToCurrentRegion;
    private bool _limitIncursionsToCurrentRegion;
    private bool _limitStormsToCurrentRegion;
    private bool _limitIntelReportsToCurrentRegion;
    private bool _limitZkillmailsToCurrentRegion;
    private bool _hideZkillmailsOutsideKnownSpace;
    private bool _intelEnabled = true;
    private bool _miningEnabled;
    private bool _isMiningStatsLoading;
    private bool _isMiningOverlayVisible;
    private bool _preferredMiningOverlayVisible;
    private MiningStatsRangeMode _selectedMiningRangeMode = MiningStatsRangeMode.CurrentSession;
    private MiningOverlayRangeMode _selectedMiningOverlayRangeMode = MiningOverlayRangeMode.Rolling10Minutes;
    private string _miningRefineYieldPercentText = "90.63";
    private string _intelIncludeChannelsText = string.Empty;
    private readonly ObservableCollection<MiningStatsCard> _miningStatsCards = [];
    private MiningStatsCard? _aggregateMiningStatsCard;
    private MiningStatsCard? _overlayAggregateMiningStatsCard;
    private CancellationTokenSource? _miningStatsRefreshCts;
    private readonly DispatcherTimer _miningOverlayRefreshTimer;
    private int _intelSystemExpiryMinutes = 15;
    private int _intelListExpiryMinutes = 30;
    private readonly object _knownSpaceGate = new();
    private readonly HashSet<long> _knownSpaceSystemIds = [];
    private readonly Dictionary<string, long> _knownSpaceSystemIdByName = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _searchSuggestionsCts;
    private MapCoordinateMode _savedUniverseCoordinateMode = MapCoordinateMode.SdePlanarXY;
    private MapCoordinateMode _savedRegionCoordinateMode = MapCoordinateMode.SdePlanarXY;
    private bool _isInitializing = true;
    private const string ViewModeKey = "Map.SelectedViewMode";
    private const string RegionIdKey = "Map.SelectedRegionId";
    private const string RegionTokenKey = "Map.SelectedRegionToken";
    private const string FavoriteRegionTokensKey = "Map.FavoriteRegionTokens";
    private const string CoordinateModeKey = "Map.SelectedCoordinateMode";
    private const string CoordinateModeUniverseKey = "Map.SelectedCoordinateMode.Universe";
    private const string CoordinateModeRegionKey = "Map.SelectedCoordinateMode.Region";
    private const string StretchMapToWindowKey = "Map.StretchToWindow";
    private const string AutoHideNavigationKey = "Window.AutoHideNavigation";
    private const string NodeColorModeKey = "Map.NodeColorMode";
    private const string NodeBackgroundColorModeKey = "Map.NodeBackgroundColorMode";
    private const string HostileColorSettingsKey = "Map.HostileColorSettings";
    private const string ShowIndicatorRegionKey = "Map.ShowIndicatorRegion";
    private const string ShowIndicatorConstellationKey = "Map.ShowIndicatorConstellation";
    private const string ShowIndicatorSecurityStatusKey = "Map.ShowIndicatorSecurityStatus";
    private const string ShowIndicatorStarClassKey = "Map.ShowIndicatorStarClass";
    private const string ShowIndicatorA0StarIconKey = "Map.ShowIndicatorA0StarIcon";
    private const string ShowIndicatorJoveObservatoryIconKey = "Map.ShowIndicatorJoveObservatoryIcon";
    private const string ShowIndicatorIceBeltsIconKey = "Map.ShowIndicatorIceBeltsIcon";
    private const string ShowIndicatorStormIconKey = "Map.ShowIndicatorStormIcon";
    private const string ShowIndicatorWormholeIconKey = "Map.ShowIndicatorWormholeIcon";
    private const string ShowIndicatorSovUpgradeIconKey = "Map.ShowIndicatorSovUpgradeIcon";
    private const string ShowIndicatorIncursionIconKey = "Map.ShowIndicatorIncursionIcon";
    private const string ShowIndicatorSystemJumpsKey = "Map.ShowIndicatorSystemJumps";
    private const string ShowIndicatorShipKillsKey = "Map.ShowIndicatorShipKills";
    private const string ShowIndicatorPodKillsKey = "Map.ShowIndicatorPodKills";
    private const string ShowIndicatorNpcKillsKey = "Map.ShowIndicatorNpcKills";
    private const string ShowIndicatorJumpRangeLyKey = "Map.ShowIndicatorJumpRangeLy";
    private const string RememberJumpRangeOriginsKey = "Map.RememberJumpRangeOrigins";
    private const string JumpRangeOriginsKey = "Map.JumpRangeOrigins";
    private const string ShowIndicatorCharacterPresenceKey = "Map.ShowIndicatorCharacterPresence";
    private const string ShowInfoBoxCharacterPresenceKey = "Map.ShowInfoBoxCharacterPresence";
    private const string CharacterPresenceHoverMaxNamesKey = "Map.CharacterPresenceHoverMaxNames";
    private const string EnableLinkAnimationsKey = "Map.EnableLinkAnimations";
    private const string EnableIntelReportAnimationsKey = "Map.EnableIntelReportAnimations";
    private const string ShowAnsiblexNetworkKey = "Map.ShowAnsiblexNetwork";
    private const string InfoBoxShowRegionKey = "Map.InfoBoxShowRegion";
    private const string InfoBoxShowConstellationKey = "Map.InfoBoxShowConstellation";
    private const string InfoBoxShowSecurityStatusKey = "Map.InfoBoxShowSecurityStatus";
    private const string InfoBoxShowStarClassKey = "Map.InfoBoxShowStarClass";
    private const string InfoBoxShowA0StarIconKey = "Map.InfoBoxShowA0StarIcon";
    private const string InfoBoxShowJoveObservatoryIconKey = "Map.InfoBoxShowJoveObservatoryIcon";
    private const string InfoBoxShowIceBeltsIconKey = "Map.InfoBoxShowIceBeltsIcon";
    private const string InfoBoxShowStormIconKey = "Map.InfoBoxShowStormIcon";
    private const string InfoBoxShowWormholeIconKey = "Map.InfoBoxShowWormholeIcon";
    private const string InfoBoxShowSovUpgradeIconKey = "Map.InfoBoxShowSovUpgradeIcon";
    private const string InfoBoxShowIncursionIconKey = "Map.InfoBoxShowIncursionIcon";
    private const string InfoBoxShowSystemJumpsKey = "Map.InfoBoxShowSystemJumps";
    private const string InfoBoxShowShipKillsKey = "Map.InfoBoxShowShipKills";
    private const string InfoBoxShowPodKillsKey = "Map.InfoBoxShowPodKills";
    private const string InfoBoxShowNpcKillsKey = "Map.InfoBoxShowNpcKills";
    private const string InfoBoxShowJumpRangeLyKey = "Map.InfoBoxShowJumpRangeLy";
    private const string IndicatorSovFilterKeysKey = "Map.IndicatorSovFilter.Keys";
    private const string OverlaySovFilterKeysKey = "Map.OverlaySovFilter.Keys";
    private const string IndicatorSovFilterConfiguredKey = "Map.IndicatorSovFilter.Configured";
    private const string OverlaySovFilterConfiguredKey = "Map.OverlaySovFilter.Configured";
    private const string AlwaysShowHubWormholesKey = "Map.AlwaysShowHubWormholes";
    private const string AlwaysShowIncursionsKey = "Map.AlwaysShowIncursions";
    private const string HubWormholeMarkerModeKey = "Map.HubWormholeMarkerMode";
    private const string ShowMissingConnectionMarkersKey = "Map.ShowMissingConnectionMarkers";
    private const string WindowPlacementKey = "Window.Main.Placement";
    private const string MapViewportPrefixKey = "Map.Viewport";
    private const string TrackingLogsRootPathKey = "Tracking.LogsRootPath";
    private const string TrackingCharacterPreferencesKey = "Tracking.CharacterPreferences";
    private const string FollowCharacterIdKey = "Tracking.FollowCharacterId";
    private const string IntelEnabledKey = "Intel.Enabled";
    private const string MiningEnabledKey = "Mining.Enabled";
    private const string MiningRefineYieldPercentKey = "Mining.RefineYieldPercent";
    private const string MiningRangeModeKey = "Mining.RangeMode";
    private const string MiningOverlayRangeModeKey = "Mining.Overlay.RangeMode";
    private const string MiningOverlayVisibleKey = "Mining.Overlay.Visible";
    private const string MiningStatsWindowPlacementKey = "Window.MiningStats.Placement";
    private const string MiningOverlayWindowPlacementKey = "Window.MiningOverlay.Placement";
    private const string IntelIncludeChannelsKey = "Intel.Channels.Include";
    private const string HubWormholeLimitToCurrentRegionKey = "Intel.HubWormholes.Overlay.LimitToCurrentRegion";
    private const string IncursionLimitToCurrentRegionKey = "Intel.Incursions.Overlay.LimitToCurrentRegion";
    private const string StormLimitToCurrentRegionKey = "Intel.Storms.Overlay.LimitToCurrentRegion";
    private const string IntelLimitToCurrentRegionKey = "Intel.Overlay.LimitToCurrentRegion";
    private const string ZkillLimitToCurrentRegionKey = "Intel.Zkill.Overlay.LimitToCurrentRegion";
    private const string ZkillHideOutsideKnownSpaceKey = "Intel.Zkill.HideOutsideKnownSpace";
    private const string IntelSystemExpiryMinutesKey = "Intel.SystemExpiryMinutes";
    private const string IntelListExpiryMinutesKey = "Intel.ListExpiryMinutes";
    private const string AlertRulesKey = "Alerts.Rules";
    private const string AlertPopupSettingsKey = "Alerts.Popup.Settings";
    private const int MaxIntelReportHistory = 350;
    private const int MaxIntelOverlayCards = 140;
    private const int MaxIntelShipBitmapCacheItems = 600;
    private const int MaxIntelPortraitBitmapCacheItems = 500;
    private const int MaxIntelCorporationBitmapCacheItems = 500;
    private const int MaxIntelAllianceBitmapCacheItems = 500;
    private const int MaxParallelImageRequests = 10;
    private static readonly TimeSpan OverlayAgeChipRefreshInterval = TimeSpan.FromSeconds(1);
    private readonly Task _initialLoadTask;
    private readonly DispatcherTimer _miningSiteReminderTimer;
    private readonly DispatcherTimer _miningSiteAlertDispatchTimer;
    private readonly Queue<MiningSiteReport> _pendingMiningSiteAlerts = [];
    private readonly HashSet<string> _pendingMiningSiteAlertKeys = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(
        IMapDataService mapDataService,
        ISettingsService settingsService,
        IStormStateService stormStateService,
        IHubWormholeStateService hubWormholeStateService,
        ISovUpgradeStateService sovUpgradeStateService,
        IMiningSiteTrackerService miningSiteTrackerService,
        IAnsiblexNetworkStateService ansiblexNetworkStateService,
        IIncursionStateService incursionStateService,
        ISystemActivityStateService systemActivityStateService,
        ILocalCharacterLocationFeed localCharacterLocationFeed,
        IIntelFeed intelFeed,
        IMiningSessionFeed miningSessionFeed,
        IAlertRuleEngine alertRuleEngine)
    {
        _mapDataService = mapDataService;
        _settingsService = settingsService;
        _stormStateService = stormStateService;
        _hubWormholeStateService = hubWormholeStateService;
        _sovUpgradeStateService = sovUpgradeStateService;
        _miningSiteTrackerService = miningSiteTrackerService;
        _ansiblexNetworkStateService = ansiblexNetworkStateService;
        _incursionStateService = incursionStateService;
        _systemActivityStateService = systemActivityStateService;
        _localCharacterLocationFeed = localCharacterLocationFeed;
        _intelFeed = intelFeed;
        _miningSessionFeed = miningSessionFeed;
        _alertRuleEngine = alertRuleEngine;
        ViewModes = new ObservableCollection<MapViewMode>(Enum.GetValues<MapViewMode>());
        CoordinateModes = new ObservableCollection<MapCoordinateMode>(Enum.GetValues<MapCoordinateMode>());
        MiningRangeModes = new ObservableCollection<MiningStatsRangeMode>(Enum.GetValues<MiningStatsRangeMode>());
        MiningOverlayRangeModes = new ObservableCollection<MiningOverlayRangeMode>(Enum.GetValues<MiningOverlayRangeMode>());
        var orderedColorModes = new List<MapNodeColorMode> { MapNodeColorMode.None, MapNodeColorMode.Hostiles };
        orderedColorModes.AddRange(
            Enum.GetValues<MapNodeColorMode>()
                .Where(x => x != MapNodeColorMode.None && x != MapNodeColorMode.Hostiles));
        NodeColorModes = new ObservableCollection<MapNodeColorMode>(orderedColorModes);
        HubWormholeMarkerModes = new ObservableCollection<HubWormholeMarkerMode>(Enum.GetValues<HubWormholeMarkerMode>());
        Regions = [];
        _stormStateService.StormSnapshotUpdated += OnStormSnapshotUpdated;
        _hubWormholeStateService.HubWormholeSnapshotUpdated += OnHubWormholeSnapshotUpdated;
        _sovUpgradeStateService.SnapshotUpdated += OnSovUpgradesSnapshotUpdated;
        _miningSiteTrackerService.ReportsUpdated += OnMiningSiteReportsUpdated;
        _ansiblexNetworkStateService.SnapshotUpdated += OnAnsiblexNetworkSnapshotUpdated;
        _incursionStateService.IncursionSnapshotUpdated += OnIncursionSnapshotUpdated;
        _systemActivityStateService.SystemActivitySnapshotUpdated += OnSystemActivitySnapshotUpdated;
        _localCharacterLocationFeed.SystemChanged += OnLocalCharacterSystemChanged;
        _intelFeed.ReportReceived += OnIntelReportReceived;
        _intelFeed.SnapshotUpdated += OnIntelSnapshotUpdated;
        _miningSessionFeed.SnapshotUpdated += OnMiningSnapshotUpdated;
        _intelOverlayAgeTimer = new DispatcherTimer { Interval = OverlayAgeChipRefreshInterval };
        _intelOverlayAgeTimer.Tick += (_, _) => RefreshIntelOverlayCardAges();
        _intelOverlayAgeTimer.Start();
        _miningSiteReminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _miningSiteReminderTimer.Tick += async (_, _) => await CheckMiningSiteRemindersAsync();
        _miningSiteReminderTimer.Start();
        _miningSiteAlertDispatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _miningSiteAlertDispatchTimer.Tick += async (_, _) => await DispatchNextMiningSiteAlertAsync();
        _miningOverlayRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _miningOverlayRefreshTimer.Tick += async (_, _) =>
        {
            if (IsMiningOverlayVisible)
            {
                await RefreshMiningOverlayAsync();
            }
        };
        _activityCardsRebuildDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _activityCardsRebuildDebounceTimer.Tick += (_, _) =>
        {
            _activityCardsRebuildDebounceTimer.Stop();
            _ = RunScheduledActivityCardsRebuildAsync();
        };
        _overlayCardUiRefreshDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _overlayCardUiRefreshDebounceTimer.Tick += (_, _) =>
        {
            _overlayCardUiRefreshDebounceTimer.Stop();
            if (_pendingIntelCardsUiRefresh)
            {
                _pendingIntelCardsUiRefresh = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelCardsForView)));
            }

            if (_pendingZkillCardsUiRefresh)
            {
                _pendingZkillCardsUiRefresh = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailCardsForView)));
            }
        };
        _initialLoadTask = LoadAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AlertTriggered>? AlertTriggered;

    public bool IsDebugBuild
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public ObservableCollection<MapViewMode> ViewModes { get; }
    public ObservableCollection<MapCoordinateMode> CoordinateModes { get; }
    public ObservableCollection<MiningStatsRangeMode> MiningRangeModes { get; }
    public ObservableCollection<MiningOverlayRangeMode> MiningOverlayRangeModes { get; }
    public ObservableCollection<MapNodeColorMode> NodeColorModes { get; }
    public ObservableCollection<HubWormholeMarkerMode> HubWormholeMarkerModes { get; }
    public ObservableCollection<RegionOption> Regions { get; }
    public ObservableCollection<MapSearchCandidate> SearchSuggestions { get; } = [];
    public ObservableCollection<SovUpgradeDisplayOption> IndicatorSovUpgradeOptions { get; } = [];
    public ObservableCollection<SovUpgradeDisplayOption> OverlaySovUpgradeOptions { get; } = [];
    public IEnumerable<long> MissingConnectionNodeIdsForView { get; private set; } = [];
    public IEnumerable<long> JumpRangeOriginNodeIdsForView => _jumpRangeOriginsLyByNodeId.Keys;
    public IEnumerable<long> JumpRangeInRangeNodeIdsForView => _jumpRangeInRangeNodeIdsForView;
    public IReadOnlyList<JumpRangeOriginDisplay> JumpRangeOriginsDisplayForView => _jumpRangeOriginsDisplayForView;
    public IEnumerable<long> LyCoverageCoveredNodeIdsForView => _lyCoverageCoveredNodeIdsForView;
    public IEnumerable<long> LyCoverageUncoveredNodeIdsForView => _lyCoverageUncoveredNodeIdsForView;
    public IEnumerable<long> JumpRouteNodeIdsForView => _jumpRouteNodeIdsForView;
    public IEnumerable<long> JumpRouteSkippedNodeIdsForView => _jumpRouteSkippedNodeIdsForView;
    public IReadOnlyList<MapLink> AnsiblexLinksForView { get; private set; } = [];
    public IReadOnlyList<WormholeOverlayCard> HubWormholeCardsForView => _hubWormholeCardsForView;
    public IReadOnlyList<IncursionOverlayCard> IncursionCardsForView => _incursionCardsForView;
    public IReadOnlyList<StormOverlayCard> StormCardsForView => _stormCardsForView;
    public IReadOnlyList<IntelOverlayCard> IntelCardsForView => _intelCardsForView;
    public IReadOnlyList<ZkillmailOverlayCard> ZkillmailCardsForView => _zkillmailCardsForView;
    public IReadOnlyDictionary<long, int> CharacterPresenceCountsByNodeIdForView => _characterPresenceCountsByNodeId;
    public IReadOnlyDictionary<long, IReadOnlyList<string>> CharacterPresenceNamesByNodeIdForView => _characterPresenceNamesByNodeId;
    public IReadOnlyDictionary<long, IReadOnlyList<int>> CharacterPresenceCharacterIdsByNodeIdForView => _characterPresenceCharacterIdsByNodeId;
    public IReadOnlyDictionary<long, DateTime> CharacterPresenceLastUpdatedUtcByNodeIdForView => _characterPresenceLastUpdatedUtcByNodeId;
    public IReadOnlyDictionary<long, IReadOnlyList<string>> IntelIconKeysByNodeIdForView => _intelIconKeysByNodeId;
    public IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverReport>> IntelRecentReportsByNodeIdForView => _intelRecentReportsByNodeId;
    public IReadOnlyDictionary<long, IReadOnlyList<IntelMapHoverKillmail>> ZkillRecentReportsByNodeIdForView => _zkillRecentReportsByNodeId;
    public IReadOnlyDictionary<long, int> IntelHostileScoresByNodeIdForView => _intelHostileScoresByNodeId;
    public ObservableCollection<CharacterTrackingCardViewModel> CharacterTrackingCards => _characterTrackingCards;
    public ObservableCollection<CharacterTrackingCardViewModel> EnabledCharacterTrackingCards => _enabledCharacterTrackingCards;
    public ObservableCollection<CharacterTrackingCardViewModel> DisabledCharacterTrackingCards => _disabledCharacterTrackingCards;
    public ObservableCollection<FollowCharacterOption> FollowCharacterOptions => _followCharacterOptions;
    public bool HasFollowableCharacters => _followCharacterOptions.Count > 1;
    public FollowCharacterOption? FollowedCharacter
    {
        get => _followedCharacter;
        set
        {
            var characterId = value?.CharacterId;
            if (_isRebuildingFollowCharacterOptions && value is null)
            {
                return;
            }

            if (ReferenceEquals(_followedCharacter, value))
            {
                return;
            }

            var changed = _followedCharacterId != characterId;
            _followedCharacter = value;
            _followedCharacterId = characterId;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FollowedCharacter)));

            if (changed && !_isInitializing)
            {
                _ = _settingsService.SetAsync(FollowCharacterIdKey, characterId);
            }
        }
    }

    public event EventHandler<LocalCharacterSystemChange>? CharacterSystemChanged;
    public string HubWormholeOverlayTitle => $"Thera/Turnur Wormholes ({_hubWormholeCardsForView.Count})";
    public string IncursionOverlayTitle => $"Incursions ({_incursionCardsForView.Count})";
    public string StormOverlayTitle => $"Metaliminal Storms ({_stormCardsForView.Count})";
    public string IntelOverlayTitle => $"Intel Reports ({_intelCardsForView.Count})";
    public string ZkillmailOverlayTitle => $"zKillmails ({_zkillmailCardsForView.Count})";
    public bool LimitHubWormholesToCurrentRegion
    {
        get => _limitHubWormholesToCurrentRegion;
        set
        {
            if (!SetProperty(ref _limitHubWormholesToCurrentRegion, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(HubWormholeLimitToCurrentRegionKey, value);
            ScheduleActivityCardsRebuild();
        }
    }

    public bool LimitIncursionsToCurrentRegion
    {
        get => _limitIncursionsToCurrentRegion;
        set
        {
            if (!SetProperty(ref _limitIncursionsToCurrentRegion, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(IncursionLimitToCurrentRegionKey, value);
            ScheduleActivityCardsRebuild();
        }
    }

    public bool LimitStormsToCurrentRegion
    {
        get => _limitStormsToCurrentRegion;
        set
        {
            if (!SetProperty(ref _limitStormsToCurrentRegion, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(StormLimitToCurrentRegionKey, value);
            ScheduleActivityCardsRebuild();
        }
    }

    public bool LimitIntelReportsToCurrentRegion
    {
        get => _limitIntelReportsToCurrentRegion;
        set
        {
            if (!SetProperty(ref _limitIntelReportsToCurrentRegion, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(IntelLimitToCurrentRegionKey, value);
            ScheduleActivityCardsRebuild();
        }
    }

    public bool LimitZkillmailsToCurrentRegion
    {
        get => _limitZkillmailsToCurrentRegion;
        set
        {
            if (!SetProperty(ref _limitZkillmailsToCurrentRegion, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(ZkillLimitToCurrentRegionKey, value);
            ScheduleActivityCardsRebuild();
        }
    }

    public bool HideZkillmailsOutsideKnownSpace
    {
        get => _hideZkillmailsOutsideKnownSpace;
        set
        {
            if (!SetProperty(ref _hideZkillmailsOutsideKnownSpace, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(ZkillHideOutsideKnownSpaceKey, value);
            ScheduleActivityCardsRebuild();
            RebuildIntelPresenceForView();
        }
    }
    public string LogsPathValidationStatus => _logsPathValidationStatus;
    public bool IsLogsPathValid => _isLogsPathValid;
    public IReadOnlyDictionary<long, IReadOnlyList<long>> JumpRangeMembershipByNodeIdForView =>
        _jumpRangeMembershipByNodeId.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<long>)kvp.Value);
    public IReadOnlyDictionary<long, IReadOnlyList<JumpRangeDistanceDisplay>> JumpRangeDistancesByNodeIdForView =>
        _jumpRangeDistancesByNodeId.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<JumpRangeDistanceDisplay>)kvp.Value);
    public bool IntelEnabled
    {
        get => _intelEnabled;
        set => SetProperty(ref _intelEnabled, value);
    }

    public string IntelIncludeChannelsText
    {
        get => _intelIncludeChannelsText;
        set => SetProperty(ref _intelIncludeChannelsText, value);
    }

    public int IntelSystemExpiryMinutes
    {
        get => _intelSystemExpiryMinutes;
        set => SetProperty(ref _intelSystemExpiryMinutes, Math.Clamp(value, 1, 180));
    }

    public int IntelListExpiryMinutes
    {
        get => _intelListExpiryMinutes;
        set => SetProperty(ref _intelListExpiryMinutes, Math.Clamp(value, 1, 240));
    }

    public MapViewMode SelectedViewMode
    {
        get => _selectedViewMode;
        set
        {
            if (SetProperty(ref _selectedViewMode, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUniverseMode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUniverseRegionsMode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRegionMode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCoordinateSelectorVisible)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRegionSelectorVisible)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchWatermark)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAnsiblexLegendVisible)));
                EnforceCoordinateModeForView();
                if (!_isInitializing)
                {
                    _ = _settingsService.SetAsync(ViewModeKey, value);
                    _ = UpdateSearchSuggestionsAsync(MapSearchText);
                    _ = ReloadGraphAsync();
                }
            }
        }
    }

    public bool IsUniverseMode
    {
        get => SelectedViewMode == MapViewMode.Universe;
        set
        {
            if (value)
            {
                SelectedViewMode = MapViewMode.Universe;
            }
        }
    }

    public bool IsUniverseRegionsMode
    {
        get => SelectedViewMode == MapViewMode.UniverseRegions;
        set
        {
            if (value)
            {
                SelectedViewMode = MapViewMode.UniverseRegions;
            }
        }
    }

    public bool IsRegionMode
    {
        get => SelectedViewMode == MapViewMode.Region;
        set
        {
            if (value)
            {
                SelectedViewMode = MapViewMode.Region;
            }
        }
    }

    public bool IsCoordinateSelectorVisible => SelectedViewMode != MapViewMode.UniverseRegions;
    public bool IsRegionSelectorVisible => SelectedViewMode == MapViewMode.Region;
    public string SearchWatermark => SelectedViewMode == MapViewMode.UniverseRegions
        ? "Search region"
        : "Search region, constellation, system...";

    public bool StretchMapToWindow
    {
        get => _stretchMapToWindow;
        set
        {
            if (SetProperty(ref _stretchMapToWindow, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(StretchMapToWindowKey, value);
            }
        }
    }

    public bool IsDisplaySettingsOpen
    {
        get => _isDisplaySettingsOpen;
        set => SetProperty(ref _isDisplaySettingsOpen, value);
    }

    public MapNodeColorMode NodeColorMode
    {
        get => _nodeColorMode;
        set
        {
            if (SetProperty(ref _nodeColorMode, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(NodeColorModeKey, value);
            }
        }
    }

    public MapNodeColorMode NodeBackgroundColorMode
    {
        get => _nodeBackgroundColorMode;
        set
        {
            if (SetProperty(ref _nodeBackgroundColorMode, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(NodeBackgroundColorModeKey, value);
            }
        }
    }

    public bool ShowIndicatorRegion
    {
        get => _showIndicatorRegion;
        set
        {
            if (SetProperty(ref _showIndicatorRegion, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorRegionKey, value);
            }
        }
    }

    public bool ShowIndicatorConstellation
    {
        get => _showIndicatorConstellation;
        set
        {
            if (SetProperty(ref _showIndicatorConstellation, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorConstellationKey, value);
            }
        }
    }

    public bool ShowIndicatorSecurityStatus
    {
        get => _showIndicatorSecurityStatus;
        set
        {
            if (SetProperty(ref _showIndicatorSecurityStatus, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorSecurityStatusKey, value);
            }
        }
    }

    public bool ShowIndicatorStarClass
    {
        get => _showIndicatorStarClass;
        set
        {
            if (SetProperty(ref _showIndicatorStarClass, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorStarClassKey, value);
            }
        }
    }

    public bool ShowIndicatorA0StarIcon
    {
        get => _showIndicatorA0StarIcon;
        set
        {
            if (SetProperty(ref _showIndicatorA0StarIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorA0StarIconKey, value);
            }
        }
    }

    public bool ShowIndicatorJoveObservatoryIcon
    {
        get => _showIndicatorJoveObservatoryIcon;
        set
        {
            if (SetProperty(ref _showIndicatorJoveObservatoryIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorJoveObservatoryIconKey, value);
            }
        }
    }

    public bool ShowIndicatorIceBeltsIcon
    {
        get => _showIndicatorIceBeltsIcon;
        set
        {
            if (SetProperty(ref _showIndicatorIceBeltsIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorIceBeltsIconKey, value);
            }
        }
    }

    public bool ShowIndicatorStormIcon
    {
        get => _showIndicatorStormIcon;
        set
        {
            if (SetProperty(ref _showIndicatorStormIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorStormIconKey, value);
            }
        }
    }

    public bool ShowIndicatorWormholeIcon
    {
        get => _showIndicatorWormholeIcon;
        set
        {
            if (SetProperty(ref _showIndicatorWormholeIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorWormholeIconKey, value);
            }
        }
    }

    public bool ShowIndicatorSovUpgradeIcon
    {
        get => _showIndicatorSovUpgradeIcon;
        set
        {
            if (SetProperty(ref _showIndicatorSovUpgradeIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorSovUpgradeIconKey, value);
            }
        }
    }

    public bool ShowIndicatorIncursionIcon
    {
        get => _showIndicatorIncursionIcon;
        set
        {
            if (SetProperty(ref _showIndicatorIncursionIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorIncursionIconKey, value);
            }
        }
    }

    public bool ShowIndicatorJumpRangeLy
    {
        get => _showIndicatorJumpRangeLy;
        set
        {
            if (SetProperty(ref _showIndicatorJumpRangeLy, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorJumpRangeLyKey, value);
            }
        }
    }

    public HostileColorSettings HostileColorSettings
    {
        get => _hostileColorSettings;
        private set => SetProperty(ref _hostileColorSettings, value);
    }

    public HostileColorSettings GetHostileColorSettingsSnapshot() => new()
    {
        LowMaxHostiles = HostileColorSettings.LowMaxHostiles,
        MediumMaxHostiles = HostileColorSettings.MediumMaxHostiles,
        HighMaxHostiles = HostileColorSettings.HighMaxHostiles,
        LowColorHex = HostileColorSettings.LowColorHex,
        MediumColorHex = HostileColorSettings.MediumColorHex,
        HighColorHex = HostileColorSettings.HighColorHex,
        AboveHighColorHex = HostileColorSettings.AboveHighColorHex
    };

    public async Task SaveHostileColorSettingsAsync(HostileColorSettings settings)
    {
        var sanitized = SanitizeHostileColorSettings(settings);
        HostileColorSettings = sanitized;
        await _settingsService.SetAsync(HostileColorSettingsKey, sanitized);
        await RebuildActivityCardsAsync(CurrentGraph);
    }

    public bool RememberJumpRangeOrigins
    {
        get => _rememberJumpRangeOrigins;
        set
        {
            if (!SetProperty(ref _rememberJumpRangeOrigins, value) || _isInitializing)
            {
                return;
            }

            _ = _settingsService.SetAsync(RememberJumpRangeOriginsKey, value);
            _ = PersistJumpRangeOriginsAsync();
        }
    }

    public bool ShowIndicatorSystemJumps
    {
        get => _showIndicatorSystemJumps;
        set
        {
            if (SetProperty(ref _showIndicatorSystemJumps, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorSystemJumpsKey, value);
            }
        }
    }

    public bool ShowIndicatorShipKills
    {
        get => _showIndicatorShipKills;
        set
        {
            if (SetProperty(ref _showIndicatorShipKills, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorShipKillsKey, value);
            }
        }
    }

    public bool ShowIndicatorPodKills
    {
        get => _showIndicatorPodKills;
        set
        {
            if (SetProperty(ref _showIndicatorPodKills, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorPodKillsKey, value);
            }
        }
    }

    public bool ShowIndicatorNpcKills
    {
        get => _showIndicatorNpcKills;
        set
        {
            if (SetProperty(ref _showIndicatorNpcKills, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorNpcKillsKey, value);
            }
        }
    }

    public bool ShowIndicatorCharacterPresence
    {
        get => _showIndicatorCharacterPresence;
        set
        {
            if (SetProperty(ref _showIndicatorCharacterPresence, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowIndicatorCharacterPresenceKey, value);
            }
        }
    }

    public bool ShowInfoBoxCharacterPresence
    {
        get => _showInfoBoxCharacterPresence;
        set
        {
            if (SetProperty(ref _showInfoBoxCharacterPresence, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowInfoBoxCharacterPresenceKey, value);
            }
        }
    }

    public int CharacterPresenceHoverMaxNames
    {
        get => _characterPresenceHoverMaxNames;
        set
        {
            var clamped = Math.Clamp(value, 1, 12);
            if (SetProperty(ref _characterPresenceHoverMaxNames, clamped) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(CharacterPresenceHoverMaxNamesKey, clamped);
            }
        }
    }

    public string LogsRootPath
    {
        get => _logsRootPath;
        set => SetProperty(ref _logsRootPath, value);
    }

    public bool MiningEnabled
    {
        get => _miningEnabled;
        set
        {
            if (!SetProperty(ref _miningEnabled, value) || _isInitializing)
            {
                return;
            }

            _ = _settingsService.SetAsync(MiningEnabledKey, value);
            StatusText = value
                ? "Mining tracking enabled. Restart HISA to begin reading Gamelogs."
                : "Mining tracking disabled. Gamelogs will not be read after restart.";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MiningWindowStatus)));
        }
    }

    public ObservableCollection<MiningStatsCard> MiningStatsCards => _miningStatsCards;

    public MiningStatsCard? AggregateMiningStatsCard => _aggregateMiningStatsCard;

    public MiningStatsCard? OverlayAggregateMiningStatsCard => _overlayAggregateMiningStatsCard;

    public bool HasAggregateMiningStats => _aggregateMiningStatsCard is not null;

    public bool HasOverlayAggregateMiningStats => _overlayAggregateMiningStatsCard is not null;

    public MiningStatsRangeMode SelectedMiningRangeMode
    {
        get => _selectedMiningRangeMode;
        set
        {
            if (!SetProperty(ref _selectedMiningRangeMode, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(MiningRangeModeKey, value);
            _ = RefreshMiningStatsForSelectedRangeAsync();
            if (IsMiningOverlayVisible && SelectedMiningOverlayRangeMode == MiningOverlayRangeMode.UseSelectedRange)
            {
                _ = RefreshMiningOverlayAsync();
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MiningWindowStatus)));
        }
    }

    public MiningOverlayRangeMode SelectedMiningOverlayRangeMode
    {
        get => _selectedMiningOverlayRangeMode;
        set
        {
            if (!SetProperty(ref _selectedMiningOverlayRangeMode, value))
            {
                return;
            }

            _ = _settingsService.SetAsync(MiningOverlayRangeModeKey, value);
            if (IsMiningOverlayVisible)
            {
                _ = RefreshMiningOverlayAsync();
            }
        }
    }

    public bool IsMiningStatsLoading
    {
        get => _isMiningStatsLoading;
        private set
        {
            if (SetProperty(ref _isMiningStatsLoading, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MiningWindowStatus)));
            }
        }
    }

    public bool IsMiningOverlayVisible
    {
        get => _isMiningOverlayVisible;
        set => SetMiningOverlayVisibility(value, persistPreference: true);
    }

    public bool ShouldRestoreMiningOverlayVisible => _preferredMiningOverlayVisible;

    public bool IsApplicationShuttingDown { get; private set; }

    public void BeginApplicationShutdown()
    {
        IsApplicationShuttingDown = true;
    }

    public void SetMiningOverlayVisibility(bool value, bool persistPreference)
    {
        if (!SetProperty(ref _isMiningOverlayVisible, value))
        {
            if (persistPreference && _preferredMiningOverlayVisible != value)
            {
                _preferredMiningOverlayVisible = value;
                _ = _settingsService.SetAsync(MiningOverlayVisibleKey, value);
            }
            return;
        }

        if (persistPreference)
        {
            _preferredMiningOverlayVisible = value;
            _ = _settingsService.SetAsync(MiningOverlayVisibleKey, value);
        }

        if (value)
        {
            _miningOverlayRefreshTimer.Start();
            _ = RefreshMiningOverlayAsync();
        }
        else
        {
            _miningOverlayRefreshTimer.Stop();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MiningOverlayToggleLabel)));
    }

    public string MiningRefineYieldPercentText
    {
        get => _miningRefineYieldPercentText;
        set => SetProperty(ref _miningRefineYieldPercentText, value);
    }

    public bool HasMiningStats => _miningStatsCards.Count > 0;

    public bool HasNoMiningStats => _miningStatsCards.Count == 0;

    public string MiningWindowStatus => !MiningEnabled
        ? "Mining tracking is disabled in Preferences. Enable it, then restart HISA."
        : IsMiningStatsLoading
            ? $"Loading {GetMiningRangeModeLabel(SelectedMiningRangeMode)} mining stats..."
            : SelectedMiningRangeMode == MiningStatsRangeMode.CurrentSession
                ? _miningStatsCards.Count == 0
                    ? "Watching Gamelogs for the newest session per active character."
                    : $"{_miningStatsCards.Count} active mining session(s)."
                : _miningStatsCards.Count == 0
                    ? $"No mining entries found for {GetMiningRangeModeLabel(SelectedMiningRangeMode).ToLowerInvariant()}."
                    : $"{_miningStatsCards.Count} character total(s) for {GetMiningRangeModeLabel(SelectedMiningRangeMode).ToLowerInvariant()}."
            ;

    public string MiningOverlayToggleLabel => IsMiningOverlayVisible ? "Hide Overlay" : "Show Overlay";

    public bool EnableLinkAnimations
    {
        get => _enableLinkAnimations;
        set
        {
            if (SetProperty(ref _enableLinkAnimations, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(EnableLinkAnimationsKey, value);
            }
        }
    }

    public bool EnableIntelReportAnimations
    {
        get => _enableIntelReportAnimations;
        set
        {
            if (SetProperty(ref _enableIntelReportAnimations, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(EnableIntelReportAnimationsKey, value);
            }
        }
    }

    public bool ShowAnsiblexNetwork
    {
        get => _showAnsiblexNetwork;
        set
        {
            if (SetProperty(ref _showAnsiblexNetwork, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowAnsiblexNetworkKey, value);
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAnsiblexLegendVisible)));
        }
    }

    public bool IsAnsiblexLegendVisible => ShowAnsiblexNetwork && SelectedViewMode != MapViewMode.UniverseRegions;

    public bool InfoBoxShowRegion
    {
        get => _infoBoxShowRegion;
        set
        {
            if (SetProperty(ref _infoBoxShowRegion, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowRegionKey, value);
            }
        }
    }

    public bool InfoBoxShowConstellation
    {
        get => _infoBoxShowConstellation;
        set
        {
            if (SetProperty(ref _infoBoxShowConstellation, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowConstellationKey, value);
            }
        }
    }

    public bool InfoBoxShowSecurityStatus
    {
        get => _infoBoxShowSecurityStatus;
        set
        {
            if (SetProperty(ref _infoBoxShowSecurityStatus, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowSecurityStatusKey, value);
            }
        }
    }

    public bool InfoBoxShowStarClass
    {
        get => _infoBoxShowStarClass;
        set
        {
            if (SetProperty(ref _infoBoxShowStarClass, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowStarClassKey, value);
            }
        }
    }

    public bool InfoBoxShowA0StarIcon
    {
        get => _infoBoxShowA0StarIcon;
        set
        {
            if (SetProperty(ref _infoBoxShowA0StarIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowA0StarIconKey, value);
            }
        }
    }

    public bool InfoBoxShowJoveObservatoryIcon
    {
        get => _infoBoxShowJoveObservatoryIcon;
        set
        {
            if (SetProperty(ref _infoBoxShowJoveObservatoryIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowJoveObservatoryIconKey, value);
            }
        }
    }

    public bool InfoBoxShowIceBeltsIcon
    {
        get => _infoBoxShowIceBeltsIcon;
        set
        {
            if (SetProperty(ref _infoBoxShowIceBeltsIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowIceBeltsIconKey, value);
            }
        }
    }

    public bool InfoBoxShowStormIcon
    {
        get => _infoBoxShowStormIcon;
        set
        {
            if (SetProperty(ref _infoBoxShowStormIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowStormIconKey, value);
            }
        }
    }

    public bool InfoBoxShowWormholeIcon
    {
        get => _infoBoxShowWormholeIcon;
        set
        {
            if (SetProperty(ref _infoBoxShowWormholeIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowWormholeIconKey, value);
            }
        }
    }

    public bool InfoBoxShowSovUpgradeIcon
    {
        get => _infoBoxShowSovUpgradeIcon;
        set
        {
            if (SetProperty(ref _infoBoxShowSovUpgradeIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowSovUpgradeIconKey, value);
            }
        }
    }

    public bool InfoBoxShowIncursionIcon
    {
        get => _infoBoxShowIncursionIcon;
        set
        {
            if (SetProperty(ref _infoBoxShowIncursionIcon, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowIncursionIconKey, value);
            }
        }
    }

    public bool InfoBoxShowJumpRangeLy
    {
        get => _infoBoxShowJumpRangeLy;
        set
        {
            if (SetProperty(ref _infoBoxShowJumpRangeLy, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowJumpRangeLyKey, value);
            }
        }
    }

    public bool AlwaysShowHubWormholes
    {
        get => _alwaysShowHubWormholes;
        set
        {
            if (SetProperty(ref _alwaysShowHubWormholes, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(AlwaysShowHubWormholesKey, value);
            }
        }
    }

    public HubWormholeMarkerMode HubWormholeMarkerMode
    {
        get => _hubWormholeMarkerMode;
        set
        {
            if (SetProperty(ref _hubWormholeMarkerMode, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(HubWormholeMarkerModeKey, value);
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWormholePreviewBadge)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWormholePreviewRing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWormholePreviewHalo)));
        }
    }

    public bool IsWormholePreviewBadge => HubWormholeMarkerMode == HubWormholeMarkerMode.Badge;
    public bool IsWormholePreviewRing => HubWormholeMarkerMode == HubWormholeMarkerMode.Ring;
    public bool IsWormholePreviewHalo => HubWormholeMarkerMode == HubWormholeMarkerMode.Halo;

    public bool ShowMissingConnectionMarkers
    {
        get => _showMissingConnectionMarkers;
        set
        {
            if (SetProperty(ref _showMissingConnectionMarkers, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(ShowMissingConnectionMarkersKey, value);
            }
        }
    }

    public bool AlwaysShowIncursions
    {
        get => _alwaysShowIncursions;
        set
        {
            if (SetProperty(ref _alwaysShowIncursions, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(AlwaysShowIncursionsKey, value);
            }
        }
    }

    public IEnumerable<string> SelectedIndicatorSovUpgradeKeys =>
        IndicatorSovUpgradeOptions.Where(x => x.IsSelected).Select(x => x.Key).ToList();

    public IEnumerable<string> SelectedOverlaySovUpgradeKeys =>
        OverlaySovUpgradeOptions.Where(x => x.IsSelected).Select(x => x.Key).ToList();

    public RegionOption? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (value?.IsHeader == true)
            {
                return;
            }

            if (SetProperty(ref _selectedRegion, value) && SelectedViewMode == MapViewMode.Region)
            {
                EnforceCoordinateModeForSelectedRegion();
                if (!_isInitializing && !_suppressSelectedRegionReload)
                {
                    _ = _settingsService.SetAsync(RegionIdKey, value?.RegionId);
                    _ = SaveSelectedRegionTokenAsync(value);
                    _ = ReloadGraphAsync();
                }
            }
        }
    }

    public MapCoordinateMode SelectedCoordinateMode
    {
        get => _selectedCoordinateMode;
        set
        {
            var persistByMode = true;
            if (SelectedViewMode == MapViewMode.Region && SelectedRegion is { Kind: not RegionOptionKind.Regular })
            {
                value = MapCoordinateMode.SdePlanarXY;
                persistByMode = false;
            }

            if (SelectedViewMode == MapViewMode.UniverseRegions && value != MapCoordinateMode.SdePlanarXY)
            {
                value = MapCoordinateMode.SdePlanarXY;
                persistByMode = false;
            }

            if (SetProperty(ref _selectedCoordinateMode, value))
            {
                if (!_isInitializing)
                {
                    if (persistByMode)
                    {
                        PersistCoordinateModeForCurrentView(value);
                    }
                    _ = ReloadGraphAsync();
                }
            }
        }
    }

    public string RegionSearchText
    {
        get => _regionSearchText;
        set
        {
            if (SetProperty(ref _regionSearchText, value))
            {
                ApplyRegionFilter();
            }
        }
    }

    public string MapSearchText
    {
        get => _mapSearchText;
        set
        {
            if (SetProperty(ref _mapSearchText, value))
            {
                _ = UpdateSearchSuggestionsAsync(value);
            }
        }
    }

    public MapSearchCandidate? SelectedSearchSuggestion
    {
        get => _selectedSearchSuggestion;
        set => SetProperty(ref _selectedSearchSuggestion, value);
    }

    public MapGraph? CurrentGraph
    {
        get => _currentGraph;
        private set => SetProperty(ref _currentGraph, value);
    }

    public long? SelectedNodeId
    {
        get => _selectedNodeId;
        set => SetProperty(ref _selectedNodeId, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasSearchSuggestions => SearchSuggestions.Count > 0;
    public bool HasJumpRangeOverlay => _jumpRangeOriginsLyByNodeId.Count > 0;
    public bool HasHubWormholeOverlayData => _hubWormholeCardsForView.Count > 0;
    public bool HasIncursionOverlayData => _incursionCardsForView.Count > 0;
    public bool HasStormOverlayData => _stormCardsForView.Count > 0;
    public bool HasIntelOverlayData => _intelCardsForView.Count > 0;
    public bool HasZkillmailOverlayData => _zkillmailCardsForView.Count > 0;
    public bool HasNoHubWormholeOverlayData => _hubWormholeCardsForView.Count == 0;
    public bool HasNoIncursionOverlayData => _incursionCardsForView.Count == 0;
    public bool HasNoStormOverlayData => _stormCardsForView.Count == 0;
    public bool HasNoIntelOverlayData => _intelCardsForView.Count == 0;
    public bool HasNoZkillmailOverlayData => _zkillmailCardsForView.Count == 0;
    public Task InitialLoadTask => _initialLoadTask;

    public bool IsHubWormholesOverlayOpen
    {
        get => _isHubWormholesOverlayOpen;
        set
        {
            if (!SetProperty(ref _isHubWormholesOverlayOpen, value) || !value)
            {
                return;
            }

            if (_isIncursionsOverlayOpen)
            {
                _isIncursionsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncursionsOverlayOpen)));
            }

            if (_isIntelOverlayOpen)
            {
                _isIntelOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIntelOverlayOpen)));
            }

            if (_isZkillmailsOverlayOpen)
            {
                _isZkillmailsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsZkillmailsOverlayOpen)));
            }

            if (_isStormsOverlayOpen)
            {
                _isStormsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStormsOverlayOpen)));
            }

            if (_isIntelOverlayOpen)
            {
                _isIntelOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIntelOverlayOpen)));
            }

            if (_isZkillmailsOverlayOpen)
            {
                _isZkillmailsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsZkillmailsOverlayOpen)));
            }
        }
    }

    public bool IsIncursionsOverlayOpen
    {
        get => _isIncursionsOverlayOpen;
        set
        {
            if (!SetProperty(ref _isIncursionsOverlayOpen, value) || !value)
            {
                return;
            }

            if (_isHubWormholesOverlayOpen)
            {
                _isHubWormholesOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHubWormholesOverlayOpen)));
            }

            if (_isStormsOverlayOpen)
            {
                _isStormsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStormsOverlayOpen)));
            }

            if (_isIntelOverlayOpen)
            {
                _isIntelOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIntelOverlayOpen)));
            }

            if (_isZkillmailsOverlayOpen)
            {
                _isZkillmailsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsZkillmailsOverlayOpen)));
            }
        }
    }

    public bool IsStormsOverlayOpen
    {
        get => _isStormsOverlayOpen;
        set
        {
            if (!SetProperty(ref _isStormsOverlayOpen, value) || !value)
            {
                return;
            }

            if (_isHubWormholesOverlayOpen)
            {
                _isHubWormholesOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHubWormholesOverlayOpen)));
            }

            if (_isIncursionsOverlayOpen)
            {
                _isIncursionsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncursionsOverlayOpen)));
            }

            if (_isIntelOverlayOpen)
            {
                _isIntelOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIntelOverlayOpen)));
            }

            if (_isZkillmailsOverlayOpen)
            {
                _isZkillmailsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsZkillmailsOverlayOpen)));
            }
        }
    }

    public bool TrySetJumpRangeOrigin(long nodeId, double lightYears)
    {
        if (lightYears <= 0 || CurrentGraph is null)
        {
            return false;
        }

        var node = CurrentGraph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || !HasSdePosition(node))
        {
            StatusText = "Jump range failed: selected system has no SDE xyz coordinates.";
            return false;
        }

        _jumpRangeOriginsLyByNodeId[nodeId] = new JumpRangeOriginSettings
        {
            SolarSystemId = node.Id,
            SolarSystemName = node.Name,
            PositionX = node.PositionX!.Value,
            PositionY = node.PositionY!.Value,
            PositionZ = node.PositionZ!.Value,
            RangeLy = lightYears
        };
        RebuildJumpRangeOverlay();
        _ = PersistJumpRangeOriginsAsync();
        return true;
    }

    public bool RemoveJumpRangeOrigin(long nodeId)
    {
        if (!_jumpRangeOriginsLyByNodeId.Remove(nodeId))
        {
            return false;
        }

        RebuildJumpRangeOverlay();
        _ = PersistJumpRangeOriginsAsync();
        return true;
    }

    public void ClearJumpRangeOrigins()
    {
        _jumpRangeOriginsLyByNodeId.Clear();
        _jumpRangeOriginColorByNodeId.Clear();
        ClearLyCoverageHighlights();
        RebuildJumpRangeOverlay();
        _ = PersistJumpRangeOriginsAsync();
    }

    private Task PersistJumpRangeOriginsAsync()
    {
        return PersistJumpRangeOriginsCoreAsync();
    }

    private async Task PersistJumpRangeOriginsCoreAsync()
    {
        await _jumpRangeOriginsPersistenceGate.WaitAsync();
        try
        {
            var origins = RememberJumpRangeOrigins
                ? _jumpRangeOriginsLyByNodeId.Values.OrderBy(x => x.SolarSystemId).ToList()
                : [];
            await _settingsService.SetAsync(JumpRangeOriginsKey, origins);
        }
        finally
        {
            _jumpRangeOriginsPersistenceGate.Release();
        }
    }

    public async Task<LyCoverageAnalysisResult> AnalyzeLyCoverageAsync(
        string inputSystems,
        double lyRange,
        bool inputOnlyCenters,
        int maxResults = 250,
        CancellationToken cancellationToken = default)
    {
        if (lyRange <= 0)
        {
            return new LyCoverageAnalysisResult
            {
                Candidates = [],
                InvalidTokens = [],
                TargetCount = 0,
                CandidateCountTested = 0
            };
        }

        var tokens = ParseSystemTokens(inputSystems);
        if (tokens.Count == 0)
        {
            return new LyCoverageAnalysisResult
            {
                Candidates = [],
                InvalidTokens = [],
                TargetCount = 0,
                CandidateCountTested = 0
            };
        }

        var systems = await _mapDataService.GetSystemsWithSdeCoordinatesAsync(cancellationToken);
        var byName = systems
            .GroupBy(s => s.SolarSystemName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targets = new List<MapSystemPosition>();
        var invalidTokens = new List<string>();
        var seenTargetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (!byName.TryGetValue(token, out var match))
            {
                invalidTokens.Add(token);
                continue;
            }

            if (seenTargetNames.Add(match.SolarSystemName))
            {
                targets.Add(match);
            }
        }

        if (targets.Count == 0)
        {
            return new LyCoverageAnalysisResult
            {
                Candidates = [],
                InvalidTokens = invalidTokens,
                TargetCount = 0,
                CandidateCountTested = 0
            };
        }

        var candidateCenters = inputOnlyCenters ? targets : systems;
        var rows = new List<LyCoverageCandidateRow>(candidateCenters.Count);
        foreach (var center in candidateCenters)
        {
            var coveredDistances = new List<double>(targets.Count);
            var coveredSystemIds = new List<long>(targets.Count);
            var uncovered = new List<string>();
            var uncoveredSystemIds = new List<long>();
            foreach (var target in targets)
            {
                var dist = center.SolarSystemId == target.SolarSystemId ? 0 : GetDistanceLy(center, target);
                if (dist <= lyRange)
                {
                    coveredDistances.Add(dist);
                    coveredSystemIds.Add(target.SolarSystemId);
                }
                else
                {
                    uncovered.Add(target.SolarSystemName);
                    uncoveredSystemIds.Add(target.SolarSystemId);
                }
            }

            if (coveredDistances.Count == 0)
            {
                continue;
            }

            var coveragePercent = (coveredDistances.Count * 100.0) / targets.Count;
            var avg = coveredDistances.Average();
            var max = coveredDistances.Max();
            rows.Add(new LyCoverageCandidateRow
            {
                CenterSystemId = center.SolarSystemId,
                CenterSystemName = center.SolarSystemName,
                RegionName = center.RegionName ?? "Unknown",
                CoveredCount = coveredDistances.Count,
                TargetCount = targets.Count,
                CoveragePercent = coveragePercent,
                AverageDistanceLy = avg,
                MaxDistanceLy = max,
                UncoveredSystems = uncovered.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                CoveredSystemIds = coveredSystemIds,
                UncoveredSystemIds = uncoveredSystemIds
            });
        }

        var ranked = rows
            .OrderByDescending(r => r.CoveredCount)
            .ThenBy(r => r.AverageDistanceLy)
            .ThenBy(r => r.MaxDistanceLy)
            .ThenBy(r => r.CenterSystemName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToList();

        return new LyCoverageAnalysisResult
        {
            Candidates = ranked,
            InvalidTokens = invalidTokens,
            TargetCount = targets.Count,
            CandidateCountTested = candidateCenters.Count
        };
    }

    public bool ApplyLyCoverageCenter(long centerSystemId, double lyRange, bool clearExisting = true)
    {
        if (clearExisting)
        {
            ClearJumpRangeOrigins();
        }

        return TrySetJumpRangeOrigin(centerSystemId, lyRange);
    }

    public bool ApplyLyCoverageCandidate(LyCoverageCandidateRow row, double lyRange, bool clearExisting = true)
    {
        if (!ApplyLyCoverageCenter(row.CenterSystemId, lyRange, clearExisting))
        {
            return false;
        }

        _lyCoverageCoveredNodeIdsForView = row.CoveredSystemIds.Distinct().ToList();
        _lyCoverageUncoveredNodeIdsForView = row.UncoveredSystemIds.Distinct().ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LyCoverageCoveredNodeIdsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LyCoverageUncoveredNodeIdsForView)));
        return true;
    }

    public void ClearLyCoverageHighlights()
    {
        if (_lyCoverageCoveredNodeIdsForView.Count == 0 && _lyCoverageUncoveredNodeIdsForView.Count == 0)
        {
            return;
        }

        _lyCoverageCoveredNodeIdsForView = [];
        _lyCoverageUncoveredNodeIdsForView = [];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LyCoverageCoveredNodeIdsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LyCoverageUncoveredNodeIdsForView)));
    }

    public async Task<JumpRouteAnalysisResult> AnalyzeJumpRoutesAsync(
        string inputSystems,
        bool followInputOrder,
        double maxJumpLy,
        string? startSystem,
        string? endSystem,
        bool returnToStart,
        int topResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (maxJumpLy <= 0)
        {
            return new JumpRouteAnalysisResult { Candidates = [], InvalidTokens = [], TargetCount = 0 };
        }

        var systems = await _mapDataService.GetSystemsWithSdeCoordinatesAsync(cancellationToken);
        var byName = systems
            .GroupBy(s => s.SolarSystemName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var tokens = ParseSystemTokens(inputSystems);
        var invalid = new List<string>();
        var targets = new List<MapSystemPosition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (!byName.TryGetValue(token, out var sys))
            {
                invalid.Add(token);
                continue;
            }
            if (seen.Add(sys.SolarSystemName))
            {
                targets.Add(sys);
            }
        }

        if (targets.Count == 0)
        {
            return new JumpRouteAnalysisResult { Candidates = [], InvalidTokens = invalid, TargetCount = 0 };
        }

        var priorities = new HashSet<long>();

        MapSystemPosition? fixedStart = null;
        if (!string.IsNullOrWhiteSpace(startSystem) && byName.TryGetValue(startSystem.Trim(), out var startMatch))
        {
            fixedStart = startMatch;
        }

        MapSystemPosition? fixedEnd = null;
        if (!string.IsNullOrWhiteSpace(endSystem) && byName.TryGetValue(endSystem.Trim(), out var endMatch))
        {
            fixedEnd = endMatch;
        }

        var candidates = new List<JumpRouteCandidateRow>();
        string? orderingMessage = null;
        var orderingFailed = false;

        if (followInputOrder)
        {
            if (TryBuildStrictInputOrderedRoute(targets, fixedStart, fixedEnd, maxJumpLy, returnToStart, out var orderedRoute, out var orderFailureReason))
            {
                var orderedSkipped = targets.Where(t => orderedRoute.All(r => r.SolarSystemId != t.SolarSystemId)).ToList();
                var orderedLegs = BuildRouteLegs(orderedRoute, maxJumpLy);
                candidates.Add(new JumpRouteCandidateRow
                {
                    RouteText = string.Join(" -> ", orderedRoute.Select(x => x.SolarSystemName)),
                    RouteSystemIds = orderedRoute.Select(x => x.SolarSystemId).ToList(),
                    RouteSystemNames = orderedRoute.Select(x => x.SolarSystemName).ToList(),
                    VisitedCount = orderedRoute.Select(x => x.SolarSystemId).Distinct().Count(id => targets.Any(t => t.SolarSystemId == id)),
                    TargetCount = targets.Count,
                    TotalDistanceLy = orderedLegs.Sum(l => l.DistanceLy),
                    MaxLegLy = orderedLegs.Count == 0 ? 0 : orderedLegs.Max(l => l.DistanceLy),
                    SkippedSystems = orderedSkipped.Select(x => x.SolarSystemName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    SkippedReasonLines = BuildSkippedReasonLines(orderedRoute, orderedSkipped, maxJumpLy),
                    SkippedSystemIds = orderedSkipped.Select(x => x.SolarSystemId).Distinct().ToList(),
                    Legs = orderedLegs
                });
                orderingMessage = "Input order followed.";
            }
            else
            {
                orderingMessage = $"Input order could not be followed exactly: {orderFailureReason}";
                orderingFailed = true;
            }
        }

        var seeds = fixedStart is not null
            ? new List<MapSystemPosition> { fixedStart }
            : targets.Take(Math.Min(12, targets.Count)).ToList();
        foreach (var seed in seeds)
        {
            var route = BuildGreedyRoute(seed, targets, maxJumpLy, priorities, fixedEnd, returnToStart);
            if (route.Route.Count == 0)
            {
                continue;
            }

            var repairedRoute = ExpandRouteWithFeasibleInsertions(route.Route, targets, maxJumpLy, priorities, fixedStart, fixedEnd, returnToStart);
            var improvedRoute = TwoOptImprove(repairedRoute, maxJumpLy);
            var skippedSystems = targets
                .Where(t => improvedRoute.All(r => r.SolarSystemId != t.SolarSystemId))
                .ToList();
            var skippedReasonLines = BuildSkippedReasonLines(improvedRoute, skippedSystems, maxJumpLy);
            var legs = BuildRouteLegs(improvedRoute, maxJumpLy);
            var totalLy = legs.Sum(l => l.DistanceLy);
            var maxLegLy = legs.Count == 0 ? 0 : legs.Max(l => l.DistanceLy);

            candidates.Add(new JumpRouteCandidateRow
            {
                RouteText = string.Join(" -> ", improvedRoute.Select(x => x.SolarSystemName)),
                RouteSystemIds = improvedRoute.Select(x => x.SolarSystemId).ToList(),
                RouteSystemNames = improvedRoute.Select(x => x.SolarSystemName).ToList(),
                VisitedCount = improvedRoute.Select(x => x.SolarSystemId).Distinct().Count(id => targets.Any(t => t.SolarSystemId == id)),
                TargetCount = targets.Count,
                TotalDistanceLy = totalLy,
                MaxLegLy = maxLegLy,
                SkippedSystems = skippedSystems.Select(x => x.SolarSystemName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                SkippedReasonLines = skippedReasonLines,
                SkippedSystemIds = skippedSystems.Select(x => x.SolarSystemId).Distinct().ToList(),
                Legs = legs
            });
        }

        var ranked = candidates
            .OrderByDescending(c => c.VisitedCount)
            .ThenBy(c => c.TotalDistanceLy)
            .ThenBy(c => c.MaxLegLy)
            .Take(Math.Max(1, topResults))
            .ToList();

        if (followInputOrder && orderingFailed)
        {
            orderingMessage = ranked.Count > 0
                ? $"{orderingMessage} Showing best alternate routes."
                : $"{orderingMessage} No alternate route satisfies current max jump constraints.";
        }

        return new JumpRouteAnalysisResult
        {
            Candidates = ranked,
            InvalidTokens = invalid,
            TargetCount = targets.Count,
            OrderingMessage = orderingMessage,
            OrderingFailed = orderingFailed
        };
    }

    public void ApplyJumpRouteCandidate(JumpRouteCandidateRow row)
    {
        _jumpRouteNodeIdsForView = row.RouteSystemIds.ToList();
        _jumpRouteSkippedNodeIdsForView = row.SkippedSystemIds.ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRouteNodeIdsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRouteSkippedNodeIdsForView)));
        if (row.RouteSystemIds.Count > 0)
        {
            SelectedNodeId = row.RouteSystemIds[0];
        }
    }

    public void ClearJumpRouteHighlights()
    {
        if (_jumpRouteNodeIdsForView.Count == 0 && _jumpRouteSkippedNodeIdsForView.Count == 0)
        {
            return;
        }
        _jumpRouteNodeIdsForView = [];
        _jumpRouteSkippedNodeIdsForView = [];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRouteNodeIdsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRouteSkippedNodeIdsForView)));
    }

    public async Task<WindowPlacementState?> GetWindowPlacementAsync()
    {
        return await _settingsService.GetAsync<WindowPlacementState>(WindowPlacementKey);
    }

    public Task SaveWindowPlacementAsync(WindowPlacementState placement)
    {
        return _settingsService.SetAsync(WindowPlacementKey, placement);
    }

    public async Task<MapViewportState?> GetViewportAsync(MapViewMode viewMode)
    {
        return await _settingsService.GetAsync<MapViewportState>($"{MapViewportPrefixKey}.{viewMode}");
    }

    public Task SaveViewportAsync(MapViewMode viewMode, MapViewportState viewport)
    {
        return _settingsService.SetAsync($"{MapViewportPrefixKey}.{viewMode}", viewport);
    }

    public Task<WindowPlacementState?> GetMiningStatsWindowPlacementAsync()
    {
        return _settingsService.GetAsync<WindowPlacementState>(MiningStatsWindowPlacementKey);
    }

    public Task SaveMiningStatsWindowPlacementAsync(WindowPlacementState placement)
    {
        return _settingsService.SetAsync(MiningStatsWindowPlacementKey, placement);
    }

    public Task<WindowPlacementState?> GetMiningOverlayWindowPlacementAsync()
    {
        return _settingsService.GetAsync<WindowPlacementState>(MiningOverlayWindowPlacementKey);
    }

    public Task SaveMiningOverlayWindowPlacementAsync(WindowPlacementState placement)
    {
        return _settingsService.SetAsync(MiningOverlayWindowPlacementKey, placement);
    }

    public Task SaveSelectedViewModeAsync()
    {
        return _settingsService.SetAsync(ViewModeKey, SelectedViewMode);
    }

    public async Task RestoreSelectedViewModeAsync()
    {
        var saved = await _settingsService.GetAsync<MapViewMode?>(ViewModeKey);
        if (saved is not null && SelectedViewMode != saved.Value)
        {
            SelectedViewMode = saved.Value;
        }
    }

    public async Task RefreshRegionOptionsAsync()
    {
        var selectedId = SelectedRegion?.RegionId;
        var selectedToken = CreateRegionToken(SelectedRegion);

        _allRegions = (await _mapDataService.GetRegionsAsync()).ToList();
        ApplyFavoriteFlags();
        ApplyRegionFilter();

        SelectedRegion = (selectedId is not null ? Regions.FirstOrDefault(r => !r.IsHeader && r.RegionId == selectedId.Value) : null)
            ?? FindRegionByToken(selectedToken)
            ?? GetFirstRegularRegionOption()
            ?? Regions.FirstOrDefault(r => !r.IsHeader);
    }

    private async Task LoadAsync()
    {
        _allRegions = (await _mapDataService.GetRegionsAsync()).ToList();
        await LoadFavoriteRegionTokensAsync();
        ApplyRegionFilter();
        await LoadKnownSpaceSystemCacheAsync();

        var legacyCoordinateMode = await _settingsService.GetAsync<MapCoordinateMode?>(CoordinateModeKey) ?? MapCoordinateMode.SdePlanarXY;
        _savedUniverseCoordinateMode = await _settingsService.GetAsync<MapCoordinateMode?>(CoordinateModeUniverseKey) ?? legacyCoordinateMode;
        _savedRegionCoordinateMode = await _settingsService.GetAsync<MapCoordinateMode?>(CoordinateModeRegionKey) ?? legacyCoordinateMode;
        SelectedCoordinateMode = _savedUniverseCoordinateMode;
        StretchMapToWindow = await _settingsService.GetAsync<bool?>(StretchMapToWindowKey) ?? false;
        AutoHideNavigation = await _settingsService.GetAsync<bool?>(AutoHideNavigationKey) ?? false;
        NodeColorMode = await _settingsService.GetAsync<MapNodeColorMode?>(NodeColorModeKey) ?? MapNodeColorMode.None;
        NodeBackgroundColorMode = await _settingsService.GetAsync<MapNodeColorMode?>(NodeBackgroundColorModeKey) ?? MapNodeColorMode.None;
        HostileColorSettings = SanitizeHostileColorSettings(
            await _settingsService.GetAsync<HostileColorSettings>(HostileColorSettingsKey) ?? new HostileColorSettings());
        ShowIndicatorRegion = await _settingsService.GetAsync<bool?>(ShowIndicatorRegionKey) ?? false;
        ShowIndicatorConstellation = await _settingsService.GetAsync<bool?>(ShowIndicatorConstellationKey) ?? false;
        ShowIndicatorSecurityStatus = await _settingsService.GetAsync<bool?>(ShowIndicatorSecurityStatusKey) ?? false;
        ShowIndicatorStarClass = await _settingsService.GetAsync<bool?>(ShowIndicatorStarClassKey) ?? false;
        ShowIndicatorA0StarIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorA0StarIconKey) ?? true;
        ShowIndicatorJoveObservatoryIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorJoveObservatoryIconKey) ?? true;
        ShowIndicatorIceBeltsIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorIceBeltsIconKey) ?? true;
        ShowIndicatorStormIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorStormIconKey) ?? true;
        ShowIndicatorWormholeIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorWormholeIconKey) ?? true;
        ShowIndicatorSovUpgradeIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorSovUpgradeIconKey) ?? true;
        ShowIndicatorIncursionIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorIncursionIconKey) ?? true;
        ShowIndicatorSystemJumps = await _settingsService.GetAsync<bool?>(ShowIndicatorSystemJumpsKey) ?? true;
        ShowIndicatorShipKills = await _settingsService.GetAsync<bool?>(ShowIndicatorShipKillsKey) ?? true;
        ShowIndicatorPodKills = await _settingsService.GetAsync<bool?>(ShowIndicatorPodKillsKey) ?? true;
        ShowIndicatorNpcKills = await _settingsService.GetAsync<bool?>(ShowIndicatorNpcKillsKey) ?? true;
        ShowIndicatorJumpRangeLy = await _settingsService.GetAsync<bool?>(ShowIndicatorJumpRangeLyKey) ?? true;
        RememberJumpRangeOrigins = await _settingsService.GetAsync<bool?>(RememberJumpRangeOriginsKey) ?? false;
        if (RememberJumpRangeOrigins)
        {
            var savedJumpRangeOrigins = await _settingsService.GetAsync<List<JumpRangeOriginSettings>>(JumpRangeOriginsKey) ?? [];
            _jumpRangeOriginsLyByNodeId.Clear();
            foreach (var origin in savedJumpRangeOrigins.Where(x =>
                         x.SolarSystemId > 0 &&
                         !string.IsNullOrWhiteSpace(x.SolarSystemName) &&
                         x.RangeLy > 0 &&
                         double.IsFinite(x.PositionX) &&
                         double.IsFinite(x.PositionY) &&
                         double.IsFinite(x.PositionZ)))
            {
                _jumpRangeOriginsLyByNodeId[origin.SolarSystemId] = origin;
            }
        }
        ShowIndicatorCharacterPresence = await _settingsService.GetAsync<bool?>(ShowIndicatorCharacterPresenceKey) ?? true;
        ShowInfoBoxCharacterPresence = await _settingsService.GetAsync<bool?>(ShowInfoBoxCharacterPresenceKey) ?? true;
        CharacterPresenceHoverMaxNames = await _settingsService.GetAsync<int?>(CharacterPresenceHoverMaxNamesKey) ?? 6;
        IntelEnabled = await _settingsService.GetAsync<bool?>(IntelEnabledKey) ?? true;
        MiningEnabled = await _settingsService.GetAsync<bool?>(MiningEnabledKey) ?? false;
        MiningRefineYieldPercentText = $"{await _settingsService.GetAsync<decimal?>(MiningRefineYieldPercentKey) ?? 90.63m:0.##}";
        _selectedMiningRangeMode = await _settingsService.GetAsync<MiningStatsRangeMode?>(MiningRangeModeKey) ?? MiningStatsRangeMode.CurrentSession;
        _selectedMiningOverlayRangeMode = await _settingsService.GetAsync<MiningOverlayRangeMode?>(MiningOverlayRangeModeKey) ?? MiningOverlayRangeMode.Rolling10Minutes;
        _preferredMiningOverlayVisible = await _settingsService.GetAsync<bool?>(MiningOverlayVisibleKey) ?? false;
        _isMiningOverlayVisible = false;
        IntelSystemExpiryMinutes = await _settingsService.GetAsync<int?>(IntelSystemExpiryMinutesKey) ?? 15;
        IntelListExpiryMinutes = await _settingsService.GetAsync<int?>(IntelListExpiryMinutesKey) ?? 30;
        _alertRules = await _settingsService.GetAsync<List<AlertRule>>(AlertRulesKey) ?? [];
        _alertPopupSettings = SanitizeAlertPopupSettings(
            await _settingsService.GetAsync<AlertPopupSettings>(AlertPopupSettingsKey) ?? new AlertPopupSettings());
        LimitHubWormholesToCurrentRegion = await _settingsService.GetAsync<bool?>(HubWormholeLimitToCurrentRegionKey) ?? false;
        LimitIncursionsToCurrentRegion = await _settingsService.GetAsync<bool?>(IncursionLimitToCurrentRegionKey) ?? false;
        LimitStormsToCurrentRegion = await _settingsService.GetAsync<bool?>(StormLimitToCurrentRegionKey) ?? false;
        LimitIntelReportsToCurrentRegion = await _settingsService.GetAsync<bool?>(IntelLimitToCurrentRegionKey) ?? false;
        LimitZkillmailsToCurrentRegion = await _settingsService.GetAsync<bool?>(ZkillLimitToCurrentRegionKey) ?? true;
        HideZkillmailsOutsideKnownSpace = await _settingsService.GetAsync<bool?>(ZkillHideOutsideKnownSpaceKey) ?? false;
        var initialIncludeChannels = await _settingsService.GetAsync<List<string>>(IntelIncludeChannelsKey) ?? [];
        IntelIncludeChannelsText = string.Join(Environment.NewLine, initialIncludeChannels);
        if (IntelEnabled && initialIncludeChannels.Count == 0)
        {
            StatusText = "Intel feed paused: configure included channels in Intel Settings.";
        }
        EnableLinkAnimations = await _settingsService.GetAsync<bool?>(EnableLinkAnimationsKey) ?? true;
        EnableIntelReportAnimations = await _settingsService.GetAsync<bool?>(EnableIntelReportAnimationsKey) ?? true;
        ShowAnsiblexNetwork = await _settingsService.GetAsync<bool?>(ShowAnsiblexNetworkKey) ?? true;
        LogsRootPath = (await _settingsService.GetAsync<string>(TrackingLogsRootPathKey)) ?? GetDefaultEveLogsRootPath();
        InfoBoxShowRegion = await _settingsService.GetAsync<bool?>(InfoBoxShowRegionKey) ?? true;
        InfoBoxShowConstellation = await _settingsService.GetAsync<bool?>(InfoBoxShowConstellationKey) ?? true;
        InfoBoxShowSecurityStatus = await _settingsService.GetAsync<bool?>(InfoBoxShowSecurityStatusKey) ?? true;
        InfoBoxShowStarClass = await _settingsService.GetAsync<bool?>(InfoBoxShowStarClassKey) ?? false;
        InfoBoxShowA0StarIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowA0StarIconKey) ?? true;
        InfoBoxShowJoveObservatoryIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowJoveObservatoryIconKey) ?? true;
        InfoBoxShowIceBeltsIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowIceBeltsIconKey) ?? true;
        InfoBoxShowStormIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowStormIconKey) ?? true;
        InfoBoxShowWormholeIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowWormholeIconKey) ?? true;
        InfoBoxShowSovUpgradeIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowSovUpgradeIconKey) ?? true;
        InfoBoxShowIncursionIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowIncursionIconKey) ?? true;
        InfoBoxShowSystemJumps = await _settingsService.GetAsync<bool?>(InfoBoxShowSystemJumpsKey) ?? true;
        InfoBoxShowShipKills = await _settingsService.GetAsync<bool?>(InfoBoxShowShipKillsKey) ?? true;
        InfoBoxShowPodKills = await _settingsService.GetAsync<bool?>(InfoBoxShowPodKillsKey) ?? true;
        InfoBoxShowNpcKills = await _settingsService.GetAsync<bool?>(InfoBoxShowNpcKillsKey) ?? true;
        InfoBoxShowJumpRangeLy = await _settingsService.GetAsync<bool?>(InfoBoxShowJumpRangeLyKey) ?? true;
        await _sovUpgradeStateService.InitializeAsync();
        await _miningSiteTrackerService.InitializeAsync();
        await _ansiblexNetworkStateService.InitializeAsync();
        InitializeSovFilterOptions();
        var indicatorKeys = await _settingsService.GetAsync<List<string>>(IndicatorSovFilterKeysKey) ?? [];
        var overlayKeys = await _settingsService.GetAsync<List<string>>(OverlaySovFilterKeysKey) ?? [];
        var indicatorConfigured = await _settingsService.GetAsync<bool?>(IndicatorSovFilterConfiguredKey) ?? false;
        var overlayConfigured = await _settingsService.GetAsync<bool?>(OverlaySovFilterConfiguredKey) ?? false;
        ApplySelectedSovKeys(IndicatorSovUpgradeOptions, indicatorKeys, indicatorConfigured);
        ApplySelectedSovKeys(OverlaySovUpgradeOptions, overlayKeys, overlayConfigured);
        AlwaysShowHubWormholes = await _settingsService.GetAsync<bool?>(AlwaysShowHubWormholesKey) ?? true;
        AlwaysShowIncursions = await _settingsService.GetAsync<bool?>(AlwaysShowIncursionsKey) ?? true;
        HubWormholeMarkerMode = await _settingsService.GetAsync<HubWormholeMarkerMode?>(HubWormholeMarkerModeKey) ?? HubWormholeMarkerMode.Badge;
        ShowMissingConnectionMarkers = await _settingsService.GetAsync<bool?>(ShowMissingConnectionMarkersKey) ?? true;
        ValidateLogsRootPath();
        SelectedViewMode = await _settingsService.GetAsync<MapViewMode?>(ViewModeKey) ?? MapViewMode.Universe;
        EnforceCoordinateModeForView();

        var savedRegionId = await _settingsService.GetAsync<int?>(RegionIdKey);
        var savedRegionToken = await _settingsService.GetAsync<SavedRegionToken>(RegionTokenKey);
        SelectedRegion = _allRegions.FirstOrDefault(r => r.RegionId == savedRegionId)
            ?? FindRegionByToken(savedRegionToken)
            ?? GetFirstRegularRegionOption()
            ?? Regions.FirstOrDefault(r => !r.IsHeader);

        _isInitializing = false;
        var savedTrackingPreferences = await _settingsService.GetAsync<List<CharacterTrackingPreference>>(TrackingCharacterPreferencesKey) ?? [];
        _followedCharacterId = await _settingsService.GetAsync<int?>(FollowCharacterIdKey);
        _characterTrackingPreferencesById.Clear();
        foreach (var pref in savedTrackingPreferences)
        {
            _characterTrackingPreferencesById[pref.CharacterId] = pref;
        }
        lock (_localCharacterLocationsByCharacterId)
        {
            _localCharacterLocationsByCharacterId.Clear();
            foreach (var kvp in _localCharacterLocationFeed.Snapshot)
            {
                _localCharacterLocationsByCharacterId[kvp.Key] = kvp.Value;
                EnsureCharacterTrackingPreference(kvp.Value);
            }
        }
        RebuildCharacterTrackingCards();
        lock (_intelSnapshotsBySystemId)
        {
            _intelSnapshotsBySystemId.Clear();
            var nowUtc = DateTime.UtcNow;
            foreach (var kvp in _intelFeed.Snapshot)
            {
                if (IsIntelSnapshotFresh(kvp.Value, nowUtc))
                {
                    _intelSnapshotsBySystemId[kvp.Key] = kvp.Value;
                }
            }
        }
        lock (_miningStatsByCharacterId)
        {
            _miningStatsByCharacterId.Clear();
            foreach (var kvp in _miningSessionFeed.Snapshot)
            {
                _miningStatsByCharacterId[kvp.Key] = kvp.Value;
            }
        }
        RebuildMiningStatsCards();
        _systemJumpGraph = await _mapDataService.GetSystemJumpGraphAsync();
        await ReloadGraphAsync();
        RebuildCharacterPresenceForView();
        RebuildIntelPresenceForView();
    }

    public bool InfoBoxShowSystemJumps
    {
        get => _infoBoxShowSystemJumps;
        set
        {
            if (SetProperty(ref _infoBoxShowSystemJumps, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowSystemJumpsKey, value);
            }
        }
    }

    public bool InfoBoxShowShipKills
    {
        get => _infoBoxShowShipKills;
        set
        {
            if (SetProperty(ref _infoBoxShowShipKills, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowShipKillsKey, value);
            }
        }
    }

    public bool InfoBoxShowPodKills
    {
        get => _infoBoxShowPodKills;
        set
        {
            if (SetProperty(ref _infoBoxShowPodKills, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowPodKillsKey, value);
            }
        }
    }

    public bool InfoBoxShowNpcKills
    {
        get => _infoBoxShowNpcKills;
        set
        {
            if (SetProperty(ref _infoBoxShowNpcKills, value) && !_isInitializing)
            {
                _ = _settingsService.SetAsync(InfoBoxShowNpcKillsKey, value);
            }
        }
    }

    public bool IsIntelOverlayOpen
    {
        get => _isIntelOverlayOpen;
        set
        {
            if (!SetProperty(ref _isIntelOverlayOpen, value) || !value)
            {
                return;
            }

            if (_isHubWormholesOverlayOpen)
            {
                _isHubWormholesOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHubWormholesOverlayOpen)));
            }

            if (_isIncursionsOverlayOpen)
            {
                _isIncursionsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncursionsOverlayOpen)));
            }

            if (_isStormsOverlayOpen)
            {
                _isStormsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStormsOverlayOpen)));
            }

            if (_isZkillmailsOverlayOpen)
            {
                _isZkillmailsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsZkillmailsOverlayOpen)));
            }
        }
    }

    public bool IsZkillmailsOverlayOpen
    {
        get => _isZkillmailsOverlayOpen;
        set
        {
            if (!SetProperty(ref _isZkillmailsOverlayOpen, value) || !value)
            {
                return;
            }

            if (_isHubWormholesOverlayOpen)
            {
                _isHubWormholesOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHubWormholesOverlayOpen)));
            }

            if (_isIncursionsOverlayOpen)
            {
                _isIncursionsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncursionsOverlayOpen)));
            }

            if (_isStormsOverlayOpen)
            {
                _isStormsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStormsOverlayOpen)));
            }

            if (_isIntelOverlayOpen)
            {
                _isIntelOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIntelOverlayOpen)));
            }
        }
    }

    private async Task ReloadGraphAsync(bool resetSelection = true)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        try
        {
            MapGraph graph = SelectedViewMode switch
            {
                MapViewMode.Universe => await _mapDataService.GetUniverseGraphAsync(SelectedCoordinateMode),
                MapViewMode.UniverseRegions => await _mapDataService.GetUniverseRegionsGraphAsync(SelectedCoordinateMode),
                MapViewMode.Region when SelectedRegion is not null => await _mapDataService.GetRegionGraphAsync(SelectedRegion.RegionId, SelectedCoordinateMode),
                MapViewMode.Region => new MapGraph { Nodes = [], Links = [] },
                _ => new MapGraph { Nodes = [], Links = [] }
            };

            CurrentGraph = graph;
            RebuildAnsiblexLinksForView(graph);
            await RefreshRegionMissingConnectionMarkersAsync(graph);
            RebuildJumpRangeOverlay();
            RebuildCharacterPresenceForView();
            RebuildIntelPresenceForView();
            await RebuildActivityCardsAsync(graph);
            if (resetSelection)
            {
                SelectedNodeId = null;
            }
            StatusText = $"Mode: {SelectedViewMode} | Coordinates: {SelectedCoordinateMode} | Nodes: {graph.Nodes.Count} | Links: {graph.Links.Count}";
            _ = _settingsService.SetAsync(ViewModeKey, SelectedViewMode);
            _ = _settingsService.SetAsync(RegionIdKey, SelectedRegion?.RegionId);
            _ = SaveSelectedRegionTokenAsync(SelectedRegion);
        }
        catch (Exception ex)
        {
            StatusText = $"Map load error: {ex.Message}";
            CurrentGraph = new MapGraph { Nodes = [], Links = [] };
            MissingConnectionNodeIdsForView = [];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MissingConnectionNodeIdsForView)));
            AnsiblexLinksForView = [];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnsiblexLinksForView)));
            RebuildJumpRangeOverlay();
            RebuildCharacterPresenceForView();
            RebuildIntelPresenceForView();
            await RebuildActivityCardsAsync(CurrentGraph);
            if (resetSelection)
            {
                SelectedNodeId = null;
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void OnStormSnapshotUpdated(object? sender, StormSnapshot snapshot)
    {
        var newStormKeys = ObserveSpawnKeys(AlertEventType.StormSpawn,
            snapshot.Centers.Select(center => $"storm:{center.Type}:{center.SolarSystemId}"));
        var newCenters = snapshot.Centers
            .Where(center => newStormKeys.Contains($"storm:{center.Type}:{center.SolarSystemId}"))
            .ToList();
        if (_isInitializing)
        {
            return;
        }

        var metadataById = await _mapDataService.GetSystemMetadataByIdsAsync(
            newCenters.Select(x => x.SolarSystemId).Distinct().ToArray());

        foreach (var center in newCenters)
        {
            metadataById.TryGetValue(center.SolarSystemId, out var metadata);
            var effects = snapshot.EffectsBySystemId
                .SelectMany(x => x.Value)
                .Where(x => x.CenterSolarSystemId == center.SolarSystemId)
                .ToList();
            EvaluateAlertRules(new AlertSourceEvent
            {
                EventType = AlertEventType.StormSpawn,
                TimestampUtc = (center.ReportedAtUtc ?? snapshot.FetchedAtUtc).UtcDateTime,
                SolarSystemId = center.SolarSystemId,
                RegionId = ResolveRegionId(center.SolarSystemId),
                DedupeKey = $"storm:{center.Type}:{center.SolarSystemId}",
                Summary = $"{center.DisplayName ?? center.Type.ToString()} storm in {metadata?.SolarSystemName ?? $"system {center.SolarSystemId}"}",
                SystemName = metadata?.SolarSystemName ?? center.DisplayName,
                ConstellationName = metadata?.ConstellationName,
                RegionName = metadata?.RegionName,
                StormType = center.Type,
                StormAffectedSystemCount = effects.Count,
                StormStrongSystemCount = effects.Count(x => x.Strength == StormStrength.Strong),
                StormWeakSystemCount = effects.Count(x => x.Strength == StormStrength.Weak)
            });
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync(resetSelection: false);
        });
    }

    private async void OnHubWormholeSnapshotUpdated(object? sender, HubWormholeSnapshot snapshot)
    {
        var connections = snapshot.ConnectionsBySystemId.Values.SelectMany(x => x).ToList();
        var newWormholeKeys = ObserveSpawnKeys(AlertEventType.HubWormholeSpawn,
            connections.Select(connection => $"hub-wormhole:{connection.HubType}:{connection.SolarSystemId}"));
        var newConnections = connections
            .Where(connection => newWormholeKeys.Contains($"hub-wormhole:{connection.HubType}:{connection.SolarSystemId}"))
            .ToList();
        if (_isInitializing)
        {
            return;
        }

        var metadataById = await _mapDataService.GetSystemMetadataByIdsAsync(
            newConnections.Select(x => x.SolarSystemId).Distinct().ToArray());

        foreach (var connection in newConnections)
        {
            metadataById.TryGetValue(connection.SolarSystemId, out var metadata);
            EvaluateAlertRules(new AlertSourceEvent
            {
                EventType = AlertEventType.HubWormholeSpawn,
                TimestampUtc = (connection.ReportedAtUtc ?? connection.LastUpdatedAtUtc ?? snapshot.FetchedAtUtc).UtcDateTime,
                SolarSystemId = connection.SolarSystemId,
                RegionId = ResolveRegionId(connection.SolarSystemId),
                DedupeKey = $"hub-wormhole:{connection.HubType}:{connection.SolarSystemId}",
                Summary = $"{connection.HubType} wormhole in {metadata?.SolarSystemName ?? $"system {connection.SolarSystemId}"}",
                SystemName = metadata?.SolarSystemName,
                ConstellationName = metadata?.ConstellationName,
                RegionName = metadata?.RegionName,
                HubWormholeConnection = connection
            });
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync(resetSelection: false);
        });
    }

    private void OnSovUpgradesSnapshotUpdated(object? sender, EventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync(resetSelection: false);
        });
    }

    public bool AutoHideNavigation
    {
        get => _autoHideNavigation;
        set
        {
            if (!SetProperty(ref _autoHideNavigation, value))
            {
                return;
            }

            SetNavigationCollapsed(value, value);
            if (!_isInitializing)
            {
                _ = _settingsService.SetAsync(AutoHideNavigationKey, value);
            }
        }
    }

    public bool IsTopNavigationVisible => !AutoHideNavigation || !_isTopNavigationCollapsed;
    public bool IsBottomNavigationVisible => !AutoHideNavigation || !_isBottomNavigationCollapsed;
    public bool IsTopNavigationHandleVisible => AutoHideNavigation && _isTopNavigationCollapsed;
    public bool IsBottomNavigationHandleVisible => AutoHideNavigation && _isBottomNavigationCollapsed;
    public bool IsTopNavigationCollapseButtonVisible => AutoHideNavigation && !_isTopNavigationCollapsed;
    public bool IsBottomNavigationCollapseButtonVisible => AutoHideNavigation && !_isBottomNavigationCollapsed;

    public void ToggleTopNavigation()
    {
        if (AutoHideNavigation)
        {
            SetNavigationCollapsed(!_isTopNavigationCollapsed, _isBottomNavigationCollapsed);
        }
    }

    public void ToggleBottomNavigation()
    {
        if (AutoHideNavigation)
        {
            SetNavigationCollapsed(_isTopNavigationCollapsed, !_isBottomNavigationCollapsed);
        }
    }

    private void SetNavigationCollapsed(bool topCollapsed, bool bottomCollapsed)
    {
        _isTopNavigationCollapsed = topCollapsed;
        _isBottomNavigationCollapsed = bottomCollapsed;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTopNavigationVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBottomNavigationVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTopNavigationHandleVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBottomNavigationHandleVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTopNavigationCollapseButtonVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBottomNavigationCollapseButtonVisible)));
    }

    private void OnMiningSiteReportsUpdated(object? sender, EventArgs e)
    {
        if (!_isInitializing)
        {
            Dispatcher.UIThread.Post(async () => await ReloadGraphAsync(resetSelection: false));
        }
    }

    private async Task CheckMiningSiteRemindersAsync()
    {
        var due = await _miningSiteTrackerService.GetDueReportsAsync(DateTime.UtcNow);
        foreach (var report in due)
        {
            var key = MiningSiteAlertKey(report);
            if (_pendingMiningSiteAlertKeys.Add(key)) _pendingMiningSiteAlerts.Enqueue(report);
        }

        if (_pendingMiningSiteAlerts.Count > 0 && !_miningSiteAlertDispatchTimer.IsEnabled)
        {
            await DispatchNextMiningSiteAlertAsync();
            if (_pendingMiningSiteAlerts.Count > 0) _miningSiteAlertDispatchTimer.Start();
        }
    }

    private async Task DispatchNextMiningSiteAlertAsync()
    {
        if (_pendingMiningSiteAlerts.Count == 0)
        {
            _miningSiteAlertDispatchTimer.Stop();
            return;
        }

        var report = _pendingMiningSiteAlerts.Dequeue();
        _pendingMiningSiteAlertKeys.Remove(MiningSiteAlertKey(report));
        var now = DateTime.UtcNow;
        if (!await _miningSiteTrackerService.MarkAlertEmittedAsync(report, now)) return;

        var node = CurrentGraph?.Nodes.FirstOrDefault(x => x.Id == report.SolarSystemId);
        var readyAt = report.AvailableAtUtc;
        var overdue = readyAt is not null && now - readyAt.Value > TimeSpan.FromMinutes(1);
        EvaluateAlertRules(new AlertSourceEvent
        {
            EventType = AlertEventType.MiningSiteReady,
            TimestampUtc = now,
            SolarSystemId = report.SolarSystemId,
            RegionId = node?.RegionId,
            DedupeKey = MiningSiteAlertKey(report),
            Summary = $"{node?.Name ?? report.SolarSystemId.ToString()}: {report.UpgradeName} T{report.Tier} is ready to check.",
            MiningSiteSystemName = node?.Name ?? report.SolarSystemId.ToString(),
            MiningSiteUpgradeName = report.UpgradeName,
            MiningSiteTier = report.Tier,
            MiningSiteReadyAtUtc = readyAt,
            MiningSiteWasOverdue = overdue
        });
    }

    private static string MiningSiteAlertKey(MiningSiteReport report) =>
        $"mining-site:{report.SolarSystemId}:{report.UpgradeName}:{report.Tier}:{report.AvailableAtUtc:O}";

    private async void OnIncursionSnapshotUpdated(object? sender, IncursionSnapshot snapshot)
    {
        var newIncursionKeys = ObserveSpawnKeys(AlertEventType.IncursionSpawn,
            snapshot.Incursions.Select(incursion => $"incursion:{incursion.ConstellationId}:{incursion.StagingSolarSystemId}:{incursion.Type}"));
        var newIncursions = snapshot.Incursions
            .Where(incursion => newIncursionKeys.Contains($"incursion:{incursion.ConstellationId}:{incursion.StagingSolarSystemId}:{incursion.Type}"))
            .ToList();
        if (_isInitializing)
        {
            return;
        }

        var metadataById = await _mapDataService.GetSystemMetadataByIdsAsync(
            newIncursions.Select(x => (long)x.StagingSolarSystemId).Distinct().ToArray());

        foreach (var incursion in newIncursions)
        {
            metadataById.TryGetValue(incursion.StagingSolarSystemId, out var metadata);
            EvaluateAlertRules(new AlertSourceEvent
            {
                EventType = AlertEventType.IncursionSpawn,
                TimestampUtc = snapshot.FetchedAtUtc.UtcDateTime,
                SolarSystemId = incursion.StagingSolarSystemId,
                RegionId = ResolveRegionId(incursion.StagingSolarSystemId),
                DedupeKey = $"incursion:{incursion.ConstellationId}:{incursion.StagingSolarSystemId}:{incursion.Type}",
                Summary = $"{incursion.Type} incursion ({incursion.State}) staging in {metadata?.SolarSystemName ?? $"system {incursion.StagingSolarSystemId}"}",
                SystemName = metadata?.SolarSystemName,
                ConstellationName = metadata?.ConstellationName,
                RegionName = metadata?.RegionName,
                Incursion = incursion
            });
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync(resetSelection: false);
        });
    }

    private void OnSystemActivitySnapshotUpdated(object? sender, SystemActivitySnapshot snapshot)
    {
        if (_isInitializing)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync(resetSelection: false);
        });
    }

    private IReadOnlySet<string> ObserveSpawnKeys(AlertEventType eventType, IEnumerable<string> keys)
    {
        lock (_observedSpawnAlertGate)
        {
            var currentKeys = keys.ToHashSet(StringComparer.Ordinal);
            if (!_observedSpawnAlertKeysByEventType.TryGetValue(eventType, out var previousKeys))
            {
                previousKeys = new HashSet<string>(StringComparer.Ordinal);
                _observedSpawnAlertKeysByEventType[eventType] = previousKeys;
            }

            var newKeys = currentKeys.Where(key => !previousKeys.Contains(key)).ToHashSet(StringComparer.Ordinal);
            previousKeys.Clear();
            previousKeys.UnionWith(currentKeys);
            return newKeys;
        }
    }

    private void OnAnsiblexNetworkSnapshotUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RebuildAnsiblexLinksForView(CurrentGraph);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnsiblexLinksForView)));
        });
    }

    private void OnLocalCharacterSystemChanged(object? sender, LocalCharacterSystemChange change)
    {
        EnsureCharacterTrackingPreference(change);
        if (!IsCharacterTrackingEnabled(change.CharacterId))
        {
            return;
        }

        lock (_localCharacterLocationsByCharacterId)
        {
            _localCharacterLocationsByCharacterId[change.CharacterId] = change;
        }

        Dispatcher.UIThread.Post(() =>
        {
            RebuildCharacterTrackingCards();
            RebuildCharacterPresenceForView();
            CharacterSystemChanged?.Invoke(this, change);
        });
    }

    private void OnIntelReportReceived(object? sender, IntelChatReport report)
    {
        var listMaxAgeUtc = DateTime.UtcNow - TimeSpan.FromMinutes(IntelListExpiryMinutes);
        if (report.TimestampUtc < listMaxAgeUtc)
        {
            return;
        }

        var isZkillReport = string.Equals(report.ChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase)
                            || report.SourceFilePath.StartsWith("api://zkillboard", StringComparison.OrdinalIgnoreCase);
        if (isZkillReport && !ShouldShowZkillmailReport(report))
        {
            return;
        }

        lock (_intelReportHistory)
        {
            _intelReportHistory.RemoveAll(x => x.TimestampUtc < listMaxAgeUtc);
            if (_intelReportHistory.Any(x => string.Equals(x.DedupeKey, report.DedupeKey, StringComparison.Ordinal)))
            {
                return;
            }

            _intelReportHistory.Add(report);
            if (_intelReportHistory.Count > MaxIntelReportHistory)
            {
                _intelReportHistory.RemoveRange(0, _intelReportHistory.Count - MaxIntelReportHistory);
            }
        }

        if (isZkillReport)
        {
            var includeInCurrentView = ShouldIncludeZkillmailReportInCurrentView(report);
            Dispatcher.UIThread.Post(() => AppendZkillmailCardForView(report));
            if (includeInCurrentView)
            {
                EvaluateAlertRules(report, isKillmail: true);
            }
            return;
        }

        EvaluateAlertRules(report, isKillmail: false);
        Dispatcher.UIThread.Post(() => AppendIntelCardForView(report));
    }

    private void EvaluateAlertRules(IntelChatReport report, bool isKillmail)
    {
        if (_alertRules.Count == 0)
        {
            return;
        }

        long solarSystemId = 0;
        var systemName = report.Systems.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(systemName))
        {
            if (CurrentGraph is { Nodes.Count: > 0 } graph)
            {
                var node = graph.Nodes.FirstOrDefault(n => string.Equals(n.Name, systemName, StringComparison.OrdinalIgnoreCase));
                if (node is not null)
                {
                    solarSystemId = node.Id;
                }
            }

            if (solarSystemId <= 0)
            {
                lock (_intelSnapshotsBySystemId)
                {
                    var snap = _intelSnapshotsBySystemId.Values.FirstOrDefault(x =>
                        string.Equals(x.SolarSystemName, systemName, StringComparison.OrdinalIgnoreCase));
                    if (snap is not null)
                    {
                        solarSystemId = snap.SolarSystemId;
                    }
                }
            }
        }

        if (solarSystemId <= 0)
        {
            return;
        }

        var sourceEvent = new AlertSourceEvent
        {
            EventType = isKillmail ? AlertEventType.Killmail : AlertEventType.IntelReport,
            TimestampUtc = report.TimestampUtc,
            SolarSystemId = solarSystemId,
            RegionId = ResolveRegionId(solarSystemId),
            KillmailId = report.Killmail?.KillmailId,
            IsClearIntelReport = !isKillmail && report.IsClear,
            DedupeKey = report.DedupeKey,
            Summary = report.MessageText
        };

        EvaluateAlertRules(sourceEvent);
    }

    private void EvaluateAlertRules(AlertSourceEvent sourceEvent)
    {
        if (_alertRules.Count == 0 || sourceEvent.SolarSystemId <= 0)
        {
            return;
        }

        var characterLocations = new Dictionary<int, long>();
        lock (_localCharacterLocationsByCharacterId)
        {
            foreach (var kvp in _localCharacterLocationsByCharacterId)
            {
                var sourceSystemId = ResolveSystemIdByName(kvp.Value.SolarSystemName);
                if (sourceSystemId > 0)
                {
                    characterLocations[kvp.Key] = sourceSystemId;
                }
            }
        }

        var triggered = _alertRuleEngine.Evaluate(new AlertEvaluationRequest
        {
            Rules = _alertRules,
            SourceEvent = sourceEvent,
            Graph = CurrentGraph,
            RoutingGraph = _systemJumpGraph,
            CharacterLocationsByCharacterId = characterLocations,
            AnsiblexLinks = _ansiblexNetworkStateService.CurrentLinks
        });
        if (triggered.Count == 0)
        {
            return;
        }

        var labels = string.Join(", ", triggered.Select(x => x.RuleName).Distinct(StringComparer.OrdinalIgnoreCase));
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = $"Alert triggered ({triggered.Count}): {labels}";
            foreach (var item in triggered)
            {
                AlertTriggered?.Invoke(this, item);
            }
        });
    }

    public long ResolveSystemIdByName(string? solarSystemName)
    {
        if (string.IsNullOrWhiteSpace(solarSystemName))
        {
            return 0;
        }

        if (CurrentGraph is { Nodes.Count: > 0 } graph)
        {
            var node = graph.Nodes.FirstOrDefault(n => string.Equals(n.Name, solarSystemName, StringComparison.OrdinalIgnoreCase));
            if (node is not null)
            {
                return node.Id;
            }
        }

        lock (_intelSnapshotsBySystemId)
        {
            var snapshot = _intelSnapshotsBySystemId.Values.FirstOrDefault(x =>
                string.Equals(x.SolarSystemName, solarSystemName, StringComparison.OrdinalIgnoreCase));
            if (snapshot is not null)
            {
                return snapshot.SolarSystemId;
            }
        }

        return 0;
    }

    public async Task<long> ResolveSystemIdByNameAsync(string? solarSystemName, CancellationToken cancellationToken = default)
    {
        var systemId = ResolveSystemIdByName(solarSystemName);
        if (systemId > 0 || string.IsNullOrWhiteSpace(solarSystemName))
        {
            return systemId;
        }

        var candidates = await _mapDataService.SearchAsync(solarSystemName.Trim(), cancellationToken);
        return candidates.FirstOrDefault(candidate =>
                   candidate.Kind == MapSearchKind.SolarSystem &&
                   candidate.SolarSystemId is > 0 &&
                   string.Equals(candidate.Name, solarSystemName.Trim(), StringComparison.OrdinalIgnoreCase))
               ?.SolarSystemId
               ?? 0;
    }

    private int? ResolveRegionId(long solarSystemId) => CurrentGraph?.Nodes
        .FirstOrDefault(node => node.Id == solarSystemId)?.RegionId;

    private bool ShouldIncludeZkillmailReportInCurrentView(IntelChatReport report)
    {
        if (!ShouldShowZkillmailReport(report))
        {
            return false;
        }

        if (!LimitZkillmailsToCurrentRegion)
        {
            return true;
        }

        var visibleNodeIds = CurrentGraph?.Nodes.Select(n => n.Id).ToHashSet() ?? new HashSet<long>();
        if (visibleNodeIds.Count == 0)
        {
            return false;
        }

        var systemName = report.Systems.FirstOrDefault();
        var solarSystemId = ResolveSystemIdByName(systemName);
        return solarSystemId > 0 && visibleNodeIds.Contains(solarSystemId);
    }

    private static HostileColorSettings SanitizeHostileColorSettings(HostileColorSettings settings)
    {
        var low = Math.Clamp(settings.LowMaxHostiles, 1, 99);
        var medium = Math.Clamp(settings.MediumMaxHostiles, low + 1, 100);
        var high = Math.Clamp(settings.HighMaxHostiles, medium + 1, 250);
        return new HostileColorSettings
        {
            LowMaxHostiles = low,
            MediumMaxHostiles = medium,
            HighMaxHostiles = high,
            LowColorHex = IsHexColor(settings.LowColorHex) ? settings.LowColorHex.Trim() : "#E6D86C",
            MediumColorHex = IsHexColor(settings.MediumColorHex) ? settings.MediumColorHex.Trim() : "#EE8639",
            HighColorHex = IsHexColor(settings.HighColorHex) ? settings.HighColorHex.Trim() : "#D90F13",
            AboveHighColorHex = IsHexColor(settings.AboveHighColorHex) ? settings.AboveHighColorHex.Trim() : "#DD008C"
        };
    }

    private static bool IsHexColor(string? value)
    {
        var color = value?.Trim();
        return color is { Length: 7 or 9 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
    }

    private static AlertRule CloneAlertRule(AlertRule rule)
    {
        return new AlertRule
        {
            Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id,
            Name = rule.Name.Trim(),
            Enabled = rule.Enabled,
            EventType = rule.EventType,
            ScopeMode = rule.ScopeMode,
            CharacterIds = (rule.CharacterIds ?? []).Distinct().ToList(),
            CharacterNames = (rule.CharacterNames ?? [])
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RegionIds = (rule.RegionIds ?? []).Where(id => id > 0).Distinct().ToList(),
            DistanceMode = rule.DistanceMode,
            MaxJumps = Math.Max(0, rule.MaxJumps),
            IncludeAnsiblexLinks = rule.IncludeAnsiblexLinks,
            ShowClearIntelReports = rule.ShowClearIntelReports,
            TextPattern = rule.TextPattern?.Trim() ?? string.Empty,
            UseRegex = rule.UseRegex,
            CooldownSeconds = Math.Max(0, rule.CooldownSeconds),
            SoundFile = string.IsNullOrWhiteSpace(rule.SoundFile) ? "default-alert.wav" : rule.SoundFile.Trim(),
            SoundVolume = Math.Clamp(rule.SoundVolume, 0.0, 1.0),
            Actions = (rule.Actions ?? []).Distinct().ToList()
        };
    }

    private static AlertPopupSettings SanitizeAlertPopupSettings(AlertPopupSettings settings)
    {
        return new AlertPopupSettings
        {
            Enabled = settings.Enabled,
            MaxCards = Math.Clamp(settings.MaxCards, 1, 30),
            AutoDismissSeconds = Math.Clamp(settings.AutoDismissSeconds, 3, 300),
            Opacity = Math.Clamp(settings.Opacity, 0.2, 1.0),
            Width = Math.Clamp(settings.Width, 180, 1200),
            Height = Math.Clamp(settings.Height, 120, 1200),
            Anchor = settings.Anchor,
            OffsetX = Math.Clamp(settings.OffsetX, -10000, 10000),
            OffsetY = Math.Clamp(settings.OffsetY, -10000, 10000)
        };
    }

    private void AppendZkillmailCardForView(IntelChatReport report)
    {
        var graphNodeByName = BuildGraphNodeByNameLookup(CurrentGraph);
        var visibleNodeIds = CurrentGraph?.Nodes.Select(n => n.Id).ToHashSet() ?? new HashSet<long>();
        Dictionary<string, IntelSystemSnapshot> snapshotByName;
        lock (_intelSnapshotsBySystemId)
        {
            snapshotByName = _intelSnapshotsBySystemId.Values
                .GroupBy(s => s.SolarSystemName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastUpdatedUtc).First(), StringComparer.OrdinalIgnoreCase);
        }
        var card = BuildZkillmailOverlayCard(
            report,
            graphNodeByName,
            visibleNodeIds,
            snapshotByName,
            limitToVisibleRegion: LimitZkillmailsToCurrentRegion,
            applyCachedIdentityData: true);
        if (card is null)
        {
            return;
        }

        var existing = _zkillmailCardsForView.ToList();
        if (report.Killmail?.KillmailId is > 0 and var killmailId &&
            existing.Any(x =>
                x.KillmailUrl.EndsWith($"/{killmailId}/", StringComparison.OrdinalIgnoreCase) ||
                x.MessageText.Contains(killmailId.ToString(), StringComparison.Ordinal)))
        {
            return;
        }

        if (existing.Count == 0)
        {
            _zkillmailCardsForView = [card];
        }
        else
        {
            var insertAt = FindInsertIndexByTimestampDesc(existing, card.TimestampUtc);
            existing.Insert(insertAt, card);
            if (existing.Count > MaxIntelOverlayCards)
            {
                existing.RemoveRange(MaxIntelOverlayCards, existing.Count - MaxIntelOverlayCards);
            }

            _zkillmailCardsForView = existing;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasZkillmailOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoZkillmailOverlayData)));

        _ = EnsureShipImagesForIntelCardsAsync();
        _ = EnsureZkillIdentityAssetsAsync();
    }

    private void AppendIntelCardForView(IntelChatReport report)
    {
        var graphNodeByName = BuildGraphNodeByNameLookup(CurrentGraph);
        var visibleNodeIds = CurrentGraph?.Nodes.Select(n => n.Id).ToHashSet() ?? new HashSet<long>();
        Dictionary<string, IntelSystemSnapshot> snapshotByName;
        lock (_intelSnapshotsBySystemId)
        {
            snapshotByName = _intelSnapshotsBySystemId.Values
                .GroupBy(s => s.SolarSystemName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastUpdatedUtc).First(), StringComparer.OrdinalIgnoreCase);
        }

        var card = BuildIntelOverlayCard(
            report,
            graphNodeByName,
            visibleNodeIds,
            snapshotByName,
            limitToVisibleRegion: LimitIntelReportsToCurrentRegion,
            applyCachedIdentityData: true);
        if (card is null)
        {
            return;
        }

        var existing = _intelCardsForView.ToList();
        if (existing.Any(x =>
                x.SortTimestampUtc == card.SortTimestampUtc &&
                string.Equals(x.SystemName, card.SystemName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ReporterName, card.ReporterName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.MessageText, card.MessageText, StringComparison.Ordinal)))
        {
            return;
        }

        if (existing.Count == 0)
        {
            _intelCardsForView = [card];
        }
        else
        {
            var insertAt = FindInsertIndexByTimestampDesc(existing, card.SortTimestampUtc);
            existing.Insert(insertAt, card);
            if (existing.Count > MaxIntelOverlayCards)
            {
                existing.RemoveRange(MaxIntelOverlayCards, existing.Count - MaxIntelOverlayCards);
            }

            _intelCardsForView = existing;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIntelOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIntelOverlayData)));

        _ = ResolveIntelCharacterIdsAsync();
        _ = EnsureShipImagesForIntelCardsAsync();
        _ = EnsureIntelIdentityAssetsAsync();
    }

    private static int FindInsertIndexByTimestampDesc(IReadOnlyList<ZkillmailOverlayCard> cards, DateTime timestampUtc)
    {
        var lo = 0;
        var hi = cards.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (cards[mid].TimestampUtc >= timestampUtc)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static int FindInsertIndexByTimestampDesc(IReadOnlyList<IntelOverlayCard> cards, DateTime timestampUtc)
    {
        var lo = 0;
        var hi = cards.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (cards[mid].SortTimestampUtc >= timestampUtc)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void OnIntelSnapshotUpdated(object? sender, IReadOnlyDictionary<long, IntelSystemSnapshot> snapshot)
    {
        lock (_intelSnapshotsBySystemId)
        {
            _intelSnapshotsBySystemId.Clear();
            var nowUtc = DateTime.UtcNow;
            foreach (var kvp in snapshot)
            {
                if (IsIntelSnapshotFresh(kvp.Value, nowUtc))
                {
                    _intelSnapshotsBySystemId[kvp.Key] = kvp.Value;
                }
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            RebuildIntelPresenceForView();
        });
    }

    private void ScheduleActivityCardsRebuild()
    {
        Interlocked.Increment(ref _activityCardsRebuildVersion);
        _activityCardsRebuildDebounceTimer.Stop();
        _activityCardsRebuildDebounceTimer.Start();
    }

    private void RequestOverlayCardsUiRefresh(bool intelCardsChanged, bool zkillCardsChanged)
    {
        if (intelCardsChanged)
        {
            _pendingIntelCardsUiRefresh = true;
        }

        if (zkillCardsChanged)
        {
            _pendingZkillCardsUiRefresh = true;
        }

        _overlayCardUiRefreshDebounceTimer.Stop();
        _overlayCardUiRefreshDebounceTimer.Start();
    }

    private async Task RunScheduledActivityCardsRebuildAsync()
    {
        if (_activityCardsRebuildInFlight)
        {
            return;
        }

        _activityCardsRebuildInFlight = true;
        try
        {
            while (true)
            {
                var requestedVersion = Volatile.Read(ref _activityCardsRebuildVersion);
                if (requestedVersion == _activityCardsRebuildRunningVersion)
                {
                    break;
                }

                _activityCardsRebuildRunningVersion = requestedVersion;
                await RebuildActivityCardsAsync(CurrentGraph);
            }
        }
        finally
        {
            _activityCardsRebuildInFlight = false;
        }
    }

    private void RebuildIntelPresenceForView()
    {
        var graph = CurrentGraph;
        if (graph is null || graph.Nodes.Count == 0 || SelectedViewMode == MapViewMode.UniverseRegions)
        {
            _intelIconKeysByNodeId = new Dictionary<long, IReadOnlyList<string>>();
            _intelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>();
            _zkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>>();
            _intelHostileScoresByNodeId = new Dictionary<long, int>();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelIconKeysByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelRecentReportsByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillRecentReportsByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelHostileScoresByNodeIdForView)));
            return;
        }

        Dictionary<long, IntelSystemSnapshot> snapshot;
        List<IntelChatReport> history;
        lock (_intelSnapshotsBySystemId)
        {
            snapshot = _intelSnapshotsBySystemId
                .Where(kvp => ShouldKeepIntelSnapshotForCurrentSettings(kvp.Value))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        lock (_intelReportHistory)
        {
            var listCutoffUtc = DateTime.UtcNow - TimeSpan.FromMinutes(IntelListExpiryMinutes);
            history = _intelReportHistory
                .Where(ShouldKeepIntelReportForCurrentSettings)
                .Where(report => report.TimestampUtc >= listCutoffUtc)
                .ToList();
        }

        var validNodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();
        var iconsByNode = new Dictionary<long, IReadOnlyList<string>>();
        var recentReportsByNode = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>();
        var hostileScoresByNode = new Dictionary<long, int>();
        var snapshotCutoffUtc = DateTime.UtcNow - TimeSpan.FromMinutes(IntelSystemExpiryMinutes);
        var latestSnapshotSystemByCharacterId = BuildLatestIntelSystemByCharacterId(
            snapshot.Values.Select(system => (
                system.SolarSystemId,
                system.LastUpdatedUtc,
                system.HostilePilotNames)));
        foreach (var system in snapshot.Values)
        {
            if (system.LastUpdatedUtc < snapshotCutoffUtc ||
                !validNodeIds.Contains(system.SolarSystemId))
            {
                continue;
            }

            if (system.IsClear)
            {
                // "clear/clr" means no active hostile intel for map overlays.
                continue;
            }

            var movedResolvedHostileCount = CountMovedResolvedHostiles(
                system.SolarSystemId,
                system.HostilePilotNames,
                latestSnapshotSystemByCharacterId);
            var activeHostileScore = Math.Max(0, system.HostileScore - movedResolvedHostileCount);
            if (movedResolvedHostileCount > 0 && activeHostileScore == 0)
            {
                continue;
            }

            var iconKeys = BuildIntelRingIconKeys(system, activeHostileScore).ToList();

            foreach (var alert in system.Alerts.Distinct())
            {
                if (alert == IntelAlertType.Clear)
                {
                    continue;
                }
                // Placeholder for missing alert icons (per current asset set).
                iconKeys.Add("crosshair");
            }

            if (system.IsClear && iconKeys.Count == 0)
            {
                iconKeys.Add("question-mark");
            }

            if (iconKeys.Count > 0)
            {
                iconsByNode[system.SolarSystemId] = iconKeys;
            }

            hostileScoresByNode[system.SolarSystemId] = activeHostileScore;
        }

        var nodeByName = graph.Nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var intelHistory = history
            .Where(r => !(string.Equals(r.ChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase)
                          || r.SourceFilePath.StartsWith("api://zkillboard", StringComparison.OrdinalIgnoreCase)))
            .Select(r => new { Report = r, System = r.Systems.FirstOrDefault() })
            .Where(x => !string.IsNullOrWhiteSpace(x.System) && nodeByName.ContainsKey(x.System!))
            .ToList();
        var latestHistorySystemByCharacterId = BuildLatestIntelSystemByCharacterId(
            intelHistory.Select(x => (
                nodeByName[x.System!].Id,
                x.Report.TimestampUtc,
                x.Report.ReportedHostileNames)));
        var intelByNode = intelHistory
            .GroupBy(x => nodeByName[x.System!].Id)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<IntelMapHoverReport>)g
                    .OrderByDescending(x => x.Report.TimestampUtc)
                    .Select(x =>
                    {
                        var hostileCards = FilterMovedResolvedHostiles(
                            g.Key,
                            BuildIntelHostileCards(x.Report.ReportedHostileNames, x.Report.ReportedShipNames, x.Report.ReportedShipTypeIds, x.Report.ShipClasses),
                            latestHistorySystemByCharacterId);
                        if (x.Report.ReportedHostileNames.Count > 0 && hostileCards.Count == 0)
                        {
                            return null;
                        }

                        var movedResolvedHostileCount = CountMovedResolvedHostiles(
                            g.Key,
                            x.Report.ReportedHostileNames,
                            latestHistorySystemByCharacterId);
                        var hoverShips = BuildIntelHoverShips(
                            x.Report.ReportedShipNames,
                            x.Report.ReportedShipTypeIds,
                            x.Report.ShipClasses);
                        return new IntelMapHoverReport
                        {
                            TimestampUtc = x.Report.TimestampUtc,
                            ReporterName = x.Report.ReporterName,
                            MessageText = x.Report.MessageText,
                            Ships = hoverShips.Take(MaxIntelHoverShipRows).ToList(),
                            HiddenShipCount = Math.Max(0, hoverShips.Count - MaxIntelHoverShipRows),
                            Hostiles = hostileCards
                            .Take(3)
                            .Select(h => new IntelMapHoverHostile
                            {
                                Name = h.Name,
                                CharacterId = h.CharacterId,
                                ShipTypeId = h.ShipTypeId,
                                ShipDisplayName = h.ShipDisplayName,
                                ShipIconKey = h.ShipIconKey,
                                CorporationId = h.CorporationId,
                                AllianceId = h.AllianceId,
                                CorporationTicker = h.CorporationTicker,
                                AllianceTicker = h.AllianceTicker
                            })
                            .ToList(),
                            HiddenHostileCount = Math.Max(0, hostileCards.Count - 3),
                            HostileCount = Math.Max(hostileCards.Count, x.Report.ReportedHostileCount - movedResolvedHostileCount)
                        };
                    })
                    .Where(x => x is not null)
                    .Take(1)
                    .Select(x => x!)
                    .ToList())
            .Where(x => x.Value.Count > 0)
            .ToDictionary(x => x.Key, x => x.Value);

        var zkillByNode = history
            .Where(r => string.Equals(r.ChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase)
                        || r.SourceFilePath.StartsWith("api://zkillboard", StringComparison.OrdinalIgnoreCase))
            .Where(ShouldShowZkillmailReport)
            .Select(r => new { Report = r, System = r.Systems.FirstOrDefault() })
            .Where(x => !string.IsNullOrWhiteSpace(x.System) && nodeByName.ContainsKey(x.System!))
            .GroupBy(x => nodeByName[x.System!].Id)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<IntelMapHoverKillmail>)g
                    .OrderByDescending(x => x.Report.TimestampUtc)
                    .Take(1)
                    .Select(x =>
                    {
                        var victim = BuildZkillVictimCard(x.Report.Killmail, x.Report.ReportedShipNames, x.Report.ReportedShipTypeIds, x.Report.ShipClasses);
                        var allAttackers = BuildZkillAttackerCards(x.Report.Killmail);
                        var attackers = allAttackers.Take(3).ToList();
                        return new IntelMapHoverKillmail
                        {
                            TimestampUtc = x.Report.TimestampUtc,
                            MessageText = x.Report.MessageText,
                            KillmailUrl = x.Report.Killmail?.Url ?? string.Empty,
                            VictimName = victim.Name,
                            VictimMembership = victim.MembershipTickerSummary,
                            VictimCharacterId = victim.CharacterId,
                            VictimCorporationId = victim.CorporationId,
                            VictimAllianceId = victim.AllianceId,
                            VictimShipDisplayName = victim.ShipDisplayName,
                            VictimShipTypeId = victim.ShipTypeId,
                            Attackers = attackers.Select(a => new IntelMapHoverHostile
                            {
                                Name = a.Name,
                                CharacterId = a.CharacterId,
                                ShipTypeId = a.ShipTypeId,
                                ShipDisplayName = a.ShipDisplayName,
                                ShipIconKey = a.ShipIconKey,
                                CorporationId = a.CorporationId,
                                AllianceId = a.AllianceId,
                                CorporationTicker = a.CorporationTicker,
                                AllianceTicker = a.AllianceTicker
                            }).ToList(),
                            HiddenAttackerCount = Math.Max(0, allAttackers.Count - 3),
                            IskLostLabel = $"ISK Lost: {FormatCompactIsk(x.Report.Killmail?.TotalValue ?? 0m)}"
                        };
                    })
                    .ToList());

        // Snapshot state and hover history are updated independently. Do not render a ring
        // when its supporting report has already expired or was not loaded during startup.
        var reportNodeIds = intelByNode.Keys
            .Concat(zkillByNode.Keys)
            .ToHashSet();
        iconsByNode = iconsByNode
            .Where(kvp => reportNodeIds.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        hostileScoresByNode = hostileScoresByNode
            .Where(kvp => reportNodeIds.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _intelIconKeysByNodeId = iconsByNode;
        _intelRecentReportsByNodeId = intelByNode;
        _zkillRecentReportsByNodeId = zkillByNode;
        _intelHostileScoresByNodeId = hostileScoresByNode;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelIconKeysByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelRecentReportsByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillRecentReportsByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelHostileScoresByNodeIdForView)));
    }

    private void PruneIntelStateForCurrentSettings()
    {
        lock (_intelSnapshotsBySystemId)
        {
            foreach (var key in _intelSnapshotsBySystemId
                         .Where(kvp => !ShouldKeepIntelSnapshotForCurrentSettings(kvp.Value))
                         .Select(kvp => kvp.Key)
                         .ToList())
            {
                _intelSnapshotsBySystemId.Remove(key);
            }
        }

        lock (_intelReportHistory)
        {
            _intelReportHistory.RemoveAll(report => !ShouldKeepIntelReportForCurrentSettings(report));
        }
    }

    private bool ShouldKeepIntelSnapshotForCurrentSettings(IntelSystemSnapshot snapshot)
    {
        if (!IntelEnabled)
        {
            return false;
        }

        if (string.Equals(snapshot.LastChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var include = GetConfiguredIntelIncludeChannels();
        return include.Count == 0 || include.Contains(snapshot.LastChannelName);
    }

    private bool ShouldKeepIntelReportForCurrentSettings(IntelChatReport report)
    {
        var isZkillReport = string.Equals(report.ChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase)
                            || report.SourceFilePath.StartsWith("api://zkillboard", StringComparison.OrdinalIgnoreCase);
        if (isZkillReport)
        {
            return IntelEnabled;
        }

        if (!IntelEnabled)
        {
            return false;
        }

        var include = GetConfiguredIntelIncludeChannels();
        return include.Count == 0 || include.Contains(report.ChannelName);
    }

    private HashSet<string> GetConfiguredIntelIncludeChannels()
    {
        return IntelIncludeChannelsText
            .Split(['\r', '\n', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool IsIntelSnapshotFresh(IntelSystemSnapshot snapshot, DateTime nowUtc)
    {
        return snapshot.LastUpdatedUtc >= nowUtc - TimeSpan.FromMinutes(IntelSystemExpiryMinutes);
    }

    private static string? ShipClassToIconKey(IntelShipClass shipClass)
    {
        return shipClass switch
        {
            IntelShipClass.Frigate => "frigate",
            IntelShipClass.Destroyer => "destroyer",
            IntelShipClass.Cruiser => "cruiser",
            IntelShipClass.Battlecruiser => "battlecruiser",
            IntelShipClass.Battleship => "battleship",
            IntelShipClass.Capital => "capital",
            IntelShipClass.Supercapital => "supercapital",
            IntelShipClass.Titan => "titan",
            IntelShipClass.Industrial => "industrial",
            IntelShipClass.IndustrialCommand => "industrialcommand",
            IntelShipClass.Freighter => "freighter",
            IntelShipClass.MiningFrigate => "miningfrigate",
            IntelShipClass.MiningBarge => "miningbarge",
            IntelShipClass.Capsule => "capsule",
            IntelShipClass.Shuttle => "shuttle",
            IntelShipClass.Rookie => "crosshair",
            _ => "crosshair"
        };
    }

    private static IReadOnlyList<string> BuildIntelRingIconKeys(IntelSystemSnapshot system, int? hostileOverride = null)
    {
        var classCounts = system.ShipClasses
            .GroupBy(x => x)
            .ToDictionary(g => g.Key, g => g.Count());
        var hostile = Math.Max(0, hostileOverride ?? system.HostileScore);
        if (hostile <= 0)
        {
            return [];
        }

        // Never render more ring icons than reported hostiles (still capped for readability/perf).
        var maxIcons = Math.Clamp(hostile, 1, 10);
        var icons = new List<string>(maxIcons);

        var priority = new[]
        {
            IntelShipClass.Titan,
            IntelShipClass.Supercapital,
            IntelShipClass.Capital,
            IntelShipClass.Battleship,
            IntelShipClass.Battlecruiser,
            IntelShipClass.Cruiser,
            IntelShipClass.Destroyer,
            IntelShipClass.Frigate,
            IntelShipClass.Freighter,
            IntelShipClass.IndustrialCommand,
            IntelShipClass.Industrial,
            IntelShipClass.MiningBarge,
            IntelShipClass.MiningFrigate,
            IntelShipClass.Shuttle,
            IntelShipClass.Capsule,
            IntelShipClass.Rookie
        };

        var presentClasses = priority
            .Where(classCounts.ContainsKey)
            .ToList();

        if (presentClasses.Count == 0)
        {
            if (hostile >= 8)
            {
                return Enumerable.Repeat("squadron", maxIcons).ToList();
            }

            return Enumerable.Repeat("crosshair", maxIcons).ToList();
        }

        if (hostile >= 8)
        {
            var classSlots = Math.Clamp(maxIcons / 3, 1, 4);
            var chosenClasses = BuildWeightedClassSequence(classCounts, presentClasses, classSlots);
            foreach (var shipClass in chosenClasses)
            {
                var icon = ShipClassToIconKey(shipClass);
                if (!string.IsNullOrWhiteSpace(icon))
                {
                    icons.Add(icon);
                }
            }

            while (icons.Count < maxIcons)
            {
                icons.Add("squadron");
            }

            return icons;
        }

        var weightedClasses = BuildWeightedClassSequence(classCounts, presentClasses, maxIcons);
        foreach (var shipClass in weightedClasses)
        {
            var icon = ShipClassToIconKey(shipClass);
            if (!string.IsNullOrWhiteSpace(icon))
            {
                icons.Add(icon);
            }
        }

        while (icons.Count < maxIcons && presentClasses.Count > 0)
        {
            var topClass = presentClasses[0];
            var icon = ShipClassToIconKey(topClass);
            if (!string.IsNullOrWhiteSpace(icon))
            {
                icons.Add(icon);
            }
        }

        return icons;
    }

    private Dictionary<int, long> BuildLatestIntelSystemByCharacterId(
        IEnumerable<(long SystemId, DateTime TimestampUtc, IReadOnlyList<string> HostileNames)> sightings)
    {
        var latestByCharacterId = new Dictionary<int, (DateTime TimestampUtc, long SystemId)>();
        foreach (var sighting in sightings)
        {
            foreach (var hostileName in sighting.HostileNames)
            {
                if (!TryGetResolvedIntelCharacterId(hostileName, out var characterId))
                {
                    continue;
                }

                if (!latestByCharacterId.TryGetValue(characterId, out var latest) ||
                    sighting.TimestampUtc >= latest.TimestampUtc)
                {
                    latestByCharacterId[characterId] = (sighting.TimestampUtc, sighting.SystemId);
                }
            }
        }

        return latestByCharacterId.ToDictionary(x => x.Key, x => x.Value.SystemId);
    }

    private int CountMovedResolvedHostiles(
        long systemId,
        IReadOnlyList<string> hostileNames,
        IReadOnlyDictionary<int, long> latestSystemByCharacterId)
    {
        return hostileNames
            .Select(name => TryGetResolvedIntelCharacterId(name, out var characterId) ? characterId : 0)
            .Where(characterId => characterId > 0)
            .Distinct()
            .Count(characterId =>
                latestSystemByCharacterId.TryGetValue(characterId, out var latestSystemId) &&
                latestSystemId != systemId);
    }

    private static List<IntelOverlayHostileCard> FilterMovedResolvedHostiles(
        long systemId,
        IReadOnlyList<IntelOverlayHostileCard> hostiles,
        IReadOnlyDictionary<int, long> latestSystemByCharacterId)
    {
        return hostiles
            .Where(hostile =>
                hostile.CharacterId is not { } characterId ||
                !latestSystemByCharacterId.TryGetValue(characterId, out var latestSystemId) ||
                latestSystemId == systemId)
            .ToList();
    }

    private bool TryGetResolvedIntelCharacterId(string hostileName, out int characterId)
    {
        return _characterIdByName.TryGetValue(hostileName.Trim(), out characterId) && characterId > 0;
    }

    private static IReadOnlyList<IntelShipClass> BuildWeightedClassSequence(
        IReadOnlyDictionary<IntelShipClass, int> classCounts,
        IReadOnlyList<IntelShipClass> orderedClasses,
        int slots)
    {
        if (slots <= 0 || orderedClasses.Count == 0)
        {
            return [];
        }

        var total = orderedClasses.Sum(c => Math.Max(0, classCounts.TryGetValue(c, out var count) ? count : 0));
        if (total <= 0)
        {
            return orderedClasses.Take(Math.Min(slots, orderedClasses.Count)).ToList();
        }

        var result = new List<IntelShipClass>(slots);
        var allocated = new Dictionary<IntelShipClass, int>();
        foreach (var shipClass in orderedClasses)
        {
            var count = classCounts.TryGetValue(shipClass, out var raw) ? Math.Max(0, raw) : 0;
            if (count <= 0)
            {
                continue;
            }

            var weightedShare = (double)count / total;
            var slotsForClass = Math.Max(1, (int)Math.Round(weightedShare * slots, MidpointRounding.AwayFromZero));
            allocated[shipClass] = slotsForClass;
        }

        foreach (var shipClass in orderedClasses)
        {
            if (!allocated.TryGetValue(shipClass, out var forClass) || forClass <= 0)
            {
                continue;
            }

            for (var i = 0; i < forClass && result.Count < slots; i++)
            {
                result.Add(shipClass);
            }
        }

        while (result.Count < slots)
        {
            result.Add(orderedClasses[0]);
        }

        if (result.Count > slots)
        {
            result = result.Take(slots).ToList();
        }

        return result;
    }

    private void RebuildCharacterPresenceForView()
    {
        var graph = CurrentGraph;
        if (graph is null || graph.Nodes.Count == 0 || SelectedViewMode == MapViewMode.UniverseRegions)
        {
            _characterPresenceCountsByNodeId = new Dictionary<long, int>();
            _characterPresenceNamesByNodeId = new Dictionary<long, IReadOnlyList<string>>();
            _characterPresenceCharacterIdsByNodeId = new Dictionary<long, IReadOnlyList<int>>();
            _characterPresenceLastUpdatedUtcByNodeId = new Dictionary<long, DateTime>();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceCountsByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceNamesByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceCharacterIdsByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceLastUpdatedUtcByNodeIdForView)));
            return;
        }

        Dictionary<int, LocalCharacterSystemChange> snapshot;
        lock (_localCharacterLocationsByCharacterId)
        {
            snapshot = _localCharacterLocationsByCharacterId.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        var nodeByName = graph.Nodes
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var namesByNode = new Dictionary<long, List<(int CharacterId, string CharacterName)>>();
        var latestSeenByNode = new Dictionary<long, DateTime>();
        foreach (var character in snapshot.Values)
        {
            if (!IsCharacterTrackingEnabled(character.CharacterId))
            {
                continue;
            }

            if (!nodeByName.TryGetValue(character.SolarSystemName, out var node))
            {
                continue;
            }

            if (!namesByNode.TryGetValue(node.Id, out var names))
            {
                names = [];
                namesByNode[node.Id] = names;
            }

            names.Add((character.CharacterId, character.CharacterName));
            if (!latestSeenByNode.TryGetValue(node.Id, out var currentLatest) || character.TimestampUtc > currentLatest)
            {
                latestSeenByNode[node.Id] = character.TimestampUtc;
            }
        }

        _characterPresenceCountsByNodeId = namesByNode.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
        var sortedByNode = namesByNode.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .OrderBy(n => GetCharacterPriorityById(n.CharacterId))
                .ThenBy(n => n.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToList());
        _characterPresenceNamesByNodeId = sortedByNode.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.Select(n => n.CharacterName).ToList());
        _characterPresenceCharacterIdsByNodeId = sortedByNode.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<int>)kvp.Value.Select(n => n.CharacterId).ToList());
        _characterPresenceLastUpdatedUtcByNodeId = latestSeenByNode;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceCountsByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceNamesByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceCharacterIdsByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CharacterPresenceLastUpdatedUtcByNodeIdForView)));
    }

    public void MoveCharacterTrackingPriorityUp(int characterId)
    {
        if (!_characterTrackingPreferencesById.TryGetValue(characterId, out var pref))
        {
            return;
        }

        var ordered = _characterTrackingPreferencesById.Values.OrderBy(x => x.Priority).ToList();
        var index = ordered.FindIndex(x => x.CharacterId == characterId);
        if (index <= 0)
        {
            return;
        }

        (ordered[index - 1].Priority, ordered[index].Priority) = (ordered[index].Priority, ordered[index - 1].Priority);
        NormalizeCharacterPriorities(ordered);
        _ = SaveCharacterTrackingPreferencesAsync();
        RebuildCharacterTrackingCards();
        RebuildCharacterPresenceForView();
    }

    public void MoveCharacterTrackingPriorityDown(int characterId)
    {
        if (!_characterTrackingPreferencesById.TryGetValue(characterId, out var pref))
        {
            return;
        }

        var ordered = _characterTrackingPreferencesById.Values.OrderBy(x => x.Priority).ToList();
        var index = ordered.FindIndex(x => x.CharacterId == characterId);
        if (index < 0 || index >= ordered.Count - 1)
        {
            return;
        }

        (ordered[index + 1].Priority, ordered[index].Priority) = (ordered[index].Priority, ordered[index + 1].Priority);
        NormalizeCharacterPriorities(ordered);
        _ = SaveCharacterTrackingPreferencesAsync();
        RebuildCharacterTrackingCards();
        RebuildCharacterPresenceForView();
    }

    public void SetCharacterTrackingEnabled(int characterId, bool isEnabled)
    {
        if (!_characterTrackingPreferencesById.TryGetValue(characterId, out var pref))
        {
            return;
        }

        if (pref.IsEnabled == isEnabled)
        {
            return;
        }

        pref.IsEnabled = isEnabled;
        if (!isEnabled)
        {
            lock (_localCharacterLocationsByCharacterId)
            {
                _localCharacterLocationsByCharacterId.Remove(characterId);
            }
        }

        _ = SaveCharacterTrackingPreferencesAsync();
        RebuildCharacterTrackingCards();
        RebuildCharacterPresenceForView();
    }

    public void MoveCharacterTrackingAmongEnabled(int sourceCharacterId, int targetCharacterId)
    {
        if (sourceCharacterId == targetCharacterId)
        {
            return;
        }

        var enabledOrdered = _characterTrackingPreferencesById.Values
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToList();
        var sourceIndex = enabledOrdered.FindIndex(x => x.CharacterId == sourceCharacterId);
        var targetIndex = enabledOrdered.FindIndex(x => x.CharacterId == targetCharacterId);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var moved = enabledOrdered[sourceIndex];
        enabledOrdered.RemoveAt(sourceIndex);
        enabledOrdered.Insert(targetIndex, moved);

        var disabledOrdered = _characterTrackingPreferencesById.Values
            .Where(x => !x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToList();

        var combined = enabledOrdered.Concat(disabledOrdered).ToList();
        NormalizeCharacterPriorities(combined);
        _ = SaveCharacterTrackingPreferencesAsync();
        RebuildCharacterTrackingCards();
        RebuildCharacterPresenceForView();
    }

    public void MoveCharacterTrackingUpAmongEnabled(int characterId)
    {
        var enabledOrdered = _characterTrackingPreferencesById.Values
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToList();
        var index = enabledOrdered.FindIndex(x => x.CharacterId == characterId);
        if (index <= 0)
        {
            return;
        }

        MoveCharacterTrackingAmongEnabled(characterId, enabledOrdered[index - 1].CharacterId);
    }

    public void MoveCharacterTrackingDownAmongEnabled(int characterId)
    {
        var enabledOrdered = _characterTrackingPreferencesById.Values
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Priority)
            .ToList();
        var index = enabledOrdered.FindIndex(x => x.CharacterId == characterId);
        if (index < 0 || index >= enabledOrdered.Count - 1)
        {
            return;
        }

        MoveCharacterTrackingAmongEnabled(characterId, enabledOrdered[index + 1].CharacterId);
    }

    private void EnsureCharacterTrackingPreference(LocalCharacterSystemChange change)
    {
        var created = false;
        if (!_characterTrackingPreferencesById.TryGetValue(change.CharacterId, out var pref))
        {
            pref = new CharacterTrackingPreference
            {
                CharacterId = change.CharacterId,
                CharacterName = change.CharacterName,
                IsEnabled = true,
                Priority = _characterTrackingPreferencesById.Count
            };
            _characterTrackingPreferencesById[change.CharacterId] = pref;
            created = true;
        }
        else if (!string.IsNullOrWhiteSpace(change.CharacterName) &&
                 !string.Equals(pref.CharacterName, change.CharacterName, StringComparison.Ordinal))
        {
            pref.CharacterName = change.CharacterName;
        }

        if (created)
        {
            _ = SaveCharacterTrackingPreferencesAsync();
        }
    }

    private bool IsCharacterTrackingEnabled(int characterId)
    {
        return !_characterTrackingPreferencesById.TryGetValue(characterId, out var pref) || pref.IsEnabled;
    }

    private int GetCharacterPriorityById(int characterId)
    {
        return _characterTrackingPreferencesById.TryGetValue(characterId, out var pref)
            ? pref.Priority
            : int.MaxValue;
    }

    private void RebuildCharacterTrackingCards()
    {
        var lastByCharacterId = new Dictionary<int, LocalCharacterSystemChange>();
        lock (_localCharacterLocationsByCharacterId)
        {
            foreach (var kvp in _localCharacterLocationsByCharacterId)
            {
                lastByCharacterId[kvp.Key] = kvp.Value;
            }
        }

        var ordered = _characterTrackingPreferencesById.Values
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        NormalizeCharacterPriorities(ordered);

        _characterTrackingCards.Clear();
        _enabledCharacterTrackingCards.Clear();
        _disabledCharacterTrackingCards.Clear();
        _isRebuildingFollowCharacterOptions = true;
        foreach (var pref in ordered)
        {
            var hasLast = lastByCharacterId.TryGetValue(pref.CharacterId, out var last);
            var card = new CharacterTrackingCardViewModel
            {
                CharacterId = pref.CharacterId,
                Name = string.IsNullOrWhiteSpace(pref.CharacterName) ? pref.CharacterId.ToString() : pref.CharacterName,
                LastLocation = hasLast ? last!.SolarSystemName : "Unknown",
                LastUpdated = hasLast ? FormatRelativeAge(last!.TimestampUtc) : "Never",
                IsEnabled = pref.IsEnabled,
                Priority = pref.Priority + 1
            };
            _characterTrackingCards.Add(card);
            if (pref.IsEnabled)
            {
                _enabledCharacterTrackingCards.Add(card);
            }
            else
            {
                _disabledCharacterTrackingCards.Add(card);
            }
            _ = card.EnsurePortraitLoadedAsync();
        }

        try
        {
            _followCharacterOptions.Clear();
            _followCharacterOptions.Add(new FollowCharacterOption
            {
                CharacterId = null,
                DisplayName = "Follow: Off"
            });

            foreach (var pref in ordered
                         .Where(pref => pref.IsEnabled)
                         .OrderBy(pref => pref.CharacterName, StringComparer.OrdinalIgnoreCase))
            {
                _followCharacterOptions.Add(new FollowCharacterOption
                {
                    CharacterId = pref.CharacterId,
                    DisplayName = string.IsNullOrWhiteSpace(pref.CharacterName) ? pref.CharacterId.ToString() : pref.CharacterName
                });
            }

            FollowedCharacter = _followCharacterOptions.FirstOrDefault(option => option.CharacterId == _followedCharacterId)
                ?? _followCharacterOptions[0];
        }
        finally
        {
            _isRebuildingFollowCharacterOptions = false;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFollowableCharacters)));
    }

    public bool TryGetCharacterLocation(int characterId, out LocalCharacterSystemChange? location)
    {
        lock (_localCharacterLocationsByCharacterId)
        {
            return _localCharacterLocationsByCharacterId.TryGetValue(characterId, out location);
        }
    }

    private static void NormalizeCharacterPriorities(List<CharacterTrackingPreference> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Priority = i;
        }
    }

    private Task SaveCharacterTrackingPreferencesAsync()
    {
        var payload = _characterTrackingPreferencesById.Values
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.CharacterName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CharacterTrackingPreference
            {
                CharacterId = x.CharacterId,
                CharacterName = x.CharacterName,
                IsEnabled = x.IsEnabled,
                Priority = x.Priority
            })
            .ToList();
        return _settingsService.SetAsync(TrackingCharacterPreferencesKey, payload);
    }

    private static string FormatRelativeAge(DateTime timestampUtc)
    {
        var utc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)}s ago";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        return $"{(int)elapsed.TotalDays}d ago";
    }

    public void ValidateLogsRootPath()
    {
        var result = LocalChatLogsPathValidator.Validate(LogsRootPath);
        _isLogsPathValid = result.IsValid;
        _logsPathValidationStatus = result.IsValid
            ? $"{result.Message} ({result.ChatLogsPath})"
            : result.Message;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLogsPathValid)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogsPathValidationStatus)));
    }

    public async Task SaveLogsRootPathAsync()
    {
        ValidateLogsRootPath();
        if (!_isLogsPathValid)
        {
            return;
        }

        await _settingsService.SetAsync(TrackingLogsRootPathKey, LogsRootPath.Trim());
    }

    private void OnMiningSnapshotUpdated(object? sender, IReadOnlyDictionary<int, MiningCharacterStatsSnapshot> snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (_miningStatsByCharacterId)
            {
                _miningStatsByCharacterId.Clear();
                foreach (var kvp in snapshot)
                {
                    _miningStatsByCharacterId[kvp.Key] = kvp.Value;
                }
            }

            if (SelectedMiningRangeMode == MiningStatsRangeMode.CurrentSession)
            {
                RebuildMiningStatsCards();
            }
        });
    }

    private void RebuildMiningStatsCards()
    {
        List<MiningCharacterStatsSnapshot> snapshots;
        lock (_miningStatsByCharacterId)
        {
            snapshots = _miningStatsByCharacterId.Values.ToList();
        }

        RebuildMiningStatsCards(snapshots);
    }

    private void RebuildMiningStatsCards(IEnumerable<MiningCharacterStatsSnapshot> snapshots)
    {
        var next = snapshots
            .OrderByDescending(x => x.LastActivityUtc)
            .ThenBy(x => x.CharacterName, StringComparer.OrdinalIgnoreCase)
            .Select(BuildMiningStatsCard)
            .ToList();
        var aggregate = BuildAggregateMiningStatsCard(snapshots.ToList());

        _miningStatsCards.Clear();
        foreach (var card in next)
        {
            _miningStatsCards.Add(card);
            _ = card.EnsurePortraitLoadedAsync();
        }
        _aggregateMiningStatsCard = aggregate;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MiningStatsCards)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AggregateMiningStatsCard)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAggregateMiningStats)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMiningStats)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoMiningStats)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MiningWindowStatus)));
    }

    public async Task RefreshMiningStatsForSelectedRangeAsync()
    {
        _miningStatsRefreshCts?.Cancel();
        _miningStatsRefreshCts?.Dispose();
        _miningStatsRefreshCts = new CancellationTokenSource();
        var ct = _miningStatsRefreshCts.Token;

        IsMiningStatsLoading = true;
        try
        {
            var snapshot = await _miningSessionFeed.GetSnapshotAsync(SelectedMiningRangeMode, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => RebuildMiningStatsCards(snapshot.Values));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsMiningStatsLoading = false;
            }
        }
    }

    public async Task RefreshMiningOverlayAsync()
    {
        IReadOnlyDictionary<int, MiningCharacterStatsSnapshot> snapshot = SelectedMiningOverlayRangeMode switch
        {
            MiningOverlayRangeMode.UseSelectedRange => await _miningSessionFeed.GetSnapshotAsync(SelectedMiningRangeMode),
            MiningOverlayRangeMode.Rolling5Minutes => await _miningSessionFeed.GetRollingSnapshotAsync(TimeSpan.FromMinutes(5)),
            MiningOverlayRangeMode.Rolling10Minutes => await _miningSessionFeed.GetRollingSnapshotAsync(TimeSpan.FromMinutes(10)),
            MiningOverlayRangeMode.Rolling15Minutes => await _miningSessionFeed.GetRollingSnapshotAsync(TimeSpan.FromMinutes(15)),
            MiningOverlayRangeMode.Rolling30Minutes => await _miningSessionFeed.GetRollingSnapshotAsync(TimeSpan.FromMinutes(30)),
            _ => await _miningSessionFeed.GetRollingSnapshotAsync(TimeSpan.FromMinutes(10))
        };
        var aggregate = BuildAggregateMiningStatsCard(
            snapshot.Values.ToList(),
            overlayMode: true,
            overlayRangeMode: SelectedMiningOverlayRangeMode,
            selectedRangeMode: SelectedMiningRangeMode);
        _overlayAggregateMiningStatsCard = aggregate;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OverlayAggregateMiningStatsCard)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOverlayAggregateMiningStats)));
    }

    public async Task SaveMiningRefineYieldAsync()
    {
        var raw = MiningRefineYieldPercentText?.Trim();
        if (!decimal.TryParse(raw, out var parsed))
        {
            StatusText = "Invalid mining refine yield. Use a percent like 90.63.";
            return;
        }

        parsed = Math.Clamp(parsed, 1m, 100m);
        MiningRefineYieldPercentText = $"{parsed:0.##}";
        await _settingsService.SetAsync(MiningRefineYieldPercentKey, parsed);
        StatusText = "Mining refine yield saved. Mining values were refreshed with the new percentage.";
        await RefreshMiningStatsForSelectedRangeAsync();
        if (IsMiningOverlayVisible)
        {
            await RefreshMiningOverlayAsync();
        }
    }

    private static MiningStatsCard BuildMiningStatsCard(MiningCharacterStatsSnapshot snapshot)
    {
        var oreMixSummary = string.Join(
            " | ",
            snapshot.Ores
                .OrderByDescending(x => x.TotalMinedVolumeM3)
                .Take(3)
                .Select(x => $"{x.OreName} {x.TotalMinedVolumeM3:N0} m3"));
        var totalFlow = snapshot.TotalMiningVolumeM3;
        var yieldRatio = totalFlow > 0 ? snapshot.TotalMinedVolumeM3 / totalFlow : 1d;
        var wasteRatio = totalFlow > 0 ? snapshot.TotalWasteVolumeM3 / totalFlow : 0d;

        return new MiningStatsCard
        {
            CharacterId = snapshot.CharacterId,
            CharacterName = snapshot.CharacterName,
            PrimaryOreName = string.IsNullOrWhiteSpace(snapshot.PrimaryOreName) ? "Mixed" : snapshot.PrimaryOreName,
            EfficiencyPercentSummary = $"{snapshot.EfficiencyPercent:0.#}%",
            YieldPercentSummary = $"{snapshot.YieldPercent:0.#}%",
            CritPercentSummary = $"{snapshot.CritPercent:0.#}%",
            WastePercentSummary = $"{snapshot.WastePercent:0.#}%",
            TotalMiningRateSummary = $"{snapshot.TotalMiningRateM3PerHour / 3600d:0.##} m3/s | {snapshot.TotalMiningRateM3PerHour:N0} m3/hr",
            MiningRateSummary = $"{snapshot.MiningRateM3PerHour / 3600d:0.##} m3/s | {snapshot.MiningRateM3PerHour:N0} m3/hr",
            WasteRateSummary = $"{snapshot.WasteRateM3PerHour / 3600d:0.##} m3/s | {snapshot.WasteRateM3PerHour:N0} m3/hr",
            IskRateSummary = snapshot.EstimatedIskPerHour > 0 ? $"{snapshot.EstimatedIskPerHour:N0} ISK/hr" : "Price unavailable",
            WasteIskRateSummary = snapshot.WasteEstimatedIskPerHour > 0 ? $"-{snapshot.WasteEstimatedIskPerHour:N0} ISK/hr" : "No waste loss",
            TotalMinedSummary = $"{snapshot.TotalMinedVolumeM3:N0} m3 mined",
            TotalEstimatedIskSummary = snapshot.TotalEstimatedIsk > 0 ? $"{snapshot.TotalEstimatedIsk:N0} ISK total" : "Price unavailable",
            TotalWasteIskSummary = snapshot.TotalWasteEstimatedIsk > 0
                ? $"-{snapshot.TotalWasteEstimatedIsk:N0} ISK"
                : "No waste loss",
            SessionTotalSummary = $"{snapshot.TotalRegularYieldVolumeM3:N0} m3 yield | {snapshot.TotalCritVolumeM3:N0} m3 crit",
            WasteTotalSummary = $"{snapshot.TotalWasteVolumeM3:N0} m3",
            EfficiencySummary = BuildMiningEfficiencySummary(snapshot),
            SessionAgeSummary = FormatMiningElapsed(snapshot.LastActivityUtc - snapshot.SessionStartedUtc),
            LastUpdatedSummary = FormatOverlayAge(DateTime.UtcNow - snapshot.LastActivityUtc),
            OreMixSummary = string.IsNullOrWhiteSpace(oreMixSummary) ? "No ore mix available yet." : oreMixSummary,
            YieldRatio = yieldRatio,
            WasteRatio = wasteRatio
        };
    }

    private static MiningStatsCard? BuildAggregateMiningStatsCard(
        IReadOnlyList<MiningCharacterStatsSnapshot> snapshots,
        bool overlayMode = false,
        MiningOverlayRangeMode overlayRangeMode = MiningOverlayRangeMode.Rolling10Minutes,
        MiningStatsRangeMode selectedRangeMode = MiningStatsRangeMode.CurrentSession)
    {
        if (snapshots.Count == 0)
        {
            return null;
        }

        var totalRegularYieldVolume = snapshots.Sum(x => x.TotalRegularYieldVolumeM3);
        var totalCritVolume = snapshots.Sum(x => x.TotalCritVolumeM3);
        var totalMinedVolume = snapshots.Sum(x => x.TotalMinedVolumeM3);
        var totalWasteVolume = snapshots.Sum(x => x.TotalWasteVolumeM3);
        var totalEstimatedIsk = snapshots.Sum(x => x.TotalEstimatedIsk);
        var totalWasteEstimatedIsk = snapshots.Sum(x => x.TotalWasteEstimatedIsk);
        var earliestSessionStartUtc = snapshots.Min(x => x.SessionStartedUtc);
        var lastActivityUtc = snapshots.Max(x => x.LastActivityUtc);
        var activeCharacters = snapshots.Count;
        var totalFlow = totalMinedVolume + totalWasteVolume;
        var totalDepletionFlow = totalRegularYieldVolume + totalWasteVolume;
        var totalMiningRateM3PerHour = snapshots.Sum(x => x.TotalMiningRateM3PerHour);
        var miningRateM3PerHour = snapshots.Sum(x => x.MiningRateM3PerHour);
        var wasteRateM3PerHour = snapshots.Sum(x => x.WasteRateM3PerHour);
        var depletionRateM3PerHour = snapshots.Sum(x => x.DepletionRateM3PerHour);
        var estimatedIskPerHour = snapshots.Sum(x => x.EstimatedIskPerHour);
        var yieldRatio = totalFlow > 0 ? totalMinedVolume / totalFlow : 1d;
        var wasteRatio = totalFlow > 0 ? totalWasteVolume / totalFlow : 0d;
        var elapsed = lastActivityUtc > earliestSessionStartUtc
            ? lastActivityUtc - earliestSessionStartUtc
            : TimeSpan.FromSeconds(1);
        var totalWasteEstimatedIskPerHour = snapshots.Sum(x => x.WasteEstimatedIskPerHour);
        var oreMixSummary = string.Join(
            " | ",
            snapshots
                .SelectMany(x => x.Ores)
                .GroupBy(x => x.OreName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    OreName = group.Key,
                    Volume = group.Sum(x => x.TotalMinedVolumeM3)
                })
                .OrderByDescending(x => x.Volume)
                .Take(5)
                .Select(x => $"{x.OreName} {x.Volume:N0} m3"));

        var weightedEfficiency = totalDepletionFlow > 0 ? (totalMinedVolume / totalDepletionFlow) * 100d : 100d;
        var yieldPercent = totalDepletionFlow > 0 ? (totalRegularYieldVolume / totalDepletionFlow) * 100d : 0d;
        var critPercent = totalDepletionFlow > 0 ? (totalCritVolume / totalDepletionFlow) * 100d : 0d;
        var wastePercent = totalDepletionFlow > 0 ? (totalWasteVolume / totalDepletionFlow) * 100d : 0d;
        var overlayPrimaryLabel = overlayRangeMode == MiningOverlayRangeMode.UseSelectedRange
            ? $"Using {GetMiningRangeModeLabel(selectedRangeMode)} | {activeCharacters} character{(activeCharacters == 1 ? string.Empty : "s")}"
            : $"{GetMiningOverlayRangeModeLabel(overlayRangeMode)} live window | {activeCharacters} character{(activeCharacters == 1 ? string.Empty : "s")}";

        return new MiningStatsCard
        {
            CharacterId = 0,
            CharacterName = overlayMode ? "Live Mining Overlay" : "Combined Fleet",
            PrimaryOreName = overlayMode
                ? overlayPrimaryLabel
                : $"{activeCharacters} character{(activeCharacters == 1 ? string.Empty : "s")} included",
            EfficiencyPercentSummary = $"{weightedEfficiency:0.#}%",
            YieldPercentSummary = $"{yieldPercent:0.#}%",
            CritPercentSummary = $"{critPercent:0.#}%",
            WastePercentSummary = $"{wastePercent:0.#}%",
            TotalMiningRateSummary = overlayMode
                ? $"{depletionRateM3PerHour / 3600d:0.##} m3/s depletion"
                : $"{totalMiningRateM3PerHour / 3600d:0.##} m3/s | {totalMiningRateM3PerHour:N0} m3/hr",
            MiningRateSummary = overlayMode
                ? $"{miningRateM3PerHour / 3600d:0.##} m3/s yield"
                : $"{miningRateM3PerHour / 3600d:0.##} m3/s | {miningRateM3PerHour:N0} m3/hr",
            WasteRateSummary = overlayMode
                ? $"{wasteRateM3PerHour / 3600d:0.##} m3/s"
                : $"{wasteRateM3PerHour / 3600d:0.##} m3/s | {wasteRateM3PerHour:N0} m3/hr",
            IskRateSummary = estimatedIskPerHour > 0 ? $"{estimatedIskPerHour:N0} ISK/hr" : "Price unavailable",
            WasteIskRateSummary = totalWasteEstimatedIskPerHour > 0 ? $"-{totalWasteEstimatedIskPerHour:N0} ISK/hr" : "No waste loss",
            TotalMinedSummary = $"{totalMinedVolume:N0} m3 mined",
            TotalEstimatedIskSummary = totalEstimatedIsk > 0 ? $"{totalEstimatedIsk:N0} ISK total" : "Price unavailable",
            TotalWasteIskSummary = totalWasteEstimatedIsk > 0
                ? $"-{totalWasteEstimatedIsk:N0} ISK"
                : "No waste loss",
            SessionTotalSummary = $"{totalRegularYieldVolume:N0} m3 yield | {totalCritVolume:N0} m3 crit",
            WasteTotalSummary = $"{totalWasteVolume:N0} m3 residue",
            EfficiencySummary = $"{weightedEfficiency:0.#}% actual efficiency across fleet",
            SessionAgeSummary = FormatMiningElapsed(elapsed),
            LastUpdatedSummary = FormatOverlayAge(DateTime.UtcNow - lastActivityUtc),
            OreMixSummary = string.IsNullOrWhiteSpace(oreMixSummary) ? "No ore mix available yet." : oreMixSummary,
            YieldRatio = yieldRatio,
            WasteRatio = wasteRatio
        };
    }

    private static string BuildMiningEfficiencySummary(MiningCharacterStatsSnapshot snapshot)
    {
        var ratioPercent = $"{snapshot.EfficiencyPercent:0.#}% actual efficiency";
        return snapshot.CurrentEfficiencyPercent is { } currentPercent
            ? $"{ratioPercent} | site {currentPercent}%"
            : ratioPercent;
    }

    private static string FormatMiningElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m session";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s session";
        }

        return $"{Math.Max(0, (int)elapsed.TotalSeconds)}s session";
    }

    public static string GetMiningRangeModeLabel(MiningStatsRangeMode mode)
    {
        return mode switch
        {
            MiningStatsRangeMode.CurrentSession => "Current Session",
            MiningStatsRangeMode.Last1Hour => "Last 1 Hour",
            MiningStatsRangeMode.Last2Hours => "Last 2 Hours",
            MiningStatsRangeMode.Last4Hours => "Last 4 Hours",
            MiningStatsRangeMode.Last6Hours => "Last 6 Hours",
            MiningStatsRangeMode.Last8Hours => "Last 8 Hours",
            MiningStatsRangeMode.Last12Hours => "Last 12 Hours",
            MiningStatsRangeMode.Last24Hours => "Last 24 Hours",
            MiningStatsRangeMode.Last3Days => "Last 3 Days",
            MiningStatsRangeMode.Last7Days => "Last 7 Days",
            _ => mode.ToString()
        };
    }

    public static string GetMiningOverlayRangeModeLabel(MiningOverlayRangeMode mode)
    {
        return mode switch
        {
            MiningOverlayRangeMode.UseSelectedRange => "Selected Range",
            MiningOverlayRangeMode.Rolling5Minutes => "5m",
            MiningOverlayRangeMode.Rolling10Minutes => "10m",
            MiningOverlayRangeMode.Rolling15Minutes => "15m",
            MiningOverlayRangeMode.Rolling30Minutes => "30m",
            _ => mode.ToString()
        };
    }

    public async Task SaveIntelSettingsAsync()
    {
        var include = IntelIncludeChannelsText
            .Split(['\r', '\n', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (IntelEnabled && include.Count == 0)
        {
            StatusText = "Intel is enabled but no included channels are configured. Add at least one channel name.";
            return;
        }

        IntelSystemExpiryMinutes = Math.Clamp(IntelSystemExpiryMinutes, 1, 180);
        IntelListExpiryMinutes = Math.Clamp(IntelListExpiryMinutes, 1, 240);
        IntelIncludeChannelsText = string.Join(Environment.NewLine, include);

        await _settingsService.SetAsync(IntelEnabledKey, IntelEnabled);
        await _settingsService.SetAsync(IntelIncludeChannelsKey, include);
        await _settingsService.SetAsync(IntelSystemExpiryMinutesKey, IntelSystemExpiryMinutes);
        await _settingsService.SetAsync(IntelListExpiryMinutesKey, IntelListExpiryMinutes);
        await _settingsService.SetAsync(HubWormholeLimitToCurrentRegionKey, LimitHubWormholesToCurrentRegion);
        await _settingsService.SetAsync(IncursionLimitToCurrentRegionKey, LimitIncursionsToCurrentRegion);
        await _settingsService.SetAsync(StormLimitToCurrentRegionKey, LimitStormsToCurrentRegion);
        await _settingsService.SetAsync(IntelLimitToCurrentRegionKey, LimitIntelReportsToCurrentRegion);
        await _settingsService.SetAsync(ZkillLimitToCurrentRegionKey, LimitZkillmailsToCurrentRegion);
        await _settingsService.SetAsync(ZkillHideOutsideKnownSpaceKey, HideZkillmailsOutsideKnownSpace);
        await _intelFeed.ApplySettingsAsync();
        PruneIntelStateForCurrentSettings();
        RebuildIntelPresenceForView();
        ScheduleActivityCardsRebuild();
        StatusText = "Intel settings saved and applied.";
    }

    public IReadOnlyList<AlertRule> GetAlertRulesSnapshot()
    {
        return _alertRules
            .Select(CloneAlertRule)
            .ToList();
    }

    public IReadOnlyList<RegionOption> GetAlertRegionOptionsSnapshot() => _allRegions
        .Where(region => !region.IsHeader && region.Kind == RegionOptionKind.Regular && region.RegionId > 0)
        .OrderBy(region => region.RegionName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public async Task SaveAlertRulesAsync(IReadOnlyList<AlertRule> rules)
    {
        var sanitized = (rules ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(CloneAlertRule)
            .ToList();
        _alertRules = sanitized;
        await _settingsService.SetAsync(AlertRulesKey, sanitized);
        StatusText = $"Alerts settings saved ({sanitized.Count} rule(s)).";
    }

    public AlertPopupSettings GetAlertPopupSettingsSnapshot() => new()
    {
        Enabled = _alertPopupSettings.Enabled,
        MaxCards = _alertPopupSettings.MaxCards,
        AutoDismissSeconds = _alertPopupSettings.AutoDismissSeconds,
        Opacity = _alertPopupSettings.Opacity,
        Width = _alertPopupSettings.Width,
        Height = _alertPopupSettings.Height,
        Anchor = _alertPopupSettings.Anchor,
        OffsetX = _alertPopupSettings.OffsetX,
        OffsetY = _alertPopupSettings.OffsetY
    };

    public async Task SaveAlertPopupSettingsAsync(AlertPopupSettings settings)
    {
        _alertPopupSettings = SanitizeAlertPopupSettings(settings);
        await _settingsService.SetAsync(AlertPopupSettingsKey, _alertPopupSettings);
        StatusText = "Alert popup settings saved.";
    }

    public IReadOnlyList<string> GetTrackedCharacterNamesSnapshot()
    {
        var names = _characterTrackingPreferencesById.Values
            .Select(x => x.CharacterName?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names;
    }

    public string GetTrackedCharacterNameById(int characterId)
    {
        if (_characterTrackingPreferencesById.TryGetValue(characterId, out var pref) &&
            !string.IsNullOrWhiteSpace(pref.CharacterName))
        {
            return pref.CharacterName.Trim();
        }

        lock (_localCharacterLocationsByCharacterId)
        {
            if (_localCharacterLocationsByCharacterId.TryGetValue(characterId, out var local) &&
                !string.IsNullOrWhiteSpace(local.CharacterName))
            {
                return local.CharacterName.Trim();
            }
        }

        return characterId.ToString();
    }

    public async Task<IReadOnlyList<int>> ResolveCharacterIdsByNamesAsync(IReadOnlyList<string> characterNames)
    {
        var names = (characterNames ?? [])
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return [];
        }

        var resolved = new HashSet<int>();
        var unresolved = new List<string>();

        foreach (var name in names)
        {
            var localMatch = _characterTrackingPreferencesById.Values
                .FirstOrDefault(x => string.Equals(x.CharacterName, name, StringComparison.OrdinalIgnoreCase));
            if (localMatch is not null)
            {
                resolved.Add(localMatch.CharacterId);
                _characterIdByName[name] = localMatch.CharacterId;
                continue;
            }

            if (_characterIdByName.TryGetValue(name, out var cachedId) && cachedId > 0)
            {
                resolved.Add(cachedId);
                continue;
            }

            unresolved.Add(name);
        }

        if (unresolved.Count > 0)
        {
            try
            {
                var payload = JsonSerializer.Serialize(unresolved);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://esi.evetech.net/latest/universe/ids/?datasource=tranquility");
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await IntelPortraitHttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    await using var responseStream = await response.Content.ReadAsStreamAsync();
                    var result = await JsonSerializer.DeserializeAsync<EsiUniverseIdsResponse>(responseStream);
                    foreach (var match in result?.Characters ?? [])
                    {
                        if (match.Id <= 0 || string.IsNullOrWhiteSpace(match.Name))
                        {
                            continue;
                        }

                        resolved.Add(match.Id);
                        _characterIdByName[match.Name] = match.Id;
                    }
                }
            }
            catch
            {
                // Ignore lookup failures; unresolved names are kept as names in the rule.
            }
        }

        return resolved.ToList();
    }

    public Task ClearIntelAndKillmailHistoryAsync()
    {
        lock (_intelReportHistory)
        {
            _intelReportHistory.Clear();
        }

        lock (_intelSnapshotsBySystemId)
        {
            _intelSnapshotsBySystemId.Clear();
        }

        ScheduleActivityCardsRebuild();
        RebuildIntelPresenceForView();
        return Task.CompletedTask;
    }

    public async Task NavigateToSystemFromReportAsync(long systemId)
    {
        if (systemId <= 0)
        {
            return;
        }

        var shouldSwitchToUniverse = SelectedViewMode == MapViewMode.UniverseRegions;
        if (SelectedViewMode == MapViewMode.Region)
        {
            var existsInCurrentRegionLayout = CurrentGraph?.Nodes.Any(n => n.Id == systemId) == true;
            shouldSwitchToUniverse = !existsInCurrentRegionLayout;
        }

        if (shouldSwitchToUniverse)
        {
            SelectedViewMode = MapViewMode.Universe;
            var deadline = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < deadline)
            {
                if (CurrentGraph?.Nodes.Any(n => n.Id == systemId) == true)
                {
                    break;
                }

                await Task.Delay(30);
            }
        }

        SelectedNodeId = systemId;
    }

    private static string GetDefaultEveLogsRootPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EVE",
            "logs");
    }

    private void RebuildAnsiblexLinksForView(MapGraph? graph)
    {
        if (graph is null || graph.Nodes.Count == 0)
        {
            AnsiblexLinksForView = [];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnsiblexLinksForView)));
            return;
        }

        var nodeSet = graph.Nodes.Select(x => x.Id).ToHashSet();
        AnsiblexLinksForView = _ansiblexNetworkStateService.CurrentLinks
            .Where(x => nodeSet.Contains(x.FromSolarSystemId) && nodeSet.Contains(x.ToSolarSystemId))
            .Select(x => new MapLink { FromId = x.FromSolarSystemId, ToId = x.ToSolarSystemId })
            .ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnsiblexLinksForView)));
    }

    public Task<SovImportResult> ImportSovUpgradesAsync(string rawText, SovImportMode mode, CancellationToken cancellationToken = default)
    {
        return _sovUpgradeStateService.ImportFromTextAsync(rawText, mode, cancellationToken);
    }

    public Task AddOrUpdateSovUpgradeAsync(string systemName, string upgradeName, int tier, CancellationToken cancellationToken = default)
    {
        return _sovUpgradeStateService.AddOrUpdateUpgradeAsync(systemName, upgradeName, tier, cancellationToken);
    }

    public Task RemoveSovSystemAsync(string systemName, CancellationToken cancellationToken = default)
    {
        return _sovUpgradeStateService.RemoveSystemAsync(systemName, cancellationToken);
    }

    public Task<IReadOnlyList<SovSystemUpgradeRecord>> GetSovUpgradeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _sovUpgradeStateService.GetSnapshotAsync(cancellationToken);
    }

    public Task<IReadOnlyList<MiningSiteReportRecord>> GetMiningSiteReportsAsync(CancellationToken cancellationToken = default) =>
        _miningSiteTrackerService.GetSnapshotAsync(cancellationToken);

    public Task MarkMiningSiteClearedAsync(int solarSystemId, string upgradeName, int tier, CancellationToken cancellationToken = default) =>
        _miningSiteTrackerService.SetClearedAsync(solarSystemId, upgradeName, tier, cancellationToken);

    public Task MarkMiningSiteMissingAsync(int solarSystemId, string upgradeName, int tier, TimeSpan reminderDelay, CancellationToken cancellationToken = default) =>
        _miningSiteTrackerService.SetMissingAsync(solarSystemId, upgradeName, tier, reminderDelay, cancellationToken);

    public Task MarkMiningSiteAvailableAsync(int solarSystemId, string upgradeName, int tier, CancellationToken cancellationToken = default) =>
        _miningSiteTrackerService.MarkAvailableAsync(solarSystemId, upgradeName, tier, cancellationToken);

    public Task<AnsiblexImportResult> ImportAnsiblexNetworkAsync(string rawText, SovImportMode mode, CancellationToken cancellationToken = default)
    {
        return _ansiblexNetworkStateService.ImportFromTextAsync(rawText, mode, cancellationToken);
    }

    public Task AddOrUpdateAnsiblexLinkAsync(string fromSystemName, string toSystemName, CancellationToken cancellationToken = default)
    {
        return _ansiblexNetworkStateService.AddOrUpdateLinkAsync(fromSystemName, toSystemName, cancellationToken);
    }

    public Task RemoveAnsiblexLinkAsync(string fromSystemName, string toSystemName, CancellationToken cancellationToken = default)
    {
        return _ansiblexNetworkStateService.RemoveLinkAsync(fromSystemName, toSystemName, cancellationToken);
    }

    public Task<IReadOnlyList<AnsiblexLinkRecord>> GetAnsiblexSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return _ansiblexNetworkStateService.GetSnapshotAsync(cancellationToken);
    }

    public async Task SaveIndicatorSovFilterAsync()
    {
        await _settingsService.SetAsync(IndicatorSovFilterKeysKey, SelectedIndicatorSovUpgradeKeys.ToList());
        await _settingsService.SetAsync(IndicatorSovFilterConfiguredKey, true);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndicatorSovUpgradeKeys)));
    }

    public async Task SaveOverlaySovFilterAsync()
    {
        await _settingsService.SetAsync(OverlaySovFilterKeysKey, SelectedOverlaySovUpgradeKeys.ToList());
        await _settingsService.SetAsync(OverlaySovFilterConfiguredKey, true);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOverlaySovUpgradeKeys)));
    }

    private void InitializeSovFilterOptions()
    {
        if (IndicatorSovUpgradeOptions.Count > 0 || OverlaySovUpgradeOptions.Count > 0)
        {
            return;
        }

        var known = new[]
        {
            ("Advanced Logistics Network", 0), ("Cynosural Navigation", 0), ("Cynosural Suppression", 0),
            ("Electric Stability Generator", 0), ("Exotic Stability Generator", 0), ("Gamma Stability Generator", 0),
            ("Plasma Stability Generator", 0), ("Supercapital Construction Facilities", 0),
            ("Exploration Detector", 1), ("Exploration Detector", 2), ("Exploration Detector", 3),
            ("Isogen Prospecting Array", 1), ("Isogen Prospecting Array", 2), ("Isogen Prospecting Array", 3),
            ("Major Threat Detection Array", 1), ("Major Threat Detection Array", 2), ("Major Threat Detection Array", 3),
            ("Megacyte Prospecting Array", 1), ("Megacyte Prospecting Array", 2), ("Megacyte Prospecting Array", 3),
            ("Mexallon Prospecting Array", 1), ("Mexallon Prospecting Array", 2), ("Mexallon Prospecting Array", 3),
            ("Minor Threat Detection Array", 1), ("Minor Threat Detection Array", 2), ("Minor Threat Detection Array", 3),
            ("Nocxium Prospecting Array", 1), ("Nocxium Prospecting Array", 2), ("Nocxium Prospecting Array", 3),
            ("Power Monitoring Division", 1), ("Power Monitoring Division", 2), ("Power Monitoring Division", 3),
            ("Pyerite Prospecting Array", 1), ("Pyerite Prospecting Array", 2), ("Pyerite Prospecting Array", 3),
            ("Tritanium Prospecting Array", 1), ("Tritanium Prospecting Array", 2), ("Tritanium Prospecting Array", 3),
            ("Workforce Mecha-Tooling", 1), ("Workforce Mecha-Tooling", 2), ("Workforce Mecha-Tooling", 3),
            ("Zydrine Prospecting Array", 1), ("Zydrine Prospecting Array", 2), ("Zydrine Prospecting Array", 3)
        };

        foreach (var (name, tier) in known)
        {
            var key = BuildSovFilterKey(name, tier);
            var icon = LoadSovIcon(name, tier);
            var display = tier <= 0 ? name : $"{name} {tier}";
            var indicatorOption = new SovUpgradeDisplayOption { Key = key, DisplayName = display, Icon = icon, IsSelected = true };
            indicatorOption.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(SovUpgradeDisplayOption.IsSelected))
                {
                    await SaveIndicatorSovFilterAsync();
                }
            };
            IndicatorSovUpgradeOptions.Add(indicatorOption);

            var overlayOption = new SovUpgradeDisplayOption { Key = key, DisplayName = display, Icon = icon, IsSelected = true };
            overlayOption.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(SovUpgradeDisplayOption.IsSelected))
                {
                    await SaveOverlaySovFilterAsync();
                }
            };
            OverlaySovUpgradeOptions.Add(overlayOption);
        }
    }

    private static void ApplySelectedSovKeys(IEnumerable<SovUpgradeDisplayOption> options, IEnumerable<string> selected, bool configured)
    {
        var set = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            option.IsSelected = !configured || set.Contains(option.Key);
        }
    }

    public async Task SelectAllIndicatorSovFilterAsync()
    {
        foreach (var option in IndicatorSovUpgradeOptions)
        {
            option.IsSelected = true;
        }

        await SaveIndicatorSovFilterAsync();
    }

    public async Task UnselectAllIndicatorSovFilterAsync()
    {
        foreach (var option in IndicatorSovUpgradeOptions)
        {
            option.IsSelected = false;
        }

        await SaveIndicatorSovFilterAsync();
    }

    public async Task SelectAllOverlaySovFilterAsync()
    {
        foreach (var option in OverlaySovUpgradeOptions)
        {
            option.IsSelected = true;
        }

        await SaveOverlaySovFilterAsync();
    }

    public async Task UnselectAllOverlaySovFilterAsync()
    {
        foreach (var option in OverlaySovUpgradeOptions)
        {
            option.IsSelected = false;
        }

        await SaveOverlaySovFilterAsync();
    }

    private static Bitmap? LoadSovIcon(string upgradeName, int tier)
    {
        try
        {
            var fileName = tier <= 0 ? $"{upgradeName}.png" : $"{upgradeName} {tier}.png";
            var uri = new Uri($"avares://HISA/Assets/Icons/SOV Upgrades/{fileName}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSovFilterKey(string upgradeName, int tier)
    {
        return tier <= 0 ? upgradeName : $"{upgradeName}|{tier}";
    }

    public async Task<MapSearchFocus?> ExecuteSearchAsync(MapSearchCandidate? explicitCandidate = null)
    {
        MapSearchCandidate? pick = explicitCandidate;
        if (pick is null)
        {
            var term = MapSearchText.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                return null;
            }

            var candidates = await _mapDataService.SearchAsync(term);
            if (candidates.Count == 0)
            {
                return null;
            }

            pick = PickBestCandidateForMode(candidates);
        }

        if (pick is null)
        {
            return null;
        }

        var preserveZoomInCurrentLayout = SelectedViewMode == MapViewMode.Region && IsCandidatePresentInCurrentRegionLayout(pick);

        if (SelectedViewMode == MapViewMode.Region && pick.RegionId is not null && !preserveZoomInCurrentLayout)
        {
            var targetRegion = _allRegions.FirstOrDefault(r => r.RegionId == pick.RegionId.Value);
            if (targetRegion is not null && (_selectedRegion?.RegionId != targetRegion.RegionId))
            {
                _selectedRegion = targetRegion;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRegion)));
                await _settingsService.SetAsync(RegionIdKey, targetRegion.RegionId);
                await ReloadGraphAsync();
            }
        }

        return new MapSearchFocus
        {
            Kind = pick.Kind,
            RegionId = pick.RegionId,
            ConstellationId = pick.ConstellationId,
            SolarSystemId = pick.SolarSystemId,
            PreferPreserveZoom = preserveZoomInCurrentLayout
        };
    }

    private bool IsCandidatePresentInCurrentRegionLayout(MapSearchCandidate pick)
    {
        if (SelectedViewMode != MapViewMode.Region || CurrentGraph is null || CurrentGraph.Nodes.Count == 0)
        {
            return false;
        }

        return pick.Kind switch
        {
            MapSearchKind.SolarSystem when pick.SolarSystemId is > 0 and var solarSystemId =>
                CurrentGraph.Nodes.Any(n => n.Id == solarSystemId),
            MapSearchKind.Constellation when pick.ConstellationId is > 0 and var constellationId =>
                CurrentGraph.Nodes.Any(n => n.ConstellationId == constellationId),
            MapSearchKind.Region when pick.RegionId is > 0 and var regionId =>
                _selectedRegion?.RegionId == regionId,
            _ => false
        };
    }

    public void ClearSearchSuggestions()
    {
        if (SearchSuggestions.Count == 0)
        {
            return;
        }

        SearchSuggestions.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSearchSuggestions)));
    }

    public async Task OpenRegionFromUniverseRegionsNodeAsync(int regionId)
    {
        var region = _allRegions.FirstOrDefault(r => r.RegionId == regionId);
        if (region is null)
        {
            return;
        }

        _selectedRegion = region;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRegion)));
        await _settingsService.SetAsync(RegionIdKey, region.RegionId);
        await SaveSelectedRegionTokenAsync(region);

        SelectedViewMode = MapViewMode.Region;
        await ReloadGraphAsync();
    }

    private void ApplyRegionFilter()
    {
        _suppressSelectedRegionReload = true;
        try
        {
            var term = RegionSearchText.Trim();
            var filtered = string.IsNullOrWhiteSpace(term)
                ? _allRegions
                : _allRegions.Where(r => r.RegionName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

            var selectedId = SelectedRegion?.RegionId;
            var selectedToken = CreateRegionToken(SelectedRegion);
            Regions.Clear();
            AddFavoriteGroup(filtered);
            AddRegionGroup(RegionOptionKind.Custom, "Custom Regions", filtered);
            AddRegionGroup(RegionOptionKind.Combined, "Combined Regions", filtered);
            AddRegionGroup(RegionOptionKind.Regular, "Regular Regions", filtered);

            if (selectedId is not null)
            {
                SelectedRegion = Regions.FirstOrDefault(r => !r.IsHeader && r.RegionId == selectedId.Value)
                    ?? FindRegionByToken(selectedToken)
                    ?? GetFirstRegularRegionOption()
                    ?? Regions.FirstOrDefault(r => !r.IsHeader);
            }
            else if (SelectedRegion is null)
            {
                SelectedRegion = GetFirstRegularRegionOption() ?? Regions.FirstOrDefault(r => !r.IsHeader);
            }
        }
        finally
        {
            _suppressSelectedRegionReload = false;
        }
    }

    private void AddFavoriteGroup(IReadOnlyCollection<RegionOption> source)
    {
        var items = source
            .Where(r => !r.IsHeader && r.IsFavorite)
            .OrderBy(r => r.RegionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        Regions.Add(new RegionOption
        {
            RegionId = int.MinValue,
            RegionName = "--- Favorites ---",
            Kind = RegionOptionKind.Regular,
            IsHeader = true
        });

        foreach (var item in items)
        {
            Regions.Add(item);
        }
    }

    private void AddRegionGroup(RegionOptionKind kind, string header, IReadOnlyCollection<RegionOption> source)
    {
        var items = source
            .Where(r => !r.IsHeader && !r.IsFavorite && r.Kind == kind)
            .OrderBy(r => r.RegionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        Regions.Add(new RegionOption
        {
            RegionId = int.MinValue + (int)kind,
            RegionName = $"--- {header} ---",
            Kind = kind,
            IsHeader = true
        });

        foreach (var item in items)
        {
            Regions.Add(item);
        }
    }

    private RegionOption? GetFirstRegularRegionOption()
    {
        return Regions.FirstOrDefault(r => !r.IsHeader && r.Kind == RegionOptionKind.Regular);
    }

    private RegionOption? FindRegionByToken(SavedRegionToken? token)
    {
        if (token is null || string.IsNullOrWhiteSpace(token.RegionName))
        {
            return null;
        }

        return _allRegions.FirstOrDefault(r =>
            !r.IsHeader &&
            r.Kind == token.Kind &&
            string.Equals(r.RegionName, token.RegionName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ToggleRegionFavoriteAsync(RegionOption? region)
    {
        if (region is null || region.IsHeader)
        {
            return;
        }

        var key = BuildRegionFavoriteKey(region.Kind, region.RegionName);
        var isFavorite = !_favoriteRegionKeys.Remove(key);
        if (isFavorite)
        {
            _favoriteRegionKeys.Add(key);
        }

        ApplyFavoriteFlags();
        ApplyRegionFilter();
        await SaveFavoriteRegionTokensAsync();
        StatusText = isFavorite
            ? $"Favorited region: {region.RegionName}"
            : $"Removed favorite: {region.RegionName}";
    }

    private Task SaveSelectedRegionTokenAsync(RegionOption? region)
    {
        if (region is null || region.IsHeader)
        {
            return _settingsService.SetAsync<SavedRegionToken?>(RegionTokenKey, null);
        }

        var token = CreateRegionToken(region)!;
        return _settingsService.SetAsync(RegionTokenKey, token);
    }

    private async Task LoadFavoriteRegionTokensAsync()
    {
        _favoriteRegionKeys.Clear();
        var tokens = await _settingsService.GetAsync<List<SavedRegionToken>>(FavoriteRegionTokensKey) ?? [];
        foreach (var token in tokens.Where(t => !string.IsNullOrWhiteSpace(t.RegionName)))
        {
            _favoriteRegionKeys.Add(BuildRegionFavoriteKey(token.Kind, token.RegionName));
        }

        ApplyFavoriteFlags();
    }

    private Task SaveFavoriteRegionTokensAsync()
    {
        var tokens = _allRegions
            .Where(r => !r.IsHeader && r.IsFavorite)
            .OrderBy(r => r.RegionName, StringComparer.OrdinalIgnoreCase)
            .Select(r => CreateRegionToken(r)!)
            .ToList();
        return _settingsService.SetAsync(FavoriteRegionTokensKey, tokens);
    }

    private void ApplyFavoriteFlags()
    {
        foreach (var region in _allRegions)
        {
            region.IsFavorite = !region.IsHeader && _favoriteRegionKeys.Contains(BuildRegionFavoriteKey(region.Kind, region.RegionName));
        }
    }

    private static SavedRegionToken? CreateRegionToken(RegionOption? region)
    {
        if (region is null || region.IsHeader)
        {
            return null;
        }

        return new SavedRegionToken
        {
            RegionName = region.RegionName,
            Kind = region.Kind
        };
    }

    private static string BuildRegionFavoriteKey(RegionOptionKind kind, string regionName)
    {
        return $"{kind}|{regionName.Trim()}";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RebuildJumpRangeOverlay()
    {
        if (CurrentGraph is null || CurrentGraph.Nodes.Count == 0 || _jumpRangeOriginsLyByNodeId.Count == 0)
        {
            if (_jumpRangeInRangeNodeIdsForView.Count > 0)
            {
                _jumpRangeInRangeNodeIdsForView = [];
            }

            _jumpRangeOriginsDisplayForView = [];
            _jumpRangeMembershipByNodeId.Clear();
            _jumpRangeDistancesByNodeId.Clear();

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeOriginNodeIdsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeInRangeNodeIdsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeOriginsDisplayForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeMembershipByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeDistancesByNodeIdForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasJumpRangeOverlay)));
            return;
        }

        var originColorPalette = new uint[]
        {
            0xFF3DE1FF, 0xFFFFC233, 0xFF7BFF4D, 0xFFFF66D6, 0xFF8C7BFF, 0xFFFF8F3D, 0xFF53FFB8, 0xFFFF4D4D
        };
        var sortedOrigins = _jumpRangeOriginsLyByNodeId.Keys.OrderBy(x => x).ToList();
        for (var i = 0; i < sortedOrigins.Count; i++)
        {
            var originId = sortedOrigins[i];
            if (_jumpRangeOriginColorByNodeId.ContainsKey(originId))
            {
                continue;
            }

            var color = originColorPalette
                .FirstOrDefault(c => !_jumpRangeOriginColorByNodeId.Values.Contains(c));
            if (color == 0)
            {
                color = originColorPalette[i % originColorPalette.Length];
            }

            _jumpRangeOriginColorByNodeId[originId] = color;
        }
        _jumpRangeOriginsDisplayForView = sortedOrigins
            .Select(originId => new JumpRangeOriginDisplay
            {
                NodeId = originId,
                SystemName = _jumpRangeOriginsLyByNodeId[originId].SolarSystemName,
                RangeLy = _jumpRangeOriginsLyByNodeId[originId].RangeLy,
                ColorArgb = _jumpRangeOriginColorByNodeId[originId],
                ColorHex = $"#{_jumpRangeOriginColorByNodeId[originId]:X8}"
            })
            .ToList();

        var inRange = new List<long>();
        _jumpRangeMembershipByNodeId.Clear();
        _jumpRangeDistancesByNodeId.Clear();
        if (_jumpRangeOriginsLyByNodeId.Count > 0)
        {
            foreach (var targetNode in CurrentGraph.Nodes)
            {
                foreach (var (originId, origin) in _jumpRangeOriginsLyByNodeId)
                {
                    if (originId == targetNode.Id)
                    {
                        AddJumpRangeDistance(targetNode, origin, 0);
                        if (targetNode.Security is null || targetNode.Security.Value <= 0.45)
                        {
                            inRange.Add(targetNode.Id);
                            if (!_jumpRangeMembershipByNodeId.TryGetValue(targetNode.Id, out var sourceList))
                            {
                                sourceList = [];
                                _jumpRangeMembershipByNodeId[targetNode.Id] = sourceList;
                            }
                            sourceList.Add(originId);
                        }
                        continue;
                    }

                    var distanceLy = GetDistanceLy(origin, targetNode);
                    if (distanceLy < 0)
                    {
                        continue;
                    }

                    var isInRange = distanceLy > 0 && distanceLy < origin.RangeLy;
                    AddJumpRangeDistance(targetNode, origin, distanceLy);
                    if (isInRange && (targetNode.Security is null || targetNode.Security.Value <= 0.45))
                    {
                        inRange.Add(targetNode.Id);
                        if (!_jumpRangeMembershipByNodeId.TryGetValue(targetNode.Id, out var sourceList))
                        {
                            sourceList = [];
                            _jumpRangeMembershipByNodeId[targetNode.Id] = sourceList;
                        }
                        sourceList.Add(originId);
                    }
                }
            }
        }
        foreach (var targetId in _jumpRangeDistancesByNodeId.Keys.ToList())
        {
            _jumpRangeDistancesByNodeId[targetId] = _jumpRangeDistancesByNodeId[targetId]
                .OrderBy(x => x.DistanceLy)
                .ToList();
        }

        _jumpRangeInRangeNodeIdsForView = inRange;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeOriginNodeIdsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeInRangeNodeIdsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeOriginsDisplayForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeMembershipByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeDistancesByNodeIdForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasJumpRangeOverlay)));
    }

    private async Task RebuildActivityCardsAsync(MapGraph? graph)
    {
        var wormholeBySystem = _hubWormholeStateService.Current.ConnectionsBySystemId;
        var incursions = _incursionStateService.Current.Incursions;
        var storms = _stormStateService.Current;
        var allSystemIds = new HashSet<long>(wormholeBySystem.Keys);
        foreach (var inc in incursions)
        {
            allSystemIds.Add(inc.StagingSolarSystemId);
            foreach (var id in inc.InfestedSolarSystems)
            {
                allSystemIds.Add(id);
            }
        }
        foreach (var center in storms.Centers)
        {
            allSystemIds.Add(center.SolarSystemId);
        }
        List<IntelSystemSnapshot> intelSnapshots;
        List<IntelChatReport> intelHistory;
        lock (_intelSnapshotsBySystemId)
        {
            intelSnapshots = _intelSnapshotsBySystemId.Values.ToList();
        }
        lock (_intelReportHistory)
        {
            intelHistory = _intelReportHistory.ToList();
        }
        var listMaxAgeUtc = DateTime.UtcNow - TimeSpan.FromMinutes(IntelListExpiryMinutes);
        intelHistory = intelHistory.Where(x => x.TimestampUtc >= listMaxAgeUtc).ToList();
        foreach (var intel in intelSnapshots)
        {
            allSystemIds.Add(intel.SolarSystemId);
        }

        if (allSystemIds.Count == 0)
        {
            _hubWormholeCardsForView = [];
            _incursionCardsForView = [];
            _stormCardsForView = [];
            _intelCardsForView = [];
            _zkillmailCardsForView = [];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHubWormholeOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIncursionOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStormOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIntelOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoHubWormholeOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIncursionOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoStormOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIntelOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasZkillmailOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoZkillmailOverlayData)));
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var metadataById = await _mapDataService.GetSystemMetadataByIdsAsync(allSystemIds);
        lock (_systemLocationGate)
        {
            foreach (var kvp in metadataById)
            {
                _systemLocationById[kvp.Key] = (
                    string.IsNullOrWhiteSpace(kvp.Value.RegionName) ? "Unknown Region" : kvp.Value.RegionName,
                    string.IsNullOrWhiteSpace(kvp.Value.ConstellationName) ? "Unknown Constellation" : kvp.Value.ConstellationName);
            }
        }
        var visibleNodeIds = graph?.Nodes.Select(n => n.Id).ToHashSet() ?? new HashSet<long>();

        _hubWormholeCardsForView = wormholeBySystem
            .Where(kvp => kvp.Value.Count > 0)
            .Where(kvp => !LimitHubWormholesToCurrentRegion || visibleNodeIds.Contains(kvp.Key))
            .SelectMany(kvp =>
            {
                var systemId = kvp.Key;
                metadataById.TryGetValue(systemId, out var meta);
                return kvp.Value.Select(link =>
                {
                    var hubIsThera = link.HubType == WormholeHubType.Thera;
                    var accent = hubIsThera ? "#44D19D" : "#FFB34D";
                    var hubs = hubIsThera ? "Thera" : "Turnur";
                    var hubLabelColor = hubIsThera ? "#00FF00" : "#FF9C1A";
                    var inSig = string.IsNullOrWhiteSpace(link.InSignature) ? "?" : link.InSignature.Trim().ToUpperInvariant();
                    var outSig = string.IsNullOrWhiteSpace(link.OutSignature) ? "?" : link.OutSignature.Trim().ToUpperInvariant();
                    var expiry = link.ExpiresAtUtc.HasValue ? link.ExpiresAtUtc.Value - now : default;
                    var expiryLabel = !link.ExpiresAtUtc.HasValue
                        ? "Unknown expiry"
                        : expiry <= TimeSpan.Zero ? "Now" : BuildExpiryHoursLabel(expiry);
                    var expiryColor = !link.ExpiresAtUtc.HasValue ? "#BED5F2" : GetExpiryColorHex(expiry);
                    var reportedLabel = link.ReportedAtUtc.HasValue ? $"{link.ReportedAtUtc.Value:yyyy-MM-dd HH:mm} UTC" : "n/a";
                    var updatedLabel = link.LastUpdatedAtUtc.HasValue ? $"{link.LastUpdatedAtUtc.Value:yyyy-MM-dd HH:mm} UTC" : "n/a";

                    return new WormholeOverlayCard
                    {
                        SolarSystemId = systemId,
                        SystemName = meta?.SolarSystemName ?? $"System {systemId}",
                        RegionName = meta?.RegionName ?? "Unknown Region",
                        ConstellationName = meta?.ConstellationName ?? "Unknown Constellation",
                        HubSummary = hubs,
                        HubLabelColorHex = hubLabelColor,
                        ShipSizeSummary = string.IsNullOrWhiteSpace(link.MaxShipSize)
                            ? "?"
                            : link.MaxShipSize.Trim().ToUpperInvariant(),
                        SignatureSummary = $"In {inSig}  |  Out {outSig}",
                        ReportedUpdatedSummary = $"Reported {reportedLabel}  |  Updated {updatedLabel}",
                        ExpirySummary = expiryLabel,
                        ExpiryColorHex = expiryColor,
                        ConnectionCount = 1,
                        AccentHex = accent
                    };
                });
            })
            .OrderBy(c => c.SystemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _incursionCardsForView = incursions
            .Where(i => !LimitIncursionsToCurrentRegion || visibleNodeIds.Contains(i.StagingSolarSystemId))
            .Select(i =>
            {
                metadataById.TryGetValue(i.StagingSolarSystemId, out var stagingMeta);
                var affectedKnown = i.InfestedSolarSystems.Count(id => metadataById.ContainsKey(id));
                var isMobilizing = i.State.Equals("mobilizing", StringComparison.OrdinalIgnoreCase);
                var isWithdrawing = i.State.Equals("withdrawing", StringComparison.OrdinalIgnoreCase);
                var isEstablished = i.State.Equals("established", StringComparison.OrdinalIgnoreCase);
                var accent = isMobilizing ? "#5BA8FF" : isWithdrawing ? "#FFA35A" : i.HasBoss ? "#FF6A7D" : "#A77BFF";
                var stateColor = isMobilizing ? "#7CC2FF" : isWithdrawing ? "#FFB36B" : isEstablished ? "#C390FF" : "#B7A8D9";
                var typeColor = i.Type.Contains("assault", StringComparison.OrdinalIgnoreCase)
                    ? "#FF8F6A"
                    : i.Type.Contains("vanguard", StringComparison.OrdinalIgnoreCase)
                        ? "#72D3FF"
                        : "#C8A9FF";
                var bossColor = i.HasBoss ? "#FF6A7D" : "#7E8EA8";
                return new IncursionOverlayCard
                {
                    SolarSystemId = i.StagingSolarSystemId,
                    StagingSystemName = stagingMeta?.SolarSystemName ?? $"System {i.StagingSolarSystemId}",
                    ConstellationName = stagingMeta?.ConstellationName ?? $"Constellation {i.ConstellationId}",
                    RegionName = stagingMeta?.RegionName ?? "Unknown Region",
                    TypeLabel = i.Type,
                    StateLabel = i.State,
                    StateColorHex = stateColor,
                    FactionLabel = $"Faction ID: {i.FactionId}",
                    BossLabel = i.HasBoss ? "Mothership: Present" : "Mothership: Not present",
                    InfluenceLabel = $"Influence: {i.Influence:P0}",
                    AffectedSystemsLabel = $"Systems: {affectedKnown}/{i.InfestedSolarSystems.Count}",
                    TypeColorHex = typeColor,
                    BossColorHex = bossColor,
                    AccentHex = accent
                };
            })
            .OrderBy(c => c.StagingSystemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _stormCardsForView = storms.Centers
            .Where(center => !LimitStormsToCurrentRegion || visibleNodeIds.Contains(center.SolarSystemId))
            .Select(center =>
            {
                metadataById.TryGetValue(center.SolarSystemId, out var centerMeta);
                var effects = storms.EffectsBySystemId
                    .Where(kvp => kvp.Value.Any(e => e.CenterSolarSystemId == center.SolarSystemId))
                    .SelectMany(kvp => kvp.Value.Where(e => e.CenterSolarSystemId == center.SolarSystemId))
                    .ToList();
                var weakCount = effects.Count(e => e.Strength == StormStrength.Weak);
                var strongCount = effects.Count(e => e.Strength == StormStrength.Strong);
                var centerCount = effects.Count(e => e.Strength == StormStrength.Center);
                var totalSystems = effects.Count;
                var (typeLabel, typeColor) = GetStormTypeDisplay(center.Type);
                return new StormOverlayCard
                {
                    SolarSystemId = center.SolarSystemId,
                    CenterSystemName = centerMeta?.SolarSystemName ?? center.DisplayName ?? $"System {center.SolarSystemId}",
                    ConstellationName = centerMeta?.ConstellationName ?? "Unknown Constellation",
                    RegionName = centerMeta?.RegionName ?? "Unknown Region",
                    StormTypeLabel = typeLabel,
                    StormTypeColorHex = typeColor,
                    CoverageSummary = $"Affected systems: {totalSystems}",
                    StrengthSummary = $"Center {centerCount} | Strong {strongCount} | Weak {weakCount}",
                    ReportedSummary = center.ReportedAtUtc.HasValue
                        ? $"Reported {center.ReportedAtUtc.Value:yyyy-MM-dd HH:mm} UTC"
                        : "Reported n/a",
                    AccentHex = typeColor
                };
            })
            .OrderBy(c => c.CenterSystemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var graphNodeByName = (graph?.Nodes ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var snapshotByName = intelSnapshots
            .GroupBy(s => s.SolarSystemName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.LastUpdatedUtc).First(), StringComparer.OrdinalIgnoreCase);

        var (intelCards, zkillCards) = await Task.Run(() =>
        {
            var nextIntelCards = intelHistory
                .Where(r => !(string.Equals(r.ChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase)
                              || r.SourceFilePath.StartsWith("api://zkillboard", StringComparison.OrdinalIgnoreCase)))
                .Select(r =>
                {
                    var systemName = r.Systems.FirstOrDefault() ?? "Unknown";
                    long solarSystemId = 0;
                    string constellationName = "Unknown Constellation";
                    string regionName = "Unknown Region";
                    int? constellationId = null;
                    int? regionId = null;

                    if (graphNodeByName.TryGetValue(systemName, out var node))
                    {
                        solarSystemId = node.Id;
                        constellationName = string.IsNullOrWhiteSpace(node.ConstellationName) ? constellationName : node.ConstellationName;
                        regionName = string.IsNullOrWhiteSpace(node.RegionName) ? regionName : node.RegionName;
                        constellationId = node.ConstellationId;
                        regionId = node.RegionId;
                    }
                    else if (snapshotByName.TryGetValue(systemName, out var snapshotRef))
                    {
                        solarSystemId = snapshotRef.SolarSystemId;
                        if (metadataById.TryGetValue(snapshotRef.SolarSystemId, out var metaFromSnapshot))
                        {
                            constellationName = metaFromSnapshot.ConstellationName ?? constellationName;
                            regionName = metaFromSnapshot.RegionName ?? regionName;
                            constellationId = metaFromSnapshot.ConstellationId;
                            regionId = metaFromSnapshot.RegionId;
                        }
                    }

                    if (LimitIntelReportsToCurrentRegion && solarSystemId > 0 && !visibleNodeIds.Contains(solarSystemId))
                    {
                        return null;
                    }

                    var age = DateTime.UtcNow - r.TimestampUtc;
                    if (age < TimeSpan.Zero)
                    {
                        age = TimeSpan.Zero;
                    }

                    var shipSummary = r.ReportedShipNames.Count > 0
                        ? string.Join(", ", r.ReportedShipNames.Select(CapitalizeFirstLetter).Distinct(StringComparer.OrdinalIgnoreCase))
                        : r.ShipClasses.Count > 0
                            ? string.Join(", ", r.ShipClasses.Select(x => x.ToString()))
                        : "Unknown";
                    var maxShipTier = GetMaxShipThreatTier(r.ShipClasses);
                    var shipBadgeColors = GetThreatBadgeColors(maxShipTier / 8.0);
                    var hostileScore = Math.Max(0, r.ReportedHostileCount > 0 ? r.ReportedHostileCount : r.ReportedHostileNames.Count);
                    var hostileBadgeColors = GetHostileBadgeColors(hostileScore);
                    var hostileCards = BuildIntelHostileCards(r.ReportedHostileNames, r.ReportedShipNames, r.ReportedShipTypeIds, r.ShipClasses, applyCachedIdentityData: false);
                    var shipsSummary = BuildIntelShipsSummary(r.ReportedShipNames, r.ReportedShipTypeIds, r.ShipClasses);

                    return new IntelOverlayCard
                    {
                        SortTimestampUtc = r.TimestampUtc,
                        LastUpdatedUtc = r.TimestampUtc,
                        SolarSystemId = solarSystemId,
                        SystemName = systemName,
                        ConstellationName = constellationName,
                        RegionName = regionName,
                        ConstellationId = constellationId,
                        RegionId = regionId,
                        ChannelName = r.ChannelName,
                        ReporterName = r.ReporterName,
                        AgeSummary = FormatOverlayAge(age),
                        MessageText = r.MessageText,
                        Hostiles = hostileCards,
                        ShipsSummary = shipsSummary,
                        ShipClassSummary = shipSummary,
                        HostileCount = hostileScore,
                        ShipBadgeBackgroundHex = shipBadgeColors.BackgroundHex,
                        ShipBadgeBorderHex = shipBadgeColors.BorderHex,
                        HostileBadgeBackgroundHex = hostileBadgeColors.BackgroundHex,
                        HostileBadgeBorderHex = hostileBadgeColors.BorderHex,
                        AccentHex = r.IsClear ? "#6FE38E" : "#FFB347"
                    };
                })
                .Where(c => c is not null)
                .Select(c => c!)
                .OrderByDescending(c => c.SortTimestampUtc)
                .ThenBy(c => c.SystemName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxIntelOverlayCards)
                .ToList();

            var nextZkillCards = intelHistory
                .Where(r => string.Equals(r.ChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase)
                            || r.SourceFilePath.StartsWith("api://zkillboard", StringComparison.OrdinalIgnoreCase))
                .Where(ShouldShowZkillmailReport)
                .Select(r => BuildZkillmailOverlayCard(
                    r,
                    graphNodeByName,
                    visibleNodeIds,
                    snapshotByName,
                    limitToVisibleRegion: LimitZkillmailsToCurrentRegion,
                    applyCachedIdentityData: false))
                .Where(x => x is not null)
                .Select(x => x!)
                .OrderByDescending(x => x.TimestampUtc)
                .Take(MaxIntelOverlayCards)
                .ToList();

            return (nextIntelCards, nextZkillCards);
        });

        _intelCardsForView = intelCards;
        _zkillmailCardsForView = zkillCards;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHubWormholeOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIncursionOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStormOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIntelOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoHubWormholeOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIncursionOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoStormOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIntelOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasZkillmailOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoZkillmailOverlayData)));

        _ = ResolveIntelCharacterIdsAsync();
        _ = EnsureShipImagesForIntelCardsAsync();
        _ = EnsureIntelIdentityAssetsAsync();
        _ = EnsureZkillIdentityAssetsAsync();
    }

    private async Task EnsureIntelIdentityAssetsAsync()
    {
        var characterIds = _intelCardsForView
            .SelectMany(c => c.Hostiles)
            .Select(h => h.CharacterId)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (characterIds.Count == 0)
        {
            return;
        }

        var throttler = new SemaphoreSlim(MaxParallelImageRequests);
        var characterTasks = characterIds.Select(async characterId =>
        {
            await throttler.WaitAsync();
            try
            {
                await EnsureIntelHostileImagesAsync(characterId);
            }
            finally
            {
                throttler.Release();
            }
        }).ToList();

        await Task.WhenAll(characterTasks);
    }

    private async Task EnsureZkillIdentityAssetsAsync()
    {
        var characterIds = _zkillmailCardsForView
            .SelectMany(c =>
                new[] { c.Victim.CharacterId }
                    .Concat(c.VisibleAttackers.Select(a => a.CharacterId)))
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var throttler = new SemaphoreSlim(MaxParallelImageRequests);
        var characterTasks = characterIds.Select(async characterId =>
        {
            await throttler.WaitAsync();
            try
            {
                await EnsureIntelHostileImagesAsync(characterId);
            }
            finally
            {
                throttler.Release();
            }
        }).ToList();

        var corporationIds = _zkillmailCardsForView
            .SelectMany(c =>
                new[] { c.Victim.CorporationId }
                    .Concat(c.VisibleAttackers.Select(a => a.CorporationId)))
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var corporationTasks = corporationIds.Select(async corporationId =>
        {
            await throttler.WaitAsync();
            try
            {
                await GetOrLoadCachedBitmapAsync(
                    IntelCorporationBitmapCache,
                    corporationId,
                    "corporations",
                    $"{corporationId}.png",
                    $"https://images.evetech.net/corporations/{corporationId}/logo?tenant=tranquility&size=64");
                await GetOrLoadCorporationTickerAsync(corporationId);
            }
            finally
            {
                throttler.Release();
            }
        }).ToList();

        var allianceIds = _zkillmailCardsForView
            .SelectMany(c =>
                new[] { c.Victim.AllianceId }
                    .Concat(c.VisibleAttackers.Select(a => a.AllianceId)))
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var allianceTasks = allianceIds.Select(async allianceId =>
        {
            await throttler.WaitAsync();
            try
            {
                await GetOrLoadCachedBitmapAsync(
                    IntelAllianceBitmapCache,
                    allianceId,
                    "alliances",
                    $"{allianceId}.png",
                    $"https://images.evetech.net/alliances/{allianceId}/logo?tenant=tranquility&size=64");
                await GetOrLoadAllianceTickerAsync(allianceId);
            }
            finally
            {
                throttler.Release();
            }
        }).ToList();

        await Task.WhenAll(characterTasks.Concat(corporationTasks).Concat(allianceTasks));

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var card in _zkillmailCardsForView)
            {
                ApplyCachedIntelIdentityData(card.Victim);
                foreach (var attacker in card.VisibleAttackers)
                {
                    ApplyCachedIntelIdentityData(attacker);
                }
            }

            RequestOverlayCardsUiRefresh(intelCardsChanged: false, zkillCardsChanged: true);
        });
    }

    private List<IntelOverlayShipSummaryCard> BuildIntelShipsSummary(
        IReadOnlyList<string> shipNames,
        IReadOnlyList<int> shipTypeIds,
        IReadOnlyList<IntelShipClass> shipClasses)
    {
        return BuildGroupedShipSummaries(shipNames, shipTypeIds, shipClasses)
            .Select(summary =>
            {
                Bitmap? bitmap = null;
                if (summary.ShipTypeId is { } id && IntelShipBitmapCache.TryGetValue(id, out var cachedBitmap))
                {
                    bitmap = cachedBitmap;
                }

                return new IntelOverlayShipSummaryCard
                {
                    ShipName = summary.ShipName,
                    Count = summary.Count,
                    ThreatTier = summary.ThreatTier,
                    ShipTypeId = summary.ShipTypeId,
                    ShipIconKey = summary.ShipIconKey,
                    ShipBitmap = bitmap
                };
            })
            .ToList();
    }

    private async Task EnsureShipImagesForIntelCardsAsync()
    {
        var typeIds = _intelCardsForView
            .SelectMany(c => c.Hostiles)
            .Select(h => h.ShipTypeId)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Concat(_intelCardsForView.SelectMany(c => c.ShipsSummary).Select(s => s.ShipTypeId).Where(id => id is > 0).Select(id => id!.Value))
            .Concat(_zkillmailCardsForView.Select(c => c.Victim.ShipTypeId).Where(id => id is > 0).Select(id => id!.Value))
            .Concat(_zkillmailCardsForView.SelectMany(c => c.VisibleAttackers).Select(a => a.ShipTypeId).Where(id => id is > 0).Select(id => id!.Value))
            .Concat(_zkillmailCardsForView.SelectMany(c => c.ShipsSummary).Select(s => s.ShipTypeId).Where(id => id is > 0).Select(id => id!.Value))
            .Distinct()
            .ToList();
        if (typeIds.Count == 0)
        {
            return;
        }

        foreach (var typeId in typeIds)
        {
            if (IntelShipBitmapCache.ContainsKey(typeId))
            {
                continue;
            }

            var bitmap = await GetOrLoadCachedBitmapAsync(
                IntelShipBitmapCache,
                typeId,
                "ships",
                $"{typeId}.png",
                $"https://images.evetech.net/types/{typeId}/icon?tenant=tranquility&size=64");
            if (bitmap is null)
            {
                continue;
            }

            foreach (var card in _intelCardsForView)
            {
                foreach (var hostile in card.Hostiles)
                {
                    if (hostile.ShipTypeId == typeId)
                    {
                        hostile.ShipBitmap = bitmap;
                    }
                }
                foreach (var ship in card.ShipsSummary)
                {
                    if (ship.ShipTypeId == typeId)
                    {
                        ship.ShipBitmap = bitmap;
                    }
                }
            }

            foreach (var card in _zkillmailCardsForView)
            {
                if (card.Victim.ShipTypeId == typeId)
                {
                    card.Victim.ShipBitmap = bitmap;
                }

                foreach (var attacker in card.VisibleAttackers)
                {
                    if (attacker.ShipTypeId == typeId)
                    {
                        attacker.ShipBitmap = bitmap;
                    }
                }

                foreach (var ship in card.ShipsSummary)
                {
                    if (ship.ShipTypeId == typeId)
                    {
                        ship.ShipBitmap = bitmap;
                    }
                }
            }
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailCardsForView)));
    }

    private List<IntelOverlayHostileCard> BuildIntelHostileCards(
        IReadOnlyList<string> hostilePilotNames,
        IReadOnlyList<string> shipNames,
        IReadOnlyList<int> shipTypeIds,
        IReadOnlyList<IntelShipClass> shipClasses,
        bool applyCachedIdentityData = true)
    {
        var names = hostilePilotNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => !_invalidHostilePilotNames.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return [];
        }

        var rankedShipClasses = (shipClasses.Count > 0 ? shipClasses : [IntelShipClass.Unknown])
            .OrderByDescending(GetShipClassThreatTier)
            .ToList();
        var shipNamesExpanded = shipNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();

        var result = new List<IntelOverlayHostileCard>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var shipClass = rankedShipClasses[Math.Min(i, rankedShipClasses.Count - 1)];
            var shipName = shipNamesExpanded.Count > 0
                ? shipNamesExpanded[Math.Min(i, shipNamesExpanded.Count - 1)]
                : ShipClassToDisplayName(shipClass);
            var shipTypeId = shipTypeIds.Count > 0 ? shipTypeIds[Math.Min(i, shipTypeIds.Count - 1)] : (int?)null;
            var card = new IntelOverlayHostileCard
            {
                Name = names[i],
                CharacterId = _characterIdByName.TryGetValue(names[i], out var characterId) && characterId > 0
                    ? characterId
                    : null,
                ShipTypeId = shipTypeId,
                ShipBitmap = shipTypeId is { } id && IntelShipBitmapCache.TryGetValue(id, out var cachedShip) ? cachedShip : null,
                ShipDisplayName = NormalizeShipDisplayName(shipName),
                ShipIconKey = ShipClassToOverlayIconKey(shipClass)
            };
            if (applyCachedIdentityData)
            {
                ApplyCachedIntelIdentityData(card);
            }

            result.Add(card);
        }

        return result;
    }

    private IntelOverlayHostileCard BuildZkillVictimCard(
        IntelKillmailDetails? killmail,
        IReadOnlyList<string> shipNames,
        IReadOnlyList<int> shipTypeIds,
        IReadOnlyList<IntelShipClass> shipClasses,
        bool applyCachedIdentityData = true)
    {
        var shipName = shipNames.FirstOrDefault() ?? "Unknown";
        var shipTypeId = killmail?.VictimShipTypeId ?? shipTypeIds.FirstOrDefault();
        var shipClass = shipClasses.FirstOrDefault();
        var card = new IntelOverlayHostileCard
        {
            Name = !string.IsNullOrWhiteSpace(killmail?.VictimName)
                ? killmail!.VictimName
                : killmail?.VictimCharacterId is { } c
                ? (IntelCharacterNamesById.TryGetValue(c, out var cachedName) ? cachedName : $"Pilot {c}")
                : "Unknown Victim",
            CharacterId = killmail?.VictimCharacterId,
            CorporationId = killmail is { VictimCorporationId: > 0 } ? killmail.VictimCorporationId : null,
            AllianceId = killmail?.VictimAllianceId,
            CorporationTicker = string.Empty,
            AllianceTicker = string.Empty,
            ShipTypeId = shipTypeId > 0 ? shipTypeId : null,
            ShipDisplayName = NormalizeShipDisplayName(shipName),
            ShipIconKey = ShipClassToOverlayIconKey(shipClass)
        };
        if (applyCachedIdentityData)
        {
            ApplyCachedIntelIdentityData(card);
        }

        return card;
    }

    private List<IntelOverlayHostileCard> BuildZkillAttackerCards(IntelKillmailDetails? killmail, bool applyCachedIdentityData = true)
    {
        if (killmail?.Attackers is null || killmail.Attackers.Count == 0)
        {
            return [];
        }

        var result = new List<IntelOverlayHostileCard>(killmail.Attackers.Count);
        foreach (var attacker in killmail.Attackers)
        {
            var shipName = attacker.ShipTypeId is { } id ? $"Type {id}" : "Unknown";
            var card = new IntelOverlayHostileCard
            {
                Name = attacker.CharacterId is { } characterId
                    ? (IntelCharacterNamesById.TryGetValue(characterId, out var cachedName) ? cachedName : $"Pilot {characterId}")
                    : (!string.IsNullOrWhiteSpace(attacker.Name) ? attacker.Name : "Unknown Attacker"),
                CharacterId = attacker.CharacterId,
                CorporationId = attacker.CorporationId > 0 ? attacker.CorporationId : null,
                AllianceId = attacker.AllianceId,
                CorporationTicker = string.Empty,
                AllianceTicker = string.Empty,
                ShipTypeId = attacker.ShipTypeId,
                ShipDisplayName = shipName,
                ShipIconKey = "crosshair"
            };
            if (applyCachedIdentityData)
            {
                ApplyCachedIntelIdentityData(card);
            }

            result.Add(card);
        }

        return result;
    }

    private ZkillmailOverlayCard? BuildZkillmailOverlayCard(
        IntelChatReport report,
        IReadOnlyDictionary<string, MapNode> graphNodeByName,
        IReadOnlySet<long> visibleNodeIds,
        IReadOnlyDictionary<string, IntelSystemSnapshot> snapshotByName,
        bool limitToVisibleRegion,
        bool applyCachedIdentityData)
    {
        if (!ShouldShowZkillmailReport(report))
        {
            return null;
        }

        var systemName = report.Systems.FirstOrDefault() ?? "Unknown";
        long solarSystemId = 0;
        var regionName = "Unknown Region";
        var constellationName = "Unknown Constellation";
        if (graphNodeByName.TryGetValue(systemName, out var node))
        {
            solarSystemId = node.Id;
            regionName = string.IsNullOrWhiteSpace(node.RegionName) ? regionName : node.RegionName;
            constellationName = string.IsNullOrWhiteSpace(node.ConstellationName) ? constellationName : node.ConstellationName;
        }
        else if (snapshotByName.TryGetValue(systemName, out var snapshotRef))
        {
            solarSystemId = snapshotRef.SolarSystemId;
            lock (_systemLocationGate)
            {
                if (_systemLocationById.TryGetValue(solarSystemId, out var location))
                {
                    regionName = location.RegionName;
                    constellationName = location.ConstellationName;
                }
            }
        }

        if (limitToVisibleRegion && (solarSystemId <= 0 || !visibleNodeIds.Contains(solarSystemId)))
        {
            return null;
        }

        var age = DateTime.UtcNow - report.TimestampUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var shipSummary = report.ReportedShipNames.Count > 0
            ? string.Join(", ", report.ReportedShipNames.Select(CapitalizeFirstLetter).Distinct(StringComparer.OrdinalIgnoreCase))
            : "Unknown";
        var killmail = report.Killmail;
        var victim = BuildZkillVictimCard(killmail, report.ReportedShipNames, report.ReportedShipTypeIds, report.ShipClasses, applyCachedIdentityData);
        var attackers = BuildZkillAttackerCards(killmail, applyCachedIdentityData);
        var visibleAttackers = attackers.Take(4).ToList();
        var hiddenAttackers = Math.Max(0, attackers.Count - visibleAttackers.Count);
        var shipsSummary = BuildZkillShipsSummary(killmail, report.ReportedShipNames, report.ReportedShipTypeIds, report.ShipClasses);
        var (iskBg, iskBorder) = GetIskLossBadgeColors(killmail?.TotalValue ?? 0m);

        return new ZkillmailOverlayCard
        {
            TimestampUtc = report.TimestampUtc,
            SolarSystemId = solarSystemId,
            AgeSummary = FormatOverlayAge(age),
            KillmailUrl = killmail?.Url ?? string.Empty,
            SystemName = systemName,
            RegionName = regionName,
            ConstellationName = constellationName,
            ShipSummary = shipSummary,
            HostileCount = Math.Max(1, report.ReportedHostileCount),
            MessageText = report.MessageText,
            IskLostLabel = $"ISK Lost: {FormatCompactIsk(killmail?.TotalValue ?? 0m)}",
            IskLostBackgroundHex = iskBg,
            IskLostBorderHex = iskBorder,
            Victim = victim,
            VisibleAttackers = visibleAttackers,
            HiddenAttackerCount = hiddenAttackers,
            ShipsSummary = shipsSummary
        };
    }

    private async Task LoadKnownSpaceSystemCacheAsync(CancellationToken cancellationToken = default)
    {
        lock (_knownSpaceGate)
        {
            if (_knownSpaceSystemIds.Count > 0 && _knownSpaceSystemIdByName.Count > 0)
            {
                return;
            }
        }

        var universeGraph = await _mapDataService.GetUniverseGraphAsync(MapCoordinateMode.SdePlanarXY, cancellationToken);
        var systemIds = universeGraph.Nodes
            .Where(n => n.Id > 0)
            .Select(n => n.Id)
            .Distinct()
            .ToList();
        var metadataById = await _mapDataService.GetSystemMetadataByIdsAsync(systemIds, cancellationToken);
        var nextIds = new HashSet<long>();
        var nextByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in universeGraph.Nodes)
        {
            if (node.Id <= 0 || string.IsNullOrWhiteSpace(node.Name))
            {
                continue;
            }

            metadataById.TryGetValue(node.Id, out var metadata);
            var regionName = !string.IsNullOrWhiteSpace(node.RegionName)
                ? node.RegionName
                : metadata?.RegionName;
            if (IsExcludedKnownSpaceRegion(regionName) || IsExcludedKnownSpaceSystem(node.Name))
            {
                continue;
            }

            nextIds.Add(node.Id);
            nextByName[node.Name] = node.Id;
        }

        lock (_knownSpaceGate)
        {
            _knownSpaceSystemIds.Clear();
            foreach (var id in nextIds)
            {
                _knownSpaceSystemIds.Add(id);
            }

            _knownSpaceSystemIdByName.Clear();
            foreach (var pair in nextByName)
            {
                _knownSpaceSystemIdByName[pair.Key] = pair.Value;
            }
        }
    }

    private bool ShouldShowZkillmailReport(IntelChatReport report)
    {
        if (!HideZkillmailsOutsideKnownSpace)
        {
            return true;
        }

        return IsKnownSpaceSystem(report.Systems.FirstOrDefault());
    }

    private bool IsKnownSpaceSystem(string? systemName)
    {
        if (string.IsNullOrWhiteSpace(systemName))
        {
            return false;
        }

        var trimmed = systemName.Trim();
        if (IsExcludedKnownSpaceSystem(trimmed))
        {
            return false;
        }

        lock (_knownSpaceGate)
        {
            if (_knownSpaceSystemIdByName.Count == 0)
            {
                return true;
            }

            return _knownSpaceSystemIdByName.ContainsKey(trimmed);
        }
    }

    private static bool IsExcludedKnownSpaceSystem(string systemName)
    {
        return string.Equals(systemName, "Thera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(systemName, "Turnur", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedKnownSpaceRegion(string? regionName)
    {
        return string.Equals(regionName, "Pochven", StringComparison.OrdinalIgnoreCase)
            || string.Equals(regionName, "Yasna Zakh", StringComparison.OrdinalIgnoreCase);
    }

    private IntelOverlayCard? BuildIntelOverlayCard(
        IntelChatReport report,
        IReadOnlyDictionary<string, MapNode> graphNodeByName,
        IReadOnlySet<long> visibleNodeIds,
        IReadOnlyDictionary<string, IntelSystemSnapshot> snapshotByName,
        bool limitToVisibleRegion,
        bool applyCachedIdentityData)
    {
        var systemName = report.Systems.FirstOrDefault() ?? "Unknown";
        long solarSystemId = 0;
        string constellationName = "Unknown Constellation";
        string regionName = "Unknown Region";
        int? constellationId = null;
        int? regionId = null;

        if (graphNodeByName.TryGetValue(systemName, out var node))
        {
            solarSystemId = node.Id;
            constellationName = string.IsNullOrWhiteSpace(node.ConstellationName) ? constellationName : node.ConstellationName;
            regionName = string.IsNullOrWhiteSpace(node.RegionName) ? regionName : node.RegionName;
            constellationId = node.ConstellationId;
            regionId = node.RegionId;
        }
        else if (snapshotByName.TryGetValue(systemName, out var snapshotRef))
        {
            solarSystemId = snapshotRef.SolarSystemId;
            lock (_systemLocationGate)
            {
                if (_systemLocationById.TryGetValue(solarSystemId, out var location))
                {
                    regionName = location.RegionName;
                    constellationName = location.ConstellationName;
                }
            }
        }

        if (limitToVisibleRegion && solarSystemId > 0 && !visibleNodeIds.Contains(solarSystemId))
        {
            return null;
        }

        var age = DateTime.UtcNow - report.TimestampUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var shipSummary = report.ReportedShipNames.Count > 0
            ? string.Join(", ", report.ReportedShipNames.Select(CapitalizeFirstLetter).Distinct(StringComparer.OrdinalIgnoreCase))
            : report.ShipClasses.Count > 0
                ? string.Join(", ", report.ShipClasses.Select(x => x.ToString()))
                : "Unknown";
        var maxShipTier = GetMaxShipThreatTier(report.ShipClasses);
        var shipBadgeColors = GetThreatBadgeColors(maxShipTier / 8.0);
        var hostileScore = Math.Max(0, report.ReportedHostileCount > 0 ? report.ReportedHostileCount : report.ReportedHostileNames.Count);
        var hostileBadgeColors = GetHostileBadgeColors(hostileScore);
        var hostileCards = BuildIntelHostileCards(report.ReportedHostileNames, report.ReportedShipNames, report.ReportedShipTypeIds, report.ShipClasses, applyCachedIdentityData);
        var shipsSummary = BuildIntelShipsSummary(report.ReportedShipNames, report.ReportedShipTypeIds, report.ShipClasses);

        return new IntelOverlayCard
        {
            SortTimestampUtc = report.TimestampUtc,
            LastUpdatedUtc = report.TimestampUtc,
            SolarSystemId = solarSystemId,
            SystemName = systemName,
            ConstellationName = constellationName,
            RegionName = regionName,
            ConstellationId = constellationId,
            RegionId = regionId,
            ChannelName = report.ChannelName,
            ReporterName = report.ReporterName,
            AgeSummary = FormatOverlayAge(age),
            MessageText = report.MessageText,
            Hostiles = hostileCards,
            ShipsSummary = shipsSummary,
            ShipClassSummary = shipSummary,
            HostileCount = hostileScore,
            ShipBadgeBackgroundHex = shipBadgeColors.BackgroundHex,
            ShipBadgeBorderHex = shipBadgeColors.BorderHex,
            HostileBadgeBackgroundHex = hostileBadgeColors.BackgroundHex,
            HostileBadgeBorderHex = hostileBadgeColors.BorderHex,
            AccentHex = report.IsClear ? "#6FE38E" : "#FFB347"
        };
    }

    private static Dictionary<string, MapNode> BuildGraphNodeByNameLookup(MapGraph? graph)
    {
        if (graph is null || graph.Nodes.Count == 0)
        {
            return new Dictionary<string, MapNode>(StringComparer.OrdinalIgnoreCase);
        }

        return graph.Nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private List<IntelOverlayShipSummaryCard> BuildZkillShipsSummary(
        IntelKillmailDetails? killmail,
        IReadOnlyList<string> shipNames,
        IReadOnlyList<int> shipTypeIds,
        IReadOnlyList<IntelShipClass> shipClasses)
    {
        if (killmail?.Attackers is { Count: > 0 })
        {
            var names = killmail.Attackers
                .Where(x => x.ShipTypeId is > 0)
                .Select(x => $"Type {x.ShipTypeId!.Value}")
                .ToList();
            var typeIds = killmail.Attackers
                .Where(x => x.ShipTypeId is > 0)
                .Select(x => x.ShipTypeId!.Value)
                .ToList();
            var classes = Enumerable.Repeat(IntelShipClass.Unknown, typeIds.Count).ToList();
            if (names.Count > 0)
            {
                return BuildIntelShipsSummary(names, typeIds, classes);
            }
        }

        return BuildIntelShipsSummary(shipNames, shipTypeIds, shipClasses);
    }

    private void ApplyCachedIntelIdentityData(IntelOverlayHostileCard card)
    {
        if (card.CharacterId is { } characterId)
        {
            if (IntelPortraitBitmapCache.TryGetValue(characterId, out var portrait))
            {
                card.PortraitBitmap = portrait;
            }

            if (IntelAffiliationsByCharacterId.TryGetValue(characterId, out var affiliation))
            {
                card.CorporationId = affiliation.CorpId;
                card.AllianceId = affiliation.AllianceId;
            }
        }

        if (card.CorporationId is { } corpId)
        {
            if (IntelCorporationBitmapCache.TryGetValue(corpId, out var corp))
            {
                card.CorporationBitmap = corp;
            }

            if (IntelCorporationTickersById.TryGetValue(corpId, out var corporationTicker))
            {
                card.CorporationTicker = corporationTicker;
            }
        }

        if (card.AllianceId is { } allianceId)
        {
            if (IntelAllianceBitmapCache.TryGetValue(allianceId, out var alliance))
            {
                card.AllianceBitmap = alliance;
            }

            if (IntelAllianceTickersById.TryGetValue(allianceId, out var allianceTicker))
            {
                card.AllianceTicker = allianceTicker;
            }
        }

        if (card.ShipTypeId is { } shipTypeId && IntelShipBitmapCache.TryGetValue(shipTypeId, out var shipBitmap))
        {
            card.ShipBitmap = shipBitmap;
        }
    }

    private static int GetShipClassThreatTier(IntelShipClass shipClass)
    {
        return shipClass switch
        {
            IntelShipClass.Titan => 8,
            IntelShipClass.Supercapital => 7,
            IntelShipClass.Capital => 6,
            IntelShipClass.Battleship => 5,
            IntelShipClass.Battlecruiser => 4,
            IntelShipClass.Cruiser => 3,
            IntelShipClass.Destroyer => 2,
            IntelShipClass.Frigate => 1,
            _ => 0
        };
    }

    private static string ShipClassToDisplayName(IntelShipClass shipClass)
    {
        return shipClass switch
        {
            IntelShipClass.Battlecruiser => "Battlecruiser",
            IntelShipClass.Supercapital => "Supercapital",
            IntelShipClass.IndustrialCommand => "Industrial Command",
            IntelShipClass.MiningFrigate => "Mining Frigate",
            IntelShipClass.MiningBarge => "Mining Barge",
            IntelShipClass.Capsule => "Capsule",
            IntelShipClass.Rookie => "Rookie",
            IntelShipClass.Unknown => "Unknown",
            _ => shipClass.ToString()
        };
    }

    private static string CapitalizeFirstLetter(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0
            ? string.Empty
            : $"{char.ToUpperInvariant(trimmed[0])}{trimmed[1..]}";
    }

    private static string NormalizeShipDisplayName(string value)
    {
        return SingularizeShipName(CapitalizeFirstLetter(value));
    }

    private static int GetShipRolePriority(string shipName)
    {
        if (string.IsNullOrWhiteSpace(shipName))
        {
            return 0;
        }

        if (shipName.Contains("cyno", StringComparison.OrdinalIgnoreCase) ||
            shipName.Contains("recon", StringComparison.OrdinalIgnoreCase) ||
            ReconPriorityShipNames.Contains(shipName))
        {
            return 2;
        }

        if (shipName.Contains("hictor", StringComparison.OrdinalIgnoreCase) ||
            shipName.Contains("dictor", StringComparison.OrdinalIgnoreCase) ||
            shipName.Contains("interdictor", StringComparison.OrdinalIgnoreCase) ||
            InterdictionPriorityShipNames.Contains(shipName))
        {
            return 1;
        }

        return 0;
    }

    private static string SingularizeShipName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= 2)
        {
            return trimmed;
        }

        if (trimmed.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return trimmed.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^1]
            : trimmed;
    }

    private static IReadOnlyList<GroupedShipSummary> BuildGroupedShipSummaries(
        IReadOnlyList<string> shipNames,
        IReadOnlyList<int> shipTypeIds,
        IReadOnlyList<IntelShipClass> shipClasses)
    {
        if (shipNames.Count == 0)
        {
            return [];
        }

        var entries = new List<ShipSummaryEntry>(shipNames.Count);
        for (var i = 0; i < shipNames.Count; i++)
        {
            var raw = shipNames[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var normalizedName = NormalizeShipDisplayName(raw);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var shipClass = shipClasses.Count > 0 ? shipClasses[Math.Min(i, shipClasses.Count - 1)] : IntelShipClass.Unknown;
            var shipTypeId = shipTypeIds.Count > 0 ? shipTypeIds[Math.Min(i, shipTypeIds.Count - 1)] : (int?)null;
            entries.Add(new ShipSummaryEntry(
                normalizedName,
                shipTypeId,
                ShipClassToOverlayIconKey(shipClass),
                GetShipClassThreatTier(shipClass),
                GetShipRolePriority(normalizedName)));
        }

        return entries
            .GroupBy(x => x.ShipName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var best = g
                    .OrderByDescending(x => x.ThreatTier)
                    .ThenByDescending(x => x.RolePriority)
                    .ThenByDescending(x => x.ShipTypeId is > 0)
                    .First();

                return new GroupedShipSummary(
                    best.ShipName,
                    g.Count(),
                    best.ThreatTier,
                    best.RolePriority,
                    best.ShipTypeId,
                    best.ShipIconKey);
            })
            .OrderByDescending(x => x.ThreatTier)
            .ThenByDescending(x => x.RolePriority)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.ShipName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatCompactIsk(decimal value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000_000_000m => $"{value / 1_000_000_000_000m:0.#}t",
            >= 1_000_000_000m => $"{value / 1_000_000_000m:0.#}b",
            >= 1_000_000m => $"{value / 1_000_000m:0.#}m",
            >= 1_000m => $"{value / 1_000m:0.#}k",
            _ => $"{value:0}"
        };
    }

    private static (string Background, string Border) GetIskLossBadgeColors(decimal totalValue)
    {
        return totalValue switch
        {
            >= 10_000_000_000m => ("#4A245A", "#9D61B8"), // purple
            >= 1_000_000_000m => ("#4A2525", "#C16565"),  // red
            >= 100_000_000m => ("#4A4022", "#C9A34E"),    // yellow
            _ => ("#1F3E28", "#4FA36A")                   // green
        };
    }

    private static IReadOnlyList<IntelMapHoverShip> BuildIntelHoverShips(
        IReadOnlyList<string> shipNames,
        IReadOnlyList<int> shipTypeIds,
        IReadOnlyList<IntelShipClass> shipClasses)
    {
        return BuildGroupedShipSummaries(shipNames, shipTypeIds, shipClasses)
            .Select(summary => new IntelMapHoverShip
            {
                ShipDisplayName = summary.ShipName,
                ShipIconKey = summary.ShipIconKey,
                ShipTypeId = summary.ShipTypeId,
                Count = summary.Count
            })
            .ToList();
    }

    private static string ShipClassToOverlayIconKey(IntelShipClass shipClass)
    {
        return shipClass switch
        {
            IntelShipClass.Titan => "titan",
            IntelShipClass.Supercapital => "supercapital",
            IntelShipClass.Capital => "capital",
            IntelShipClass.Battleship => "battleship",
            IntelShipClass.Battlecruiser => "battlecruiser",
            IntelShipClass.Cruiser => "cruiser",
            IntelShipClass.Destroyer => "destroyer",
            IntelShipClass.Frigate => "frigate",
            IntelShipClass.Industrial => "industrial",
            IntelShipClass.IndustrialCommand => "industrialcommand",
            IntelShipClass.Freighter => "freighter",
            IntelShipClass.MiningFrigate => "miningfrigate",
            IntelShipClass.MiningBarge => "miningbarge",
            IntelShipClass.Capsule => "capsule",
            IntelShipClass.Shuttle => "shuttle",
            IntelShipClass.Rookie => "rookie",
            _ => "crosshair"
        };
    }

    private sealed record ShipSummaryEntry(
        string ShipName,
        int? ShipTypeId,
        string ShipIconKey,
        int ThreatTier,
        int RolePriority);

    private sealed record GroupedShipSummary(
        string ShipName,
        int Count,
        int ThreatTier,
        int RolePriority,
        int? ShipTypeId,
        string ShipIconKey);

    private void RefreshIntelOverlayCardAges()
    {
        if (_intelCardsForView.Count == 0 && _zkillmailCardsForView.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var intelChanged = false;
        foreach (var card in _intelCardsForView)
        {
            var age = now - card.LastUpdatedUtc;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            var nextAge = FormatOverlayAge(age);
            if (!string.Equals(card.AgeSummary, nextAge, StringComparison.Ordinal))
            {
                card.AgeSummary = nextAge;
                intelChanged = true;
            }
        }

        var zkillChanged = false;
        foreach (var card in _zkillmailCardsForView)
        {
            var age = now - card.TimestampUtc;
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            var nextAge = FormatOverlayAge(age);
            if (!string.Equals(card.AgeSummary, nextAge, StringComparison.Ordinal))
            {
                card.AgeSummary = nextAge;
                zkillChanged = true;
            }
        }

        if (intelChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelCardsForView)));
        }

        if (zkillChanged)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZkillmailCardsForView)));
        }
    }

    private static string FormatOverlayAgeClock(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var totalMinutes = (int)age.TotalMinutes;
        var seconds = age.Seconds;
        return $"{totalMinutes:00}:{seconds:00}";
    }

    private Task ResolveIntelCharacterIdsAsync()
    {
        var unresolvedNames = _intelCardsForView
            .SelectMany(c => c.Hostiles)
            .Where(h => h.CharacterId is null)
            .Select(h => h.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in unresolvedNames)
        {
            var trimmed = name.Trim();
            if (_characterIdByName.TryGetValue(trimmed, out var cachedId))
            {
                ApplyResolvedCharacterId(trimmed, trimmed, cachedId);
                _ = EnsureIntelHostileImagesAsync(cachedId);
                continue;
            }

            lock (_characterIdLookupGate)
            {
                if (_characterIdLookupInFlight.Contains(trimmed))
                {
                    continue;
                }

                _characterIdLookupInFlight.Add(trimmed);
            }

            _ = ResolveCharacterIdByNameAsync(trimmed);
        }

        return Task.CompletedTask;
    }

    private async Task ResolveCharacterIdByNameAsync(string characterName)
    {
        try
        {
            var searchNames = new List<string> { characterName };

            var payload = JsonSerializer.Serialize(searchNames);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://esi.evetech.net/latest/universe/ids/?datasource=tranquility");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await IntelPortraitHttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            var result = await JsonSerializer.DeserializeAsync<EsiUniverseIdsResponse>(responseStream);
            var orderedMatches = result?.Characters?
                .OrderBy(x => searchNames.FindIndex(n => string.Equals(n, x.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var match = orderedMatches?.FirstOrDefault(x =>
                searchNames.Any(n => string.Equals(n, x.Name, StringComparison.OrdinalIgnoreCase)));
            if (match is null || match.Id <= 0)
            {
                _invalidHostilePilotNames.Add(characterName);
                Dispatcher.UIThread.Post(ScheduleActivityCardsRebuild);
                return;
            }

            _characterIdByName[characterName] = match.Id;
            _characterIdByName[match.Name] = match.Id;
            ApplyResolvedCharacterId(characterName, match.Name, match.Id);
            _ = EnsureIntelHostileImagesAsync(match.Id);
        }
        catch
        {
            // Ignore lookup failures; overlay keeps name-only hostile entry.
        }
        finally
        {
            lock (_characterIdLookupGate)
            {
                _characterIdLookupInFlight.Remove(characterName);
            }
        }
    }

    private void ApplyResolvedCharacterId(string characterName, string resolvedName, int characterId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var changed = false;
            foreach (var card in _intelCardsForView)
            {
                foreach (var hostile in card.Hostiles)
                {
                    if (!string.Equals(hostile.Name, characterName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.Equals(hostile.Name, resolvedName, StringComparison.Ordinal))
                    {
                        hostile.Name = resolvedName;
                        changed = true;
                    }
                    if (hostile.CharacterId != characterId)
                    {
                        hostile.CharacterId = characterId;
                        changed = true;
                    }
                    if (IntelPortraitBitmapCache.TryGetValue(characterId, out var portrait))
                    {
                        hostile.PortraitBitmap = portrait;
                    }
                    if (IntelAffiliationsByCharacterId.TryGetValue(characterId, out var affiliation))
                    {
                        hostile.CorporationId = affiliation.CorpId > 0 ? affiliation.CorpId : null;
                        hostile.AllianceId = affiliation.AllianceId;
                        if (IntelCorporationBitmapCache.TryGetValue(affiliation.CorpId, out var corp))
                        {
                            hostile.CorporationBitmap = corp;
                        }
                        if (IntelCorporationTickersById.TryGetValue(affiliation.CorpId, out var corporationTicker))
                        {
                            hostile.CorporationTicker = corporationTicker;
                        }
                        if (affiliation.AllianceId is { } allianceId &&
                            IntelAllianceBitmapCache.TryGetValue(allianceId, out var alliance))
                        {
                            hostile.AllianceBitmap = alliance;
                        }
                        if (affiliation.AllianceId is { } tickerAllianceId &&
                            IntelAllianceTickersById.TryGetValue(tickerAllianceId, out var allianceTicker))
                        {
                            hostile.AllianceTicker = allianceTicker;
                        }
                    }
                    changed = true;

                }
            }

            if (changed)
            {
                RebuildIntelPresenceForView();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntelCardsForView)));
                _ = ResolveIntelCharacterIdsAsync();
            }
        });
    }

    private async Task EnsureIntelHostileImagesAsync(int characterId)
    {
        if (!IntelImageLoadingByCharacterId.TryAdd(characterId, 0))
        {
            return;
        }

        try
        {
            var portrait = await GetOrLoadCachedBitmapAsync(
                IntelPortraitBitmapCache,
                characterId,
                "characters",
                $"{characterId}.png",
                $"https://images.evetech.net/characters/{characterId}/portrait?tenant=tranquility&size=64");

            if (!IntelAffiliationsByCharacterId.TryGetValue(characterId, out var affiliation))
            {
                var details = await LoadCharacterAffiliationAsync(characterId);
                affiliation = details is null ? default : (details.CorpId, details.AllianceId);
                if (details is not null && !string.IsNullOrWhiteSpace(details.Name))
                {
                    IntelCharacterNamesById[characterId] = details.Name;
                }

                if (affiliation.CorpId > 0)
                {
                    IntelAffiliationsByCharacterId[characterId] = affiliation;
                }
            }

            Bitmap? corpBitmap = null;
            Bitmap? allianceBitmap = null;
            var corporationTicker = string.Empty;
            var allianceTicker = string.Empty;
            if (affiliation.CorpId > 0)
            {
                corpBitmap = await GetOrLoadCachedBitmapAsync(
                    IntelCorporationBitmapCache,
                    affiliation.CorpId,
                    "corporations",
                    $"{affiliation.CorpId}.png",
                    $"https://images.evetech.net/corporations/{affiliation.CorpId}/logo?tenant=tranquility&size=64");
                corporationTicker = await GetOrLoadCorporationTickerAsync(affiliation.CorpId);
            }
            if (affiliation.AllianceId is { } allianceId && allianceId > 0)
            {
                allianceBitmap = await GetOrLoadCachedBitmapAsync(
                    IntelAllianceBitmapCache,
                    allianceId,
                    "alliances",
                    $"{allianceId}.png",
                    $"https://images.evetech.net/alliances/{allianceId}/logo?tenant=tranquility&size=64");
                allianceTicker = await GetOrLoadAllianceTickerAsync(allianceId);
            }

            Dispatcher.UIThread.Post(() =>
            {
                var changed = false;
                foreach (var card in _intelCardsForView)
                {
                    foreach (var hostile in card.Hostiles)
                    {
                        if (hostile.CharacterId != characterId)
                        {
                            continue;
                        }

                        if (!ReferenceEquals(hostile.PortraitBitmap, portrait))
                        {
                            hostile.PortraitBitmap = portrait;
                            changed = true;
                        }
                        if (!ReferenceEquals(hostile.CorporationBitmap, corpBitmap))
                        {
                            hostile.CorporationBitmap = corpBitmap;
                            changed = true;
                        }
                        var corpId = affiliation.CorpId > 0 ? affiliation.CorpId : (int?)null;
                        if (hostile.CorporationId != corpId)
                        {
                            hostile.CorporationId = corpId;
                            changed = true;
                        }
                        if (hostile.AllianceId != affiliation.AllianceId)
                        {
                            hostile.AllianceId = affiliation.AllianceId;
                            changed = true;
                        }
                        if (!ReferenceEquals(hostile.AllianceBitmap, allianceBitmap))
                        {
                            hostile.AllianceBitmap = allianceBitmap;
                            changed = true;
                        }
                        if (!string.Equals(hostile.CorporationTicker, corporationTicker, StringComparison.Ordinal))
                        {
                            hostile.CorporationTicker = corporationTicker;
                            changed = true;
                        }
                        if (!string.Equals(hostile.AllianceTicker, allianceTicker, StringComparison.Ordinal))
                        {
                            hostile.AllianceTicker = allianceTicker;
                            changed = true;
                        }
                        if (IntelCharacterNamesById.TryGetValue(characterId, out var resolvedName) &&
                            !string.Equals(hostile.Name, resolvedName, StringComparison.Ordinal))
                        {
                            hostile.Name = resolvedName;
                            changed = true;
                        }
                    }
                }

                foreach (var zkill in _zkillmailCardsForView)
                {
                    if (zkill.Victim.CharacterId == characterId)
                    {
                        zkill.Victim.PortraitBitmap = portrait;
                        zkill.Victim.CorporationBitmap = corpBitmap;
                        zkill.Victim.CorporationId = affiliation.CorpId > 0 ? affiliation.CorpId : null;
                        zkill.Victim.AllianceId = affiliation.AllianceId;
                        zkill.Victim.AllianceBitmap = allianceBitmap;
                        zkill.Victim.CorporationTicker = corporationTicker;
                        zkill.Victim.AllianceTicker = allianceTicker;
                        if (IntelCharacterNamesById.TryGetValue(characterId, out var resolvedName))
                        {
                            zkill.Victim.Name = resolvedName;
                        }
                        changed = true;
                    }

                    foreach (var attacker in zkill.VisibleAttackers)
                    {
                        if (attacker.CharacterId != characterId)
                        {
                            continue;
                        }

                        attacker.PortraitBitmap = portrait;
                        attacker.CorporationBitmap = corpBitmap;
                        attacker.CorporationId = affiliation.CorpId > 0 ? affiliation.CorpId : null;
                        attacker.AllianceId = affiliation.AllianceId;
                        attacker.AllianceBitmap = allianceBitmap;
                        attacker.CorporationTicker = corporationTicker;
                        attacker.AllianceTicker = allianceTicker;
                        if (IntelCharacterNamesById.TryGetValue(characterId, out var resolvedName))
                        {
                            attacker.Name = resolvedName;
                        }
                        changed = true;
                    }
                }

                if (changed)
                {
                    RebuildIntelPresenceForView();
                    RequestOverlayCardsUiRefresh(intelCardsChanged: true, zkillCardsChanged: true);
                }
            });
        }
        catch
        {
            // Ignore portrait/logo load failures.
        }
        finally
        {
            IntelImageLoadingByCharacterId.TryRemove(characterId, out _);
        }
    }

    private static async Task<Bitmap?> GetOrLoadCachedBitmapAsync(
        ConcurrentDictionary<int, Bitmap> memoryCache,
        int key,
        string folderName,
        string fileName,
        string sourceUrl,
        bool checkServerForNewer = true)
    {
        if (!checkServerForNewer && memoryCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var lockKey = $"{folderName}:{key}";
        var gate = IntelBitmapLoadLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!checkServerForNewer && memoryCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var filePath = Path.Combine(IntelImageCacheRoot, folderName, fileName);
            var bitmap = await LoadBitmapFromDiskOrDownloadAsync(filePath, sourceUrl, checkServerForNewer);
            if (bitmap is not null)
            {
                memoryCache[key] = bitmap;
                TrimBitmapCache(memoryCache, ResolveBitmapCacheLimit(folderName));
            }

            return bitmap;
        }
        finally
        {
            gate.Release();
        }
    }

    private static int ResolveBitmapCacheLimit(string folderName)
    {
        return folderName.ToLowerInvariant() switch
        {
            "ships" => MaxIntelShipBitmapCacheItems,
            "characters" => MaxIntelPortraitBitmapCacheItems,
            "corporations" => MaxIntelCorporationBitmapCacheItems,
            "alliances" => MaxIntelAllianceBitmapCacheItems,
            _ => 500
        };
    }

    private static void TrimBitmapCache(ConcurrentDictionary<int, Bitmap> memoryCache, int maxItems)
    {
        if (memoryCache.Count <= maxItems)
        {
            return;
        }

        var overflow = memoryCache.Count - maxItems;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var key in memoryCache.Keys.Take(overflow))
        {
            if (memoryCache.TryRemove(key, out _))
            {
                // Bitmap instances can still be referenced by active cards; avoid disposing here.
            }
        }
    }

    private static async Task<Bitmap?> LoadBitmapFromDiskOrDownloadAsync(string filePath, string url, bool checkServerForNewer = true)
    {
        var existing = TryLoadBitmapFromFile(filePath);
        if (existing is not null)
        {
            if (checkServerForNewer)
            {
                var refreshed = await TryRefreshCachedBitmapIfNewerAsync(filePath, url);
                if (refreshed is not null)
                {
                    return refreshed;
                }
            }

            return existing;
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = await IntelPortraitHttpClient.GetByteArrayAsync(url);
            try
            {
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                await fileStream.WriteAsync(bytes);
                await fileStream.FlushAsync();
            }
            catch (IOException)
            {
                // Another process/instance may be touching this file; fallback to whichever file is available.
            }

            return TryLoadBitmapFromFile(filePath);
        }
        catch
        {
            return TryLoadBitmapFromFile(filePath);
        }
    }

    private static async Task<Bitmap?> TryRefreshCachedBitmapIfNewerAsync(string filePath, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await IntelPortraitHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var remoteLastModifiedUtc = response.Content.Headers.LastModified?.UtcDateTime;
            if (remoteLastModifiedUtc is null)
            {
                return null;
            }

            var localLastWriteUtc = File.GetLastWriteTimeUtc(filePath);
            if (localLastWriteUtc >= remoteLastModifiedUtc.Value)
            {
                return null;
            }

            var bytes = await IntelPortraitHttpClient.GetByteArrayAsync(url);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            await fileStream.WriteAsync(bytes);
            await fileStream.FlushAsync();
            File.SetLastWriteTimeUtc(filePath, remoteLastModifiedUtc.Value);
            return TryLoadBitmapFromFile(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? TryLoadBitmapFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new Bitmap(fileStream);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<EsiCharacterIdentity?> LoadCharacterAffiliationAsync(int characterId)
    {
        try
        {
            using var response = await IntelPortraitHttpClient.GetAsync($"https://esi.evetech.net/latest/characters/{characterId}/?datasource=tranquility");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            var details = await JsonSerializer.DeserializeAsync<EsiCharacterDetailsResponse>(responseStream);
            if (details is null || details.CorporationId <= 0)
            {
                return null;
            }

            return new EsiCharacterIdentity
            {
                CorpId = details.CorporationId,
                AllianceId = details.AllianceId,
                Name = details.Name?.Trim() ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> GetOrLoadCorporationTickerAsync(int corporationId)
    {
        if (IntelCorporationTickersById.TryGetValue(corporationId, out var cached))
        {
            return cached;
        }

        var ticker = await LoadPublicTickerAsync($"https://esi.evetech.net/latest/corporations/{corporationId}/?datasource=tranquility");
        if (!string.IsNullOrWhiteSpace(ticker))
        {
            IntelCorporationTickersById[corporationId] = ticker;
        }

        return ticker;
    }

    private static async Task<string> GetOrLoadAllianceTickerAsync(int allianceId)
    {
        if (IntelAllianceTickersById.TryGetValue(allianceId, out var cached))
        {
            return cached;
        }

        var ticker = await LoadPublicTickerAsync($"https://esi.evetech.net/latest/alliances/{allianceId}/?datasource=tranquility");
        if (!string.IsNullOrWhiteSpace(ticker))
        {
            IntelAllianceTickersById[allianceId] = ticker;
        }

        return ticker;
    }

    private static async Task<string> LoadPublicTickerAsync(string url)
    {
        try
        {
            using var response = await IntelPortraitHttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            var details = await JsonSerializer.DeserializeAsync<EsiOrganizationDetailsResponse>(responseStream);
            return details?.Ticker?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class EsiUniverseIdsResponse
    {
        [JsonPropertyName("characters")]
        public List<EsiUniverseIdEntry>? Characters { get; init; }
    }

    private sealed class EsiUniverseIdEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class EsiCharacterDetailsResponse
    {
        [JsonPropertyName("corporation_id")]
        public int CorporationId { get; init; }
        [JsonPropertyName("alliance_id")]
        public int? AllianceId { get; init; }
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class EsiCharacterIdentity
    {
        public int CorpId { get; init; }
        public int? AllianceId { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    private sealed class EsiOrganizationDetailsResponse
    {
        [JsonPropertyName("ticker")]
        public string? Ticker { get; init; }
    }

    private static string FormatOverlayAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(1, (int)age.TotalSeconds)}s ago";
        }
        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m ago";
        }
        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours}h ago";
        }
        return $"{(int)age.TotalDays}d ago";
    }

    private static (string Label, string ColorHex) GetStormTypeDisplay(StormType type)
    {
        return type switch
        {
            StormType.Electrical => ("Electrical", "#4AA8FF"),
            StormType.Gamma => ("Gamma", "#E69138"),
            StormType.Exotic => ("Exotic", "#CFD4DC"),
            StormType.Plasma => ("Plasma", "#DE5B52"),
            _ => ("Unknown", "#9AA7B8")
        };
    }

    private static int GetMaxShipThreatTier(IReadOnlyList<IntelShipClass> shipClasses)
    {
        if (shipClasses.Count == 0)
        {
            return 0;
        }

        var max = 0;
        foreach (var shipClass in shipClasses)
        {
            var tier = shipClass switch
            {
                IntelShipClass.Titan => 8,
                IntelShipClass.Supercapital => 7,
                IntelShipClass.Capital => 6,
                IntelShipClass.Battleship => 5,
                IntelShipClass.Battlecruiser => 4,
                IntelShipClass.Cruiser => 3,
                IntelShipClass.Destroyer => 2,
                IntelShipClass.Frigate => 1,
                _ => 1
            };
            if (tier > max)
            {
                max = tier;
            }
        }

        return max;
    }

    private static (string BackgroundHex, string BorderHex) GetThreatBadgeColors(double intensity)
    {
        var t = Math.Clamp(intensity, 0.0, 1.0);
        var bg = GetPaletteColor(t, (74, 82, 94), (125, 104, 34), (150, 84, 30), (156, 44, 44));
        var border = GetPaletteColor(t, (112, 122, 136), (176, 144, 50), (196, 108, 42), (206, 74, 74));
        return (ToHex(bg), ToHex(border));
    }

    private static (int R, int G, int B) GetPaletteColor(
        double t,
        (int R, int G, int B) c0,
        (int R, int G, int B) c1,
        (int R, int G, int B) c2,
        (int R, int G, int B) c3)
    {
        if (t <= 0.33)
        {
            return LerpColor(c0, c1, t / 0.33);
        }

        if (t <= 0.66)
        {
            return LerpColor(c1, c2, (t - 0.33) / 0.33);
        }

        return LerpColor(c2, c3, (t - 0.66) / 0.34);
    }

    private static (int R, int G, int B) LerpColor((int R, int G, int B) from, (int R, int G, int B) to, double t)
    {
        return (
            (int)Math.Round(from.R + ((to.R - from.R) * t)),
            (int)Math.Round(from.G + ((to.G - from.G) * t)),
            (int)Math.Round(from.B + ((to.B - from.B) * t))
        );
    }

    private static string ToHex((int R, int G, int B) color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string BuildExpiryHoursLabel(TimeSpan remaining)
    {
        var hours = Math.Max(1, (int)Math.Ceiling(remaining.TotalHours));
        return hours > 18 ? "> 18h" : $"> {hours}h";
    }

    private static string GetExpiryColorHex(TimeSpan remaining)
    {
        var hours = Math.Max(0, remaining.TotalHours);
        if (hours <= 2)
        {
            return "#FF5C5C";
        }
        if (hours <= 5)
        {
            return "#FF8F3D";
        }
        if (hours <= 9)
        {
            return "#FFC24A";
        }
        if (hours <= 14)
        {
            return "#B6DC61";
        }

        return "#6FE38E";
    }

    private void AddJumpRangeDistance(MapNode targetNode, JumpRangeOriginSettings origin, double distanceLy)
    {
        if (!_jumpRangeDistancesByNodeId.TryGetValue(targetNode.Id, out var values))
        {
            values = [];
            _jumpRangeDistancesByNodeId[targetNode.Id] = values;
        }

        values.Add(new JumpRangeDistanceDisplay
        {
            OriginNodeId = origin.SolarSystemId,
            OriginSystemName = origin.SolarSystemName,
            DistanceLy = distanceLy,
            MaxLy = origin.RangeLy,
            IsInRange = distanceLy == 0 || (distanceLy > 0 && distanceLy < origin.RangeLy)
        });
    }

    private (string BackgroundHex, string BorderHex) GetHostileBadgeColors(int hostileCount)
    {
        var color = GetHostileColor(hostileCount);
        var background = LerpColor(color, (13, 19, 29), 0.72);
        return (ToHex(background), ToHex(color));
    }

    private (int R, int G, int B) GetHostileColor(int hostileCount)
    {
        var settings = HostileColorSettings;
        var lowMax = Math.Max(1, settings.LowMaxHostiles);
        var mediumMax = Math.Max(lowMax + 1, settings.MediumMaxHostiles);
        var highMax = Math.Max(mediumMax + 1, settings.HighMaxHostiles);
        var low = ParseRgb(settings.LowColorHex, (230, 216, 108));
        var medium = ParseRgb(settings.MediumColorHex, (238, 134, 57));
        var high = ParseRgb(settings.HighColorHex, (217, 15, 19));
        var aboveHigh = ParseRgb(settings.AboveHighColorHex, (221, 0, 140));

        if (hostileCount <= lowMax)
        {
            return low;
        }

        if (hostileCount <= mediumMax)
        {
            return LerpColor(low, medium, (hostileCount - lowMax) / (double)(mediumMax - lowMax));
        }

        if (hostileCount <= highMax)
        {
            return LerpColor(medium, high, (hostileCount - mediumMax) / (double)(highMax - mediumMax));
        }

        var highBandWidth = Math.Max(1, highMax - mediumMax);
        var progress = 1.0 - Math.Exp(-(hostileCount - highMax) / (double)highBandWidth);
        return LerpColor(high, aboveHigh, progress);
    }

    private static (int R, int G, int B) ParseRgb(string? color, (int R, int G, int B) fallback)
    {
        var hex = color?.Trim().TrimStart('#') ?? string.Empty;
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        return hex.Length == 6 &&
               int.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var red) &&
               int.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var green) &&
               int.TryParse(hex[4..], System.Globalization.NumberStyles.HexNumber, null, out var blue)
            ? (red, green, blue)
            : fallback;
    }

    private static double GetDistanceLy(JumpRangeOriginSettings from, MapNode to)
    {
        if (to.PositionX is not double toX ||
            to.PositionY is not double toY ||
            to.PositionZ is not double toZ)
        {
            return -1;
        }

        var dx = toX - from.PositionX;
        var dy = toY - from.PositionY;
        var dz = toZ - from.PositionZ;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / 9_460_000_000_000_000.0;
    }

    private static double GetDistanceLy(MapSystemPosition from, MapSystemPosition to)
    {
        var dx = to.PositionX - from.PositionX;
        var dy = to.PositionY - from.PositionY;
        var dz = to.PositionZ - from.PositionZ;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / 9_460_000_000_000_000.0;
    }

    private static List<string> ParseSystemTokens(string input)
    {
        return input
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static (List<MapSystemPosition> Route, List<MapSystemPosition> Skipped, double TotalLy, double MaxLegLy) BuildGreedyRoute(
        MapSystemPosition seed,
        IReadOnlyList<MapSystemPosition> targets,
        double maxJumpLy,
        ISet<long> priorities,
        MapSystemPosition? fixedEnd,
        bool returnToStart)
    {
        var remaining = targets.ToDictionary(t => t.SolarSystemId, t => t);
        var route = new List<MapSystemPosition>();
        var skipped = new List<MapSystemPosition>();
        var total = 0.0;
        var maxLeg = 0.0;
        var current = seed;

        route.Add(seed);
        remaining.Remove(seed.SolarSystemId);

        while (remaining.Count > 0)
        {
            if (fixedEnd is not null &&
                remaining.Count == 1 &&
                remaining.TryGetValue(fixedEnd.SolarSystemId, out var lastTarget))
            {
                var lastDist = GetDistanceLy(current, lastTarget);
                if (lastDist <= maxJumpLy)
                {
                    route.Add(lastTarget);
                    remaining.Remove(lastTarget.SolarSystemId);
                    total += lastDist;
                    maxLeg = Math.Max(maxLeg, lastDist);
                    current = lastTarget;
                    continue;
                }
            }

            var candidates = remaining.Values
                .Where(candidate => fixedEnd is null || candidate.SolarSystemId != fixedEnd.SolarSystemId || remaining.Count == 1);

            var next = candidates
                .Select(candidate =>
                {
                    var d = GetDistanceLy(current, candidate);
                    var priorityBoost = priorities.Contains(candidate.SolarSystemId) ? -0.35 : 0.0;
                    return new { candidate, d, score = d + priorityBoost };
                })
                .Where(x => x.d <= maxJumpLy)
                .OrderBy(x => x.score)
                .ThenBy(x => x.d)
                .FirstOrDefault();

            if (next is null)
            {
                skipped.AddRange(remaining.Values);
                break;
            }

            route.Add(next.candidate);
            remaining.Remove(next.candidate.SolarSystemId);
            total += next.d;
            maxLeg = Math.Max(maxLeg, next.d);
            current = next.candidate;
        }

        if (fixedEnd is not null && route.All(x => x.SolarSystemId != fixedEnd.SolarSystemId))
        {
            var endDist = GetDistanceLy(current, fixedEnd);
            if (endDist <= maxJumpLy)
            {
                route.Add(fixedEnd);
                total += endDist;
                maxLeg = Math.Max(maxLeg, endDist);
                current = fixedEnd;
            }
        }

        if (returnToStart && route.Count > 1)
        {
            var start = route[0];
            var backDist = GetDistanceLy(current, start);
            if (backDist <= maxJumpLy)
            {
                route.Add(start);
                total += backDist;
                maxLeg = Math.Max(maxLeg, backDist);
            }
        }

        return (route, skipped, total, maxLeg);
    }

    private static List<string> BuildSkippedReasonLines(
        IReadOnlyList<MapSystemPosition> route,
        IReadOnlyList<MapSystemPosition> skippedSystems,
        double maxJumpLy)
    {
        var lines = new List<string>();
        foreach (var skipped in skippedSystems.OrderBy(x => x.SolarSystemName, StringComparer.OrdinalIgnoreCase))
        {
            var feasible = false;
            for (var i = 0; i <= route.Count; i++)
            {
                MapSystemPosition? prev = i > 0 ? route[i - 1] : null;
                MapSystemPosition? next = i < route.Count ? route[i] : null;
                if (prev is not null && GetDistanceLy(prev, skipped) > maxJumpLy)
                {
                    continue;
                }

                if (next is not null && GetDistanceLy(skipped, next) > maxJumpLy)
                {
                    continue;
                }

                feasible = true;
                break;
            }

            lines.Add(feasible
                ? $"{skipped.SolarSystemName}: deferred by optimizer ordering"
                : $"{skipped.SolarSystemName}: no feasible insertion <= {maxJumpLy:0.00} LY");
        }

        return lines;
    }

    public async Task<IReadOnlyList<string>> GetSystemNameSuggestionsAsync(string query, int maxCount = 8, CancellationToken cancellationToken = default)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            return [];
        }

        var candidates = await _mapDataService.SearchAsync(term, cancellationToken);
        return candidates
            .Where(c => c.Kind == MapSearchKind.SolarSystem && !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxCount))
            .ToList();
    }

    private static List<MapSystemPosition> TwoOptImprove(List<MapSystemPosition> route, double maxJumpLy)
    {
        if (route.Count < 4)
        {
            return route;
        }

        var improved = route.ToList();
        var changed = true;
        var guard = 0;
        while (changed && guard++ < 20)
        {
            changed = false;
            for (var i = 1; i < improved.Count - 2; i++)
            {
                for (var k = i + 1; k < improved.Count - 1; k++)
                {
                    var a = improved[i - 1];
                    var b = improved[i];
                    var c = improved[k];
                    var d = improved[k + 1];
                    var current = GetDistanceLy(a, b) + GetDistanceLy(c, d);
                    var candidate = GetDistanceLy(a, c) + GetDistanceLy(b, d);
                    if (GetDistanceLy(a, c) > maxJumpLy || GetDistanceLy(b, d) > maxJumpLy)
                    {
                        continue;
                    }

                    if (candidate + 0.000001 < current)
                    {
                        improved.Reverse(i, (k - i) + 1);
                        changed = true;
                    }
                }
            }
        }

        return improved;
    }

    private static List<MapSystemPosition> ExpandRouteWithFeasibleInsertions(
        List<MapSystemPosition> route,
        IReadOnlyList<MapSystemPosition> targets,
        double maxJumpLy,
        ISet<long> priorities,
        MapSystemPosition? fixedStart,
        MapSystemPosition? fixedEnd,
        bool returnToStart)
    {
        var result = route.ToList();
        var remaining = targets
            .Where(t => result.All(r => r.SolarSystemId != t.SolarSystemId))
            .OrderByDescending(t => priorities.Contains(t.SolarSystemId))
            .ThenBy(t => t.SolarSystemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Keep retrying skipped systems because earlier insertions can create new feasible slots.
        var progress = true;
        while (progress && remaining.Count > 0)
        {
            progress = false;
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                var candidate = remaining[i];
                var bestIdx = -1;
                var bestAdded = double.MaxValue;
                for (var insertIdx = 0; insertIdx <= result.Count; insertIdx++)
                {
                    if (fixedStart is not null && insertIdx == 0)
                    {
                        continue;
                    }

                    if (fixedEnd is not null && !returnToStart && insertIdx == result.Count)
                    {
                        continue;
                    }

                    MapSystemPosition? prev = insertIdx > 0 ? result[insertIdx - 1] : null;
                    MapSystemPosition? next = insertIdx < result.Count ? result[insertIdx] : null;
                    if (prev is not null && GetDistanceLy(prev, candidate) > maxJumpLy)
                    {
                        continue;
                    }
                    if (next is not null && GetDistanceLy(candidate, next) > maxJumpLy)
                    {
                        continue;
                    }

                    var removed = (prev is not null && next is not null) ? GetDistanceLy(prev, next) : 0.0;
                    var added = (prev is not null ? GetDistanceLy(prev, candidate) : 0.0) +
                                (next is not null ? GetDistanceLy(candidate, next) : 0.0) -
                                removed;
                    if (added < bestAdded)
                    {
                        bestAdded = added;
                        bestIdx = insertIdx;
                    }
                }

                if (bestIdx >= 0)
                {
                    result.Insert(bestIdx, candidate);
                    remaining.RemoveAt(i);
                    progress = true;
                }
            }
        }

        return result;
    }

    private static List<JumpRouteLegRow> BuildRouteLegs(List<MapSystemPosition> route, double maxJumpLy)
    {
        var legs = new List<JumpRouteLegRow>();
        for (var i = 0; i < route.Count - 1; i++)
        {
            var d = GetDistanceLy(route[i], route[i + 1]);
            if (d > maxJumpLy)
            {
                continue;
            }

            legs.Add(new JumpRouteLegRow
            {
                From = route[i].SolarSystemName,
                To = route[i + 1].SolarSystemName,
                DistanceLy = d
            });
        }

        return legs;
    }

    private static bool TryBuildStrictInputOrderedRoute(
        IReadOnlyList<MapSystemPosition> targetsInInputOrder,
        MapSystemPosition? fixedStart,
        MapSystemPosition? fixedEnd,
        double maxJumpLy,
        bool returnToStart,
        out List<MapSystemPosition> route,
        out string failureReason)
    {
        route = [];
        failureReason = string.Empty;

        if (targetsInInputOrder.Count == 0)
        {
            failureReason = "no valid target systems";
            return false;
        }

        var ordered = targetsInInputOrder.ToList();
        if (fixedStart is not null)
        {
            ordered.RemoveAll(x => x.SolarSystemId == fixedStart.SolarSystemId);
            route.Add(fixedStart);
        }

        if (fixedEnd is not null)
        {
            ordered.RemoveAll(x => x.SolarSystemId == fixedEnd.SolarSystemId);
        }

        route.AddRange(ordered);

        if (fixedEnd is not null)
        {
            route.Add(fixedEnd);
        }

        if (route.Count == 0)
        {
            failureReason = "empty route after start/end constraints";
            return false;
        }

        for (var i = 0; i < route.Count - 1; i++)
        {
            var d = GetDistanceLy(route[i], route[i + 1]);
            if (d > maxJumpLy)
            {
                failureReason = $"{route[i].SolarSystemName} -> {route[i + 1].SolarSystemName} requires {d:0.00} LY (> {maxJumpLy:0.00})";
                return false;
            }
        }

        if (returnToStart && route.Count > 1)
        {
            var back = GetDistanceLy(route[^1], route[0]);
            if (back > maxJumpLy)
            {
                failureReason = $"return leg {route[^1].SolarSystemName} -> {route[0].SolarSystemName} requires {back:0.00} LY (> {maxJumpLy:0.00})";
                return false;
            }

            route.Add(route[0]);
        }

        return true;
    }

    private static bool HasSdePosition(MapNode node)
    {
        return node.PositionX is not null && node.PositionY is not null && node.PositionZ is not null;
    }

    private void EnforceCoordinateModeForView()
    {
        if (SelectedViewMode == MapViewMode.Universe)
        {
            if (SelectedCoordinateMode != _savedUniverseCoordinateMode)
            {
                _selectedCoordinateMode = _savedUniverseCoordinateMode;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCoordinateMode)));
            }
            return;
        }

        if (SelectedViewMode == MapViewMode.Region)
        {
            if (SelectedCoordinateMode != _savedRegionCoordinateMode)
            {
                _selectedCoordinateMode = _savedRegionCoordinateMode;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCoordinateMode)));
            }
            EnforceCoordinateModeForSelectedRegion();
            return;
        }

        if (SelectedCoordinateMode != MapCoordinateMode.SdePlanarXY)
        {
            _selectedCoordinateMode = MapCoordinateMode.SdePlanarXY;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCoordinateMode)));
        }
    }

    private void EnforceCoordinateModeForSelectedRegion()
    {
        if (SelectedViewMode != MapViewMode.Region || SelectedRegion is not { Kind: not RegionOptionKind.Regular })
        {
            return;
        }

        if (SelectedCoordinateMode == MapCoordinateMode.SdePlanarXY)
        {
            return;
        }

        _selectedCoordinateMode = MapCoordinateMode.SdePlanarXY;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCoordinateMode)));
    }

    private void PersistCoordinateModeForCurrentView(MapCoordinateMode value)
    {
        switch (SelectedViewMode)
        {
            case MapViewMode.Universe:
                _savedUniverseCoordinateMode = value;
                _ = _settingsService.SetAsync(CoordinateModeUniverseKey, value);
                break;
            case MapViewMode.Region:
                _savedRegionCoordinateMode = value;
                _ = _settingsService.SetAsync(CoordinateModeRegionKey, value);
                break;
            default:
                break;
        }
    }

    private async Task UpdateSearchSuggestionsAsync(string rawText)
    {
        _searchSuggestionsCts?.Cancel();
        _searchSuggestionsCts?.Dispose();
        _searchSuggestionsCts = new CancellationTokenSource();
        var ct = _searchSuggestionsCts.Token;

        var term = rawText.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            ClearSearchSuggestions();
            return;
        }

        try
        {
            await Task.Delay(120, ct);
            var candidates = await _mapDataService.SearchAsync(term, ct);
            var filtered = FilterCandidatesForCurrentMode(candidates).Take(10).ToList();

            SearchSuggestions.Clear();
            foreach (var candidate in filtered)
            {
                SearchSuggestions.Add(candidate);
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSearchSuggestions)));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ClearSearchSuggestions();
        }
    }

    private async Task RefreshRegionMissingConnectionMarkersAsync(MapGraph graph)
    {
        if (SelectedViewMode != MapViewMode.Region)
        {
            if (MissingConnectionNodeIdsForView.Any())
            {
                MissingConnectionNodeIdsForView = [];
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MissingConnectionNodeIdsForView)));
            }

            return;
        }

        var presentById = graph.Nodes.ToDictionary(n => n.Id);
        var presentSystemIds = presentById.Keys.Where(id => id > 0).ToHashSet();
        if (presentSystemIds.Count == 0)
        {
            MissingConnectionNodeIdsForView = [];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MissingConnectionNodeIdsForView)));
            return;
        }

        var neighborCounts = await _mapDataService.GetSystemNeighborCountsAsync(presentSystemIds);
        var presentNeighborCounts = presentSystemIds.ToDictionary(id => id, _ => 0);
        foreach (var link in graph.Links)
        {
            if (link.FromId > 0 && link.ToId > 0)
            {
                if (presentNeighborCounts.ContainsKey(link.FromId))
                {
                    presentNeighborCounts[link.FromId]++;
                }

                if (presentNeighborCounts.ContainsKey(link.ToId))
                {
                    presentNeighborCounts[link.ToId]++;
                }
            }
        }

        var missing = new List<long>();
        foreach (var id in presentSystemIds)
        {
            var total = neighborCounts.TryGetValue(id, out var totalCount) ? totalCount : 0;
            var present = presentNeighborCounts.TryGetValue(id, out var presentCount) ? presentCount : 0;
            if (total > present)
            {
                missing.Add(id);
            }
        }

        MissingConnectionNodeIdsForView = missing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MissingConnectionNodeIdsForView)));
    }

    private IReadOnlyList<MapSearchCandidate> FilterCandidatesForCurrentMode(IReadOnlyList<MapSearchCandidate> candidates)
    {
        return SelectedViewMode switch
        {
            MapViewMode.UniverseRegions => candidates.Where(c => c.Kind == MapSearchKind.Region).ToList(),
            MapViewMode.Universe => candidates.ToList(),
            MapViewMode.Region => candidates.ToList(),
            _ => candidates.ToList()
        };
    }

    private MapSearchCandidate? PickBestCandidateForMode(IReadOnlyList<MapSearchCandidate> candidates)
    {
        var filtered = FilterCandidatesForCurrentMode(candidates);
        return SelectedViewMode switch
        {
            MapViewMode.UniverseRegions => filtered.FirstOrDefault(c => c.Kind == MapSearchKind.Region),
            MapViewMode.Universe => filtered.FirstOrDefault(c => c.Kind == MapSearchKind.SolarSystem)
                ?? filtered.FirstOrDefault(c => c.Kind == MapSearchKind.Constellation)
                ?? filtered.FirstOrDefault(c => c.Kind == MapSearchKind.Region),
            MapViewMode.Region => filtered.FirstOrDefault(c => c.Kind == MapSearchKind.SolarSystem)
                ?? filtered.FirstOrDefault(c => c.Kind == MapSearchKind.Constellation)
                ?? filtered.FirstOrDefault(c => c.Kind == MapSearchKind.Region),
            _ => filtered.FirstOrDefault()
        };
    }
}





