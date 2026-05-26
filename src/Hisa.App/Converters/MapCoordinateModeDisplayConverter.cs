using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Hisa.Core.Models;

namespace Hisa.App.Converters;

public sealed class MapCoordinateModeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MapCoordinateMode mode)
        {
            return value?.ToString() ?? string.Empty;
        }

        return mode switch
        {
            MapCoordinateMode.ThreeDProjectedXZ => "3D Flattened",
            MapCoordinateMode.SdePlanarXY => "2D Map",
            _ => mode.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
