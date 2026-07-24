using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using System.Text.RegularExpressions;

namespace Hisa.Services.Alerts;

public sealed class AlertRuleEngine : IAlertRuleEngine
{
    private static readonly TimeSpan EmittedAlertRetention = TimeSpan.FromDays(1);
    private readonly IRouteDistanceService _routeDistanceService;
    private readonly object _cooldownGate = new();
    private readonly Dictionary<string, DateTime> _cooldownUntilByKey = [];
    private readonly object _emittedAlertGate = new();
    private readonly Dictionary<string, DateTime> _emittedAlertAtByKey = [];

    public AlertRuleEngine(IRouteDistanceService routeDistanceService)
    {
        _routeDistanceService = routeDistanceService;
    }

    public IReadOnlyList<AlertTriggered> Evaluate(AlertEvaluationRequest request)
    {
        var source = request.SourceEvent;
        var graph = request.Graph;
        var now = DateTime.UtcNow;
        // A source event should produce one coherent alert. Rules are intentionally
        // ordered: when two matching rules have the same specificity, the first rule
        // in the user's list wins.
        var matchingRules = new List<(AlertRule Rule, int Index)>();

        for (var index = 0; index < request.Rules.Count; index++)
        {
            var rule = request.Rules[index];
            if (!rule.Enabled || !MatchesEventType(rule, source))
            {
                continue;
            }

            if ((rule.EventType == AlertEventType.IntelReport || rule.EventType == AlertEventType.IntelTextMatch) &&
                source.IsClearIntelReport &&
                !rule.ShowClearIntelReports)
            {
                continue;
            }

            if (!MatchesScope(rule, source, graph, request))
            {
                continue;
            }

            matchingRules.Add((rule, index));
        }

        var winner = SelectWinningRule(matchingRules);
        if (winner is null)
        {
            return [];
        }

        var winningRule = winner.Value.Rule;
        var dedupe = BuildAlertKey(winningRule, source);
        if (!TryMarkAlertEmitted(dedupe, now))
        {
            return [];
        }

        var actions = new List<AlertActionType> { AlertActionType.ShowPopup };
        var wantsSound = winningRule.Actions.Contains(AlertActionType.PlaySound);
        if (wantsSound)
        {
            if (!IsCoolingDown(dedupe, now))
            {
                actions.Add(AlertActionType.PlaySound);
                SetCooldown(dedupe, now, Math.Max(0, winningRule.CooldownSeconds));
            }
        }

        return
        [
            new AlertTriggered
            {
                RuleId = winningRule.Id,
                RuleName = winningRule.Name,
                SourceEvent = source,
                TriggeredAtUtc = now,
                Actions = actions,
                SoundFile = string.IsNullOrWhiteSpace(winningRule.SoundFile) ? "default-alert.wav" : winningRule.SoundFile,
                SoundVolume = Math.Clamp(winningRule.SoundVolume, 0.0, 1.0)
            }
        ];
    }

    private static (AlertRule Rule, int Index)? SelectWinningRule(IReadOnlyList<(AlertRule Rule, int Index)> matchingRules)
    {
        if (matchingRules.Count == 0)
        {
            return null;
        }

        return matchingRules
            .OrderBy(x => x.Rule.EventType == AlertEventType.IntelTextMatch ? 0 : 1)
            .ThenBy(x => GetDistanceSpecificity(x.Rule.DistanceMode))
            .ThenBy(x => x.Rule.DistanceMode == AlertDistanceMode.MaxJumps ? Math.Max(0, x.Rule.MaxJumps) : int.MaxValue)
            .ThenBy(x => x.Index)
            .First();
    }

    private static int GetDistanceSpecificity(AlertDistanceMode distanceMode) => distanceMode switch
    {
        AlertDistanceMode.MaxJumps => 0,
        AlertDistanceMode.CurrentRegion => 1,
        _ => 2
    };

    private static bool MatchesEventType(AlertRule rule, AlertSourceEvent source)
    {
        if (rule.EventType == source.EventType)
        {
            return rule.EventType != AlertEventType.IntelTextMatch || MatchesTextPattern(rule, source.Summary);
        }

        // Text-match rules participate in the same decision as normal Intel rules,
        // so a matching phrase can provide one distinct, more-specific alert.
        return source.EventType == AlertEventType.IntelReport &&
               rule.EventType == AlertEventType.IntelTextMatch &&
               MatchesTextPattern(rule, source.Summary);
    }

    private static bool MatchesTextPattern(AlertRule rule, string text)
    {
        var pattern = rule.TextPattern?.Trim();
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!rule.UseRegex)
        {
            return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private bool MatchesScope(AlertRule rule, AlertSourceEvent source, MapGraph? graph, AlertEvaluationRequest request)
    {
        if (rule.ScopeMode == AlertLocationScopeMode.Global)
        {
            return true;
        }

        if (rule.ScopeMode == AlertLocationScopeMode.SelectedRegions)
        {
            var sourceRegionId = source.RegionId
                ?? graph?.Nodes.FirstOrDefault(node => node.Id == source.SolarSystemId)?.RegionId;
            return sourceRegionId is not null && rule.RegionIds.Contains(sourceRegionId.Value);
        }

        if (graph is null || graph.Nodes.Count == 0 || source.SolarSystemId <= 0)
        {
            return false;
        }

        var characterLocations = request.CharacterLocationsByCharacterId;
        if (characterLocations.Count == 0)
        {
            return false;
        }

        var relevantSources = rule.ScopeMode switch
        {
            AlertLocationScopeMode.AnyTrackedCharacter => characterLocations.Values.Where(id => id > 0).Distinct().ToList(),
            AlertLocationScopeMode.SpecificCharacters => rule.CharacterIds
                .Where(characterLocations.ContainsKey)
                .Select(characterId => characterLocations[characterId])
                .Where(id => id > 0)
                .Distinct()
                .ToList(),
            _ => []
        };

        if (relevantSources.Count == 0)
        {
            return false;
        }

        if (rule.DistanceMode == AlertDistanceMode.Any)
        {
            return true;
        }

        if (rule.DistanceMode == AlertDistanceMode.CurrentRegion)
        {
            var nodeById = graph.Nodes.ToDictionary(n => n.Id);
            if (!nodeById.TryGetValue(source.SolarSystemId, out var target) || target.RegionId is null)
            {
                return false;
            }

            foreach (var sourceSystemId in relevantSources)
            {
                if (!nodeById.TryGetValue(sourceSystemId, out var sourceNode) || sourceNode.RegionId is null)
                {
                    continue;
                }

                if (sourceNode.RegionId == target.RegionId)
                {
                    return true;
                }
            }

            return false;
        }

        var distanceMap = _routeDistanceService.ComputeDistances(new RoutingDistancesRequest
        {
            Graph = graph,
            SourceSystemIds = relevantSources,
            CostMode = RoutingCostMode.HopCount,
            IncludeAnsiblexLinks = rule.IncludeAnsiblexLinks,
            AnsiblexLinks = rule.IncludeAnsiblexLinks ? request.AnsiblexLinks : [],
            AnsiblexCostMultiplier = request.AnsiblexCostMultiplier
        });
        if (!distanceMap.TryGetValue(source.SolarSystemId, out var distance))
        {
            return false;
        }

        return distance <= Math.Max(0, rule.MaxJumps);
    }

    private static string BuildAlertKey(AlertRule rule, AlertSourceEvent source)
    {
        var dedupe = !string.IsNullOrWhiteSpace(source.DedupeKey)
            ? source.DedupeKey
            : (source.KillmailId?.ToString() ?? $"{source.SolarSystemId}:{source.TimestampUtc:O}");
        return $"{rule.Id}:{source.EventType}:{dedupe}";
    }

    private bool TryMarkAlertEmitted(string key, DateTime now)
    {
        lock (_emittedAlertGate)
        {
            var cutoff = now - EmittedAlertRetention;
            foreach (var expiredKey in _emittedAlertAtByKey
                         .Where(x => x.Value < cutoff)
                         .Select(x => x.Key)
                         .ToList())
            {
                _emittedAlertAtByKey.Remove(expiredKey);
            }

            if (_emittedAlertAtByKey.ContainsKey(key))
            {
                return false;
            }

            _emittedAlertAtByKey[key] = now;
            return true;
        }
    }

    private bool IsCoolingDown(string key, DateTime now)
    {
        lock (_cooldownGate)
        {
            return _cooldownUntilByKey.TryGetValue(key, out var until) && now < until;
        }
    }

    private void SetCooldown(string key, DateTime now, int cooldownSeconds)
    {
        lock (_cooldownGate)
        {
            _cooldownUntilByKey[key] = now.AddSeconds(cooldownSeconds);
        }
    }
}
