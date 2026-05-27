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

public interface IHubWormholeSource
{
    Task<IReadOnlyList<HubWormholeConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default);
}

public interface IHubWormholeStateService
{
    HubWormholeSnapshot Current { get; }
    event EventHandler<HubWormholeSnapshot>? HubWormholeSnapshotUpdated;
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
