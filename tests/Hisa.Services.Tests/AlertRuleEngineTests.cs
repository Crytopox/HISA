using Hisa.Core.Models;
using Hisa.Services.Alerts;
using Hisa.Services.Routing;

namespace Hisa.Services.Tests;

public sealed class AlertRuleEngineTests
{
    [Fact]
    public void Evaluate_ClearIntelReport_IsSuppressedByDefault()
    {
        var result = EvaluateClearIntelReport(showClearIntelReports: false);

        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_ClearIntelReport_IsIncludedWhenRuleOptsIn()
    {
        var result = EvaluateClearIntelReport(showClearIntelReports: true);

        Assert.Single(result);
    }

    [Fact]
    public void Evaluate_DuplicateIntelAlert_IsOnlyEmittedOnce()
    {
        var engine = new AlertRuleEngine(new DijkstraRouteDistanceService());
        var request = CreateIntelRequest("intel:30000142:2026-06-02T12:00:00.0000000Z:Intel:Scout:GPLB-C Crytopox Haakario nv");

        var first = engine.Evaluate(request);
        var duplicate = engine.Evaluate(request);

        Assert.Single(first);
        Assert.Empty(duplicate);
    }

    [Fact]
    public void Evaluate_DistinctIntelAlerts_AreBothEmitted()
    {
        var engine = new AlertRuleEngine(new DijkstraRouteDistanceService());

        var first = engine.Evaluate(CreateIntelRequest("intel:30000142:2026-06-02T12:00:00.0000000Z:Intel:Scout:GPLB-C Crytopox Haakario nv"));
        var second = engine.Evaluate(CreateIntelRequest("intel:30000142:2026-06-02T12:00:05.0000000Z:Intel:Scout:GPLB-C Crytopox Haakario nv"));

        Assert.Single(first);
        Assert.Single(second);
    }

    [Fact]
    public void Evaluate_OverlappingJumpRules_UsesTheTightestMatchingRule()
    {
        var result = new AlertRuleEngine(new DijkstraRouteDistanceService()).Evaluate(CreateOverlappingJumpRulesRequest());

        var triggered = Assert.Single(result);
        Assert.Equal("one-jump", triggered.RuleId);
    }

    [Fact]
    public void Evaluate_EquallySpecificRules_UsesListOrder()
    {
        var request = CreateOverlappingJumpRulesRequest();
        request = new AlertEvaluationRequest
        {
            Rules =
            [
                new AlertRule { Id = "first", Name = "First", ScopeMode = AlertLocationScopeMode.AnyTrackedCharacter, DistanceMode = AlertDistanceMode.MaxJumps, MaxJumps = 5 },
                new AlertRule { Id = "second", Name = "Second", ScopeMode = AlertLocationScopeMode.AnyTrackedCharacter, DistanceMode = AlertDistanceMode.MaxJumps, MaxJumps = 5 }
            ],
            SourceEvent = request.SourceEvent,
            Graph = request.Graph,
            CharacterLocationsByCharacterId = request.CharacterLocationsByCharacterId
        };

        var result = new AlertRuleEngine(new DijkstraRouteDistanceService()).Evaluate(request);

        var triggered = Assert.Single(result);
        Assert.Equal("first", triggered.RuleId);
    }

    [Fact]
    public void Evaluate_IntelTextMatch_OverridesGeneralIntelRule()
    {
        var result = new AlertRuleEngine(new DijkstraRouteDistanceService()).Evaluate(new AlertEvaluationRequest
        {
            Rules =
            [
                new AlertRule { Id = "general", Name = "General", EventType = AlertEventType.IntelReport },
                new AlertRule { Id = "phrase", Name = "Phrase", EventType = AlertEventType.IntelTextMatch, TextPattern = "capsuleer spotted" }
            ],
            SourceEvent = new AlertSourceEvent
            {
                EventType = AlertEventType.IntelReport,
                TimestampUtc = DateTime.UtcNow,
                SolarSystemId = 42,
                Summary = "CAPSULEER spotted near the gate"
            },
            Graph = null,
            CharacterLocationsByCharacterId = new Dictionary<int, long>()
        });

        Assert.Equal("phrase", Assert.Single(result).RuleId);
    }

    [Fact]
    public void Evaluate_IntelTextMatch_InvalidRegexDoesNotTrigger()
    {
        var result = new AlertRuleEngine(new DijkstraRouteDistanceService()).Evaluate(new AlertEvaluationRequest
        {
            Rules = [new AlertRule { Id = "regex", Name = "Regex", EventType = AlertEventType.IntelTextMatch, TextPattern = "[", UseRegex = true }],
            SourceEvent = new AlertSourceEvent { EventType = AlertEventType.IntelReport, TimestampUtc = DateTime.UtcNow, SolarSystemId = 42, Summary = "anything" },
            Graph = null,
            CharacterLocationsByCharacterId = new Dictionary<int, long>()
        });

        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_SelectedRegions_OnlyMatchesConfiguredRegions()
    {
        var rule = new AlertRule
        {
            Id = "regions",
            Name = "Regions",
            EventType = AlertEventType.StormSpawn,
            ScopeMode = AlertLocationScopeMode.SelectedRegions,
            RegionIds = [10000002, 10000043]
        };
        var engine = new AlertRuleEngine(new DijkstraRouteDistanceService());

        var matching = engine.Evaluate(new AlertEvaluationRequest
        {
            Rules = [rule],
            SourceEvent = new AlertSourceEvent { EventType = AlertEventType.StormSpawn, TimestampUtc = DateTime.UtcNow, SolarSystemId = 1, RegionId = 10000043 },
            Graph = null,
            CharacterLocationsByCharacterId = new Dictionary<int, long>()
        });
        var nonMatching = engine.Evaluate(new AlertEvaluationRequest
        {
            Rules = [rule],
            SourceEvent = new AlertSourceEvent { EventType = AlertEventType.StormSpawn, TimestampUtc = DateTime.UtcNow, SolarSystemId = 2, RegionId = 10000069 },
            Graph = null,
            CharacterLocationsByCharacterId = new Dictionary<int, long>()
        });

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    private static IReadOnlyList<AlertTriggered> EvaluateClearIntelReport(bool showClearIntelReports)
    {
        var engine = new AlertRuleEngine(new DijkstraRouteDistanceService());
        return engine.Evaluate(new AlertEvaluationRequest
        {
            Rules =
            [
                new AlertRule
                {
                    Id = "intel-rule",
                    Name = "Intel",
                    EventType = AlertEventType.IntelReport,
                    ShowClearIntelReports = showClearIntelReports
                }
            ],
            SourceEvent = new AlertSourceEvent
            {
                EventType = AlertEventType.IntelReport,
                TimestampUtc = DateTime.UtcNow,
                SolarSystemId = 30000142,
                IsClearIntelReport = true
            },
            Graph = null,
            CharacterLocationsByCharacterId = new Dictionary<int, long>()
        });
    }

    private static AlertEvaluationRequest CreateIntelRequest(string dedupeKey)
    {
        return new AlertEvaluationRequest
        {
            Rules =
            [
                new AlertRule
                {
                    Id = "intel-rule",
                    Name = "Intel",
                    EventType = AlertEventType.IntelReport
                }
            ],
            SourceEvent = new AlertSourceEvent
            {
                EventType = AlertEventType.IntelReport,
                TimestampUtc = DateTime.UtcNow,
                SolarSystemId = 30000142,
                DedupeKey = dedupeKey
            },
            Graph = null,
            CharacterLocationsByCharacterId = new Dictionary<int, long>()
        };
    }

    private static AlertEvaluationRequest CreateOverlappingJumpRulesRequest()
    {
        var graph = new MapGraph
        {
            Nodes =
            [
                new MapNode { Id = 1, Name = "Home", X = 0, Y = 0 },
                new MapNode { Id = 2, Name = "One jump", X = 1, Y = 0 }
            ],
            Links = [new MapLink { FromId = 1, ToId = 2 }]
        };

        return new AlertEvaluationRequest
        {
            Rules =
            [
                new AlertRule { Id = "five-jump", Name = "Five", ScopeMode = AlertLocationScopeMode.AnyTrackedCharacter, DistanceMode = AlertDistanceMode.MaxJumps, MaxJumps = 5 },
                new AlertRule { Id = "one-jump", Name = "One", ScopeMode = AlertLocationScopeMode.AnyTrackedCharacter, DistanceMode = AlertDistanceMode.MaxJumps, MaxJumps = 1 }
            ],
            SourceEvent = new AlertSourceEvent { EventType = AlertEventType.IntelReport, TimestampUtc = DateTime.UtcNow, SolarSystemId = 2 },
            Graph = graph,
            CharacterLocationsByCharacterId = new Dictionary<int, long> { [7] = 1 }
        };
    }
}
