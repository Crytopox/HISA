using Hisa.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Services.Background;

public sealed class StormRefreshHostedService : BackgroundService
{
    private readonly IStormStateService _stormStateService;
    private readonly ILogger<StormRefreshHostedService> _logger;
    private readonly StormRefreshOptions _options;

    public StormRefreshHostedService(
        IStormStateService stormStateService,
        IOptions<StormRefreshOptions> options,
        ILogger<StormRefreshHostedService> logger)
    {
        _stormStateService = stormStateService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshMinutes = Math.Clamp(_options.RefreshIntervalMinutes, 15, 30);
        var refreshInterval = TimeSpan.FromMinutes(refreshMinutes);

        _logger.LogInformation("Starting storm refresh service with interval {Minutes} minutes.", refreshMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _stormStateService.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Storm refresh failed.");
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

public sealed class StormRefreshOptions
{
    public int RefreshIntervalMinutes { get; set; } = 20;
}
