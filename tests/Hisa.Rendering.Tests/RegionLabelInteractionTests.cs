using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;
using SkiaSharp;

namespace Hisa.Rendering.Tests;

public sealed class RegionLabelInteractionTests
{
    [AvaloniaFact]
    public void VisibleUniverseLabelTogglesSelectionOverSystemAtTheSamePoint()
    {
        var map = new MapControl
        {
            ViewMode = MapViewMode.Universe,
            Graph = new MapGraph
            {
                Nodes = [new MapNode { Id = 1, Name = "System", X = 0.5, Y = 0.5, RegionId = 10, RegionName = "Region" }],
                Links = []
            }
        };
        var window = new Window { Width = 800, Height = 600, Content = map };
        try
        {
            window.Show();
            map.SetViewportState(new MapViewportState { Zoom = 1, PanOffsetX = 0, PanOffsetY = 0 });
            using var idle = Render(map);
            var point = map.WorldToScreen(new Point(0.5, 0.5));
            var labelPixel = new PixelPoint((int)point.X, (int)point.Y - 8);
            window.MouseMove(point);
            using var hovered = Render(map);
            Assert.NotEqual(idle.GetPixel(labelPixel.X, labelPixel.Y), hovered.GetPixel(labelPixel.X, labelPixel.Y));

            map.SelectedNodeId = 1;
            Click(window, point);
            Assert.Null(map.SelectedNodeId);
            using var selected = Render(map);

            Click(window, point);
            Assert.Null(map.SelectedNodeId);
            using var deselected = Render(map);
            Assert.NotEqual(selected.GetPixel(labelPixel.X, labelPixel.Y), deselected.GetPixel(labelPixel.X, labelPixel.Y));
            Assert.Equal(hovered.GetPixel(labelPixel.X, labelPixel.Y), deselected.GetPixel(labelPixel.X, labelPixel.Y));

            map.SetViewportState(new MapViewportState { Zoom = 6, PanOffsetX = 0, PanOffsetY = 0 });
            using (Render(map)) { }
            Assert.Equal(point, map.WorldToScreen(new Point(0.5, 0.5)));
            Click(window, point);
            Assert.Equal(1L, map.SelectedNodeId);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RegionsOverviewLabelsCoverAnimatedLinks()
    {
        var map = new MapControl
        {
            ViewMode = MapViewMode.UniverseRegions,
            SuspendAnimations = true,
            EnableLinkAnimations = false,
            Graph = new MapGraph
            {
                Nodes =
                [
                    new MapNode { Id = 10, Name = "First Region", X = 0.3, Y = 0.5 },
                    new MapNode { Id = 20, Name = "Second Region", X = 0.7, Y = 0.5 }
                ],
                Links = [new MapLink { FromId = 10, ToId = 20 }]
            }
        };
        var window = new Window { Width = 800, Height = 600, Content = map };
        try
        {
            window.Show();
            using (Render(map)) { }
            var first = map.WorldToScreen(new Point(0.3, 0.5));
            var second = map.WorldToScreen(new Point(0.7, 0.5));
            window.MouseMove(first);
            using var withoutAnimation = Render(map);
            map.EnableLinkAnimations = true;
            using var withAnimation = Render(map);

            var y = (int)first.Y;
            var midpoint = (int)((first.X + second.X) / 2);
            Assert.NotEqual(withoutAnimation.GetPixel(midpoint, y), withAnimation.GetPixel(midpoint, y));
            foreach (var center in new[] { first, second })
            {
                for (var row = y - 4; row <= y + 4; row++)
                for (var x = (int)center.X - 20; x <= (int)center.X + 20; x++)
                {
                    Assert.Equal(withoutAnimation.GetPixel(x, row), withAnimation.GetPixel(x, row));
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static SKBitmap Render(MapControl map)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 600), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext(true))
        {
            map.Render(context);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
