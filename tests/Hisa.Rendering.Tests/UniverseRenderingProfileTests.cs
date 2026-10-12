using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;
using Xunit.Abstractions;

namespace Hisa.Rendering.Tests;

public sealed class UniverseRenderingProfileTests(ITestOutputHelper output)
{
    // Run with HISA_PROFILE_UNIVERSE=1. The node and link counts match the bundled SDE.
    [AvaloniaFact]
    [Trait("Category", "ManualPerformance")]
    public void ProfileFullSizeUniverse()
    {
        if (Environment.GetEnvironmentVariable("HISA_PROFILE_UNIVERSE") != "1")
        {
            return;
        }

        var graph = CreateFullSizeGraph();
        foreach (var scaling in new[] { 1.0, 2.0 })
        foreach (var zoom in new[] { 0.4, 12.0 })
        {
            var map = new MapControl
            {
                Graph = graph,
                ViewMode = MapViewMode.Universe,
                NodeBackgroundColorMode = MapNodeColorMode.None,
                EnableLinkAnimations = true,
                EnableIntelRingAnimations = true,
                IntelHostileScoresByNodeId = graph.Nodes
                    .Where((_, index) => index % 17 == 0)
                    .ToDictionary(node => node.Id, _ => 3),
                ShowIndicatorSecurityStatus = true,
                SelectedNodeId = 1,
                UniverseRenderScalingOverride = scaling
            };
            map.Measure(new Size(1280, 720));
            map.Arrange(new Rect(0, 0, 1280, 720));
            map.ZoomBy(zoom);
            using var target = new RenderTargetBitmap(
                new PixelSize((int)(1280 * scaling), (int)(720 * scaling)),
                new Vector(96 * scaling, 96 * scaling));
            void Draw()
            {
                using var context = target.CreateDrawingContext(true);
                map.Render(context);
            }

            var watch = Stopwatch.StartNew();
            Draw();
            var firstFrame = watch.ElapsedMilliseconds;
            Thread.Sleep(180);
            watch.Restart();
            for (var i = 0; i < 100 && map.UniverseBakeTimerActive; i++)
            {
                map.OnUniverseIdleBakeTick();
            }
            var tileBuild = watch.ElapsedMilliseconds;
            var builtTiles = map.UniverseCachedTileCount;
            var cachedMiB = map.UniverseCachedBitmapBytes / (1024.0 * 1024.0);

            watch.Restart();
            var panFrames = new List<long>();
            for (var i = 0; i < 60; i++)
            {
                var frameStart = watch.ElapsedMilliseconds;
                map.PanBy(20, 5);
                Draw();
                if (map.UniverseBakeTimerActive)
                {
                    map.OnUniverseIdleBakeTick();
                }
                panFrames.Add(watch.ElapsedMilliseconds - frameStart);
            }
            var longPan = watch.ElapsedMilliseconds;
            panFrames.Sort();
            var panP95 = panFrames[(int)Math.Floor((panFrames.Count - 1) * 0.95)];
            var panFallbacks = map.UniverseBaseFallbackCount;

            watch.Restart();
            for (var i = 0; i < 10; i++)
            {
                map.ZoomBy(1.1);
                Draw();
            }
            var continuousZoom = watch.ElapsedMilliseconds;

            Thread.Sleep(180);
            for (var i = 0; i < 100 && map.UniverseBakeTimerActive; i++)
            {
                map.OnUniverseIdleBakeTick();
            }
            watch.Restart();
            var buildsBeforeIdle = map.UniverseTileBuildCount;
            for (var i = 0; i < 30; i++)
            {
                Draw();
            }
            var animatedFrames = watch.ElapsedMilliseconds;
            output.WriteLine(
                $"scale={scaling} zoom={zoom}: first={firstFrame}ms, " +
                $"tiles={builtTiles}/{tileBuild}ms/{cachedMiB:0.0}MiB, pan60={longPan}ms/p95={panP95}ms, " +
                $"zoom10={continuousZoom}ms, overlay30={animatedFrames}ms, " +
                $"pan fallback tiles={panFallbacks}, idle tile builds={map.UniverseTileBuildCount - buildsBeforeIdle}, " +
                $"timer active={map.UniverseBakeTimerActive}");
        }
    }

    private static MapGraph CreateFullSizeGraph()
    {
        const int nodeCount = 8490;
        const int linkCount = 13970;
        const int columns = 100;
        const int rows = 85;
        var random = new Random(34931);
        var nodes = new List<MapNode>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new MapNode
            {
                Id = i + 1,
                Name = $"System {i + 1}",
                X = ((i % columns) + random.NextDouble() * 0.3) / columns,
                Y = ((i / columns) + random.NextDouble() * 0.3) / rows,
                Security = random.NextDouble(),
                RegionId = i % 70,
                ConstellationId = i % 650
            });
        }

        var links = new List<MapLink>(linkCount);
        for (var i = 0; i < nodeCount; i++)
        {
            if (i % columns < columns - 1 && i + 1 < nodeCount)
            {
                links.Add(new MapLink { FromId = i + 1, ToId = i + 2 });
            }
        }
        for (var i = 0; i + columns < nodeCount && links.Count < linkCount; i++)
        {
            links.Add(new MapLink { FromId = i + 1, ToId = i + columns + 1 });
        }

        return new MapGraph { Nodes = nodes, Links = links };
    }
}
