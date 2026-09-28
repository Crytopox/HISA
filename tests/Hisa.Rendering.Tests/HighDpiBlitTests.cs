using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

[assembly: AvaloniaTestApplication(typeof(Hisa.Rendering.Tests.HeadlessTestApp))]

namespace Hisa.Rendering.Tests;

public sealed class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class HighDpiBlitTests
{
    [AvaloniaFact]
    public void TwoTimesDpiBlit_PixelSourceRect_KeepsMarkerAtDipPosition()
    {
        const double scaling = 2.0;
        const double dipWidth = 100;
        const double dipHeight = 50;
        var marker = new Rect(40, 20, 8, 8);
        var markerBrush = new SolidColorBrush(Color.FromRgb(255, 0, 128));

        using var baked = new RenderTargetBitmap(
            new PixelSize((int)(dipWidth * scaling), (int)(dipHeight * scaling)),
            new Vector(96 * scaling, 96 * scaling));
        using (var ctx = baked.CreateDrawingContext(true))
        using (ctx.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
        {
            ctx.FillRectangle(markerBrush, marker);
        }

        var dipSource = FindMarker(Blit(baked, new Rect(baked.Size), dipWidth, dipHeight));
        var pixelSource = FindMarker(Blit(
            baked,
            new Rect(0, 0, baked.PixelSize.Width, baked.PixelSize.Height),
            dipWidth,
            dipHeight));

        var expected = marker.Center;
        Assert.True(
            pixelSource is not null && Distance(pixelSource.Value, expected) < 2,
            $"Pixel source rect landed at {Format(pixelSource)}; DIP source rect landed at {Format(dipSource)}; expected {expected}.");
        Assert.True(
            dipSource is null || Distance(dipSource.Value, expected) > 8,
            "DIP-sized source rect unexpectedly matched the marker. The blit fix assumption is wrong.");
    }

    private static RenderTargetBitmap Blit(RenderTargetBitmap source, Rect sourceRect, double dipWidth, double dipHeight)
    {
        var output = new RenderTargetBitmap(
            new PixelSize((int)dipWidth, (int)dipHeight),
            new Vector(96, 96));
        using var ctx = output.CreateDrawingContext(true);
        using (ctx.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased, BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            ctx.DrawImage(source, sourceRect, new Rect(0, 0, dipWidth, dipHeight));
        }

        return output;
    }

    private static Point? FindMarker(RenderTargetBitmap bitmap)
    {
        using var encoded = new MemoryStream();
        bitmap.Save(encoded);
        encoded.Position = 0;
        using var decoded = SKBitmap.Decode(encoded)
            ?? throw new InvalidOperationException($"Saved bitmap was not a readable image ({encoded.Length} bytes).");
        var width = decoded.Width;
        var height = decoded.Height;

        long sumX = 0;
        long sumY = 0;
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = decoded.GetPixel(x, y);
                var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
                var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
                if (color.Alpha > 200 && max > 200 && min < 40 && color.Red + color.Green + color.Blue > 300)
                {
                    sumX += x;
                    sumY += y;
                    count++;
                }
            }
        }

        bitmap.Dispose();
        return count == 0 ? null : new Point(sumX / (double)count, sumY / (double)count);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static string Format(Point? point) => point is null ? "missing" : $"{point.Value.X:0.##},{point.Value.Y:0.##}";
}
