using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Hisa.Core.Models;

namespace Hisa.App.Converters;

public sealed class MiningOverlayRangeModeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is MiningOverlayRangeMode mode
            ? MainWindowViewModel.GetMiningOverlayRangeModeLabel(mode)
            : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
