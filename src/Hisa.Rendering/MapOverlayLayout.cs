using Avalonia;
using Avalonia.Media;

namespace Hisa.Rendering;

// Each element supplies both its draw bounds and its hit bounds. Rows are measured again
// at the final content width, so wrapping and optional icons cannot leave phantom space.
internal sealed record MapOverlayElement(Rect Bounds, Action<DrawingContext, Rect> Draw,
    string? Url = null, string? Tooltip = null, string? Text = null,
    string? ReportId = null, FormattedText? MarqueeText = null, string? Role = null);

internal sealed class MapOverlayRow
{
    internal List<MapOverlayElement> Elements { get; } = [];
    internal double? PreferredWidth { get; set; }
    internal double MinimumHeight { get; set; }
    internal double Width => Elements.Count == 0 ? 0 : Elements.Max(e => e.Bounds.Right);
    internal double Height => Math.Max(MinimumHeight, Elements.Count == 0 ? 0 : Elements.Max(e => e.Bounds.Bottom));

    internal void AddText(string value, FormattedText text, double x = 0, double y = 0, string? tooltip = null) =>
        Elements.Add(new(new Rect(x, y, text.Width, text.Height),
            (context, bounds) => context.DrawText(text, bounds.TopLeft), Tooltip: tooltip, Text: value));
}

internal sealed class MapOverlayLayout
{
    internal const double Padding = 4;
    internal const double InlineGap = 2;
    internal const double RowGap = 1;
    internal const double SectionGap = 4;
    internal const double IconSize = 20;
    internal const double HeadingIconSize = 16;
    internal const double MinWidth = 180;
    internal const double MaxWidth = 300;
    internal const double ViewportInset = 8;
    internal List<MapOverlayElement> Elements { get; } = [];
    internal List<Rect> Rows { get; } = [];
    internal Dictionary<string, Rect> Reports { get; } = new(StringComparer.Ordinal);
    internal Size Size { get; private set; }

    internal static MapOverlayLayout Measure(IReadOnlyList<(Func<double, MapOverlayRow> Measure, double Gap)> definitions,
        double maxWidth, double minimumWidth = MinWidth)
    {
        var limit = Math.Max(1, Math.Min(MaxWidth, maxWidth) - Padding * 2);
        var preferred = definitions.Select(d => d.Measure(limit)).Select(r => r.PreferredWidth ?? r.Width).DefaultIfEmpty(0).Max();
        var width = Math.Min(limit, Math.Max(Math.Max(MinWidth, minimumWidth) - Padding * 2, preferred));
        var layout = new MapOverlayLayout();
        var y = Padding;
        foreach (var definition in definitions)
        {
            var row = definition.Measure(width);
            if (row.Elements.Count == 0) continue;
            if (layout.Rows.Count > 0) y += definition.Gap;
            layout.Rows.Add(new Rect(Padding, y, width, row.Height));
            foreach (var element in row.Elements)
            {
                var positioned = element with { Bounds = element.Bounds.Translate(new Vector(Padding, y)) };
                layout.Elements.Add(positioned);
                if (element.ReportId is { } id)
                    layout.Reports[id] = layout.Reports.TryGetValue(id, out var previous)
                        ? previous.Union(positioned.Bounds) : positioned.Bounds;
            }
            y += row.Height;
        }
        layout.Size = new Size(width + Padding * 2, y + Padding);
        return layout;
    }

    internal static Rect Place(Point anchor, Size size, Size viewport)
    {
        var width = Math.Min(size.Width, Math.Max(0, viewport.Width - ViewportInset * 2));
        var height = Math.Min(size.Height, Math.Max(0, viewport.Height - ViewportInset * 2));
        var x = anchor.X + 14;
        var y = anchor.Y + 8;
        if (x + width > viewport.Width - ViewportInset) x = anchor.X - width - 14;
        if (y + height > viewport.Height - ViewportInset) y = anchor.Y - height - 8;
        x = Math.Clamp(x, ViewportInset, Math.Max(ViewportInset, viewport.Width - ViewportInset - width));
        y = Math.Clamp(y, ViewportInset, Math.Max(ViewportInset, viewport.Height - ViewportInset - height));
        return new Rect(x, y, width, height);
    }

}
