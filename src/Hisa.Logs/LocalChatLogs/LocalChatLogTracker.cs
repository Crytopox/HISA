using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Hisa.Core.Models;

namespace Hisa.Logs.LocalChatLogs;

public sealed partial class LocalChatLogTracker : IDisposable
{
    private static readonly Regex SystemChangeRegex = BuildSystemChangeRegex();
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<string, byte> _dirtyFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TrackedCharacterFile> _activeByCharacterId = [];
    private readonly Dictionary<string, TrackedCharacterFile> _activeByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, LocalCharacterSystemChange> _latestByCharacterId = [];
    private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(15);
    private TimeSpan _initialScanLookback = TimeSpan.FromHours(12);
    private HashSet<string> _startupSeedPaths = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private string? _chatLogsDirectory;

    public event EventHandler<LocalCharacterSystemChange>? SystemChanged;

    public IReadOnlyDictionary<int, LocalCharacterSystemChange> Snapshot
    {
        get
        {
            lock (_stateGate)
            {
                return new Dictionary<int, LocalCharacterSystemChange>(_latestByCharacterId);
            }
        }
    }

    public Task StartAsync(string chatLogsDirectory, TimeSpan? initialScanLookback = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatLogsDirectory);

        var fullPath = Path.GetFullPath(chatLogsDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"ChatLogs directory was not found: {fullPath}");
        }

        StopInternal();
        lock (_stateGate)
        {
            _activeByCharacterId.Clear();
            _activeByPath.Clear();
            _latestByCharacterId.Clear();
        }
        _dirtyFiles.Clear();
        _startupSeedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _chatLogsDirectory = fullPath;
        _initialScanLookback = initialScanLookback is { } lb && lb > TimeSpan.Zero
            ? lb
            : TimeSpan.FromHours(12);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetupWatcher(fullPath);
        EnqueueAllKnownFiles(fullPath, treatNewestPerCharacterAsStartupSeed: true);
        _workerTask = Task.Run(() => RunAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var task = _workerTask;
        StopInternal();
        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during normal shutdown.
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var nextScanAtUtc = DateTime.UtcNow;
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                if (now >= nextScanAtUtc && _chatLogsDirectory is not null)
                {
                    EnqueueAllKnownFiles(_chatLogsDirectory, treatNewestPerCharacterAsStartupSeed: false);
                    nextScanAtUtc = now + _scanInterval;
                }

                await FlushDirtyFilesAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during normal shutdown.
        }
    }

    private async Task FlushDirtyFilesAsync(CancellationToken cancellationToken)
    {
        if (_dirtyFiles.IsEmpty)
        {
            return;
        }

        var dirty = _dirtyFiles.ToArray();
        foreach (var entry in dirty)
        {
            _dirtyFiles.TryRemove(entry.Key, out _);
            cancellationToken.ThrowIfCancellationRequested();
            var fromWatcherEvent = entry.Value == 1;
            await ProcessFileAsync(entry.Key, fromWatcherEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task ProcessFileAsync(string fullPath, bool fromWatcherEvent, CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            lock (_stateGate)
            {
                if (_activeByPath.TryGetValue(fullPath, out var removed))
                {
                    _activeByPath.Remove(fullPath);
                    _activeByCharacterId.Remove(removed.CharacterId);
                }
            }
            return Task.CompletedTask;
        }

        var fileName = Path.GetFileName(fullPath);
        if (!LocalChatLogFileName.TryParse(fileName, out var key))
        {
            return Task.CompletedTask;
        }

        if (!fromWatcherEvent && !ShouldTrackSessionFromInitialScan(key, fullPath))
        {
            return Task.CompletedTask;
        }

        var created = false;
        var replaced = false;
        TrackedCharacterFile tracked;
        lock (_stateGate)
        {
            if (_activeByCharacterId.TryGetValue(key.CharacterId, out var existing))
            {
                if (existing.SessionStartedUtc > key.SessionStartedUtc)
                {
                    return Task.CompletedTask;
                }

                if (existing.SessionStartedUtc < key.SessionStartedUtc)
                {
                    _activeByPath.Remove(existing.FilePath);
                    tracked = new TrackedCharacterFile
                    {
                        CharacterId = key.CharacterId,
                        SessionStartedUtc = key.SessionStartedUtc,
                        FilePath = fullPath
                    };
                    _activeByCharacterId[key.CharacterId] = tracked;
                    _activeByPath[fullPath] = tracked;
                    replaced = true;
                }
                else if (!string.Equals(existing.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _activeByPath.Remove(existing.FilePath);
                    tracked = new TrackedCharacterFile
                    {
                        CharacterId = key.CharacterId,
                        SessionStartedUtc = existing.SessionStartedUtc,
                        FilePath = fullPath,
                        ReadOffset = existing.ReadOffset,
                        CharacterName = existing.CharacterName
                    };
                    _activeByCharacterId[key.CharacterId] = tracked;
                    _activeByPath[fullPath] = tracked;
                    replaced = true;
                }
                else
                {
                    tracked = existing;
                }
            }
            else
            {
                tracked = new TrackedCharacterFile
                {
                    CharacterId = key.CharacterId,
                    SessionStartedUtc = key.SessionStartedUtc,
                    FilePath = fullPath
                };
                _activeByCharacterId[key.CharacterId] = tracked;
                _activeByPath[fullPath] = tracked;
                created = true;
            }
        }

        if (created || replaced)
        {
            InitializeTrackedFile(tracked);
        }

        ReadNewEntries(tracked);
        return Task.CompletedTask;
    }

    private void InitializeTrackedFile(TrackedCharacterFile tracked)
    {
        using var stream = OpenForRead(tracked.FilePath);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        string? line;
        var inHeader = false;
        while ((line = reader.ReadLine()) is not null)
        {
            var normalized = NormalizeLine(line);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (normalized.StartsWith("------------------------------------------------------------", StringComparison.Ordinal))
            {
                inHeader = !inHeader;
                if (!inHeader)
                {
                    break;
                }

                continue;
            }

            if (!inHeader)
            {
                continue;
            }

            var separator = normalized.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = normalized[..separator].Trim();
            if (!string.Equals(key, "Listener", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            tracked.CharacterName = normalized[(separator + 1)..].Trim();
        }

        stream.Position = 0;
        using var tailReader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string? lastSystem = null;
        DateTime lastTimestamp = default;
        while ((line = tailReader.ReadLine()) is not null)
        {
            if (TryParseSystemChange(line, out var systemName, out var timestampUtc))
            {
                lastSystem = systemName;
                lastTimestamp = timestampUtc;
            }
        }

        tracked.ReadOffset = stream.Position;

        if (!string.IsNullOrWhiteSpace(lastSystem))
        {
            Publish(new LocalCharacterSystemChange
            {
                CharacterId = tracked.CharacterId,
                CharacterName = tracked.CharacterName ?? string.Empty,
                SolarSystemName = lastSystem,
                TimestampUtc = lastTimestamp,
                SourceFilePath = tracked.FilePath
            });
        }
    }

    private void ReadNewEntries(TrackedCharacterFile tracked)
    {
        using var stream = OpenForRead(tracked.FilePath);
        if (tracked.ReadOffset > stream.Length)
        {
            tracked.ReadOffset = 0;
        }

        stream.Position = tracked.ReadOffset;
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!TryParseSystemChange(line, out var systemName, out var timestampUtc))
            {
                continue;
            }

            Publish(new LocalCharacterSystemChange
            {
                CharacterId = tracked.CharacterId,
                CharacterName = tracked.CharacterName ?? string.Empty,
                SolarSystemName = systemName,
                TimestampUtc = timestampUtc,
                SourceFilePath = tracked.FilePath
            });
        }

        tracked.ReadOffset = stream.Position;
    }

    private void Publish(LocalCharacterSystemChange change)
    {
        var shouldEmit = false;
        lock (_stateGate)
        {
            if (_latestByCharacterId.TryGetValue(change.CharacterId, out var last))
            {
                if (last.SolarSystemName == change.SolarSystemName && last.TimestampUtc >= change.TimestampUtc)
                {
                    return;
                }
            }

            _latestByCharacterId[change.CharacterId] = change;
            shouldEmit = true;
        }

        if (shouldEmit)
        {
            SystemChanged?.Invoke(this, change);
        }
    }

    private void SetupWatcher(string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };
        watcher.Filters.Add("Local_*.txt");
        watcher.Changed += OnWatcherChanged;
        watcher.Created += OnWatcherChanged;
        watcher.Renamed += OnWatcherRenamed;
        watcher.Deleted += OnWatcherChanged;
        watcher.Error += OnWatcherError;
        _watcher = watcher;
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.FullPath))
        {
            _dirtyFiles.AddOrUpdate(e.FullPath, 1, (_, old) => old == 1 ? (byte)1 : (byte)1);
        }
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.OldFullPath))
        {
            _dirtyFiles.AddOrUpdate(e.OldFullPath, 1, (_, old) => old == 1 ? (byte)1 : (byte)1);
        }

        if (!string.IsNullOrWhiteSpace(e.FullPath))
        {
            _dirtyFiles.AddOrUpdate(e.FullPath, 1, (_, old) => old == 1 ? (byte)1 : (byte)1);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (_chatLogsDirectory is not null)
        {
            EnqueueAllKnownFiles(_chatLogsDirectory, treatNewestPerCharacterAsStartupSeed: false);
        }
    }

    private void EnqueueAllKnownFiles(string directory, bool treatNewestPerCharacterAsStartupSeed)
    {
        var newestFileByCharacterId = new Dictionary<int, (string Path, DateTime SessionStartedUtc)>();
        foreach (var filePath in Directory.EnumerateFiles(directory, "Local_*.txt", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(filePath);
            if (!LocalChatLogFileName.TryParse(fileName, out var key))
            {
                continue;
            }

            if (!newestFileByCharacterId.TryGetValue(key.CharacterId, out var existing) ||
                key.SessionStartedUtc > existing.SessionStartedUtc ||
                (key.SessionStartedUtc == existing.SessionStartedUtc &&
                 string.Compare(filePath, existing.Path, StringComparison.OrdinalIgnoreCase) > 0))
            {
                newestFileByCharacterId[key.CharacterId] = (filePath, key.SessionStartedUtc);
            }
        }

        HashSet<string>? startupSeedPaths = null;
        if (treatNewestPerCharacterAsStartupSeed)
        {
            startupSeedPaths = newestFileByCharacterId.Values
                .Select(x => x.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            lock (_stateGate)
            {
                _startupSeedPaths = startupSeedPaths;
            }
        }

        foreach (var newest in newestFileByCharacterId.Values)
        {
            var fromWatcherEvent = startupSeedPaths is null || !startupSeedPaths.Contains(newest.Path);
            _dirtyFiles.AddOrUpdate(newest.Path, fromWatcherEvent ? (byte)1 : (byte)0, (_, old) =>
            {
                if (old == 1 || fromWatcherEvent)
                {
                    return 1;
                }

                return 0;
            });
        }
    }

    private bool ShouldTrackSessionFromInitialScan(LocalChatLogFileKey sessionKey, string fullPath)
    {
        lock (_stateGate)
        {
            if (_startupSeedPaths.Contains(fullPath))
            {
                return true;
            }
        }

        lock (_stateGate)
        {
            if (_activeByPath.ContainsKey(fullPath) || _activeByCharacterId.ContainsKey(sessionKey.CharacterId))
            {
                return true;
            }
        }

        var sessionCutoffUtc = DateTime.UtcNow - _initialScanLookback;
        return sessionKey.SessionStartedUtc >= sessionCutoffUtc;
    }

    private static FileStream OpenForRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    private static string NormalizeLine(string line)
    {
        return line.TrimStart('\uFEFF').Trim();
    }

    private static bool TryParseSystemChange(string rawLine, out string solarSystemName, out DateTime timestampUtc)
    {
        solarSystemName = string.Empty;
        timestampUtc = default;

        var line = NormalizeLine(rawLine);
        if (line.Length == 0)
        {
            return false;
        }

        var match = SystemChangeRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var timestampText = match.Groups["timestamp"].Value;
        if (!DateTime.TryParseExact(timestampText, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        var system = match.Groups["system"].Value.Trim();
        if (system.Length == 0)
        {
            return false;
        }

        timestampUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        solarSystemName = system;
        return true;
    }

    private void StopInternal()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _workerTask = null;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatcherChanged;
            _watcher.Created -= OnWatcherChanged;
            _watcher.Renamed -= OnWatcherRenamed;
            _watcher.Deleted -= OnWatcherChanged;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        StopInternal();
    }

    [GeneratedRegex(@"^\[\s*(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]\s*EVE System\s*>\s*Channel changed to Local\s*:\s*(?<system>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex BuildSystemChangeRegex();

    private sealed class TrackedCharacterFile
    {
        public required int CharacterId { get; init; }
        public required DateTime SessionStartedUtc { get; init; }
        public required string FilePath { get; init; }
        public long ReadOffset { get; set; }
        public string? CharacterName { get; set; }
    }
}
