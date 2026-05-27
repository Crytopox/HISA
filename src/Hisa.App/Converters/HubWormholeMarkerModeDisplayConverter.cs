using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Hisa.Core.Models;

namespace Hisa.App.Converters;

public sealed class HubWormholeMarkerModeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HubWormholeMarkerMode mode)
        {
            return value?.ToString() ?? string.Empty;
        }

        return mode switch
        {
            HubWormholeMarkerMode.Badge => "Badge",
            HubWormholeMarkerMode.Ring => "Ring",
            HubWormholeMarkerMode.Halo => "Halo",
            _ => mode.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
