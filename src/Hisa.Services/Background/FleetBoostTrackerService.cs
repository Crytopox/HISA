using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Logs.ChatLogs;
using Hisa.Logs.FleetBoosts;
using Hisa.Logs.LocalChatLogs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Background;

/// <summary>Independent chat-log consumer. All I/O mutations are serialized with session resets.</summary>
public sealed class FleetBoostTrackerService(ISettingsService settings, ILogger<FleetBoostTrackerService> logger)
    : BackgroundService, IFleetBoostTracker
{
    public const string ChannelsKey = "FleetBoost.Channels";
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly object _gate = new();
    private string[] _channels = [];
    private readonly Dictionary<string, ChannelState> _states = new(StringComparer.OrdinalIgnoreCase);
    private sealed class ChannelState
    {
        public FleetBoostSession? Session;
        public string Status = "Not started";
        public readonly Dictionary<string, Cursor> Files = new(StringComparer.OrdinalIgnoreCase);
    }
    private sealed record Cursor(long Offset, DateTime CreatedUtc, byte[] Tail, bool SkipPartial, DateTime WrittenUtc);
    public event EventHandler? Changed;
    public IReadOnlyList<string> Channels { get { lock (_gate) return _channels.ToArray(); } }
    public FleetBoostSnapshot GetSnapshot(string channel)
    {
        lock (_gate)
            return _states.TryGetValue(channel, out var state)
                ? new(channel, state.Session?.StartedUtc, state.Session?.Pilots ?? [], state.Status)
                : new(channel, null, [], "Not configured");
    }

    public async Task ApplySettingsAsync(CancellationToken cancellationToken = default)
    {
        await _io.WaitAsync(cancellationToken);
        try
        {
            var channels = await settings.GetAsync<string[]>(ChannelsKey, cancellationToken) ?? [];
            lock (_gate)
            {
                _channels = channels.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var name in _states.Keys.Except(_channels, StringComparer.OrdinalIgnoreCase).ToArray()) _states.Remove(name);
                foreach (var name in _channels) _states.TryAdd(name, new());
            }
        }
        finally { _io.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task StartOrResetAsync(string channel, CancellationToken cancellationToken = default)
    {
        await _io.WaitAsync(cancellationToken);
        try
        {
            ChannelState state;
            lock (_gate)
            {
                if (!_states.TryGetValue(channel, out state!)) return;
                // The new object is the session generation; old I/O cannot cross this boundary.
                state.Session = new(DateTime.UtcNow);
                state.Files.Clear();
                state.Status = "Starting…";
            }
            var directory = await ResolveDirectoryAsync(cancellationToken);
            if (directory is not null)
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.txt")
                             .Where(p => string.Equals(ChatLogFiles.ChannelFromPath(p), channel, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        using var stream = Open(path);
                        var length = stream.Length;
                        var tail = ReadTail(stream, length);
                        var partial = length > 0 && !EndsInNewline(stream, length);
                        state.Files[path] = new(length, File.GetCreationTimeUtc(path), tail, partial, File.GetLastWriteTimeUtc(path));
                        // Remember old copies in the boundary second, without registering a pilot.
                        var (lines, _) = await ChatLogFiles.ReadCompleteChannelLinesAsync(stream, Math.Max(0, length - 512 * 1024), cancellationToken, length);
                        lock (_gate)
                            foreach (var line in lines)
                                if (FleetBoostMessageParser.Parse(line) is { } message) state.Session!.RememberExisting(message);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogDebug(ex, "Unable to baseline boost log {Path}", path);
                        // A file we could not baseline must never replay its boundary second.
                        state.Files[path] = new(-1, default, [], false, default);
                    }
                }
            }
            lock (_gate) state.Status = directory is null ? "Logs unavailable — check Preferences" : "Waiting for channel log";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate) if (_states.TryGetValue(channel, out var state)) state.Status = "Logs unavailable — retrying";
            logger.LogDebug(ex, "Unable to start boost channel {Channel}", channel);
        }
        finally { _io.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemovePilotAsync(string channel, string pilot, CancellationToken cancellationToken = default)
    {
        await _io.WaitAsync(cancellationToken);
        try { lock (_gate) if (_states.TryGetValue(channel, out var state)) state.Session?.Remove(pilot); }
        finally { _io.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ApplySettingsAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await ScanOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    // Polling both discovers new files and recovers from missed filesystem notifications.
    public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        var changed = false;
        await _io.WaitAsync(cancellationToken);
        try
        {
            KeyValuePair<string, ChannelState>[] active;
            lock (_gate) active = _states.Where(p => p.Value.Session is not null).ToArray();
            if (active.Length == 0) return;
            var directory = await ResolveDirectoryAsync(cancellationToken);
            var paths = directory is null ? [] : Directory.GetFiles(directory, "*.txt");
            foreach (var (channel, state) in active)
            {
                var reports = new List<FleetBoostMessage>();
                var sources = 0;
                var failures = 0;
                foreach (var path in paths.Where(p => string.Equals(ChatLogFiles.ChannelFromPath(p), channel, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var info = new FileInfo(path);
                        var created = info.CreationTimeUtc;
                        state.Files.TryGetValue(path, out var cursor);
                        if (cursor is not null && cursor.Offset == info.Length && cursor.CreatedUtc == created && cursor.WrittenUtc == info.LastWriteTimeUtc)
                        { sources++; continue; }
                        using var stream = Open(path);
                        if (cursor?.Offset == -1)
                        {
                            // Retry a failed baseline conservatively: only future complete lines.
                            state.Files[path] = new(stream.Length, created, ReadTail(stream, stream.Length), !EndsInNewline(stream, stream.Length), info.LastWriteTimeUtc);
                            sources++;
                            continue;
                        }
                        var offset = cursor?.Offset ?? 0;
                        var skip = cursor?.SkipPartial ?? false;
                        if (cursor is not null && (created != cursor.CreatedUtc || stream.Length < offset ||
                            !ReadTail(stream, offset).SequenceEqual(cursor.Tail)))
                        {
                            offset = 0;
                            skip = false;
                        }
                        var (lines, next) = await ChatLogFiles.ReadCompleteChannelLinesAsync(stream, offset, cancellationToken);
                        foreach (var line in lines)
                        {
                            if (skip) { skip = false; continue; }
                            if (FleetBoostMessageParser.Parse(line) is { } message) reports.Add(message);
                        }
                        state.Files[path] = new(next, created, ReadTail(stream, next), skip, info.LastWriteTimeUtc);
                        sources++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures++;
                        logger.LogDebug(ex, "Unable to read boost log {Path}", path);
                    }
                }
                lock (_gate)
                {
                    foreach (var report in reports.OrderBy(r => r.TimestampUtc)) changed |= state.Session!.Apply(report);
                    var status = directory is null ? "Logs unavailable — check Preferences"
                        : sources > 0 ? "Listening" : failures > 0 ? "Channel logs unavailable — retrying" : "Waiting for channel log";
                    changed |= state.Status != status;
                    state.Status = status;
                }
                foreach (var stale in state.Files.Keys.Where(p => !File.Exists(p)).ToArray()) state.Files.Remove(stale);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
                foreach (var state in _states.Values.Where(s => s.Session is not null))
                { changed |= state.Status != "Logs unavailable — retrying"; state.Status = "Logs unavailable — retrying"; }
            logger.LogDebug(ex, "Boost log discovery failed");
        }
        finally { _io.Release(); }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<string?> ResolveDirectoryAsync(CancellationToken cancellationToken)
    {
        var configured = await settings.GetAsync<string>("Tracking.LogsRootPath", cancellationToken);
        foreach (var root in new[] { configured, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs") })
        {
            var directory = LocalChatLogsPathValidator.ResolveChatLogsPath(root);
            if (directory is not null && Directory.Exists(directory)) return directory;
        }
        return null;
    }
    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    private static byte[] ReadTail(FileStream stream, long offset)
    {
        var count = (int)Math.Min(32, Math.Min(offset, stream.Length));
        stream.Position = Math.Max(0, offset - count);
        var bytes = new byte[count];
        stream.ReadExactly(bytes);
        return bytes;
    }
    private static bool EndsInNewline(FileStream stream, long length)
    {
        var tail = ReadTail(stream, length);
        return tail.Length == 0 || tail[^1] == 10 || (tail.Length >= 2 && tail[^2] == 10 && tail[^1] == 0);
    }
}
