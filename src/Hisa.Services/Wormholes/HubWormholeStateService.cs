using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Wormholes;

public sealed class HubWormholeStateService : IHubWormholeStateService
{
    private readonly IHubWormholeSource _source;
    private readonly ILogger<HubWormholeStateService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public HubWormholeStateService(IHubWormholeSource source, ILogger<HubWormholeStateService> logger)
    {
        _source = source;
        _logger = logger;
    }

    public HubWormholeSnapshot Current { get; private set; } = HubWormholeSnapshot.Empty;
    public event EventHandler<HubWormholeSnapshot>? HubWormholeSnapshotUpdated;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var connections = await _source.GetConnectionsAsync(cancellationToken);
            var bySystem = connections
                .GroupBy(c => c.SolarSystemId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<HubWormholeConnection>)g
                        .GroupBy(x => x.HubType)
                        .Select(x => x.OrderByDescending(i => i.ExpiresAtUtc).First())
                        .ToList());

            Current = new HubWormholeSnapshot
            {
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Source = "eve-scout-api",
                ConnectionsBySystemId = bySystem
            };

            HubWormholeSnapshotUpdated?.Invoke(this, Current);
            _logger.LogInformation("Hub wormhole snapshot updated. Affected systems: {Count}.", bySystem.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
