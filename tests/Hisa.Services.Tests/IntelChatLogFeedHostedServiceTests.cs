using System.Reflection;
using Hisa.Core.Abstractions;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public class IntelChatLogFeedHostedServiceTests
{
    [Theory]
    [InlineData("Kill: Rave Maulerant (Thorax)", true)]
    [InlineData("kill: Some Pilot (Drake Navy Issue)", true)]
    [InlineData("Kill: Rave Maulerant Thorax", false)]
    [InlineData("3 in local", false)]
    public void IsIgnoredIntelMessage_MatchesInGameKillmailFormat(string message, bool expectedIgnored)
    {
        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "IsIgnoredIntelMessage",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = (bool)method!.Invoke(null, [message])!;
        Assert.Equal(expectedIgnored, result);
    }

    [Fact]
    public void ShouldReadChannel_WhenIncludeListIsEmpty_ReturnsTrue()
    {
        var service = new IntelChatLogFeedHostedService(
            new NoopSettingsService(),
            new NoopSdeDatabase(),
            new NoopHttpClientFactory(),
            NullLogger<IntelChatLogFeedHostedService>.Instance);

        var method = typeof(IntelChatLogFeedHostedService).GetMethod(
            "ShouldReadChannel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = (bool)method!.Invoke(service, ["Any Intel Channel"])!;
        Assert.True(result);
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

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
