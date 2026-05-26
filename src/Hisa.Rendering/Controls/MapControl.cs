using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Hisa.Core.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Triangulate;
using NetTopologySuite.Precision;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;
using NtsGeometryCollection = NetTopologySuite.Geometries.GeometryCollection;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsMultiPolygon = NetTopologySuite.Geometries.MultiPolygon;
using NtsUnaryUnionOp = NetTopologySuite.Operation.Union.UnaryUnionOp;

namespace Hisa.Rendering.Controls;

public sealed class MapControl : Control
{
    private sealed class VoronoiCacheModel
    {
        public required string Key { get; init; }
        public required Dictionary<long, List<PointDto>> Polygons { get; init; }
    }

    private sealed class PointDto
    {
        public double X { get; init; }
        public double Y { get; init; }
    }

    private static readonly string VoronoiCacheDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hisa", "voronoi-cache-v1");
    private static readonly Dictionary<string, Dictionary<long, IReadOnlyList<(double X, double Y)>>> VoronoiMemoryCache = [];
    private static readonly JsonSerializerOptions VoronoiJsonOptions = new() { WriteIndented = false };

    private static readonly Point NodeLabelOffset = new(9, 3);

    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#0D131D"));

    private static readonly Pen LinksPen = new(new ImmutableSolidColorBrush(Color.Parse("#304762")), 1);
    private static readonly Pen SameConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#4D6FA2")), 1.1);
    private static readonly Pen SameRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#3E8A7E")), 1.1);
    private static readonly Pen CrossRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#8E5C8A")), 1.1);

    private static readonly Pen HighlightedDefaultPen = new(new ImmutableSolidColorBrush(Color.Parse("#8CB3DD")), 2.2);
    private static readonly Pen HighlightedSameConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#7FA7E3")), 2.2);
    private static readonly Pen HighlightedSameRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#63C2B2")), 2.2);
    private static readonly Pen HighlightedCrossRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#C28ABB")), 2.2);
    private static readonly Pen HighlightedSearchConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#E8B75E")), 2.4);

    private static readonly Pen RegionEmphasisDefaultPen = new(new ImmutableSolidColorBrush(Color.Parse("#6F8FB6")), 1.8);
    private static readonly Pen RegionEmphasisSameConstellationPen = new(new ImmutableSolidColorBrush(Color.Parse("#6F97D2")), 1.8);
    private static readonly Pen RegionEmphasisSameRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#57AEA1")), 1.8);
    private static readonly Pen RegionEmphasisCrossRegionPen = new(new ImmutableSolidColorBrush(Color.Parse("#A2749E")), 1.8);

    private static readonly IBrush SelectedBrush = new ImmutableSolidColorBrush(Color.Parse("#E8B75E"));
    private static readonly IBrush HoveredBrush = new ImmutableSolidColorBrush(Color.Parse("#7CC8FF"));
    private static readonly IBrush RegionSelectedBrush = new ImmutableSolidColorBrush(Color.Parse("#6BC1B5"));
    private static readonly IBrush NodeHoleBrush = new ImmutableSolidColorBrush(Color.Parse("#0D131D"));
    private static readonly IBrush NodeLabelBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#8A000000"));
    private static readonly IBrush TooltipBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#1A2536"));
    private static readonly IBrush HoverOverlayBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#99000000"));
    private static readonly Pen NodeOutlinePen = new(new ImmutableSolidColorBrush(Color.Parse("#88000000")), 1.1);
    private static readonly IBrush VoronoiBorderBrush = new ImmutableSolidColorBrush(Color.Parse("#AA0D131D"));
    private static readonly Pen TooltipBorderPen = new(new ImmutableSolidColorBrush(Color.Parse("#3B5678")), 1);
    private static readonly Pen HoverOverlayBorderPen = new(new ImmutableSolidColorBrush(Color.Parse("#4A617F")), 1);
    private static readonly IBrush EmptyTextBrush = new ImmutableSolidColorBrush(Color.Parse("#9FB4D2"));

    public static readonly StyledProperty<MapGraph?> GraphProperty =
        AvaloniaProperty.Register<MapControl, MapGraph?>(nameof(Graph));

    public static readonly StyledProperty<long?> SelectedNodeIdProperty =
        AvaloniaProperty.Register<MapControl, long?>(nameof(SelectedNodeId));
    public static readonly StyledProperty<MapViewMode> ViewModeProperty =
        AvaloniaProperty.Register<MapControl, MapViewMode>(nameof(ViewMode), MapViewMode.Universe);
    public static readonly StyledProperty<bool> StretchToWindowProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(StretchToWindow), true);
    public static readonly StyledProperty<MapNodeColorMode> NodeColorModeProperty =
        AvaloniaProperty.Register<MapControl, MapNodeColorMode>(nameof(NodeColorMode), MapNodeColorMode.None);
    public static readonly StyledProperty<MapNodeColorMode> NodeBackgroundColorModeProperty =
        AvaloniaProperty.Register<MapControl, MapNodeColorMode>(nameof(NodeBackgroundColorMode), MapNodeColorMode.None);
    public static readonly StyledProperty<bool> ShowIndicatorLabelTextProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorLabelText), true);
    public static readonly StyledProperty<bool> ShowIndicatorGlyphProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorGlyph), true);
    public static readonly StyledProperty<bool> InfoBoxShowRegionProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowRegion), true);
    public static readonly StyledProperty<bool> InfoBoxShowConstellationProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowConstellation), true);
    public static readonly StyledProperty<bool> InfoBoxShowSystemIdProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowSystemId), false);

    private Point? _lastPanPoint;
    private Point _panOffset = new(0, 0);
    private double _zoom = 1.0;
    private long? _hoveredNodeId;
    private int? _hoveredRegionId;
    private int? _selectedRegionId;
    private long? _searchHighlightedNodeId;
    private int? _searchHighlightedConstellationId;
    private int? _searchHighlightedRegionId;
    private MapGraph? _lastGraphForCaches;
    private readonly Dictionary<string, FormattedText> _nodeLabelCache = [];
    private readonly Dictionary<int, FormattedText> _regionLabelCache = [];
    private readonly Dictionary<string, FormattedText> _nodeLabelHaloCache = [];
    private readonly Dictionary<int, FormattedText> _regionLabelHaloCache = [];
    private readonly Dictionary<long, IReadOnlyList<(double X, double Y)>> _voronoiWorldPolygonsByNodeId = [];
    private MapGraph? _lastGraphForVoronoi;
    private Task<Dictionary<long, IReadOnlyList<(double X, double Y)>>>? _voronoiBuildTask;
    private CancellationTokenSource? _voronoiBuildCts;
    private string? _voronoiBuildKey;
    private string? _lastVoronoiCacheKey;
    private readonly Dictionary<long, MapNode> _nodeById = [];
    private readonly Dictionary<long, int> _nodeIndexById = [];
    private readonly Dictionary<long, StreamGeometry> _voronoiWorldGeometriesByNodeId = [];
    private readonly Dictionary<uint, IBrush> _brushCache = [];
    private Point[] _screenPositions = [];
    private double _graphMinX;
    private double _graphMaxX;
    private double _graphMinY;
    private double _graphMaxY;
    private double _typicalLinkSpacing;
    private const double BasePadding = 0.0;
    private const double FitPadding = 30.0;
    public event EventHandler<int>? UniverseRegionNodeDoubleClicked;

    public MapGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public long? SelectedNodeId
    {
        get => GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    public MapViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    public bool StretchToWindow
    {
        get => GetValue(StretchToWindowProperty);
        set => SetValue(StretchToWindowProperty, value);
    }

    public MapNodeColorMode NodeColorMode
    {
        get => GetValue(NodeColorModeProperty);
        set => SetValue(NodeColorModeProperty, value);
    }

    public MapNodeColorMode NodeBackgroundColorMode
    {
        get => GetValue(NodeBackgroundColorModeProperty);
        set => SetValue(NodeBackgroundColorModeProperty, value);
    }

    public bool ShowIndicatorLabelText
    {
        get => GetValue(ShowIndicatorLabelTextProperty);
        set => SetValue(ShowIndicatorLabelTextProperty, value);
    }

    public bool ShowIndicatorGlyph
    {
        get => GetValue(ShowIndicatorGlyphProperty);
        set => SetValue(ShowIndicatorGlyphProperty, value);
    }

    public bool InfoBoxShowRegion
    {
        get => GetValue(InfoBoxShowRegionProperty);
        set => SetValue(InfoBoxShowRegionProperty, value);
    }

    public bool InfoBoxShowConstellation
    {
        get => GetValue(InfoBoxShowConstellationProperty);
        set => SetValue(InfoBoxShowConstellationProperty, value);
    }

    public bool InfoBoxShowSystemId
    {
        get => GetValue(InfoBoxShowSystemIdProperty);
        set => SetValue(InfoBoxShowSystemIdProperty, value);
    }

    public MapControl()
    {
        AffectsRender<MapControl>(GraphProperty, SelectedNodeIdProperty, ViewModeProperty, StretchToWindowProperty);
        AffectsRender<MapControl>(NodeColorModeProperty, NodeBackgroundColorModeProperty, ShowIndicatorLabelTextProperty, ShowIndicatorGlyphProperty, InfoBoxShowRegionProperty, InfoBoxShowConstellationProperty, InfoBoxShowSystemIdProperty);
        ClipToBounds = true;
    }

    public void FitToView()
    {
        if (Graph is null || Graph.Nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            _zoom = 1.0;
            _panOffset = new Point(0, 0);
            InvalidateVisual();
            return;
        }

        if (_nodeById.Count != Graph.Nodes.Count)
        {
            RebuildGraphCaches();
        }

        var plot = GetPlotMetrics();
        var plotWidth = plot.Width;
        var plotHeight = plot.Height;

        var graphWidthPx = Math.Max(1e-9, (_graphMaxX - _graphMinX) * plotWidth);
        var graphHeightPx = Math.Max(1e-9, (_graphMaxY - _graphMinY) * plotHeight);

        var availableWidth = Math.Max(1.0, plotWidth - (FitPadding * 2));
        var availableHeight = Math.Max(1.0, plotHeight - (FitPadding * 2));

        var zoomX = availableWidth / graphWidthPx;
        var zoomY = availableHeight / graphHeightPx;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.4, GetMaxZoom());

        var baseCenterX = plot.OriginX + (((_graphMinX + _graphMaxX) * 0.5) * plotWidth);
        var baseCenterY = plot.OriginY + (((_graphMinY + _graphMaxY) * 0.5) * plotHeight);
        var viewCenterX = Bounds.Width * 0.5;
        var viewCenterY = Bounds.Height * 0.5;

        _panOffset = new Point(
            viewCenterX - (((baseCenterX - viewCenterX) * _zoom) + viewCenterX),
            viewCenterY - (((baseCenterY - viewCenterY) * _zoom) + viewCenterY));

        InvalidateVisual();
    }


    public MapViewportState GetViewportState() => new()
    {
        Zoom = _zoom,
        PanOffsetX = _panOffset.X,
        PanOffsetY = _panOffset.Y
    };

    public void SetViewportState(MapViewportState state)
    {
        _zoom = Math.Clamp(state.Zoom, 0.4, GetMaxZoom());
        _panOffset = new Point(state.PanOffsetX, state.PanOffsetY);
        InvalidateVisual();
    }


    private void RebuildGraphCaches()
    {
        _nodeById.Clear();
        _nodeIndexById.Clear();
        _screenPositions = [];

        if (Graph is null || Graph.Nodes.Count == 0)
        {
            _graphMinX = 0;
            _graphMaxX = 0;
            _graphMinY = 0;
            _graphMaxY = 0;
            _typicalLinkSpacing = 0.01;
            return;
        }

        _graphMinX = double.PositiveInfinity;
        _graphMaxX = double.NegativeInfinity;
        _graphMinY = double.PositiveInfinity;
        _graphMaxY = double.NegativeInfinity;

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            _nodeById[node.Id] = node;
            _nodeIndexById[node.Id] = i;

            _graphMinX = Math.Min(_graphMinX, node.X);
            _graphMaxX = Math.Max(_graphMaxX, node.X);
            _graphMinY = Math.Min(_graphMinY, node.Y);
            _graphMaxY = Math.Max(_graphMaxY, node.Y);
        }

        _typicalLinkSpacing = EstimateTypicalLinkSpacing(Graph.Nodes, Graph.Links);
    }

    private void EnsureScreenPositionBuffer()
    {
        if (Graph is null)
        {
            _screenPositions = [];
            return;
        }

        if (_screenPositions.Length != Graph.Nodes.Count)
        {
            _screenPositions = new Point[Graph.Nodes.Count];
        }
    }

    private void UpdateScreenPositions(PlotMetrics plot, double viewCenterX, double viewCenterY)
    {
        if (Graph is null)
        {
            return;
        }

        EnsureScreenPositionBuffer();

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            _screenPositions[i] = ToScreenPointFast(node.X, node.Y, plot, viewCenterX, viewCenterY);
        }
    }

    private long? FindClosestNodeAt(Point point, double threshold)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return null;
        }

        if (_screenPositions.Length != Graph.Nodes.Count)
        {
            var plot = GetPlotMetrics();
            UpdateScreenPositions(plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
        }

        var thresholdSq = threshold * threshold;
        var bestDistSq = thresholdSq;
        long? bestId = null;

        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var p = _screenPositions[i];
            var dx = p.X - point.X;
            var dy = p.Y - point.Y;
            var distSq = (dx * dx) + (dy * dy);

            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                bestId = Graph.Nodes[i].Id;
            }
        }

        return bestId;
    }

    private IBrush GetCachedBrush(Color color, double alpha01 = 1.0)
    {
        var a = (byte)Math.Clamp((int)(alpha01 * 255), 0, 255);
        var keyColor = Color.FromArgb(a, color.R, color.G, color.B);
        var key = ((uint)keyColor.A << 24) |
                  ((uint)keyColor.R << 16) |
                  ((uint)keyColor.G << 8) |
                  keyColor.B;

        if (_brushCache.TryGetValue(key, out var brush))
        {
            return brush;
        }

        brush = new ImmutableSolidColorBrush(keyColor);
        _brushCache[key] = brush;
        return brush;
    }

    public override void Render(DrawingContext context)
    {
        if (!ReferenceEquals(_lastGraphForCaches, Graph))
        {
            _lastGraphForCaches = Graph;
            _nodeLabelCache.Clear();
            _nodeLabelHaloCache.Clear();
            _regionLabelCache.Clear();
            _regionLabelHaloCache.Clear();
            _voronoiWorldPolygonsByNodeId.Clear();
            _voronoiWorldGeometriesByNodeId.Clear();
            _lastGraphForVoronoi = null;
            _lastVoronoiCacheKey = null;
            CancelVoronoiBuild();
            RebuildGraphCaches();
        }

        var bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        if (Graph is null || Graph.Nodes.Count == 0)
        {
            DrawCenteredText(context, "No map data loaded", bounds);
            return;
        }

        var plot = GetPlotMetrics();
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        UpdateScreenPositions(plot, viewCenterX, viewCenterY);

        var renderVoronoiBackground = NodeBackgroundColorMode != MapNodeColorMode.None && _zoom >= GetVoronoiZoomThreshold();
        if (renderVoronoiBackground && _voronoiWorldGeometriesByNodeId.Count == 0)
        {
            EnsureVoronoiWorldPolygons();
        }

        if (renderVoronoiBackground && _voronoiWorldGeometriesByNodeId.Count > 0)
        {
            const double margin = 220;
            var worldToScreen = GetWorldToScreenMatrix(plot);
            var worldScale = Math.Max(1e-9, ((plot.Width + plot.Height) * 0.5) * _zoom);
            var voronoiBorderPen = new Pen(VoronoiBorderBrush, 0.8 / worldScale);
            var visibleVoronoiNodeIndexes = new List<int>(Math.Min(Graph.Nodes.Count, 512));

            using (context.PushTransform(worldToScreen))
            {
                for (var i = 0; i < Graph.Nodes.Count; i++)
                {
                    var centerPoint = _screenPositions[i];
                    if (!IsPointVisible(centerPoint, bounds, margin))
                    {
                        continue;
                    }

                    visibleVoronoiNodeIndexes.Add(i);
                    DrawVoronoiCellWorld(context, Graph.Nodes[i], voronoiBorderPen);
                }
            }

            foreach (var index in visibleVoronoiNodeIndexes)
            {
                context.DrawEllipse(NodeHoleBrush, null, _screenPositions[index], 7.2, 7.2);
            }
        }

        foreach (var link in Graph.Links)
        {
            if (!_nodeIndexById.TryGetValue(link.FromId, out var fromIndex) ||
                !_nodeIndexById.TryGetValue(link.ToId, out var toIndex))
            {
                continue;
            }

            var from = _screenPositions[fromIndex];
            var to = _screenPositions[toIndex];

            if (!IsSegmentPotentiallyVisible(from, to, bounds, 48))
            {
                continue;
            }

            var isSelectedLink = SelectedNodeId is not null &&
                                 (link.FromId == SelectedNodeId.Value || link.ToId == SelectedNodeId.Value);
            var isHoveredLink = _hoveredNodeId is not null &&
                                (link.FromId == _hoveredNodeId.Value || link.ToId == _hoveredNodeId.Value);
            var isSearchConstellationLink = _searchHighlightedConstellationId is not null &&
                                            _nodeById.TryGetValue(link.FromId, out var fromNodeForSearchConstellation) &&
                                            _nodeById.TryGetValue(link.ToId, out var toNodeForSearchConstellation) &&
                                            fromNodeForSearchConstellation.ConstellationId == _searchHighlightedConstellationId.Value &&
                                            toNodeForSearchConstellation.ConstellationId == _searchHighlightedConstellationId.Value;
            var isSearchRegionLink = _searchHighlightedRegionId is not null &&
                                     _nodeById.TryGetValue(link.FromId, out var fromNodeForSearchRegion) &&
                                     _nodeById.TryGetValue(link.ToId, out var toNodeForSearchRegion) &&
                                     (fromNodeForSearchRegion.RegionId == _searchHighlightedRegionId.Value ||
                                      toNodeForSearchRegion.RegionId == _searchHighlightedRegionId.Value);
            var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
            var isRegionLink = activeRegionId is not null &&
                               _nodeById.TryGetValue(link.FromId, out var fromNodeForRegion) &&
                               _nodeById.TryGetValue(link.ToId, out var toNodeForRegion) &&
                               (fromNodeForRegion.RegionId == activeRegionId.Value || toNodeForRegion.RegionId == activeRegionId.Value);

            var basePen = GetLinkPen(LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, link, _nodeById);
            var pen = isSearchConstellationLink
                ? HighlightedSearchConstellationPen
                : isSelectedLink || isHoveredLink || isSearchRegionLink
                    ? GetHighlightedPen(basePen, LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, HighlightedDefaultPen, HighlightedSameConstellationPen, HighlightedSameRegionPen, HighlightedCrossRegionPen)
                    : isRegionLink
                        ? GetHighlightedPen(basePen, LinksPen, SameConstellationPen, SameRegionPen, CrossRegionPen, RegionEmphasisDefaultPen, RegionEmphasisSameConstellationPen, RegionEmphasisSameRegionPen, RegionEmphasisCrossRegionPen)
                        : basePen;

            context.DrawLine(pen, from, to);
        }

        var labelBudget = GetLabelBudget();
        var labelsDrawn = 0;
        for (var i = 0; i < Graph.Nodes.Count; i++)
        {
            var node = Graph.Nodes[i];
            var p = _screenPositions[i];

            if (!IsPointVisible(p, bounds, 24))
            {
                continue;
            }

            var isSelected = SelectedNodeId == node.Id;
            var isHovered = _hoveredNodeId == node.Id;
            var isSearchHighlighted = _searchHighlightedNodeId == node.Id ||
                                      (_searchHighlightedConstellationId is not null && node.ConstellationId == _searchHighlightedConstellationId.Value) ||
                                      (_searchHighlightedRegionId is not null && node.RegionId == _searchHighlightedRegionId.Value);
            var activeRegionId = _selectedRegionId ?? _hoveredRegionId;
            var isInActiveRegion = activeRegionId is not null && node.RegionId == activeRegionId.Value;
            var isSelectedRegionNode = _selectedRegionId is not null && node.RegionId == _selectedRegionId.Value;
            var radius = isSelected ? 6.2 : isHovered ? 5.6 : isSearchHighlighted ? 5.2 : 4.5;
            var brush = isSelected
                ? SelectedBrush
                : isHovered
                    ? HoveredBrush
                    : isSearchHighlighted
                        ? SelectedBrush
                        : isSelectedRegionNode
                            ? SelectedBrush
                            : isInActiveRegion
                                ? RegionSelectedBrush
                                : GetCachedBrush(GetNodeBaseColor(node, NodeColorMode));
            context.DrawEllipse(brush, NodeOutlinePen, p, radius, radius);

            var labelVisibilityMargin = ViewMode == MapViewMode.Universe ? 180 : 96;
            var suppressInlineLabel =
                (SelectedNodeId is not null && node.Id == SelectedNodeId.Value) ||
                (_hoveredNodeId is not null && node.Id == _hoveredNodeId.Value);
            if (ShowIndicatorLabelText &&
                !suppressInlineLabel &&
                (_zoom >= GetLabelZoomThreshold() || isSelected || isHovered) &&
                labelsDrawn < labelBudget &&
                IsPointVisible(p, bounds, labelVisibilityMargin))
            {
                var labelText = ShowIndicatorGlyph ? $"◆ {node.Name}" : node.Name;
                var label = GetNodeLabel(node.Id, labelText);
                var labelOrigin = GetNodeLabelOrigin(p);
                DrawNodeLabel(context, label, GetNodeLabelHalo(node.Id, labelText), labelOrigin);
                labelsDrawn++;
            }
        }

        if (SelectedNodeId is not null &&
            _nodeIndexById.TryGetValue(SelectedNodeId.Value, out var selectedIndex) &&
            _nodeById.TryGetValue(SelectedNodeId.Value, out var selectedNode))
        {
            DrawHoverOverlay(context, _screenPositions[selectedIndex], selectedNode);
        }

        if (_hoveredNodeId is not null &&
            _hoveredNodeId != SelectedNodeId &&
            _nodeIndexById.TryGetValue(_hoveredNodeId.Value, out var hoverIndex) &&
            _nodeById.TryGetValue(_hoveredNodeId.Value, out var hoverNode))
        {
            DrawHoverOverlay(context, _screenPositions[hoverIndex], hoverNode);
        }

        if (ViewMode == MapViewMode.Universe && _zoom < GetLabelZoomThreshold())
        {
            DrawUniverseRegionLabels(context);
        }
    }


    private double GetLabelZoomThreshold()
    {
        return ViewMode switch
        {
            MapViewMode.Universe => 3.5,
            MapViewMode.UniverseRegions => 0.35,
            MapViewMode.Region => 0.35,
            _ => 1.0
        };
    }

    private int GetLabelBudget()
    {
        return ViewMode switch
        {
            MapViewMode.Universe => 420,
            MapViewMode.UniverseRegions => 180,
            MapViewMode.Region => 420,
            _ => 300
        };
    }

    private double GetVoronoiZoomThreshold()
    {
        return ViewMode switch
        {
            MapViewMode.Universe => 0.4,
            MapViewMode.UniverseRegions => 0.4,
            MapViewMode.Region => 0.4,
            _ => 0.4
        };
    }

    private void DrawUniverseRegionLabels(DrawingContext context)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        foreach (var layout in BuildUniverseRegionLabelLayouts())
        {
            var isSelected = _selectedRegionId == layout.RegionId;
            var isHovered = _hoveredRegionId == layout.RegionId;
            context.FillRectangle(
                new SolidColorBrush(Color.Parse(isSelected ? "#2B3F58" : isHovered ? "#243750" : "#1A2536")),
                layout.Rect,
                4);
            context.DrawRectangle(
                new Pen(new SolidColorBrush(Color.Parse(isSelected ? "#8AC8FF" : isHovered ? "#78AEE6" : "#3F5C83")), 1),
                layout.Rect,
                4);

            var origin = new Point(layout.Center.X - (layout.Label.Width / 2.0), layout.Center.Y - (layout.Label.Height / 2.0));
            DrawLabelWithHalo(context, layout.Label, GetRegionLabelHalo(layout.RegionId, layout.RegionName), origin);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (e.ClickCount >= 2 &&
            ViewMode == MapViewMode.UniverseRegions &&
            props.IsLeftButtonPressed &&
            TrySelectNodeAt(point) &&
            SelectedNodeId is long selectedId)
        {
            UniverseRegionNodeDoubleClicked?.Invoke(this, (int)selectedId);
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            if (TryToggleRegionSelectionFromLabel(point))
            {
                SelectedNodeId = null;
                ClearSearchHighlight();
                InvalidateVisual();
                return;
            }

            if (TrySelectNodeAt(point))
            {
                _selectedRegionId = null;
                ClearSearchHighlight();
                InvalidateVisual();
                return;
            }

            SelectedNodeId = null;
            _selectedRegionId = null;
            ClearSearchHighlight();
            InvalidateVisual();
            return;
        }

        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
        {
            _lastPanPoint = point;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (_lastPanPoint is null)
        {
            UpdateHover(point);
            return;
        }

        var delta = point - _lastPanPoint.Value;
        if ((delta.X * delta.X) + (delta.Y * delta.Y) < 0.25)
        {
            return;
        }

        _panOffset += delta;
        _lastPanPoint = point;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _lastPanPoint = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y;
        var factor = delta > 0 ? 1.1 : 0.9;
        var mouse = e.GetPosition(this);
        var oldZoom = _zoom;
        var newZoom = Math.Clamp(_zoom * factor, 0.4, GetMaxZoom());
        if (Math.Abs(newZoom - oldZoom) < 1e-9)
        {
            return;
        }

        // Keep the world point under the cursor stable while zooming.
        var plot = GetPlotMetrics();
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;
        var baseX = ((mouse.X - viewCenterX - _panOffset.X) / oldZoom) + viewCenterX;
        var baseY = ((mouse.Y - viewCenterY - _panOffset.Y) / oldZoom) + viewCenterY;
        var worldX = (baseX - plot.OriginX) / plot.Width;
        var worldY = (baseY - plot.OriginY) / plot.Height;
        var newBaseX = plot.OriginX + (worldX * plot.Width);
        var newBaseY = plot.OriginY + (worldY * plot.Height);

        _zoom = newZoom;
        _panOffset = new Point(
            mouse.X - (((newBaseX - viewCenterX) * _zoom) + viewCenterX),
            mouse.Y - (((newBaseY - viewCenterY) * _zoom) + viewCenterY));

        InvalidateVisual();
    }

    private bool TrySelectNodeAt(Point point)
    {
        var closestId = FindClosestNodeAt(point, 8.0);
        if (closestId is null)
        {
            return false;
        }

        SelectedNodeId = closestId.Value;
        return true;
    }


    private void UpdateHover(Point point)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            if (_hoveredNodeId is not null)
            {
                _hoveredNodeId = null;
                InvalidateVisual();
            }
            if (_hoveredRegionId is not null)
            {
                _hoveredRegionId = null;
                InvalidateVisual();
            }

            return;
        }

        var hoverId = FindClosestNodeAt(point, 10.0);
        int? hoveredRegionId = null;
        if (hoverId is null &&
            ViewMode == MapViewMode.Universe &&
            _zoom < GetLabelZoomThreshold())
        {
            hoveredRegionId = TryGetHoveredRegionFromRegionLabel(point);
        }

        var changed = false;
        if (_hoveredNodeId != hoverId)
        {
            _hoveredNodeId = hoverId;
            changed = true;
        }

        if (_hoveredRegionId != hoveredRegionId)
        {
            _hoveredRegionId = hoveredRegionId;
            changed = true;
        }

        if (changed)
        {
            InvalidateVisual();
        }
    }


    public void FocusOnSearch(MapSearchFocus focus)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        ClearSearchHighlight();

        if (ViewMode == MapViewMode.Region)
        {
            if (focus.Kind == MapSearchKind.SolarSystem && focus.SolarSystemId is not null)
            {
                _searchHighlightedNodeId = focus.SolarSystemId.Value;
                SelectedNodeId = focus.SolarSystemId.Value;
            }
            else if (focus.Kind == MapSearchKind.Constellation && focus.ConstellationId is not null)
            {
                _searchHighlightedConstellationId = focus.ConstellationId.Value;
                SelectedNodeId = null;
            }
            else
            {
                SelectedNodeId = null;
            }

            FitToView();
            InvalidateVisual();
            return;
        }

        if (ViewMode == MapViewMode.UniverseRegions)
        {
            if (focus.Kind == MapSearchKind.Region && focus.RegionId is not null)
            {
                _searchHighlightedRegionId = focus.RegionId.Value;
                SelectedNodeId = null;
                InvalidateVisual();
            }

            return;
        }

        if (focus.Kind == MapSearchKind.SolarSystem && focus.SolarSystemId is not null)
        {
            _searchHighlightedNodeId = focus.SolarSystemId.Value;
            _selectedRegionId = null;
            FocusOnNode(focus.SolarSystemId.Value);
            return;
        }

        if (focus.Kind == MapSearchKind.Constellation && focus.ConstellationId is not null)
        {
            _searchHighlightedConstellationId = focus.ConstellationId.Value;
            _selectedRegionId = null;
            SelectedNodeId = null;
            FocusOnConstellation(focus.ConstellationId.Value);
            return;
        }

        if (focus.Kind == MapSearchKind.Region && focus.RegionId is not null)
        {
            _searchHighlightedRegionId = focus.RegionId.Value;
            _selectedRegionId = ViewMode == MapViewMode.Universe ? focus.RegionId.Value : null;
            SelectedNodeId = null;
            FocusOnRegion(focus.RegionId.Value);
            InvalidateVisual();
        }
    }

    private void FocusOnNode(long nodeId)
    {
        if (Graph is null)
        {
            return;
        }

        if (!_nodeById.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var targetZoom = ViewMode == MapViewMode.Universe ? 8.0 : 10.0;
        CenterOnWorld(node.X, node.Y, targetZoom);
    }

    private void FocusOnConstellation(int constellationId)
    {
        if (Graph is null)
        {
            return;
        }

        var nodes = Graph.Nodes.Where(n => n.ConstellationId == constellationId).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        FocusOnNodes(nodes, 130);
    }

    private void FocusOnRegion(int regionId)
    {
        if (Graph is null)
        {
            return;
        }

        var nodes = Graph.Nodes.Where(n => n.RegionId == regionId).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        FocusOnNodes(nodes, 150);
    }

    private void FocusOnNodes(IReadOnlyList<MapNode> nodes, double padding)
    {
        if (nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var plot = GetPlotMetrics();
        var plotWidth = plot.Width;
        var plotHeight = plot.Height;

        var minX = nodes.Min(n => n.X);
        var maxX = nodes.Max(n => n.X);
        var minY = nodes.Min(n => n.Y);
        var maxY = nodes.Max(n => n.Y);

        var graphWidthPx = Math.Max(1e-9, (maxX - minX) * plotWidth);
        var graphHeightPx = Math.Max(1e-9, (maxY - minY) * plotHeight);

        var availableWidth = Math.Max(1.0, plotWidth - (padding * 2));
        var availableHeight = Math.Max(1.0, plotHeight - (padding * 2));

        var zoomX = availableWidth / graphWidthPx;
        var zoomY = availableHeight / graphHeightPx;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.4, GetMaxZoom());

        var centerX = (minX + maxX) * 0.5;
        var centerY = (minY + maxY) * 0.5;
        CenterOnWorld(centerX, centerY, _zoom);
    }

    private void CenterOnWorld(double worldX, double worldY, double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.4, GetMaxZoom());
        var plot = GetPlotMetrics();
        var baseX = plot.OriginX + (worldX * plot.Width);
        var baseY = plot.OriginY + (worldY * plot.Height);
        var cx = Bounds.Width * 0.5;
        var cy = Bounds.Height * 0.5;
        _panOffset = new Point(
            cx - (((baseX - cx) * _zoom) + cx),
            cy - (((baseY - cy) * _zoom) + cy));
        InvalidateVisual();
    }

    private Point ToScreenPoint(MapNode node)
    {
        var plot = GetPlotMetrics();

        var x = plot.OriginX + (node.X * plot.Width);
        var y = plot.OriginY + (node.Y * plot.Height);

        var centeredX = ((x - Bounds.Width / 2.0) * _zoom) + (Bounds.Width / 2.0) + _panOffset.X;
        var centeredY = ((y - Bounds.Height / 2.0) * _zoom) + (Bounds.Height / 2.0) + _panOffset.Y;

        return new Point(centeredX, centeredY);
    }

    private Point ToScreenPoint(double worldX, double worldY)
    {
        var plot = GetPlotMetrics();
        return ToScreenPointFast(worldX, worldY, plot, Bounds.Width / 2.0, Bounds.Height / 2.0);
    }

    private Point ToScreenPointFast(
        double worldX,
        double worldY,
        PlotMetrics plot,
        double viewCenterX,
        double viewCenterY)
    {
        var x = plot.OriginX + (worldX * plot.Width);
        var y = plot.OriginY + (worldY * plot.Height);

        return new Point(
            ((x - viewCenterX) * _zoom) + viewCenterX + _panOffset.X,
            ((y - viewCenterY) * _zoom) + viewCenterY + _panOffset.Y);
    }

    private Matrix GetWorldToScreenMatrix(PlotMetrics plot)
    {
        var viewCenterX = Bounds.Width / 2.0;
        var viewCenterY = Bounds.Height / 2.0;

        var scaleX = plot.Width * _zoom;
        var scaleY = plot.Height * _zoom;
        var offsetX = ((plot.OriginX - viewCenterX) * _zoom) + viewCenterX + _panOffset.X;
        var offsetY = ((plot.OriginY - viewCenterY) * _zoom) + viewCenterY + _panOffset.Y;

        return new Matrix(
            scaleX, 0,
            0, scaleY,
            offsetX, offsetY);
    }


    private double GetMaxZoom()
    {
        return ViewMode switch
        {
            MapViewMode.Universe => 24.0,
            MapViewMode.Region => 18.0,
            _ => 12.0
        };
    }

    private PlotMetrics GetPlotMetrics()
    {
        var baseWidth = Math.Max(1.0, Bounds.Width - (BasePadding * 2));
        var baseHeight = Math.Max(1.0, Bounds.Height - (BasePadding * 2));

        if (StretchToWindow)
        {
            return new PlotMetrics(BasePadding, BasePadding, baseWidth, baseHeight);
        }

        var side = Math.Max(1.0, Math.Min(baseWidth, baseHeight));
        var originX = BasePadding + ((baseWidth - side) * 0.5);
        var originY = BasePadding + ((baseHeight - side) * 0.5);
        return new PlotMetrics(originX, originY, side, side);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool IsPointVisible(Point p, Rect bounds, double margin)
    {
        return p.X >= -margin &&
               p.Y >= -margin &&
               p.X <= bounds.Width + margin &&
               p.Y <= bounds.Height + margin;
    }

    private static bool IsSegmentPotentiallyVisible(Point a, Point b, Rect bounds, double margin)
    {
        if (a.X < -margin && b.X < -margin) return false;
        if (a.Y < -margin && b.Y < -margin) return false;
        if (a.X > bounds.Width + margin && b.X > bounds.Width + margin) return false;
        if (a.Y > bounds.Height + margin && b.Y > bounds.Height + margin) return false;
        return true;
    }

    private int? TryGetHoveredRegionFromRegionLabel(Point point)
    {
        foreach (var layout in BuildUniverseRegionLabelLayouts())
        {
            if (layout.Rect.Contains(point))
            {
                return layout.RegionId;
            }
        }

        return null;
    }

    private bool TryToggleRegionSelectionFromLabel(Point point)
    {
        if (ViewMode != MapViewMode.Universe || _zoom >= GetLabelZoomThreshold())
        {
            return false;
        }

        var regionId = TryGetHoveredRegionFromRegionLabel(point);
        if (regionId is null)
        {
            return false;
        }

        _selectedRegionId = _selectedRegionId == regionId ? null : regionId;
        return true;
    }

    private void ClearSearchHighlight()
    {
        _searchHighlightedNodeId = null;
        _searchHighlightedConstellationId = null;
        _searchHighlightedRegionId = null;
    }

    private IReadOnlyList<UniverseRegionLabelLayout> BuildUniverseRegionLabelLayouts()
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return [];
        }

        var result = new List<UniverseRegionLabelLayout>();
        var regionGroups = Graph.Nodes
            .Where(n => n.RegionId is not null && !string.IsNullOrWhiteSpace(n.RegionName))
            .GroupBy(n => n.RegionId!.Value);

        foreach (var group in regionGroups)
        {
            var samples = group.Select(ToScreenPoint).ToList();
            if (samples.Count == 0)
            {
                continue;
            }

            var center = new Point(samples.Average(p => p.X), samples.Average(p => p.Y));
            var label = GetRegionLabel(group.Key, group.First().RegionName!);
            var padX = 6.0;
            var padY = 3.0;
            var rect = new Rect(
                center.X - (label.Width / 2.0) - padX,
                center.Y - (label.Height / 2.0) - padY,
                label.Width + (padX * 2),
                label.Height + (padY * 2));

            result.Add(new UniverseRegionLabelLayout(group.Key, group.First().RegionName!, center, rect, label));
        }

        return result;
    }

    private Pen GetLinkPen(
        Pen defaultPen,
        Pen sameConstellationPen,
        Pen sameRegionPen,
        Pen crossRegionPen,
        MapLink link,
        IReadOnlyDictionary<long, MapNode> nodeById)
    {
        if (ViewMode == MapViewMode.UniverseRegions)
        {
            return defaultPen;
        }

        if (!nodeById.TryGetValue(link.FromId, out var fromNode) || !nodeById.TryGetValue(link.ToId, out var toNode))
        {
            return defaultPen;
        }

        var sameConstellation = fromNode.ConstellationId is not null &&
                                toNode.ConstellationId is not null &&
                                fromNode.ConstellationId == toNode.ConstellationId;
        if (sameConstellation)
        {
            return sameConstellationPen;
        }

        var sameRegion = fromNode.RegionId is not null &&
                         toNode.RegionId is not null &&
                         fromNode.RegionId == toNode.RegionId;
        return sameRegion ? sameRegionPen : crossRegionPen;
    }

    private static Pen GetHighlightedPen(
        Pen basePen,
        Pen defaultPen,
        Pen sameConstellationPen,
        Pen sameRegionPen,
        Pen crossRegionPen,
        Pen highlightedDefaultPen,
        Pen highlightedSameConstellationPen,
        Pen highlightedSameRegionPen,
        Pen highlightedCrossRegionPen)
    {
        if (ReferenceEquals(basePen, sameConstellationPen))
        {
            return highlightedSameConstellationPen;
        }

        if (ReferenceEquals(basePen, sameRegionPen))
        {
            return highlightedSameRegionPen;
        }

        if (ReferenceEquals(basePen, crossRegionPen))
        {
            return highlightedCrossRegionPen;
        }

        return highlightedDefaultPen;
    }

    private FormattedText GetNodeLabel(long nodeId, string name)
    {
        var key = $"{nodeId}:{name}";
        if (_nodeLabelCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11.5,
            new SolidColorBrush(Color.Parse("#D8E6F8")));
        _nodeLabelCache[key] = text;
        return text;
    }

    private FormattedText GetNodeLabelHalo(long nodeId, string name)
    {
        var key = $"{nodeId}:{name}";
        if (_nodeLabelHaloCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11.5,
            new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        _nodeLabelHaloCache[key] = text;
        return text;
    }

    private void EnsureVoronoiWorldPolygons()
    {
        if (Graph is null)
        {
            _voronoiWorldPolygonsByNodeId.Clear();
            _voronoiWorldGeometriesByNodeId.Clear();
            _lastGraphForVoronoi = null;
            _lastVoronoiCacheKey = null;
            CancelVoronoiBuild();
            return;
        }

        var cacheKey = ComputeVoronoiCacheKey(Graph);
        if (ReferenceEquals(_lastGraphForVoronoi, Graph) && _lastVoronoiCacheKey == cacheKey && _voronoiWorldPolygonsByNodeId.Count > 0)
        {
            return;
        }

        _voronoiWorldPolygonsByNodeId.Clear();
        _voronoiWorldGeometriesByNodeId.Clear();
        _lastGraphForVoronoi = Graph;
        _lastVoronoiCacheKey = cacheKey;

        if (TryLoadVoronoiFromCache(cacheKey, out var cachedPolygons))
        {
            foreach (var kvp in cachedPolygons)
            {
                _voronoiWorldPolygonsByNodeId[kvp.Key] = kvp.Value;
                _voronoiWorldGeometriesByNodeId[kvp.Key] = BuildWorldGeometry(kvp.Value);
            }
            return;
        }

        StartVoronoiBuild(cacheKey, Graph);
    }

    private void CancelVoronoiBuild()
    {
        _voronoiBuildCts?.Cancel();
        _voronoiBuildCts?.Dispose();
        _voronoiBuildCts = null;
        _voronoiBuildTask = null;
        _voronoiBuildKey = null;
    }

    private void StartVoronoiBuild(string cacheKey, MapGraph graph)
    {
        if (_voronoiBuildTask is { IsCompleted: false } && _voronoiBuildKey == cacheKey)
        {
            return;
        }

        CancelVoronoiBuild();
        _voronoiBuildCts = new CancellationTokenSource();
        _voronoiBuildKey = cacheKey;
        var token = _voronoiBuildCts.Token;

        _voronoiBuildTask = Task.Run(
            () => BuildVoronoiPolygons(graph, _graphMinX, _graphMaxX, _graphMinY, _graphMaxY, _typicalLinkSpacing, token),
            token);

        _ = _voronoiBuildTask.ContinueWith(task =>
        {
            if (task.IsCanceled || task.IsFaulted || token.IsCancellationRequested)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (Graph is null || _lastVoronoiCacheKey != cacheKey || _voronoiBuildKey != cacheKey)
                {
                    return;
                }

                _voronoiWorldPolygonsByNodeId.Clear();
                _voronoiWorldGeometriesByNodeId.Clear();
                foreach (var kvp in task.Result)
                {
                    _voronoiWorldPolygonsByNodeId[kvp.Key] = kvp.Value;
                    _voronoiWorldGeometriesByNodeId[kvp.Key] = BuildWorldGeometry(kvp.Value);
                }

                SaveVoronoiToCache(cacheKey, _voronoiWorldPolygonsByNodeId);
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static Dictionary<long, IReadOnlyList<(double X, double Y)>> BuildVoronoiPolygons(
        MapGraph graph,
        double graphMinX,
        double graphMaxX,
        double graphMinY,
        double graphMaxY,
        double typicalLinkSpacing,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, IReadOnlyList<(double X, double Y)>>();
        if (graph.Nodes.Count < 2 || cancellationToken.IsCancellationRequested)
        {
            return result;
        }

        var geometryFactory = new NtsGeometryFactory();
        var minX = graphMinX;
        var maxX = graphMaxX;
        var minY = graphMinY;
        var maxY = graphMaxY;
        var spacing = typicalLinkSpacing > 0 ? typicalLinkSpacing : EstimateTypicalLinkSpacing(graph.Nodes, graph.Links);
        var nodeMaskRadius = Math.Clamp(spacing * 1.95, 0.012, 0.095);
        var linkMaskRadius = Math.Clamp(spacing * 0.72, 0.006, 0.044);
        var maxBufferedLinkLength = spacing * 3.5;
        var envelopePad = Math.Clamp(spacing * 5.0, 0.04, 0.20);

        var nodeKeyToId = graph.Nodes.ToDictionary(
            n => $"{Math.Round(n.X, 8)}:{Math.Round(n.Y, 8)}",
            n => n.Id);

        var sites = graph.Nodes
            .Select(n => new NtsCoordinate(n.X, n.Y))
            .ToList();

        var territoryMask = BuildVoronoiTerritoryMask(
            graph.Nodes,
            graph.Links,
            geometryFactory,
            nodeMaskRadius,
            linkMaskRadius,
            maxBufferedLinkLength);

        if (territoryMask is null || territoryMask.IsEmpty || cancellationToken.IsCancellationRequested)
        {
            return result;
        }

        var builder = new VoronoiDiagramBuilder();
        builder.SetSites(sites);
        builder.ClipEnvelope = new NtsEnvelope(
            minX - envelopePad,
            maxX + envelopePad,
            minY - envelopePad,
            maxY + envelopePad);

        var diagram = builder.GetDiagram(geometryFactory) as NtsGeometryCollection;
        if (diagram is null)
        {
            return result;
        }

        for (var i = 0; i < diagram.NumGeometries; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }

            if (diagram.GetGeometryN(i) is not NtsPolygon polygon ||
                polygon.ExteriorRing is null ||
                polygon.ExteriorRing.NumPoints < 4)
            {
                continue;
            }

            var nodeId = ResolveVoronoiOwnerNodeId(polygon, nodeKeyToId, graph);
            if (nodeId is null)
            {
                continue;
            }

            var clipped = SafeIntersection(polygon, territoryMask);
            if (clipped is null || clipped.IsEmpty)
            {
                continue;
            }
            var points = ExtractLargestPolygonCoordinates(clipped);

            if (points.Count < 3)
            {
                continue;
            }

            result[nodeId.Value] = points;
        }
        return result;
    }

    private static NtsGeometry? SafeIntersection(NtsGeometry a, NtsGeometry b)
    {
        try
        {
            return a.Intersection(b);
        }
        catch
        {
            // Robust fallback for occasional non-noded intersections from Voronoi edges.
            try
            {
                var precision = new NetTopologySuite.Geometries.PrecisionModel(1_000_000d);
                var reducer = new GeometryPrecisionReducer(precision)
                {
                    ChangePrecisionModel = true,
                    Pointwise = false,
                    RemoveCollapsedComponents = true
                };

                var ra = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(reducer.Reduce(a)).Buffer(0);
                var rb = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(reducer.Reduce(b)).Buffer(0);
                return ra.Intersection(rb);
            }
            catch
            {
                return null;
            }
        }
    }

    private static string ComputeVoronoiCacheKey(MapGraph graph)
    {
        var sb = new StringBuilder(graph.Nodes.Count * 48 + graph.Links.Count * 24);
        sb.Append("v1|");
        sb.Append(graph.Nodes.Count).Append('|').Append(graph.Links.Count).Append('|');
        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            sb.Append(node.Id).Append(':')
                .Append(node.X.ToString("F8", CultureInfo.InvariantCulture)).Append(',')
                .Append(node.Y.ToString("F8", CultureInfo.InvariantCulture)).Append('|');
        }

        foreach (var link in graph.Links)
        {
            var a = Math.Min(link.FromId, link.ToId);
            var b = Math.Max(link.FromId, link.ToId);
            sb.Append(a).Append('-').Append(b).Append('|');
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    private static bool TryLoadVoronoiFromCache(string key, out Dictionary<long, IReadOnlyList<(double X, double Y)>> polygons)
    {
        if (VoronoiMemoryCache.TryGetValue(key, out polygons!))
        {
            return true;
        }

        polygons = [];
        try
        {
            var path = Path.Combine(VoronoiCacheDirectory, $"{key}.json");
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            var model = JsonSerializer.Deserialize<VoronoiCacheModel>(json, VoronoiJsonOptions);
            if (model is null || model.Polygons.Count == 0)
            {
                return false;
            }

            foreach (var kvp in model.Polygons)
            {
                polygons[kvp.Key] = kvp.Value.Select(p => (p.X, p.Y)).ToList();
            }

            VoronoiMemoryCache[key] = polygons;
            return polygons.Count > 0;
        }
        catch
        {
            polygons = [];
            return false;
        }
    }

    private static void SaveVoronoiToCache(string key, IReadOnlyDictionary<long, IReadOnlyList<(double X, double Y)>> polygons)
    {
        if (polygons.Count == 0)
        {
            return;
        }

        var inMemory = new Dictionary<long, IReadOnlyList<(double X, double Y)>>(polygons.Count);
        foreach (var kvp in polygons)
        {
            inMemory[kvp.Key] = kvp.Value.ToList();
        }
        VoronoiMemoryCache[key] = inMemory;

        try
        {
            Directory.CreateDirectory(VoronoiCacheDirectory);
            var model = new VoronoiCacheModel
            {
                Key = key,
                Polygons = polygons.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select(p => new PointDto { X = p.X, Y = p.Y }).ToList())
            };

            var json = JsonSerializer.Serialize(model, VoronoiJsonOptions);
            var path = Path.Combine(VoronoiCacheDirectory, $"{key}.json");
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort cache persistence only.
        }
    }

    private static NtsGeometry? BuildVoronoiTerritoryMask(
    IReadOnlyList<MapNode> nodes,
    IReadOnlyList<MapLink> links,
    NtsGeometryFactory geometryFactory,
    double nodeMaskRadius,
    double linkMaskRadius,
    double maxBufferedLinkLength)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var parts = new List<NtsGeometry>();

        foreach (var node in nodes)
        {
            var point = geometryFactory.CreatePoint(new NtsCoordinate(node.X, node.Y));
            parts.Add(point.Buffer(nodeMaskRadius, 8));
        }

        foreach (var link in links)
        {
            if (!byId.TryGetValue(link.FromId, out var from) ||
                !byId.TryGetValue(link.ToId, out var to))
            {
                continue;
            }

            var dx = from.X - to.X;
            var dy = from.Y - to.Y;
            var linkLength = Math.Sqrt((dx * dx) + (dy * dy));

            // Skip long map-spanning links so they don't create huge territory ribbons.
            if (linkLength > maxBufferedLinkLength)
            {
                continue;
            }

            var line = geometryFactory.CreateLineString([
                new NtsCoordinate(from.X, from.Y),
    new NtsCoordinate(to.X, to.Y)
            ]);

            parts.Add(line.Buffer(linkMaskRadius, 6));
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var union = NtsUnaryUnionOp.Union(parts);

        // Small smoothing pass.
        var smoothOut = nodeMaskRadius * 0.28;
        var smoothIn = nodeMaskRadius * 0.07;

        return union
            .Buffer(smoothOut, 4)
            .Buffer(-smoothIn, 4);
    }

    private static long? ResolveVoronoiOwnerNodeId(
    NtsPolygon polygon,
    IReadOnlyDictionary<string, long> nodeKeyToId,
    MapGraph graph)
    {
        if (polygon.UserData is NtsCoordinate site)
        {
            var key = $"{Math.Round(site.X, 8)}:{Math.Round(site.Y, 8)}";
            if (nodeKeyToId.TryGetValue(key, out var nodeId))
            {
                return nodeId;
            }
        }

        var centroid = polygon.Centroid;

        var nearest = graph.Nodes
            .OrderBy(n =>
            {
                var dx = n.X - centroid.X;
                var dy = n.Y - centroid.Y;
                return (dx * dx) + (dy * dy);
            })
            .FirstOrDefault();

        return nearest?.Id;
    }

    private static IReadOnlyList<(double X, double Y)> ExtractLargestPolygonCoordinates(NtsGeometry geometry)
    {
        if (geometry.IsEmpty)
        {
            return [];
        }

        NtsPolygon? bestPolygon = null;
        var bestArea = 0.0;

        void Consider(NtsGeometry candidate)
        {
            if (candidate is NtsPolygon polygon &&
                polygon.ExteriorRing is not null &&
                polygon.ExteriorRing.NumPoints >= 4 &&
                polygon.Area > bestArea)
            {
                bestPolygon = polygon;
                bestArea = polygon.Area;
            }
        }

        if (geometry is NtsPolygon singlePolygon)
        {
            Consider(singlePolygon);
        }
        else if (geometry is NtsMultiPolygon multiPolygon)
        {
            for (var i = 0; i < multiPolygon.NumGeometries; i++)
            {
                Consider(multiPolygon.GetGeometryN(i));
            }
        }
        else if (geometry is NtsGeometryCollection collection)
        {
            for (var i = 0; i < collection.NumGeometries; i++)
            {
                Consider(collection.GetGeometryN(i));
            }
        }

        if (bestPolygon is null)
        {
            return [];
        }

        return bestPolygon.ExteriorRing.Coordinates
            .Take(bestPolygon.ExteriorRing.Coordinates.Length - 1)
            .Select(c => (c.X, c.Y))
            .ToList();
    }

    private static double EstimateTypicalLinkSpacing(
    IReadOnlyList<MapNode> nodes,
    IReadOnlyList<MapLink> links)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var lengths = new List<double>(links.Count);

        foreach (var link in links)
        {
            if (!byId.TryGetValue(link.FromId, out var from) ||
                !byId.TryGetValue(link.ToId, out var to))
            {
                continue;
            }

            var dx = from.X - to.X;
            var dy = from.Y - to.Y;
            var d = Math.Sqrt((dx * dx) + (dy * dy));

            if (d > 0)
            {
                lengths.Add(d);
            }
        }

        if (lengths.Count == 0)
        {
            var minX = nodes.Min(n => n.X);
            var maxX = nodes.Max(n => n.X);
            var minY = nodes.Min(n => n.Y);
            var maxY = nodes.Max(n => n.Y);

            return Math.Max((maxX - minX + maxY - minY) * 0.015, 0.01);
        }

        lengths.Sort();
        return lengths[lengths.Count / 2];
    }

    private static StreamGeometry BuildWorldGeometry(IReadOnlyList<(double X, double Y)> polygon)
    {
        var geometry = new StreamGeometry();

        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(polygon[0].X, polygon[0].Y), true);
            for (var i = 1; i < polygon.Count; i++)
            {
                g.LineTo(new Point(polygon[i].X, polygon[i].Y));
            }
            g.EndFigure(true);
        }

        return geometry;
    }

    private void DrawVoronoiCellWorld(DrawingContext context, MapNode node, Pen borderPen)
    {
        if (!_voronoiWorldGeometriesByNodeId.TryGetValue(node.Id, out var geometry))
        {
            return;
        }

        var color = GetNodeBaseColor(node, NodeBackgroundColorMode);
        var baseFill = GetCachedBrush(color, 0.28);
        context.DrawGeometry(baseFill, borderPen, geometry);
    }


    private Color GetNodeBaseColor(MapNode node, MapNodeColorMode mode)
    {
        return mode switch
        {
            MapNodeColorMode.Security => GetSecurityColor(node),
            MapNodeColorMode.Region => GetRegionColor(node.RegionId),
            _ => Color.Parse("#8FB0D9")
        };
    }

    private static Color WithAlpha(Color color, double alpha01)
    {
        var a = (byte)Math.Clamp((int)(alpha01 * 255), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    private static Color GetSecurityColor(MapNode node)
    {
        var s = node.Id; // fallback deterministic when no security value in current model
        var bucket = (int)(Math.Abs(s) % 4);
        return bucket switch
        {
            0 => Color.Parse("#8FB0D9"),
            1 => Color.Parse("#7CCB9A"),
            2 => Color.Parse("#D9C27A"),
            _ => Color.Parse("#D98B8B")
        };
    }

    private static Color GetRegionColor(int? regionId)
    {
        if (regionId is null)
        {
            return Color.Parse("#8FB0D9");
        }

        var v = Math.Abs(regionId.Value);
        var r = 96 + (v * 37 % 120);
        var g = 96 + (v * 53 % 120);
        var b = 96 + (v * 71 % 120);
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }

    private FormattedText GetRegionLabel(int regionId, string name)
    {
        if (_regionLabelCache.TryGetValue(regionId, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            15,
            new SolidColorBrush(Color.Parse("#BFD9FF")));
        _regionLabelCache[regionId] = text;
        return text;
    }

    private FormattedText GetRegionLabelHalo(int regionId, string name)
    {
        if (_regionLabelHaloCache.TryGetValue(regionId, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            15,
            new ImmutableSolidColorBrush(Color.Parse("#CC071018")));
        _regionLabelHaloCache[regionId] = text;
        return text;
    }

    private static void DrawLabelWithHalo(DrawingContext context, FormattedText label, FormattedText halo, Point origin)
    {
        context.DrawText(halo, new Point(origin.X - 1, origin.Y));
        context.DrawText(halo, new Point(origin.X + 1, origin.Y));
        context.DrawText(halo, new Point(origin.X, origin.Y - 1));
        context.DrawText(halo, new Point(origin.X, origin.Y + 1));
        context.DrawText(label, origin);
    }

    private static void DrawNodeLabel(DrawingContext context, FormattedText label, FormattedText halo, Point origin)
    {
        var rect = new Rect(
            origin.X - 3,
            origin.Y - 2,
            label.Width + 6,
            label.Height + 4);
        context.FillRectangle(NodeLabelBackgroundBrush, rect, 3);
        DrawLabelWithHalo(context, label, halo, origin);
    }

    private static void DrawCenteredText(DrawingContext context, string message, Rect bounds)
    {
        var text = new FormattedText(
            message,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            14,
            EmptyTextBrush);

        var origin = new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2);
        context.DrawText(text, origin);
    }

    private static void DrawTooltip(DrawingContext context, Point anchor, string text)
    {
        var content = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            12,
            Brushes.White);

        var padX = 8.0;
        var padY = 6.0;
        var rect = new Rect(
            anchor.X + 12,
            anchor.Y + 12,
            content.Width + (padX * 2),
            content.Height + (padY * 2));

        context.FillRectangle(TooltipBackgroundBrush, rect, 4);
        context.DrawRectangle(TooltipBorderPen, rect, 4);
        context.DrawText(content, new Point(rect.X + padX, rect.Y + padY));
    }

    private static Point GetNodeLabelOrigin(Point nodePoint)
    {
        return new Point(nodePoint.X + NodeLabelOffset.X, nodePoint.Y + NodeLabelOffset.Y);
    }

    private void DrawHoverOverlay(DrawingContext context, Point anchor, MapNode node)
    {
        var header = ShowIndicatorGlyph ? $"◆ {node.Name}" : node.Name;
        var detailLines = new List<string>();
        if (InfoBoxShowRegion && !string.IsNullOrWhiteSpace(node.RegionName))
        {
            detailLines.Add($"Region: {node.RegionName}");
        }
        if (InfoBoxShowConstellation && !string.IsNullOrWhiteSpace(node.ConstellationName))
        {
            detailLines.Add($"Constellation: {node.ConstellationName}");
        }
        if (InfoBoxShowSystemId)
        {
            detailLines.Add($"System ID: {node.Id}");
        }

        var text = detailLines.Count == 0 ? header : $"{header}\n{string.Join('\n', detailLines)}";
        var content = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            12,
            Brushes.White);

        var start = GetNodeLabelOrigin(anchor);
        var padX = 8.0;
        var padY = 6.0;
        var rect = new Rect(
            start.X - 2,
            start.Y - 2,
            content.Width + (padX * 2),
            content.Height + (padY * 2));

        context.FillRectangle(HoverOverlayBackgroundBrush, rect, 4);
        context.DrawRectangle(HoverOverlayBorderPen, rect, 4);
        context.DrawText(content, new Point(rect.X + padX, rect.Y + padY));
    }

    private sealed record UniverseRegionLabelLayout(int RegionId, string RegionName, Point Center, Rect Rect, FormattedText Label);
    private readonly record struct PlotMetrics(double OriginX, double OriginY, double Width, double Height);
}
