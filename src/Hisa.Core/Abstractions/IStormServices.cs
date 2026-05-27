using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IStormCenterSource
{
    Task<IReadOnlyList<StormCenter>> GetStormCentersAsync(CancellationToken cancellationToken = default);
}

public interface IStormStateService
{
    StormSnapshot Current { get; }
    event EventHandler<StormSnapshot>? StormSnapshotUpdated;
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
