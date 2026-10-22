using System.Globalization;
using System.Text.RegularExpressions;
using Hisa.Core.Models;

namespace Hisa.Logs.FleetBoosts;

public static class FleetBoostMessageParser
{
    private static readonly Regex Line = new(@"^\s*\[\s*(?<time>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2})\s*\]\s*(?<pilot>.+?)\s*>\s*(?<text>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Positive = new(@"(?<!\w)(?:\+\s*)?(?:ml|mind\s*link)(?!\w)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Negative = new(@"(?<!\w)(?:no\s+|without\s+|-\s*)(?:ml|mind\s*link)(?!\w)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly (FleetBoostCharge Charge, Regex Pattern)[] Patterns = FleetBoostCatalog.Charges.Select(c =>
        (c, new Regex(@"(?<!\w)" + Regex.Escape(c.Name) + @"(?!\w)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))).ToArray();

    public static FleetBoostMessage? Parse(string line)
    {
        var match = Line.Match(line.Replace("\uFEFF", ""));
        if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value, "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)) return null;
        var pilot = match.Groups["pilot"].Value.Trim();
        if (pilot.Equals("EVE System", StringComparison.OrdinalIgnoreCase)) return null;
        var text = Spaces.Replace(match.Groups["text"].Value, " ");
        var charges = Patterns.Where(p => p.Pattern.IsMatch(text)).Select(p => p.Charge.Id).ToArray();
        bool? mindlink = Negative.IsMatch(text) ? false : Positive.IsMatch(text) ? true : null;
        return charges.Length == 0 && mindlink is null ? null : new(timestamp, pilot, charges, mindlink);
    }
}
