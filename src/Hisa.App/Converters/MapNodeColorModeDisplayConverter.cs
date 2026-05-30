using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Hisa.Core.Models;

namespace Hisa.App.Converters;

public sealed class MapNodeColorModeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MapNodeColorMode mode)
        {
            return value?.ToString() ?? string.Empty;
        }

        return mode switch
        {
            MapNodeColorMode.Hostiles => "Hostiles",
            MapNodeColorMode.Security => "Security Status",
            MapNodeColorMode.Star => "Star Color",
            MapNodeColorMode.NullsecTrueSec => "Nullsec TrueSec",
            MapNodeColorMode.JoveObservatory => "Jove Observatory",
            MapNodeColorMode.IceBelts => "Ice Belts",
            MapNodeColorMode.Storms => "Metaliminal Storms",
            MapNodeColorMode.Wormholes => "Thera/Turnur Wormholes",
            MapNodeColorMode.SovUpgrades => "SOV Upgrades",
            MapNodeColorMode.Incursions => "Incursions",
            _ => mode.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
