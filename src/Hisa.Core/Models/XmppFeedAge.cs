namespace Hisa.Core.Models;

public static class XmppFeedAge
{
    public static string Format(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var delta = (now ?? DateTimeOffset.UtcNow) - timestamp;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta < TimeSpan.FromSeconds(45)) return "just now";
        if (delta < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
            return $"{minutes}m ago";
        }
        if (delta < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return $"{hours}h ago";
        }

        var days = Math.Max(1, (int)Math.Round(delta.TotalDays));
        return $"{days}d ago";
    }
}
