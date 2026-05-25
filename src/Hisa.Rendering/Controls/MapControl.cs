using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hisa.Rendering.Controls;

public sealed class MapControl : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#101826")), bounds);

        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#2A3A52")), 1);
        context.DrawRectangle(borderPen, bounds.Deflate(0.5));

        var text = new FormattedText(
            "Map Renderer Placeholder",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            16,
            new SolidColorBrush(Color.Parse("#9FB4D2")));

        var origin = new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2);
        context.DrawText(text, origin);
    }
}
