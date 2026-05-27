using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Microsoft.Extensions.Logging;

namespace Hisa.App.Converters;

public sealed class DebugLogLevelBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LogLevel level)
        {
            return Brushes.LightGray;
        }

        var mode = parameter?.ToString();
        return mode switch
        {
            "Accent" => new SolidColorBrush(GetAccentColor(level)),
            "ChipBg" => new SolidColorBrush(GetChipBackground(level)),
            _ => new SolidColorBrush(GetTextColor(level))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Color GetAccentColor(LogLevel level) => level switch
    {
        LogLevel.Trace => Color.Parse("#6B7280"),
        LogLevel.Debug => Color.Parse("#60A5FA"),
        LogLevel.Information => Color.Parse("#34D399"),
        LogLevel.Warning => Color.Parse("#F59E0B"),
        LogLevel.Error => Color.Parse("#F87171"),
        LogLevel.Critical => Color.Parse("#EF4444"),
        _ => Color.Parse("#9CA3AF")
    };

    private static Color GetChipBackground(LogLevel level) => level switch
    {
        LogLevel.Trace => Color.Parse("#1F2937"),
        LogLevel.Debug => Color.Parse("#1E3A8A"),
        LogLevel.Information => Color.Parse("#064E3B"),
        LogLevel.Warning => Color.Parse("#78350F"),
        LogLevel.Error => Color.Parse("#7F1D1D"),
        LogLevel.Critical => Color.Parse("#450A0A"),
        _ => Color.Parse("#1F2937")
    };

    private static Color GetTextColor(LogLevel level) => level switch
    {
        LogLevel.Trace => Color.Parse("#9CA3AF"),
        LogLevel.Debug => Color.Parse("#93C5FD"),
        LogLevel.Information => Color.Parse("#A7F3D0"),
        LogLevel.Warning => Color.Parse("#FCD34D"),
        LogLevel.Error => Color.Parse("#FCA5A5"),
        LogLevel.Critical => Color.Parse("#FCA5A5"),
        _ => Color.Parse("#E5E7EB")
    };
}
