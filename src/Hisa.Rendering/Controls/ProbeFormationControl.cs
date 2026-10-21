using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.Rendering.Controls;

public sealed class ProbeSelectionEventArgs(IReadOnlyList<int> indices, int primary) : EventArgs
{
    public IReadOnlyList<int> Indices { get; } = indices;
    public int Primary { get; } = primary;
    public bool Cancel { get; set; }
}
public enum ProbeDragPhase { Started, Updated, Completed, Canceled }
public sealed class ProbeGroupDragEventArgs(IReadOnlyList<int> indices, ProbePosition delta, ProbeDragPhase phase, ProbeAxis? axis = null) : EventArgs
{
    public IReadOnlyList<int> Indices { get; } = indices;
    public ProbePosition Delta { get; } = delta;
    public ProbeDragPhase Phase { get; } = phase;
    public ProbeAxis? Axis { get; } = axis;
    public bool Cancel { get; set; }
}

public sealed class ProbeFormationControl : Control
{
    public static readonly StyledProperty<ProbeFormation?> FormationProperty = AvaloniaProperty.Register<ProbeFormationControl, ProbeFormation?>(nameof(Formation));
    public static readonly StyledProperty<int> SelectedProbeProperty = AvaloniaProperty.Register<ProbeFormationControl, int>(nameof(SelectedProbe), -1);
    public static readonly StyledProperty<bool> ShowRangesProperty = AvaloniaProperty.Register<ProbeFormationControl, bool>(nameof(ShowRanges), false);
    public ProbeFormation? Formation { get => GetValue(FormationProperty); set => SetValue(FormationProperty, value); }
    public int SelectedProbe { get => GetValue(SelectedProbeProperty); set => SetValue(SelectedProbeProperty, value); }
    public bool ShowRanges { get => GetValue(ShowRangesProperty); set => SetValue(ShowRangesProperty, value); }
    public static readonly StyledProperty<IReadOnlyList<int>> SelectedIndicesProperty = AvaloniaProperty.Register<ProbeFormationControl, IReadOnlyList<int>>(nameof(SelectedIndices), Array.Empty<int>());
    public IReadOnlyList<int> SelectedIndices { get => GetValue(SelectedIndicesProperty); set => SetValue(SelectedIndicesProperty, value); }
    public ProbeCamera Camera { get; } = new();
    public event EventHandler<ProbeSelectionEventArgs>? SelectionChanged;
    public event EventHandler<ProbeGroupDragEventArgs>? GroupDrag;
    private Point last, press;
    private bool orbit, pan, left, box, started, additive;
    private ProbeAxis? dragAxis;
    private ProbePosition dragOrigin, planeNormal, planeStart;
    private double axisStart;
    private int pressedProbe = -1;
    private int[] gestureIndices = [];
    private Rect? selectionRectangle;
    private readonly List<(ProbeAxis Axis, Point Start, Point End)> handles = [];
    private static readonly IBrush[] AxisBrushes = [Brushes.IndianRed, Brushes.LightGreen, Brushes.DeepSkyBlue];
    static ProbeFormationControl() { AffectsRender<ProbeFormationControl>(FormationProperty, SelectedProbeProperty, ShowRangesProperty, SelectedIndicesProperty); }
    public ProbeFormationControl() { Focusable = true; ClipToBounds = true; }
    public void FitToView() { if (Formation is { } f) Camera.Fit(f); InvalidateVisual(); }
    public void SetView(string view) { Camera.View(view); InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var size = Bounds.Size;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0B111B")), new Rect(size));
        handles.Clear();
        if (Formation is not { } f) { Label(context, "Create or select a formation", new(20, 20), Brushes.LightGray); return; }
        var step = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, Camera.Distance / 8))));
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#253044")), 1);
        for (var i = -10; i <= 10; i++) {
            Line(context, new(i * step, 0, -10 * step), new(i * step, 0, 10 * step), gridPen);
            Line(context, new(-10 * step, 0, i * step), new(10 * step, 0, i * step), gridPen);
        }
        var extent = Math.Max(step * 3, f.Probes.Max(p => p.Position.Length));
        foreach (var axis in Enum.GetValues<ProbeAxis>()) {
            var v = ProbeCamera.AxisVector(axis) * extent;
            Line(context, default, v, new Pen(AxisBrushes[(int)axis], 1));
            if (Camera.Project(v, size) is { } endpoint) Label(context, axis switch { ProbeAxis.X => "+X West", ProbeAxis.Y => "+Y Up", _ => "+Z North" }, endpoint, AxisBrushes[(int)axis]);
        }
        if (Camera.Project(default, size) is { } ship) { context.DrawEllipse(null, new Pen(Brushes.White, 1), ship, 6, 6); Label(context, "Origin", ship + new Vector(8, 5), Brushes.White); }
        if (Camera.Project(f.Centroid, size) is { } centroid) { context.DrawRectangle(null, new Pen(Brushes.Gold, 1), new Rect(centroid - new Vector(4, 4), new Size(8, 8))); Label(context, "Centroid", centroid + new Vector(8, -18), Brushes.Gold); }
        if (ShowRanges) foreach (var index in SelectedIndices.Where(i => i >= 0 && i < f.Probes.Count)) {
            var probe = f.Probes[index]; var rangePen = new Pen(new SolidColorBrush(Color.Parse("#50648CA8")), 1);
            foreach (var axis in Enum.GetValues<ProbeAxis>()) {
                ProbePosition? previous = null;
                for (var i = 0; i <= 96; i++) {
                    var a = i * Math.Tau / 96; var c = Math.Cos(a) * probe.RadiusMeters; var s = Math.Sin(a) * probe.RadiusMeters;
                    var v = probe.Position + (axis switch { ProbeAxis.X => new ProbePosition(0,c,s), ProbeAxis.Y => new(c,0,s), _ => new(c,s,0) });
                    if (previous is { } p) Line(context, p, v, rangePen); previous = v;
                }
            }
        }
        foreach (var item in f.Probes.Select((p,i) => (Probe:p, Index:i)).OrderByDescending(p => (p.Probe.Position - Camera.Eye).Dot(Camera.Forward))) {
            if (Camera.Project(item.Probe.Position, size) is not { } point) continue;
            var selected = SelectedIndices.Contains(item.Index); var brush = selected ? Brushes.Gold : Brushes.DeepSkyBlue;
            context.DrawEllipse(brush, new Pen(Brushes.White, selected ? 2 : 1), point, selected ? 7 : 5, selected ? 7 : 5);
            Label(context, (item.Index + 1).ToString(CultureInfo.InvariantCulture), point + new Vector(9, -9), brush);
        }
        var valid = SelectedIndices.Where(i => i >= 0 && i < f.Probes.Count).ToArray();
        if (valid.Length > 0 && Camera.Project(ProbeFormationMath.SelectionCentroid(f,valid), size) is { } start) {
            foreach (var axis in Enum.GetValues<ProbeAxis>()) {
                var end = Camera.Project(ProbeFormationMath.SelectionCentroid(f,valid) + ProbeCamera.AxisVector(axis) * Camera.Distance * .13, size);
                if (end is not { } endpoint || Distance(endpoint, start) < 12) continue;
                handles.Add((axis, start, endpoint));
                context.DrawLine(new Pen(AxisBrushes[(int)axis], 3), start, endpoint);
                context.DrawEllipse(AxisBrushes[(int)axis], null, endpoint, 4, 4);
                Label(context, axis.ToString(), endpoint + new Vector(4, -12), AxisBrushes[(int)axis]);
            }
        }
        if (selectionRectangle is { } rectangle) context.DrawRectangle(new SolidColorBrush(Color.Parse("#303B97CF")), new Pen(Brushes.DeepSkyBlue,1), rectangle);
        Label(context, "Drag probes: move selection · Empty drag: box select · Right: orbit · Middle: pan", new(12, size.Height - 25), Brushes.SlateGray);
        if (ShowRanges) Label(context, "Selected probe ranges · fit ranges to inspect at AU scale", new(12, 10), Brushes.SlateGray);
    }
    public int HitTestProbe(Point point)
    {
        if (Formation is not { } f) return -1;
        return f.Probes.Select((p,i) => (Index:i, Screen:Camera.Project(p.Position, Bounds.Size), Depth:(p.Position - Camera.Eye).Dot(Camera.Forward)))
            .Where(p => p.Screen is { } s && Distance(s, point) <= 12).OrderBy(p => p.Depth).Select(p => p.Index).DefaultIfEmpty(-1).First();
    }
    public void FitSelection()
    {
        if (Formation is not { } f || SelectedIndices.Count == 0) return;
        Camera.Fit(f with { Probes = SelectedIndices.Select(i => f.Probes[i]).ToArray() }); InvalidateVisual();
    }
    public void FitRange()
    {
        if (Formation is not { } f || SelectedIndices.Count == 0) return;
        Camera.Target = ProbeFormationMath.SelectionCentroid(f, SelectedIndices.ToArray());
        Camera.Distance = Math.Clamp(SelectedIndices.Max(i => (f.Probes[i].Position - Camera.Target).Length + f.Probes[i].RadiusMeters) * 3.6, 1, 1e25);
        InvalidateVisual();
    }
    public IReadOnlyList<int> ProbesInRectangle(Rect rectangle) => Formation is not { } f ? [] :
        f.Probes.Select((p,i) => (Index:i, Point:Camera.Project(p.Position,Bounds.Size)))
            .Where(p => p.Point is { } point && rectangle.Contains(point)).Select(p => p.Index).ToArray();
    private bool Select(IEnumerable<int> indices, int primary = -1)
    {
        var values = indices.Distinct().Order().ToArray(); var args = new ProbeSelectionEventArgs(values,primary);
        SelectionChanged?.Invoke(this,args); if (args.Cancel) return false;
        SetCurrentValue(SelectedIndicesProperty, values);
        SetCurrentValue(SelectedProbeProperty, values.Contains(primary) ? primary : values.FirstOrDefault(-1));
        return true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (left || orbit || pan) CancelGesture();
        base.OnPointerPressed(e); Focus(); press = last = e.GetPosition(this); var properties = e.GetCurrentPoint(this).Properties;
        orbit = properties.IsRightButtonPressed; pan = properties.IsMiddleButtonPressed;
        if (properties.IsLeftButtonPressed && Formation is { } f) {
            var toggle = e.KeyModifiers.HasFlag(KeyModifiers.Control); additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            pressedProbe = HitTestProbe(press);
            if ((toggle || additive) && pressedProbe >= 0) {
                Select(toggle && SelectedIndices.Contains(pressedProbe) ? SelectedIndices.Where(i => i != pressedProbe) : SelectedIndices.Append(pressedProbe), pressedProbe);
                pressedProbe = -1; e.Handled = true; return;
            }
            dragAxis = null;
            if (!toggle && !additive) foreach (var handle in handles.OrderBy(h => Distance(h.End,press))) {
                var line = new Vector(handle.End.X-handle.Start.X,handle.End.Y-handle.Start.Y);
                var t = Math.Clamp(Vector.Dot(new Vector(press.X-handle.Start.X,press.Y-handle.Start.Y),line)/line.SquaredLength,.2,1);
                if (Distance(press,handle.Start+line*t) <= 8) { dragAxis = handle.Axis; break; }
            }
            if (dragAxis is null && pressedProbe >= 0 && !SelectedIndices.Contains(pressedProbe) && !Select([pressedProbe],pressedProbe)) return;
            left = true; box = dragAxis is null && pressedProbe < 0;
            gestureIndices = SelectedIndices.ToArray();
            if (!box && gestureIndices.Length > 0) {
                dragOrigin = ProbeFormationMath.SelectionCentroid(f,gestureIndices); planeNormal = Camera.Forward;
                if (dragAxis is { } axis) { if (!Camera.TryAxisParameter(press,Bounds.Size,dragOrigin,axis,out axisStart)) { left = false; return; } }
                else if (!Camera.TryPlanePoint(press,Bounds.Size,dragOrigin,planeNormal,out planeStart)) { left = false; return; }
            }
        }
        if (orbit || pan || left) e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var point = e.GetPosition(this); var delta = point-last;
        if (left && (started || selectionRectangle is not null || Distance(point,press) >= 4)) {
            if (box) selectionRectangle = new Rect(press,point).Normalize();
            else {
                if (!started) {
                    var begin = new ProbeGroupDragEventArgs(gestureIndices,default,ProbeDragPhase.Started); GroupDrag?.Invoke(this,begin);
                    if (begin.Cancel) { ResetGesture(); e.Pointer.Capture(null); return; } started = true;
                }
                ProbePosition movement;
                if (dragAxis is { } axis) {
                    if (!Camera.TryAxisParameter(point,Bounds.Size,dragOrigin,axis,out var parameter)) return;
                    movement = ProbeCamera.AxisVector(axis)*(parameter-axisStart);
                } else {
                    if (!Camera.TryPlanePoint(point,Bounds.Size,dragOrigin,planeNormal,out var plane)) return;
                    movement = plane-planeStart;
                }
                var change = new ProbeGroupDragEventArgs(gestureIndices,movement,ProbeDragPhase.Updated,dragAxis); GroupDrag?.Invoke(this,change);
                if (change.Cancel) CancelGesture();
            }
        }
        else if (orbit) { Camera.Yaw -= delta.X*.008; Camera.Pitch = Math.Clamp(Camera.Pitch+delta.Y*.008,-Math.PI/2+.0001,Math.PI/2-.0001); }
        else if (pan) Camera.Pan(delta.X,delta.Y,Bounds.Size);
        last = point; if (orbit || pan || left) InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (started) { started = false; GroupDrag?.Invoke(this,new(gestureIndices,default,ProbeDragPhase.Completed)); }
        else if (left && box) {
            if (selectionRectangle is { } rectangle) Select(additive ? gestureIndices.Concat(ProbesInRectangle(rectangle)) : ProbesInRectangle(rectangle));
            else if (!additive) Select([]);
        } else if (left && pressedProbe >= 0 && dragAxis is null) Select([pressedProbe],pressedProbe);
        ResetGesture(); e.Pointer.Capture(null); base.OnPointerReleased(e); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { CancelGesture(); base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelGesture(); e.Handled = true; } base.OnKeyDown(e);
    }
    public void CancelGesture()
    {
        if (started) { started = false; GroupDrag?.Invoke(this,new(gestureIndices,default,ProbeDragPhase.Canceled)); }
        ResetGesture(); InvalidateVisual();
    }
    private void ResetGesture() { left = box = orbit = pan = false; dragAxis = null; pressedProbe = -1; selectionRectangle = null; }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (!left) { Camera.Zoom(e.Delta.Y); InvalidateVisual(); } e.Handled = true; base.OnPointerWheelChanged(e);
    }
    private void Line(DrawingContext c, ProbePosition a, ProbePosition b, IPen pen)
    {
        if (Camera.Project(a, Bounds.Size) is { } pa && Camera.Project(b, Bounds.Size) is { } pb && Math.Abs(pa.X) < 1e8 && Math.Abs(pa.Y) < 1e8 && Math.Abs(pb.X) < 1e8 && Math.Abs(pb.Y) < 1e8) c.DrawLine(pen, pa, pb);
    }
    private static void Label(DrawingContext c, string text, Point point, IBrush brush) => c.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush), point);
    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
