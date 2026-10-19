using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Abstractions;

public interface IIntelligenceActivityRepository
{
    // Summary-only by default: null systems means all systems; explicit systems include zero
    // observed counts with coverage attached. Ship breakdowns are an independent query.
    // Legacy includeBuckets=true requires exactly one distinct system and delegates to the bounded time series.
    Task<ActivityResult> GetActivityAsync(ActivityRange range, IReadOnlyCollection<long>? systems = null,
        bool includeBuckets = false, CancellationToken cancellationToken = default);
    Task<ActivityResult> GetSystemActivityAsync(long solarSystemId, ActivityRange range,
        CancellationToken cancellationToken = default);
    Task<ActivityResult> GetMapActivityAsync(ActivityRange range, CancellationToken cancellationToken = default);
    Task<ActivityResult> GetTimeSeriesAsync(long solarSystemId, ActivityRange range,
        CancellationToken cancellationToken = default);
    Task<ShipLossActivityResult> GetShipLossesAsync(ActivityRange range, IReadOnlyCollection<long>? systems = null,
        CancellationToken cancellationToken = default);
    Task<RankedActivityResult> GetTopSystemsAsync(ActivityRange range, ActivityRanking ranking,
        int limit, CancellationToken cancellationToken = default);
    Task<int> CatchUpActivityAsync(int maxRows, CancellationToken cancellationToken = default);
    Task<int> DeleteActivityBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default);
    Task StartCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task HeartbeatCollectionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task EndCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
