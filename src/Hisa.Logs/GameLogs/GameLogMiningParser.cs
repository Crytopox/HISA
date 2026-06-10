using System.Globalization;
using System.Text.RegularExpressions;
using Hisa.Core.Models;

namespace Hisa.Logs.GameLogs;

public static partial class GameLogMiningParser
{
    private static readonly Regex HeaderListenerRegex = BuildHeaderListenerRegex();
    private static readonly Regex MinedOreRegex = BuildMinedOreRegex();
    private static readonly Regex CriticalOreRegex = BuildCriticalOreRegex();
    private static readonly Regex ResidueRegex = BuildResidueRegex();
    private static readonly Regex EfficiencyRegex = BuildEfficiencyRegex();

    public static string? TryParseListener(string rawLine)
    {
        var line = NormalizeLine(rawLine);
        if (line.Length == 0)
        {
            return null;
        }

        var match = HeaderListenerRegex.Match(line);
        return match.Success ? match.Groups["name"].Value.Trim() : null;
    }

    public static bool TryParseMiningEvent(string rawLine, out MiningLogEvent miningEvent)
    {
        miningEvent = default!;
        var line = NormalizeLine(rawLine);
        if (line.Length == 0)
        {
            return false;
        }

        if (TryParseYield(line, out miningEvent) ||
            TryParseCriticalYield(line, out miningEvent) ||
            TryParseResidue(line, out miningEvent) ||
            TryParseEfficiency(line, out miningEvent))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseYield(string line, out MiningLogEvent miningEvent)
    {
        miningEvent = default!;
        var match = MinedOreRegex.Match(line);
        if (!match.Success || !TryParseTimestamp(match.Groups["timestamp"].Value, out var timestampUtc))
        {
            return false;
        }

        miningEvent = new MiningLogEvent
        {
            Kind = MiningLogEventKind.Yield,
            TimestampUtc = timestampUtc,
            OreName = match.Groups["ore"].Value.Trim(),
            Units = ParseUnits(match.Groups["units"].Value),
            IsCriticalBonus = false
        };
        return true;
    }

    private static bool TryParseCriticalYield(string line, out MiningLogEvent miningEvent)
    {
        miningEvent = default!;
        var match = CriticalOreRegex.Match(line);
        if (!match.Success || !TryParseTimestamp(match.Groups["timestamp"].Value, out var timestampUtc))
        {
            return false;
        }

        miningEvent = new MiningLogEvent
        {
            Kind = MiningLogEventKind.Yield,
            TimestampUtc = timestampUtc,
            OreName = match.Groups["ore"].Value.Trim(),
            Units = ParseUnits(match.Groups["units"].Value),
            IsCriticalBonus = true
        };
        return true;
    }

    private static bool TryParseResidue(string line, out MiningLogEvent miningEvent)
    {
        miningEvent = default!;
        var match = ResidueRegex.Match(line);
        if (!match.Success || !TryParseTimestamp(match.Groups["timestamp"].Value, out var timestampUtc))
        {
            return false;
        }

        miningEvent = new MiningLogEvent
        {
            Kind = MiningLogEventKind.Residue,
            TimestampUtc = timestampUtc,
            Units = ParseUnits(match.Groups["units"].Value)
        };
        return true;
    }

    private static bool TryParseEfficiency(string line, out MiningLogEvent miningEvent)
    {
        miningEvent = default!;
        var match = EfficiencyRegex.Match(line);
        if (!match.Success || !TryParseTimestamp(match.Groups["timestamp"].Value, out var timestampUtc))
        {
            return false;
        }

        miningEvent = new MiningLogEvent
        {
            Kind = MiningLogEventKind.SiteEfficiencyChanged,
            TimestampUtc = timestampUtc,
            OreName = match.Groups["ore"].Value.Trim(),
            EfficiencyPercent = int.Parse(match.Groups["efficiency"].Value, CultureInfo.InvariantCulture)
        };
        return true;
    }

    private static bool TryParseTimestamp(string raw, out DateTime timestampUtc)
    {
        if (!DateTime.TryParseExact(raw, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            timestampUtc = default;
            return false;
        }

        timestampUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    private static long ParseUnits(string raw)
    {
        return long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string NormalizeLine(string line) => line.TrimStart('\uFEFF').Trim();

    [GeneratedRegex(@"^Listener:\s*(?<name>.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BuildHeaderListenerRegex();

    [GeneratedRegex(@"^\[\s*(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]\s*\(mining\).*?You mined .*?<color=#?[0-9A-Fa-f]+>(?<units>\d+)<.*?units of .*?>(?<ore>[^<]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildMinedOreRegex();

    [GeneratedRegex(@"^\[\s*(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]\s*\(mining\).*?Critical mining success!.*?additional .*?>(?<units>\d+)<.*?units of .*?>(?<ore>[^<]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildCriticalOreRegex();

    [GeneratedRegex(@"^\[\s*(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]\s*\(mining\).*?Additional .*?<color=#?[0-9A-Fa-f]+>(?<units>\d+)<.*?units depleted from asteroid as residue$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildResidueRegex();

    [GeneratedRegex(@"^\[\s*(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]\s*\(notify\)\s*(?<ore>.+?) has .*? mined at (?<efficiency>\d+)% efficiency\.$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildEfficiencyRegex();
}
