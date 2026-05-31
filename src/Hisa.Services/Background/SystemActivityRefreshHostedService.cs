using Hisa.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Services.Background;

public sealed class SystemActivityRefreshHostedService : BackgroundService
{
    private readonly ISystemActivityStateService _systemActivityStateService;
    private readonly ILogger<SystemActivityRefreshHostedService> _logger;
    private readonly SystemActivityRefreshOptions _options;

    public SystemActivityRefreshHostedService(
        ISystemActivityStateService systemActivityStateService,
        IOptions<SystemActivityRefreshOptions> options,
        ILogger<SystemActivityRefreshHostedService> logger)
    {
        _systemActivityStateService = systemActivityStateService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshMinutes = Math.Max(60, _options.RefreshIntervalMinutes);
        var refreshInterval = TimeSpan.FromMinutes(refreshMinutes);

        _logger.LogInformation("Starting system activity refresh service with interval {Minutes} minutes.", refreshMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _systemActivityStateService.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "System activity refresh failed.");
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

public sealed class SystemActivityRefreshOptions
{
    public int RefreshIntervalMinutes { get; set; } = 60;
}
