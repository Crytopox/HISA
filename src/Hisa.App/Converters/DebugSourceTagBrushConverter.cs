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
            return new SolidColorBrush(string.Equals(tag, "NET", StringComparison.OrdinalIgnoreCase)
                ? Color.Parse("#0B3A2E")
                : Color.Parse("#1E293B"));
        }

        return new SolidColorBrush(string.Equals(tag, "NET", StringComparison.OrdinalIgnoreCase)
            ? Color.Parse("#34D399")
            : Color.Parse("#93C5FD"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
