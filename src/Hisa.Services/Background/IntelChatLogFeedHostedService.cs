using System.Collections.Concurrent;
using System.Globalization;
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
    private const string LogsRootSettingsKey = "Tracking.LogsRootPath";
    private const string IntelEnabledSettingsKey = "Intel.Enabled";
    private const string IntelIncludeChannelsSettingsKey = "Intel.Channels.Include";
    private const string IntelIgnoreChannelsSettingsKey = "Intel.Channels.Ignore";
    private const string IntelSystemExpiryMinutesSettingsKey = "Intel.SystemExpiryMinutes";
    private const string IntelClearOverlayMinutesSettingsKey = "Intel.ClearOverlayMinutes";

    private static readonly Regex ChatLineRegex = BuildChatLineRegex();
    private static readonly Regex IntelFileNameRegex = BuildIntelFileNameRegex();

    private readonly ISettingsService _settingsService;
    private readonly ISdeDatabase _sdeDatabase;
    private readonly ILogger<IntelChatLogFeedHostedService> _logger;
    private readonly ConcurrentDictionary<string, byte> _dirtyFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _readOffsetsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, IntelSystemSnapshot> _snapshotBySystemId = [];
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Dictionary<string, long> _systemIdByName = new(StringComparer.OrdinalIgnoreCase);
    private IntelChatMessageParser? _messageParser;
    private bool _enabled = true;
    private HashSet<string> _includeChannels = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _ignoreChannels = new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _systemExpiry = TimeSpan.FromMinutes(15);
    private TimeSpan _clearOverlayExpiry = TimeSpan.FromMinutes(5);

    public IntelChatLogFeedHostedService(
        ISettingsService settingsService,
        ISdeDatabase sdeDatabase,
        ILogger<IntelChatLogFeedHostedService> logger)
    {
        _settingsService = settingsService;
        _sdeDatabase = sdeDatabase;
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
        _messageParser = new IntelChatMessageParser(_systemIdByName);
        var chatLogsDirectory = await ResolveChatLogsDirectoryAsync(stoppingToken);
        if (chatLogsDirectory is null)
        {
            _logger.LogWarning("Intel chat feed disabled: ChatLogs directory was not found.");
            return;
        }

        _logger.LogInformation("Starting intel chat feed from: {Path}", chatLogsDirectory);
        SetupWatcher(chatLogsDirectory);
        EnqueueAllKnownFiles(chatLogsDirectory);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await FlushDirtyFilesAsync(stoppingToken);
                ExpireSnapshots(DateTime.UtcNow);
                await Task.Delay(250, stoppingToken);
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
        var include = await _settingsService.GetAsync<List<string>>(IntelIncludeChannelsSettingsKey, cancellationToken) ?? [];
        var ignore = await _settingsService.GetAsync<List<string>>(IntelIgnoreChannelsSettingsKey, cancellationToken) ?? [];
        _includeChannels = include.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ignoreChannels = ignore.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expiryMinutes = Math.Clamp(await _settingsService.GetAsync<int?>(IntelSystemExpiryMinutesSettingsKey, cancellationToken) ?? 15, 1, 180);
        var clearMinutes = Math.Clamp(await _settingsService.GetAsync<int?>(IntelClearOverlayMinutesSettingsKey, cancellationToken) ?? 5, 1, 60);
        _systemExpiry = TimeSpan.FromMinutes(expiryMinutes);
        _clearOverlayExpiry = TimeSpan.FromMinutes(clearMinutes);
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

        return result;
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
        _watcher.Error += (_, _) => EnqueueAllKnownFiles(chatLogsDirectory);
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

    private void EnqueueAllKnownFiles(string chatLogsDirectory)
    {
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

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long offset;
        lock (_gate)
        {
            _readOffsetsByPath.TryGetValue(filePath, out offset);
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

            var report = ParseIntelReport(timestampUtc, channelName, reporter, message, filePath);
            if (report.Systems.Count == 0)
            {
                continue;
            }

            ReportReceived?.Invoke(this, report);
            ApplyToSystemSnapshot(report);
        }

        lock (_gate)
        {
            _readOffsetsByPath[filePath] = stream.Position;
        }
    }

    private bool ShouldReadChannel(string channelName)
    {
        if (_ignoreChannels.Contains(channelName))
        {
            return false;
        }

        return _includeChannels.Count > 0 && _includeChannels.Contains(channelName);
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

    private IntelChatReport ParseIntelReport(DateTime timestampUtc, string channelName, string reporter, string message, string sourcePath)
    {
        var parsed = _messageParser?.Parse(message) ?? new IntelParseResult
        {
            Systems = [],
            ShipClasses = [],
            Alerts = [],
            IsClear = false
        };
        return new IntelChatReport
        {
            TimestampUtc = timestampUtc,
            ChannelName = channelName,
            ReporterName = reporter,
            MessageText = message,
            SourceFilePath = sourcePath,
            Systems = parsed.Systems.ToList(),
            ShipClasses = parsed.ShipClasses,
            Alerts = parsed.Alerts,
            IsClear = parsed.IsClear
        };
    }

    private void ApplyToSystemSnapshot(IntelChatReport report)
    {
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
                        .Take(2)
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
                        Alerts = [IntelAlertType.Clear],
                        RecentReports = recentReports,
                        HostileScore = 0,
                        IsClear = true
                    };
                    continue;
                }

                var previous = _snapshotBySystemId.TryGetValue(systemId, out var existingSnapshot)
                    ? existingSnapshot.RecentReports
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
                    .Take(2)
                    .ToList();
                var hostileScore = Math.Max(1, report.ShipClasses.Count + report.Alerts.Count(a => a != IntelAlertType.Clear));
                _snapshotBySystemId[systemId] = new IntelSystemSnapshot
                {
                    SolarSystemId = systemId,
                    SolarSystemName = systemName,
                    LastUpdatedUtc = report.TimestampUtc,
                    LastChannelName = report.ChannelName,
                    LastReporterName = report.ReporterName,
                    LastMessageText = report.MessageText,
                    ShipClasses = report.ShipClasses,
                    Alerts = report.Alerts,
                    RecentReports = reports,
                    HostileScore = hostileScore,
                    IsClear = false
                };
            }
        }

        SnapshotUpdated?.Invoke(this, Snapshot);
    }

    private void ExpireSnapshots(DateTime nowUtc)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var pair in _snapshotBySystemId.ToList())
            {
                var age = nowUtc - pair.Value.LastUpdatedUtc;
                var maxAge = pair.Value.IsClear ? _clearOverlayExpiry : _systemExpiry;
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

}
