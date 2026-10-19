using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Abstractions;

public interface ICombatAnalysisRepository
{
    Task<int> ProcessCombatAsync(DateTimeOffset now, TimeSpan retention, CombatDetectionOptions options, CancellationToken cancellationToken = default);
    Task<CombatPage> GetCombatEventsAsync(CombatQuery query, CancellationToken cancellationToken = default);
    Task<CombatEngagement?> GetCombatEventAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CombatSystemSummary>> GetCombatSystemsAsync(ActivityRange range, int limit = 10000, CancellationToken cancellationToken = default);
    Task<CombatMapSnapshot> GetCombatMapAsync(ActivityRange range, CancellationToken cancellationToken = default);
}
