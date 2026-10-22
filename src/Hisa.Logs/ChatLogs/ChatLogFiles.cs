using System.Text;
using System.Text.RegularExpressions;

namespace Hisa.Logs.ChatLogs;

public static class ChatLogFiles
{
    private static readonly Regex FileName = new(@"^(?<channel>.+)_\d{8}_\d{6}_\d+\.txt$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string? ChannelFromPath(string path)
    {
        var match = FileName.Match(Path.GetFileName(path));
        return match.Success ? match.Groups["channel"].Value : null;
    }

    public static async Task<(IReadOnlyList<string> Lines, long Offset)> ReadCompleteChannelLinesAsync(
        FileStream stream, long offset, CancellationToken cancellationToken, long? endOffset = null)
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
        var bytes = new byte[(int)Math.Min(512 * 1024, Math.Max(0, Math.Min(stream.Length, endOffset ?? stream.Length) - stream.Position))];
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
