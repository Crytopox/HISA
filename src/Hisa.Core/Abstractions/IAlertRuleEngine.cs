using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IAlertRuleEngine
{
    IReadOnlyList<AlertTriggered> Evaluate(AlertEvaluationRequest request);
}
