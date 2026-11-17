using System.Collections.Concurrent;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Xmpp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class XmppCredentialTests : IDisposable
{
    private const string Pilot = "pilot@example.org";
    private const string Other = "other@example.org";
    private const string Room = "fleet@conference.example.org";
    private const string PilotKey = "account/" + Pilot;
    private const string RoomKey = "room/" + Pilot + "/" + Room;
    private const string SettingsKey = "Xmpp.Client";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hisa-vault-{Guid.NewGuid():N}.db");
    private readonly FakeVault _vault = new();
    private readonly FaultableSettings _settings;

    public XmppCredentialTests()
    {
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString();
        _settings = new(new SqliteSettingsService(ConnectionString));
    }

    private string ConnectionString { get; }

    [Fact]
    public async Task SettingsWithoutCleanupKeysKeepSavedAccount()
    {
        using var history = new XmppHistoryStore(ConnectionString);
        await history.InitializeAsync(CancellationToken.None);
        await _settings.SetAsync(SettingsKey, new
        {
            Account = new XmppAccount(Pilot, null, 0, XmppTlsMode.Automatic, true, false, "HISA-test"),
            Rooms = Array.Empty<XmppRoom>(),
            Rules = Array.Empty<XmppAlertRule>()
        });
        using var client = await StartAsync(history);
        Assert.True(client.Account?.RememberPassword);
        Assert.Empty((await ReadSettingsAsync()).PendingCredentialDeletes);
        await client.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AccountSwitchQueuesAccountAndRoomPasswordsAndRetriesAfterRestart()
    {
        using var history = new XmppHistoryStore(ConnectionString);
        using (var client = await StartAsync(history))
        {
            await ConfigureAsync(client, Pilot, "old-password", remember: true);
            await client.SaveRoomAsync(Room, "pilot", false, "room-password", true, false);
            _vault.Locked = true;

            Assert.Contains("pending", await ConfigureAsync(client, Other));
            Assert.Equal(Other, client.Account?.Jid);
            Assert.Empty(client.Rooms);
            Assert.Equal(new[] { PilotKey, RoomKey }, (await ReadSettingsAsync()).PendingCredentialDeletes.Order());
            Assert.Equal(2, _vault.Values.Count);
            await client.StopAsync(CancellationToken.None);
        }

        _vault.Locked = false;
        using var restarted = await StartAsync(history);
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        restarted.StateChanged += () =>
        {
            if (_vault.Values.IsEmpty) removed.TrySetResult();
        };
        restarted.DesktopReady();
        await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty((await ReadSettingsAsync()).PendingCredentialDeletes);
        await restarted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SwitchingBackKeepsReplacementSessionOnlyUntilDeletionSucceeds()
    {
        using var history = new XmppHistoryStore(ConnectionString);
        using var client = await StartAsync(history);
        await ConfigureAsync(client, Pilot, "older", remember: true);
        _vault.Locked = true;
        await ConfigureAsync(client, Other);

        var writes = _vault.Writes;
        Assert.Contains("pending", await ConfigureAsync(client, Pilot, "newer", remember: true));
        Assert.False(client.Account?.RememberPassword);
        Assert.Equal(writes, _vault.Writes);
        Assert.Equal("older", _vault.Values[PilotKey]);

        _vault.Locked = false;
        Assert.Null(await ConfigureAsync(client, Pilot, remember: true));
        Assert.Equal("newer", _vault.Values[PilotKey]);
        Assert.True(client.Account?.RememberPassword);
        Assert.Empty((await ReadSettingsAsync()).PendingCredentialDeletes);
        await client.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailedReplacementAndRoomRemovalStopUsingPreviousSavedPasswords()
    {
        using var history = new XmppHistoryStore(ConnectionString);
        using var client = await StartAsync(history);
        await ConfigureAsync(client, Pilot, "older", remember: true);
        await client.SaveRoomAsync(Room, "pilot", false, "room-password", true, false);
        var rememberedRoom = Assert.Single(client.Rooms);
        _vault.Locked = true;

        Assert.Contains("pending", await ConfigureAsync(client, Pilot, "newer", remember: true));
        Assert.False(client.Account?.RememberPassword);
        Assert.Contains("pending", await client.SaveRoomAsync(Room, "pilot", false, null, false, false));
        Assert.False(Assert.Single(client.Rooms).HasPassword);
        Assert.Null(client.RoomPassword(Pilot, rememberedRoom));
        Assert.Contains("pending", await client.RemoveRoomAsync(Room));
        Assert.Empty(client.Rooms);
        Assert.Equal(new[] { PilotKey, RoomKey }, (await ReadSettingsAsync()).PendingCredentialDeletes.Order());

        _vault.Locked = false;
        Assert.Null(await ConfigureAsync(client, Pilot));
        Assert.Empty(_vault.Values);
        Assert.Empty((await ReadSettingsAsync()).PendingCredentialDeletes);
        await client.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task VaultWriteWithoutSettingsConfirmationIsCleanedUpOnRetry()
    {
        using var history = new XmppHistoryStore(ConnectionString);
        using var client = await StartAsync(history);
        await ConfigureAsync(client, Pilot, "previous-account-password", remember: true);
        _settings.FailRememberedCommit = true;
        await Assert.ThrowsAsync<IOException>(() => ConfigureAsync(client, Other, "interrupted", remember: true));
        Assert.False(client.Account?.RememberPassword);
        Assert.Equal(new[] { "account/" + Other, PilotKey }, (await ReadSettingsAsync()).PendingCredentialDeletes.Order());
        Assert.Equal("interrupted", _vault.Values["account/" + Other]);

        _settings.FailRememberedCommit = false;
        Assert.Contains("could not save", await ConfigureAsync(client, Other, remember: true));
        Assert.False(client.Account?.RememberPassword);
        Assert.Empty(_vault.Values);
        Assert.Empty((await ReadSettingsAsync()).PendingCredentialDeletes);
        await client.StopAsync(CancellationToken.None);
    }

    private async Task<XmppClientService> StartAsync(XmppHistoryStore history)
    {
        var client = new XmppClientService(_settings, history, NullLogger<XmppClientService>.Instance, _vault);
        await client.StartAsync(CancellationToken.None);
        await client.ReadyAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return client;
    }

    private static Task<string?> ConfigureAsync(XmppClientService client, string account, string? password = null,
        bool remember = false) => client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, password,
        remember, false, connect: false);

    private async Task<XmppClientService.SavedSettings> ReadSettingsAsync() =>
        (await _settings.GetAsync<XmppClientService.SavedSettings>(SettingsKey))!;

    public void Dispose() => File.Delete(_path);

    private sealed class FakeVault : IXmppCredentialStore
    {
        public ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public bool Locked { get; set; }
        public int Writes { get; private set; }

        public string? Read(string key) => Locked ? null : Values.GetValueOrDefault(key);

        public bool Write(string key, string secret)
        {
            Writes++;
            if (Locked) return false;
            Values[key] = secret;
            return true;
        }

        public bool Delete(string key)
        {
            if (Locked) return false;
            Values.TryRemove(key, out _);
            return true;
        }
    }

    private sealed class FaultableSettings(ISettingsService inner) : ISettingsService
    {
        public bool FailRememberedCommit { get; set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken token = default) => inner.GetAsync<T>(key, token);

        public Task SetAsync<T>(string key, T value, CancellationToken token = default)
        {
            if (FailRememberedCommit && value is XmppClientService.SavedSettings { Account.RememberPassword: true })
                throw new IOException("Fixture settings write failure");
            return inner.SetAsync(key, value, token);
        }
    }
}
