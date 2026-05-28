using Hisa.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Services.Background;

public sealed class IncursionRefreshHostedService : BackgroundService
{
    private readonly IIncursionStateService _incursionStateService;
    private readonly ILogger<IncursionRefreshHostedService> _logger;
    private readonly IncursionRefreshOptions _options;

    public IncursionRefreshHostedService(
        IIncursionStateService incursionStateService,
        IOptions<IncursionRefreshOptions> options,
        ILogger<IncursionRefreshHostedService> logger)
    {
        _incursionStateService = incursionStateService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshMinutes = Math.Max(5, _options.RefreshIntervalMinutes);
        var refreshInterval = TimeSpan.FromMinutes(refreshMinutes);

        _logger.LogInformation("Starting incursion refresh service with interval {Minutes} minutes.", refreshMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _incursionStateService.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Incursion refresh failed.");
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

public sealed class IncursionRefreshOptions
{
    public int RefreshIntervalMinutes { get; set; } = 5;
}
