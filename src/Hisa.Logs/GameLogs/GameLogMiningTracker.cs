using System.Collections.Concurrent;
using Hisa.Core.Models;

namespace Hisa.Logs.GameLogs;

public sealed class GameLogMiningTracker : IDisposable
{
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<string, byte> _dirtyFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TrackedGameLogFile> _activeByCharacterId = [];
    private readonly Dictionary<string, TrackedGameLogFile> _activeByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MiningSessionSnapshot> _snapshotByCharacterId = [];
    private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(15);
    private TimeSpan _initialScanLookback = TimeSpan.FromHours(24);

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private string? _gameLogsDirectory;

    public event EventHandler<IReadOnlyDictionary<int, MiningSessionSnapshot>>? SnapshotUpdated;

    public IReadOnlyDictionary<int, MiningSessionSnapshot> Snapshot
    {
        get
        {
            lock (_stateGate)
            {
                return new Dictionary<int, MiningSessionSnapshot>(_snapshotByCharacterId);
            }
        }
    }

    public Task StartAsync(string gameLogsDirectory, TimeSpan? initialScanLookback = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameLogsDirectory);

        var fullPath = Path.GetFullPath(gameLogsDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"GameLogs directory was not found: {fullPath}");
        }

        StopInternal();
        lock (_stateGate)
        {
            _activeByCharacterId.Clear();
            _activeByPath.Clear();
            _snapshotByCharacterId.Clear();
        }
        _dirtyFiles.Clear();

        _gameLogsDirectory = fullPath;
        _initialScanLookback = initialScanLookback is { } lb && lb > TimeSpan.Zero ? lb : TimeSpan.FromHours(24);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetupWatcher(fullPath);
        EnqueueAllKnownFiles(fullPath, fromWatcherEvent: false);
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
                if (now >= nextScanAtUtc && _gameLogsDirectory is not null)
                {
                    EnqueueAllKnownFiles(_gameLogsDirectory, fromWatcherEvent: false);
                    nextScanAtUtc = now + _scanInterval;
                }

                await FlushDirtyFilesAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FlushDirtyFilesAsync(CancellationToken cancellationToken)
    {
        if (_dirtyFiles.IsEmpty)
        {
            return;
        }

        foreach (var entry in _dirtyFiles.ToArray())
        {
            _dirtyFiles.TryRemove(entry.Key, out _);
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessFileAsync(entry.Key, entry.Value == 1, cancellationToken).ConfigureAwait(false);
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
                    _snapshotByCharacterId.Remove(removed.CharacterId);
                }
            }

            PublishSnapshot();
            return Task.CompletedTask;
        }

        var fileName = Path.GetFileName(fullPath);
        if (!GameLogFileName.TryParse(fileName, out var key))
        {
            return Task.CompletedTask;
        }

        if (!fromWatcherEvent && !ShouldTrackSessionFromInitialScan(key, fullPath))
        {
            return Task.CompletedTask;
        }

        var created = false;
        var replaced = false;
        TrackedGameLogFile tracked;
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
                    tracked = new TrackedGameLogFile(key.CharacterId, key.SessionStartedUtc, fullPath);
                    _activeByCharacterId[key.CharacterId] = tracked;
                    _activeByPath[fullPath] = tracked;
                    _snapshotByCharacterId.Remove(key.CharacterId);
                    replaced = true;
                }
                else if (!string.Equals(existing.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _activeByPath.Remove(existing.FilePath);
                    tracked = existing with { FilePath = fullPath };
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
                tracked = new TrackedGameLogFile(key.CharacterId, key.SessionStartedUtc, fullPath);
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

    private void InitializeTrackedFile(TrackedGameLogFile tracked)
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

            var listener = GameLogMiningParser.TryParseListener(normalized);
            if (!string.IsNullOrWhiteSpace(listener))
            {
                tracked.CharacterName = listener;
            }
        }

        stream.Position = 0;
        using var replayReader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        while ((line = replayReader.ReadLine()) is not null)
        {
            if (GameLogMiningParser.TryParseMiningEvent(line, out var miningEvent))
            {
                ApplyEvent(tracked, miningEvent);
            }
        }

        tracked.ReadOffset = stream.Position;
        PublishTrackedSnapshot(tracked);
    }

    private void ReadNewEntries(TrackedGameLogFile tracked)
    {
        using var stream = OpenForRead(tracked.FilePath);
        if (tracked.ReadOffset > stream.Length)
        {
            tracked.ReadOffset = 0;
        }

        stream.Position = tracked.ReadOffset;
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string? line;
        var changed = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!GameLogMiningParser.TryParseMiningEvent(line, out var miningEvent))
            {
                continue;
            }

            ApplyEvent(tracked, miningEvent);
            changed = true;
        }

        tracked.ReadOffset = stream.Position;
        if (changed)
        {
            PublishTrackedSnapshot(tracked);
        }
    }

    private static void ApplyEvent(TrackedGameLogFile tracked, MiningLogEvent miningEvent)
    {
        tracked.LastActivityUtc = miningEvent.TimestampUtc;
        switch (miningEvent.Kind)
        {
            case MiningLogEventKind.Yield:
                var ore = GetOrCreateOre(tracked, miningEvent.OreName);
                ore.MinedUnits += miningEvent.Units;
                if (miningEvent.IsCriticalBonus)
                {
                    ore.BonusUnits += miningEvent.Units;
                }
                ore.LastMinedUtc = miningEvent.TimestampUtc;
                if (tracked.CurrentEfficiencyPercent is { } efficiencyPercent)
                {
                    ore.LastKnownEfficiencyPercent = efficiencyPercent;
                }
                break;
            case MiningLogEventKind.Residue:
                var residueOre = GetOrCreateOre(tracked, tracked.LastOreName);
                residueOre.WasteUnits += miningEvent.Units;
                break;
            case MiningLogEventKind.SiteEfficiencyChanged:
                tracked.CurrentEfficiencyPercent = miningEvent.EfficiencyPercent;
                tracked.LastOreName = miningEvent.OreName;
                break;
        }

        if (!string.IsNullOrWhiteSpace(miningEvent.OreName))
        {
            tracked.LastOreName = miningEvent.OreName;
        }
    }

    private static MutableOreTotals GetOrCreateOre(TrackedGameLogFile tracked, string? oreName)
    {
        var key = string.IsNullOrWhiteSpace(oreName) ? "Unknown" : oreName.Trim();
        if (!tracked.OresByName.TryGetValue(key, out var ore))
        {
            ore = new MutableOreTotals { OreName = key };
            tracked.OresByName[key] = ore;
        }

        return ore;
    }

    private void PublishTrackedSnapshot(TrackedGameLogFile tracked)
    {
        var ores = tracked.OresByName.Values
            .Where(x => x.MinedUnits > 0 || x.BonusUnits > 0)
            .OrderByDescending(x => x.MinedUnits + x.BonusUnits)
            .Select(x => new MiningOreTotals
            {
                OreName = x.OreName,
                MinedUnits = x.MinedUnits,
                BonusUnits = x.BonusUnits,
                WasteUnits = x.WasteUnits,
                LastMinedUtc = x.LastMinedUtc == default ? tracked.SessionStartedUtc : x.LastMinedUtc,
                LastKnownEfficiencyPercent = x.LastKnownEfficiencyPercent
            })
            .ToList();

        if (ores.Count == 0)
        {
            return;
        }

        lock (_stateGate)
        {
            _snapshotByCharacterId[tracked.CharacterId] = new MiningSessionSnapshot
            {
                CharacterId = tracked.CharacterId,
                CharacterName = tracked.CharacterName ?? $"Character {tracked.CharacterId}",
                SessionStartedUtc = tracked.SessionStartedUtc,
                LastActivityUtc = tracked.LastActivityUtc == default ? tracked.SessionStartedUtc : tracked.LastActivityUtc,
                SourceFilePath = tracked.FilePath,
                CurrentEfficiencyPercent = tracked.CurrentEfficiencyPercent,
                Ores = ores
            };
        }

        PublishSnapshot();
    }

    private void PublishSnapshot() => SnapshotUpdated?.Invoke(this, Snapshot);

    private void SetupWatcher(string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };
        watcher.Filters.Add("*.txt");
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
            _dirtyFiles[e.FullPath] = 1;
        }
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.OldFullPath))
        {
            _dirtyFiles[e.OldFullPath] = 1;
        }

        if (!string.IsNullOrWhiteSpace(e.FullPath))
        {
            _dirtyFiles[e.FullPath] = 1;
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        if (_gameLogsDirectory is not null)
        {
            EnqueueAllKnownFiles(_gameLogsDirectory, fromWatcherEvent: false);
        }
    }

    private void EnqueueAllKnownFiles(string directory, bool fromWatcherEvent)
    {
        var newestFileByCharacterId = new Dictionary<int, (string Path, DateTime SessionStartedUtc)>();
        foreach (var filePath in Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(filePath);
            if (!GameLogFileName.TryParse(fileName, out var key))
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

        foreach (var newest in newestFileByCharacterId.Values)
        {
            _dirtyFiles.AddOrUpdate(newest.Path, fromWatcherEvent ? (byte)1 : (byte)0, (_, old) => old == 1 || fromWatcherEvent ? (byte)1 : (byte)0);
        }
    }

    private bool ShouldTrackSessionFromInitialScan(GameLogFileKey sessionKey, string fullPath)
    {
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

    private static FileStream OpenForRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static string NormalizeLine(string line) => line.TrimStart('\uFEFF').Trim();

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

    public void Dispose() => StopInternal();

    private sealed class MutableOreTotals
    {
        public string OreName { get; init; } = string.Empty;
        public long MinedUnits { get; set; }
        public long BonusUnits { get; set; }
        public long WasteUnits { get; set; }
        public DateTime LastMinedUtc { get; set; }
        public int? LastKnownEfficiencyPercent { get; set; }
    }

    private sealed record TrackedGameLogFile(int CharacterId, DateTime SessionStartedUtc, string FilePath)
    {
        public long ReadOffset { get; set; }
        public string? CharacterName { get; set; }
        public string? LastOreName { get; set; }
        public DateTime LastActivityUtc { get; set; }
        public int? CurrentEfficiencyPercent { get; set; }
        public Dictionary<string, MutableOreTotals> OresByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
