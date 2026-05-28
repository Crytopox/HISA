using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private bool _showEditorRegionLabel = true;
    private MapGraph _currentGraph = new() { Nodes = [], Links = [] };
    private const double SnapGridStep = 0.01;
    private const string RegionExchangeFormat = "hisa-region-v1";

    private sealed class EditableNode
    {
        public required long Id { get; init; }
        public required string Name { get; set; }
        public required double X { get; set; }
        public required double Y { get; set; }
        public double? Security { get; set; }
        public int? RegionId { get; set; }
        public string? RegionName { get; set; }
        public int? ConstellationId { get; set; }
        public string? ConstellationName { get; set; }
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
    public IEnumerable<long> SelectedNodeIdsForView { get; private set; } = [];
    public IEnumerable<long> MissingConnectionNodeIdsForView { get; private set; } = [];
    public IEnumerable<long> CrossRegionConnectorNodeIdsForView { get; private set; } = [];
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionEditable)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionReadOnly)));
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
                RefreshSelectedNodeIdsForView();
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

    public bool ShowEditorRegionLabel
    {
        get => _showEditorRegionLabel;
        set => SetProperty(ref _showEditorRegionLabel, value);
    }

    public bool IsSelectedLayoutRegionEditable => SelectedLayoutRegion is not null && !SelectedLayoutRegion.IsReadOnly;
    public bool IsSelectedLayoutRegionReadOnly => SelectedLayoutRegion is not null && SelectedLayoutRegion.IsReadOnly;

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

        if (SelectedLayoutRegion.IsReadOnly)
        {
            StatusText = "Read-only layout region. Create or select a custom region to edit.";
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

        if (SelectedLayoutRegion.IsReadOnly)
        {
            StatusText = "Read-only layout region. Import into a custom region instead.";
            return;
        }

        try
        {
            // Persist current in-editor changes first so import appends on top
            // of the latest edited state (including deletions/moves).
            await _mapLayoutEditorService.SaveLayoutRegionGraphAsync(SelectedLayoutRegion.Id, CurrentGraph);
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

        if (SelectedLayoutRegion.IsReadOnly)
        {
            StatusText = "Read-only layout region. Save is only available for custom regions.";
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

    public async Task RenameSelectedLayoutRegionAsync(string newName)
    {
        if (SelectedLayoutRegion is null)
        {
            StatusText = "Select a layout region first.";
            return;
        }

        if (SelectedLayoutRegion.IsReadOnly)
        {
            StatusText = "Read-only layout region. Rename is only available for custom regions.";
            return;
        }

        try
        {
            var regionId = SelectedLayoutRegion.Id;
            var oldName = SelectedLayoutRegion.Name;
            await _mapLayoutEditorService.RenameLayoutRegionAsync(SelectedLayoutRegion.Id, newName);
            await ReloadAsync();
            SelectedLayoutRegion = LayoutRegions.FirstOrDefault(r => r.Id == regionId);
            StatusText = $"Renamed layout region: {oldName} -> {newName.Trim()}";
        }
        catch (Exception ex)
        {
            StatusText = $"Rename layout failed: {ex.Message}";
        }
    }

    public async Task ExportSelectedRegionAsync(string filePath)
    {
        if (SelectedLayoutRegion is null)
        {
            StatusText = "Select a layout region first.";
            return;
        }

        try
        {
            var graph = await _mapLayoutEditorService.GetLayoutRegionGraphAsync(SelectedLayoutRegion.Id)
                ?? CurrentGraph;

            var payload = new RegionExchangePayload
            {
                Format = RegionExchangeFormat,
                ExportedAtUtc = DateTime.UtcNow,
                RegionName = SelectedLayoutRegion.Name,
                Nodes = graph.Nodes.Select(n => new RegionExchangeNode
                {
                    Id = n.Id,
                    Name = n.Name,
                    X = n.X,
                    Y = n.Y,
                    Security = n.Security,
                    RegionId = n.RegionId,
                    RegionName = n.RegionName,
                    ConstellationId = n.ConstellationId,
                    ConstellationName = n.ConstellationName
                }).ToList(),
                Links = graph.Links.Select(l => new RegionExchangeLink { FromId = l.FromId, ToId = l.ToId }).ToList()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            StatusText = $"Exported custom region to {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    public async Task ImportRegionJsonAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var payload = JsonSerializer.Deserialize<RegionExchangePayload>(json);
            if (payload is null || payload.Format != RegionExchangeFormat)
            {
                throw new InvalidOperationException("Unsupported region format.");
            }

            var regionNameBase = string.IsNullOrWhiteSpace(payload.RegionName)
                ? Path.GetFileNameWithoutExtension(filePath)
                : payload.RegionName.Trim();
            var regionName = EnsureUniqueRegionName(regionNameBase);
            var created = await _mapLayoutEditorService.CreateCustomRegionAsync(regionName);

            var importedGraph = new MapGraph
            {
                Nodes = payload.Nodes.Select(n => new MapNode
                {
                    Id = n.Id,
                    Name = n.Name,
                    X = n.X,
                    Y = n.Y,
                    Security = n.Security,
                    SunTypeId = null,
                    StarTypeName = null,
                    SpectralClass = null,
                    HasJoveObservatory = false,
                    IceFieldCount = 0,
                    RegionId = n.RegionId,
                    RegionName = n.RegionName,
                    ConstellationId = n.ConstellationId,
                    ConstellationName = n.ConstellationName,
                    StormEffects = [],
                    HubWormholeConnections = [],
                    SovUpgrades = []
                }).ToList(),
                Links = payload.Links.Select(l => new MapLink { FromId = l.FromId, ToId = l.ToId }).ToList()
            };

            await _mapLayoutEditorService.SaveLayoutRegionGraphAsync(created.Id, importedGraph);
            await ReloadAsync();
            SelectedLayoutRegion = LayoutRegions.FirstOrDefault(r => r.Id == created.Id);
            StatusText = $"Imported custom region: {created.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Import JSON failed: {ex.Message}";
        }
    }


    public bool MoveSelectedNodeAsync(double dx, double dy, bool freeMove = false)
    {
        if (!IsSelectedLayoutRegionEditable)
        {
            StatusText = "Read-only layout region. Node movement is disabled.";
            return false;
        }

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
        if (!IsSelectedLayoutRegionEditable)
        {
            StatusText = "Read-only layout region. Node deletion is disabled.";
            return;
        }

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
            RefreshSelectedNodeIdsForView();
            await RebuildGraphAsync(keepSelection: false);
            StatusText = "Node deleted.";
        }
    }

    public async Task AddMissingConnectedNodesForSelectionAsync()
    {
        if (!IsSelectedLayoutRegionEditable)
        {
            StatusText = "Read-only layout region. Add missing connected nodes is disabled.";
            return;
        }

        var selectedSystemIds = _selectedNodeIds.Where(id => id > 0).ToList();
        if (selectedSystemIds.Count == 0)
        {
            StatusText = "Select one or more system nodes first.";
            return;
        }

        var existingSystemIds = _editableNodesById.Keys.Where(id => id > 0).ToList();
        var existingPositions = _editableNodesById
            .Where(kvp => kvp.Key > 0)
            .ToDictionary(kvp => kvp.Key, kvp => (kvp.Value.X, kvp.Value.Y));
        var missingNodes = await _mapLayoutEditorService.GetMissingConnectedSystemsAsync(
            selectedSystemIds,
            existingSystemIds,
            existingPositions);
        var added = 0;
        foreach (var node in missingNodes)
        {
            if (_editableNodesById.ContainsKey(node.Id))
            {
                continue;
            }

            _editableNodesById[node.Id] = new EditableNode
            {
                Id = node.Id,
                Name = node.Name,
                X = node.X,
                Y = node.Y,
                Security = node.Security,
                RegionId = node.RegionId,
                RegionName = node.RegionName,
                ConstellationId = node.ConstellationId,
                ConstellationName = node.ConstellationName
            };
            added++;
        }

        if (added == 0)
        {
            StatusText = "No missing connected nodes found.";
            return;
        }

        await RebuildGraphAsync(keepSelection: true);
        StatusText = $"Added {added} connected node(s).";
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
            foreach (var layoutRegion in layoutRegions.Where(r => !r.IsReadOnly).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                LayoutRegions.Add(layoutRegion);
            }

            if (SelectedLayoutRegion is null && LayoutRegions.Count > 0)
            {
                SelectedLayoutRegion = LayoutRegions[0];
            }
            else if (SelectedLayoutRegion is not null && LayoutRegions.All(r => r.Id != SelectedLayoutRegion.Id))
            {
                SelectedLayoutRegion = LayoutRegions.FirstOrDefault();
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
                Y = node.Y,
                Security = node.Security,
                RegionId = node.RegionId,
                RegionName = node.RegionName,
                ConstellationId = node.ConstellationId,
                ConstellationName = node.ConstellationName
            };
        }

        SelectedNodeId = null;
        _selectedNodeIds.Clear();
        RefreshSelectedNodeIdsForView();
        CurrentGraph = graph;
        await RefreshEditorDiagnosticsAsync();
        StatusText = $"Loaded: {SelectedLayoutRegion.Name} | Nodes: {graph.Nodes.Count} | Links: {graph.Links.Count}";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionStatus)));
    }

    private async Task RebuildGraphAsync(bool keepSelection)
    {
        var existingById = CurrentGraph.Nodes.ToDictionary(n => n.Id);
        var nodes = _editableNodesById.Values
            .Select(n =>
            {
                if (existingById.TryGetValue(n.Id, out var existing))
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
                    Security = n.Security,
                    SunTypeId = null,
                    StarTypeName = null,
                    SpectralClass = null,
                    HasJoveObservatory = false,
                    IceFieldCount = 0,
                    RegionId = n.RegionId,
                    RegionName = n.RegionName,
                    ConstellationId = n.ConstellationId,
                    ConstellationName = n.ConstellationName,
                    StormEffects = [],
                    HubWormholeConnections = []
                };
            })
            .ToList();

        var links = await _mapLayoutEditorService.BuildAutoLinksForSystemsAsync(nodes.Where(n => n.Id > 0).Select(n => n.Id).ToHashSet());
        CurrentGraph = new MapGraph
        {
            Nodes = nodes,
            Links = links
        };
        await RefreshEditorDiagnosticsAsync();

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
                    Security = n.Security,
                    SunTypeId = null,
                    StarTypeName = null,
                    SpectralClass = null,
                    HasJoveObservatory = false,
                    IceFieldCount = 0,
                    RegionId = n.RegionId,
                    RegionName = n.RegionName,
                    ConstellationId = n.ConstellationId,
                    ConstellationName = n.ConstellationName,
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
        await RefreshEditorDiagnosticsAsync();
    }

    private string EnsureUniqueRegionName(string baseName)
    {
        var candidate = string.IsNullOrWhiteSpace(baseName) ? "Imported Region" : baseName.Trim();
        var existing = LayoutRegions.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(candidate))
        {
            return candidate;
        }

        for (var i = 2; i < 1000; i++)
        {
            var next = $"{candidate} ({i})";
            if (!existing.Contains(next))
            {
                return next;
            }
        }

        return $"{candidate} ({DateTime.UtcNow:yyyyMMddHHmmss})";
    }


    private sealed class RegionExchangePayload
    {
        public required string Format { get; init; }
        public required DateTime ExportedAtUtc { get; init; }
        public required string RegionName { get; init; }
        public required List<RegionExchangeNode> Nodes { get; init; }
        public required List<RegionExchangeLink> Links { get; init; }
    }

    private sealed class RegionExchangeNode
    {
        public long Id { get; init; }
        public required string Name { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double? Security { get; init; }
        public int? RegionId { get; init; }
        public string? RegionName { get; init; }
        public int? ConstellationId { get; init; }
        public string? ConstellationName { get; init; }
    }

    private sealed class RegionExchangeLink
    {
        public long FromId { get; init; }
        public long ToId { get; init; }
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionEditable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionReadOnly)));
        RefreshSelectedNodeIdsForView();
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionEditable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionReadOnly)));
        RefreshSelectedNodeIdsForView();
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionEditable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionReadOnly)));
        RefreshSelectedNodeIdsForView();
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionEditable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedLayoutRegionReadOnly)));
        RefreshSelectedNodeIdsForView();
    }

    public IReadOnlyCollection<long> GetSelectedNodeIds() => _selectedNodeIds;
    public bool IsNodeSelected(long nodeId) => _selectedNodeIds.Contains(nodeId);

    public double GetSnapGridStep() => SnapGridStep;

    private void RefreshSelectedNodeIdsForView()
    {
        SelectedNodeIdsForView = _selectedNodeIds.ToArray();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeIdsForView)));
    }

    private async Task RefreshEditorDiagnosticsAsync()
    {
        var presentById = CurrentGraph.Nodes.ToDictionary(n => n.Id);
        var presentSystemIds = presentById.Keys.Where(x => x > 0).ToHashSet();

        var crossRegionConnectorIds = new HashSet<long>();
        foreach (var link in CurrentGraph.Links)
        {
            if (!presentById.TryGetValue(link.FromId, out var fromNode) ||
                !presentById.TryGetValue(link.ToId, out var toNode))
            {
                continue;
            }

            if (fromNode.RegionId is not null &&
                toNode.RegionId is not null &&
                fromNode.RegionId != toNode.RegionId)
            {
                crossRegionConnectorIds.Add(fromNode.Id);
                crossRegionConnectorIds.Add(toNode.Id);
            }
        }

        var missingConnectionIds = new HashSet<long>();
        if (presentSystemIds.Count > 0)
        {
            var neighborCounts = await _mapLayoutEditorService.GetSystemNeighborCountsAsync(presentSystemIds);
            var presentNeighborCounts = new Dictionary<long, int>();
            foreach (var id in presentSystemIds)
            {
                presentNeighborCounts[id] = 0;
            }

            foreach (var link in CurrentGraph.Links)
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

            foreach (var id in presentSystemIds)
            {
                var total = neighborCounts.TryGetValue(id, out var totalCount) ? totalCount : 0;
                var present = presentNeighborCounts.TryGetValue(id, out var presentCount) ? presentCount : 0;
                if (total > present)
                {
                    missingConnectionIds.Add(id);
                }
            }
        }

        CrossRegionConnectorNodeIdsForView = crossRegionConnectorIds.ToArray();
        MissingConnectionNodeIdsForView = missingConnectionIds.ToArray();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CrossRegionConnectorNodeIdsForView)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MissingConnectionNodeIdsForView)));
    }
}
