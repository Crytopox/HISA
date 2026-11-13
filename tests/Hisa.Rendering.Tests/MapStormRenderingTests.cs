using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;
using SkiaSharp;

namespace Hisa.Rendering.Tests;

public sealed class MapStormRenderingTests
{
    [AvaloniaFact]
    public void IndicatorsReserveOneSlotPerStormAndInfoBoxUsesTheSameOrder()
    {
        var map = new MapControl();
        var center = Effect(1, StormType.Electrical, StormStrength.Center);
        var gamma = Effect(4, StormType.Gamma, StormStrength.Strong);
        var first = Effect(2, StormType.Electrical, StormStrength.Weak);
        var second = Effect(3, StormType.Electrical, StormStrength.Weak);
        var clear = Node(1, .4);
        var single = Node(1, .4, center);
        var overlap = Node(1, .4, second, gamma, first, center);

        Assert.Empty(MapControl.GetOrderedStormEffects(clear));
        Assert.Equal(new[] { center, gamma, first, second }, MapControl.GetOrderedStormEffects(overlap));
        var clearSize = map.MeasureIndicatorLabelSize(clear);
        var singleSize = map.MeasureIndicatorLabelSize(single);
        var overlapSize = map.MeasureIndicatorLabelSize(overlap);
        Assert.True(singleSize.Height > clearSize.Height);
        Assert.Equal(singleSize.Height, overlapSize.Height);
        Assert.Equal(3 * 19, overlapSize.Width - singleSize.Width);
        Assert.Empty(StormTooltips(map, clear));
        Assert.Equal(new[] { "Center Electrical storm" }, StormTooltips(map, single));
        var overlapTooltips = new[]
        {
            "Center Electrical storm", "Strong Gamma storm", "Weak Electrical storm", "Weak Electrical storm"
        };
        Assert.Equal(overlapTooltips, StormTooltips(map, overlap));

        map.ShowIndicatorStormIcon = false;
        Assert.Equal(clearSize, map.MeasureIndicatorLabelSize(overlap));
        Assert.Equal(overlapTooltips, StormTooltips(map, overlap));
        map.InfoBoxShowStormIcon = false;
        Assert.Empty(StormTooltips(map, overlap));
        map.ShowIndicatorStormIcon = true;
        Assert.Equal(overlapSize, map.MeasureIndicatorLabelSize(overlap));
    }

    [AvaloniaTheory]
    [InlineData(MapViewMode.Region)]
    [InlineData(MapViewMode.Universe)]
    public void BackgroundBandsStayInTheirCellAndRefreshWithTheGraph(MapViewMode view)
    {
        var center = Effect(1, StormType.Electrical, StormStrength.Center);
        var weak = Effect(3, StormType.Plasma, StormStrength.Weak);
        var overlap = Node(1, .4, weak, center);
        var neighbor = Node(2, .6);
        var map = new MapControl
        {
            Graph = Graph(overlap, neighbor),
            ViewMode = view,
            NodeBackgroundColorMode = MapNodeColorMode.Storms,
            NodeColorMode = MapNodeColorMode.Storms,
            StretchToWindow = true
        };
        map.Measure(new Size(400, 300));
        map.Arrange(new Rect(0, 0, 400, 300));

        SKColor Pixel(SKBitmap image, double x, double y)
        {
            var point = map.WorldToScreen(new Point(x, y));
            return image.GetPixel((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }

        // Wait for the Voronoi geometry on a cold cache before sampling the fills.
        Assert.True(SpinWait.SpinUntil(() =>
        {
            Dispatcher.UIThread.RunJobs();
            using var frame = Render(map);
            var pixel = Pixel(frame, .35, .46);
            return pixel.Blue > pixel.Red + 30;
        }, TimeSpan.FromSeconds(5)), "The electrical band never became visible.");

        using var bands = RenderSettled(map);
        var blue = Pixel(bands, .35, .46);
        var red = Pixel(bands, .45, .46);
        var background = Pixel(bands, .54, .46);
        Assert.True(blue.Blue > blue.Red + 30, $"Expected the electrical band, got {blue}.");
        Assert.True(red.Red > red.Blue + 10, $"Expected the plasma band, got {red}.");
        Assert.Equal(new SKColor(0x0D, 0x13, 0x1D), background);
        Assert.Equal(new SKColor(0x4A, 0xA8, 0xFF), Pixel(bands, .4, .5));

        var single = Node(1, .4, center);
        map.Graph = Graph(single, neighbor);
        if (view == MapViewMode.Universe) Assert.Equal(0, map.UniverseCachedTileCount);
        using var solid = RenderSettled(map);
        var solidBlue = Pixel(solid, .35, .46);
        AssertSameFill(blue, solidBlue);
        Assert.Equal(solidBlue, Pixel(solid, .45, .46));
        Assert.Equal(background, Pixel(solid, .54, .46));
        Assert.Equal(new[] { "Center Electrical storm" }, StormTooltips(map, single));

        var sameTypeOverlap = Node(1, .4, weak, Effect(4, StormType.Plasma, StormStrength.Weak));
        map.Graph = Graph(sameTypeOverlap, neighbor);
        using var sameColorBands = RenderSettled(map);
        AssertSameFill(red, Pixel(sameColorBands, .35, .46));
        AssertSameFill(red, Pixel(sameColorBands, .45, .46));
        Assert.Equal(new[] { "Weak Plasma storm", "Weak Plasma storm" }, StormTooltips(map, sameTypeOverlap));
    }

    private static void AssertSameFill(SKColor expected, SKColor actual)
    {
        // Skia can round gradient and solid brush compositing differently.
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 1);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 1);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 1);
    }

    private static StormEffect Effect(long center, StormType type, StormStrength strength) =>
        new() { CenterSolarSystemId = center, Type = type, Strength = strength };

    private static MapNode Node(long id, double x, params StormEffect[] effects) =>
        new() { Id = id, Name = "A", X = x, Y = .5, StormEffects = effects };

    private static MapGraph Graph(MapNode first, MapNode second) => new()
    {
        Nodes = [first, second],
        Links = [new MapLink { FromId = first.Id, ToId = second.Id }]
    };

    private static string?[] StormTooltips(MapControl map, MapNode node) =>
        map.BuildNodeOverlay(node, 360).Elements.Select(element => element.Tooltip)
            .Where(tooltip => tooltip?.EndsWith(" storm", StringComparison.Ordinal) == true)
            .ToArray();

    private static SKBitmap RenderSettled(MapControl map)
    {
        if (map.ViewMode == MapViewMode.Universe)
        {
            using (Render(map)) { }
            Thread.Sleep(180);
            map.OnUniverseIdleBakeTick();
            Assert.True(map.UniverseCachedTileCount > 0);
        }

        return Render(map);
    }

    private static SKBitmap Render(MapControl map)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 300), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext(true))
            map.Render(context);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
