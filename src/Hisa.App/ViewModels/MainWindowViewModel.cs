using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private sealed class SavedRegionToken
    {
        public required string RegionName { get; init; }
        public required RegionOptionKind Kind { get; init; }
    }

    private readonly IMapDataService _mapDataService;
    private readonly ISettingsService _settingsService;
    private readonly IStormStateService _stormStateService;
    private readonly IHubWormholeStateService _hubWormholeStateService;
    private readonly ISovUpgradeStateService _sovUpgradeStateService;
    private readonly IAnsiblexNetworkStateService _ansiblexNetworkStateService;
    private readonly IIncursionStateService _incursionStateService;
    private List<RegionOption> _allRegions = [];
    private bool _isBusy;
    private MapViewMode _selectedViewMode;
    private MapCoordinateMode _selectedCoordinateMode;
    private RegionOption? _selectedRegion;
    private MapGraph? _currentGraph;
    private long? _selectedNodeId;
    private string _mapSearchText = string.Empty;
    private MapSearchCandidate? _selectedSearchSuggestion;
    private string _regionSearchText = string.Empty;
    private string _statusText = "Loading map...";
    private bool _stretchMapToWindow;
    private bool _isDisplaySettingsOpen;
    private MapNodeColorMode _nodeColorMode = MapNodeColorMode.None;
    private MapNodeColorMode _nodeBackgroundColorMode = MapNodeColorMode.None;
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
    private bool _showIndicatorJumpRangeLy = true;
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
    private bool _infoBoxShowJumpRangeLy = true;
    private bool _alwaysShowHubWormholes = true;
    private bool _alwaysShowIncursions = true;
    private bool _showMissingConnectionMarkers = true;
    private bool _isHubWormholesOverlayOpen;
    private bool _isIncursionsOverlayOpen;
    private bool _isStormsOverlayOpen;
    private HubWormholeMarkerMode _hubWormholeMarkerMode = HubWormholeMarkerMode.Badge;
    private readonly Dictionary<long, double> _jumpRangeOriginsLyByNodeId = [];
    private readonly Dictionary<long, uint> _jumpRangeOriginColorByNodeId = [];
    private List<long> _jumpRangeInRangeNodeIdsForView = [];
    private IReadOnlyList<JumpRangeOriginDisplay> _jumpRangeOriginsDisplayForView = [];
    private List<long> _lyCoverageCoveredNodeIdsForView = [];
    private List<long> _lyCoverageUncoveredNodeIdsForView = [];
    private List<long> _jumpRouteNodeIdsForView = [];
    private List<long> _jumpRouteSkippedNodeIdsForView = [];
    private IReadOnlyList<WormholeOverlayCard> _hubWormholeCardsForView = [];
    private IReadOnlyList<IncursionOverlayCard> _incursionCardsForView = [];
    private IReadOnlyList<StormOverlayCard> _stormCardsForView = [];
    private readonly Dictionary<long, List<long>> _jumpRangeMembershipByNodeId = [];
    private readonly Dictionary<long, List<JumpRangeDistanceDisplay>> _jumpRangeDistancesByNodeId = [];
    private CancellationTokenSource? _searchSuggestionsCts;
    private bool _isInitializing = true;
    private const string ViewModeKey = "Map.SelectedViewMode";
    private const string RegionIdKey = "Map.SelectedRegionId";
    private const string RegionTokenKey = "Map.SelectedRegionToken";
    private const string CoordinateModeKey = "Map.SelectedCoordinateMode";
    private const string StretchMapToWindowKey = "Map.StretchToWindow";
    private const string NodeColorModeKey = "Map.NodeColorMode";
    private const string NodeBackgroundColorModeKey = "Map.NodeBackgroundColorMode";
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
    private const string ShowIndicatorJumpRangeLyKey = "Map.ShowIndicatorJumpRangeLy";
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
    private readonly Task _initialLoadTask;

    public MainWindowViewModel(
        IMapDataService mapDataService,
        ISettingsService settingsService,
        IStormStateService stormStateService,
        IHubWormholeStateService hubWormholeStateService,
        ISovUpgradeStateService sovUpgradeStateService,
        IAnsiblexNetworkStateService ansiblexNetworkStateService,
        IIncursionStateService incursionStateService)
    {
        _mapDataService = mapDataService;
        _settingsService = settingsService;
        _stormStateService = stormStateService;
        _hubWormholeStateService = hubWormholeStateService;
        _sovUpgradeStateService = sovUpgradeStateService;
        _ansiblexNetworkStateService = ansiblexNetworkStateService;
        _incursionStateService = incursionStateService;
        ViewModes = new ObservableCollection<MapViewMode>(Enum.GetValues<MapViewMode>());
        CoordinateModes = new ObservableCollection<MapCoordinateMode>(Enum.GetValues<MapCoordinateMode>());
        NodeColorModes = new ObservableCollection<MapNodeColorMode>(Enum.GetValues<MapNodeColorMode>());
        HubWormholeMarkerModes = new ObservableCollection<HubWormholeMarkerMode>(Enum.GetValues<HubWormholeMarkerMode>());
        Regions = [];
        _stormStateService.StormSnapshotUpdated += OnStormSnapshotUpdated;
        _hubWormholeStateService.HubWormholeSnapshotUpdated += OnHubWormholeSnapshotUpdated;
        _sovUpgradeStateService.SnapshotUpdated += OnSovUpgradesSnapshotUpdated;
        _ansiblexNetworkStateService.SnapshotUpdated += OnAnsiblexNetworkSnapshotUpdated;
        _incursionStateService.IncursionSnapshotUpdated += OnIncursionSnapshotUpdated;
        _initialLoadTask = LoadAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MapViewMode> ViewModes { get; }
    public ObservableCollection<MapCoordinateMode> CoordinateModes { get; }
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
    public string HubWormholeOverlayTitle => $"Thera/Turnur Wormholes ({_hubWormholeCardsForView.Count})";
    public string IncursionOverlayTitle => $"Incursions ({_incursionCardsForView.Count})";
    public string StormOverlayTitle => $"Metaliminal Storms ({_stormCardsForView.Count})";
    public IReadOnlyDictionary<long, IReadOnlyList<long>> JumpRangeMembershipByNodeIdForView =>
        _jumpRangeMembershipByNodeId.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<long>)kvp.Value);
    public IReadOnlyDictionary<long, IReadOnlyList<JumpRangeDistanceDisplay>> JumpRangeDistancesByNodeIdForView =>
        _jumpRangeDistancesByNodeId.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<JumpRangeDistanceDisplay>)kvp.Value);

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
                if (!_isInitializing)
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
            if (SelectedViewMode == MapViewMode.Region && SelectedRegion is { Kind: not RegionOptionKind.Regular })
            {
                value = MapCoordinateMode.SdePlanarXY;
            }

            if (SelectedViewMode == MapViewMode.UniverseRegions && value != MapCoordinateMode.SdePlanarXY)
            {
                value = MapCoordinateMode.SdePlanarXY;
            }

            if (SetProperty(ref _selectedCoordinateMode, value))
            {
                if (!_isInitializing)
                {
                    _ = _settingsService.SetAsync(CoordinateModeKey, value);
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
    public bool HasNoHubWormholeOverlayData => _hubWormholeCardsForView.Count == 0;
    public bool HasNoIncursionOverlayData => _incursionCardsForView.Count == 0;
    public bool HasNoStormOverlayData => _stormCardsForView.Count == 0;
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

            if (_isStormsOverlayOpen)
            {
                _isStormsOverlayOpen = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStormsOverlayOpen)));
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

        _jumpRangeOriginsLyByNodeId[nodeId] = lightYears;
        RebuildJumpRangeOverlay();
        return true;
    }

    public bool RemoveJumpRangeOrigin(long nodeId)
    {
        if (!_jumpRangeOriginsLyByNodeId.Remove(nodeId))
        {
            return false;
        }

        RebuildJumpRangeOverlay();
        return true;
    }

    public void ClearJumpRangeOrigins()
    {
        if (_jumpRangeOriginsLyByNodeId.Count == 0 && _jumpRangeInRangeNodeIdsForView.Count == 0)
        {
            return;
        }

        _jumpRangeOriginsLyByNodeId.Clear();
        _jumpRangeOriginColorByNodeId.Clear();
        ClearLyCoverageHighlights();
        RebuildJumpRangeOverlay();
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
        var selectedToken = SelectedRegion is null
            ? null
            : new SavedRegionToken
            {
                RegionName = SelectedRegion.RegionName,
                Kind = SelectedRegion.Kind
            };

        _allRegions = (await _mapDataService.GetRegionsAsync()).ToList();
        ApplyRegionFilter();

        SelectedRegion = (selectedId is not null ? Regions.FirstOrDefault(r => !r.IsHeader && r.RegionId == selectedId.Value) : null)
            ?? FindRegionByToken(selectedToken)
            ?? GetFirstRegularRegionOption()
            ?? Regions.FirstOrDefault(r => !r.IsHeader);
    }

    private async Task LoadAsync()
    {
        _allRegions = (await _mapDataService.GetRegionsAsync()).ToList();
        ApplyRegionFilter();

        SelectedCoordinateMode = await _settingsService.GetAsync<MapCoordinateMode?>(CoordinateModeKey) ?? MapCoordinateMode.SdePlanarXY;
        StretchMapToWindow = await _settingsService.GetAsync<bool?>(StretchMapToWindowKey) ?? false;
        NodeColorMode = await _settingsService.GetAsync<MapNodeColorMode?>(NodeColorModeKey) ?? MapNodeColorMode.None;
        NodeBackgroundColorMode = await _settingsService.GetAsync<MapNodeColorMode?>(NodeBackgroundColorModeKey) ?? MapNodeColorMode.None;
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
        ShowIndicatorJumpRangeLy = await _settingsService.GetAsync<bool?>(ShowIndicatorJumpRangeLyKey) ?? true;
        ShowAnsiblexNetwork = await _settingsService.GetAsync<bool?>(ShowAnsiblexNetworkKey) ?? true;
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
        InfoBoxShowJumpRangeLy = await _settingsService.GetAsync<bool?>(InfoBoxShowJumpRangeLyKey) ?? true;
        await _sovUpgradeStateService.InitializeAsync();
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
        SelectedViewMode = await _settingsService.GetAsync<MapViewMode?>(ViewModeKey) ?? MapViewMode.Universe;
        EnforceCoordinateModeForView();

        var savedRegionId = await _settingsService.GetAsync<int?>(RegionIdKey);
        var savedRegionToken = await _settingsService.GetAsync<SavedRegionToken>(RegionTokenKey);
        SelectedRegion = _allRegions.FirstOrDefault(r => r.RegionId == savedRegionId)
            ?? FindRegionByToken(savedRegionToken)
            ?? GetFirstRegularRegionOption()
            ?? Regions.FirstOrDefault(r => !r.IsHeader);

        _isInitializing = false;
        await ReloadGraphAsync();
    }

    private async Task ReloadGraphAsync()
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
            await RebuildActivityCardsAsync(graph);
            SelectedNodeId = null;
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
            await RebuildActivityCardsAsync(CurrentGraph);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void OnStormSnapshotUpdated(object? sender, StormSnapshot snapshot)
    {
        if (_isInitializing)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync();
        });
    }

    private void OnHubWormholeSnapshotUpdated(object? sender, HubWormholeSnapshot snapshot)
    {
        if (_isInitializing)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync();
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
            await ReloadGraphAsync();
        });
    }

    private void OnIncursionSnapshotUpdated(object? sender, IncursionSnapshot snapshot)
    {
        if (_isInitializing)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            await ReloadGraphAsync();
        });
    }

    private void OnAnsiblexNetworkSnapshotUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RebuildAnsiblexLinksForView(CurrentGraph);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnsiblexLinksForView)));
        });
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
            var uri = new Uri($"avares://Hisa.App/Assets/Icons/SOV Upgrades/{fileName}");
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

        if (SelectedViewMode == MapViewMode.Region && pick.RegionId is not null)
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
            SolarSystemId = pick.SolarSystemId
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
        var term = RegionSearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(term)
            ? _allRegions
            : _allRegions.Where(r => r.RegionName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

        var selectedId = SelectedRegion?.RegionId;
        Regions.Clear();
        // Keep user-created custom regions as the first group in the selector.
        AddRegionGroup(RegionOptionKind.Custom, "Custom Regions", filtered);
        AddRegionGroup(RegionOptionKind.Combined, "Combined Regions", filtered);
        AddRegionGroup(RegionOptionKind.Regular, "Regular Regions", filtered);

        if (selectedId is not null)
        {
            SelectedRegion = Regions.FirstOrDefault(r => r.RegionId == selectedId.Value)
                ?? GetFirstRegularRegionOption()
                ?? Regions.FirstOrDefault(r => !r.IsHeader);
        }
        else if (SelectedRegion is null)
        {
            SelectedRegion = GetFirstRegularRegionOption() ?? Regions.FirstOrDefault(r => !r.IsHeader);
        }
    }

    private void AddRegionGroup(RegionOptionKind kind, string header, IReadOnlyCollection<RegionOption> source)
    {
        var items = source
            .Where(r => !r.IsHeader && r.Kind == kind)
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

    private Task SaveSelectedRegionTokenAsync(RegionOption? region)
    {
        if (region is null || region.IsHeader)
        {
            return _settingsService.SetAsync<SavedRegionToken?>(RegionTokenKey, null);
        }

        var token = new SavedRegionToken
        {
            RegionName = region.RegionName,
            Kind = region.Kind
        };
        return _settingsService.SetAsync(RegionTokenKey, token);
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
            if (_jumpRangeOriginsLyByNodeId.Count > 0)
            {
                _jumpRangeOriginsLyByNodeId.Clear();
            }
            if (_jumpRangeOriginColorByNodeId.Count > 0)
            {
                _jumpRangeOriginColorByNodeId.Clear();
            }

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

        var nodeById = CurrentGraph.Nodes.ToDictionary(n => n.Id);
        var removedAny = false;
        foreach (var originId in _jumpRangeOriginsLyByNodeId.Keys.ToList())
        {
            if (!nodeById.TryGetValue(originId, out var originNode) || !HasSdePosition(originNode))
            {
                _jumpRangeOriginsLyByNodeId.Remove(originId);
                _jumpRangeOriginColorByNodeId.Remove(originId);
                removedAny = true;
            }
        }

        if (_jumpRangeOriginsLyByNodeId.Count == 0)
        {
            _jumpRangeInRangeNodeIdsForView = [];
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
            .Where(nodeById.ContainsKey)
            .Select(originId => new JumpRangeOriginDisplay
            {
                NodeId = originId,
                SystemName = nodeById[originId].Name,
                RangeLy = _jumpRangeOriginsLyByNodeId[originId],
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
                foreach (var (originId, maxLy) in _jumpRangeOriginsLyByNodeId)
                {
                    if (!nodeById.TryGetValue(originId, out var originNode))
                    {
                        continue;
                    }

                    if (originId == targetNode.Id)
                    {
                        AddJumpRangeDistance(targetNode, originNode, originId, maxLy, 0);
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

                    var distanceLy = GetDistanceLy(originNode, targetNode);
                    if (distanceLy < 0)
                    {
                        continue;
                    }

                    var isInRange = distanceLy > 0 && distanceLy < maxLy;
                    AddJumpRangeDistance(targetNode, originNode, originId, maxLy, distanceLy);
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
        if (removedAny)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(JumpRangeOriginNodeIdsForView)));
        }

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

        if (allSystemIds.Count == 0)
        {
            _hubWormholeCardsForView = [];
            _incursionCardsForView = [];
            _stormCardsForView = [];
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormCardsForView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormOverlayTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHubWormholeOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIncursionOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStormOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoHubWormholeOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIncursionOverlayData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoStormOverlayData)));
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var metadataById = await _mapDataService.GetSystemMetadataByIdsAsync(allSystemIds);

        _hubWormholeCardsForView = wormholeBySystem
            .Where(kvp => kvp.Value.Count > 0)
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

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormCardsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HubWormholeOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IncursionOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StormOverlayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHubWormholeOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIncursionOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStormOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoHubWormholeOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoIncursionOverlayData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoStormOverlayData)));
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

    private void AddJumpRangeDistance(MapNode targetNode, MapNode originNode, long originId, double maxLy, double distanceLy)
    {
        if (!_jumpRangeDistancesByNodeId.TryGetValue(targetNode.Id, out var values))
        {
            values = [];
            _jumpRangeDistancesByNodeId[targetNode.Id] = values;
        }

        values.Add(new JumpRangeDistanceDisplay
        {
            OriginNodeId = originId,
            OriginSystemName = originNode.Name,
            DistanceLy = distanceLy,
            MaxLy = maxLy,
            IsInRange = distanceLy == 0 || (distanceLy > 0 && distanceLy < maxLy)
        });
    }

    private static double GetDistanceLy(MapNode from, MapNode to)
    {
        if (from.PositionX is double fromX &&
            from.PositionY is double fromY &&
            from.PositionZ is double fromZ &&
            to.PositionX is double toX &&
            to.PositionY is double toY &&
            to.PositionZ is double toZ)
        {
            var dx3 = toX - fromX;
            var dy3 = toY - fromY;
            var dz3 = toZ - fromZ;
            return Math.Sqrt((dx3 * dx3) + (dy3 * dy3) + (dz3 * dz3)) / 9_460_000_000_000_000.0;
        }

        return -1;
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
        if (SelectedViewMode == MapViewMode.UniverseRegions && SelectedCoordinateMode != MapCoordinateMode.SdePlanarXY)
        {
            _selectedCoordinateMode = MapCoordinateMode.SdePlanarXY;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCoordinateMode)));
            _ = _settingsService.SetAsync(CoordinateModeKey, MapCoordinateMode.SdePlanarXY);
        }

        EnforceCoordinateModeForSelectedRegion();
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
        _ = _settingsService.SetAsync(CoordinateModeKey, MapCoordinateMode.SdePlanarXY);
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
