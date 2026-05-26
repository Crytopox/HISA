using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IMapDataService _mapDataService;
    private readonly ISettingsService _settingsService;
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
    private bool _stretchMapToWindow = true;
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
    private bool _infoBoxShowRegion = true;
    private bool _infoBoxShowConstellation = true;
    private bool _infoBoxShowSecurityStatus = true;
    private bool _infoBoxShowStarClass;
    private bool _infoBoxShowA0StarIcon = true;
    private bool _infoBoxShowJoveObservatoryIcon = true;
    private bool _infoBoxShowIceBeltsIcon = true;
    private CancellationTokenSource? _searchSuggestionsCts;
    private bool _isInitializing = true;
    private const string ViewModeKey = "Map.SelectedViewMode";
    private const string RegionIdKey = "Map.SelectedRegionId";
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
    private const string InfoBoxShowRegionKey = "Map.InfoBoxShowRegion";
    private const string InfoBoxShowConstellationKey = "Map.InfoBoxShowConstellation";
    private const string InfoBoxShowSecurityStatusKey = "Map.InfoBoxShowSecurityStatus";
    private const string InfoBoxShowStarClassKey = "Map.InfoBoxShowStarClass";
    private const string InfoBoxShowA0StarIconKey = "Map.InfoBoxShowA0StarIcon";
    private const string InfoBoxShowJoveObservatoryIconKey = "Map.InfoBoxShowJoveObservatoryIcon";
    private const string InfoBoxShowIceBeltsIconKey = "Map.InfoBoxShowIceBeltsIcon";
    private const string WindowPlacementKey = "Window.Main.Placement";
    private const string MapViewportPrefixKey = "Map.Viewport";
    private readonly Task _initialLoadTask;

    public MainWindowViewModel(IMapDataService mapDataService, ISettingsService settingsService)
    {
        _mapDataService = mapDataService;
        _settingsService = settingsService;
        ViewModes = new ObservableCollection<MapViewMode>(Enum.GetValues<MapViewMode>());
        CoordinateModes = new ObservableCollection<MapCoordinateMode>(Enum.GetValues<MapCoordinateMode>());
        NodeColorModes = new ObservableCollection<MapNodeColorMode>(Enum.GetValues<MapNodeColorMode>());
        Regions = [];
        _initialLoadTask = LoadAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MapViewMode> ViewModes { get; }
    public ObservableCollection<MapCoordinateMode> CoordinateModes { get; }
    public ObservableCollection<MapNodeColorMode> NodeColorModes { get; }
    public ObservableCollection<RegionOption> Regions { get; }
    public ObservableCollection<MapSearchCandidate> SearchSuggestions { get; } = [];

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

    public RegionOption? SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (SetProperty(ref _selectedRegion, value) && SelectedViewMode == MapViewMode.Region)
            {
                if (!_isInitializing)
                {
                    _ = _settingsService.SetAsync(RegionIdKey, value?.RegionId);
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

    private async Task LoadAsync()
    {
        _allRegions = (await _mapDataService.GetRegionsAsync()).ToList();
        ApplyRegionFilter();

        SelectedCoordinateMode = await _settingsService.GetAsync<MapCoordinateMode?>(CoordinateModeKey) ?? MapCoordinateMode.SdePlanarXY;
        StretchMapToWindow = await _settingsService.GetAsync<bool?>(StretchMapToWindowKey) ?? true;
        NodeColorMode = await _settingsService.GetAsync<MapNodeColorMode?>(NodeColorModeKey) ?? MapNodeColorMode.None;
        NodeBackgroundColorMode = await _settingsService.GetAsync<MapNodeColorMode?>(NodeBackgroundColorModeKey) ?? MapNodeColorMode.None;
        ShowIndicatorRegion = await _settingsService.GetAsync<bool?>(ShowIndicatorRegionKey) ?? false;
        ShowIndicatorConstellation = await _settingsService.GetAsync<bool?>(ShowIndicatorConstellationKey) ?? false;
        ShowIndicatorSecurityStatus = await _settingsService.GetAsync<bool?>(ShowIndicatorSecurityStatusKey) ?? false;
        ShowIndicatorStarClass = await _settingsService.GetAsync<bool?>(ShowIndicatorStarClassKey) ?? false;
        ShowIndicatorA0StarIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorA0StarIconKey) ?? true;
        ShowIndicatorJoveObservatoryIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorJoveObservatoryIconKey) ?? true;
        ShowIndicatorIceBeltsIcon = await _settingsService.GetAsync<bool?>(ShowIndicatorIceBeltsIconKey) ?? true;
        InfoBoxShowRegion = await _settingsService.GetAsync<bool?>(InfoBoxShowRegionKey) ?? true;
        InfoBoxShowConstellation = await _settingsService.GetAsync<bool?>(InfoBoxShowConstellationKey) ?? true;
        InfoBoxShowSecurityStatus = await _settingsService.GetAsync<bool?>(InfoBoxShowSecurityStatusKey) ?? true;
        InfoBoxShowStarClass = await _settingsService.GetAsync<bool?>(InfoBoxShowStarClassKey) ?? false;
        InfoBoxShowA0StarIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowA0StarIconKey) ?? true;
        InfoBoxShowJoveObservatoryIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowJoveObservatoryIconKey) ?? true;
        InfoBoxShowIceBeltsIcon = await _settingsService.GetAsync<bool?>(InfoBoxShowIceBeltsIconKey) ?? true;
        SelectedViewMode = await _settingsService.GetAsync<MapViewMode?>(ViewModeKey) ?? MapViewMode.Universe;
        EnforceCoordinateModeForView();

        var savedRegionId = await _settingsService.GetAsync<int?>(RegionIdKey);
        SelectedRegion = _allRegions.FirstOrDefault(r => r.RegionId == savedRegionId) ?? Regions.FirstOrDefault();

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
            SelectedNodeId = null;
            StatusText = $"Mode: {SelectedViewMode} | Coordinates: {SelectedCoordinateMode} | Nodes: {graph.Nodes.Count} | Links: {graph.Links.Count}";
            _ = _settingsService.SetAsync(ViewModeKey, SelectedViewMode);
            _ = _settingsService.SetAsync(RegionIdKey, SelectedRegion?.RegionId);
        }
        catch (Exception ex)
        {
            StatusText = $"Map load error: {ex.Message}";
            CurrentGraph = new MapGraph { Nodes = [], Links = [] };
        }
        finally
        {
            _isBusy = false;
        }
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
        foreach (var region in filtered)
        {
            Regions.Add(region);
        }

        if (selectedId is not null)
        {
            SelectedRegion = Regions.FirstOrDefault(r => r.RegionId == selectedId.Value) ?? Regions.FirstOrDefault();
        }
        else if (SelectedRegion is null)
        {
            SelectedRegion = Regions.FirstOrDefault();
        }
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
