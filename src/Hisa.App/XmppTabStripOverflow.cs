namespace Hisa.App;

/// <summary>Pure layout helpers for Jabber tab-strip overflow (no Avalonia types).</summary>
internal static class XmppTabStripOverflow
{
    /// <summary>
    /// Enter overflow when min-width tabs exceed available space.
    /// Once overflowing, stay until there is clear slack so losing the chevron
    /// does not immediately re-enter.
    /// </summary>
    public static bool ShouldUseOverflow(bool currentlyOverflowing, double minTotal, double available, double exitSlack)
    {
        if (minTotal > available) return true;
        if (!currentlyOverflowing) return false;
        return available < minTotal + Math.Max(0, exitSlack);
    }

    /// <summary>
    /// First-fit keep tabs within <paramref name="budget"/>, always retaining
    /// <paramref name="activeIndex"/> when it is a valid index.
    /// </summary>
    public static IReadOnlyList<int> SelectVisibleIndices(
        IReadOnlyList<double> widths, double spacing, double budget, int activeIndex)
    {
        if (widths.Count == 0) return Array.Empty<int>();

        var visible = new List<int>(widths.Count);
        var used = 0d;
        for (var index = 0; index < widths.Count; index++)
        {
            var width = Math.Max(0, widths[index]);
            var next = visible.Count == 0 ? width : used + spacing + width;
            if (next <= budget || (index == activeIndex && visible.Count == 0))
            {
                visible.Add(index);
                used = next;
            }
        }

        if (activeIndex < 0 || activeIndex >= widths.Count || visible.Contains(activeIndex))
            return visible;

        while (visible.Count > 0 && !visible.Contains(activeIndex))
        {
            visible.RemoveAt(visible.Count - 1);
            used = 0;
            for (var i = 0; i < visible.Count; i++)
            {
                used += Math.Max(0, widths[visible[i]]);
                if (i > 0) used += spacing;
            }

            var width = Math.Max(0, widths[activeIndex]);
            var withActive = visible.Count == 0 ? width : used + spacing + width;
            if (withActive <= budget || visible.Count == 0)
            {
                visible.Add(activeIndex);
                used = withActive;
            }
        }

        if (!visible.Contains(activeIndex))
            visible.Add(activeIndex);
        return visible;
    }

    public static double FitCaptionWidth(
        Func<double, double> measureTotal, double minWidth, double maxWidth, double available, int steps = 12)
    {
        if (maxWidth <= minWidth) return minWidth;
        var low = minWidth;
        var high = maxWidth;
        for (var step = 0; step < steps; step++)
        {
            var mid = (low + high) / 2;
            if (measureTotal(mid) <= available) low = mid;
            else high = mid;
        }
        return low;
    }
}
