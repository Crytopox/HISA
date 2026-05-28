using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IIncursionStateService
{
    IncursionSnapshot Current { get; }
    event EventHandler<IncursionSnapshot>? IncursionSnapshotUpdated;
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IEsiMetricsStore
{
    event EventHandler<EsiRequestMetric>? MetricAdded;
    void Add(EsiRequestMetric metric);
    IReadOnlyList<EsiRequestMetric> Snapshot();
    EsiRateState CurrentRateState { get; }
    void UpdateRateState(EsiRateState state);
}
