using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Esi.Clients;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Incursions;

public sealed class IncursionStateService : IIncursionStateService
{
    private readonly IEsiPublicClient _esiPublicClient;
    private readonly ILogger<IncursionStateService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public IncursionStateService(IEsiPublicClient esiPublicClient, ILogger<IncursionStateService> logger)
    {
        _esiPublicClient = esiPublicClient;
        _logger = logger;
    }

    public IncursionSnapshot Current { get; private set; } = IncursionSnapshot.Empty;
    public event EventHandler<IncursionSnapshot>? IncursionSnapshotUpdated;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var incursions = await _esiPublicClient.GetIncursionsAsync(cancellationToken);
            var systems = incursions
                .SelectMany(i => i.InfestedSolarSystems.Append(i.StagingSolarSystemId))
                .ToHashSet();

            Current = new IncursionSnapshot
            {
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Source = "esi-incursions",
                Incursions = incursions,
                ActiveSystemIds = systems
            };

            IncursionSnapshotUpdated?.Invoke(this, Current);
            _logger.LogInformation("Incursion snapshot updated. Incursions: {Count}, affected systems: {SystemCount}.", incursions.Count, systems.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
