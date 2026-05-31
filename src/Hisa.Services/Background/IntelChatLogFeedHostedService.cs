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
    private static readonly TimeSpan DirtyFlushActiveDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan DirtyFlushIdleDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SnapshotExpirySweepInterval = TimeSpan.FromSeconds(5);
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

    private readonly ISettingsService _settingsService;
    private readonly ISdeDatabase _sdeDatabase;
    private readonly ILogger<IntelChatLogFeedHostedService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, byte> _dirtyFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _readOffsetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Path, DateTime LastWriteUtc)> _activeFileByChannel = new(StringComparer.OrdinalIgnoreCase);
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
    private long? _nextZkillSequence;
    private DateTime _nextZkillPollAfterUtc = DateTime.MinValue;
    private HashSet<string> _includeChannels = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _systemExpiry = TimeSpan.FromMinutes(15);
    private readonly DateTime _startupHistoryCutoffUtc = DateTime.UtcNow - TimeSpan.FromMinutes(10);

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
            var nextExpirySweepUtc = DateTime.UtcNow + SnapshotExpirySweepInterval;
            while (!stoppingToken.IsCancellationRequested)
            {
                await FlushDirtyFilesAsync(stoppingToken);
                await PollZkillAsync(stoppingToken);
                var nowUtc = DateTime.UtcNow;
                if (nowUtc >= nextExpirySweepUtc)
                {
                    ExpireSnapshots(nowUtc);
                    nextExpirySweepUtc = nowUtc + SnapshotExpirySweepInterval;
                }

                var delay = _dirtyFiles.IsEmpty ? DirtyFlushIdleDelay : DirtyFlushActiveDelay;
                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
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

            if (e.Name is not null && TryExtractChannelNameFromFileName(e.Name, out channelFromName))
            {
                if (!TryPromoteActiveFile(channelFromName, e.FullPath, out var activePath) ||
                    !string.Equals(activePath, e.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            _dirtyFiles[e.FullPath] = 1;
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

            if (e.Name is not null && TryExtractChannelNameFromFileName(e.Name, out channelFromName))
            {
                if (!TryPromoteActiveFile(channelFromName, e.FullPath, out var activePath) ||
                    !string.Equals(activePath, e.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            _dirtyFiles[e.FullPath] = 1;
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

    private void EnqueueAllKnownFiles(string chatLogsDirectory, bool startupOnlyNewestPerChannel)
    {
        if (startupOnlyNewestPerChannel)
        {
            var newestByChannel = new Dictionary<string, (string Path, DateTime LastWriteUtc)>(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in Directory.EnumerateFiles(chatLogsDirectory, "*.txt", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(filePath);
                if (!IsCandidateIntelFile(fileName))
                {
                    continue;
                }

                if (!TryExtractChannelNameFromFileName(fileName, out var channelFromName) || !ShouldReadChannel(channelFromName))
                {
                    continue;
                }

                var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);
                if (!newestByChannel.TryGetValue(channelFromName, out var existing) || lastWriteUtc > existing.LastWriteUtc)
                {
                    newestByChannel[channelFromName] = (filePath, lastWriteUtc);
                }
            }

            lock (_gate)
            {
                _activeFileByChannel.Clear();
                foreach (var kvp in newestByChannel)
                {
                    _activeFileByChannel[kvp.Key] = kvp.Value;
                }
            }

            foreach (var entry in newestByChannel.Values)
            {
                _dirtyFiles[entry.Path] = 0;
            }

            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(chatLogsDirectory, "*.txt", SearchOption.TopDirectoryOnly))
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

            _dirtyFiles[filePath] = 0;
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
            await ProcessFileAsync(file, cancellationToken);
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

        if (!TryPromoteActiveFile(channelName, filePath, out var activePath) ||
            !string.Equals(activePath, filePath, StringComparison.OrdinalIgnoreCase))
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

            if (timestampUtc < _startupHistoryCutoffUtc)
            {
                continue;
            }

            if (IsChannelMotdMessage(reporter, message))
            {
                continue;
            }

            var report = ParseIntelReport(timestampUtc, channelName, reporter, message, filePath);
            if (report.Systems.Count == 0)
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
        return _includeChannels.Count > 0 && _includeChannels.Contains(channelName);
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

    private bool TryPromoteActiveFile(string channelName, string candidatePath, out string activePath)
    {
        activePath = candidatePath;
        DateTime candidateLastWriteUtc;
        try
        {
            candidateLastWriteUtc = File.GetLastWriteTimeUtc(candidatePath);
        }
        catch
        {
            return false;
        }

        lock (_gate)
        {
            if (!_activeFileByChannel.TryGetValue(channelName, out var existing))
            {
                _activeFileByChannel[channelName] = (candidatePath, candidateLastWriteUtc);
                activePath = candidatePath;
                return true;
            }

            if (string.Equals(existing.Path, candidatePath, StringComparison.OrdinalIgnoreCase))
            {
                if (candidateLastWriteUtc > existing.LastWriteUtc)
                {
                    _activeFileByChannel[channelName] = (candidatePath, candidateLastWriteUtc);
                }

                activePath = candidatePath;
                return true;
            }

            var existingExists = File.Exists(existing.Path);
            var promoteCandidate = !existingExists || candidateLastWriteUtc > existing.LastWriteUtc;
            if (!promoteCandidate && candidateLastWriteUtc == existing.LastWriteUtc)
            {
                // EVE can rotate same-channel files with identical write timestamps at second precision.
                // Prefer lexicographically newer filename to follow rollover immediately.
                var existingName = Path.GetFileName(existing.Path);
                var candidateName = Path.GetFileName(candidatePath);
                if (!string.IsNullOrWhiteSpace(existingName) &&
                    !string.IsNullOrWhiteSpace(candidateName) &&
                    string.Compare(candidateName, existingName, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    promoteCandidate = true;
                }
            }

            if (promoteCandidate)
            {
                _readOffsetsByPath.Remove(existing.Path);
                _activeFileByChannel[channelName] = (candidatePath, candidateLastWriteUtc);
                activePath = candidatePath;
                return true;
            }

            activePath = existing.Path;
            return false;
        }
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

    private IntelChatReport ParseIntelReport(DateTime timestampUtc, string channelName, string reporter, string message, string sourcePath)
    {
        var parsed = _messageParser?.Parse(message) ?? new IntelParseResult
        {
            Systems = [],
            ShipClasses = [],
            ShipNames = [],
            Alerts = [],
            HostileNames = [],
            IsClear = false,
            HostileCount = 0
        };
        var reportedShipTypeIds = ResolveShipTypeIds(parsed.ShipNames);
        return new IntelChatReport
        {
            TimestampUtc = timestampUtc,
            ChannelName = channelName,
            ReporterName = reporter,
            MessageText = message,
            SourceFilePath = sourcePath,
            Systems = parsed.Systems.ToList(),
            ShipClasses = parsed.ShipClasses,
            ReportedShipNames = parsed.ShipNames,
            ReportedShipTypeIds = reportedShipTypeIds,
            Alerts = parsed.Alerts,
            ReportedHostileNames = parsed.HostileNames,
            IsClear = parsed.IsClear,
            ReportedHostileCount = parsed.HostileCount,
            Killmail = null
        };
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
        }

        return changed;
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

            var derivedHostileScore = Math.Max(
                remaining.Count,
                Math.Max(pair.Value.ShipClasses.Count, pair.Value.Alerts.Any(a => a != IntelAlertType.Clear) ? 1 : 0));

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

    [GeneratedRegex(@"^(?<channel>.+)_\d{8}_\d{6}_\d+\.txt$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BuildIntelFileNameRegex();

    private sealed class ZkillSequenceDto
    {
        [JsonPropertyName("sequence")]
        public long? Sequence { get; init; }
    }

}
