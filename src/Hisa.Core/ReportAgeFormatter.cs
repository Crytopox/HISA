using System.Globalization;

namespace Hisa.Core;

public static class ReportAgeFormatter
{
    public static string Format(TimeSpan age)
    {
        var seconds = Math.Max(0, (long)age.TotalSeconds);
        if (seconds < 60) return string.Create(CultureInfo.InvariantCulture, $"{seconds}s ago");
        var span = TimeSpan.FromSeconds(seconds);
        if (seconds < 3600)
            return string.Create(CultureInfo.InvariantCulture, $"{(long)span.TotalMinutes}m {span.Seconds:00}s ago");
        if (seconds < 86400)
            return string.Create(CultureInfo.InvariantCulture, $"{(long)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s ago");
        return string.Create(CultureInfo.InvariantCulture, $"{span.Days}d {span.Hours:00}h {span.Minutes:00}m {span.Seconds:00}s ago");
    }
}
