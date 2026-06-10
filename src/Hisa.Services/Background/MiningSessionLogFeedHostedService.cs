using System.Net.Http.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Logs.GameLogs;
using Hisa.Logs.LocalChatLogs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Background;

public sealed class MiningSessionLogFeedHostedService : BackgroundService, IMiningSessionFeed
{
    private const string LogsRootSettingsKey = "Tracking.LogsRootPath";
    private const string MiningEnabledSettingsKey = "Mining.Enabled";
    private const string RecentSessionLookbackHoursSettingsKey = "Mining.RecentSessionLookbackHours";
    private const string MiningRefineYieldPercentSettingsKey = "Mining.RefineYieldPercent";
    private static readonly TimeSpan PriceCacheTtl = TimeSpan.FromMinutes(20);

    private readonly ISettingsService _settingsService;
    private readonly ISdeDatabase _sdeDatabase;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MiningSessionLogFeedHostedService> _logger;
    private readonly GameLogMiningTracker _tracker = new();
    private readonly object _gate = new();
    private readonly Dictionary<int, MiningCharacterStatsSnapshot> _snapshotByCharacterId = [];
    private Dictionary<string, OreReferenceValue> _oreValuesByName = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _oreValuesFetchedAtUtc = DateTime.MinValue;
    private bool _enabled;
    private string? _gameLogsDirectory;
    private decimal _refineYieldFactor = 0.9063m;

    public MiningSessionLogFeedHostedService(
        ISettingsService settingsService,
        ISdeDatabase sdeDatabase,
        IHttpClientFactory httpClientFactory,
        ILogger<MiningSessionLogFeedHostedService> logger)
    {
        _settingsService = settingsService;
        _sdeDatabase = sdeDatabase;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _tracker.SnapshotUpdated += OnTrackerSnapshotUpdated;
    }

    public event EventHandler<IReadOnlyDictionary<int, MiningCharacterStatsSnapshot>>? SnapshotUpdated;

    public IReadOnlyDictionary<int, MiningCharacterStatsSnapshot> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, MiningCharacterStatsSnapshot>(_snapshotByCharacterId);
            }
        }
    }

    public async Task<IReadOnlyDictionary<int, MiningCharacterStatsSnapshot>> GetSnapshotAsync(
        MiningStatsRangeMode rangeMode,
        CancellationToken cancellationToken = default)
    {
        await EnsureOreValuesFreshAsync(cancellationToken);

        if (rangeMode == MiningStatsRangeMode.CurrentSession)
        {
            return _tracker.Snapshot.ToDictionary(
                kvp => kvp.Key,
                kvp => BuildCharacterSnapshot(kvp.Value, kvp.Value.SessionStartedUtc));
        }

        var gameLogsDirectory = _gameLogsDirectory ?? await ResolveGameLogsDirectoryAsync(cancellationToken);
        if (gameLogsDirectory is null)
        {
            return new Dictionary<int, MiningCharacterStatsSnapshot>();
        }

        var windowEndUtc = DateTime.UtcNow;
        var cutoffUtc = windowEndUtc - rangeMode switch
        {
            MiningStatsRangeMode.Last1Hour => TimeSpan.FromHours(1),
            MiningStatsRangeMode.Last2Hours => TimeSpan.FromHours(2),
            MiningStatsRangeMode.Last4Hours => TimeSpan.FromHours(4),
            MiningStatsRangeMode.Last6Hours => TimeSpan.FromHours(6),
            MiningStatsRangeMode.Last8Hours => TimeSpan.FromHours(8),
            MiningStatsRangeMode.Last12Hours => TimeSpan.FromHours(12),
            MiningStatsRangeMode.Last24Hours => TimeSpan.FromHours(24),
            MiningStatsRangeMode.Last3Days => TimeSpan.FromDays(3),
            MiningStatsRangeMode.Last7Days => TimeSpan.FromDays(7),
            _ => TimeSpan.Zero
        };

        var raw = await GameLogMiningHistoryReader.ReadAsync(gameLogsDirectory, cutoffUtc, cancellationToken);
        return raw.ToDictionary(
            kvp => kvp.Key,
            kvp => BuildCharacterSnapshot(kvp.Value, cutoffUtc, windowEndUtc));
    }

    public async Task<IReadOnlyDictionary<int, MiningCharacterStatsSnapshot>> GetRollingSnapshotAsync(
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        await EnsureOreValuesFreshAsync(cancellationToken);

        var gameLogsDirectory = _gameLogsDirectory ?? await ResolveGameLogsDirectoryAsync(cancellationToken);
        if (gameLogsDirectory is null)
        {
            return new Dictionary<int, MiningCharacterStatsSnapshot>();
        }

        var cutoffUtc = DateTime.UtcNow - window;
        var raw = await GameLogMiningHistoryReader.ReadAsync(gameLogsDirectory, cutoffUtc, cancellationToken);
        return raw.ToDictionary(
            kvp => kvp.Key,
            kvp => BuildCharacterSnapshot(kvp.Value, cutoffUtc));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _enabled = await _settingsService.GetAsync<bool?>(MiningEnabledSettingsKey, stoppingToken) ?? false;
        if (!_enabled)
        {
            _logger.LogInformation("Mining session tracking is disabled by settings.");
            return;
        }

        _refineYieldFactor = await ResolveRefineYieldFactorAsync(stoppingToken);

        _gameLogsDirectory = await ResolveGameLogsDirectoryAsync(stoppingToken);
        if (_gameLogsDirectory is null)
        {
            _logger.LogWarning("Mining session tracking disabled: GameLogs directory was not found.");
            return;
        }

        await RefreshOreValuesAsync(stoppingToken);
        var lookback = await ResolveInitialScanLookbackAsync(stoppingToken);
        _logger.LogInformation("Starting mining session tracking from: {Path}", _gameLogsDirectory);
        _logger.LogInformation("Mining session startup scan lookback: {Hours} hour(s).", lookback.TotalHours);
        await _tracker.StartAsync(_gameLogsDirectory, lookback, stoppingToken);

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

    private async void OnTrackerSnapshotUpdated(object? sender, IReadOnlyDictionary<int, MiningSessionSnapshot> snapshot)
    {
        try
        {
            await EnsureOreValuesFreshAsync(CancellationToken.None);

            Dictionary<int, MiningCharacterStatsSnapshot> next;
            lock (_gate)
            {
                next = snapshot.ToDictionary(kvp => kvp.Key, kvp => BuildCharacterSnapshot(kvp.Value, kvp.Value.SessionStartedUtc));
                _snapshotByCharacterId.Clear();
                foreach (var kvp in next)
                {
                    _snapshotByCharacterId[kvp.Key] = kvp.Value;
                }
            }

            SnapshotUpdated?.Invoke(this, Snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rebuild mining session snapshot.");
        }
    }

    private MiningCharacterStatsSnapshot BuildCharacterSnapshot(
        MiningSessionSnapshot session,
        DateTime baselineUtc,
        DateTime? rateWindowEndUtc = null)
    {
        var startUtc = session.SessionStartedUtc > baselineUtc ? session.SessionStartedUtc : baselineUtc;
        var rateWindowEnd = rateWindowEndUtc ?? session.LastActivityUtc;
        var elapsed = rateWindowEnd > startUtc
            ? rateWindowEnd - startUtc
            : TimeSpan.FromSeconds(1);
        var elapsedHours = Math.Max(elapsed.TotalHours, 1d / 3600d);

        var oreStats = session.Ores
            .Select(ore =>
            {
                _oreValuesByName.TryGetValue(ore.OreName, out var oreValue);
                var minedUnits = ore.MinedUnits + ore.BonusUnits;
                var volumePerUnit = oreValue?.VolumeM3 ?? 0d;
                var iskPerUnit = oreValue?.EstimatedIskPerUnit ?? 0m;
                var regularYieldVolume = ore.MinedUnits * volumePerUnit;
                var critYieldVolume = ore.BonusUnits * volumePerUnit;
                return new MiningOreStatsSnapshot
                {
                    OreName = ore.OreName,
                    MinedUnits = ore.MinedUnits,
                    BonusUnits = ore.BonusUnits,
                    WasteUnits = ore.WasteUnits,
                    VolumePerUnitM3 = volumePerUnit,
                    EstimatedIskPerUnit = iskPerUnit,
                    TotalRegularYieldVolumeM3 = regularYieldVolume,
                    TotalCritVolumeM3 = critYieldVolume,
                    TotalMinedVolumeM3 = minedUnits * volumePerUnit,
                    TotalWasteVolumeM3 = ore.WasteUnits * volumePerUnit,
                    TotalEstimatedIsk = minedUnits * iskPerUnit,
                    LastKnownEfficiencyPercent = ore.LastKnownEfficiencyPercent
                };
            })
            .OrderByDescending(x => x.TotalMinedVolumeM3)
            .ToList();

        var totalRegularYieldVolume = oreStats.Sum(x => x.TotalRegularYieldVolumeM3);
        var totalCritVolume = oreStats.Sum(x => x.TotalCritVolumeM3);
        var totalMinedVolume = oreStats.Sum(x => x.TotalMinedVolumeM3);
        var totalWasteVolume = oreStats.Sum(x => x.TotalWasteVolumeM3);
        var totalEstimatedIsk = oreStats.Sum(x => x.TotalEstimatedIsk);
        var totalWasteEstimatedIsk = oreStats.Sum(x => x.WasteUnits * x.EstimatedIskPerUnit);
        var totalDepletionVolume = totalRegularYieldVolume + totalWasteVolume;
        var totalMiningVolume = totalMinedVolume + totalWasteVolume;
        var efficiencyRatio = totalMiningVolume <= 0
            ? 1d
            : totalMinedVolume / totalMiningVolume;
        var yieldPercent = totalDepletionVolume <= 0 ? 0d : (totalRegularYieldVolume / totalDepletionVolume) * 100d;
        var critPercent = totalDepletionVolume <= 0 ? 0d : (totalCritVolume / totalDepletionVolume) * 100d;
        var wastePercent = totalDepletionVolume <= 0 ? 0d : (totalWasteVolume / totalDepletionVolume) * 100d;
        var efficiencyPercent = totalDepletionVolume <= 0 ? 100d : (totalMinedVolume / totalDepletionVolume) * 100d;

        return new MiningCharacterStatsSnapshot
        {
            CharacterId = session.CharacterId,
            CharacterName = session.CharacterName,
            SessionStartedUtc = startUtc,
            LastActivityUtc = session.LastActivityUtc,
            SourceFilePath = session.SourceFilePath,
            PrimaryOreName = oreStats.FirstOrDefault()?.OreName ?? string.Empty,
            CurrentEfficiencyPercent = session.CurrentEfficiencyPercent,
            TotalRegularYieldVolumeM3 = totalRegularYieldVolume,
            TotalCritVolumeM3 = totalCritVolume,
            TotalMinedVolumeM3 = totalMinedVolume,
            TotalWasteVolumeM3 = totalWasteVolume,
            TotalMiningVolumeM3 = totalMiningVolume,
            YieldPercent = yieldPercent,
            CritPercent = critPercent,
            WastePercent = wastePercent,
            EfficiencyPercent = efficiencyPercent,
            MiningRateM3PerHour = totalMinedVolume / elapsedHours,
            WasteRateM3PerHour = totalWasteVolume / elapsedHours,
            TotalMiningRateM3PerHour = totalMiningVolume / elapsedHours,
            TotalEstimatedIsk = totalEstimatedIsk,
            TotalWasteEstimatedIsk = totalWasteEstimatedIsk,
            EstimatedIskPerHour = totalEstimatedIsk / (decimal)elapsedHours,
            WasteEstimatedIskPerHour = totalWasteEstimatedIsk / (decimal)elapsedHours,
            EfficiencyRatio = efficiencyRatio,
            Ores = oreStats
        };
    }

    private async Task EnsureOreValuesFreshAsync(CancellationToken cancellationToken)
    {
        var nextRefineYieldFactor = await ResolveRefineYieldFactorAsync(cancellationToken);
        if (nextRefineYieldFactor != _refineYieldFactor)
        {
            _refineYieldFactor = nextRefineYieldFactor;
            _oreValuesFetchedAtUtc = DateTime.MinValue;
        }

        if (DateTime.UtcNow - _oreValuesFetchedAtUtc <= PriceCacheTtl && _oreValuesByName.Count > 0)
        {
            return;
        }

        await RefreshOreValuesAsync(cancellationToken);
    }

    private async Task RefreshOreValuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(MiningSessionLogFeedHostedService));
            client.BaseAddress ??= new Uri("https://evehaklabs.cloud/");
            client.Timeout = TimeSpan.FromSeconds(20);

            var standardTask = client.GetFromJsonAsync<OreEnvelope<List<StandardOreDto>>>("api/public/v1/ores/standard", cancellationToken);
            var moonTask = client.GetFromJsonAsync<OreEnvelope<List<MoonOreDto>>>("api/public/v1/ores/moon", cancellationToken);
            var iceTask = client.GetFromJsonAsync<OreEnvelope<List<IceOreDto>>>("api/public/v1/ores/ice", cancellationToken);
            var prismaticiteTask = client.GetFromJsonAsync<OreEnvelope<PrismaticiteDto>>("api/public/v1/ores/prismaticite", cancellationToken);
            await Task.WhenAll(standardTask, moonTask, iceTask, prismaticiteTask);

            var next = new Dictionary<string, OreReferenceValue>(StringComparer.OrdinalIgnoreCase);
            var fallbackVolumesByName = await LoadOreVolumesByNameAsync(cancellationToken);

            foreach (var ore in standardTask.Result?.Data ?? [])
            {
                next[ore.Name] = BuildOreReferenceValue(ore.Volume, ore.UnitsToReprocess, (decimal)ore.RefinedValueToday);
            }

            foreach (var ore in moonTask.Result?.Data ?? [])
            {
                var volume = ore.Volume ?? (fallbackVolumesByName.TryGetValue(ore.Name, out var fallbackVolume) ? fallbackVolume : 0d);
                next[ore.Name] = BuildOreReferenceValue(volume, ore.UnitsToReprocess, (decimal)ore.RefinedValueToday);
            }

            foreach (var ore in iceTask.Result?.Data ?? [])
            {
                next[ore.Name] = BuildOreReferenceValue(ore.Volume, ore.UnitsToReprocess, (decimal)ore.RefinedValueToday);
            }

            var prismaticite = prismaticiteTask.Result?.Data;
            if (prismaticite?.OreName is { Length: > 0 } prismaticiteName)
            {
                next[prismaticiteName] = new OreReferenceValue(prismaticite.OreVolume, (decimal)prismaticite.ExpectedRandomValuePerOre * _refineYieldFactor);
            }

            lock (_gate)
            {
                _oreValuesByName = next;
                _oreValuesFetchedAtUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh mining ore values from EveHakLabs. Mining stats will continue without ISK valuation.");
        }
    }

    private async Task<Dictionary<string, double>> LoadOreVolumesByNameAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT typeName, volume
            FROM invTypes
            WHERE volume IS NOT NULL;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetDouble(1);
        }

        return result;
    }

    private async Task<string?> ResolveGameLogsDirectoryAsync(CancellationToken cancellationToken)
    {
        var configuredRoot = await _settingsService.GetAsync<string>(LogsRootSettingsKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredValidation = LocalChatLogsPathValidator.Validate(configuredRoot);
            if (configuredValidation.IsValid && configuredValidation.NormalizedLogsRootPath is not null)
            {
                var configuredGameLogs = Path.Combine(configuredValidation.NormalizedLogsRootPath, "Gamelogs");
                if (Directory.Exists(configuredGameLogs))
                {
                    return configuredGameLogs;
                }
            }
        }

        var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs");
        var defaultValidation = LocalChatLogsPathValidator.Validate(defaultRoot);
        if (defaultValidation.IsValid && defaultValidation.NormalizedLogsRootPath is not null)
        {
            var defaultGameLogs = Path.Combine(defaultValidation.NormalizedLogsRootPath, "Gamelogs");
            if (Directory.Exists(defaultGameLogs))
            {
                return defaultGameLogs;
            }
        }

        return null;
    }

    private async Task<TimeSpan> ResolveInitialScanLookbackAsync(CancellationToken cancellationToken)
    {
        var configured = await _settingsService.GetAsync<int?>(RecentSessionLookbackHoursSettingsKey, cancellationToken);
        return TimeSpan.FromHours(Math.Clamp(configured ?? 24, 1, 168));
    }

    private async Task<decimal> ResolveRefineYieldFactorAsync(CancellationToken cancellationToken)
    {
        var configuredPercent = await _settingsService.GetAsync<decimal?>(MiningRefineYieldPercentSettingsKey, cancellationToken);
        var clampedPercent = Math.Clamp(configuredPercent ?? 90.63m, 1m, 100m);
        return clampedPercent / 100m;
    }

    private OreReferenceValue BuildOreReferenceValue(double volumePerUnitM3, int unitsToReprocess, decimal refinedValuePerBatch)
    {
        var units = Math.Max(1, unitsToReprocess);
        var iskPerUnit = (refinedValuePerBatch * _refineYieldFactor) / units;
        return new OreReferenceValue(volumePerUnitM3, iskPerUnit);
    }

    private sealed record OreReferenceValue(double VolumeM3, decimal EstimatedIskPerUnit);

    private sealed class OreEnvelope<T>
    {
        public DateTime GeneratedAt { get; init; }
        public T? Data { get; init; }
    }

    private sealed class StandardOreDto
    {
        public string Name { get; init; } = string.Empty;
        public double Volume { get; init; }
        public int UnitsToReprocess { get; init; }
        public double RefinedValueToday { get; init; }
    }

    private sealed class MoonOreDto
    {
        public string Name { get; init; } = string.Empty;
        public double? Volume { get; init; }
        public int UnitsToReprocess { get; init; }
        public double RefinedValueToday { get; init; }
    }

    private sealed class IceOreDto
    {
        public string Name { get; init; } = string.Empty;
        public double Volume { get; init; }
        public int UnitsToReprocess { get; init; }
        public double RefinedValueToday { get; init; }
    }

    private sealed class PrismaticiteDto
    {
        public string OreName { get; init; } = string.Empty;
        public double OreVolume { get; init; }
        public double ExpectedRandomValuePerOre { get; init; }
    }
}
