using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.Esi.Telemetry;

public sealed class EsiMetricsStore : IEsiMetricsStore
{
    private readonly List<EsiRequestMetric> _entries = [];
    private readonly object _sync = new();
    private EsiRateState _rateState = new()
    {
        LastRequestAtUtc = DateTimeOffset.MinValue,
        NextAllowedAtUtc = null,
        RequestsLast15Minutes = 0,
        RouteTokenLimit15Minutes = 150
    };

    public event EventHandler<EsiRequestMetric>? MetricAdded;

    public EsiRateState CurrentRateState
    {
        get
        {
            lock (_sync)
            {
                return _rateState;
            }
        }
    }

    public void Add(EsiRequestMetric metric)
    {
        lock (_sync)
        {
            _entries.Add(metric);
            if (_entries.Count > 1000)
            {
                _entries.RemoveAt(0);
            }
        }

        MetricAdded?.Invoke(this, metric);
    }

    public IReadOnlyList<EsiRequestMetric> Snapshot()
    {
        lock (_sync)
        {
            return _entries.ToList();
        }
    }

    public void UpdateRateState(EsiRateState state)
    {
        lock (_sync)
        {
            _rateState = state;
        }
    }
}
