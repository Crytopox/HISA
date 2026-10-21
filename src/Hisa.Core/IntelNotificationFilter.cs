using System.Text.RegularExpressions;
using Hisa.Core.Models;

namespace Hisa.Core;

/// <summary>Literal, whole-word exclusions applied only at notification boundaries.</summary>
public sealed class IntelNotificationFilter
{
    private readonly Regex[] _patterns;

    public IntelNotificationFilter(string? text)
    {
        Entries = (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _patterns = Entries.Select(entry => new Regex(
            @"(?<![\p{L}\p{N}_])" +
            string.Join(@"\s+", Regex.Split(entry, @"\s+").Select(Regex.Escape)) +
            @"(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
    }

    public IReadOnlyList<string> Entries { get; }

    public bool Suppresses(IntelChatReport report) =>
        !report.SourceFilePath.StartsWith("api://zkillboard", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(report.ChannelName, "zKillboard", StringComparison.OrdinalIgnoreCase) &&
        _patterns.Any(pattern => pattern.IsMatch(report.MessageText));
}
