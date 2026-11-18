using System.Text.Json;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Xmpp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class XmppHistoryTests
{
    [Fact]
    public async Task ClearingFeedPersistsPerAccountAndPreservesUnreadChatHistory()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        const string account = "pilot@example.org";
        const string otherAccount = "other@example.org";
        var token = CancellationToken.None;
        await history.InitializeAsync(token);
        var directorbot = CreateMessage("Original broadcast", "original");
        var room = directorbot with
        {
            Conversation = new(XmppConversationKind.Room, "fleet@conference.example.org"),
            Body = "Room alert",
            ShowInFleetFeed = true,
            IdentityKeys = ["room"]
        };
        Assert.True(await history.InsertAsync(account, directorbot, token));
        Assert.True(await history.InsertAsync(account, room, token));
        Assert.True(await history.InsertAsync(otherAccount, directorbot, token));
        Assert.Equal(2, await history.FeedUnreadCountAsync(account, token));

        await history.ClearFeedAsync(account, token);
        var recovered = directorbot with
        {
            Timestamp = directorbot.Timestamp.AddSeconds(-1), Recovered = true, IdentityKeys = ["recovered"],
            Body = "Archived broadcast"
        };
        Assert.True(await history.InsertAsync(account, recovered, token));
        Assert.Empty(await history.ReadAsync(account, null, true, null, token));
        Assert.Equal(0, await history.FeedUnreadCountAsync(account, token));

        using var reopened = new XmppHistoryStore(database.ConnectionString);
        var originalRows = await reopened.ReadAsync(account, directorbot.Conversation, false, null, token);
        Assert.Equal(2, originalRows.Count);
        Assert.All(originalRows, row => Assert.True(row.Unread));
        Assert.True(Assert.Single(await reopened.ReadAsync(account, room.Conversation, false, null, token)).Unread);
        Assert.Single(await reopened.ReadAsync(otherAccount, null, true, null, token));

        var newAlert = directorbot with
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(1), Body = "New broadcast", IdentityKeys = ["new"]
        };
        Assert.True(await reopened.InsertAsync(account, newAlert, token));
        var visible = Assert.Single(await reopened.ReadAsync(account, null, true, null, token));
        Assert.Equal(newAlert.Body, visible.Body);
        Assert.Equal(1, await reopened.FeedUnreadCountAsync(account, token));
        Assert.False(await reopened.HasEarlierAsync(account, directorbot.Conversation, true, visible));
        Assert.True(await reopened.HasEarlierAsync(account, directorbot.Conversation, false, visible));
    }

    private const string Broadcast = "Fleet up\n\n" +
        "~~~ This was a broadcast from dispatcher to all at 2026-11-16 18:15:29.909911 EVE ~~~";
    private const string CoordBroadcast = """
        Hostiles in staging ----- form up and prepare to undock soon

        FC Name: Ada Nova
        Formup Location: Z-TEST1
        PAP Type: Strategic
        Comms: Op 10 https://comms.example.test/op10.html
        Doctrine: Sample Doctrine (SCN) (Boosters > Sample Cruiser > Logistics > Support)
        Get your ships ready
        ~~~ This was a coord broadcast from sample_coord to all at 2026-11-16 19:11:55.632889 EVE ~~~
        """;

    private static string AddCoordinatorPadding(string body) => body
        .Replace(": Ada", ":\u200D\uFEFF\u200D Ada")
        .Replace(": Sample", ":\uFEFF\u200D Sample")
        .Replace("\n~~~", "\n\u200D\uFEFF\u200D\n~~~");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimestampedBroadcastCopiesWithDifferentIdsPublishOnce(bool coordinator)
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        using var client = new XmppClientService(new SqliteSettingsService(database.ConnectionString),
            history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = stop.Token;
        const string account = "pilot@goonfleet.com";
        await client.StartAsync(token);
        try
        {
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            var received = new List<XmppMessage>();
            var alerts = new List<XmppAlert>();
            client.MessageReceived += received.Add;
            client.AlertReceived += alerts.Add;

            var body = (coordinator ? CoordBroadcast : Broadcast).Replace("\r\n", "\n");
            var copy = coordinator ? AddCoordinatorPadding(body) : body;
            var live = CreateGoonMessage(body, "live");
            await client.RecordAsync(account, live, null, token);
            await client.RecordAsync(account, live with { Body = copy, IdentityKeys = ["second-live"] }, null, token);
            await client.RecordAsync(account, live with
            {
                Sender = live.Sender + "/archive",
                Body = copy.Replace("\n", "\r\n"),
                Recovered = true,
                IdentityKeys = ["archive"]
            }, null, token);
            Assert.Equal(coordinator, Assert.Single(received).IsFleetPing);
            Assert.Single(alerts);
            Assert.Single(await client.ReadAsync(live.Conversation, false));
            Assert.Single(await client.ReadAsync(null, true));

            var changedTime = coordinator ? body.Replace("19:11:55.632889", "19:11:56") :
                body.Replace("18:15:29.909911", "18:15:30");
            var later = live with { Body = changedTime, IdentityKeys = ["later"] };
            await client.RecordAsync(account, later, null, token);
            await client.RecordAsync(account, later with { IdentityKeys = ["later-copy"] }, null, token);
            Assert.Equal(2, received.Count);
            Assert.Equal(2, (await client.ReadAsync(null, true)).Count);
            if (coordinator)
            {
                await client.RecordAsync(account, live with
                {
                    Body = body.Replace("Get your ships ready", "Stand down"),
                    IdentityKeys = ["changed-note"]
                }, null, token);
                Assert.Equal(3, received.Count);
                Assert.Equal(3, (await client.ReadAsync(null, true)).Count);
            }
        }
        finally { await client.StopAsync(token); }
    }

    [Fact]
    public async Task InitializationMergesSavedBroadcastCopiesAndPreservesTheirAliases()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        const string account = "pilot@example.org";
        var token = CancellationToken.None;
        await history.InitializeAsync(token);
        var first = CreateMessage(AddCoordinatorPadding(CoordBroadcast), "old-live") with { IsFleetPing = false };
        var copy = first with
        {
            Body = CoordBroadcast, Unread = false, FeedTitle = "Broadcast", ShowInFleetFeed = true,
            IdentityKeys = ["old-archive"]
        };

        using (var db = new SqliteConnection(database.ConnectionString))
        {
            await db.OpenAsync(token);
            using var command = db.CreateCommand();
            command.CommandText = """
                DELETE FROM Settings WHERE Key = 'xmpp.broadcast-identity.v2';
                INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('xmpp.broadcast-identity.v1', 'true');
                INSERT INTO XmppMessage (Id, Account, Kind, Address, Stamp, Unread, Value)
                VALUES (1, $account, $kind, $address, $stamp, 1, $first),
                       (2, $account, $kind, $address, $stamp, 0, $copy);
                INSERT INTO XmppMessageKey (Account, Key, MessageId)
                VALUES ($account, 'old-live', 1), ($account, 'old-archive', 2);
                """;
            command.Parameters.AddWithValue("$account", account);
            command.Parameters.AddWithValue("$kind", (int)first.Conversation.Kind);
            command.Parameters.AddWithValue("$address", first.Conversation.Jid);
            command.Parameters.AddWithValue("$stamp", first.Timestamp.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$first", JsonSerializer.Serialize(first));
            command.Parameters.AddWithValue("$copy", JsonSerializer.Serialize(copy));
            await command.ExecuteNonQueryAsync(token);
        }

        await history.InitializeAsync(token);
        await history.InitializeAsync(token);
        var merged = Assert.Single(await history.ReadAsync(account, null, true, null, token));
        Assert.Equal(1, merged.LocalId);
        Assert.False(merged.Unread);
        Assert.True(merged.IsFleetPing);
        Assert.Equal(first.Body, merged.Body);
        Assert.Equal("Broadcast", merged.FeedTitle);
        Assert.False(await history.InsertAsync(account, copy with { Body = "Alias still resolves" }, token));
        Assert.False(await history.InsertAsync(account, first with { IdentityKeys = ["new-delivery"] }, token));
        Assert.Single(await history.ReadAsync(account, first.Conversation, false, null, token));
    }

    [Fact]
    public async Task RoomArchiveMergesSourcesAndKeepsOlderPagesOnlyForThisSession()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        const string account = "pilot@example.org";
        var token = CancellationToken.None;
        await history.InitializeAsync(token);
        var room = new XmppConversation(XmppConversationKind.Room, "fleet@conference.example.org");
        var recent = CreateMessage("form up", "unused") with
        {
            Conversation = room, Sender = $"{room.Jid}/Fleet Commander", IdentityKeys = [],
            Recovered = true, Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var old = recent with { Body = "older report", Timestamp = recent.Timestamp.AddDays(-40) };
        Assert.True(await history.InsertAsync(account, recent with { JoinHistoryOrdinal = 1 }, token, XmppHistoryDelivery.RoomJoin));
        Assert.True(await history.InsertAsync(account, recent with { JoinHistoryOrdinal = 2 }, token, XmppHistoryDelivery.RoomJoin));
        Assert.False(await history.InsertAsync(account, recent with { IdentityKeys = ["archive-1"] }, token, XmppHistoryDelivery.RoomArchive));
        Assert.False(await history.InsertAsync(account, recent with { IdentityKeys = ["archive-2"] }, token, XmppHistoryDelivery.RoomArchive));
        Assert.True(await history.InsertAsync(account, old, token, XmppHistoryDelivery.RoomArchive));
        var messages = await history.ReadAsync(account, room, false, null, token);
        Assert.Equal(new[] { "older report", "form up", "form up" }, messages.Select(message => message.Body));
        Assert.All(messages, message => Assert.False(message.Unread));
        Assert.True(messages[0].LocalId < 0);
        using var reopened = new XmppHistoryStore(database.ConnectionString);
        Assert.Equal(2, (await reopened.ReadAsync(account, room, false, null, token)).Count);
        Assert.False(await reopened.InsertAsync(account, recent with { JoinHistoryOrdinal = 1 }, token, XmppHistoryDelivery.RoomJoin));
        Assert.False(await reopened.InsertAsync(account, recent with { JoinHistoryOrdinal = 2 }, token, XmppHistoryDelivery.RoomJoin));

        await history.ClearAsync(account, token);
        Assert.Empty(await history.ReadAsync(account, room, false, null, token));
        Assert.False(await history.InsertAsync(account, old, token, XmppHistoryDelivery.RoomArchive));
        Assert.False(await history.InsertAsync(account, recent, token, XmppHistoryDelivery.RoomJoin));
    }

    [Fact]
    public async Task RoomBackfillUsesTheJoinReadPositionWhileLiveMessagesAreBeingRead()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        const string account = "pilot@example.org";
        var token = CancellationToken.None;
        await history.InitializeAsync(token);
        var room = new XmppConversation(XmppConversationKind.Room, "fleet@conference.example.org");
        var first = CreateMessage("first", "first") with
        {
            Conversation = room, Sender = $"{room.Jid}/Other Pilot", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        Assert.True(await history.InsertAsync(account, first, token));
        await history.MarkVisibleAsync(account, room, await history.ReadAsync(account, room, false, null, token), token);
        var position = await history.GetRoomReadPositionAsync(account, room.Jid, token);
        var live = first with { Body = "live", IdentityKeys = ["live"], Timestamp = first.Timestamp.AddMinutes(2) };
        Assert.True(await history.InsertAsync(account, live, token));
        await history.MarkVisibleAsync(account, room, [(await history.ReadAsync(account, room, false, null, token))[^1]], token);
        var missed = first with
        {
            Body = "missed", IdentityKeys = ["missed"], Timestamp = first.Timestamp.AddMinutes(1), Recovered = true
        };
        Assert.True(await history.InsertAsync(account, missed, token, XmppHistoryDelivery.RoomArchive, position));
        Assert.False(await history.InsertAsync(account, live with { Recovered = true, IdentityKeys = [] }, token,
            XmppHistoryDelivery.RoomArchive, position));
        var context = first with { Body = "context", IdentityKeys = ["context"], Recovered = true };
        Assert.True(await history.InsertAsync(account, context, token, XmppHistoryDelivery.RoomJoin, position));
        Assert.Equal("missed", Assert.Single(await history.ReadAsync(account, room, false, null, token), row => row.Unread).Body);
    }

    [Fact]
    public async Task HistoryMergesAliasesAndKeepsUnreadStateAfterReopening()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = stopping.Token;
        await history.InitializeAsync(token);
        var message = CreateMessage("fleet now", "origin");
        Assert.True(await history.InsertAsync("pilot@example.org", message, token));
        Assert.False(await history.InsertAsync("pilot@example.org",
            message with { Recovered = true, IdentityKeys = ["origin", "archive"] }, token));
        Assert.False(await history.InsertAsync("pilot@example.org",
            message with { Recovered = true, IdentityKeys = ["archive"] }, token));
        Assert.True(await history.InsertAsync("pilot@example.org", message with { IdentityKeys = ["separate"] }, token));
        Assert.True(await history.InsertAsync("another@example.org", message, token));

        using var reopened = new XmppHistoryStore(database.ConnectionString);
        Assert.Equal(2, (await reopened.CountsAsync("pilot@example.org", token))[message.Conversation]);
        Assert.Single(await reopened.ReadAsync("another@example.org", null, true, null, token));
        await reopened.MarkReadAsync("pilot@example.org", message.Conversation, token);
        Assert.All(await reopened.ReadAsync("pilot@example.org", message.Conversation, true, null, token),
            row => Assert.False(row.Unread));
    }

    [Fact]
    public async Task ClearKeepsCheckpointAndBlocksOlderArchiveResults()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var account = "pilot@example.org";
        var token = CancellationToken.None;
        await history.InitializeAsync(token);
        var message = CreateMessage("fleet now", "original") with { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) };
        Assert.True(await history.InsertAsync(account, message, token));
        await history.SaveCheckpointAsync(account, account, message.Timestamp, token);
        await history.ClearAsync(account, token);
        Assert.Empty(await history.ReadAsync(account, null, true, null, token));
        Assert.False(await history.InsertAsync(account, message with { Recovered = true }, token));
        Assert.True(await history.GetCheckpointAsync(account, "$cleared", message.Timestamp, token) > message.Timestamp);
        Assert.True(await history.InsertAsync(account, message with { Timestamp = DateTimeOffset.UtcNow }, token));
        Assert.False(await history.InsertAsync(account,
            message with { Timestamp = DateTimeOffset.UtcNow.AddDays(-31), IdentityKeys = ["expired"] }, token));
    }

    [Fact]
    public async Task DirectorbotAlertsOnceAndCatchUpStaysSilent()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var settings = new SqliteSettingsService(database.ConnectionString);
        using var client = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(stop.Token);
        try
        {
            const string account = "pilot@goonfleet.com";
            await client.ConfigureAsync(account, "connect.other.example", 0, XmppTlsMode.Automatic, null, false, false,
                connect: false);
            var rule = new XmppAlertRule(Guid.NewGuid(), new(XmppConversationKind.Direct, "directorbot@goonfleet.com"),
                XmppAlertMatch.ContainsText, "fleet", true, "another.wav");
            await client.SaveRuleAsync(rule);
            var alerts = new List<XmppAlert>();
            client.AlertReceived += alerts.Add;
            var live = CreateGoonMessage("Fleet up", "live") with { Sender = "Directorbot@GOONFLEET.COM/dispatcher", IsFleetPing = false };
            await client.RecordAsync(account, live, null, stop.Token);
            await client.RecordAsync(account, live, null, stop.Token);
            await client.RecordAsync(account, CreateGoonMessage("Fleet up", "archive") with { Recovered = true }, null, stop.Token);
            await client.RecordAsync(account, CreateGoonMessage("Fleet up", "evil") with
            {
                Conversation = new(XmppConversationKind.Direct, "directorbot@other.example"),
                Sender = "directorbot@other.example"
            }, null, stop.Token);
            Assert.Equal(2, alerts.Count);
            Assert.True(alerts[0].Popup);
            Assert.False(alerts[0].Message.IsFleetPing);
            Assert.Equal("Directorbot", alerts[0].Message.FeedTitle);
            Assert.True(alerts[0].Message.ShowInFleetFeed);
            Assert.Equal("pidgin_receive.wav", alerts[0].Sound);
            Assert.False(alerts[1].Popup);
            Assert.False(alerts[1].Message.IsFleetPing);
            Assert.Equal(2, (await client.CountsAsync())[live.Conversation]);
        }
        finally { await client.StopAsync(stop.Token); }
    }

    [Fact]
    public async Task FleetRuleMigratesOnceAndRemembersWhenDisabled()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var settings = new SqliteSettingsService(database.ConnectionString);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await history.InitializeAsync(stop.Token);
        var legacy = Guid.NewGuid();
        var room = Guid.NewGuid();
        await settings.SetAsync("Xmpp.Client", new
        {
            Account = new { Jid = "pilot@goonfleet.com", DirectorbotJid = "directorbot@goonfleet.com", Resource = "HISA-test" },
            Rooms = Array.Empty<object>(),
            Rules = new[]
            {
                new { Id = legacy, Source = XmppAddresses.Directorbot("pilot@goonfleet.com"), Match = XmppAlertMatch.AnyMessage,
                    Popup = false, Sound = "custom.wav", Volume = 0.4 },
                new { Id = room, Source = new XmppConversation(XmppConversationKind.Room, "fleet@conference.goonfleet.com"),
                    Match = XmppAlertMatch.AnyMessage, Popup = true, Sound = "room.wav", Volume = 1.0 }
            }
        }, stop.Token);

        using (var first = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore()))
        {
            await first.StartAsync(stop.Token);
            try
            {
                await first.ReadyAsync();
                var rule = Assert.Single(first.Rules, item => item.FleetPings);
                Assert.Equal(legacy, rule.Id);
                Assert.True(rule.Enabled);
                Assert.False(rule.Popup);
                Assert.Equal("custom.wav", rule.Sound);
                Assert.Equal(0.4, rule.Volume);
                await first.SaveRuleAsync(rule with { Enabled = false });
            }
            finally { await first.StopAsync(stop.Token); }
        }

        using var reopened = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        await reopened.StartAsync(stop.Token);
        try
        {
            await reopened.ReadyAsync();
            Assert.Contains(reopened.Rules, item => item.Id == room && !item.FleetPings);
            Assert.False(Assert.Single(reopened.Rules, item => item.FleetPings).Enabled);
            var alerts = new List<XmppAlert>();
            reopened.AlertReceived += alerts.Add;
            await reopened.RecordAsync("pilot@goonfleet.com", CreateGoonMessage("quiet", "muted"), null, stop.Token);
            Assert.Empty(alerts);
        }
        finally { await reopened.StopAsync(stop.Token); }
    }

    [Fact]
    public async Task SystemAlertDefaultsOnlyApplyForGoonfleetAccounts()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var settings = new SqliteSettingsService(database.ConnectionString);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using (var other = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore()))
        {
            await other.StartAsync(stop.Token);
            try
            {
                await other.ConfigureAsync("pilot@example.org", null, 0, XmppTlsMode.Automatic, null, false, false, false);
                Assert.DoesNotContain(other.Rules, rule => rule.FleetPings);
                Assert.DoesNotContain(other.Rules, rule =>
                    rule.Source == XmppAddresses.Delve911 &&
                    string.Equals(rule.Text, "!bping all", StringComparison.OrdinalIgnoreCase));
            }
            finally { await other.StopAsync(stop.Token); }
        }

        using var goon = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        await goon.StartAsync(stop.Token);
        try
        {
            await goon.ConfigureAsync("pilot@goonfleet.com", null, 0, XmppTlsMode.Automatic, null, false, false, false);
            Assert.Contains(goon.Rules, rule => rule.FleetPings);
            var directorbot = Assert.Single(goon.Rules, rule => rule.FleetPings);
            Assert.True(directorbot.ShowInFleetFeed);
            Assert.False(directorbot.Highlight);
            Assert.Equal("Directorbot", directorbot.FeedTitle);
            Assert.Equal("pidgin_receive.wav", directorbot.Sound);
            var bping = Assert.Single(goon.Rules, rule =>
                rule.Source == XmppAddresses.Delve911 &&
                string.Equals(rule.Text, "!bping all", StringComparison.OrdinalIgnoreCase));
            Assert.True(bping.ShowInFleetFeed);
            Assert.True(bping.Highlight);
            Assert.Equal("Delve911", bping.FeedTitle);
            Assert.Equal("pidgin_alert.wav", bping.Sound);
        }
        finally { await goon.StopAsync(stop.Token); }
    }

    [Fact]
    public async Task ResetPreferencesRestoresDefaultsAndGoonfleetSystemRules()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var settings = new SqliteSettingsService(database.ConnectionString);
        using var client = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(stop.Token);
        try
        {
            const string account = "pilot@goonfleet.com";
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, false);
            await client.SaveDisplayPreferencesAsync(new(
                Use12HourClock: true, ShowMessageDividers: false, AlternatingMessageColors: true,
                ShowOccupantSidebar: false, FontFamily: "Consolas", MessageFontSize: 16,
                AlwaysShowMessageHeaders: false, MessageBlockPadding: 8));
            await client.SaveAlertSoundPreferencesAsync(new(
                SoftPingDirectMessages: false, SoftPingDirectVolume: 0.9, SoftPingDirectSound: "twinkle.wav",
                SoftPingMentions: false, SoftPingMentionVolume: 0.8, SoftPingMentionSound: "twinkle.wav"));
            await client.SaveMessageFiltersAsync(
            [
                new(Guid.NewGuid(), "noisy", XmppMessageFilterAction.Hide)
            ]);
            await client.SaveRuleAsync(new(Guid.NewGuid(),
                new(XmppConversationKind.Direct, "ally@goonfleet.com"),
                XmppAlertMatch.ContainsText, "ping", true, "soft-alert.wav"));

            await client.ResetPreferencesAsync();

            Assert.Equal(new XmppDisplayPreferences(), client.DisplayPreferences);
            Assert.Equal(new XmppAlertSoundPreferences(), client.AlertSoundPreferences);
            Assert.Empty(client.MessageFilters);
            Assert.DoesNotContain(client.Rules, rule =>
                rule.Source.Jid == "ally@goonfleet.com" &&
                string.Equals(rule.Text, "ping", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(client.Rules, rule => rule.FleetPings);
            Assert.Contains(client.Rules, rule =>
                rule.Source == XmppAddresses.Delve911 &&
                string.Equals(rule.Text, "!bping all", StringComparison.OrdinalIgnoreCase));
        }
        finally { await client.StopAsync(stop.Token); }
    }

    [Fact]
    public async Task StructuredDirectorbotFleetPingSetsIsFleetPingAndFleetTitle()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var settings = new SqliteSettingsService(database.ConnectionString);
        using var client = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(stop.Token);
        try
        {
            const string account = "pilot@goonfleet.com";
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, false);
            const string body =
                """
                "Hurf"
                FC Name: Test FC
                Formup Location: Q-EX01
                PAP Type: Strategic
                ~~~ This was a skirmishbot broadcast from bot to examplefleet ~~~
                """;
            await client.RecordAsync(account, CreateGoonMessage(body, "fleet-card") with
            {
                Sender = "Directorbot@GOONFLEET.COM/dispatcher",
                IsFleetPing = false
            }, null, stop.Token);

            var message = Assert.Single(await client.ReadAsync(null, true));
            Assert.True(message.IsFleetPing);
            Assert.True(message.ShowInFleetFeed);
            Assert.False(message.Highlight);
            Assert.Equal("Fleet ping", message.FeedTitle);
        }
        finally { await client.StopAsync(stop.Token); }
    }

    [Fact]
    public async Task DelveBpingRuleHighlightsAndPostsToNotificationFeed()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var settings = new SqliteSettingsService(database.ConnectionString);
        using var client = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(stop.Token);
        try
        {
            const string account = "pilot@goonfleet.com";
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, false);
            foreach (var rule in client.Rules.Where(rule => rule.FleetPings).ToArray())
                await client.SaveRuleAsync(rule with { Enabled = false });

            await client.RecordAsync(account, new(XmppAddresses.Delve911, "delve911@conference.goonfleet.com/Scout",
                "please !bping all now", DateTimeOffset.UtcNow, false, false, false, true, ["bp1"]), null, stop.Token);

            var feed = await client.ReadAsync(null, true);
            var message = Assert.Single(feed);
            Assert.True(message.ShowInFleetFeed);
            Assert.True(message.Highlight);
            Assert.Equal("Delve911", message.FeedTitle);
            Assert.Contains("Scout", message.FeedSubtitle);
            Assert.Contains("!bping all", message.FeedSubtitle);
        }
        finally { await client.StopAsync(stop.Token); }
    }

    [Fact]
    public async Task PingsReadTheDerivedConversationEvenForOlderHistory()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var token = CancellationToken.None;
        const string account = "pilot@example.org";
        await history.InitializeAsync(token);
        await history.InsertAsync(account, CreateMessage("old DM", "old") with { IsFleetPing = false }, token);
        await history.InsertAsync(account, CreateMessage("former bot", "other") with
        {
            Conversation = new(XmppConversationKind.Direct, "directorbot@other.example")
        }, token);
        await history.InsertAsync(account, CreateMessage("outgoing DM", "out") with { Outgoing = true }, token);

        var ping = Assert.Single(await history.ReadAsync(account, null, true, null, token));
        Assert.Equal("old DM", ping.Body);
        Assert.False(ping.IsFleetPing);

        const string structured =
            """
            "Hurf"
            FC Name: Test FC
            Formup Location: Q-EX01
            PAP Type: Strategic
            ~~~ This was a skirmishbot broadcast from bot to examplefleet ~~~
            """;
        await history.InsertAsync(account, CreateMessage(structured, "structured") with { IsFleetPing = false }, token);
        var feed = await history.ReadAsync(account, null, true, null, token);
        var fleet = Assert.Single(feed, message => message.Body == structured);
        Assert.True(fleet.IsFleetPing);
        Assert.Equal("Fleet ping", fleet.FeedTitle);
    }

    [Fact]
    public async Task FleetFeedIncludesShowInFleetFeedMessagesFromOtherConversations()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var token = CancellationToken.None;
        const string account = "pilot@example.org";
        await history.InitializeAsync(token);
        var room = new XmppConversation(XmppConversationKind.Room, "ops@conference.example.org");
        await history.InsertAsync(account, CreateMessage("normal room", "room1") with
        {
            Conversation = room,
            Sender = "ops@conference.example.org/Scout",
            IsFleetPing = false,
            ShowInFleetFeed = false
        }, token);
        await history.InsertAsync(account, CreateMessage("alert room", "room2") with
        {
            Conversation = room,
            Sender = "ops@conference.example.org/Scout",
            IsFleetPing = false,
            ShowInFleetFeed = true
        }, token);
        await history.InsertAsync(account, CreateMessage("fleet ping", "ping1"), token);

        var feed = await history.ReadAsync(account, null, true, null, token);
        Assert.Equal(2, feed.Count);
        Assert.Contains(feed, message => message.Body == "alert room");
        Assert.Contains(feed, message => message.Body == "fleet ping");
    }

    [Fact]
    public async Task AlertRuleCooldownSuppressesRepeatPopupAndSound()
    {
        using var database = new TestDatabase();
        using var history = new XmppHistoryStore(database.ConnectionString);
        var settings = new SqliteSettingsService(database.ConnectionString);
        using var client = new XmppClientService(settings, history, NullLogger<XmppClientService>.Instance, new NoCredentialStore());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(stop.Token);
        try
        {
            const string account = "pilot@example.org";
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            foreach (var existing in client.Rules.ToArray())
                await client.SaveRuleAsync(existing with { Enabled = false });
            await client.SaveAlertSoundPreferencesAsync(new(SoftPingDirectMessages: false, SoftPingMentions: false));
            var friend = new XmppConversation(XmppConversationKind.Direct, "friend@example.org");
            await client.SaveRuleAsync(new(Guid.NewGuid(), friend, XmppAlertMatch.ContainsText, "ping",
                true, "soft-alert.wav", CooldownSeconds: 60, ShowInFleetFeed: true));
            var alerts = new List<XmppAlert>();
            client.AlertReceived += alerts.Add;

            await client.RecordAsync(account, new(friend, "friend@example.org", "ping one",
                DateTimeOffset.UtcNow, false, false, false, true, ["c1"]), null, stop.Token);
            await client.RecordAsync(account, new(friend, "friend@example.org", "ping two",
                DateTimeOffset.UtcNow, false, false, false, true, ["c2"]), null, stop.Token);

            var alert = Assert.Single(alerts);
            Assert.True(alert.Popup);
            Assert.Equal("soft-alert.wav", alert.Sound);
            var feed = await client.ReadAsync(null, true);
            Assert.Equal(2, feed.Count);
            Assert.All(feed, message => Assert.True(message.ShowInFleetFeed));
        }
        finally { await client.StopAsync(stop.Token); }
    }

    [Theory]
    [InlineData("Fleet", "Conference.Example.org", "fleet@conference.example.org")]
    [InlineData("fleet", "rooms.alliance.example", "fleet@rooms.alliance.example")]
    [InlineData("", "conference.example.org", null)]
    [InlineData("fleet@example.org", "conference.example.org", null)]
    [InlineData("fleet", "conference.example.org/rooms", null)]
    public void RoomFieldsBuildOnlyBareRoomAddresses(string room, string server, string? expected)
    {
        Assert.Equal("conference.example.org", XmppAddresses.ConferenceServer("pilot@example.org"));
        Assert.Equal(expected, XmppAddresses.RoomAddress(room, server));
    }

    [Theory]
    [InlineData("warp to Grey Pilot now", true)]
    [InlineData("@grey pilot", true)]
    [InlineData("DarkGrey Pilot", false)]
    [InlineData("Grey Piloting", false)]
    public void RoomNicknameMatchRespectsWordBoundaries(string body, bool expected) =>
        Assert.Equal(expected, XmppAddresses.Mentions(body, "Grey Pilot"));

    private static XmppMessage CreateMessage(string body, string key) =>
        new(new(XmppConversationKind.Direct, "directorbot@example.org"), "directorbot@example.org",
            body, DateTimeOffset.UtcNow, false, false, true, true, [key]);

    private static XmppMessage CreateGoonMessage(string body, string key) =>
        new(new(XmppConversationKind.Direct, "directorbot@goonfleet.com"), "directorbot@goonfleet.com",
            body, DateTimeOffset.UtcNow, false, false, true, true, [key]);

    private sealed class NoCredentialStore : IXmppCredentialStore
    {
        public string? Read(string key) => null;
        public bool Write(string key, string secret) => false;
        public bool Delete(string key) => true;
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-xmpp.db");
        public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString();
        public void Dispose() => File.Delete(_path);
    }
}

