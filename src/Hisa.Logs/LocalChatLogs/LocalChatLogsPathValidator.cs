using System.Text.RegularExpressions;

namespace Hisa.Logs.LocalChatLogs;

public static class LocalChatLogsPathValidator
{
    private const string ChatLogsDirectoryName = "ChatLogs";
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

        var chatLogsPath = ResolveChatLogsPath(logsRootOrChatLogsPath);
        var logsRootPath = ResolveLogsRootPath(logsRootOrChatLogsPath);

        if (chatLogsPath is null || logsRootPath is null || !Directory.Exists(chatLogsPath))
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

    public static string? ResolveChatLogsPath(string? logsRootOrChatLogsPath)
    {
        if (string.IsNullOrWhiteSpace(logsRootOrChatLogsPath))
        {
            return null;
        }

        var normalizedInput = Path.GetFullPath(logsRootOrChatLogsPath.Trim());
        return TryResolveChatLogsDirectory(normalizedInput, out var chatLogsPath)
            ? chatLogsPath
            : BuildFallbackChatLogsPath(normalizedInput);
    }

    public static string? ResolveLogsRootPath(string? logsRootOrChatLogsPath)
    {
        if (string.IsNullOrWhiteSpace(logsRootOrChatLogsPath))
        {
            return null;
        }

        var normalizedInput = Path.GetFullPath(logsRootOrChatLogsPath.Trim());
        if (TryResolveChatLogsDirectory(normalizedInput, out var chatLogsPath))
        {
            return Directory.GetParent(chatLogsPath)?.FullName ?? chatLogsPath;
        }

        return IsChatLogsDirectoryPath(normalizedInput)
            ? (Directory.GetParent(normalizedInput)?.FullName ?? normalizedInput)
            : normalizedInput;
    }

    private static bool TryResolveChatLogsDirectory(string normalizedInput, out string chatLogsPath)
    {
        if (Directory.Exists(normalizedInput) && IsChatLogsDirectoryPath(normalizedInput))
        {
            chatLogsPath = normalizedInput;
            return true;
        }

        var logsRootPath = IsChatLogsDirectoryPath(normalizedInput)
            ? (Directory.GetParent(normalizedInput)?.FullName ?? normalizedInput)
            : normalizedInput;

        if (Directory.Exists(logsRootPath))
        {
            var matchedDirectory = Directory
                .EnumerateDirectories(logsRootPath, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => IsChatLogsDirectoryPath(path));

            if (matchedDirectory is not null)
            {
                chatLogsPath = Path.GetFullPath(matchedDirectory);
                return true;
            }
        }

        chatLogsPath = BuildFallbackChatLogsPath(normalizedInput);
        return false;
    }

    private static bool IsChatLogsDirectoryPath(string path)
    {
        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.Equals(directoryName, ChatLogsDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFallbackChatLogsPath(string normalizedInput)
    {
        return IsChatLogsDirectoryPath(normalizedInput)
            ? normalizedInput
            : Path.Combine(normalizedInput, ChatLogsDirectoryName);
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
