using System.Text.RegularExpressions;
using Hisa.Core.Models;

namespace Hisa.Logs.IntelChatLogs;

public sealed partial class IntelChatMessageParser
{
    private static readonly Regex WordRegex = BuildWordRegex();
    private readonly IReadOnlyDictionary<string, long> _systemIdByName;
    private readonly IReadOnlyDictionary<string, IntelShipClass> _shipClassByName;
    private readonly IReadOnlyDictionary<string, string> _shipAliases;
    private static readonly IReadOnlyDictionary<string, int> CountWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 1,
        ["an"] = 1,
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["both"] = 2
    };

    public IntelChatMessageParser(
        IReadOnlyDictionary<string, long> systemIdByName,
        IReadOnlyDictionary<string, IntelShipClass>? shipClassByName = null,
        IReadOnlyDictionary<string, string>? shipAliases = null)
    {
        _systemIdByName = systemIdByName;
        _shipClassByName = shipClassByName ?? new Dictionary<string, IntelShipClass>(StringComparer.OrdinalIgnoreCase);
        _shipAliases = shipAliases ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public IntelParseResult Parse(string messageText)
    {
        var message = messageText?.Trim() ?? string.Empty;
        var lower = $" {message.ToLowerInvariant()} ";
        var systems = ResolveSystems(message);
        var shipClasses = DetectShipClasses(message, lower);
        var alerts = DetectAlerts(lower);
        var isClear = IsClear(lower);
        if (isClear)
        {
            alerts.Add(IntelAlertType.Clear);
        }

        return new IntelParseResult
        {
            Systems = systems,
            ShipClasses = shipClasses.ToList(),
            Alerts = alerts.ToList(),
            IsClear = isClear
        };
    }

    private List<IntelShipClass> DetectShipClasses(string message, string lower)
    {
        var result = new List<IntelShipClass>();
        var tokens = WordRegex.Matches(message)
            .Select(x => x.Value.Trim())
            .Where(x => x.Length > 0)
            .ToList();
        if (tokens.Count == 0)
        {
            return result;
        }

        var used = new bool[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            var matched = false;
            for (var len = Math.Min(5, tokens.Count - i); len >= 1; len--)
            {
                var phrase = string.Join(" ", tokens.Skip(i).Take(len));
                if (!TryResolveShipClass(phrase, out var shipClass))
                {
                    if (len == 1 && phrase.EndsWith('s'))
                    {
                        _ = TryResolveShipClass(phrase[..^1], out shipClass);
                    }
                }

                if (shipClass == IntelShipClass.Unknown)
                {
                    continue;
                }

                var count = DetectShipCount(tokens, i, len);
                count = Math.Clamp(count, 1, 20);
                for (var c = 0; c < count; c++)
                {
                    result.Add(shipClass);
                }

                for (var j = i; j < i + len; j++)
                {
                    used[j] = true;
                }

                matched = true;
                break;
            }

            if (!matched)
            {
                continue;
            }
        }

        // Fallback for old shorthand-only reports when no concrete ships were recognized.
        if (result.Count == 0)
        {
            DetectLegacyShipClasses(lower, result);
        }

        return result;
    }

    private bool TryResolveShipClass(string phrase, out IntelShipClass shipClass)
    {
        shipClass = IntelShipClass.Unknown;
        var lower = phrase.ToLowerInvariant();
        if (_shipAliases.TryGetValue(lower, out var canonical))
        {
            lower = canonical;
        }

        if (_shipClassByName.TryGetValue(lower, out shipClass))
        {
            return true;
        }

        return false;
    }

    private static int DetectShipCount(IReadOnlyList<string> tokens, int start, int len)
    {
        // Examples: "2 loki", "2x loki", "loki x2", "both capsules"
        var prev = start > 0 ? tokens[start - 1].ToLowerInvariant() : string.Empty;
        var next = (start + len) < tokens.Count ? tokens[start + len].ToLowerInvariant() : string.Empty;

        if (CountWords.TryGetValue(prev, out var wordCount))
        {
            return wordCount;
        }

        if (int.TryParse(prev, out var directCount))
        {
            return directCount;
        }

        if (prev.Length > 1 && (prev.EndsWith('x') || prev.EndsWith('*')) &&
            int.TryParse(prev[..^1], out var prefixedCount))
        {
            return prefixedCount;
        }

        if (next.StartsWith('x') && next.Length > 1 && int.TryParse(next[1..], out var suffixedCount))
        {
            return suffixedCount;
        }

        return 1;
    }

    private static void DetectLegacyShipClasses(string lower, List<IntelShipClass> target)
    {
        AddIf(lower, target, " frig", IntelShipClass.Frigate);
        AddIf(lower, target, " ceptor", IntelShipClass.Frigate);
        AddIf(lower, target, " dessie", IntelShipClass.Destroyer);
        AddIf(lower, target, " destroyer", IntelShipClass.Destroyer);
        AddIf(lower, target, " cruiser", IntelShipClass.Cruiser);
        AddIf(lower, target, " hac", IntelShipClass.Cruiser);
        AddIf(lower, target, " hictor", IntelShipClass.Cruiser);
        AddIf(lower, target, " battlecruiser", IntelShipClass.Battlecruiser);
        AddIf(lower, target, " battleship", IntelShipClass.Battleship);
        AddIf(lower, target, " bs ", IntelShipClass.Battleship);
        AddIf(lower, target, " carrier", IntelShipClass.Capital);
        AddIf(lower, target, " dread", IntelShipClass.Capital);
        AddIf(lower, target, " fax", IntelShipClass.Capital);
        AddIf(lower, target, " capital", IntelShipClass.Capital);
        AddIf(lower, target, " super", IntelShipClass.Supercapital);
        AddIf(lower, target, " titan", IntelShipClass.Titan);
        AddIf(lower, target, " rorq", IntelShipClass.IndustrialCommand);
        AddIf(lower, target, " industrial", IntelShipClass.Industrial);
        AddIf(lower, target, " hauler", IntelShipClass.Industrial);
        AddIf(lower, target, " freighter", IntelShipClass.Freighter);
        AddIf(lower, target, " jf ", IntelShipClass.Freighter);
        AddIf(lower, target, " mining", IntelShipClass.MiningBarge);
        AddIf(lower, target, " barge", IntelShipClass.MiningBarge);
        AddIf(lower, target, " venture", IntelShipClass.MiningFrigate);
        AddIf(lower, target, " pod", IntelShipClass.Capsule);
        AddIf(lower, target, " capsule", IntelShipClass.Capsule);
        AddIf(lower, target, " shuttle", IntelShipClass.Shuttle);
    }

    private HashSet<string> ResolveSystems(string message)
    {
        var systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WordRegex.Matches(message))
        {
            var token = match.Value.Trim();
            if (token.Length < 3)
            {
                continue;
            }

            if (_systemIdByName.ContainsKey(token))
            {
                systems.Add(token);
                continue;
            }

            if (LooksLikeSystemCode(token))
            {
                var matched = _systemIdByName.Keys.FirstOrDefault(x => x.Equals(token, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(matched))
                {
                    systems.Add(matched);
                }
            }
        }

        return systems;
    }

    private static bool LooksLikeSystemCode(string token)
    {
        return token.Contains('-')
            && token.Any(char.IsDigit)
            && token.Length >= 5
            && token.Length <= 8;
    }

    private static HashSet<IntelAlertType> DetectAlerts(string lower)
    {
        var alerts = new HashSet<IntelAlertType>();
        if (lower.Contains(" gate camp ") || lower.Contains(" gatecamp ") || lower.Contains(" camp "))
        {
            alerts.Add(IntelAlertType.GateCamp);
        }
        if (lower.Contains(" cyno ") || lower.Contains(" blops "))
        {
            alerts.Add(IntelAlertType.Cyno);
        }
        if (lower.Contains(" bubble ") || lower.Contains(" bubbles ") || lower.Contains(" bubbled "))
        {
            alerts.Add(IntelAlertType.Bubble);
        }
        if (lower.Contains(" spike "))
        {
            alerts.Add(IntelAlertType.Spike);
        }
        if (lower.Contains(" wormhole ") || lower.Contains(" k162 "))
        {
            alerts.Add(IntelAlertType.Wormhole);
        }
        if (lower.Contains(" fight ") || lower.Contains(" combat ") || lower.Contains(" engaged ") || lower.Contains(" engaging "))
        {
            alerts.Add(IntelAlertType.Fight);
        }

        return alerts;
    }

    private static bool IsClear(string lower)
    {
        return lower.Contains(" clr ")
            || lower.StartsWith("clr ")
            || lower.Contains(" clear ")
            || lower.StartsWith("clear ")
            || lower.EndsWith(" nv ")
            || lower == "nv";
    }

    private static void AddIf(string text, List<IntelShipClass> target, string needle, IntelShipClass value)
    {
        if (text.Contains(needle))
        {
            target.Add(value);
        }
    }

    [GeneratedRegex(@"[A-Za-z0-9'\-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex BuildWordRegex();
}

public sealed class IntelParseResult
{
    public required HashSet<string> Systems { get; init; }
    public required IReadOnlyList<IntelShipClass> ShipClasses { get; init; }
    public required IReadOnlyList<IntelAlertType> Alerts { get; init; }
    public bool IsClear { get; init; }
}
