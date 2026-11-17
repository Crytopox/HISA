using System.Globalization;
using System.Text.RegularExpressions;

namespace Hisa.Core.Models;

public sealed record XmppFleetPing(
    string? Hurf,
    string? FcName,
    string? FormupLocation,
    string? PapType,
    string? CommsLabel,
    string? CommsUrl,
    string? Doctrine,
    string? BroadcastFrom,
    string? Notes = null);

public sealed record XmppFleetLineup(string Title, string? Hurf, IReadOnlyList<XmppFleetCommander> Commanders);

public sealed record XmppFleetCommander(string Name, string? CommsLabel, string? CommsUrl);

public static partial class XmppFleetPingParsing
{
    public static XmppFleetPing? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var text = NormalizeBroadcast(body);
        var footer = FooterPattern().Match(text);
        if (!footer.Success) return null;

        var broadcast = text[..footer.Index];
        var fields = FleetFieldPattern().Matches(broadcast);
        if (fields.Count == 0) return null;

        var hurf = broadcast[..fields[0].Index].Trim();
        if (hurf is ['"', .., '"']) hurf = hurf[1..^1].Trim();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match field in fields)
        {
            var value = field.Groups["value"].Value.Trim();
            if (value.Length > 0)
                values.TryAdd(field.Groups["name"].Value, value);
        }
        var fc = values.GetValueOrDefault("FC Name");
        var formup = values.GetValueOrDefault("Formup Location");
        var pap = values.GetValueOrDefault("PAP Type");
        var doctrine = values.GetValueOrDefault("Doctrine");
        var (commsLabel, commsUrl) = ParseComms(values.GetValueOrDefault("Comms"));
        var from = footer.Groups[1].Value.Trim();

        // Require enough structure to look like a fleet broadcast, not a free-form Directorbot note.
        if (fc is null && formup is null && pap is null)
            return null;

        var lastField = fields[^1];
        var notes = doctrine is null ? null : broadcast[(lastField.Index + lastField.Length)..].Trim();
        return new(
            string.IsNullOrWhiteSpace(hurf) ? null : hurf,
            NormalizeFcName(fc) ?? fc, formup, pap, commsLabel, commsUrl, doctrine, from,
            string.IsNullOrEmpty(notes) ? null : notes);
    }

    public static string? TimestampedBroadcastBody(string body)
    {
        var normalized = NormalizeBroadcast(body);
        var footer = TimestampedFooterPattern().Match(normalized);
        if (!HasValidTimestamp(footer)) return null;
        // The bot can emit blank padding lines immediately before the footer.
        return normalized[..footer.Index].TrimEnd() + "\n" + footer.Value;
    }

    public static XmppFleetLineup? TryParseLineup(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var text = NormalizeBroadcast(body);
        var footer = TimestampedFooterPattern().Match(text);
        if (!HasValidTimestamp(footer)) return null;

        var broadcast = text[..footer.Index].TrimEnd();
        var firstField = LineupFieldPattern().Match(broadcast);
        if (!firstField.Success) return null;
        var heading = broadcast[..firstField.Index].Trim();
        var lastBreak = heading.LastIndexOf('\n');
        var title = heading[(lastBreak + 1)..].Trim();
        var hasHeading = title.Equals("Mining Fleets", StringComparison.OrdinalIgnoreCase);
        var hurf = hasHeading ? heading[..Math.Max(0, lastBreak)].Trim() : heading;
        if (hurf is ['"', .., '"']) hurf = hurf[1..^1].Trim();

        var commanders = new List<XmppFleetCommander>();
        var hasComms = false;
        foreach (var line in broadcast[firstField.Index..].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var field = LineupFieldPattern().Match(line);
            if (!field.Success) return null;
            var value = TrimFieldPadding(field.Groups["value"].Value);
            if (field.Groups["name"].Value.Equals("FC", StringComparison.OrdinalIgnoreCase))
            {
                var name = NormalizeFcName(value);
                if (name is null) return null;
                commanders.Add(new(name, null, null));
                continue;
            }

            if (commanders.Count == 0 || commanders[^1].CommsLabel is not null || value.Length == 0)
                return null;
            var (label, url) = ParseComms(value.Replace(@"https\://", "https://", StringComparison.OrdinalIgnoreCase)
                .Replace(@"http\://", "http://", StringComparison.OrdinalIgnoreCase));
            commanders[^1] = commanders[^1] with { CommsLabel = label, CommsUrl = url };
            hasComms = true;
        }

        return commanders.Count >= 2 && hasComms
            ? new(hasHeading ? "Locust Weekend Fleets" : "Fleet Lineup", hurf.Length == 0 ? null : hurf, commanders)
            : null;
    }

    private static bool HasValidTimestamp(Match footer) =>
        footer.Success && DateTime.TryParseExact(footer.Groups["stamp"].Value, "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Trim, collapse spaces, and strip simple trailing "(…)" / "/ alt" noise for ESI lookup.</summary>
    public static string? NormalizeFcName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var value = WhitespacePattern().Replace(name.Trim(), " ");
        var paren = value.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0) value = value[..paren].Trim();
        var slash = value.IndexOf(" / ", StringComparison.Ordinal);
        if (slash > 0) value = value[..slash].Trim();
        return value.Length == 0 ? null : value;
    }

    private static string NormalizeBroadcast(string body)
    {
        var text = body.Replace("\r\n", "\n").Replace('\r', '\n');
        text = PaddingLinePattern().Replace(text, "");
        text = FleetFieldPattern().Replace(text, field =>
            field.Groups["name"].Value + ": " + TrimFieldPadding(field.Groups["value"].Value));
        return text.Trim();
    }

    private static string TrimFieldPadding(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsFieldPadding(value[start])) start++;
        while (end > start && IsFieldPadding(value[end - 1])) end--;
        return value[start..end];
    }

    private static bool IsFieldPadding(char value) => char.IsWhiteSpace(value) || value is '\u200D' or '\uFEFF';

    private static (string? Label, string? Url) ParseComms(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var urlMatch = UrlPattern().Match(value);
        if (!urlMatch.Success) return (value.Trim(), null);
        var url = urlMatch.Value.Trim().TrimEnd(')', '.', ',', ';');
        var label = value[..urlMatch.Index].Trim();
        if (label.Length == 0) label = url;
        return (label, url);
    }

    [GeneratedRegex(@"^[ \t]*(?<name>FC Name|Formup Location|PAP Type|Comms|Doctrine)[ \t]*:(?<value>[^\n]*)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FleetFieldPattern();

    [GeneratedRegex(@"^[ \t]*(?<name>FC|Comms)[ \t]*:(?<value>[^\n]*)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineupFieldPattern();

    [GeneratedRegex(@"^[ \t\u200D\uFEFF]+$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PaddingLinePattern();

    [GeneratedRegex(@"(?m)^~~~ This was a (?:(?:skirmishbot|coord) )?broadcast from [^\n]+? to [^\n]+? at (?<stamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?) EVE ~~~\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimestampedFooterPattern();

    [GeneratedRegex(@"~~~ This was a (?:skirmishbot|coord) broadcast from (.+?) to .+?~~~",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FooterPattern();

    [GeneratedRegex("""https?://[^\s<>"']+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex("""\s+""", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
