namespace Hisa.Services.Background;

public sealed partial class IntelChatLogFeedHostedService
{
    private sealed record ChannelFile(string Path, string Channel, DateTime SessionUtc, DateTime WrittenUtc, DateTime ActivityUtc, DateTime CreatedUtc, long Length);
    private readonly Dictionary<string, ChannelFile> _channelFileCandidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChannelFile> _processedChannelFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _activeChannelFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _unavailableChannelFiles = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshChannelFile(string path, bool observedWrite = false)
    {
        if (!IsCandidateIntelFile(System.IO.Path.GetFileName(path)) ||
            !TryParseIntelFileName(System.IO.Path.GetFileName(path), out var channel, out var session)) return;
        try
        {
            var info = new FileInfo(path);
            lock (_gate)
            {
                if (!info.Exists || !ShouldReadChannel(channel))
                {
                    _channelFileCandidates.Remove(path);
                    _readOffsetsByPath.Remove(path);
                    _processedChannelFiles.Remove(path);
                    _unavailableChannelFiles.Remove(path);
                    return;
                }
                _channelFileCandidates.TryGetValue(path, out var previous);
                var activity = info.LastWriteTimeUtc;
                // Some clients keep handles open and their last-write timestamp lags behind actual
                // writes. Watcher notifications and observed size changes are activity too.
                if (previous is not null && previous.ActivityUtc > activity) activity = previous.ActivityUtc;
                if (observedWrite || (previous is not null && previous.Length != info.Length))
                    if (DateTime.UtcNow > activity) activity = DateTime.UtcNow;
                _channelFileCandidates[path] = new(path, channel, session, info.LastWriteTimeUtc, activity, info.CreationTimeUtc, info.Length);
            }
        }
        catch (IOException) { /* Retry on the next watcher event or safety poll. */ }
        catch (UnauthorizedAccessException) { /* Another account may still provide this channel. */ }
    }

    private IReadOnlyList<ChannelFile> SelectChannelFiles()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var selected = new List<ChannelFile>();
            foreach (var group in _channelFileCandidates.Values.Where(file => ShouldReadChannel(file.Channel) && file.Length > 0 &&
                         (!_unavailableChannelFiles.TryGetValue(file.Path, out var retryAt) || retryAt <= now))
                         .GroupBy(file => file.Channel, StringComparer.OrdinalIgnoreCase))
            {
                _activeChannelFiles.TryGetValue(group.Key, out var active);
                var file = group.OrderByDescending(file => file.ActivityUtc)
                    .ThenByDescending(file => string.Equals(file.Path, active, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(file => file.SessionUtc).ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).First();
                _activeChannelFiles[group.Key] = file.Path;
                selected.Add(file);
            }
            return selected;
        }
    }

    private bool NeedsChannelRead(ChannelFile file)
    {
        lock (_gate)
        {
            if (!_processedChannelFiles.TryGetValue(file.Path, out var previous)) return true;
            if (previous.CreatedUtc != file.CreatedUtc || file.Length < previous.Length ||
                (file.Length == previous.Length && file.WrittenUtc != previous.WrittenUtc))
            {
                _readOffsetsByPath.Remove(file.Path); // Rotation/rewrite, including replacement at the same path.
                return true;
            }
            return !_readOffsetsByPath.TryGetValue(file.Path, out var offset) || offset < file.Length;
        }
    }

    // Never consume an unterminated line: EVE can write a single report in multiple chunks.
    // Offsets are byte offsets and remain correct for both UTF-8 and EVE's UTF-16 logs.
    private static Task<(IReadOnlyList<string> Lines, long Offset)> ReadCompleteChannelLinesAsync(
        FileStream stream, long offset, CancellationToken cancellationToken) =>
        Hisa.Logs.ChatLogs.ChatLogFiles.ReadCompleteChannelLinesAsync(stream, offset, cancellationToken);
}
