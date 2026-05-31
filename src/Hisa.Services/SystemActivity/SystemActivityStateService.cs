using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Esi.Clients;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.SystemActivity;

public sealed class SystemActivityStateService : ISystemActivityStateService
{
    private readonly IEsiPublicClient _esiPublicClient;
    private readonly ILogger<SystemActivityStateService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public SystemActivityStateService(IEsiPublicClient esiPublicClient, ILogger<SystemActivityStateService> logger)
    {
        _esiPublicClient = esiPublicClient;
        _logger = logger;
    }

    public SystemActivitySnapshot Current { get; private set; } = SystemActivitySnapshot.Empty;
    public event EventHandler<SystemActivitySnapshot>? SystemActivitySnapshotUpdated;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var kills = await _esiPublicClient.GetSystemKillsAsync(cancellationToken);
            var jumps = await _esiPublicClient.GetSystemJumpsAsync(cancellationToken);

            Current = new SystemActivitySnapshot
            {
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Source = "esi-system-activity",
                KillsBySystemId = kills,
                JumpsBySystemId = jumps
            };

            SystemActivitySnapshotUpdated?.Invoke(this, Current);
            _logger.LogInformation(
                "System activity snapshot updated. Kills systems: {KillsCount}, jumps systems: {JumpsCount}.",
                kills.Count,
                jumps.Count);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
