using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;

namespace Hisa.Rendering.Tests;

public sealed class ProbeFormationControlTests
{
    [AvaloniaFact]
    public void FreeGroupDragUsesViewPlaneAndEscapeCancelsGesture()
    {
        var f = new ProbeFormation(Guid.NewGuid(),"group",[new(new(-1000,800,0),100),new(new(1000,-800,0),200),new(new(2500,1500,0),300)]);
        var control = new ProbeFormationControl {Formation=f,SelectedIndices=[0,1],SelectedProbe=0};
        var window = new Window {Width=800,Height=600,Content=control}; var events = new List<ProbeGroupDragEventArgs>();
        control.GroupDrag += (_,e) => {events.Add(e); if (e.Phase == ProbeDragPhase.Updated) control.Formation=ProbeFormationMath.TransformSelection(f,e.Indices.ToArray(),s => ProbeFormationMath.Translate(s,e.Delta)); if (e.Phase == ProbeDragPhase.Canceled) control.Formation=f;};
        try {
            window.Show(); control.Camera.View("Front"); control.Camera.Distance=10000;
            using (var image = new RenderTargetBitmap(new(800,600))) image.Render(control);
            var p = control.Camera.Project(f.Probes[0].Position,control.Bounds.Size)!.Value;
            window.MouseDown(p,MouseButton.Left); Assert.Equal(new[]{0,1},control.SelectedIndices);
            window.MouseMove(p+new Vector(30,20)); Assert.NotEqual(f.Probes[0].Position,control.Formation.Probes[0].Position);
            Assert.Equal(f.Probes[1].Position-f.Probes[0].Position,control.Formation.Probes[1].Position-control.Formation.Probes[0].Position);
            Assert.Equal(f.Probes[2],control.Formation.Probes[2]); Assert.All(events.Where(e => e.Phase==ProbeDragPhase.Updated),e => Assert.Equal(0,e.Delta.Z));
            window.MouseMove(p); Assert.Equal(f.Probes,control.Formation.Probes);
            window.MouseMove(p+new Vector(30,20));
            window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); window.MouseUp(p+new Vector(30,20),MouseButton.Left);
            Assert.Single(events,e => e.Phase==ProbeDragPhase.Canceled); Assert.DoesNotContain(events,e => e.Phase==ProbeDragPhase.Completed); Assert.Equal(f.Probes,control.Formation.Probes);
        } finally {window.Close();}
    }
    [AvaloniaFact]
    public void ModifierClicksBoxSelectionAndOverlappingProbePickingAreConsistent()
    {
        var f = new ProbeFormation(Guid.NewGuid(),"selection",[new(new(-1000,0,0),100),new(new(1000,0,0),100),new(new(0,2000,0),100)]);
        var control = new ProbeFormationControl {Formation=f}; var window=new Window {Width=800,Height=600,Content=control};
        try {
            window.Show(); control.Camera.View("Front"); control.Camera.Distance=10000;
            Point P(int i) => control.Camera.Project(f.Probes[i].Position,control.Bounds.Size)!.Value;
            void Click(Point p,RawInputModifiers modifiers=RawInputModifiers.None) {window.MouseDown(p,MouseButton.Left,modifiers);window.MouseUp(p,MouseButton.Left,modifiers);}
            Click(P(0)); Assert.Equal(new[]{0},control.SelectedIndices);
            Click(P(1),RawInputModifiers.Control); Assert.Equal(new[]{0,1},control.SelectedIndices);
            Click(P(0),RawInputModifiers.Control); Assert.Equal(new[]{1},control.SelectedIndices);
            Click(P(2),RawInputModifiers.Shift); Assert.Equal(new[]{1,2},control.SelectedIndices);
            var a=P(0)+new Vector(-20,-20);var b=P(1)+new Vector(20,20);
            window.MouseDown(a,MouseButton.Left);window.MouseMove(b);window.MouseUp(b,MouseButton.Left);Assert.Equal(new[]{0,1},control.SelectedIndices);
            Click(P(2));window.MouseDown(a,MouseButton.Left,RawInputModifiers.Shift);window.MouseMove(b,RawInputModifiers.Shift);window.MouseUp(b,MouseButton.Left,RawInputModifiers.Shift);Assert.Equal(new[]{0,1,2},control.SelectedIndices);
            Click(new(20,20)); Assert.Empty(control.SelectedIndices);
            control.Formation=f with {Probes=[new(new(0,0,-100),100),new(new(0,0,100),100)]};
            Assert.Equal(1,control.HitTestProbe(new(400,300)));
            Assert.Equal(new[]{0,1},control.ProbesInRectangle(new Rect(390,290,20,20)));
        } finally {window.Close();}
    }
    [Fact]
    public void ProjectionAxisMathAndCameraSingularitiesAreStable()
    {
        var camera = new ProbeCamera(); var size = new Size(800, 600);
        camera.View("Front"); Assert.Equal(new Point(400, 300), camera.Project(default, size));
        Assert.False(camera.TryAxisParameter(new(400, 300), size, default, ProbeAxis.Z, out _));
        Assert.True(camera.TryPlanePoint(new(420,300),size,default,camera.Forward,out var plane));Assert.True(plane.X>0);Assert.Equal(0,plane.Z,7);
        Assert.False(camera.TryPlanePoint(new(400,300),size,default,camera.Right,out _));
        Assert.True(camera.TryAxisParameter(new(420, 300), size, default, ProbeAxis.X, out var offset)); Assert.True(offset > 0);
        Assert.Null(camera.Project(camera.Eye - camera.Forward, size));
        camera.View("Top"); Assert.NotNull(camera.Project(default, size));
        camera.Fit(ProbeFormationMath.Create()); Assert.True(double.IsFinite(camera.Distance));
        for (var i = 0; i < 1000; i++) camera.Zoom(100);
        Assert.True(camera.Distance >= 1); Assert.NotNull(camera.Project(default, size));
    }
    [AvaloniaFact]
    public void RangeSphereAndHitTestingRenderAcrossCameraViews()
    {
        var control = new ProbeFormationControl { Formation = ProbeFormationMath.Create(), SelectedIndices = [0,1], SelectedProbe = 0 };
        var window = new Window { Width = 800, Height = 600, Content = control };
        try {
            window.Show(); control.FitToView();
            foreach (var view in new[] { "Front", "Top", "Side", "Perspective" }) {
                control.SetView(view); var screen = control.Camera.Project(control.Formation.Probes[0].Position, control.Bounds.Size)!.Value;
                Assert.InRange(control.HitTestProbe(screen), 0, 7);
                control.ShowRanges = true; control.FitRange();
                using var bitmap = new RenderTargetBitmap(new(800, 600)); bitmap.Render(control);
                Assert.True(control.ShowRanges);
            }
        } finally { window.Close(); }
    }
    [AvaloniaFact]
    public void AxisDragRaisesOneCompletedGestureAndChangesOnlyTheChosenAxis()
    {
        var formation = new ProbeFormation(Guid.NewGuid(), "drag", [new(default, 1000)]);
        var control = new ProbeFormationControl { Formation = formation, SelectedIndices = [0], SelectedProbe = 0 };
        var window = new Window { Width = 800, Height = 600, Content = control };
        var events = new List<ProbeGroupDragEventArgs>();
        control.GroupDrag += (_, e) => { events.Add(e); if (e.Phase == ProbeDragPhase.Updated) control.Formation = formation with { Probes = [new(e.Delta, 1000)] }; };
        try {
            window.Show(); control.Camera.View("Front"); control.Camera.Distance = 10000;
            using (var bitmap = new RenderTargetBitmap(new(800,600))) bitmap.Render(control);
            var start = control.Camera.Project(new(1300,0,0), control.Bounds.Size)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(start + new Vector(30,0)); window.MouseUp(start + new Vector(30,0), MouseButton.Left);
            Assert.Contains(events, e => e.Phase == ProbeDragPhase.Updated && e.Delta.X > 0 && e.Axis == ProbeAxis.X);
            Assert.Single(events, e => e.Phase == ProbeDragPhase.Completed); Assert.All(events, e => { Assert.Equal(0, e.Delta.Y); Assert.Equal(0, e.Delta.Z); });
        } finally { window.Close(); }
    }
}
