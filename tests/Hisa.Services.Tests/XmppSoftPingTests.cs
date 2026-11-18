using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Xmpp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class XmppSoftPingTests
{
    [Fact]
    public async Task DirectMessageSoftPingsWhenEnabled()
    {
        await using var harness = await Harness.CreateAsync();
        var alerts = harness.CaptureAlerts();
        await harness.Client.SaveAlertSoundPreferencesAsync(new(
            SoftPingDirectMessages: true, SoftPingDirectVolume: 0.2, SoftPingDirectSound: "twinkle.wav",
            SoftPingMentions: false, SoftPingMentionVolume: 0.25));

        await harness.Client.RecordAsync(Harness.Account, Dm("hello there", "dm1"), null, harness.Token);

        var alert = Assert.Single(alerts);
        Assert.False(alert.Popup);
        Assert.Equal("twinkle.wav", alert.Sound);
        Assert.Equal(0.2, alert.Volume);
    }

    [Fact]
    public async Task DirectMessageStaysSilentWhenSoftPingDisabled()
    {
        await using var harness = await Harness.CreateAsync();
        var alerts = harness.CaptureAlerts();
        await harness.Client.SaveAlertSoundPreferencesAsync(new(
            SoftPingDirectMessages: false, SoftPingMentions: false));

        await harness.Client.RecordAsync(Harness.Account, Dm("hello there", "dm2"), null, harness.Token);

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task MentionInDirectUsesMentionSoftPingNotDirect()
    {
        await using var harness = await Harness.CreateAsync();
        var alerts = harness.CaptureAlerts();
        await harness.Client.SaveAlertSoundPreferencesAsync(new(
            SoftPingDirectMessages: true, SoftPingDirectVolume: 0.2, SoftPingDirectSound: "twinkle.wav",
            SoftPingMentions: true, SoftPingMentionVolume: 0.33, SoftPingMentionSound: "soft-mention.wav"));

        await harness.Client.RecordAsync(Harness.Account, Dm("hey pilot are you free?", "dm3"), "pilot", harness.Token);

        var alert = Assert.Single(alerts);
        Assert.True(alert.Message.Mention);
        Assert.Equal("soft-mention.wav", alert.Sound);
        Assert.Equal(0.33, alert.Volume);
    }

    [Fact]
    public async Task RoomMentionSoftPingsWhenEnabled()
    {
        await using var harness = await Harness.CreateAsync();
        var alerts = harness.CaptureAlerts();
        await harness.Client.SaveAlertSoundPreferencesAsync(new(
            SoftPingMentions: true, SoftPingMentionVolume: 0.18, SoftPingMentionSound: "notification-power-up.wav"));

        var room = new XmppConversation(XmppConversationKind.Room, "fleet@conference.example.org");
        await harness.Client.RecordAsync(Harness.Account,
            new(room, "ally@example.org/nick", "grey pilot warp", DateTimeOffset.UtcNow, false, false, false, true, ["room1"]),
            "grey pilot", harness.Token);

        var alert = Assert.Single(alerts);
        Assert.True(alert.Message.Mention);
        Assert.Equal("notification-power-up.wav", alert.Sound);
        Assert.Equal(0.18, alert.Volume);
    }

    private static XmppMessage Dm(string body, string key) =>
        new(new(XmppConversationKind.Direct, "friend@example.org"), "friend@example.org",
            body, DateTimeOffset.UtcNow, false, false, false, true, [key]);

    [Fact]
    public async Task DirectorbotUsesOnlyTheDefaultRuleSoundAndStaysSilentWhenTheRuleIsDisabled()
    {
        const string account = "pilot@goonfleet.com";
        await using var harness = await Harness.CreateAsync(account, useDefaultRules: true);
        await harness.Client.SaveAlertSoundPreferencesAsync(new(SoftPingDirectMessages: true, SoftPingMentions: true));
        var alerts = harness.CaptureAlerts();
        var bot = XmppAddresses.Directorbot(account);
        var ping = Dm("pilot, fleet is ready", "broadcast") with { Conversation = bot, Sender = bot.Jid };

        await harness.Client.RecordAsync(account, ping, "pilot", harness.Token);
        var alert = Assert.Single(alerts);
        Assert.Equal("pidgin_receive.wav", alert.Sound);
        Assert.True(alert.Message.ShowInFleetFeed);

        var rule = Assert.Single(harness.Client.Rules, rule => rule.FleetPings);
        await harness.Client.SaveRuleAsync(rule with { Enabled = false });
        await harness.Client.RecordAsync(account, ping with { IdentityKeys = ["next-broadcast"] }, "pilot", harness.Token);
        Assert.Single(alerts);
    }

    [Fact]
    public async Task MatchingRulesUseTheFirstSoundAndCooldownDoesNotFallBackToSoftPings()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Client.SaveAlertSoundPreferencesAsync(new(SoftPingDirectMessages: true, SoftPingMentions: true));
        var alerts = harness.CaptureAlerts();
        var message = Dm("pilot, ready to go", "first");
        await harness.Client.SaveRuleAsync(new(Guid.NewGuid(), message.Conversation, XmppAlertMatch.ContainsText,
            "ready", Popup: false, Sound: "twinkle.wav", Volume: 0.2, CooldownSeconds: 60));
        await harness.Client.SaveRuleAsync(new(Guid.NewGuid(), message.Conversation, XmppAlertMatch.ContainsText,
            "ready", Popup: true, Sound: "soft-alert.wav", CooldownSeconds: 60));

        await harness.Client.RecordAsync(Harness.Account, message, "pilot", harness.Token);
        var alert = Assert.Single(alerts);
        Assert.True(alert.Popup);
        Assert.Equal("twinkle.wav", alert.Sound);
        Assert.Equal(0.2, alert.Volume);

        await harness.Client.RecordAsync(Harness.Account, message with { IdentityKeys = ["cooldown"] }, "pilot", harness.Token);
        Assert.Single(alerts);
        await harness.Client.RecordAsync(Harness.Account, Dm("hello pilot", "unrelated"), "pilot", harness.Token);
        Assert.Equal(2, alerts.Count);
        Assert.Equal("regular_notification.wav", alerts[1].Sound);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PopupOnlyAndFeedOnlyRulesSuppressMentionSounds(bool popup)
    {
        await using var harness = await Harness.CreateAsync();
        await harness.Client.SaveAlertSoundPreferencesAsync(new(SoftPingDirectMessages: true, SoftPingMentions: true));
        var alerts = harness.CaptureAlerts();
        var message = Dm("hello pilot", "handled");
        await harness.Client.SaveRuleAsync(new(Guid.NewGuid(), message.Conversation, XmppAlertMatch.AnyMessage,
            null, Popup: popup, Sound: null, ShowInFleetFeed: !popup));
        await harness.Client.RecordAsync(Harness.Account, message, "pilot", harness.Token);
        if (popup)
        {
            var alert = Assert.Single(alerts);
            Assert.True(alert.Popup);
            Assert.Null(alert.Sound);
        }
        else
        {
            Assert.Empty(alerts);
            Assert.Single(await harness.Client.ReadAsync(null, true));
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public const string Account = "pilot@example.org";
        private readonly TestDatabase _database;
        private readonly XmppHistoryStore _history;
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(10));

        public XmppClientService Client { get; }
        public CancellationToken Token => _stop.Token;

        private Harness(XmppClientService client, XmppHistoryStore history, TestDatabase database)
        {
            Client = client;
            _history = history;
            _database = database;
        }

        public static async Task<Harness> CreateAsync(string account = Account, bool useDefaultRules = false)
        {
            var database = new TestDatabase();
            var history = new XmppHistoryStore(database.ConnectionString);
            var settings = new SqliteSettingsService(database.ConnectionString);
            var client = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
            var harness = new Harness(client, history, database);
            await client.StartAsync(harness.Token);
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            if (!useDefaultRules)
            {
                foreach (var rule in client.Rules.ToArray())
                    await client.SaveRuleAsync(rule with { Enabled = false });
            }
            return harness;
        }

        public List<XmppAlert> CaptureAlerts()
        {
            var alerts = new List<XmppAlert>();
            Client.AlertReceived += alerts.Add;
            return alerts;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.StopAsync(Token);
            _history.Dispose();
            _database.Dispose();
            _stop.Dispose();
        }
    }

    private sealed class NoCredentialStore : IXmppCredentialStore
    {
        public string? Read(string key) => null;
        public bool Write(string key, string secret) => false;
        public bool Delete(string key) => true;
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-xmpp-soft.db");
        public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString();
        public void Dispose()
        {
            try { File.Delete(_path); } catch (IOException) { }
        }
    }
}
