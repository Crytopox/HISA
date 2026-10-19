using System.Collections.Frozen;
using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Hisa.Core.Models;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Rendering.Tests;

public sealed partial class HistoricalMapRenderingTests
{
    [AvaloniaFact]
    public void CombatMarkersRemainSeparateFromLiveMap_PreserveViewportAndReuseUniverseCaches()
    {
        var graph = Graph(6000); var map = Map(graph, MapViewMode.Universe);
        map.HistoricalActivity = Snapshot(6000);
        map.SelectedNodeId = 1; map.PanBy(4, -3); var viewport = map.GetViewportState();
        using (Render(map)) { }
        Thread.Sleep(180); map.OnUniverseIdleBakeTick();
        for (var i = 0; i < 40 && map.UniverseBakeTimerActive; i++) map.OnUniverseIdleBakeTick();
        var timer = Stopwatch.StartNew();
        var snapshot = new CombatMapSnapshot(Range, Enumerable.Range(1, 6000).ToFrozenDictionary(id => (long)id, id => (byte)(id % 3 == 0 ? 6 : id % 2 + 1)), false);
        var preparation = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); map.CombatAnalysis = snapshot; var assignment = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        using (Render(map)) { }
        var fallbackMs = timer.Elapsed.TotalMilliseconds;
        Thread.Sleep(180); timer.Restart(); map.OnUniverseIdleBakeTick();
        for (var i = 0; i < 40 && map.UniverseBakeTimerActive; i++) map.OnUniverseIdleBakeTick();
        var bakeMs = timer.Elapsed.TotalMilliseconds;
        var builds = map.UniverseTileBuildCount; var revision = map.UniverseSceneRevision;
        for (var frame = 0; frame < 10; frame++) using (Render(map)) { }
        var before = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
        for (var frame = 0; frame < 20; frame++) using (Render(map)) { }
        var frameMs = timer.Elapsed.TotalMilliseconds / 20;
        var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
        Assert.Equal(builds, map.UniverseTileBuildCount); Assert.Equal(revision, map.UniverseSceneRevision);
        map.CombatAnalysis = null;
        Assert.Equal(viewport.Zoom, map.GetViewportState().Zoom); Assert.Equal(viewport.PanOffsetX, map.GetViewportState().PanOffsetX);
        Assert.Equal(viewport.PanOffsetY, map.GetViewportState().PanOffsetY); Assert.Equal(1, map.SelectedNodeId); Assert.Same(graph, map.Graph);
        output.WriteLine($"6000 combat-marked systems / 11840 links: snapshot={preparation:F2} ms; UI assignment={assignment:F3} ms; first uncached frame={fallbackMs:F2} ms; tile bake work={bakeMs:F2} ms (excludes idle scheduling delay); headless warm Universe frame={frameMs:F2} ms; allocated frame including test bitmap/context={allocated:N0} bytes. Snapshot rebakes once; cached scene revision and tile builds then remain unchanged.");
    }
}
