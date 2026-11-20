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
    string? Notes = null,
    string? BroadcastTo = null)
{
    public bool IsStructured =>
        FcName is not null || FormupLocation is not null || PapType is not null ||
        Doctrine is not null || CommsLabel is not null || CommsUrl is not null;
}

public sealed record XmppFleetLineup(string Title, string? Hurf, IReadOnlyList<XmppFleetCommander> Commanders);

public sealed record XmppFleetCommander(string Name, string? CommsLabel, string? CommsUrl);

public static partial class XmppFleetPingParsing
{
    /// <summary>Structured fleet/sig ping (FC / formup / PAP / doctrine / comms).</summary>
    public static XmppFleetPing? TryParse(string? body)
    {
        var envelope = TryParseBroadcast(body);
        return envelope is { IsStructured: true } ? envelope : null;
    }

    /// <summary>Any Directorbot-style broadcast with a footer, structured or free-form.</summary>
    public static XmppFleetPing? TryParseBroadcast(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var text = NormalizeBroadcast(body);
        var footer = FooterPattern().Match(text);
        if (!footer.Success) return null;

        var broadcast = text[..footer.Index];
        var from = footer.Groups["from"].Value.Trim();
        var to = NormalizeBroadcastTo(footer.Groups["to"].Value);

        var fields = FleetFieldPattern().Matches(broadcast);
        string? hurf;
        string? fc = null, formup = null, pap = null, doctrine = null, notes = null;
        string? commsLabel = null, commsUrl = null;

        if (fields.Count == 0)
        {
            hurf = broadcast.Trim();
            if (hurf is ['"', .., '"']) hurf = hurf[1..^1].Trim();
            if (string.IsNullOrWhiteSpace(hurf)) hurf = null;
            return new(hurf, null, null, null, null, null, null, from, null, to);
        }

        hurf = broadcast[..fields[0].Index].Trim();
        if (hurf is ['"', .., '"']) hurf = hurf[1..^1].Trim();
        if (string.IsNullOrWhiteSpace(hurf)) hurf = null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match field in fields)
        {
            var name = CanonicalFieldName(field.Groups["name"].Value);
            var value = field.Groups["value"].Value.Trim();
            if (value.Length > 0)
                values.TryAdd(name, value);
        }

        fc = values.GetValueOrDefault("FC Name") ?? values.GetValueOrDefault("FC");
        formup = values.GetValueOrDefault("Formup Location");
        pap = values.GetValueOrDefault("PAP Type");
        doctrine = values.GetValueOrDefault("Doctrine");
        if (values.TryGetValue("Comms", out var comms))
            (commsLabel, commsUrl) = ParseComms(comms);

        var lastField = fields[^1];
        notes = doctrine is null ? null : broadcast[(lastField.Index + lastField.Length)..].Trim();
        if (string.IsNullOrEmpty(notes)) notes = null;

        return new(
            hurf,
            NormalizeFcName(fc) ?? fc,
            formup, pap, commsLabel, commsUrl, doctrine, from, notes, to);
    }

    public static string? NormalizeBroadcastTo(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var value = target.Trim().ToLowerInvariant();
        // Drop trailing timestamp fragment if a loose match leaked it (defensive).
        var at = value.IndexOf(" at ", StringComparison.Ordinal);
        if (at > 0) value = value[..at].Trim();
        if (value is "locust") value = "locusts";
        return value.Length == 0 ? null : value;
    }

    public static string FeedTitleForTarget(string? broadcastTo, bool structured) =>
        broadcastTo switch
        {
            "opt-krab" => "Opt-Krab",
            "locusts" => "Locust Fleet",
            "beehive" => "Beehive Fleet",
            "all" => "Announcement",
            "opt-all" => "Opt-All",
            _ when structured => "Fleet ping",
            _ => "Directorbot"
        };

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
            ? new(hasHeading ? "Mining Fleet Lineup" : "Fleet Lineup", hurf.Length == 0 ? null : hurf, commanders)
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

    private static string CanonicalFieldName(string name)
    {
        if (name.Equals("FC", StringComparison.OrdinalIgnoreCase)) return "FC";
        if (name.Equals("FC Name", StringComparison.OrdinalIgnoreCase)) return "FC Name";
        if (name.Equals("Formup Location", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Formup location", StringComparison.OrdinalIgnoreCase))
            return "Formup Location";
        if (name.Equals("PAP Type", StringComparison.OrdinalIgnoreCase)) return "PAP Type";
        if (name.Equals("Doctrine", StringComparison.OrdinalIgnoreCase)) return "Doctrine";
        if (name.Equals("Comms", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Beehive Comms", StringComparison.OrdinalIgnoreCase))
            return "Comms";
        return name;
    }

    private static string NormalizeBroadcast(string body)
    {
        var text = body.Replace("\r\n", "\n").Replace('\r', '\n');
        // Drop ZW-only padding lines before stripping Format chars (which would leave blank lines).
        text = PaddingLinePattern().Replace(text, "");
        text = StripInvisibleFormat(text);
        // Normalize "Beehive Comms - url" before colon-based field rewrites.
        text = BeehiveCommsPattern().Replace(text, "Comms: $1");
        text = GluedFleetFieldPattern().Replace(text, "\n");
        text = FleetFieldPattern().Replace(text, field =>
            CanonicalFieldName(field.Groups["name"].Value) + ": " + TrimFieldPadding(field.Groups["value"].Value));
        return text.Trim();
    }

    private static string StripInvisibleFormat(string text)
    {
        var buffer = new char[text.Length];
        var n = 0;
        foreach (var ch in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.Format)
                buffer[n++] = ch;
        }
        return n == text.Length ? text : new string(buffer, 0, n);
    }

    private static string TrimFieldPadding(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsFieldPadding(value[start])) start++;
        while (end > start && IsFieldPadding(value[end - 1])) end--;
        return value[start..end];
    }

    private static bool IsFieldPadding(char value) =>
        char.IsWhiteSpace(value) || CharUnicodeInfo.GetUnicodeCategory(value) == UnicodeCategory.Format;

    private static (string? Label, string? Url) ParseComms(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var urlMatch = UrlPattern().Match(value);
        if (!urlMatch.Success) return (value.Trim(), null);
        var url = urlMatch.Value.Trim().TrimEnd(')', '.', ',', ';');
        var label = value[..urlMatch.Index].Trim().TrimEnd('-', '–', '—', ':').Trim();
        if (label.Length == 0) label = url;
        return (label, url);
    }

    // Longer labels first so "FC Name" wins over "FC".
    [GeneratedRegex(@"^[ \t]*(?<name>FC Name|Formup Location|PAP Type|Beehive Comms|Doctrine|Comms|FC)[ \t]*:(?<value>[^\n]*)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FleetFieldPattern();

    [GeneratedRegex(@"(?<=\S)[ \t]*(?=(?:FC Name|Formup Location|PAP Type|Beehive Comms|Doctrine|Comms|FC)[ \t]*:)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GluedFleetFieldPattern();

    [GeneratedRegex(@"^[ \t]*Beehive Comms[ \t]*[-–—:][ \t]*(?<value>[^\n]*)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BeehiveCommsPattern();

    [GeneratedRegex(@"^[ \t]*(?<name>FC|Comms)[ \t]*:(?<value>[^\n]*)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineupFieldPattern();

    [GeneratedRegex(@"^[ \t\u200B\u200C\u200D\uFEFF]+$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PaddingLinePattern();

    [GeneratedRegex(
        @"(?m)^~~~ This was a (?:(?<channel>[\w-]+) )?broadcast from (?<from>[^\n]+?) to (?<to>[^\n]+?) at (?<stamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?) EVE ~~~\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimestampedFooterPattern();

    [GeneratedRegex(
        @"~~~ This was a (?:(?<channel>[\w-]+) )?broadcast from (?<from>.+?) to (?<to>.+?)(?: at \d{4}-\d{2}-\d{2}[^\n]* EVE)?\s*~~~",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FooterPattern();

    [GeneratedRegex("""https?://[^\s<>"']+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex("""\s+""", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
