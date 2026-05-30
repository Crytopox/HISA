using System.Globalization;
using System.Text.RegularExpressions;

namespace Hisa.Logs.LocalChatLogs;

internal static partial class LocalChatLogFileName
{
    private static readonly Regex FileNameRegex = BuildRegex();

    public static bool TryParse(string fileName, out LocalChatLogFileKey key)
    {
        key = default;
        var match = FileNameRegex.Match(fileName);
        if (!match.Success)
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

        if (!int.TryParse(match.Groups["characterId"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var characterId))
        {
            return false;
        }

        key = new LocalChatLogFileKey
        {
            CharacterId = characterId,
            SessionStartedUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
        };
        return true;
    }

    [GeneratedRegex(@"^Local_(?<date>\d{8})_(?<time>\d{6})_(?<characterId>\d+)\.txt$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BuildRegex();
}

internal readonly record struct LocalChatLogFileKey
{
    public required int CharacterId { get; init; }
    public required DateTime SessionStartedUtc { get; init; }
}
