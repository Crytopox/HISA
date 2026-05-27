using Hisa.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Services.Background;

public sealed class HubWormholeRefreshHostedService : BackgroundService
{
    private readonly IHubWormholeStateService _stateService;
    private readonly ILogger<HubWormholeRefreshHostedService> _logger;
    private readonly StormRefreshOptions _options;

    public HubWormholeRefreshHostedService(
        IHubWormholeStateService stateService,
        IOptions<StormRefreshOptions> options,
        ILogger<HubWormholeRefreshHostedService> logger)
    {
        _stateService = stateService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshMinutes = Math.Clamp(_options.RefreshIntervalMinutes, 15, 30);
        var refreshInterval = TimeSpan.FromMinutes(refreshMinutes);
        _logger.LogInformation("Starting hub wormhole refresh service with interval {Minutes} minutes.", refreshMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _stateService.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hub wormhole refresh failed.");
            }

            try
            {
                await Task.Delay(refreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
