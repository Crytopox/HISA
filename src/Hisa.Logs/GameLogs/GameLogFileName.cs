using System.Globalization;
using System.Text.RegularExpressions;

namespace Hisa.Logs.GameLogs;

internal static partial class GameLogFileName
{
    private static readonly Regex FileNameRegex = BuildRegex();

    public static bool TryParse(string fileName, out GameLogFileKey key)
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

        key = new GameLogFileKey
        {
            CharacterId = characterId,
            SessionStartedUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
        };
        return true;
    }

    [GeneratedRegex(@"^(?<date>\d{8})_(?<time>\d{6})_(?<characterId>\d+)\.txt$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BuildRegex();
}

internal readonly record struct GameLogFileKey
{
    public required int CharacterId { get; init; }
    public required DateTime SessionStartedUtc { get; init; }
}
