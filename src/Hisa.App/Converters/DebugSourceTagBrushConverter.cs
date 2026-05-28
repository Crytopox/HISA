using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Hisa.App.Converters;

public sealed class DebugSourceTagBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tag = value?.ToString() ?? "APP";
        var mode = parameter?.ToString();

        if (string.Equals(mode, "Bg", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(tag.ToUpperInvariant() switch
            {
                "NET" => Color.Parse("#0B3A2E"),
                "ESI" => Color.Parse("#3A2A0B"),
                _ => Color.Parse("#1E293B")
            });
        }

        return new SolidColorBrush(tag.ToUpperInvariant() switch
        {
            "NET" => Color.Parse("#34D399"),
            "ESI" => Color.Parse("#FCD34D"),
            _ => Color.Parse("#93C5FD")
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
