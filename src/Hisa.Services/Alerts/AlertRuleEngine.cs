using Hisa.Core.Abstractions;
using Hisa.Core.Models;

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
        var result = new List<AlertTriggered>();

        foreach (var rule in request.Rules)
        {
            if (!rule.Enabled || rule.EventType != source.EventType)
            {
                continue;
            }

            if (source.EventType == AlertEventType.IntelReport &&
                source.IsClearIntelReport &&
                !rule.ShowClearIntelReports)
            {
                continue;
            }

            if (!MatchesScope(rule, source, graph, request))
            {
                continue;
            }

            var dedupe = BuildAlertKey(rule, source);
            if (!TryMarkAlertEmitted(dedupe, now))
            {
                continue;
            }

            var actions = new List<AlertActionType> { AlertActionType.ShowPopup };
            var wantsSound = rule.Actions.Contains(AlertActionType.PlaySound);
            if (wantsSound)
            {
                if (!IsCoolingDown(dedupe, now))
                {
                    actions.Add(AlertActionType.PlaySound);
                    SetCooldown(dedupe, now, Math.Max(0, rule.CooldownSeconds));
                }
            }
            result.Add(new AlertTriggered
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                SourceEvent = source,
                TriggeredAtUtc = now,
                Actions = actions,
                SoundFile = string.IsNullOrWhiteSpace(rule.SoundFile) ? "default-alert.wav" : rule.SoundFile,
                SoundVolume = Math.Clamp(rule.SoundVolume, 0.0, 1.0)
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
