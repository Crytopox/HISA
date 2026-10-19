using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.App.Controls;
using Hisa.Core;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Core.Models.Intelligence;
using Hisa.Data.Database;
using Hisa.Rendering.Controls;
using SkiaSharp;
using Xunit.Abstractions;

namespace Hisa.Rendering.Tests;

public sealed class HistoricalMapRenderingTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly ActivityRange Range = new(Epoch.AddHours(-1), Epoch);

    [AvaloniaFact]
    public void SnapshotEnableDisable_PreservesViewportGraphSelectionAndLiveColors_AndInvalidatesCachedBase()
    {
        var graph = Graph(1);
        var map = Map(graph, MapViewMode.Universe);
        map.NodeColorMode = MapNodeColorMode.Security;
        map.NodeBackgroundColorMode = MapNodeColorMode.None;
        map.SelectedNodeId = 1; map.ZoomBy(1.4); map.PanBy(12, -9);
        var viewport = map.GetViewportState();
        var revision = map.UniverseSceneRevision;
        map.HistoricalActivity = Snapshot(1);
        Assert.True(map.UniverseSceneRevision > revision);
        using (var bitmap = Render(map)) { }
        Assert.Same(graph, map.Graph); Assert.Equal(1, map.SelectedNodeId);
        Assert.Equal(viewport.Zoom, map.GetViewportState().Zoom);
        Assert.Equal(viewport.PanOffsetX, map.GetViewportState().PanOffsetX); Assert.Equal(viewport.PanOffsetY, map.GetViewportState().PanOffsetY);
        Assert.Equal(MapNodeColorMode.Security, map.NodeColorMode);
        map.HistoricalActivity = null;
        Assert.Same(graph, map.Graph); Assert.Equal(1, map.SelectedNodeId);
        Assert.Equal(viewport.Zoom, map.GetViewportState().Zoom);
        Assert.Equal(MapNodeColorMode.Security, map.NodeColorMode);
    }

    [AvaloniaFact]
    public void NodeColors_DistinguishObservedZeroAndUnknown_InBothSystemMapPaths()
    {
        foreach (var mode in new[] { MapViewMode.Region, MapViewMode.Universe })
        {
            var map = Map(Graph(1), mode);
            map.HistoricalActivity = Snapshot(0);
            using var unknown = Decode(Render(map));
            var point = map.WorldToScreen(new Point(0.5, 0.5));
            var unavailablePixel = unknown.GetPixel((int)point.X, (int)point.Y);
            map.HistoricalActivity = Snapshot(0, ActivityCoverageState.Observed);
            using var observed = Decode(Render(map));
            var zeroPixel = observed.GetPixel((int)point.X, (int)point.Y);
            Assert.NotEqual(unavailablePixel, zeroPixel);
        }
    }

    [AvaloniaFact]
    public void ThousandsOfSystems_SnapshotUpdatesAndSteadyFramesReuseCaches()
    {
        const int count = 6000;
        var graph = Graph(count);
        var map = Map(graph, MapViewMode.Universe);
        var timer = Stopwatch.StartNew();
        var snapshot = Snapshot(count);
        var preparation = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); map.HistoricalActivity = snapshot;
        var uiUpdate = timer.Elapsed.TotalMilliseconds;
        using (Render(map)) { }
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        for (var i = 0; i < 40 && map.UniverseBakeTimerActive; i++) map.OnUniverseIdleBakeTick();
        Assert.True(map.UniverseCachedBitmapBytes > 0);
        var builds = map.UniverseTileBuildCount;
        var fallbacks = map.UniverseBaseFallbackCount;
        var revision = map.UniverseSceneRevision;
        for (var frame = 0; frame < 20; frame++) using (Render(map)) { }
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        for (var frame = 0; frame < 20; frame++) using (Render(map)) { }
        var frameTime = timer.Elapsed.TotalMilliseconds / 20;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(builds, map.UniverseTileBuildCount); Assert.Equal(revision, map.UniverseSceneRevision);
        Assert.Equal(fallbacks, map.UniverseBaseFallbackCount);
        map.HistoricalActivity = null;
        using (Render(map)) { }
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        for (var i = 0; i < 40 && map.UniverseBakeTimerActive; i++) map.OnUniverseIdleBakeTick();
        for (var frame = 0; frame < 20; frame++) using (Render(map)) { }
        var baselineBefore = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        for (var frame = 0; frame < 20; frame++) using (Render(map)) { }
        var baselineTime = timer.Elapsed.TotalMilliseconds / 20;
        var baselineAllocated = (GC.GetAllocatedBytesForCurrentThread() - baselineBefore) / 20;
        map.HistoricalActivity = snapshot;
        map.ViewMode = MapViewMode.Region;
        Assert.Same(snapshot, map.HistoricalActivity);
        using (Render(map)) { }
        output.WriteLine($"Historical map: {count} systems / {graph.Links.Count} links; visual preparation={preparation:F2} ms; UI snapshot assignment={uiUpdate:F2} ms; warm Universe render={frameTime:F2} ms/frame; allocations={allocated / 20} bytes/frame including test bitmap/context. Live baseline={baselineTime:F2} ms/frame, {baselineAllocated} bytes/frame. No DB service is supplied to the renderer.");
    }

    [AvaloniaFact]
    public async Task PanelBindings_DispatchToUiThread_AndProduceReadablePreview()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-history-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var repository = new SqliteIntelligenceHistoryRepository(Path.Combine(directory, "history.db"), TimeSpan.FromSeconds(1));
        await repository.InitializeAsync();
        await repository.InsertBatchAsync(Enumerable.Range(1, 80).Select(id => new KillmailObservation
        {
            KillmailId = id, Source = "R2Z2", SolarSystemId = id % 12 + 1,
            OccurredAtUtc = Range.FromUtc.AddMinutes((id % 12) * 5), ReceivedAtUtc = Epoch,
            Victim = new() { ShipTypeId = 587, CharacterId = 100 }, Attackers = [new() { CharacterId = 200 }],
            TotalValue = id % 4 == 0 ? null : 124000000m
        }).ToArray());
        await repository.StartCollectionSessionAsync("ui-test", Range.FromUtc);
        await repository.HeartbeatCollectionAsync("ui-test", Epoch);
        await using var feature = new HistoricalActivityFeature(repository, new Settings(), async action => await Dispatcher.UIThread.InvokeAsync(action),
            TimeSpan.FromDays(30), clock: new Clock());
        await feature.InitializeAsync();
        var graph = Graph(12);
        feature.SetMapContext(graph, MapViewMode.Universe, 1);
        var uiThread = Environment.CurrentManagedThreadId;
        var notificationThreads = new List<int>();
        feature.PropertyChanged += (_, _) => notificationThreads.Add(Environment.CurrentManagedThreadId);
        feature.IsEnabled = true;
        await feature.PendingMapTask; await feature.PendingDetailTask;
        Assert.All(notificationThreads, id => Assert.Equal(uiThread, id));
        if (!Application.Current!.Styles.OfType<FluentTheme>().Any()) Application.Current.Styles.Add(new FluentTheme());
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        var map = Map(graph, MapViewMode.Universe);
        using var binding = map.Bind(MapControl.HistoricalActivityProperty, new Binding(nameof(feature.Snapshot)) { Source = feature });
        var panel = new HistoricalActivityPanel { DataContext = feature };
        var details = new HistoricalSystemDetails { DataContext = feature, Width = 310, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(12) };
        using var panelVisibility = panel.Bind(Control.IsVisibleProperty, new Binding(nameof(feature.IsEnabled)) { Source = feature });
        using var detailVisibility = details.Bind(Control.IsVisibleProperty, new Binding(nameof(feature.IsEnabled)) { Source = feature });
        var mapGrid = new Grid(); mapGrid.Children.Add(map); mapGrid.Children.Add(details);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Background = new SolidColorBrush(Color.Parse("#0D131D")) };
        root.Children.Add(panel); Grid.SetRow(mapGrid, 1); root.Children.Add(mapGrid);
        var window = new Window { Width = 1200, Height = 800, Content = root, Background = root.Background };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.True(panel.Bounds.Height > 100); Assert.True(panel.Bounds.Height < 360);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("UTC") == true);
            Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("Killmail losses") == true);
            var previewDirectory = Environment.GetEnvironmentVariable("HISA_HISTORICAL_PREVIEW_DIR") ?? Path.Combine(AppContext.BaseDirectory, "historical-map-preview");
            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 800), new Vector(96, 96));
                bitmap.Render(root); bitmap.Save(Path.Combine(previewDirectory, "historical-map-preview.png"));
                output.WriteLine("UI preview: " + Path.Combine(previewDirectory, "historical-map-preview.png"));
            }
            map.SelectedNodeId = 1; map.ZoomBy(1.3); map.PanBy(8, -4);
            var viewport = map.GetViewportState();
            feature.IsEnabled = false; window.UpdateLayout();
            Assert.False(panel.IsVisible); Assert.False(details.IsVisible); Assert.Null(map.HistoricalActivity);
            Assert.Equal(viewport.Zoom, map.GetViewportState().Zoom);
            Assert.Equal(viewport.PanOffsetX, map.GetViewportState().PanOffsetX);
            Assert.Equal(viewport.PanOffsetY, map.GetViewportState().PanOffsetY);
            Assert.Equal(1, map.SelectedNodeId); Assert.Same(graph, map.Graph);
            feature.IsEnabled = true; await feature.PendingMapTask; window.UpdateLayout();
            Assert.True(panel.IsVisible); Assert.Equal(viewport.Zoom, map.GetViewportState().Zoom);
            Assert.Equal(1, map.SelectedNodeId);
        }
        finally { window.Close(); await feature.DisposeAsync(); await repository.DisposeAsync(); }
        Directory.Delete(directory, true);
    }

    private static MapGraph Graph(int count) => new()
    {
        Nodes = count == 1 ? [new() { Id = 1, Name = "Test system", X = 0.5, Y = 0.5 }] :
            Enumerable.Range(1, count).Select(id => new MapNode { Id = id, Name = "System " + id,
                X = count < 50 ? 0.35 + (id % 4) * 0.16 : (id % 100) / 100d,
                Y = count < 50 ? 0.3 + (id / 4) * 0.2 : (id / 100) / 60d }).ToArray(),
        Links = count < 50 ? [] : Enumerable.Range(1, count).SelectMany(id => new[]
            { id % 100 == 0 || id == count ? null : new MapLink { FromId = id, ToId = id + 1 },
              id + 100 > count ? null : new MapLink { FromId = id, ToId = id + 100 } }).OfType<MapLink>().ToArray()
    };
    private static MapControl Map(MapGraph graph, MapViewMode mode)
    {
        var map = new MapControl { Graph = graph, ViewMode = mode, NodeColorMode = MapNodeColorMode.Security, NodeBackgroundColorMode = MapNodeColorMode.None };
        map.Measure(new Size(900, 600)); map.Arrange(new Rect(0, 0, 900, 600)); return map;
    }
    private static HistoricalMapSnapshot Snapshot(int count, ActivityCoverageState coverage = ActivityCoverageState.Unknown)
    {
        var systems = Enumerable.Range(1, count).Select(id => new SystemActivity(id,
            new ActivityMetrics(id % 500 + 1, 1, id % 500, id * 1000000m, 1, 0, id % 900, Range.FromUtc, Epoch.AddMinutes(-5)))).ToArray();
        return HistoricalMapSnapshot.Create(new(Range, Range, systems, [], [], ActivityMetrics.Zero,
            new(coverage, TimeSpan.Zero, 0, false)), HistoricalActivityMetric.Killmails);
    }
    private static RenderTargetBitmap Render(MapControl map)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(900, 600), new Vector(96, 96));
        using var context = bitmap.CreateDrawingContext(true); map.Render(context); return bitmap;
    }
    private static SKBitmap Decode(RenderTargetBitmap bitmap)
    {
        using (bitmap) { using var stream = new MemoryStream(); bitmap.Save(stream); stream.Position = 0; return SKBitmap.Decode(stream); }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Epoch.AddMinutes(2); }
    private sealed class Settings : ISettingsService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
