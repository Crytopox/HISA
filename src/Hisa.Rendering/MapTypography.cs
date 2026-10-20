using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Hisa.Rendering;

internal static class MapTypography
{
    internal const double LabelSize = 10;
    internal const double SecondarySize = 10;
    internal const double BodySize = 11;
    internal const double HeadingSize = 12;
    internal const double LabelPadX = 2;
    internal const double LabelPadY = 1;
    internal static readonly FontFamily Family = new("fonts:Inter#Inter");
    internal static readonly FontFeatureCollection Features = new()
    {
        FontFeature.Parse("+zero"),
        FontFeature.Parse("+tnum")
    };

    // Security and threat palettes also color large map shapes. Their darkest values
    // need a lighter text variant on the map's dark surfaces at these small sizes.
    internal static Color ReadableColor(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color value) => 0.2126 * Channel(value.R) + 0.7152 * Channel(value.G) + 0.0722 * Channel(value.B);
        var background = Color.Parse("#101722");
        var candidate = color;
        for (var step = 1; step <= 20 && (Luminance(candidate) + 0.05) / (Luminance(background) + 0.05) < 4.5; step++)
        {
            var amount = step / 20.0;
            candidate = Color.FromRgb((byte)(color.R + (255 - color.R) * amount),
                (byte)(color.G + (255 - color.G) * amount), (byte)(color.B + (255 - color.B) * amount));
        }
        return candidate;
    }

    internal static FormattedText Create(string text, double size, IBrush? brush,
        FontWeight weight = FontWeight.Normal, double maxWidth = double.PositiveInfinity, int maxLines = int.MaxValue)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Family, FontStyle.Normal, weight == default ? FontWeight.Normal : weight), size, brush);
        formatted.SetFontFeatures(Features);
        if (double.IsFinite(maxWidth))
        {
            formatted.MaxTextWidth = Math.Max(1, maxWidth);
            formatted.MaxLineCount = maxLines;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }
        return formatted;
    }
}
