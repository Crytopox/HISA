using Hisa.Logs.LocalChatLogs;

namespace Hisa.Logs.Tests;

public sealed class LocalChatLogsPathValidatorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "hisa-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Validate_WhenLogsRootContainsChatlogsDirectory_UsesActualDirectoryPath()
    {
        var logsRoot = CreateLogsRootWithLocalChatlogs();

        var result = LocalChatLogsPathValidator.Validate(logsRoot);

        Assert.True(result.IsValid);
        Assert.Equal(logsRoot, result.NormalizedLogsRootPath);
        Assert.Equal(Path.Combine(logsRoot, "Chatlogs"), result.ChatLogsPath);
    }

    [Fact]
    public void ResolveChatLogsPath_WhenInputIsChatlogsDirectory_ReturnsActualDirectoryPath()
    {
        var logsRoot = CreateLogsRootWithLocalChatlogs();
        var chatlogsPath = Path.Combine(logsRoot, "Chatlogs");

        var resolved = LocalChatLogsPathValidator.ResolveChatLogsPath(chatlogsPath);
        var resolvedRoot = LocalChatLogsPathValidator.ResolveLogsRootPath(chatlogsPath);

        Assert.Equal(chatlogsPath, resolved);
        Assert.Equal(logsRoot, resolvedRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string CreateLogsRootWithLocalChatlogs()
    {
        var logsRoot = Path.Combine(_tempRoot, "logs");
        var chatlogsPath = Path.Combine(logsRoot, "Chatlogs");
        Directory.CreateDirectory(chatlogsPath);
        File.WriteAllText(Path.Combine(chatlogsPath, "Local_20250610_120000_12345678.txt"), string.Empty);
        return logsRoot;
    }
}
