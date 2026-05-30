using System.Text.RegularExpressions;

namespace Hisa.Logs.LocalChatLogs;

public static class LocalChatLogsPathValidator
{
    private static readonly Regex LocalFilePattern = new(@"^Local_\d{8}_\d{6}_\d+\.txt$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static LocalChatLogsPathValidationResult Validate(string? logsRootOrChatLogsPath)
    {
        if (string.IsNullOrWhiteSpace(logsRootOrChatLogsPath))
        {
            return new LocalChatLogsPathValidationResult
            {
                IsValid = false,
                Message = "Path is empty.",
                NormalizedLogsRootPath = null,
                ChatLogsPath = null,
                MatchingLocalLogFileCount = 0
            };
        }

        var normalizedInput = Path.GetFullPath(logsRootOrChatLogsPath.Trim());
        var chatLogsPath = normalizedInput.EndsWith("ChatLogs", StringComparison.OrdinalIgnoreCase)
            ? normalizedInput
            : Path.Combine(normalizedInput, "ChatLogs");

        var logsRootPath = normalizedInput.EndsWith("ChatLogs", StringComparison.OrdinalIgnoreCase)
            ? (Directory.GetParent(normalizedInput)?.FullName ?? normalizedInput)
            : normalizedInput;

        if (!Directory.Exists(chatLogsPath))
        {
            return new LocalChatLogsPathValidationResult
            {
                IsValid = false,
                Message = "ChatLogs folder was not found.",
                NormalizedLogsRootPath = logsRootPath,
                ChatLogsPath = chatLogsPath,
                MatchingLocalLogFileCount = 0
            };
        }

        var count = Directory
            .EnumerateFiles(chatLogsPath, "Local_*.txt", SearchOption.TopDirectoryOnly)
            .Count(path => LocalFilePattern.IsMatch(Path.GetFileName(path)));

        if (count <= 0)
        {
            return new LocalChatLogsPathValidationResult
            {
                IsValid = false,
                Message = "No Local_*.txt chat logs were found in ChatLogs.",
                NormalizedLogsRootPath = logsRootPath,
                ChatLogsPath = chatLogsPath,
                MatchingLocalLogFileCount = 0
            };
        }

        return new LocalChatLogsPathValidationResult
        {
            IsValid = true,
            Message = $"Valid. Found {count} Local log file(s).",
            NormalizedLogsRootPath = logsRootPath,
            ChatLogsPath = chatLogsPath,
            MatchingLocalLogFileCount = count
        };
    }
}

public sealed class LocalChatLogsPathValidationResult
{
    public required bool IsValid { get; init; }
    public required string Message { get; init; }
    public required string? NormalizedLogsRootPath { get; init; }
    public required string? ChatLogsPath { get; init; }
    public required int MatchingLocalLogFileCount { get; init; }
}
