using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.Rendering.Controls;

public sealed class MapControl : Control
{
    public static readonly StyledProperty<MapGraph?> GraphProperty =
        AvaloniaProperty.Register<MapControl, MapGraph?>(nameof(Graph));

    public static readonly StyledProperty<long?> SelectedNodeIdProperty =
        AvaloniaProperty.Register<MapControl, long?>(nameof(SelectedNodeId));
    public static readonly StyledProperty<MapViewMode> ViewModeProperty =
        AvaloniaProperty.Register<MapControl, MapViewMode>(nameof(ViewMode), MapViewMode.Universe);

    private Point? _lastPanPoint;
    private Point _panOffset = new(0, 0);
    private double _zoom = 1.0;
    private long? _hoveredNodeId;
    private MapGraph? _lastGraphForCaches;
    private readonly Dictionary<long, FormattedText> _nodeLabelCache = [];
    private readonly Dictionary<int, FormattedText> _regionLabelCache = [];
    private const double BasePadding = 0.0;
    private const double FitPadding = 30.0;

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

    public MapControl()
    {
        AffectsRender<MapControl>(GraphProperty, SelectedNodeIdProperty, ViewModeProperty);
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

        var plotWidth = Math.Max(1.0, Bounds.Width - (BasePadding * 2));
        var plotHeight = Math.Max(1.0, Bounds.Height - (BasePadding * 2));

        var minX = Graph.Nodes.Min(n => n.X);
        var maxX = Graph.Nodes.Max(n => n.X);
        var minY = Graph.Nodes.Min(n => n.Y);
        var maxY = Graph.Nodes.Max(n => n.Y);

        var graphWidthPx = Math.Max(1e-9, (maxX - minX) * plotWidth);
        var graphHeightPx = Math.Max(1e-9, (maxY - minY) * plotHeight);

        var availableWidth = Math.Max(1.0, plotWidth - (FitPadding * 2));
        var availableHeight = Math.Max(1.0, plotHeight - (FitPadding * 2));

        var zoomX = availableWidth / graphWidthPx;
        var zoomY = availableHeight / graphHeightPx;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.4, GetMaxZoom());

        var baseCenterX = BasePadding + (((minX + maxX) * 0.5) * plotWidth);
        var baseCenterY = BasePadding + (((minY + maxY) * 0.5) * plotHeight);
        var viewCenterX = Bounds.Width * 0.5;
        var viewCenterY = Bounds.Height * 0.5;

        _panOffset = new Point(
            viewCenterX - (((baseCenterX - viewCenterX) * _zoom) + viewCenterX),
            viewCenterY - (((baseCenterY - viewCenterY) * _zoom) + viewCenterY));

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (!ReferenceEquals(_lastGraphForCaches, Graph))
        {
            _lastGraphForCaches = Graph;
            _nodeLabelCache.Clear();
            _regionLabelCache.Clear();
        }

        var bounds = Bounds;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0D131D")), bounds);

        if (Graph is null || Graph.Nodes.Count == 0)
        {
            DrawCenteredText(context, "No map data loaded", bounds);
            return;
        }

        var linksPen = new Pen(new SolidColorBrush(Color.Parse("#304762")), 1);
        var sameConstellationPen = new Pen(new SolidColorBrush(Color.Parse("#4D6FA2")), 1.1);
        var sameRegionPen = new Pen(new SolidColorBrush(Color.Parse("#3E8A7E")), 1.1);
        var crossRegionPen = new Pen(new SolidColorBrush(Color.Parse("#8E5C8A")), 1.1);
        var highlightedDefaultPen = new Pen(new SolidColorBrush(Color.Parse("#8CB3DD")), 2.2);
        var highlightedSameConstellationPen = new Pen(new SolidColorBrush(Color.Parse("#7FA7E3")), 2.2);
        var highlightedSameRegionPen = new Pen(new SolidColorBrush(Color.Parse("#63C2B2")), 2.2);
        var highlightedCrossRegionPen = new Pen(new SolidColorBrush(Color.Parse("#C28ABB")), 2.2);
        var nodeBrush = new SolidColorBrush(Color.Parse("#8FB0D9"));
        var selectedBrush = new SolidColorBrush(Color.Parse("#E8B75E"));
        var hoveredBrush = new SolidColorBrush(Color.Parse("#7CC8FF"));

        var positions = Graph.Nodes.ToDictionary(n => n.Id, ToScreenPoint);
        var nodeById = Graph.Nodes.ToDictionary(n => n.Id);

        foreach (var link in Graph.Links)
        {
            if (!positions.TryGetValue(link.FromId, out var from) || !positions.TryGetValue(link.ToId, out var to))
            {
                continue;
            }

            if (!IsSegmentPotentiallyVisible(from, to, bounds, 48))
            {
                continue;
            }

            var isSelectedLink = SelectedNodeId is not null &&
                                 (link.FromId == SelectedNodeId.Value || link.ToId == SelectedNodeId.Value);
            var isHoveredLink = _hoveredNodeId is not null &&
                                (link.FromId == _hoveredNodeId.Value || link.ToId == _hoveredNodeId.Value);

            var basePen = GetLinkPen(linksPen, sameConstellationPen, sameRegionPen, crossRegionPen, link, nodeById);
            var pen = isSelectedLink || isHoveredLink
                ? GetHighlightedPen(basePen, linksPen, sameConstellationPen, sameRegionPen, crossRegionPen, highlightedDefaultPen, highlightedSameConstellationPen, highlightedSameRegionPen, highlightedCrossRegionPen)
                : basePen;

            context.DrawLine(pen, from, to);
        }

        var labelBudget = GetLabelBudget();
        var labelsDrawn = 0;
        foreach (var node in Graph.Nodes)
        {
            if (!positions.TryGetValue(node.Id, out var p))
            {
                continue;
            }

            if (!IsPointVisible(p, bounds, 24))
            {
                continue;
            }

            var isSelected = SelectedNodeId == node.Id;
            var isHovered = _hoveredNodeId == node.Id;
            var radius = isSelected ? 4.8 : isHovered ? 4.2 : 3.2;
            var brush = isSelected ? selectedBrush : isHovered ? hoveredBrush : nodeBrush;
            context.DrawEllipse(brush, null, p, radius, radius);

            var labelVisibilityMargin = ViewMode == MapViewMode.Universe ? 180 : 96;
            if ((_zoom >= GetLabelZoomThreshold() || isSelected || isHovered) &&
                labelsDrawn < labelBudget &&
                IsPointVisible(p, bounds, labelVisibilityMargin))
            {
                var label = GetNodeLabel(node.Id, node.Name);

                context.DrawText(label, new Point(p.X + 6, p.Y - 7));
                labelsDrawn++;
            }
        }

        if (_hoveredNodeId is not null &&
            positions.TryGetValue(_hoveredNodeId.Value, out var hoverPoint) &&
            nodeById.TryGetValue(_hoveredNodeId.Value, out var hoverNode))
        {
            DrawTooltip(context, hoverPoint, hoverNode.Name);
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

    private void DrawUniverseRegionLabels(DrawingContext context)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

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
            var name = group.First().RegionName!;
            var label = GetRegionLabel(group.Key, name);

            var padX = 6.0;
            var padY = 3.0;
            var rect = new Rect(
                center.X - (label.Width / 2.0) - padX,
                center.Y - (label.Height / 2.0) - padY,
                label.Width + (padX * 2),
                label.Height + (padY * 2));

            context.FillRectangle(new SolidColorBrush(Color.Parse("#1A2536")), rect, 4);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#3F5C83")), 1), rect, 4);

            context.DrawText(label, new Point(center.X - (label.Width / 2.0), center.Y - (label.Height / 2.0)));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsLeftButtonPressed)
        {
            SelectNodeAt(point);
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
        var worldX = ((mouse.X - (Bounds.Width / 2.0) - _panOffset.X) / oldZoom) + (Bounds.Width / 2.0);
        var worldY = ((mouse.Y - (Bounds.Height / 2.0) - _panOffset.Y) / oldZoom) + (Bounds.Height / 2.0);

        _zoom = newZoom;
        _panOffset = new Point(
            mouse.X - (((worldX - (Bounds.Width / 2.0)) * _zoom) + (Bounds.Width / 2.0)),
            mouse.Y - (((worldY - (Bounds.Height / 2.0)) * _zoom) + (Bounds.Height / 2.0)));

        InvalidateVisual();
    }

    private void SelectNodeAt(Point point)
    {
        if (Graph is null || Graph.Nodes.Count == 0)
        {
            return;
        }

        const double threshold = 8.0;
        var closest = Graph.Nodes
            .Select(n => new { n.Id, Screen = ToScreenPoint(n), Dist = Distance(ToScreenPoint(n), point) })
            .Where(x => x.Dist <= threshold)
            .OrderBy(x => x.Dist)
            .FirstOrDefault();

        if (closest is not null)
        {
            SelectedNodeId = closest.Id;
        }
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

            return;
        }

        const double threshold = 10.0;
        var closest = Graph.Nodes
            .Select(n => new { n.Id, Dist = Distance(ToScreenPoint(n), point) })
            .Where(x => x.Dist <= threshold)
            .OrderBy(x => x.Dist)
            .FirstOrDefault();

        var hoverId = closest?.Id;
        if (_hoveredNodeId != hoverId)
        {
            _hoveredNodeId = hoverId;
            InvalidateVisual();
        }
    }

    private Point ToScreenPoint(MapNode node)
    {
        var padding = BasePadding;
        var w = Math.Max(1.0, Bounds.Width - (padding * 2));
        var h = Math.Max(1.0, Bounds.Height - (padding * 2));

        var x = padding + (node.X * w);
        var y = padding + (node.Y * h);

        var centeredX = ((x - Bounds.Width / 2.0) * _zoom) + (Bounds.Width / 2.0) + _panOffset.X;
        var centeredY = ((y - Bounds.Height / 2.0) * _zoom) + (Bounds.Height / 2.0) + _panOffset.Y;

        return new Point(centeredX, centeredY);
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
        if (_nodeLabelCache.TryGetValue(nodeId, out var text))
        {
            return text;
        }

        text = new FormattedText(
            name,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11,
            new SolidColorBrush(Color.Parse("#BFD1E8")));
        _nodeLabelCache[nodeId] = text;
        return text;
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

    private static void DrawCenteredText(DrawingContext context, string message, Rect bounds)
    {
        var text = new FormattedText(
            message,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            14,
            new SolidColorBrush(Color.Parse("#9FB4D2")));

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

        context.FillRectangle(new SolidColorBrush(Color.Parse("#1A2536")), rect, 4);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#3B5678")), 1), rect, 4);
        context.DrawText(content, new Point(rect.X + padX, rect.Y + padY));
    }
}
