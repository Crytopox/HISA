using System.Text.RegularExpressions;
using Hisa.Core.Models;

namespace Hisa.Logs.IntelChatLogs;

public sealed partial class IntelChatMessageParser
{
    private static readonly Regex WordRegex = BuildWordRegex();
    private readonly IReadOnlyDictionary<string, long> _systemIdByName;

    public IntelChatMessageParser(IReadOnlyDictionary<string, long> systemIdByName)
    {
        _systemIdByName = systemIdByName;
    }

    public IntelParseResult Parse(string messageText)
    {
        var message = messageText?.Trim() ?? string.Empty;
        var lower = $" {message.ToLowerInvariant()} ";
        var systems = ResolveSystems(message);
        var shipClasses = DetectShipClasses(lower);
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

    private static HashSet<IntelShipClass> DetectShipClasses(string lower)
    {
        var result = new HashSet<IntelShipClass>();
        AddIf(lower, result, " frig", IntelShipClass.Frigate);
        AddIf(lower, result, " ceptor", IntelShipClass.Frigate);
        AddIf(lower, result, " dessie", IntelShipClass.Destroyer);
        AddIf(lower, result, " destroyer", IntelShipClass.Destroyer);
        AddIf(lower, result, " cruiser", IntelShipClass.Cruiser);
        AddIf(lower, result, " hac", IntelShipClass.Cruiser);
        AddIf(lower, result, " hictor", IntelShipClass.Cruiser);
        AddIf(lower, result, " battlecruiser", IntelShipClass.Battlecruiser);
        AddIf(lower, result, " battleship", IntelShipClass.Battleship);
        AddIf(lower, result, " bs ", IntelShipClass.Battleship);
        AddIf(lower, result, " carrier", IntelShipClass.Capital);
        AddIf(lower, result, " dread", IntelShipClass.Capital);
        AddIf(lower, result, " fax", IntelShipClass.Capital);
        AddIf(lower, result, " capital", IntelShipClass.Capital);
        AddIf(lower, result, " super", IntelShipClass.Supercapital);
        AddIf(lower, result, " titan", IntelShipClass.Titan);
        AddIf(lower, result, " rorq", IntelShipClass.IndustrialCommand);
        AddIf(lower, result, " industrial", IntelShipClass.Industrial);
        AddIf(lower, result, " hauler", IntelShipClass.Industrial);
        AddIf(lower, result, " freighter", IntelShipClass.Freighter);
        AddIf(lower, result, " jf ", IntelShipClass.Freighter);
        AddIf(lower, result, " mining", IntelShipClass.MiningBarge);
        AddIf(lower, result, " barge", IntelShipClass.MiningBarge);
        AddIf(lower, result, " venture", IntelShipClass.MiningFrigate);
        AddIf(lower, result, " pod", IntelShipClass.Capsule);
        AddIf(lower, result, " capsule", IntelShipClass.Capsule);
        AddIf(lower, result, " shuttle", IntelShipClass.Shuttle);
        return result;
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

    private static void AddIf(string text, HashSet<IntelShipClass> target, string needle, IntelShipClass value)
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
