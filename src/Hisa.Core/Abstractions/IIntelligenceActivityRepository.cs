using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Abstractions;

public interface IIntelligenceActivityRepository
{
    // Null systems means all systems; explicit systems also return zero observed counts with coverage attached.
    Task<ActivityResult> GetActivityAsync(ActivityRange range, IReadOnlyCollection<long>? systems = null,
        bool includeBuckets = false, CancellationToken cancellationToken = default);
    Task<RankedActivityResult> GetTopSystemsAsync(ActivityRange range, ActivityRanking ranking,
        int limit, CancellationToken cancellationToken = default);
    Task<int> CatchUpActivityAsync(int maxRows, CancellationToken cancellationToken = default);
    Task<int> DeleteActivityBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default);
    Task StartCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task HeartbeatCollectionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task EndCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
