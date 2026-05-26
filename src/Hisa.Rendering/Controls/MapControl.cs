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

    private Point? _lastPanPoint;
    private Point _panOffset = new(0, 0);
    private double _zoom = 1.0;
    private long? _hoveredNodeId;
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

    public MapControl()
    {
        AffectsRender<MapControl>(GraphProperty, SelectedNodeIdProperty);
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
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.4, 12.0);

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
        var bounds = Bounds;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0D131D")), bounds);

        if (Graph is null || Graph.Nodes.Count == 0)
        {
            DrawCenteredText(context, "No map data loaded", bounds);
            return;
        }

        var linksPen = new Pen(new SolidColorBrush(Color.Parse("#304762")), 1);
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

            context.DrawLine(linksPen, from, to);
        }

        foreach (var node in Graph.Nodes)
        {
            if (!positions.TryGetValue(node.Id, out var p))
            {
                continue;
            }

            var isSelected = SelectedNodeId == node.Id;
            var isHovered = _hoveredNodeId == node.Id;
            var radius = isSelected ? 4.8 : isHovered ? 4.2 : 3.2;
            var brush = isSelected ? selectedBrush : isHovered ? hoveredBrush : nodeBrush;
            context.DrawEllipse(brush, null, p, radius, radius);

            if (_zoom >= 1.0 || isSelected || isHovered)
            {
                var label = new FormattedText(
                    node.Name,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    11,
                    new SolidColorBrush(Color.Parse("#BFD1E8")));

                context.DrawText(label, new Point(p.X + 6, p.Y - 7));
            }
        }

        if (_hoveredNodeId is not null &&
            positions.TryGetValue(_hoveredNodeId.Value, out var hoverPoint) &&
            nodeById.TryGetValue(_hoveredNodeId.Value, out var hoverNode))
        {
            DrawTooltip(context, hoverPoint, hoverNode.Name);
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
        var newZoom = Math.Clamp(_zoom * factor, 0.4, 12.0);
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

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
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
