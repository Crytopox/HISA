using System.Text;

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
    private static async Task<(IReadOnlyList<string> Lines, long Offset)> ReadCompleteChannelLinesAsync(
        FileStream stream, long offset, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var header = new byte[3];
        var headerLength = await stream.ReadAsync(header, cancellationToken);
        var utf16 = headerLength >= 2 && ((header[0] == 0xFF && header[1] == 0xFE) || (header[0] == 0xFE && header[1] == 0xFF));
        var bigEndian = utf16 && header[0] == 0xFE;
        var encoding = utf16 ? (bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode) : Encoding.UTF8;
        var bomLength = utf16 ? 2 : headerLength >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF ? 3 : 0;
        offset = Math.Max(offset, bomLength);
        if (utf16 && offset % 2 != 0) offset++;
        stream.Position = Math.Min(offset, stream.Length);
        var bytes = new byte[(int)Math.Min(InitialReadTailBytes, Math.Max(0, stream.Length - stream.Position))];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        var complete = 0;
        if (utf16)
        {
            for (var i = count / 2 * 2 - 2; i >= 0; i -= 2)
                if (bytes[i] == (bigEndian ? 0 : 10) && bytes[i + 1] == (bigEndian ? 10 : 0)) { complete = i + 2; break; }
        }
        else
        {
            for (var i = count - 1; i >= 0; i--)
                if (bytes[i] == 10) { complete = i + 1; break; }
        }
        if (complete == 0) return ([], offset);
        var text = encoding.GetString(bytes, 0, complete);
        return (text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray(), offset + complete);
    }
}
