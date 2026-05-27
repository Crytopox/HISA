using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    private static readonly Typeface NodeLabelTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private const double NodeLabelFontSize = 11.5;
    private const double NodeRegionConstellationFontSize = 10.5;
    private const double UniverseMinNodeScale = 0.55;
    private const double IconSize = 18.0;
    private const double IndicatorIconLeftPadding = 4.0;
    private const double IndicatorIconSlotGap = 3.0;
    private const string A0BlueSmallName = "Sun A0 (Blue Small)";
    private const int A0BlueSmallTypeId = 3801;

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
    private static readonly IBrush NodeLabelBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#B5000000"));
    private static readonly IBrush TooltipBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#1A2536"));
    private static readonly IBrush HoverOverlayBackgroundBrush = new ImmutableSolidColorBrush(Color.Parse("#99000000"));
    private static readonly Pen NodeOutlinePen = new(new ImmutableSolidColorBrush(Color.Parse("#88000000")), 1.1);
    private static readonly IBrush VoronoiBorderBrush = new ImmutableSolidColorBrush(Color.Parse("#AA0D131D"));
    private static readonly Pen TooltipBorderPen = new(new ImmutableSolidColorBrush(Color.Parse("#3B5678")), 1);
    private static readonly Pen HoverOverlayBorderPen = new(new ImmutableSolidColorBrush(Color.Parse("#4A617F")), 1);
    private static readonly IBrush EmptyTextBrush = new ImmutableSolidColorBrush(Color.Parse("#9FB4D2"));
    private static readonly Lazy<Bitmap?> A0StarIcon = new(LoadA0StarIcon);
    private static readonly Lazy<Bitmap?> JoveObservatoryIcon = new(LoadJoveObservatoryIcon);
    private static readonly Lazy<Bitmap?> IceFieldIcon = new(LoadIceFieldIcon);
    private static readonly Lazy<Bitmap?> StormElectricCenterIcon = new(() => LoadIcon("storm_electric_center.png"));
    private static readonly Lazy<Bitmap?> StormElectricStrongIcon = new(() => LoadIcon("storm_electric_strong.png"));
    private static readonly Lazy<Bitmap?> StormElectricWeakIcon = new(() => LoadIcon("storm_electric_weak.png"));
    private static readonly Lazy<Bitmap?> StormGammaCenterIcon = new(() => LoadIcon("storm_gamma_center.png"));
    private static readonly Lazy<Bitmap?> StormGammaStrongIcon = new(() => LoadIcon("storm_gamma_strong.png"));
    private static readonly Lazy<Bitmap?> StormGammaWeakIcon = new(() => LoadIcon("storm_gamma_weak.png"));
    private static readonly Lazy<Bitmap?> StormExoticCenterIcon = new(() => LoadIcon("storm_exotic_center.png"));
    private static readonly Lazy<Bitmap?> StormExoticStrongIcon = new(() => LoadIcon("storm_exotic_strong.png"));
    private static readonly Lazy<Bitmap?> StormExoticWeakIcon = new(() => LoadIcon("storm_exotic_weak.png"));
    private static readonly Lazy<Bitmap?> StormPlasmaCenterIcon = new(() => LoadIcon("storm_plasma_center.png"));
    private static readonly Lazy<Bitmap?> StormPlasmaStrongIcon = new(() => LoadIcon("storm_plasma_strong.png"));
    private static readonly Lazy<Bitmap?> StormPlasmaWeakIcon = new(() => LoadIcon("storm_plasma_weak.png"));
    private static readonly Lazy<Bitmap?> StormUnknownIcon = new(() => LoadIcon("storm_unknown.png"));
    private static readonly Lazy<Bitmap?> WormholeIcon = new(() => LoadIcon("wormhole.png"));

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
    public static readonly StyledProperty<bool> ShowIndicatorRegionProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorRegion), false);
    public static readonly StyledProperty<bool> ShowIndicatorConstellationProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorConstellation), false);
    public static readonly StyledProperty<bool> ShowIndicatorSecurityStatusProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorSecurityStatus), false);
    public static readonly StyledProperty<bool> ShowIndicatorStarClassProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorStarClass), false);
    public static readonly StyledProperty<bool> ShowIndicatorA0StarIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorA0StarIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorJoveObservatoryIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorJoveObservatoryIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorIceBeltsIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorIceBeltsIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorStormIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorStormIcon), true);
    public static readonly StyledProperty<bool> ShowIndicatorWormholeIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ShowIndicatorWormholeIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowRegionProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowRegion), true);
    public static readonly StyledProperty<bool> InfoBoxShowConstellationProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowConstellation), true);
    public static readonly StyledProperty<bool> InfoBoxShowSecurityStatusProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowSecurityStatus), true);
    public static readonly StyledProperty<bool> InfoBoxShowStarClassProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowStarClass), false);
    public static readonly StyledProperty<bool> InfoBoxShowA0StarIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowA0StarIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowJoveObservatoryIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowJoveObservatoryIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowIceBeltsIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowIceBeltsIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowStormIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowStormIcon), true);
    public static readonly StyledProperty<bool> InfoBoxShowWormholeIconProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(InfoBoxShowWormholeIcon), true);
    public static readonly StyledProperty<bool> AlwaysShowHubWormholesProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(AlwaysShowHubWormholes), false);
    public static readonly StyledProperty<HubWormholeMarkerMode> HubWormholeMarkerModeProperty =
        AvaloniaProperty.Register<MapControl, HubWormholeMarkerMode>(nameof(HubWormholeMarkerMode), HubWormholeMarkerMode.Badge);

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
    private readonly Dictionary<string, FormattedText> _nodeSecondaryLabelCache = [];
    private readonly Dictionary<string, FormattedText> _nodeSecondaryLabelHaloCache = [];
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

    public bool ShowIndicatorRegion
    {
        get => GetValue(ShowIndicatorRegionProperty);
        set => SetValue(ShowIndicatorRegionProperty, value);
    }

    public bool ShowIndicatorConstellation
    {
        get => GetValue(ShowIndicatorConstellationProperty);
        set => SetValue(ShowIndicatorConstellationProperty, value);
    }

    public bool ShowIndicatorSecurityStatus
    {
        get => GetValue(ShowIndicatorSecurityStatusProperty);
        set => SetValue(ShowIndicatorSecurityStatusProperty, value);
    }

    public bool ShowIndicatorStarClass
    {
        get => GetValue(ShowIndicatorStarClassProperty);
        set => SetValue(ShowIndicatorStarClassProperty, value);
    }

    public bool ShowIndicatorA0StarIcon
    {
        get => GetValue(ShowIndicatorA0StarIconProperty);
        set => SetValue(ShowIndicatorA0StarIconProperty, value);
    }

    public bool ShowIndicatorJoveObservatoryIcon
    {
        get => GetValue(ShowIndicatorJoveObservatoryIconProperty);
        set => SetValue(ShowIndicatorJoveObservatoryIconProperty, value);
    }

    public bool ShowIndicatorIceBeltsIcon
    {
        get => GetValue(ShowIndicatorIceBeltsIconProperty);
        set => SetValue(ShowIndicatorIceBeltsIconProperty, value);
    }

    public bool ShowIndicatorStormIcon
    {
        get => GetValue(ShowIndicatorStormIconProperty);
        set => SetValue(ShowIndicatorStormIconProperty, value);
    }

    public bool ShowIndicatorWormholeIcon
    {
        get => GetValue(ShowIndicatorWormholeIconProperty);
        set => SetValue(ShowIndicatorWormholeIconProperty, value);
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

    public bool InfoBoxShowSecurityStatus
    {
        get => GetValue(InfoBoxShowSecurityStatusProperty);
        set => SetValue(InfoBoxShowSecurityStatusProperty, value);
    }

    public bool InfoBoxShowStarClass
    {
        get => GetValue(InfoBoxShowStarClassProperty);
        set => SetValue(InfoBoxShowStarClassProperty, value);
    }

    public bool InfoBoxShowA0StarIcon
    {
        get => GetValue(InfoBoxShowA0StarIconProperty);
        set => SetValue(InfoBoxShowA0StarIconProperty, value);
    }

    public bool InfoBoxShowJoveObservatoryIcon
    {
        get => GetValue(InfoBoxShowJoveObservatoryIconProperty);
        set => SetValue(InfoBoxShowJoveObservatoryIconProperty, value);
    }

    public bool InfoBoxShowIceBeltsIcon
    {
        get => GetValue(InfoBoxShowIceBeltsIconProperty);
        set => SetValue(InfoBoxShowIceBeltsIconProperty, value);
    }

    public bool InfoBoxShowStormIcon
    {
        get => GetValue(InfoBoxShowStormIconProperty);
        set => SetValue(InfoBoxShowStormIconProperty, value);
    }

    public bool InfoBoxShowWormholeIcon
    {
        get => GetValue(InfoBoxShowWormholeIconProperty);
        set => SetValue(InfoBoxShowWormholeIconProperty, value);
    }

    public bool AlwaysShowHubWormholes
    {
        get => GetValue(AlwaysShowHubWormholesProperty);
        set => SetValue(AlwaysShowHubWormholesProperty, value);
    }

    public HubWormholeMarkerMode HubWormholeMarkerMode
    {
        get => GetValue(HubWormholeMarkerModeProperty);
        set => SetValue(HubWormholeMarkerModeProperty, value);
    }

    public MapControl()
    {
        AffectsRender<MapControl>(GraphProperty, SelectedNodeIdProperty, ViewModeProperty, StretchToWindowProperty);
        AffectsRender<MapControl>(
            NodeColorModeProperty,
            NodeBackgroundColorModeProperty,
            ShowIndicatorRegionProperty,
            ShowIndicatorConstellationProperty,
            ShowIndicatorSecurityStatusProperty,
            ShowIndicatorStarClassProperty,
            ShowIndicatorA0StarIconProperty,
            ShowIndicatorJoveObservatoryIconProperty,
            ShowIndicatorIceBeltsIconProperty,
            ShowIndicatorStormIconProperty,
            ShowIndicatorWormholeIconProperty,
            InfoBoxShowRegionProperty,
            InfoBoxShowConstellationProperty,
            InfoBoxShowSecurityStatusProperty,
            InfoBoxShowStarClassProperty,
            InfoBoxShowA0StarIconProperty,
            InfoBoxShowJoveObservatoryIconProperty,
            InfoBoxShowIceBeltsIconProperty,
            InfoBoxShowStormIconProperty,
            InfoBoxShowWormholeIconProperty,
            AlwaysShowHubWormholesProperty,
            HubWormholeMarkerModeProperty);
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
            _nodeSecondaryLabelCache.Clear();
            _nodeSecondaryLabelHaloCache.Clear();
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
                var holeRadius = 10.8 * GetUniverseNodeZoomScale();
                context.DrawEllipse(NodeHoleBrush, null, _screenPositions[index], holeRadius, holeRadius);
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
            var universeNodeScale = GetUniverseNodeZoomScale();
            var radius = (isSelected ? 9.3 : isHovered ? 8.4 : isSearchHighlighted ? 7.8 : 6.75) * universeNodeScale;
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
            if (AlwaysShowHubWormholes && node.HubWormholeConnections.Count > 0)
            {
                switch (HubWormholeMarkerMode)
                {
                    case HubWormholeMarkerMode.Ring:
                        DrawHubWormholeRing(context, p, node, radius);
                        break;
                    case HubWormholeMarkerMode.Halo:
                        DrawHubWormholeHalo(context, p, node, radius);
                        break;
                    default:
                        DrawHubWormholeBeacon(context, p, node);
                        break;
                }
            }

            var labelVisibilityMargin = ViewMode == MapViewMode.Universe ? 180 : 96;
            var suppressInlineLabel =
                (SelectedNodeId is not null && node.Id == SelectedNodeId.Value) ||
                (_hoveredNodeId is not null && node.Id == _hoveredNodeId.Value);
            if (!suppressInlineLabel &&
                (_zoom >= GetLabelZoomThreshold() || isSelected || isHovered) &&
                labelsDrawn < labelBudget &&
                IsPointVisible(p, bounds, labelVisibilityMargin))
            {
                var labelOrigin = GetNodeLabelOrigin(p);
                DrawIndicatorLabel(context, node, labelOrigin);
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
            MapViewMode.Universe => 5.4,
            MapViewMode.UniverseRegions => 0.5,
            MapViewMode.Region => 0.45,
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

    private double GetUniverseNodeZoomScale()
    {
        if (ViewMode != MapViewMode.Universe)
        {
            return 1.0;
        }

        var threshold = GetLabelZoomThreshold();
        if (_zoom >= threshold)
        {
            return 1.0;
        }

        const double minZoom = 0.4;
        var progress = (_zoom - minZoom) / (threshold - minZoom);
        progress = Math.Clamp(progress, 0.0, 1.0);
        return UniverseMinNodeScale + ((1.0 - UniverseMinNodeScale) * progress);
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
            MapViewMode.Universe => 60.0,
            MapViewMode.Region => 3.0,
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
            NodeLabelTypeface,
            NodeLabelFontSize,
            new SolidColorBrush(Color.Parse("#EEF6FF")));
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
            NodeLabelTypeface,
            NodeLabelFontSize,
            new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        _nodeLabelHaloCache[key] = text;
        return text;
    }

    private FormattedText GetNodeSecondaryLabel(long nodeId, string name)
    {
        var key = $"{nodeId}:{name}";
        if (_nodeSecondaryLabelCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            NodeLabelFontSize,
            new SolidColorBrush(Color.Parse("#E6F0FF")));
        _nodeSecondaryLabelCache[key] = text;
        return text;
    }

    private FormattedText GetNodeSecondaryLabelHalo(long nodeId, string name)
    {
        var key = $"{nodeId}:{name}";
        if (_nodeSecondaryLabelHaloCache.TryGetValue(key, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            NodeLabelTypeface,
            NodeLabelFontSize,
            new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        _nodeSecondaryLabelHaloCache[key] = text;
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

        if (NodeBackgroundColorMode == MapNodeColorMode.JoveObservatory && !node.HasJoveObservatory)
        {
            return;
        }

        if (NodeBackgroundColorMode == MapNodeColorMode.IceBelts && node.IceFieldCount <= 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.Storms && node.StormEffects.Count == 0)
        {
            return;
        }
        if (NodeBackgroundColorMode == MapNodeColorMode.Wormholes && node.HubWormholeConnections.Count == 0)
        {
            return;
        }

        var color = GetNodeBaseColor(node, NodeBackgroundColorMode);
        var tuned = BrightenForBackground(color);
        var baseFill = GetCachedBrush(tuned, 0.40);
        context.DrawGeometry(baseFill, borderPen, geometry);
    }


    private Color GetNodeBaseColor(MapNode node, MapNodeColorMode mode)
    {
        return mode switch
        {
            MapNodeColorMode.Security => GetSecurityColor(node),
            MapNodeColorMode.Region => GetRegionColor(node.RegionId),
            MapNodeColorMode.Star => GetStarColor(node),
            MapNodeColorMode.NullsecTrueSec => GetNullsecTrueSecColor(node),
            MapNodeColorMode.JoveObservatory => node.HasJoveObservatory ? Color.Parse("#2ED436") : Color.Parse("#98A6B8"),
            MapNodeColorMode.IceBelts => node.IceFieldCount > 0 ? Color.Parse("#58B9FF") : Color.Parse("#98A6B8"),
            MapNodeColorMode.Storms => GetStormColor(node),
            MapNodeColorMode.Wormholes => GetHubWormholeColor(node),
            _ => Color.Parse("#98A6B8")
        };
    }

    private static Color GetHubWormholeColor(MapNode node)
    {
        var hasThera = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Thera);
        var hasTurnur = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Turnur);
        if (!hasThera && !hasTurnur)
        {
            return Color.Parse("#98A6B8");
        }

        if (hasThera && hasTurnur)
        {
            return Color.Parse("#ff0000");
        }

        return hasThera ? Color.Parse("#00ff00") : Color.Parse("#ff9c1a");
    }

    private static Color GetStormColor(MapNode node)
    {
        var primary = GetPrimaryStormEffect(node);
        if (primary is null)
        {
            return Color.Parse("#98A6B8");
        }

        var baseColor = GetStormBaseColor(primary.Type);
        return primary.Strength switch
        {
            StormStrength.Center => baseColor,
            StormStrength.Strong => LerpColor(baseColor, Color.FromRgb(210, 210, 210), 0.25),
            _ => LerpColor(baseColor, Color.FromRgb(210, 210, 210), 0.50)
        };
    }

    private static Color GetStormBaseColor(StormType type)
    {
        return type switch
        {
            StormType.Electrical => Color.Parse("#4AA8FF"),
            StormType.Plasma => Color.Parse("#DE5B52"),
            StormType.Gamma => Color.Parse("#E69138"),
            StormType.Exotic => Color.Parse("#CFD4DC"),
            _ => Color.Parse("#A9B2BF")
        };
    }

    private static StormEffect? GetPrimaryStormEffect(MapNode node)
    {
        if (node.StormEffects.Count == 0)
        {
            return null;
        }

        return node.StormEffects
            .OrderByDescending(e => e.Strength)
            .ThenBy(e => e.Type)
            .FirstOrDefault();
    }

    private static Color WithAlpha(Color color, double alpha01)
    {
        var a = (byte)Math.Clamp((int)(alpha01 * 255), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    private static Color GetSecurityColor(MapNode node)
    {
        var rounded = RoundSecurityForDisplay(node.Security ?? 0.0);
        return rounded switch
        {
            >= 1.0 => Color.Parse("#2C75E1"),
            >= 0.9 => Color.Parse("#399AEB"),
            >= 0.8 => Color.Parse("#4ECEF8"),
            >= 0.7 => Color.Parse("#60DBA3"),
            >= 0.6 => Color.Parse("#71E754"),
            >= 0.5 => Color.Parse("#F5FF83"),
            >= 0.4 => Color.Parse("#DC6C06"),
            >= 0.3 => Color.Parse("#CE440F"),
            >= 0.2 => Color.Parse("#BB1116"),
            >= 0.1 => Color.Parse("#731F1F"),
            _ => Color.Parse("#8D3163")
        };
    }

    private static double RoundSecurityForDisplay(double security)
    {
        if (security == 0.0d)
        {
            return 0.0d;
        }

        if (security > 0.0d && security < 0.05d)
        {
            return Math.Round((security * 10.0d) + 0.5d, MidpointRounding.AwayFromZero) / 10.0d;
        }

        return Math.Round(security * 10.0d, MidpointRounding.AwayFromZero) / 10.0d;
    }

    private string BuildIndicatorLabel(MapNode node) => node.Name;

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

    private static Color GetStarColor(MapNode node)
    {
        var raw = (node.StarTypeName ?? node.SpectralClass ?? string.Empty).Trim();
        var type = raw.ToUpperInvariant();

        if (type.Length > 0)
        {
            // Explicit in-game type-name mapping first.
            // A0 Blue Small is intentionally the most visible blue.
            if (type == "SUN A0 (BLUE SMALL)")
            {
                return Color.Parse("#4AA3FF");
            }
            if (type == "SUN A0 (CAPTURED BLUE SMALL)")
            {
                return Color.Parse("#63AEFF");
            }
            if (type == "SUN A0 (DISRUPTED BLUE SMALL)")
            {
                return Color.Parse("#6DB6FF");
            }
            if (type == "SUN A0 (GLORY IMMANENCE)")
            {
                return Color.Parse("#5AA9FF");
            }
            if (type == "SUN A0IV (TURBULENT BLUE SUBGIANT)")
            {
                return Color.Parse("#7CC0FF");
            }
            if (type == "SUN B0 (BLUE)")
            {
                return Color.Parse("#79BCFF");
            }
            if (type == "SUN B0 (FRUITFUL IMMANENCE)")
            {
                return Color.Parse("#84C4FF");
            }
            if (type == "SUN B5 (WHITE DWARF)")
            {
                return Color.Parse("#EAF4FF");
            }
            if (type == "SUN F0 (WHITE)")
            {
                return Color.Parse("#F4F8FF");
            }
            if (type == "SUN G3 (PINK SMALL)")
            {
                return Color.Parse("#FFC6DA");
            }
            if (type == "SUN G5 (GOLD IMMANENCE)")
            {
                return Color.Parse("#FFD76B");
            }
            if (type == "SUN G5 (PINK)")
            {
                return Color.Parse("#FFB7D3");
            }
            if (type == "SUN G5 (YELLOW)")
            {
                return Color.Parse("#FFE08A");
            }
            if (type == "SUN K3 (YELLOW SMALL)")
            {
                return Color.Parse("#FFD98A");
            }
            if (type == "SUN K5 (ORANGE BRIGHT)")
            {
                return Color.Parse("#FFB35A");
            }
            if (type == "SUN K5 (RED GIANT)")
            {
                return Color.Parse("#FF8066");
            }
            if (type == "SUN K7 (ORANGE)")
            {
                return Color.Parse("#FFA55E");
            }
            if (type == "SUN M0 (ORANGE RADIANT)")
            {
                return Color.Parse("#FF9560");
            }
            if (type == "SUN O1 (BRIGHT BLUE)")
            {
                return Color.Parse("#66B7FF");
            }
            if (type == "SUN O1 (DIVINE IMMANENCE)")
            {
                return Color.Parse("#73BEFF");
            }

            // Fallback for compact type labels / spectral-like formats.
            var firstClassLetter = type.FirstOrDefault(c => c is >= 'A' and <= 'Z');
            switch (firstClassLetter)
            {
                case 'O': return Color.Parse("#6FA8FF");
                case 'B': return Color.Parse("#9CC4FF");
                case 'A': return Color.Parse("#E9F1FF");
                case 'F': return Color.Parse("#FFF4D6");
                case 'G': return Color.Parse("#FFE08A");
                case 'K': return Color.Parse("#FFB05A");
                case 'M': return Color.Parse("#FF6B5C");
                case 'L': return Color.Parse("#F45A46");
                case 'T': return Color.Parse("#C84B3A");
                case 'Y': return Color.Parse("#A74135");
            }
        }

        if (type.Contains("BLUE") || type.Contains("TYPE O"))
        {
            return Color.Parse("#8FB8FF");
        }
        if (type.Contains("TYPE B"))
        {
            return Color.Parse("#A5C7FF");
        }
        if (type.Contains("WHITE") || type.Contains("TYPE A"))
        {
            return Color.Parse("#EAF2FF");
        }
        if (type.Contains("TYPE F"))
        {
            return Color.Parse("#FFF6D1");
        }
        if (type.Contains("YELLOW") || type.Contains("TYPE G"))
        {
            return Color.Parse("#FFE08A");
        }
        if (type.Contains("ORANGE") || type.Contains("TYPE K"))
        {
            return Color.Parse("#FFB869");
        }
        if (type.Contains("RED") || type.Contains("TYPE M"))
        {
            return Color.Parse("#FF7E6B");
        }
        if (type.Contains("TYPE L"))
        {
            return Color.Parse("#FF985E");
        }
        if (type.Contains("TYPE T"))
        {
            return Color.Parse("#D45B4A");
        }
        if (type.Contains("TYPE Y"))
        {
            return Color.Parse("#B3433A");
        }
        if (type.Contains("DWARF"))
        {
            return Color.Parse("#E7F0FF");
        }
        if (type.Contains("CARBON"))
        {
            return Color.Parse("#FF9A87");
        }

        return Color.Parse("#C9D8EE");
    }

    private static Color GetNullsecTrueSecColor(MapNode node)
    {
        var security = node.Security ?? 0.0;
        if (security > 0.0)
        {
            return Color.Parse("#8FB0D9");
        }

        // Truesec gradient for null systems:
        // -0.1 => orange, mid => red, -1.0 => null purple.
        var clamped = Math.Clamp(security, -1.0, -0.1);
        var t = (-0.1 - clamped) / 0.9; // 0 at -0.1, 1 at -1.0

        var orange = Color.Parse("#FF9B4A");
        var red = Color.Parse("#D84545");
        var purple = Color.Parse("#8D3163");

        if (t < 0.55)
        {
            return LerpColor(orange, red, t / 0.55);
        }

        return LerpColor(red, purple, (t - 0.55) / 0.45);
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte Mix(byte x, byte y) => (byte)Math.Clamp((int)Math.Round(x + ((y - x) * t)), 0, 255);
        return Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    private static Color BrightenForBackground(Color color)
    {
        static byte Lift(byte c)
        {
            // Lift 16% toward white to make Voronoi/background fills read clearer.
            return (byte)Math.Clamp(c + ((255 - c) * 0.16), 0, 255);
        }

        return Color.FromRgb(Lift(color.R), Lift(color.G), Lift(color.B));
    }

    private static string? GetStarClassDisplayValue(MapNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.StarTypeName))
        {
            return node.StarTypeName;
        }

        if (!string.IsNullOrWhiteSpace(node.SpectralClass))
        {
            return node.SpectralClass;
        }

        return null;
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

    private void DrawIndicatorLabel(DrawingContext context, MapNode node, Point origin)
    {
        const double gap = 7.0;
        const double lineGap = 1.0;
        var nameText = BuildIndicatorLabel(node);
        var name = GetNodeLabel(node.Id, nameText);
        var nameHalo = GetNodeLabelHalo(node.Id, nameText);
        FormattedText? sec = null;
        FormattedText? secHalo = null;

        if (ShowIndicatorSecurityStatus && node.Security is not null)
        {
            var rounded = RoundSecurityForDisplay(node.Security.Value);
            var securityLabel = rounded.ToString("0.0", CultureInfo.InvariantCulture);
            sec = new FormattedText(
                securityLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeLabelFontSize,
                GetCachedBrush(GetSecurityColor(node)));
            secHalo = new FormattedText(
                securityLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeLabelFontSize,
                new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        }

        FormattedText? region = null;
        FormattedText? regionHalo = null;
        if (ShowIndicatorRegion && !string.IsNullOrWhiteSpace(node.RegionName))
        {
            region = new FormattedText(
                node.RegionName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeRegionConstellationFontSize-1,
                new SolidColorBrush(Color.Parse("#E6F0FF")));
            regionHalo = new FormattedText(
                node.RegionName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeRegionConstellationFontSize-1,
                new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        }

        FormattedText? constellation = null;
        FormattedText? constellationHalo = null;
        if (ShowIndicatorConstellation && !string.IsNullOrWhiteSpace(node.ConstellationName))
        {
            constellation = new FormattedText(
                node.ConstellationName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeRegionConstellationFontSize-1,
                new SolidColorBrush(Color.Parse("#E6F0FF")));
            constellationHalo = new FormattedText(
                node.ConstellationName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeRegionConstellationFontSize-1,
                new ImmutableSolidColorBrush(Color.Parse("#AA0A111A")));
        }

        var starClass = GetStarClassDisplayValue(node);
        FormattedText? starClassText = null;
        FormattedText? starClassHalo = null;
        if (ShowIndicatorStarClass && !string.IsNullOrWhiteSpace(starClass))
        {
            starClassText = GetNodeSecondaryLabel(node.Id, starClass);
            starClassHalo = GetNodeSecondaryLabelHalo(node.Id, starClass);
        }

        var firstLineWidth = name.Width + (sec is null ? 0 : gap + sec.Width);
        var width = firstLineWidth;
        if (region is not null)
        {
            width = Math.Max(width, region.Width);
        }
        if (constellation is not null)
        {
            width = Math.Max(width, constellation.Width);
        }
        if (starClassText is not null)
        {
            width = Math.Max(width, starClassText.Width);
        }

        var height = name.Height;
        if (region is not null)
        {
            height += lineGap + region.Height;
        }
        if (constellation is not null)
        {
            height += lineGap + constellation.Height;
        }
        if (starClassText is not null)
        {
            height += lineGap + starClassText.Height;
        }

        var rect = new Rect(origin.X - 3, origin.Y - 2, width + 6, height + 4);
        context.FillRectangle(NodeLabelBackgroundBrush, rect, 3);

        DrawLabelWithHalo(context, name, nameHalo, origin);
        if (sec is not null && secHalo is not null)
        {
            var secOrigin = new Point(origin.X + name.Width + gap, origin.Y);
            DrawLabelWithHalo(context, sec, secHalo, secOrigin);
        }

        var y = origin.Y + name.Height + lineGap;
        if (region is not null && regionHalo is not null)
        {
            DrawLabelWithHalo(context, region, regionHalo, new Point(origin.X, y));
            y += region.Height + lineGap;
        }

        if (constellation is not null && constellationHalo is not null)
        {
            DrawLabelWithHalo(context, constellation, constellationHalo, new Point(origin.X, y));
            y += constellation.Height + lineGap;
        }

        if (starClassText is not null && starClassHalo is not null)
        {
            DrawLabelWithHalo(context, starClassText, starClassHalo, new Point(origin.X, y));
        }

        var indicatorIconSlot = 0;
        if (ShowIndicatorA0StarIcon && IsA0BlueSmall(node))
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (indicatorIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom;
            DrawA0Icon(context, new Point(iconX, iconY), IconSize);
            indicatorIconSlot++;
        }
        if (ShowIndicatorJoveObservatoryIcon && node.HasJoveObservatory)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (indicatorIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom;
            DrawJoveObservatoryIcon(context, new Point(iconX, iconY), IconSize);
            indicatorIconSlot++;
        }
        if (ShowIndicatorIceBeltsIcon && node.IceFieldCount > 0)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (indicatorIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom;
            DrawIceFieldIcon(context, new Point(iconX, iconY), IconSize);
            indicatorIconSlot++;
        }
        if (ShowIndicatorStormIcon && node.StormEffects.Count > 0)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (indicatorIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom;
            DrawStormIcon(context, node, new Point(iconX, iconY), IconSize);
            indicatorIconSlot++;
        }
        if (ShowIndicatorWormholeIcon && node.HubWormholeConnections.Count > 0)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (indicatorIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom;
            DrawHubWormholeIcon(context, node, new Point(iconX, iconY), IconSize);
        }
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
        var header = node.Name;
        var detailLines = new List<string>();
        var regionText = (InfoBoxShowRegion && !string.IsNullOrWhiteSpace(node.RegionName))
            ? $"{node.RegionName}"
            : null;
        var constellationText = (InfoBoxShowConstellation && !string.IsNullOrWhiteSpace(node.ConstellationName))
            ? $"{node.ConstellationName}"
            : null;
        var regionConstellationLine = regionText is not null || constellationText is not null
            ? (regionText is not null && constellationText is not null
                ? $"{regionText} | {constellationText}"
                : regionText ?? constellationText!)
            : null;
        var starClass = GetStarClassDisplayValue(node);
        if (InfoBoxShowStarClass && !string.IsNullOrWhiteSpace(starClass))
        {
            detailLines.Add(starClass);
        }
        if (node.StormEffects.Count > 0)
        {
            foreach (var storm in node.StormEffects.OrderByDescending(e => e.Strength).ThenBy(e => e.Type))
            {
                detailLines.Add($"Storm: {storm.Strength} {storm.Type}");
            }
        }
        var wormholes = node.HubWormholeConnections
            .OrderBy(c => c.HubType)
            .ToList();
        var headerText = new FormattedText(
            header,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            12,
            Brushes.White);
        FormattedText? securityText = null;
        if (InfoBoxShowSecurityStatus && node.Security is not null)
        {
            var rounded = RoundSecurityForDisplay(node.Security.Value);
            securityText = new FormattedText(
                rounded.ToString("0.0", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                12,
                GetCachedBrush(GetSecurityColor(node)));
        }
        var detailsText = detailLines.Count == 0
            ? null
            : new FormattedText(
                string.Join('\n', detailLines),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                12,
                Brushes.White);
        var regionConstellationText = regionConstellationLine is null
            ? null
            : new FormattedText(
                regionConstellationLine,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                NodeLabelTypeface,
                NodeRegionConstellationFontSize,
                Brushes.White);

        var wormholeLineHeight = 0.0;
        var wormholeMaxWidth = 0.0;
        foreach (var wh in wormholes)
        {
            var hub = new FormattedText($"{wh.HubType}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, Brushes.White);
            var sep = new FormattedText(" | ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#8A96A8")));
            var inSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.InSignature) ? "-" : wh.InSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#5EDC8A")));
            var outSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.OutSignature) ? "-" : wh.OutSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F07171")));
            var mass = new FormattedText($"[{GetWormholeMassShort(wh.MaxShipSize)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F5C06A")));
            var width = hub.Width + sep.Width + inSig.Width + sep.Width + outSig.Width + sep.Width + mass.Width;
            wormholeMaxWidth = Math.Max(wormholeMaxWidth, width);
            wormholeLineHeight = Math.Max(wormholeLineHeight, Math.Max(hub.Height, Math.Max(inSig.Height, outSig.Height)));
        }

        var start = GetNodeLabelOrigin(anchor);
        var padX = 8.0;
        var padY = 6.0;
        var headerWidth = headerText.Width + (securityText is null ? 0 : (8 + securityText.Width));
        var bodyWidth = Math.Max(Math.Max(regionConstellationText?.Width ?? 0, detailsText?.Width ?? 0), wormholeMaxWidth);
        var contentWidth = Math.Max(headerWidth, bodyWidth);
        var contentHeight = headerText.Height
            + (regionConstellationText is null ? 0 : regionConstellationText.Height + 2)
            + (detailsText is null ? 0 : detailsText.Height + 2)
            + (wormholes.Count == 0 ? 0 : (wormholes.Count * (wormholeLineHeight + 1)));
        var rect = new Rect(
            start.X - 2,
            start.Y - 2,
            contentWidth + (padX * 2),
            contentHeight + (padY * 2));

        context.FillRectangle(HoverOverlayBackgroundBrush, rect, 4);
        context.DrawRectangle(HoverOverlayBorderPen, rect, 4);
        var headerOrigin = new Point(rect.X + padX, rect.Y + padY);
        context.DrawText(headerText, headerOrigin);
        if (securityText is not null)
        {
            var secOrigin = new Point(headerOrigin.X + headerText.Width + 8, headerOrigin.Y);
            context.DrawText(securityText, secOrigin);
        }
        var detailsStartY = headerOrigin.Y + headerText.Height + 2;
        if (regionConstellationText is not null)
        {
            context.DrawText(regionConstellationText, new Point(headerOrigin.X, detailsStartY));
            detailsStartY += regionConstellationText.Height + 2;
        }
        if (detailsText is not null)
        {
            context.DrawText(detailsText, new Point(headerOrigin.X, detailsStartY));
            detailsStartY += detailsText.Height;
        }

        var wormholeStartY = detailsStartY;
        foreach (var wh in wormholes)
        {
            var hub = new FormattedText($"{wh.HubType}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, Brushes.White);
            var sep = new FormattedText(" | ", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#8A96A8")));
            var inSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.InSignature) ? "-" : wh.InSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#5EDC8A")));
            var outSig = new FormattedText($"[{(string.IsNullOrWhiteSpace(wh.OutSignature) ? "-" : wh.OutSignature!)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F07171")));
            var mass = new FormattedText($"[{GetWormholeMassShort(wh.MaxShipSize)}]", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 12, new ImmutableSolidColorBrush(Color.Parse("#F5C06A")));

            var lineX = headerOrigin.X;
            context.DrawText(hub, new Point(lineX, wormholeStartY));
            lineX += hub.Width;
            context.DrawText(sep, new Point(lineX, wormholeStartY));
            lineX += sep.Width;
            context.DrawText(inSig, new Point(lineX, wormholeStartY));
            lineX += inSig.Width;
            context.DrawText(sep, new Point(lineX, wormholeStartY));
            lineX += sep.Width;
            context.DrawText(outSig, new Point(lineX, wormholeStartY));
            lineX += outSig.Width;
            context.DrawText(sep, new Point(lineX, wormholeStartY));
            lineX += sep.Width;
            context.DrawText(mass, new Point(lineX, wormholeStartY));
            wormholeStartY += wormholeLineHeight + 1;
        }

        var overlayIconSlot = 0;
        if (InfoBoxShowA0StarIcon && IsA0BlueSmall(node))
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (overlayIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom + 3;
            DrawA0Icon(context, new Point(iconX, iconY), IconSize);
            overlayIconSlot++;
        }
        if (InfoBoxShowJoveObservatoryIcon && node.HasJoveObservatory)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (overlayIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom + 3;
            DrawJoveObservatoryIcon(context, new Point(iconX, iconY), IconSize);
            overlayIconSlot++;
        }
        if (InfoBoxShowIceBeltsIcon && node.IceFieldCount > 0)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (overlayIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom + 3;
            DrawIceFieldIcon(context, new Point(iconX, iconY), IconSize);
            overlayIconSlot++;
        }
        if (InfoBoxShowStormIcon && node.StormEffects.Count > 0)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (overlayIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom + 3;
            DrawStormIcon(context, node, new Point(iconX, iconY), IconSize);
            overlayIconSlot++;
        }
        if (InfoBoxShowWormholeIcon && node.HubWormholeConnections.Count > 0)
        {
            var iconX = rect.X + IndicatorIconLeftPadding + (overlayIconSlot * (IconSize + IndicatorIconSlotGap));
            var iconY = rect.Bottom + 3;
            DrawHubWormholeIcon(context, node, new Point(iconX, iconY), IconSize);
        }
    }

    private static string GetWormholeMassShort(string? maxShipSize)
    {
        return maxShipSize?.Trim().ToLowerInvariant() switch
        {
            "xlarge" => "XL",
            "large" => "L",
            "medium" => "M",
            "small" => "S",
            _ => "?"
        };
    }

    private static Bitmap? LoadA0StarIcon()
    {
        return LoadIcon("a0-star.png");
    }

    private static Bitmap? LoadJoveObservatoryIcon()
    {
        return LoadIcon("jove_observatory.png");
    }

    private static Bitmap? LoadIceFieldIcon()
    {
        return LoadIcon("iceField.png");
    }

    private static Bitmap? LoadIcon(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://Hisa.App/Assets/Icons/{fileName}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static void DrawStormIcon(DrawingContext context, MapNode node, Point topLeft, double size)
    {
        var effect = GetPrimaryStormEffect(node);
        var icon = effect switch
        {
            null => StormUnknownIcon.Value,
            { Type: StormType.Electrical, Strength: StormStrength.Center } => StormElectricCenterIcon.Value,
            { Type: StormType.Electrical, Strength: StormStrength.Strong } => StormElectricStrongIcon.Value,
            { Type: StormType.Electrical, Strength: StormStrength.Weak } => StormElectricWeakIcon.Value,
            { Type: StormType.Gamma, Strength: StormStrength.Center } => StormGammaCenterIcon.Value,
            { Type: StormType.Gamma, Strength: StormStrength.Strong } => StormGammaStrongIcon.Value,
            { Type: StormType.Gamma, Strength: StormStrength.Weak } => StormGammaWeakIcon.Value,
            { Type: StormType.Exotic, Strength: StormStrength.Center } => StormExoticCenterIcon.Value,
            { Type: StormType.Exotic, Strength: StormStrength.Strong } => StormExoticStrongIcon.Value,
            { Type: StormType.Exotic, Strength: StormStrength.Weak } => StormExoticWeakIcon.Value,
            { Type: StormType.Plasma, Strength: StormStrength.Center } => StormPlasmaCenterIcon.Value,
            { Type: StormType.Plasma, Strength: StormStrength.Strong } => StormPlasmaStrongIcon.Value,
            { Type: StormType.Plasma, Strength: StormStrength.Weak } => StormPlasmaWeakIcon.Value,
            _ => StormUnknownIcon.Value
        };
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static void DrawHubWormholeIcon(DrawingContext context, MapNode node, Point topLeft, double size)
    {
        var icon = WormholeIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static void DrawHubWormholeBeacon(DrawingContext context, Point nodePoint, MapNode node)
    {
        var hasThera = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Thera);
        var hasTurnur = node.HubWormholeConnections.Any(c => c.HubType == WormholeHubType.Turnur);
        var color = GetHubWormholeColor(node);
        var fill = new ImmutableSolidColorBrush(Color.FromArgb(230, color.R, color.G, color.B));
        var border = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(255, 15, 20, 28)), 1);
        var label = hasThera && hasTurnur ? "T+U" : hasThera ? "T" : "U";
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            10,
            Brushes.Black);

        var rect = new Rect(
            nodePoint.X + 7.0,
            nodePoint.Y - 14.0,
            Math.Max(14, text.Width + 6),
            12);
        context.FillRectangle(fill, rect, 3);
        context.DrawRectangle(border, rect, 3);
        context.DrawText(text, new Point(rect.X + ((rect.Width - text.Width) / 2), rect.Y + ((rect.Height - text.Height) / 2) - 0.5));
    }

    private static void DrawHubWormholeRing(DrawingContext context, Point nodePoint, MapNode node, double nodeRadius)
    {
        var color = GetHubWormholeColor(node);
        var pen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B)), 2.0);
        var ringRadius = nodeRadius + 3.4;
        context.DrawEllipse(null, pen, nodePoint, ringRadius, ringRadius);
    }

    private static void DrawHubWormholeHalo(DrawingContext context, Point nodePoint, MapNode node, double nodeRadius)
    {
        var color = GetHubWormholeColor(node);
        var halo = new ImmutableSolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B));
        var haloRadius = nodeRadius + 4.2;
        context.DrawEllipse(halo, null, nodePoint, haloRadius, haloRadius);
    }

    private static bool IsA0BlueSmall(MapNode node)
    {
        if (node.SunTypeId == A0BlueSmallTypeId)
        {
            return true;
        }

        var name = node.StarTypeName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (string.Equals(name, A0BlueSmallName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fallback for minor naming differences in imported datasets.
        return name.Contains("A0", StringComparison.OrdinalIgnoreCase)
            && name.Contains("Blue", StringComparison.OrdinalIgnoreCase)
            && name.Contains("Small", StringComparison.OrdinalIgnoreCase);
    }

    private static void DrawA0Icon(DrawingContext context, Point topLeft, double size)
    {
        var icon = A0StarIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static void DrawJoveObservatoryIcon(DrawingContext context, Point topLeft, double size)
    {
        var icon = JoveObservatoryIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private static void DrawIceFieldIcon(DrawingContext context, Point topLeft, double size)
    {
        var icon = IceFieldIcon.Value;
        if (icon is null)
        {
            return;
        }

        var src = new Rect(0, 0, icon.Size.Width, icon.Size.Height);
        var dst = new Rect(topLeft.X, topLeft.Y, size, size);
        context.DrawImage(icon, src, dst);
    }

    private sealed record UniverseRegionLabelLayout(int RegionId, string RegionName, Point Center, Rect Rect, FormattedText Label);
    private readonly record struct PlotMetrics(double OriginX, double OriginY, double Width, double Height);
}

