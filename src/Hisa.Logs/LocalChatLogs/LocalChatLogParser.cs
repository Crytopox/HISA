using System.Globalization;
using System.Text.RegularExpressions;

namespace Hisa.Logs.LocalChatLogs;

public sealed class LocalChatLogParser
{
    private static readonly Regex SystemChangeRegex = new(
        @"^\[\s*(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]\s*EVE System\s*>\s*Channel changed to Local\s*:\s*(?<system>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public LocalChatLogSession Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var listener = ParseHeaderValue(lines, "Listener")
            ?? throw new InvalidOperationException("Local chat log is missing header field: Listener.");
        var sessionStartedText = ParseHeaderValue(lines, "Session started")
            ?? throw new InvalidOperationException("Local chat log is missing header field: Session started.");
        var sessionStartedUtc = ParseTimestamp(sessionStartedText);

        var systemChanges = new List<LocalSystemChange>();
        foreach (var rawLine in lines)
        {
            var line = NormalizeLine(rawLine);
            if (line.Length == 0)
            {
                continue;
            }

            var match = SystemChangeRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var timestamp = ParseTimestamp(match.Groups["timestamp"].Value);
            var system = match.Groups["system"].Value.Trim();
            if (system.Length == 0)
            {
                continue;
            }

            systemChanges.Add(new LocalSystemChange
            {
                TimestampUtc = timestamp,
                SolarSystemName = system
            });
        }

        return new LocalChatLogSession
        {
            Listener = listener,
            SessionStartedUtc = sessionStartedUtc,
            SystemChanges = systemChanges
        };
    }

    private static string? ParseHeaderValue(IReadOnlyList<string> lines, string key)
    {
        foreach (var rawLine in lines)
        {
            var line = NormalizeLine(rawLine);
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var foundKey = line[..separator].Trim();
            if (!string.Equals(foundKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(separator + 1)..].Trim();
        }

        return null;
    }

    private static string NormalizeLine(string line)
    {
        return line.TrimStart('\uFEFF').Trim();
    }

    private static DateTime ParseTimestamp(string value)
    {
        return DateTime.SpecifyKind(
            DateTime.ParseExact(value.Trim(), "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeKind.Utc);
    }
}
