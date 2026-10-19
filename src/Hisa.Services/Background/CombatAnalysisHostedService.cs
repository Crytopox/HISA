using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Hisa.Services.Intelligence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Services.Background;

public sealed class CombatAnalysisHostedService(ICombatAnalysisRepository analysis, IIntelligenceHistoryRepository history,
    IOptions<IntelligencePersistenceOptions> persistence, IOptions<CombatDetectionOptions> detection,
    ILogger<CombatAnalysisHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await history.InitializeAsync(stoppingToken);
                var count = await analysis.ProcessCombatAsync(DateTimeOffset.UtcNow,
                    TimeSpan.FromTicks(Math.Min(TimeSpan.FromDays(30).Ticks, persistence.Value.AggregateRetentionPeriod.Ticks)), detection.Value, stoppingToken);
                await Task.Delay(count > 0 ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Local combat analysis deferred; ingestion remains active.");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
