using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Logs.IntelChatLogs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Background;

public sealed partial class IntelChatLogFeedHostedService : BackgroundService, IIntelFeed
{
    private const long InitialReadTailBytes = 512 * 1024;
    private static readonly TimeSpan DirtyFlushDebounceDelay = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan KnownActiveFileSweepInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SnapshotExpirySweepInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ChannelFileSweepInterval = TimeSpan.FromSeconds(10);
    // Only consider / read files newer than this value, utcNow + ChannelFileRecencyWindow
    private static readonly TimeSpan ChannelFileRecencyWindow = TimeSpan.FromDays(2);
    private const string LogsRootSettingsKey = "Tracking.LogsRootPath";
    private const string IntelEnabledSettingsKey = "Intel.Enabled";
    private const string IntelIncludeChannelsSettingsKey = "Intel.Channels.Include";
    private const string IntelSystemExpiryMinutesSettingsKey = "Intel.SystemExpiryMinutes";
    private const string IntelZkillEnabledSettingsKey = "Intel.Zkill.Enabled";
    private const string IntelZkillPollSecondsSettingsKey = "Intel.Zkill.PollSeconds";
    private const string ZkillSequenceEndpoint = "https://r2z2.zkillboard.com/ephemeral/sequence.json";
    private const string ZkillKillmailEndpointFormat = "https://r2z2.zkillboard.com/ephemeral/{0}.json";
    private const string ZkillReporterName = "zKillboard";
    private const string ZkillChannelName = "zKillboard";
    private const string ZkillSourcePath = "api://zkillboard/r2z2";

    private static readonly Regex ChatLineRegex = BuildChatLineRegex();
    private static readonly Regex IntelFileNameRegex = BuildIntelFileNameRegex();
    private static readonly Regex InGameKillmailLinkRegex = new(
        @"^Kill:\s+.+\s+\(.+\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ExternalIntelLinkRegex = new(
        @"https?://(?:(?:www\.)?adashboard\.info/intel/dscan/view/[A-Za-z0-9]+|(?:www\.)?dscan\.info/v/[A-Za-z0-9]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex DscanInfoSystemRegex = new(
        @"System:\s*<b><a[^>]*>(?<system>[^<]+)</a>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex DscanInfoShipItemRegex = new(
        @"<li[^>]*data-sclid=""[^""]+""[^>]*>\s*<span[^>]*>\s*(?<count>\d+)\s*</span>\s*<b>(?<name>[^<]+)</b>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex DashboardShipRowRegex = new(
        @"<tr[^>]*>\s*<td[^>]*title=""(?<class>[^""]+)""[^>]*>.*?&nbsp;(?<name>[^<]+)</td>\s*<td[^>]*>\s*<span>\s*(?<count>\d+)\s*</span>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private readonly ISettingsService _settingsService;
    private readonly ISdeDatabase _sdeDatabase;
    private readonly ILogger<IntelChatLogFeedHostedService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, byte> _dirtyFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _dirtySignal = new(0, int.MaxValue);
    private readonly Dictionary<string, long> _readOffsetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, IntelSystemSnapshot> _snapshotBySystemId = [];
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Dictionary<string, long> _systemIdByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<long, string> _systemNameById = [];
    private Dictionary<string, IntelShipClass> _shipClassByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _shipTypeIdByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, IntelShipClass> _shipClassByTypeId = [];
    private Dictionary<int, string> _shipNameByTypeId = [];
    private static readonly IReadOnlyDictionary<string, string> ShipAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["kiki"] = "kikimora",
        ["iki"] = "ikitursa",
        ["stileto"] = "stiletto",
        ["stilleto"] = "stiletto",
        ["stilletto"] = "stiletto",
        ["pod"] = "capsule",
        ["exeq"] = "exequror",
        ["cerb"] = "cerberus",
        ["retri"] = "retribution",
        ["sythe"] = "scythe",
        ["trasher"] = "thrasher",
        ["porp"] = "porpoise",
        ["bni"] = "brutix navy issue",
        ["eni"] = "exequror navy issue",
        ["bc"] = "battlecruiser",
        ["bs"] = "battleship",
        ["jf"] = "jump freighter",
        ["hictor"] = "heavy interdiction cruiser",
        ["hac"] = "heavy assault cruiser",
        ["fax"] = "force auxiliary"
    };
    private IntelChatMessageParser? _messageParser;
    private bool _enabled = true;
    private bool _zkillEnabled = true;
    private TimeSpan _zkillPollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ZkillSuccessDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ZkillNotFoundDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ZkillRateLimitDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ZkillAcceptedAgeWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReportDedupeRetention = TimeSpan.FromHours(1);
    private long? _nextZkillSequence;
    private DateTime _nextZkillPollAfterUtc = DateTime.MinValue;
    private HashSet<string> _includeChannels = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _systemExpiry = TimeSpan.FromMinutes(15);
    private readonly DateTime _startupHistoryCutoffUtc = DateTime.UtcNow - TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, DateTime> _recentReportKeys = new(StringComparer.Ordinal);

    public IntelChatLogFeedHostedService(
        ISettingsService settingsService,
        ISdeDatabase sdeDatabase,
        IHttpClientFactory httpClientFactory,
        ILogger<IntelChatLogFeedHostedService> logger)
    {
        _settingsService = settingsService;
        _sdeDatabase = sdeDatabase;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public event EventHandler<IntelChatReport>? ReportReceived;
    public event EventHandler<IReadOnlyDictionary<long, IntelSystemSnapshot>>? SnapshotUpdated;

    public IReadOnlyDictionary<long, IntelSystemSnapshot> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<long, IntelSystemSnapshot>(_snapshotBySystemId);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadSettingsAsync(stoppingToken);
        if (!_enabled)
        {
            _logger.LogInformation("Intel chat feed is disabled by settings.");
            return;
        }

        _systemIdByName = await LoadSystemNameMapAsync(stoppingToken);
        (_shipClassByName, _shipTypeIdByName, _shipClassByTypeId, _shipNameByTypeId) = await LoadShipMapsAsync(stoppingToken);
        _messageParser = new IntelChatMessageParser(_systemIdByName, _shipClassByName, ShipAliases);
        if (_zkillEnabled)
        {
            await InitializeZkillSequenceAsync(stoppingToken);
        }
        var chatLogsDirectory = await ResolveChatLogsDirectoryAsync(stoppingToken);
        if (chatLogsDirectory is null && _includeChannels.Count > 0)
        {
            _logger.LogWarning("Intel chat feed disabled: ChatLogs directory was not found.");
        }

        if (chatLogsDirectory is not null)
        {
            _logger.LogInformation("Starting intel chat feed from: {Path}", chatLogsDirectory);
            SetupWatcher(chatLogsDirectory);
            EnqueueAllKnownFiles(chatLogsDirectory, startupOnlyNewestPerChannel: true);
        }

        try
        {
            var intelLoop = RunIntelLogLoopAsync(chatLogsDirectory, stoppingToken);
            var zkillLoop = RunZkillLoopAsync(stoppingToken);
            await Task.WhenAll(intelLoop, zkillLoop);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
    }

    private async Task RunIntelLogLoopAsync(string? chatLogsDirectory, CancellationToken stoppingToken)
    {
        var nextKnownFileSweepUtc = DateTime.UtcNow + KnownActiveFileSweepInterval;
        var nextExpirySweepUtc = DateTime.UtcNow + SnapshotExpirySweepInterval;
        var nextChannelSweepUtc = DateTime.UtcNow + ChannelFileSweepInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var delayUntilKnownFileSweep = nextKnownFileSweepUtc - nowUtc;
                var delayUntilExpirySweep = nextExpirySweepUtc - nowUtc;
                var delayUntilChannelSweep = chatLogsDirectory is null
                    ? Timeout.InfiniteTimeSpan
                    : nextChannelSweepUtc - nowUtc;
                var waitDelay = MinPositiveDelay(delayUntilKnownFileSweep, delayUntilExpirySweep, delayUntilChannelSweep);

                if (await _dirtySignal.WaitAsync(waitDelay, stoppingToken))
                {
                    // Coalesce a burst of file watcher events for the same active session log before
                    // reopening files. Offsets still guarantee we only consume each appended line once.
                    while (await _dirtySignal.WaitAsync(DirtyFlushDebounceDelay, stoppingToken))
                    {
                    }

                    await FlushDirtyFilesAsync(stoppingToken);
                    nowUtc = DateTime.UtcNow;
                }

                if (nowUtc >= nextKnownFileSweepUtc)
                {
                    EnqueueKnownActiveFiles();
                    nextKnownFileSweepUtc = nowUtc + KnownActiveFileSweepInterval;
                }

                if (nowUtc >= nextExpirySweepUtc)
                {
                    ExpireSnapshots(nowUtc);
                    nextExpirySweepUtc = nowUtc + SnapshotExpirySweepInterval;
                }

                if (chatLogsDirectory is not null && nowUtc >= nextChannelSweepUtc)
                {
                    EnqueueAllKnownFiles(chatLogsDirectory, startupOnlyNewestPerChannel: true);
                    nextChannelSweepUtc = nowUtc + ChannelFileSweepInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Intel chat log loop iteration failed; continuing.");
            }

        }
    }

    private void EnqueueKnownActiveFiles()
    {
        string[] knownFiles;
        lock (_gate)
        {
            knownFiles = _readOffsetsByPath.Keys.ToArray();
        }

        foreach (var filePath in knownFiles)
        {
            EnqueueDirtyFile(filePath);
        }
    }

    private static TimeSpan MinPositiveDelay(params TimeSpan[] delays)
    {
        var best = Timeout.InfiniteTimeSpan;
        foreach (var delay in delays)
        {
            if (delay <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            if (delay == Timeout.InfiniteTimeSpan)
            {
                continue;
            }

            if (best == Timeout.InfiniteTimeSpan || delay < best)
            {
                best = delay;
            }
        }

        return best;
    }

    private async Task RunZkillLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollZkillAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "zKill poll loop iteration failed; continuing.");
            }

            await Task.Delay(_zkillPollDelay, stoppingToken);
        }
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        _enabled = await _settingsService.GetAsync<bool?>(IntelEnabledSettingsKey, cancellationToken) ?? true;
        _zkillEnabled = await _settingsService.GetAsync<bool?>(IntelZkillEnabledSettingsKey, cancellationToken) ?? true;
        var zkillPollSeconds = Math.Clamp(await _settingsService.GetAsync<int?>(IntelZkillPollSecondsSettingsKey, cancellationToken) ?? 2, 2, 60);
        _zkillPollDelay = TimeSpan.FromSeconds(zkillPollSeconds);
        var include = await _settingsService.GetAsync<List<string>>(IntelIncludeChannelsSettingsKey, cancellationToken) ?? [];
        _includeChannels = include.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expiryMinutes = Math.Clamp(await _settingsService.GetAsync<int?>(IntelSystemExpiryMinutesSettingsKey, cancellationToken) ?? 15, 1, 180);
        _systemExpiry = TimeSpan.FromMinutes(expiryMinutes);
    }

    private async Task<Dictionary<string, long>> LoadSystemNameMapAsync(CancellationToken cancellationToken)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT solarSystemID, solarSystemName FROM mapSolarSystems;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(1)] = reader.GetInt64(0);
        }

        _systemNameById = result.ToDictionary(x => x.Value, x => x.Key);
        return result;
    }

    private async Task<(Dictionary<string, IntelShipClass> ClassByName, Dictionary<string, int> TypeIdByName, Dictionary<int, IntelShipClass> ClassByTypeId, Dictionary<int, string> NameByTypeId)> LoadShipMapsAsync(CancellationToken cancellationToken)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var classByName = new Dictionary<string, IntelShipClass>(StringComparer.OrdinalIgnoreCase);
        var typeIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var classByTypeId = new Dictionary<int, IntelShipClass>();
        var nameByTypeId = new Dictionary<int, string>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.typeID, t.typeName, g.groupName
            FROM invTypes t
            INNER JOIN invGroups g ON g.groupID = t.groupID
            WHERE g.categoryID = 6;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var typeId = reader.GetInt32(0);
            var shipName = reader.GetString(1);
            var groupName = reader.GetString(2);
            var shipClass = ShipGroupToIntelShipClass(groupName);
            if (shipClass == IntelShipClass.Unknown)
            {
                continue;
            }

            var key = shipName.ToLowerInvariant();
            classByName[key] = shipClass;
            typeIdByName[key] = typeId;
            classByTypeId[typeId] = shipClass;
            nameByTypeId[typeId] = shipName;
        }

        return (classByName, typeIdByName, classByTypeId, nameByTypeId);
    }

    private static IntelShipClass ShipGroupToIntelShipClass(string groupName)
    {
        return groupName switch
        {
            "Capsule" => IntelShipClass.Capsule,
            "Shuttle" => IntelShipClass.Shuttle,
            "Corvette" => IntelShipClass.Rookie,
            "Frigate" or "Assault Frigate" or "Interceptor" or "Electronic Attack Ship" or "Covert Ops" or "Logistics Frigate" or "Prototype Exploration Ship" or "Stealth Bomber" => IntelShipClass.Frigate,
            "Destroyer" or "Command Destroyer" or "Tactical Destroyer" or "Interdictor" => IntelShipClass.Destroyer,
            "Cruiser" or "Combat Recon Ship" or "Flag Cruiser" or "Force Recon Ship" or "Heavy Assault Cruiser" or "Heavy Interdiction Cruiser" or "Logistics" or "Strategic Cruiser" => IntelShipClass.Cruiser,
            "Attack Battlecruiser" or "Combat Battlecruiser" or "Command Ship" => IntelShipClass.Battlecruiser,
            "Battleship" or "Black Ops" or "Marauder" => IntelShipClass.Battleship,
            "Carrier" or "Force Auxiliary" or "Dreadnought" or "Lancer Dreadnought" => IntelShipClass.Capital,
            "Supercarrier" => IntelShipClass.Supercapital,
            "Titan" => IntelShipClass.Titan,
            "Hauler" or "Blockade Runner" or "Deep Space Transport" => IntelShipClass.Industrial,
            "Expedition Frigate" => IntelShipClass.MiningFrigate,
            "Mining Barge" or "Exhumer" => IntelShipClass.MiningBarge,
            "Industrial Command Ship" => IntelShipClass.IndustrialCommand,
            "Freighter" or "Jump Freighter" or "Capital Industrial Ship" => IntelShipClass.Freighter,
            _ => IntelShipClass.Unknown
        };
    }

    private async Task<string?> ResolveChatLogsDirectoryAsync(CancellationToken cancellationToken)
    {
        var configuredRoot = await _settingsService.GetAsync<string>(LogsRootSettingsKey, cancellationToken);
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            roots.Add(configuredRoot.Trim());
        }

        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs"));
        foreach (var root in roots)
        {
            var chatLogs = Path.Combine(root, "ChatLogs");
            if (Directory.Exists(chatLogs))
            {
                return Path.GetFullPath(chatLogs);
            }
        }

        return null;
    }

    private void SetupWatcher(string chatLogsDirectory)
    {
        _watcher = new FileSystemWatcher(chatLogsDirectory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };
        _watcher.Filters.Add("*.txt");
        _watcher.Changed += OnWatcherChanged;
        _watcher.Created += OnWatcherChanged;
        _watcher.Renamed += OnWatcherRenamed;
        _watcher.Error += (_, _) => EnqueueAllKnownFiles(chatLogsDirectory, startupOnlyNewestPerChannel: true);
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (IsCandidateIntelFile(e.Name))
        {
            if (e.Name is not null &&
                TryExtractChannelNameFromFileName(e.Name, out var channelFromName) &&
                !ShouldReadChannel(channelFromName))
            {
                return;
            }

            EnqueueDirtyFile(e.FullPath);
        }
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (IsCandidateIntelFile(e.Name))
        {
            if (e.Name is not null &&
                TryExtractChannelNameFromFileName(e.Name, out var channelFromName) &&
                !ShouldReadChannel(channelFromName))
            {
                return;
            }

            EnqueueDirtyFile(e.FullPath);
        }
    }

    private static bool IsCandidateIntelFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("Local_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractChannelNameFromFileName(string fileName, out string channelName)
    {
        channelName = string.Empty;
        var match = IntelFileNameRegex.Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        channelName = match.Groups["channel"].Value;
        return channelName.Length > 0;
    }

    // Extracts both the channel and the session-start timestamp encoded in the file name (Channel_yyyyMMdd_HHmmss_characterId.txt)
    private static bool TryParseIntelFileName(string fileName, out string channelName, out DateTime sessionStartedUtc)
    {
        channelName = string.Empty;
        sessionStartedUtc = default;
        var match = IntelFileNameRegex.Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        channelName = match.Groups["channel"].Value;
        if (channelName.Length == 0)
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                $"{match.Groups["date"].Value} {match.Groups["time"].Value}",
                "yyyyMMdd HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        sessionStartedUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    private void EnqueueAllKnownFiles(string chatLogsDirectory, bool startupOnlyNewestPerChannel)
    {
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(chatLogsDirectory, "*.txt", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate chat log files in {Path}", chatLogsDirectory);
            return;
        }

        if (startupOnlyNewestPerChannel)
        {
            var cutoffUtc = DateTime.UtcNow - ChannelFileRecencyWindow;
            var newestByChannel = new Dictionary<string, (string Path, DateTime SessionStartedUtc)>(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                if (!IsCandidateIntelFile(fileName))
                {
                    continue;
                }

                if (!TryParseIntelFileName(fileName, out var channelFromName, out var sessionStartedUtc) || !ShouldReadChannel(channelFromName))
                {
                    continue;
                }

                // Skip historical session files without touching disk; only the current session per
                // channel is of interest, and active files keep flowing in via the FileSystemWatcher.
                if (sessionStartedUtc < cutoffUtc)
                {
                    continue;
                }

                if (!newestByChannel.TryGetValue(channelFromName, out var existing) || sessionStartedUtc > existing.SessionStartedUtc)
                {
                    newestByChannel[channelFromName] = (filePath, sessionStartedUtc);
                }
            }

            foreach (var entry in newestByChannel.Values)
            {
                EnqueueDirtyFile(entry.Path);
            }

            return;
        }

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            if (!IsCandidateIntelFile(fileName))
            {
                continue;
            }

            if (TryExtractChannelNameFromFileName(fileName, out var channelFromName) && !ShouldReadChannel(channelFromName))
            {
                continue;
            }

            EnqueueDirtyFile(filePath);
        }
    }

    private void EnqueueDirtyFile(string filePath)
    {
        if (_dirtyFiles.TryAdd(filePath, 1))
        {
            try
            {
                _dirtySignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Best effort wake signal; processing still occurs via periodic loop delay.
            }
        }
    }

    private async Task FlushDirtyFilesAsync(CancellationToken cancellationToken)
    {
        if (_dirtyFiles.IsEmpty)
        {
            return;
        }

        var files = _dirtyFiles.Keys.ToArray();
        foreach (var file in files)
        {
            _dirtyFiles.TryRemove(file, out _);
            try
            {
                await ProcessFileAsync(file, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process intel chat log file {FilePath}", file);
            }
        }
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            lock (_gate)
            {
                _readOffsetsByPath.Remove(filePath);
            }
            return;
        }

        var channelName = await ReadHeaderChannelNameAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelName) || !ShouldReadChannel(channelName))
        {
            return;
        }

        var snapshotChanged = false;
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long offset;
        lock (_gate)
        {
            _readOffsetsByPath.TryGetValue(filePath, out offset);
        }

        if (offset <= 0 && stream.Length > InitialReadTailBytes)
        {
            // Startup optimization: tail the latest chunk instead of replaying full historical logs.
            offset = stream.Length - InitialReadTailBytes;
        }

        if (offset > stream.Length)
        {
            offset = 0;
        }

        stream.Position = offset;
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (!TryParseChatLine(line, out var timestampUtc, out var reporter, out var message))
            {
                continue;
            }

            if (timestampUtc < _startupHistoryCutoffUtc ||
                timestampUtc < DateTime.UtcNow - _systemExpiry)
            {
                continue;
            }

            if (IsChannelMotdMessage(reporter, message))
            {
                continue;
            }

            if (IsIgnoredIntelMessage(message))
            {
                continue;
            }

            var report = await ParseIntelReportAsync(timestampUtc, channelName, reporter, message, filePath, cancellationToken);
            if (report.Systems.Count == 0)
            {
                continue;
            }

            if (!TryRegisterReport(report))
            {
                continue;
            }

            ReportReceived?.Invoke(this, report);
            snapshotChanged |= ApplyToSystemSnapshot(report);
        }

        lock (_gate)
        {
            _readOffsetsByPath[filePath] = stream.Position;
        }

        if (snapshotChanged)
        {
            SnapshotUpdated?.Invoke(this, Snapshot);
        }
    }

    private bool ShouldReadChannel(string channelName)
    {
        if (_includeChannels.Count == 0)
        {
            // No explicit include list configured: treat as "read all intel channels".
            return true;
        }

        return _includeChannels.Contains(channelName);
    }

    private IReadOnlyList<int> ResolveShipTypeIds(IReadOnlyList<string> shipNames)
    {
        if (shipNames.Count == 0)
        {
            return [];
        }

        var result = new List<int>(shipNames.Count);
        foreach (var rawName in shipNames)
        {
            var key = (rawName ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0)
            {
                continue;
            }

            if (ShipAliases.TryGetValue(key, out var canonical))
            {
                key = canonical;
            }

            if (_shipTypeIdByName.TryGetValue(key, out var typeId))
            {
                result.Add(typeId);
                continue;
            }

            if (key.EndsWith('s') && _shipTypeIdByName.TryGetValue(key[..^1], out typeId))
            {
                result.Add(typeId);
            }
        }

        return result;
    }

    private static bool TryParseChatLine(string rawLine, out DateTime timestampUtc, out string reporter, out string message)
    {
        timestampUtc = default;
        reporter = string.Empty;
        message = string.Empty;
        var line = rawLine.TrimStart('\uFEFF').Trim();
        var match = ChatLineRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        if (!DateTime.TryParseExact(match.Groups["timestamp"].Value, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        reporter = match.Groups["reporter"].Value.Trim();
        message = match.Groups["message"].Value.Trim();
        if (message.Length == 0)
        {
            return false;
        }

        timestampUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    private static bool IsChannelMotdMessage(string reporter, string message)
    {
        return reporter.Equals("EVE System", StringComparison.OrdinalIgnoreCase)
            && message.StartsWith("Channel MOTD:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredIntelMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return InGameKillmailLinkRegex.IsMatch(message.Trim());
    }

    private async Task<IntelChatReport> ParseIntelReportAsync(DateTime timestampUtc, string channelName, string reporter, string message, string sourcePath, CancellationToken cancellationToken)
    {
        var sanitizedMessage = RemoveExternalIntelLinks(message);
        var parsed = _messageParser?.Parse(sanitizedMessage) ?? new IntelParseResult
        {
            Systems = [],
            ShipClasses = [],
            ShipNames = [],
            Alerts = [],
            HostileNames = [],
            IsClear = false,
            HostileCount = 0
        };
        var linkedData = await TryLoadExternalIntelLinkDataAsync(message, cancellationToken);
        var mergedSystems = parsed.Systems.Count > 0
            ? parsed.Systems.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : (linkedData?.Systems?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var mergedShipNames = parsed.ShipNames.ToList();
        if (linkedData is not null)
        {
            mergedShipNames.AddRange(linkedData.ShipNames);
        }

        var mergedShipClasses = parsed.ShipClasses.ToList();
        if (linkedData is not null)
        {
            mergedShipClasses.AddRange(linkedData.ShipClasses);
        }

        if (mergedShipClasses.Count == 0 && mergedShipNames.Count > 0)
        {
            mergedShipClasses.AddRange(ResolveShipClasses(mergedShipNames));
        }

        var reportedShipTypeIds = ResolveShipTypeIds(mergedShipNames);
        var hostileCount = parsed.IsClear
            ? 0
            : Math.Max(parsed.HostileCount, Math.Max(mergedShipNames.Count, mergedShipClasses.Count));
        return new IntelChatReport
        {
            DedupeKey = BuildIntelReportDedupeKey(timestampUtc, reporter, message),
            TimestampUtc = timestampUtc,
            ChannelName = channelName,
            ReporterName = reporter,
            MessageText = message,
            SourceFilePath = sourcePath,
            Systems = mergedSystems.ToList(),
            ShipClasses = mergedShipClasses,
            ReportedShipNames = mergedShipNames,
            ReportedShipTypeIds = reportedShipTypeIds,
            Alerts = parsed.Alerts,
            ReportedHostileNames = parsed.HostileNames,
            IsClear = parsed.IsClear,
            ReportedHostileCount = hostileCount,
            Killmail = null
        };
    }

    private IReadOnlyList<IntelShipClass> ResolveShipClasses(IReadOnlyList<string> shipNames)
    {
        if (shipNames.Count == 0)
        {
            return [];
        }

        var result = new List<IntelShipClass>(shipNames.Count);
        foreach (var rawName in shipNames)
        {
            var key = (rawName ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (_shipClassByName.TryGetValue(key, out var shipClass) && shipClass != IntelShipClass.Unknown)
            {
                result.Add(shipClass);
            }
        }

        return result;
    }

    private async Task<ExternalIntelLinkData?> TryLoadExternalIntelLinkDataAsync(string message, CancellationToken cancellationToken)
    {
        var urls = ExternalIntelLinkRegex.Matches(message ?? string.Empty)
            .Select(x => x.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
        {
            return null;
        }

        var mergedSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedShipNames = new List<string>();
        var mergedShipClasses = new List<IntelShipClass>();
        var client = CreateIntelLinkHttpClient();
        foreach (var url in urls)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!TryParseExternalIntelLinkData(url, html, out var parsed))
                {
                    continue;
                }

                foreach (var system in parsed.Systems)
                {
                    mergedSystems.Add(system);
                }

                mergedShipNames.AddRange(parsed.ShipNames);
                mergedShipClasses.AddRange(parsed.ShipClasses);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enrich intel report from external dscan link {Url}.", url);
            }
        }

        if (mergedSystems.Count == 0 && mergedShipNames.Count == 0 && mergedShipClasses.Count == 0)
        {
            return null;
        }

        if (mergedShipClasses.Count == 0 && mergedShipNames.Count > 0)
        {
            mergedShipClasses.AddRange(ResolveShipClasses(mergedShipNames));
        }

        return new ExternalIntelLinkData
        {
            Systems = mergedSystems,
            ShipNames = mergedShipNames,
            ShipClasses = mergedShipClasses
        };
    }

    private HttpClient CreateIntelLinkHttpClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(IntelChatLogFeedHostedService));
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HISA/1.0 (https://github.com/Crytopox/HISA)");
        }

        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    private bool TryParseExternalIntelLinkData(string url, string html, out ExternalIntelLinkData data)
    {
        data = new ExternalIntelLinkData();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        if (url.Contains("dscan.info", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseDscanInfoHtml(html, out data);
        }

        if (url.Contains("adashboard.info", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseAdashboardHtml(html, out data);
        }

        return false;
    }

    private bool TryParseDscanInfoHtml(string html, out ExternalIntelLinkData data)
    {
        data = new ExternalIntelLinkData();
        var systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var systemMatch = DscanInfoSystemRegex.Match(html);
        if (systemMatch.Success)
        {
            var systemName = WebUtility.HtmlDecode(systemMatch.Groups["system"].Value).Trim();
            if (systemName.Length > 0)
            {
                systems.Add(systemName);
            }
        }

        var shipNames = new List<string>();
        foreach (Match match in DscanInfoShipItemRegex.Matches(html))
        {
            var shipName = WebUtility.HtmlDecode(match.Groups["name"].Value).Trim();
            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || shipName.Length == 0)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                shipNames.Add(shipName);
            }
        }

        var shipClasses = ResolveShipClasses(shipNames).ToList();
        data = new ExternalIntelLinkData
        {
            Systems = systems,
            ShipNames = shipNames,
            ShipClasses = shipClasses
        };
        return systems.Count > 0 || shipNames.Count > 0 || shipClasses.Count > 0;
    }

    private bool TryParseAdashboardHtml(string html, out ExternalIntelLinkData data)
    {
        data = new ExternalIntelLinkData();
        var shipNames = new List<string>();
        var shipClasses = new List<IntelShipClass>();
        foreach (Match match in DashboardShipRowRegex.Matches(html))
        {
            var shipName = WebUtility.HtmlDecode(match.Groups["name"].Value).Trim();
            var shipClassName = WebUtility.HtmlDecode(match.Groups["class"].Value).Trim();
            if (!int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || shipName.Length == 0)
            {
                continue;
            }

            var mappedClass = MapExternalShipClass(shipClassName);
            for (var i = 0; i < count; i++)
            {
                shipNames.Add(shipName);
                if (mappedClass != IntelShipClass.Unknown)
                {
                    shipClasses.Add(mappedClass);
                }
            }
        }

        if (shipClasses.Count == 0 && shipNames.Count > 0)
        {
            shipClasses.AddRange(ResolveShipClasses(shipNames));
        }

        data = new ExternalIntelLinkData
        {
            Systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ShipNames = shipNames,
            ShipClasses = shipClasses
        };
        return shipNames.Count > 0 || shipClasses.Count > 0;
    }

    private static string RemoveExternalIntelLinks(string message)
    {
        return ExternalIntelLinkRegex.Replace(message ?? string.Empty, " ").Trim();
    }

    private static IntelShipClass MapExternalShipClass(string className)
    {
        return className.Trim().ToLowerInvariant() switch
        {
            "frigate" => IntelShipClass.Frigate,
            "assault frigate" => IntelShipClass.Frigate,
            "interceptor" => IntelShipClass.Frigate,
            "destroyer" => IntelShipClass.Destroyer,
            "interdictor" => IntelShipClass.Destroyer,
            "cruiser" => IntelShipClass.Cruiser,
            "battlecruiser" => IntelShipClass.Battlecruiser,
            "battleship" => IntelShipClass.Battleship,
            "capital" => IntelShipClass.Capital,
            "industrial" => IntelShipClass.Industrial,
            "industrial command" => IntelShipClass.IndustrialCommand,
            "freighter" => IntelShipClass.Freighter,
            "capsule" => IntelShipClass.Capsule,
            "shuttle" => IntelShipClass.Shuttle,
            "rookie ship" => IntelShipClass.Rookie,
            _ => IntelShipClass.Unknown
        };
    }

    private sealed class ExternalIntelLinkData
    {
        public HashSet<string> Systems { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ShipNames { get; init; } = [];
        public List<IntelShipClass> ShipClasses { get; init; } = [];
    }

    private bool TryRegisterReport(IntelChatReport report)
    {
        if (string.IsNullOrWhiteSpace(report.DedupeKey))
        {
            return true;
        }

        var nowUtc = DateTime.UtcNow;
        lock (_gate)
        {
            PruneRecentReportKeys(nowUtc);
            if (_recentReportKeys.ContainsKey(report.DedupeKey))
            {
                return false;
            }

            _recentReportKeys[report.DedupeKey] = nowUtc;
            return true;
        }
    }

    private void PruneRecentReportKeys(DateTime nowUtc)
    {
        if (_recentReportKeys.Count == 0)
        {
            return;
        }

        var staleKeys = _recentReportKeys
            .Where(x => nowUtc - x.Value > ReportDedupeRetention)
            .Select(x => x.Key)
            .ToList();
        foreach (var staleKey in staleKeys)
        {
            _recentReportKeys.Remove(staleKey);
        }
    }

    private bool ApplyToSystemSnapshot(IntelChatReport report)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var systemName in report.Systems)
            {
                if (!_systemIdByName.TryGetValue(systemName, out var systemId))
                {
                    continue;
                }

                if (report.IsClear)
                {
                    var previousReports = _snapshotBySystemId.TryGetValue(systemId, out var existingClearSnapshot)
                        ? existingClearSnapshot.RecentReports
                        : [];
                    var recentReports = new List<IntelRecentReport>
                    {
                        new()
                        {
                            TimestampUtc = report.TimestampUtc,
                            ReporterName = report.ReporterName,
                            MessageText = report.MessageText
                        }
                    };
                    recentReports.AddRange(previousReports.Where(x =>
                        x.TimestampUtc != report.TimestampUtc ||
                        !string.Equals(x.ReporterName, report.ReporterName, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(x.MessageText, report.MessageText, StringComparison.Ordinal)));
                    recentReports = recentReports
                        .OrderByDescending(x => x.TimestampUtc)
                        .Take(4)
                        .ToList();
                    _snapshotBySystemId[systemId] = new IntelSystemSnapshot
                    {
                        SolarSystemId = systemId,
                        SolarSystemName = systemName,
                        LastUpdatedUtc = report.TimestampUtc,
                        LastChannelName = report.ChannelName,
                        LastReporterName = report.ReporterName,
                        LastMessageText = report.MessageText,
                        ShipClasses = [],
                        ShipNames = [],
                        ShipTypeIds = [],
                        Alerts = [IntelAlertType.Clear],
                        HostilePilotNames = [],
                        RecentReports = recentReports,
                        HostileScore = 0,
                        IsClear = true
                    };
                    changed = true;
                    continue;
                }

                var movedHostileNames = report.ReportedHostileNames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (movedHostileNames.Count > 0)
                {
                    MoveReportedHostilesFromOtherSystems(systemId, movedHostileNames);
                }

                var previous = _snapshotBySystemId.TryGetValue(systemId, out var existingSnapshot)
                    ? existingSnapshot.RecentReports
                    : [];
                var previousHostileNames = _snapshotBySystemId.TryGetValue(systemId, out existingSnapshot)
                    ? existingSnapshot.HostilePilotNames
                    : [];
                var reports = new List<IntelRecentReport>
                {
                    new()
                    {
                        TimestampUtc = report.TimestampUtc,
                        ReporterName = report.ReporterName,
                        MessageText = report.MessageText
                    }
                };
                reports.AddRange(previous.Where(x =>
                    x.TimestampUtc != report.TimestampUtc ||
                    !string.Equals(x.ReporterName, report.ReporterName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(x.MessageText, report.MessageText, StringComparison.Ordinal)));
                reports = reports
                    .OrderByDescending(x => x.TimestampUtc)
                    .Take(4)
                    .ToList();

                var mergedHostiles = MergeHostileNames(previousHostileNames, movedHostileNames);
                var hostileScoreBase = report.ReportedHostileCount > 0
                    ? report.ReportedHostileCount
                    : Math.Max(mergedHostiles.Count, Math.Max(report.ShipClasses.Count, report.Alerts.Any(a => a != IntelAlertType.Clear) ? 1 : 0));
                var hostileScore = Math.Max(1, hostileScoreBase);
                _snapshotBySystemId[systemId] = new IntelSystemSnapshot
                {
                    SolarSystemId = systemId,
                    SolarSystemName = systemName,
                    LastUpdatedUtc = report.TimestampUtc,
                    LastChannelName = report.ChannelName,
                    LastReporterName = report.ReporterName,
                    LastMessageText = report.MessageText,
                    ShipClasses = report.ShipClasses,
                    ShipNames = report.ReportedShipNames,
                    ShipTypeIds = report.ReportedShipTypeIds,
                    Alerts = report.Alerts,
                    HostilePilotNames = mergedHostiles,
                    RecentReports = reports,
                    HostileScore = hostileScore,
                    IsClear = false
                };
                changed = true;
            }

            if (changed)
            {
                EnforceSingleSystemPerHostile();
            }
        }

        return changed;
    }

    private void EnforceSingleSystemPerHostile()
    {
        var entries = new List<(long SystemId, DateTime LastUpdatedUtc, string Name)>();
        foreach (var pair in _snapshotBySystemId)
        {
            if (pair.Value.IsClear || pair.Value.HostilePilotNames.Count == 0)
            {
                continue;
            }

            foreach (var name in pair.Value.HostilePilotNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    entries.Add((pair.Key, pair.Value.LastUpdatedUtc, name.Trim()));
                }
            }
        }

        if (entries.Count <= 1)
        {
            return;
        }

        var canonicalBySystem = new Dictionary<long, List<string>>();
        for (var i = 0; i < entries.Count; i++)
        {
            var candidate = entries[i];
            var best = candidate;
            for (var j = 0; j < entries.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var other = entries[j];
                if (!HostileNamesMatch(candidate.Name, other.Name))
                {
                    continue;
                }

                if (other.LastUpdatedUtc > best.LastUpdatedUtc ||
                    (other.LastUpdatedUtc == best.LastUpdatedUtc && other.SystemId > best.SystemId))
                {
                    best = other;
                }
            }

            if (!canonicalBySystem.TryGetValue(best.SystemId, out var names))
            {
                names = [];
                canonicalBySystem[best.SystemId] = names;
            }

            if (!names.Any(existing => HostileNamesMatch(existing, candidate.Name)))
            {
                names.Add(candidate.Name);
            }
        }

        foreach (var pair in _snapshotBySystemId.ToList())
        {
            if (pair.Value.IsClear)
            {
                continue;
            }

            var keptNames = canonicalBySystem.TryGetValue(pair.Key, out var canonical)
                ? canonical
                : [];
            if (keptNames.Count == 0 && pair.Value.HostilePilotNames.Count > 0)
            {
                _snapshotBySystemId.Remove(pair.Key);
                continue;
            }

            var removedHostileCount = pair.Value.HostilePilotNames.Count - keptNames.Count;
            var derivedHostileScore = Math.Max(
                keptNames.Count,
                pair.Value.HostileScore - removedHostileCount);

            _snapshotBySystemId[pair.Key] = new IntelSystemSnapshot
            {
                SolarSystemId = pair.Value.SolarSystemId,
                SolarSystemName = pair.Value.SolarSystemName,
                LastUpdatedUtc = pair.Value.LastUpdatedUtc,
                LastChannelName = pair.Value.LastChannelName,
                LastReporterName = pair.Value.LastReporterName,
                LastMessageText = pair.Value.LastMessageText,
                ShipClasses = pair.Value.ShipClasses,
                ShipNames = pair.Value.ShipNames,
                ShipTypeIds = pair.Value.ShipTypeIds,
                Alerts = pair.Value.Alerts,
                HostilePilotNames = keptNames,
                RecentReports = pair.Value.RecentReports,
                HostileScore = derivedHostileScore,
                IsClear = pair.Value.IsClear
            };
        }
    }

    private void ExpireSnapshots(DateTime nowUtc)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var pair in _snapshotBySystemId.ToList())
            {
                var age = nowUtc - pair.Value.LastUpdatedUtc;
                var maxAge = _systemExpiry;
                if (age > maxAge)
                {
                    _snapshotBySystemId.Remove(pair.Key);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            SnapshotUpdated?.Invoke(this, Snapshot);
        }
    }

    private void MoveReportedHostilesFromOtherSystems(long targetSystemId, IReadOnlyList<string> movedHostileNames)
    {
        foreach (var pair in _snapshotBySystemId.ToList())
        {
            if (pair.Key == targetSystemId || pair.Value.IsClear)
            {
                continue;
            }

            var remaining = pair.Value.HostilePilotNames
                .Where(existing => !movedHostileNames.Any(incoming => HostileNamesMatch(existing, incoming)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (remaining.Count == pair.Value.HostilePilotNames.Count)
            {
                continue;
            }

            if (remaining.Count == 0)
            {
                _snapshotBySystemId.Remove(pair.Key);
                continue;
            }

            var removedHostileCount = pair.Value.HostilePilotNames.Count - remaining.Count;
            var derivedHostileScore = Math.Max(
                remaining.Count,
                pair.Value.HostileScore - removedHostileCount);

            _snapshotBySystemId[pair.Key] = new IntelSystemSnapshot
            {
                SolarSystemId = pair.Value.SolarSystemId,
                SolarSystemName = pair.Value.SolarSystemName,
                LastUpdatedUtc = pair.Value.LastUpdatedUtc,
                LastChannelName = pair.Value.LastChannelName,
                LastReporterName = pair.Value.LastReporterName,
                LastMessageText = pair.Value.LastMessageText,
                ShipClasses = pair.Value.ShipClasses,
                ShipNames = pair.Value.ShipNames,
                ShipTypeIds = pair.Value.ShipTypeIds,
                Alerts = pair.Value.Alerts,
                HostilePilotNames = remaining,
                RecentReports = pair.Value.RecentReports,
                HostileScore = derivedHostileScore,
                IsClear = pair.Value.IsClear
            };
        }
    }

    private static List<string> MergeHostileNames(IReadOnlyList<string> existing, IReadOnlyList<string> incoming)
    {
        var result = new List<string>(existing.Count + incoming.Count);

        foreach (var name in existing)
        {
            if (!result.Any(x => HostileNamesMatch(x, name)))
            {
                result.Add(name);
            }
        }

        foreach (var name in incoming)
        {
            var idx = result.FindIndex(x => HostileNamesMatch(x, name));
            if (idx >= 0)
            {
                // Prefer latest incoming representation so future matching gets fresh tokens.
                result[idx] = name;
            }
            else
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static bool HostileNamesMatch(string a, string b)
    {
        var left = NormalizeHostileName(a);
        var right = NormalizeHostileName(b);
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        var leftParts = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rightParts = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // "John Smith" vs "Smith John"
        if (leftParts.Length == 2 && rightParts.Length == 2 &&
            leftParts[0] == rightParts[1] && leftParts[1] == rightParts[0])
        {
            return true;
        }

        // Single-token fallback for reports that abbreviate one side of a full name.
        if (leftParts.Length == 1 && rightParts.Length == 2)
        {
            return rightParts[0] == leftParts[0] || rightParts[1] == leftParts[0];
        }

        if (leftParts.Length == 2 && rightParts.Length == 1)
        {
            return leftParts[0] == rightParts[0] || leftParts[1] == rightParts[0];
        }

        return false;
    }

    private static string NormalizeHostileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '\'' || c == '-' || char.IsWhiteSpace(c))
            .ToArray());
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    private async Task<string?> ReadHeaderChannelNameAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        for (var i = 0; i < 80; i++)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            var normalized = line.TrimStart('\uFEFF').Trim();
            if (!normalized.StartsWith("Channel Name:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idx = normalized.IndexOf(':');
            if (idx < 0 || idx + 1 >= normalized.Length)
            {
                continue;
            }

            return normalized[(idx + 1)..].Trim();
        }

        return null;
    }

    private async Task InitializeZkillSequenceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateZkillHttpClient();
            var seq = await client.GetFromJsonAsync<ZkillSequenceDto>(ZkillSequenceEndpoint, cancellationToken);
            if (seq?.Sequence is null or <= 0)
            {
                return;
            }

            // Start from the next sequence to avoid historical backfill on startup.
            _nextZkillSequence = seq.Sequence.Value + 1;
            _nextZkillPollAfterUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize zKillboard sequence.");
            _nextZkillPollAfterUtc = DateTime.UtcNow + _zkillPollDelay;
        }
    }

    private async Task PollZkillAsync(CancellationToken cancellationToken)
    {
        if (!_zkillEnabled || _nextZkillSequence is null || DateTime.UtcNow < _nextZkillPollAfterUtc)
        {
            return;
        }

        var client = CreateZkillHttpClient();
        var sequence = _nextZkillSequence.Value;
        var maxBatch = 5;
        var processed = 0;
        while (processed < maxBatch && !cancellationToken.IsCancellationRequested)
        {
            var uri = string.Format(CultureInfo.InvariantCulture, ZkillKillmailEndpointFormat, sequence);
            using var response = await client.GetAsync(uri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _nextZkillSequence = sequence;
                _nextZkillPollAfterUtc = DateTime.UtcNow + ZkillNotFoundDelay;
                return;
            }

            if ((int)response.StatusCode == 429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.Zero;
                _nextZkillSequence = sequence;
                _nextZkillPollAfterUtc = DateTime.UtcNow + (retryAfter > ZkillRateLimitDelay ? retryAfter : ZkillRateLimitDelay);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("zKillboard sequence {Sequence} returned {StatusCode}", sequence, (int)response.StatusCode);
                _nextZkillSequence = sequence;
                _nextZkillPollAfterUtc = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(_zkillPollDelay.TotalSeconds, 10));
                return;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            if (TryBuildZkillIntelReport(document.RootElement, out var report))
            {
                ReportReceived?.Invoke(this, report);
                if (ApplyToSystemSnapshot(report))
                {
                    SnapshotUpdated?.Invoke(this, Snapshot);
                }
            }

            sequence++;
            processed++;
            await Task.Delay(ZkillSuccessDelay, cancellationToken);
        }

        _nextZkillSequence = sequence;
        _nextZkillPollAfterUtc = DateTime.UtcNow + ZkillSuccessDelay;
    }

    private HttpClient CreateZkillHttpClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(IntelChatLogFeedHostedService));
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HISA/1.0 (https://github.com/Crytopox/HISA)");
        }

        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private bool TryBuildZkillIntelReport(JsonElement root, out IntelChatReport report)
    {
        report = default!;

        var killmail = root.TryGetProperty("esi", out var esiKillmail)
            ? esiKillmail
            : (root.TryGetProperty("killmail", out var nestedKillmail) ? nestedKillmail : root);
        if (killmail.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var systemId = ReadInt64(killmail, "solar_system_id");
        var victim = killmail.TryGetProperty("victim", out var victimNode) ? victimNode : default;
        var victimShipTypeId = victim.ValueKind == JsonValueKind.Object ? ReadInt32(victim, "ship_type_id") : null;
        if (systemId is null || victimShipTypeId is null)
        {
            return false;
        }

        if (!_systemNameById.TryGetValue(systemId.Value, out var systemName) || string.IsNullOrWhiteSpace(systemName))
        {
            return false;
        }

        var timestampUtc = ReadDateTime(killmail, "killmail_time") ?? DateTime.UtcNow;
        if (timestampUtc < DateTime.UtcNow - ZkillAcceptedAgeWindow)
        {
            return false;
        }
        var victimShipClass = _shipClassByTypeId.TryGetValue(victimShipTypeId.Value, out var cls) ? cls : IntelShipClass.Unknown;
        var victimShipName = _shipNameByTypeId.TryGetValue(victimShipTypeId.Value, out var shipName) ? shipName : $"Type {victimShipTypeId.Value}";
        var attackerCount = killmail.TryGetProperty("attackers", out var attackersNode) && attackersNode.ValueKind == JsonValueKind.Array
            ? attackersNode.GetArrayLength()
            : 0;
        var zkb = root.TryGetProperty("zkb", out var zkbNode) ? zkbNode : default;
        var value = zkb.ValueKind == JsonValueKind.Object ? ReadDecimal(zkb, "totalValue") ?? 0m : 0m;
        var message = $"Killmail: {victimShipName} destroyed ({attackerCount} attacker{(attackerCount == 1 ? string.Empty : "s")}, {value:N0} ISK).";
        var killmailId = ReadInt64(root, "killmail_id") ?? 0;
        var hash = ReadString(root, "hash");
        var killmailUrl = killmailId > 0 && !string.IsNullOrWhiteSpace(hash)
            ? $"https://zkillboard.com/kill/{killmailId}/"
            : string.Empty;
        var victimCharacterId = victim.ValueKind == JsonValueKind.Object ? ReadInt32(victim, "character_id") : null;
        var victimCorporationId = victim.ValueKind == JsonValueKind.Object ? (ReadInt32(victim, "corporation_id") ?? 0) : 0;
        var victimAllianceId = victim.ValueKind == JsonValueKind.Object ? ReadInt32(victim, "alliance_id") : null;

        var attackers = new List<IntelKillmailAttacker>();
        if (attackersNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var attacker in attackersNode.EnumerateArray())
            {
                if (attacker.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                attackers.Add(new IntelKillmailAttacker
                {
                    Name = ReadString(attacker, "character_name") ?? string.Empty,
                    CharacterId = ReadInt32(attacker, "character_id"),
                    CorporationId = ReadInt32(attacker, "corporation_id") ?? 0,
                    AllianceId = ReadInt32(attacker, "alliance_id"),
                    ShipTypeId = ReadInt32(attacker, "ship_type_id")
                });
            }
        }

        report = new IntelChatReport
        {
            DedupeKey = BuildZkillReportDedupeKey(killmailId, timestampUtc, systemName, message),
            TimestampUtc = timestampUtc,
            ChannelName = ZkillChannelName,
            ReporterName = ZkillReporterName,
            MessageText = message,
            SourceFilePath = ZkillSourcePath,
            Systems = [systemName],
            ShipClasses = victimShipClass == IntelShipClass.Unknown ? [] : [victimShipClass],
            ReportedShipNames = [victimShipName],
            ReportedShipTypeIds = [victimShipTypeId.Value],
            Alerts = [IntelAlertType.Fight],
            ReportedHostileNames = [],
            IsClear = false,
            ReportedHostileCount = Math.Max(1, attackerCount),
            Killmail = new IntelKillmailDetails
            {
                KillmailId = killmailId,
                Hash = hash ?? string.Empty,
                Url = killmailUrl,
                VictimCharacterId = victimCharacterId,
                VictimCorporationId = victimCorporationId,
                VictimAllianceId = victimAllianceId,
                VictimShipTypeId = victimShipTypeId,
                VictimName = ReadString(victim, "character_name") ?? string.Empty,
                TotalValue = value,
                Attackers = attackers
            }
        };

        return true;
    }

    private static string BuildIntelReportDedupeKey(DateTime timestampUtc, string reporter, string message)
    {
        return $"intel:{timestampUtc:O}:{NormalizeReportIdentityPart(reporter)}:{NormalizeReportIdentityPart(message)}";
    }

    private static string BuildZkillReportDedupeKey(long killmailId, DateTime timestampUtc, string systemName, string message)
    {
        if (killmailId > 0)
        {
            return $"killmail:{killmailId}";
        }

        return $"killmail:{timestampUtc:O}:{NormalizeReportIdentityPart(systemName)}:{NormalizeReportIdentityPart(message)}";
    }

    private static string NormalizeReportIdentityPart(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static long? ReadInt64(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(node.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
    }

    private static int? ReadInt32(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(node.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var node))
        {
            return null;
        }

        return node.ValueKind switch
        {
            JsonValueKind.Number when node.TryGetDecimal(out var n) => n,
            JsonValueKind.String when decimal.TryParse(node.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
    }

    private static DateTime? ReadDateTime(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = node.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return node.GetString();
    }

    public override void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatcherChanged;
            _watcher.Created -= OnWatcherChanged;
            _watcher.Renamed -= OnWatcherRenamed;
            _watcher.Dispose();
            _watcher = null;
        }

        base.Dispose();
    }

    [GeneratedRegex(@"^\[\s*(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]\s*(?<reporter>.+?)\s*>\s*(?<message>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex BuildChatLineRegex();

    [GeneratedRegex(@"^(?<channel>.+)_(?<date>\d{8})_(?<time>\d{6})_\d+\.txt$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BuildIntelFileNameRegex();

    private sealed class ZkillSequenceDto
    {
        [JsonPropertyName("sequence")]
        public long? Sequence { get; init; }
    }

}
