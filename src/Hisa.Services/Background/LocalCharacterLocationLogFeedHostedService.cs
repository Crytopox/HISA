using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Logs.LocalChatLogs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Background;

public sealed class LocalCharacterLocationLogFeedHostedService : BackgroundService, ILocalCharacterLocationFeed
{
    private const string LogsRootSettingsKey = "Tracking.LogsRootPath";
    private const string RecentSessionLookbackHoursSettingsKey = "Tracking.LocalChatRecentSessionLookbackHours";
    private readonly ISettingsService _settingsService;
    private readonly ILogger<LocalCharacterLocationLogFeedHostedService> _logger;
    private readonly LocalChatLogTracker _tracker = new();

    public LocalCharacterLocationLogFeedHostedService(
        ISettingsService settingsService,
        ILogger<LocalCharacterLocationLogFeedHostedService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        _tracker.SystemChanged += (_, change) => SystemChanged?.Invoke(this, change);
    }

    public event EventHandler<LocalCharacterSystemChange>? SystemChanged;

    public IReadOnlyDictionary<int, LocalCharacterSystemChange> Snapshot => _tracker.Snapshot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var chatLogsDirectory = await ResolveChatLogsDirectoryAsync(stoppingToken);
        if (chatLogsDirectory is null)
        {
            _logger.LogWarning("Local character location tracking disabled: ChatLogs directory was not found.");
            return;
        }

        _logger.LogInformation("Starting local character location tracking from: {Path}", chatLogsDirectory);
        var lookback = await ResolveInitialScanLookbackAsync(stoppingToken);
        _logger.LogInformation("Local character location startup scan lookback: {Hours} hour(s).", lookback.TotalHours);
        await _tracker.StartAsync(chatLogsDirectory, lookback, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _tracker.StopAsync();
        }
    }

    public override void Dispose()
    {
        _tracker.Dispose();
        base.Dispose();
    }

    private async Task<string?> ResolveChatLogsDirectoryAsync(CancellationToken cancellationToken)
    {
        var configuredRoot = await _settingsService.GetAsync<string>(LogsRootSettingsKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredValidation = LocalChatLogsPathValidator.Validate(configuredRoot);
            if (configuredValidation.IsValid && configuredValidation.ChatLogsPath is not null)
            {
                return configuredValidation.ChatLogsPath;
            }
        }

        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EVE",
            "logs");
        var defaultValidation = LocalChatLogsPathValidator.Validate(defaultRoot);
        if (defaultValidation.IsValid && defaultValidation.ChatLogsPath is not null)
        {
            return defaultValidation.ChatLogsPath;
        }

        return null;
    }

    private async Task<TimeSpan> ResolveInitialScanLookbackAsync(CancellationToken cancellationToken)
    {
        var configured = await _settingsService.GetAsync<int?>(RecentSessionLookbackHoursSettingsKey, cancellationToken);
        if (configured is null)
        {
            return TimeSpan.FromHours(24);
        }

        var clampedHours = Math.Clamp(configured.Value, 1, 168);
        return TimeSpan.FromHours(clampedHours);
    }
}
