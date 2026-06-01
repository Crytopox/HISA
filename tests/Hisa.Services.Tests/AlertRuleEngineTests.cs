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
}
