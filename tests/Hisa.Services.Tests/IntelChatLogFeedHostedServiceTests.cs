using System.Reflection;
using Hisa.Core.Abstractions;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public class IntelChatLogFeedHostedServiceTests
{
    [Fact]
    public void TryPromoteActiveFile_OnlyPromotesNewestFilePerChannel()
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            NullLogger<IntelChatLogFeedHostedService>.Instance);

        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "TryPromoteActiveFile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var tempDir = Path.Combine(Path.GetTempPath(), "hisa-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var olderPath = Path.Combine(tempDir, "Intel_20260101_120000_0000001.txt");
            var newerPath = Path.Combine(tempDir, "Intel_20260101_120500_0000002.txt");
            File.WriteAllText(olderPath, "old");
            File.WriteAllText(newerPath, "new");

            var olderWriteUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var newerWriteUtc = olderWriteUtc.AddMinutes(5);
            File.SetLastWriteTimeUtc(olderPath, olderWriteUtc);
            File.SetLastWriteTimeUtc(newerPath, newerWriteUtc);

            var firstArgs = new object?[] { "Intel", olderPath, null };
            var firstAccepted = (bool)method!.Invoke(service, firstArgs)!;
            Assert.True(firstAccepted);
            Assert.Equal(olderPath, Assert.IsType<string>(firstArgs[2]));

            var secondArgs = new object?[] { "Intel", newerPath, null };
            var secondAccepted = (bool)method.Invoke(service, secondArgs)!;
            Assert.True(secondAccepted);
            Assert.Equal(newerPath, Assert.IsType<string>(secondArgs[2]));

            var thirdArgs = new object?[] { "Intel", olderPath, null };
            var thirdAccepted = (bool)method.Invoke(service, thirdArgs)!;
            Assert.False(thirdAccepted);
            Assert.Equal(newerPath, Assert.IsType<string>(thirdArgs[2]));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class NoopSettingsService : ISettingsService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoopSdeDatabase : ISdeDatabase
    {
        public SqliteConnection CreateConnection()
        {
            throw new NotSupportedException();
        }
    }
}
