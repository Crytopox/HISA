using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;
using SkiaSharp;

namespace Hisa.Rendering.Tests;

public sealed class UniverseTileRenderingTests
{
    [AvaloniaFact]
    public void PanReusesTileAndGraphChangeDiscardsIt()
    {
        var map = CreateMap();
        Render(map);
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        Assert.Equal(1, map.UniverseCachedTileCount);

        var builds = map.UniverseTileBuildCount;
        var revision = map.UniverseSceneRevision;

        map.PanBy(12, -8);
        Render(map);
        Assert.Equal(builds, map.UniverseTileBuildCount);
        Assert.Equal(1, map.UniverseCachedTileCount);

        map.Graph = CreateGraph(0.7);
        Assert.Equal(0, map.UniverseCachedTileCount);
        Assert.True(map.UniverseSceneRevision > revision);
        Render(map);
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        Assert.Equal(1, map.UniverseCachedTileCount);

        map.Graph = CreateGraph(0.3);
        Assert.Equal(0, map.UniverseCachedTileCount);
        Render(map);
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        Assert.Equal(1, map.UniverseCachedTileCount);
    }

    [AvaloniaFact]
    public void ZoomKeepsOldTileUntilSettledThenBuildsAtExactZoom()
    {
        var map = CreateMap();
        Render(map);
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        var oldZoom = map.UniverseBakedZoom;

        map.ZoomBy(1.1);
        Render(map);
        Assert.Equal(oldZoom, map.UniverseBakedZoom);

        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        Assert.Equal(oldZoom * 1.1, map.UniverseBakedZoom, 6);
        Assert.True(map.UniverseTileBuildCount >= 2);
    }

    [AvaloniaFact]
    public void ViewModeChangeDropsUniverseTiles()
    {
        var map = CreateMap();
        Render(map);
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        Assert.Equal(1, map.UniverseCachedTileCount);

        map.ViewMode = MapViewMode.Region;
        Assert.Equal(0, map.UniverseCachedTileCount);
    }

    [AvaloniaFact]
    public void OverlayChangesKeepTilesWhileBaseColorsInvalidateThem()
    {
        var map = CreateMap();
        Render(map);
        Thread.Sleep(180);
        map.OnUniverseIdleBakeTick();
        Assert.Equal(1, map.UniverseCachedTileCount);

        map.SelectedNodeId = 1;
        map.IntelHostileScoresByNodeId = new Dictionary<long, int> { [1] = 3 };
        Assert.Equal(1, map.UniverseCachedTileCount);

        map.NodeColorMode = MapNodeColorMode.Region;
        Assert.Equal(0, map.UniverseCachedTileCount);
    }

    [AvaloniaFact]
    public void SettledViewStopsBuildingAndReusesBaseOnLaterFrames()
    {
        var map = CreateMap();
        Render(map);
        Thread.Sleep(180);
        for (var i = 0; i < 32 && map.UniverseBakeTimerActive; i++)
        {
            map.OnUniverseIdleBakeTick();
        }

        Assert.False(map.UniverseBakeTimerActive);
        Assert.True(map.UniverseCachedBitmapBytes <
            map.UniverseCachedTileCount * 520L * 520L * 4L);
        var builds = map.UniverseTileBuildCount;
        var fallbacks = map.UniverseBaseFallbackCount;
        Render(map);
        Render(map);
        Assert.Equal(builds, map.UniverseTileBuildCount);
        Assert.Equal(fallbacks, map.UniverseBaseFallbackCount);
    }

    [AvaloniaFact]
    public void ResizeKeepsNodeOnWorldTransform()
    {
        foreach (var mode in new[] { MapViewMode.Region, MapViewMode.Universe })
        {
            var map = new MapControl
            {
                Graph = CreateGraph(0.5),
                ViewMode = mode,
                NodeBackgroundColorMode = MapNodeColorMode.None,
                StretchToWindow = true
            };
            Arrange(map, 400, 300);
            map.ZoomBy(6);
            Render(map, 400, 300);

            var before = map.WorldToScreen(new Point(0.5, 0.5));
            Arrange(map, 800, 300);
            var after = map.WorldToScreen(new Point(0.5, 0.5));
            using var bitmap = RenderBitmap(map, 800, 300, 1);
            using var decoded = Decode(bitmap);

            Assert.True(Math.Abs(after.X - before.X) > 20, $"{mode} resize did not move the world transform.");
            Assert.True(HasNodePixel(decoded, after), $"{mode} node is missing at {after}.");
            Assert.False(HasNodePixel(decoded, before), $"{mode} node stayed at pre-resize {before}.");
        }
    }

    [Fact]
    public void TileResolutionTracksDisplayScale()
    {
        Assert.Equal(new PixelSize(520, 520), MapControl.GetUniverseTilePixelSize(1));
        Assert.Equal(new PixelSize(1040, 1040), MapControl.GetUniverseTilePixelSize(2));
    }

    [Fact]
    public void EvictionKeepsVisibleTilesAndRemovesOldestOffscreenFirst()
    {
        var entries = new List<((int X, int Y) Key, long Bytes, long LastUse)>
        {
            ((0, 0), 10, 1),
            ((1, 0), 10, 2),
            ((2, 0), 10, 3),
            ((3, 0), 10, 4)
        };
        var visible = new HashSet<(int X, int Y)> { (0, 0), (3, 0) };

        var evicted = MapControl.SelectUniverseTilesForEviction(entries, visible, 25);

        Assert.Equal([(1, 0), (2, 0)], evicted);
    }

    [AvaloniaFact]
    public void NodeOnTileBoundaryRemainsVisibleAtOneAndTwoTimesScale()
    {
        foreach (var scaling in new[] { 1.0, 2.0 })
        {
            var map = new MapControl
            {
                Graph = CreateGraph(0.5),
                ViewMode = MapViewMode.Universe,
                NodeBackgroundColorMode = MapNodeColorMode.None,
                StretchToWindow = true,
                UniverseRenderScalingOverride = scaling
            };
            map.Measure(new Size(1024, 512));
            map.Arrange(new Rect(0, 0, 1024, 512));
            using var first = RenderBitmap(map, 1024, 512, scaling);
            Thread.Sleep(180);
            for (var i = 0; i < 32 && map.UniverseBakeTimerActive; i++)
            {
                map.OnUniverseIdleBakeTick();
            }

            using var cached = RenderBitmap(map, 1024, 512, scaling);
            using var stream = new MemoryStream();
            cached.Save(stream);
            stream.Position = 0;
            using var decoded = SKBitmap.Decode(stream);
            var centerX = (int)(512 * scaling);
            var centerY = (int)(256 * scaling);
            var background = new SKColor(0x0D, 0x13, 0x1D);
            var coloredPixels = 0;
            for (var y = centerY - 8; y <= centerY + 8; y++)
            for (var x = centerX - 8; x <= centerX + 8; x++)
            {
                if (decoded.GetPixel(x, y) != background)
                {
                    coloredPixels++;
                }
            }

            Assert.True(coloredPixels > 0, $"Node vanished at a tile seam at {scaling}x.");
        }
    }

    private static MapControl CreateMap()
    {
        var map = new MapControl
        {
            Graph = CreateGraph(0.3),
            ViewMode = MapViewMode.Universe,
            NodeBackgroundColorMode = MapNodeColorMode.None
        };
        map.Measure(new Size(300, 200));
        map.Arrange(new Rect(0, 0, 300, 200));
        return map;
    }

    private static MapGraph CreateGraph(double x) => new()
    {
        Nodes = [new MapNode { Id = 1, Name = "Test", X = x, Y = 0.5 }],
        Links = []
    };

    private static void Arrange(MapControl map, int width, int height)
    {
        map.Measure(new Size(width, height));
        map.Arrange(new Rect(0, 0, width, height));
    }

    private static void Render(MapControl map)
    {
        Render(map, 300, 200);
    }

    private static void Render(MapControl map, int width, int height)
    {
        using var bitmap = RenderBitmap(map, width, height, 1);
    }

    private static SKBitmap Decode(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static bool HasNodePixel(SKBitmap bitmap, Point screen)
    {
        var node = new SKColor(0x98, 0xA6, 0xB8);
        var cx = (int)Math.Round(screen.X);
        var cy = (int)Math.Round(screen.Y);
        for (var y = cy - 2; y <= cy + 2; y++)
        {
            for (var x = cx - 2; x <= cx + 2; x++)
            {
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
                {
                    continue;
                }

                var pixel = bitmap.GetPixel(x, y);
                var distance = Math.Abs(pixel.Red - node.Red) +
                               Math.Abs(pixel.Green - node.Green) +
                               Math.Abs(pixel.Blue - node.Blue);
                if (distance < 48)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static RenderTargetBitmap RenderBitmap(MapControl map, int width, int height, double scaling)
    {
        var bitmap = new RenderTargetBitmap(
            new PixelSize((int)(width * scaling), (int)(height * scaling)),
            new Vector(96 * scaling, 96 * scaling));
        using var context = bitmap.CreateDrawingContext(true);
        map.Render(context);
        return bitmap;
    }
}
