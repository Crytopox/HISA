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
    private bool _alwaysShowHubWormholes = true;
    private bool _showMissingConnectionMarkers = true;
    private HubWormholeMarkerMode _hubWormholeMarkerMode = HubWormholeMarkerMode.Badge;
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
    private const string IndicatorSovFilterKeysKey = "Map.IndicatorSovFilter.Keys";
    private const string OverlaySovFilterKeysKey = "Map.OverlaySovFilter.Keys";
    private const string IndicatorSovFilterConfiguredKey = "Map.IndicatorSovFilter.Configured";
    private const string OverlaySovFilterConfiguredKey = "Map.OverlaySovFilter.Configured";
    private const string AlwaysShowHubWormholesKey = "Map.AlwaysShowHubWormholes";
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
        ISovUpgradeStateService sovUpgradeStateService)
    {
        _mapDataService = mapDataService;
        _settingsService = settingsService;
        _stormStateService = stormStateService;
        _hubWormholeStateService = hubWormholeStateService;
        _sovUpgradeStateService = sovUpgradeStateService;
        ViewModes = new ObservableCollection<MapViewMode>(Enum.GetValues<MapViewMode>());
        CoordinateModes = new ObservableCollection<MapCoordinateMode>(Enum.GetValues<MapCoordinateMode>());
        NodeColorModes = new ObservableCollection<MapNodeColorMode>(Enum.GetValues<MapNodeColorMode>());
        HubWormholeMarkerModes = new ObservableCollection<HubWormholeMarkerMode>(Enum.GetValues<HubWormholeMarkerMode>());
        Regions = [];
        _stormStateService.StormSnapshotUpdated += OnStormSnapshotUpdated;
        _hubWormholeStateService.HubWormholeSnapshotUpdated += OnHubWormholeSnapshotUpdated;
        _sovUpgradeStateService.SnapshotUpdated += OnSovUpgradesSnapshotUpdated;
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
    public Task InitialLoadTask => _initialLoadTask;

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
        await _sovUpgradeStateService.InitializeAsync();
        InitializeSovFilterOptions();
        var indicatorKeys = await _settingsService.GetAsync<List<string>>(IndicatorSovFilterKeysKey) ?? [];
        var overlayKeys = await _settingsService.GetAsync<List<string>>(OverlaySovFilterKeysKey) ?? [];
        var indicatorConfigured = await _settingsService.GetAsync<bool?>(IndicatorSovFilterConfiguredKey) ?? false;
        var overlayConfigured = await _settingsService.GetAsync<bool?>(OverlaySovFilterConfiguredKey) ?? false;
        ApplySelectedSovKeys(IndicatorSovUpgradeOptions, indicatorKeys, indicatorConfigured);
        ApplySelectedSovKeys(OverlaySovUpgradeOptions, overlayKeys, overlayConfigured);
        AlwaysShowHubWormholes = await _settingsService.GetAsync<bool?>(AlwaysShowHubWormholesKey) ?? true;
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
            await RefreshRegionMissingConnectionMarkersAsync(graph);
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
