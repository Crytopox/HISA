using Avalonia;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using Hisa.Core;
using Hisa.Core.Models;

namespace Hisa.Rendering.Controls;

public sealed partial class MapControl
{
    public static readonly StyledProperty<IReadOnlyList<MapReportPopupRequest>> ReportPopupRequestsProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyList<MapReportPopupRequest>>(nameof(ReportPopupRequests), []);
    public static readonly StyledProperty<bool> ReportPopupsEnabledProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ReportPopupsEnabled), true);
    public static readonly StyledProperty<int> ReportPopupSecondsProperty =
        AvaloniaProperty.Register<MapControl, int>(nameof(ReportPopupSeconds), 10);
    public static readonly StyledProperty<bool> ReportPopupsShowKillmailsProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ReportPopupsShowKillmails), true);
    public static readonly StyledProperty<bool> ReportPopupsShowInUniverseProperty =
        AvaloniaProperty.Register<MapControl, bool>(nameof(ReportPopupsShowInUniverse), true);
    public bool ReportPopupsShowKillmails { get => GetValue(ReportPopupsShowKillmailsProperty); set => SetValue(ReportPopupsShowKillmailsProperty, value); }
    public bool ReportPopupsShowInUniverse { get => GetValue(ReportPopupsShowInUniverseProperty); set => SetValue(ReportPopupsShowInUniverseProperty, value); }
    public IReadOnlyList<MapReportPopupRequest> ReportPopupRequests { get => GetValue(ReportPopupRequestsProperty); set => SetValue(ReportPopupRequestsProperty, value); }
    public bool ReportPopupsEnabled { get => GetValue(ReportPopupsEnabledProperty); set => SetValue(ReportPopupsEnabledProperty, value); }
    public int ReportPopupSeconds { get => GetValue(ReportPopupSecondsProperty); set => SetValue(ReportPopupSecondsProperty, value); }

    private sealed record NodeOverlayView(long NodeId, string SystemName, Rect Bounds, double MaxScroll,
        MapOverlayLayout Layout, double Scroll, bool Automatic);
    internal IReadOnlyList<(long NodeId, Rect Bounds, bool Automatic)> VisibleReportOverlays =>
        _nodeOverlayViews.Select(v => (v.NodeId, v.Bounds, v.Automatic)).ToArray();
    private sealed record OverlayScrollBar(long NodeId, string? ReportId, Rect Track, Rect Thumb, double Maximum);
    private sealed record OverlayDrag(OverlayScrollBar Bar, Point Start, double StartOffset);
    private sealed record MessageHit(long NodeId, string ReportId, Rect Bounds, double Maximum);
    private readonly MapReportPopupController _reportPopups = new();
    private readonly DispatcherTimer _reportOverlayTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _reportAgeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal Func<DateTime> ReportOverlayUtcNow { get; set; } = () => DateTime.UtcNow;
    private long _reportAgeSecond = long.MinValue;
    internal bool IsReportAgeTimerRunning => _reportAgeTimer.IsEnabled;
    internal bool RefreshReportOverlayAges()
    {
        var second = ReportOverlayUtcNow().Ticks / TimeSpan.TicksPerSecond;
        if (second == _reportAgeSecond) return false;
        _reportAgeSecond = second;
        _overlayLayouts.Clear();
        return true;
    }
    private readonly List<NodeOverlayView> _nodeOverlayViews = [];
    private readonly Dictionary<long, double> _nodeOverlayScroll = [];
    private readonly Dictionary<(MapNode Node, double Width, double MinimumWidth, string Reports), MapOverlayLayout> _overlayLayouts = [];
    private readonly Dictionary<long, MapOverlayLayout> _lastOverlayLayouts = [];
    private readonly Dictionary<long, double> _openOverlayWidths = [];
    private readonly Dictionary<long, double> _automaticOverlayWidths = [];
    private MapOverlayLayout BuildOpenNodeOverlay(MapNode node, IReadOnlySet<string>? reports = null)
    {
        var layout = BuildNodeOverlay(node, Bounds.Width - 16, reports, _openOverlayWidths.GetValueOrDefault(node.Id));
        _openOverlayWidths[node.Id] = Math.Max(_openOverlayWidths.GetValueOrDefault(node.Id), layout.Size.Width);
        return layout;
    }

    private readonly Dictionary<long, (string ReportId, double Distance)> _overlayAnchors = [];
    private readonly Dictionary<string, MapMessageScrollState> _messageScroll = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _messageLastFrame = new(StringComparer.Ordinal);
    private readonly List<OverlayScrollBar> _overlayScrollBars = [];
    private readonly List<MessageHit> _messageHits = [];
    private OverlayDrag? _overlayDrag;
    private string? _raisedPopupSystem;
    private DateTimeOffset _lastOverlayFrame = DateTimeOffset.UtcNow;
    private TimeSpan _overlayFrameElapsed;
    private long _overlayFrame;
    private bool _hasVisibleMarquee;
    private string? _hoveredMessageReportId;

    private void InitializeReportOverlays()
    {
        _reportAgeTimer.Tick += (_, _) =>
        {
            if (RefreshReportOverlayAges()) InvalidateVisual();
            UpdateReportOverlayTimer();
        };
        _reportOverlayTimer.Tick += (_, _) =>
        {
            _reportPopups.Tick();
            InvalidateInteractive();
            UpdateReportOverlayTimer();
        };
    }

    private void UpdateReportOverlayTimer()
    {
        if (_isAttachedToVisualTree && _nodeOverlayViews.Any(v => v.Layout.Reports.Count > 0))
            _reportAgeTimer.Start();
        else _reportAgeTimer.Stop();
        if (_isAttachedToVisualTree && !SuspendAnimations && (_reportPopups.Active.Count > 0 || _hasVisibleMarquee))
            _reportOverlayTimer.Start();
        else _reportOverlayTimer.Stop();
    }

    private void OnReportOverlayPropertyChanged(AvaloniaProperty property)
    {
        _overlayLayouts?.Clear();
        if (property == ReportPopupRequestsProperty || property == ReportPopupsEnabledProperty || property == ReportPopupSecondsProperty || property == ReportPopupsShowKillmailsProperty)
        {
            if (property == ReportPopupRequestsProperty && ReportPopupRequests.Count == 0) ClearReportOverlayState();
            _reportPopups.Receive(ReportPopupRequests, ReportPopupsEnabled, ReportPopupSeconds, ReportPopupsShowKillmails);
        }
        UpdateReportOverlayTimer();
    }

    private void ClearReportOverlayState()
    {
        _reportPopups.Clear();
        _nodeOverlayScroll.Clear();
        _openOverlayWidths.Clear();
        _automaticOverlayWidths.Clear();
        _lastOverlayLayouts.Clear();
        _overlayAnchors.Clear();
        _messageScroll.Clear();
        _messageLastFrame.Clear();
        _overlayDrag = null;
        _nodeOverlayViews.Clear();
        _overlayScrollBars.Clear();
        _messageHits.Clear();
        _hasVisibleMarquee = false;
        _reportAgeTimer.Stop();
    }

    internal void UpdateReportOverlayHover(Point point)
    {
        _lastPointerPosition = point;
        _hoveredMessageReportId = _messageHits.LastOrDefault(hit => hit.Bounds.Contains(point))?.ReportId;
        var view = _nodeOverlayViews.LastOrDefault(view => view.Bounds.Contains(point));
        var previousRaised = _raisedPopupSystem;
        _raisedPopupSystem = view?.Automatic == true ? view.SystemName : null;
        var dragging = _overlayDrag is null ? null : _nodeOverlayViews.LastOrDefault(v => v.NodeId == _overlayDrag.Bar.NodeId);
        _reportPopups.SetHovered((dragging ?? view)?.SystemName);
        if (view is not null || _messageHits.Count > 0 || previousRaised != _raisedPopupSystem) InvalidateInteractive();
        UpdateReportOverlayTimer();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _lastPointerPosition = new Point(-1, -1);
        _hoveredMessageReportId = null;
        _raisedPopupSystem = null;
        if (_overlayDrag is null) _reportPopups.SetHovered(null);
        InvalidateInteractive();
        UpdateReportOverlayTimer();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndOverlayDrag(_lastPointerPosition);
    }

    internal void DrawReportOverlays(DrawingContext context)
    {
        _nodeOverlayViews.Clear();
        _intelOverlayLinks.Clear();
        _overlayScrollBars.Clear();
        _messageHits.Clear();
        _hasVisibleMarquee = false;
        if (Graph is not null && ViewMode != MapViewMode.UniverseRegions) DrawNodeOverlays(context);
        else UpdateReportOverlayTimer();
    }

    private void DrawNodeOverlays(DrawingContext context)
    {
        _overlayFrame++;
        var now = DateTimeOffset.UtcNow;
        _overlayFrameElapsed = now - _lastOverlayFrame;
        _lastOverlayFrame = now;
        _hasVisibleMarquee = false;
        _overlayScrollBars.Clear();
        _messageHits.Clear();
        var manual = new List<(MapNode Node, Point Anchor, MapOverlayLayout Layout, Rect Bounds)>();
        foreach (var id in new[] { SelectedNodeId, _hoveredNodeId }.Where(id => id is not null).Distinct())
        {
            if (!_nodeById.TryGetValue(id!.Value, out var node) || !_nodeIndexById.TryGetValue(node.Id, out var index)) continue;
            var layout = BuildOpenNodeOverlay(node);
            var anchor = _screenPositions[index];
            manual.Add((node, anchor, layout, MapOverlayLayout.Place(anchor,
                new Size(layout.Size.Width, Math.Min(480, layout.Size.Height)), Bounds.Size)));
        }
        var automatic = new List<(MapNode Node, Point Anchor, MapOverlayLayout Layout, long Sequence)>();
        foreach (var popup in _reportPopups.Active.Where(_ => ViewMode != MapViewMode.Universe || ReportPopupsShowInUniverse).OrderByDescending(p => p.LatestArrivalSequence).ThenBy(p => p.SystemName, StringComparer.Ordinal))
        {
            var node = Graph?.Nodes.FirstOrDefault(n => string.Equals(n.Name, popup.SystemName, StringComparison.OrdinalIgnoreCase));
            if (node is null || !_nodeIndexById.TryGetValue(node.Id, out var index)) continue;
            var anchor = _screenPositions[index];
            if (!new Rect(Bounds.Size).Contains(anchor)) continue;
            var layout = BuildNodeOverlay(node, Bounds.Width - 16, popup.ReportIds, _automaticOverlayWidths.GetValueOrDefault(node.Id));
            _automaticOverlayWidths[node.Id] = Math.Max(_automaticOverlayWidths.GetValueOrDefault(node.Id), layout.Size.Width);
            if (layout.Reports.Count > 0) automatic.Add((node, anchor, layout, popup.LatestArrivalSequence));
        }
        var placed = new List<(MapNode Node, Point Anchor, MapOverlayLayout Layout, Rect Bounds, long Sequence)>();
        foreach (var entry in automatic)
        {
            var size = new Size(entry.Layout.Size.Width, Math.Min(320, entry.Layout.Size.Height));
            var rect = MapOverlayLayout.Place(entry.Anchor, size, Bounds.Size);
            placed.Add((entry.Node, entry.Anchor, entry.Layout, rect, entry.Sequence));
        }
        // Connectors sit below every card; drawing and hit regions share the paint order.
        foreach (var entry in placed.Where(p => !manual.Any(m => m.Node.Id == p.Node.Id)))
            context.DrawLine(new Pen(OverlayMutedBrush, 0.8), entry.Anchor,
                new Point(Math.Clamp(entry.Anchor.X, entry.Bounds.Left, entry.Bounds.Right),
                    Math.Clamp(entry.Anchor.Y, entry.Bounds.Top, entry.Bounds.Bottom)));
        foreach (var entry in placed.Where(p => !manual.Any(m => m.Node.Id == p.Node.Id)).OrderBy(p => p.Node.Name == _raisedPopupSystem)
                     .ThenBy(p => p.Sequence).ThenBy(p => p.Node.Id))
            DrawNodeOverlay(context, entry.Node, entry.Layout, entry.Bounds, true);
        foreach (var entry in manual) DrawNodeOverlay(context, entry.Node, entry.Layout, entry.Bounds, false);
        var activeSystems = _reportPopups.Active.Select(p => p.SystemName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeIds = _nodeOverlayViews.Select(v => v.NodeId)
            .Concat(Graph?.Nodes.Where(n => activeSystems.Contains(n.Name)).Select(n => n.Id) ?? []).ToHashSet();
        foreach (var id in _nodeOverlayScroll.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _nodeOverlayScroll.Remove(id);
            _openOverlayWidths.Remove(id);
            _automaticOverlayWidths.Remove(id);
            _lastOverlayLayouts.Remove(id);
            _overlayAnchors.Remove(id);
        }
        var reportIds = _nodeOverlayViews.SelectMany(v => v.Layout.Reports.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _messageScroll.Keys.Where(id => !reportIds.Contains(id)).ToArray())
        {
            _messageScroll.Remove(id);
            _messageLastFrame.Remove(id);
        }
        var hovered = _overlayDrag is { } drag
            ? _nodeOverlayViews.LastOrDefault(v => v.NodeId == drag.Bar.NodeId)
            : _nodeOverlayViews.LastOrDefault(v => v.Bounds.Contains(_lastPointerPosition));
        _reportPopups.SetHovered(hovered?.SystemName);
        UpdateReportOverlayTimer();
    }

    private void DrawNodeOverlay(DrawingContext context, MapNode node, MapOverlayLayout layout, Rect rect, bool automatic)
    {
        if (rect.Width <= 12 || rect.Height <= 12) return;
        // Keep each card's clip/transform state isolated on scaled bitmap targets too.
        using var overlayTransform = context.PushTransform(Matrix.Identity);
        var maximum = Math.Max(0, layout.Size.Height - rect.Height);
        var scroll = _nodeOverlayScroll.GetValueOrDefault(node.Id);
        if (scroll > 0 && _lastOverlayLayouts.GetValueOrDefault(node.Id) != layout
            && _overlayAnchors.TryGetValue(node.Id, out var anchor) && layout.Reports.TryGetValue(anchor.ReportId, out var report))
            scroll = report.Top + anchor.Distance;
        scroll = Math.Clamp(scroll, 0, maximum);
        _nodeOverlayScroll[node.Id] = scroll;
        _lastOverlayLayouts[node.Id] = layout;
        var view = new NodeOverlayView(node.Id, node.Name, rect, maximum, layout, scroll, automatic);
        _nodeOverlayViews.Add(view);
        context.FillRectangle(HoverOverlayBackgroundBrush, rect, 4);
        context.DrawRectangle(HoverOverlayBorderPen, rect, 4);
        var clip = rect.Deflate(new Thickness(MapOverlayLayout.Padding));
        string? tooltip = null;
        using (context.PushClip(clip))
        {
            foreach (var element in layout.Elements)
            {
                var bounds = element.Bounds.Translate(new Vector(rect.X, rect.Y - scroll));
                var visible = bounds.Intersect(clip);
                if (visible.Width <= 0 || visible.Height <= 0) continue;
                if (element.MarqueeText is { } text && element.ReportId is { } id)
                {
                    var maxOffset = Math.Max(0, text.Width - bounds.Width);
                    if (!_messageScroll.TryGetValue(id, out var state)) _messageScroll[id] = state = new MapMessageScrollState();
                    var hovering = bounds.Contains(_lastPointerPosition);
                    var previous = _messageLastFrame.GetValueOrDefault(id, -1);
                    state.Advance(previous == _overlayFrame - 1 ? _overlayFrameElapsed : TimeSpan.Zero, maxOffset,
                        hovering || id == _hoveredMessageReportId || _overlayDrag?.Bar.ReportId == id);
                    _messageLastFrame[id] = _overlayFrame;
                    // Message rows span the full content width. The card clip already
                    // constrains their single line, including partially visible rows.
                    context.DrawText(text, new Point(bounds.X - state.Offset, bounds.Y));
                    if (maxOffset > 0)
                    {
                        _hasVisibleMarquee = true;
                        _messageHits.Add(new(node.Id, id, visible, maxOffset));
                        if (hovering)
                        {
                            var track = new Rect(bounds.X, bounds.Bottom - 3, bounds.Width, 3).Intersect(clip);
                            var thumbWidth = Math.Max(16, bounds.Width * bounds.Width / text.Width);
                            var thumb = new Rect(track.X + (track.Width - thumbWidth) * state.Offset / maxOffset, track.Y, thumbWidth, 3);
                            context.FillRectangle(new SolidColorBrush(Color.Parse("#39465A")), track, 1);
                            context.FillRectangle(OverlayMutedBrush, thumb, 1);
                            _overlayScrollBars.Add(new(node.Id, id, track, thumb, maxOffset));
                        }
                    }
                }
                else element.Draw(context, bounds);
                if (element.Url is not null) _intelOverlayLinks.Add((visible, element.Url, node.Id));
                if (visible.Contains(_lastPointerPosition)) tooltip = element.Tooltip;
            }
        }
        if (maximum > 0)
        {
            var track = new Rect(rect.Right - MapOverlayLayout.Padding, rect.Top + MapOverlayLayout.Padding, 3, rect.Height - MapOverlayLayout.Padding * 2);
            var height = Math.Max(12, track.Height * rect.Height / layout.Size.Height);
            var thumb = new Rect(track.X, track.Y + (track.Height - height) * scroll / maximum, 3, height);
            context.FillRectangle(OverlayMutedBrush, thumb, 1);
            _overlayScrollBars.Add(new(node.Id, null, track, thumb, maximum));
        }
        if (!string.IsNullOrWhiteSpace(tooltip)) DrawCompactTooltip(context, _lastPointerPosition, tooltip);
        if (automatic)
        {
            var close = PopupCloseBounds(rect);
            context.FillRectangle(HoverOverlayBackgroundBrush, close, 2);
            var color = close.Contains(_lastPointerPosition) ? OverlayTextBrush : OverlayMutedBrush;
            var pen = new Pen(color, 1.2);
            context.DrawLine(pen, close.TopLeft + new Vector(6, 6), close.BottomRight - new Vector(6, 6));
            context.DrawLine(pen, close.TopRight + new Vector(-6, 6), close.BottomLeft + new Vector(6, -6));
        }
    }

    internal static Rect PopupCloseBounds(Rect card) => new(card.Right - 24, card.Top + 3, 20, 20);

    internal bool DismissReportPopupAt(Point point)
    {
        var view = _nodeOverlayViews.LastOrDefault(v => v.Bounds.Contains(point));
        if (view is null || !view.Automatic || !PopupCloseBounds(view.Bounds).Contains(point)) return false;
        _reportPopups.Dismiss(view.SystemName);
        _raisedPopupSystem = null;
        _overlayDrag = null;
        InvalidateInteractive();
        UpdateReportOverlayTimer();
        return true;
    }

    private void SetOverlayScroll(long nodeId, double offset, double maximum)
    {
        offset = Math.Clamp(offset, 0, maximum);
        _nodeOverlayScroll[nodeId] = offset;
        var view = _nodeOverlayViews.LastOrDefault(v => v.NodeId == nodeId);
        if (offset <= 0) _overlayAnchors.Remove(nodeId);
        else if (view is not null)
        {
            var report = view.Layout.Reports.OrderBy(r => r.Value.Top).FirstOrDefault(r => r.Value.Bottom > offset + MapOverlayLayout.Padding);
            if (report.Key is not null) _overlayAnchors[nodeId] = (report.Key, offset - report.Value.Top);
        }
        InvalidateInteractive();
    }

    internal bool ScrollNodeOverlay(Point point, double delta)
    {
        var view = _nodeOverlayViews.LastOrDefault(v => v.Bounds.Contains(point));
        if (view is null || view.MaxScroll <= 0) return false;
        SetOverlayScroll(view.NodeId, _nodeOverlayScroll.GetValueOrDefault(view.NodeId) - delta * 36, view.MaxScroll);
        return true;
    }

    internal bool HandleOverlayWheel(Point point, double x, double y, bool shift)
    {
        var view = _nodeOverlayViews.LastOrDefault(v => v.Bounds.Contains(point));
        if (view is null) return false;
        var message = _messageHits.LastOrDefault(m => m.NodeId == view.NodeId && m.Bounds.Contains(point));
        if (message is not null && (shift || Math.Abs(x) > 0))
            _messageScroll[message.ReportId].Seek(_messageScroll[message.ReportId].Offset - (shift ? y : x) * 36, message.Maximum);
        else ScrollNodeOverlay(point, y);
        UpdateReportOverlayHover(point);
        InvalidateInteractive();
        return true;
    }

    internal bool BeginOverlayDrag(Point point)
    {
        var view = _nodeOverlayViews.LastOrDefault(v => v.Bounds.Contains(point));
        var bar = _overlayScrollBars.LastOrDefault(b => b.NodeId == view?.NodeId && b.Track.Inflate(3).Contains(point));
        if (bar is null) return false;
        var current = bar.ReportId is null ? _nodeOverlayScroll.GetValueOrDefault(bar.NodeId) : _messageScroll[bar.ReportId].Offset;
        if (!bar.Thumb.Contains(point))
        {
            var coordinate = bar.ReportId is null ? point.Y - bar.Track.Top - bar.Thumb.Height / 2 : point.X - bar.Track.Left - bar.Thumb.Width / 2;
            var length = bar.ReportId is null ? bar.Track.Height - bar.Thumb.Height : bar.Track.Width - bar.Thumb.Width;
            current = Math.Clamp(coordinate / Math.Max(1, length) * bar.Maximum, 0, bar.Maximum);
        }
        _overlayDrag = new(bar, point, current);
        DragOverlayScroll(point);
        return true;
    }

    internal bool DragOverlayScroll(Point point)
    {
        if (_overlayDrag is not { } drag) return false;
        var vertical = drag.Bar.ReportId is null;
        var delta = vertical ? point.Y - drag.Start.Y : point.X - drag.Start.X;
        var length = vertical ? drag.Bar.Track.Height - drag.Bar.Thumb.Height : drag.Bar.Track.Width - drag.Bar.Thumb.Width;
        var value = drag.StartOffset + delta / Math.Max(1, length) * drag.Bar.Maximum;
        if (drag.Bar.ReportId is { } id) _messageScroll[id].Seek(value, drag.Bar.Maximum);
        else SetOverlayScroll(drag.Bar.NodeId, value, drag.Bar.Maximum);
        InvalidateInteractive();
        return true;
    }

    internal bool EndOverlayDrag(Point point)
    {
        if (_overlayDrag is null) return false;
        _overlayDrag = null;
        UpdateReportOverlayHover(point);
        return true;
    }

    internal string? GetNodeOverlayLink(Point point)
    {
        var view = _nodeOverlayViews.LastOrDefault(v => v.Bounds.Contains(point));
        return view is null ? null : _intelOverlayLinks.LastOrDefault(link => link.NodeId == view.NodeId && link.Bounds.Contains(point)).Url;
    }
}
