using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.Services.Alerts;

public sealed class AlertRuleEngine : IAlertRuleEngine
{
    private readonly IRouteDistanceService _routeDistanceService;
    private readonly object _cooldownGate = new();
    private readonly Dictionary<string, DateTime> _cooldownUntilByKey = [];

    public AlertRuleEngine(IRouteDistanceService routeDistanceService)
    {
        _routeDistanceService = routeDistanceService;
    }

    public IReadOnlyList<AlertTriggered> Evaluate(AlertEvaluationRequest request)
    {
        var source = request.SourceEvent;
        var graph = request.Graph;
        var now = DateTime.UtcNow;
        var result = new List<AlertTriggered>();

        foreach (var rule in request.Rules)
        {
            if (!rule.Enabled || rule.EventType != source.EventType)
            {
                continue;
            }

            if (!MatchesScope(rule, source, graph, request))
            {
                continue;
            }

            var dedupe = BuildCooldownKey(rule, source);
            if (IsCoolingDown(dedupe, now))
            {
                continue;
            }

            SetCooldown(dedupe, now, Math.Max(0, rule.CooldownSeconds));
            result.Add(new AlertTriggered
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                SourceEvent = source,
                TriggeredAtUtc = now,
                Actions = rule.Actions
            });
        }

        return result;
    }

    private bool MatchesScope(AlertRule rule, AlertSourceEvent source, MapGraph? graph, AlertEvaluationRequest request)
    {
        if (rule.ScopeMode == AlertLocationScopeMode.Global)
        {
            return true;
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

    private static string BuildCooldownKey(AlertRule rule, AlertSourceEvent source)
    {
        var dedupe = !string.IsNullOrWhiteSpace(source.DedupeKey)
            ? source.DedupeKey
            : (source.KillmailId?.ToString() ?? $"{source.SolarSystemId}:{source.TimestampUtc:yyyyMMddHHmm}");
        return $"{rule.Id}:{source.EventType}:{dedupe}";
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
