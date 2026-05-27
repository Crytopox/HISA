using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class MapEditorViewModel : INotifyPropertyChanged
{
    private readonly IMapDataService _mapDataService;
    private readonly IMapLayoutDataService _mapLayoutDataService;
    private readonly IMapLayoutEditorService _mapLayoutEditorService;
    private readonly Dictionary<long, EditableNode> _editableNodesById = [];
    private readonly HashSet<long> _selectedNodeIds = [];
    private MapLayoutRegionSummary? _selectedLayoutRegion;
    private long? _selectedNodeId;
    private string _newRegionName = string.Empty;
    private string _statusText = "Loading map editor data...";
    private MapGraph _currentGraph = new() { Nodes = [], Links = [] };
    private const double SnapGridStep = 0.01;

    private sealed class EditableNode
    {
        public required long Id { get; init; }
        public required string Name { get; set; }
        public required double X { get; set; }
        public required double Y { get; set; }
    }

    public MapEditorViewModel(
        IMapDataService mapDataService,
        IMapLayoutDataService mapLayoutDataService,
        IMapLayoutEditorService mapLayoutEditorService)
    {
        _mapDataService = mapDataService;
        _mapLayoutDataService = mapLayoutDataService;
        _mapLayoutEditorService = mapLayoutEditorService;
        InitialLoadTask = LoadAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RegionOption> GameRegions { get; } = [];
    public ObservableCollection<MapLayoutRegionSummary> LayoutRegions { get; } = [];
    public Task InitialLoadTask { get; }

    public string NewRegionName
    {
        get => _newRegionName;
        set => SetProperty(ref _newRegionName, value);
    }

    public MapLayoutRegionSummary? SelectedLayoutRegion
    {
        get => _selectedLayoutRegion;
        set
        {
            if (SetProperty(ref _selectedLayoutRegion, value))
            {
                _ = LoadSelectedLayoutRegionAsync();
            }
        }
    }

    public long? SelectedNodeId
    {
        get => _selectedNodeId;
        set
        {
            if (SetProperty(ref _selectedNodeId, value))
            {
                if (value is null)
                {
                    _selectedNodeIds.Clear();
                }
                else if (!_selectedNodeIds.Contains(value.Value))
                {
                    _selectedNodeIds.Clear();
                    _selectedNodeIds.Add(value.Value);
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
            }
        }
    }

    public MapGraph CurrentGraph
    {
        get => _currentGraph;
        private set => SetProperty(ref _currentGraph, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SelectionStatus => _selectedNodeIds.Count == 0
        ? "Selection: none"
        : $"Selection: {_selectedNodeIds.Count} node(s)";

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task CreateCustomRegionAsync()
    {
        try
        {
            var created = await _mapLayoutEditorService.CreateCustomRegionAsync(NewRegionName);
            await ReloadAsync();
            SelectedLayoutRegion = LayoutRegions.FirstOrDefault(r => r.Id == created.Id);
            NewRegionName = string.Empty;
            StatusText = $"Created layout region: {created.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Create custom region failed: {ex.Message}";
        }
    }

    public async Task DeleteSelectedLayoutRegionAsync()
    {
        if (SelectedLayoutRegion is null)
        {
            StatusText = "Select a layout region first.";
            return;
        }

        try
        {
            var regionName = SelectedLayoutRegion.Name;
            await _mapLayoutEditorService.DeleteLayoutRegionAsync(SelectedLayoutRegion.Id);
            await ReloadAsync();
            StatusText = $"Deleted layout region: {regionName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Delete layout failed: {ex.Message}";
        }
    }

    public async Task ImportGameRegionsAsync(IReadOnlyList<int> sourceRegionIds)
    {
        if (SelectedLayoutRegion is null)
        {
            StatusText = "Select a layout region first.";
            return;
        }

        try
        {
            await _mapLayoutEditorService.AddGameRegionsToLayoutAsync(SelectedLayoutRegion.Id, sourceRegionIds);
            await LoadSelectedLayoutRegionAsync();
            StatusText = $"Imported {sourceRegionIds.Count} game region(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
        }
    }

    public async Task SaveCurrentRegionAsync()
    {
        if (SelectedLayoutRegion is null)
        {
            StatusText = "Select a layout region first.";
            return;
        }

        try
        {
            await RebuildAutoLinksAsync();
            await _mapLayoutEditorService.SaveLayoutRegionGraphAsync(SelectedLayoutRegion.Id, CurrentGraph);
            StatusText = $"Saved: {SelectedLayoutRegion.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    public bool MoveSelectedNodeAsync(double dx, double dy, bool freeMove = false)
    {
        if (_selectedNodeIds.Count == 0)
        {
            StatusText = "Select a node first.";
            return false;
        }

        var changed = false;
        foreach (var selectedNodeId in _selectedNodeIds)
        {
            if (_editableNodesById.TryGetValue(selectedNodeId, out var node))
            {
                var oldX = node.X;
                var oldY = node.Y;
                node.X += dx;
                node.Y += dy;
                if (!freeMove)
                {
                    node.X = Math.Round(node.X / SnapGridStep) * SnapGridStep;
                    node.Y = Math.Round(node.Y / SnapGridStep) * SnapGridStep;
                }

                changed |= Math.Abs(node.X - oldX) > 1e-12 || Math.Abs(node.Y - oldY) > 1e-12;
            }
        }

        if (changed)
        {
            RefreshCurrentGraphFromEditableNodes(keepSelection: true);
        }

        return changed;
    }

    public async Task DeleteSelectedNodeAsync()
    {
        if (_selectedNodeIds.Count == 0)
        {
            StatusText = "Select a node first.";
            return;
        }

        var removedAny = false;
        foreach (var nodeId in _selectedNodeIds.ToList())
        {
            removedAny |= _editableNodesById.Remove(nodeId);
        }

        if (removedAny)
        {
            SelectedNodeId = null;
            _selectedNodeIds.Clear();
            await RebuildGraphAsync(keepSelection: false);
            StatusText = "Node deleted.";
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var gameRegions = await _mapDataService.GetRegionsAsync(cancellationToken);
            var layoutRegions = await _mapLayoutDataService.GetLayoutRegionsAsync(cancellationToken);

            GameRegions.Clear();
            foreach (var region in gameRegions.OrderBy(r => r.RegionName, StringComparer.OrdinalIgnoreCase))
            {
                GameRegions.Add(region);
            }

            LayoutRegions.Clear();
            foreach (var layoutRegion in layoutRegions)
            {
                LayoutRegions.Add(layoutRegion);
            }

            if (SelectedLayoutRegion is null && LayoutRegions.Count > 0)
            {
                SelectedLayoutRegion = LayoutRegions[0];
            }
            else
            {
                await LoadSelectedLayoutRegionAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Map editor load error: {ex.Message}";
        }
    }

    private async Task LoadSelectedLayoutRegionAsync()
    {
        _editableNodesById.Clear();

        if (SelectedLayoutRegion is null)
        {
            CurrentGraph = new MapGraph { Nodes = [], Links = [] };
            StatusText = "Select a layout region.";
            return;
        }

        var graph = await _mapLayoutEditorService.GetLayoutRegionGraphAsync(SelectedLayoutRegion.Id)
            ?? new MapGraph { Nodes = [], Links = [] };
        foreach (var node in graph.Nodes)
        {
            _editableNodesById[node.Id] = new EditableNode
            {
                Id = node.Id,
                Name = node.Name,
                X = node.X,
                Y = node.Y
            };
        }

        SelectedNodeId = null;
        _selectedNodeIds.Clear();
        CurrentGraph = graph;
        StatusText = $"Loaded: {SelectedLayoutRegion.Name} | Nodes: {graph.Nodes.Count} | Links: {graph.Links.Count}";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    private async Task RebuildGraphAsync(bool keepSelection)
    {
        var nodes = _editableNodesById.Values
            .Select(n => new MapNode
            {
                Id = n.Id,
                Name = n.Name,
                X = n.X,
                Y = n.Y,
                Security = null,
                SunTypeId = null,
                StarTypeName = null,
                SpectralClass = null,
                HasJoveObservatory = false,
                IceFieldCount = 0,
                RegionId = null,
                RegionName = null,
                ConstellationId = null,
                ConstellationName = null,
                StormEffects = [],
                HubWormholeConnections = []
            })
            .ToList();

        var links = await _mapLayoutEditorService.BuildAutoLinksForSystemsAsync(nodes.Where(n => n.Id > 0).Select(n => n.Id).ToHashSet());
        CurrentGraph = new MapGraph
        {
            Nodes = nodes,
            Links = links
        };

        if (!keepSelection || SelectedNodeId is null || !_editableNodesById.ContainsKey(SelectedNodeId.Value))
        {
            SelectedNodeId = null;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    private void RefreshCurrentGraphFromEditableNodes(bool keepSelection)
    {
        var nodeMap = CurrentGraph.Nodes.ToDictionary(n => n.Id);
        var nodes = _editableNodesById.Values
            .Select(n =>
            {
                if (nodeMap.TryGetValue(n.Id, out var existing))
                {
                    return new MapNode
                    {
                        Id = existing.Id,
                        Name = existing.Name,
                        X = n.X,
                        Y = n.Y,
                        Security = existing.Security,
                        SunTypeId = existing.SunTypeId,
                        StarTypeName = existing.StarTypeName,
                        SpectralClass = existing.SpectralClass,
                        HasJoveObservatory = existing.HasJoveObservatory,
                        IceFieldCount = existing.IceFieldCount,
                        RegionId = existing.RegionId,
                        RegionName = existing.RegionName,
                        ConstellationId = existing.ConstellationId,
                        ConstellationName = existing.ConstellationName,
                        StormEffects = existing.StormEffects,
                        HubWormholeConnections = existing.HubWormholeConnections
                    };
                }

                return new MapNode
                {
                    Id = n.Id,
                    Name = n.Name,
                    X = n.X,
                    Y = n.Y,
                    Security = null,
                    SunTypeId = null,
                    StarTypeName = null,
                    SpectralClass = null,
                    HasJoveObservatory = false,
                    IceFieldCount = 0,
                    RegionId = null,
                    RegionName = null,
                    ConstellationId = null,
                    ConstellationName = null,
                    StormEffects = [],
                    HubWormholeConnections = []
                };
            })
            .ToList();

        CurrentGraph = new MapGraph
        {
            Nodes = nodes,
            Links = CurrentGraph.Links
        };

        if (!keepSelection || SelectedNodeId is null || !_editableNodesById.ContainsKey(SelectedNodeId.Value))
        {
            SelectedNodeId = null;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    private async Task RebuildAutoLinksAsync()
    {
        var links = await _mapLayoutEditorService.BuildAutoLinksForSystemsAsync(CurrentGraph.Nodes.Where(n => n.Id > 0).Select(n => n.Id).ToHashSet());
        CurrentGraph = new MapGraph
        {
            Nodes = CurrentGraph.Nodes,
            Links = links
        };
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

    public void SetSelectedNodes(IReadOnlyCollection<long> nodeIds)
    {
        _selectedNodeIds.Clear();
        foreach (var nodeId in nodeIds)
        {
            if (_editableNodesById.ContainsKey(nodeId))
            {
                _selectedNodeIds.Add(nodeId);
            }
        }

        _selectedNodeId = _selectedNodeIds.Count > 0 ? _selectedNodeIds.First() : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    public void AddToSelection(long nodeId)
    {
        if (!_editableNodesById.ContainsKey(nodeId))
        {
            return;
        }

        _selectedNodeIds.Add(nodeId);
        _selectedNodeId = nodeId;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    public void ToggleSelection(long nodeId)
    {
        if (!_editableNodesById.ContainsKey(nodeId))
        {
            return;
        }

        if (_selectedNodeIds.Contains(nodeId))
        {
            _selectedNodeIds.Remove(nodeId);
        }
        else
        {
            _selectedNodeIds.Add(nodeId);
        }

        _selectedNodeId = _selectedNodeIds.Count > 0 ? _selectedNodeIds.First() : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    public void AddToSelection(IReadOnlyCollection<long> nodeIds)
    {
        foreach (var nodeId in nodeIds)
        {
            if (_editableNodesById.ContainsKey(nodeId))
            {
                _selectedNodeIds.Add(nodeId);
            }
        }

        _selectedNodeId = _selectedNodeIds.Count > 0 ? _selectedNodeIds.First() : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    public IReadOnlyCollection<long> GetSelectedNodeIds() => _selectedNodeIds;

    public double GetSnapGridStep() => SnapGridStep;
}
