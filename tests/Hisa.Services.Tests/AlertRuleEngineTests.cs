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
}
