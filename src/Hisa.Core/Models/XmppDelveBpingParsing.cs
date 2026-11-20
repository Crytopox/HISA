using System.Text.RegularExpressions;

namespace Hisa.Core.Models;

public sealed record XmppDelveBping(
    string? Title,
    string? Pilot,
    string? System,
    string? Location,
    string? Cyno)
{
    /// <summary>Primary system name used for map focus (strips parentheticals).</summary>
    public string? MapSystem => XmppDelveBpingParsing.PrimarySystemName(System);
}

public static partial class XmppDelveBpingParsing
{
    // Longest labels first. Short Cyno/Location use lookbehinds so they do not split
    // "Name of Cyno" / "Location In System".
    private const string LabelAlternation =
        @"Rorqual\s+Name|Rorq\s+Pilot|Rorqual\s+Pilot|" +
        @"Location\s+In\s+System(?:\s*\([^)]*\))?|Name\s+of\s+Cyno(?:\s*\([^)]*\))?|" +
        @"Cyno\s+Perch|(?<!In\s)Location|(?<!of\s)Cyno|System";

    public static XmppDelveBping? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var match = BpingPrefixPattern().Match(body);
        if (!match.Success) return null;

        var payload = body[match.Length..].Trim();
        if (payload.Length == 0) return null;

        payload = InsertBreaksBeforeLabels(payload);
        var fields = FieldPattern().Matches(payload);
        string? title = null;
        if (fields.Count > 0)
        {
            title = payload[..fields[0].Index].Trim().TrimEnd(':').Trim();
            if (title.Length == 0) title = null;
        }
        else
        {
            title = string.IsNullOrWhiteSpace(payload) ? null : payload.Trim();
            return title is null ? null : new(title, null, null, null, null);
        }

        string? pilot = null, system = null, location = null, cyno = null;
        foreach (Match field in fields)
        {
            var label = NormalizeLabel(field.Groups["label"].Value);
            var value = CleanValue(field.Groups["value"].Value);
            if (value.Length == 0) continue;
            switch (label)
            {
                case "pilot" when pilot is null:
                    pilot = value;
                    break;
                case "system" when system is null:
                    system = value;
                    break;
                case "location" when location is null:
                    location = value;
                    break;
                case "cyno" when cyno is null:
                    cyno = value;
                    break;
            }
        }

        if (pilot is null && system is null && location is null && cyno is null && title is null)
            return null;

        if (title is null && (pilot is not null || system is not null || location is not null || cyno is not null))
            title = "Rorqual Tackled";

        return new(title, pilot, system, location, cyno);
    }

    public static string? PrimarySystemName(string? system)
    {
        if (string.IsNullOrWhiteSpace(system)) return null;
        var text = system.Trim();
        var paren = text.IndexOf('(');
        if (paren > 0) text = text[..paren].Trim();
        var token = SystemTokenPattern().Match(text);
        return token.Success ? token.Value : (text.Length == 0 ? null : text.Split()[0]);
    }

    private static string InsertBreaksBeforeLabels(string payload) =>
        LabelBoundaryPattern().Replace(payload, "\n");

    private static string NormalizeLabel(string label)
    {
        var key = Regex.Replace(label.Trim().TrimEnd(':').TrimEnd(';').Trim(), @"\s+", " ",
            RegexOptions.CultureInvariant);
        if (PilotLabelPattern().IsMatch(key)) return "pilot";
        if (SystemLabelPattern().IsMatch(key)) return "system";
        if (LocationLabelPattern().IsMatch(key)) return "location";
        if (CynoLabelPattern().IsMatch(key)) return "cyno";
        return key.ToLowerInvariant();
    }

    private static string CleanValue(string value)
    {
        var text = value.Trim().TrimStart(':').Trim();
        text = Regex.Replace(text, @"[ \t]+", " ");
        return text.Trim();
    }

    [GeneratedRegex(@"^\s*!bping\s+all\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BpingPrefixPattern();

    [GeneratedRegex(
        @"(?i)(?<!(?:\S))(?<label>" + LabelAlternation + @")\s*[:;]\s*(?<value>.*?)(?=" +
        @"(?:\s+(?:" + LabelAlternation + @")\s*[:;])|$)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex FieldPattern();

    [GeneratedRegex(
        @"(?i)(?<=\S)[ \t]+(?=(?:" + LabelAlternation + @")\s*[:;])",
        RegexOptions.CultureInvariant)]
    private static partial Regex LabelBoundaryPattern();

    [GeneratedRegex(@"^(?i)(?:Rorqual\s+Name|Rorq\s+Pilot|Rorqual\s+Pilot)$", RegexOptions.CultureInvariant)]
    private static partial Regex PilotLabelPattern();

    [GeneratedRegex(@"^(?i)System$", RegexOptions.CultureInvariant)]
    private static partial Regex SystemLabelPattern();

    [GeneratedRegex(@"^(?i)Location(?:\s+In\s+System(?:\s*\([^)]*\))?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LocationLabelPattern();

    [GeneratedRegex(@"^(?i)(?:Name\s+of\s+Cyno(?:\s*\([^)]*\))?|Cyno\s+Perch|Cyno)$", RegexOptions.CultureInvariant)]
    private static partial Regex CynoLabelPattern();

    [GeneratedRegex(@"^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex SystemTokenPattern();
}
