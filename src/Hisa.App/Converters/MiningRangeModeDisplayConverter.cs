using System.Globalization;
using Avalonia.Data.Converters;
using Hisa.Core.Models;

namespace Hisa.App.Converters;

public sealed class MiningRangeModeDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is MiningStatsRangeMode mode
            ? MainWindowViewModel.GetMiningRangeModeLabel(mode)
            : value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
