using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IMiningSessionFeed
{
    event EventHandler<IReadOnlyDictionary<int, MiningCharacterStatsSnapshot>>? SnapshotUpdated;
    IReadOnlyDictionary<int, MiningCharacterStatsSnapshot> Snapshot { get; }
    Task<IReadOnlyDictionary<int, MiningCharacterStatsSnapshot>> GetSnapshotAsync(
        MiningStatsRangeMode rangeMode,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, MiningCharacterStatsSnapshot>> GetRollingSnapshotAsync(
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
