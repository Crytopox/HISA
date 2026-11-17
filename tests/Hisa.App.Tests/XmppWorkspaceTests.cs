using System.Diagnostics;
using System.Net;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.App;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Esi;
using Hisa.Esi.Clients;
using Hisa.Services.Xmpp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Hisa.App.Tests;

public sealed class XmppWorkspaceTests
{
    [AvaloniaFact]
    public async Task FeedTemplatesShowCompleteBodiesAndShareTheFleetPortrait()
    {
        const string account = "pilot@example.org";
        var directorbot = XmppAddresses.Directorbot(account);
        var body = $"First paragraph\n\n{new string('x', 500)}\nRead https://example.org/details.\nLast line";
        var hurf = $"Quick Space Job\n\n{new string('x', 320)}\nRead https://example.org/hurf.";
        var regular = new XmppMessage(directorbot, directorbot.Jid, body, DateTimeOffset.UtcNow,
            false, false, false, true, []) { LocalId = 1, FeedSubtitle = "See https://example.org/subtitle." };
        var fleet = regular with
        {
            LocalId = 2,
            Body = hurf + "\n\nFC Name:\u200D\uFEFF\u200D Feed Test Pilot\nPAP Type: Strategic\nFormup Location: C-J6MT\n" +
                   "Comms: Op 3 https://example.org/comms\nDoctrine: Flycatchers\n" +
                   "Get your Ships Ready to Fight\n\nSee https://example.org/notes.\n\uFEFF\u200D\n" +
                   "~~~ This was a coord broadcast from bot to all at 2026-11-16 19:11:55.632889 EVE ~~~"
        };
        var mainCards = XmppFeedCards.BuildFromMessages([regular, fleet], account);
        var floatingCards = XmppFeedCards.BuildFromMessages([regular, fleet], account);
        var handlers = new PortraitResponses();
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, EmptySettings>();
        services.AddHisaEsi();
        services.AddHttpClient(EveCharacterResolver.ClientName).ConfigurePrimaryHttpMessageHandler(() => handlers);
        services.AddHttpClient(EveCharacterResolver.PortraitClientName).ConfigurePrimaryHttpMessageHandler(() => handlers);
        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IEveCharacterResolver>();
        var loader = new XmppFeedPortraitLoader(resolver, NullLogger<XmppFeedPortraitLoader>.Instance);
        loader.Update(mainCards);
        await handlers.PortraitRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oldCards = mainCards;
        mainCards = XmppFeedCards.BuildFromMessages([regular, fleet], account);
        loader.Update([.. mainCards, .. floatingCards]);

        var main = new XmppWindow();
        var floating = new XmppFeedOverlayWindow();
        var portraitImages = new List<Image>();
        foreach (var (window, cards) in new[] { ((Window)main, mainCards), ((Window)floating, floatingCards) })
        {
            var template = window.FindControl<ItemsControl>("FeedList")!.ItemTemplate!;
            var regularCard = template.Build(cards[0])!;
            regularCard.DataContext = cards[0];
            var bodyText = Assert.Single(regularCard.GetVisualDescendants().OfType<XmppLinkText>(), text => text.BodyText == body);
            Assert.Equal(TextWrapping.Wrap, bodyText.TextWrapping);
            AssertLink(bodyText, "https://example.org/details");
            var subtitle = Assert.Single(regularCard.GetVisualDescendants().OfType<XmppLinkText>(),
                text => text.BodyText == regular.FeedSubtitle);
            AssertLink(subtitle, "https://example.org/subtitle");
            Assert.True(cards[0].IsUnreadStart);
            Assert.False(cards[1].IsUnreadStart);

            var fleetCard = template.Build(cards[1])!;
            fleetCard.DataContext = cards[1];
            Assert.Contains(fleetCard.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Fleet Ping");
            var hurfText = Assert.Single(fleetCard.GetVisualDescendants().OfType<XmppLinkText>(), text => text.BodyText == hurf);
            AssertLink(hurfText, "https://example.org/hurf");
            var values = fleetCard.GetVisualDescendants().OfType<XmppLinkText>()
                .Where(text => text.BodyText is "C-J6MT" or "Op 3" or "Flycatchers").ToArray();
            Assert.Equal(3, values.Length);
            Assert.All(values, value => Assert.Equal(FontWeight.Bold, Assert.Single(value.Inlines!.OfType<Run>()).FontWeight));
            var commsLink = Assert.Single(values, value => value.BodyText == "Op 3");
            var commsRun = Assert.Single(commsLink.Inlines!.OfType<Run>());
            Assert.Equal(Color.Parse("#E0C06A"), ((ISolidColorBrush)commsRun.Foreground!).Color);
            Assert.DoesNotContain(commsRun.TextDecorations ?? [], decoration => decoration.Location == TextDecorationLocation.Underline);
            var doctrine = Assert.Single(values, value => value.BodyText == "Flycatchers");
            Assert.All(hurfText.Inlines!.OfType<Run>(), run =>
            {
                Assert.Equal(doctrine.FontSize, run.FontSize);
                Assert.Equal(FontWeight.Normal, run.FontWeight);
            });
            var notes = Assert.Single(fleetCard.GetVisualDescendants().OfType<XmppLinkText>(),
                text => text.BodyText == "Get your Ships Ready to Fight\n\nSee https://example.org/notes.");
            Assert.Same(doctrine.Parent, notes.Parent);
            Assert.Equal(Grid.GetColumn(doctrine), Grid.GetColumn(notes));
            Assert.Equal(Grid.GetRow(doctrine) + 1, Grid.GetRow(notes));
            Assert.All(notes.Inlines!.OfType<Run>(), run => Assert.Equal(FontWeight.Normal, run.FontWeight));
            AssertLink(notes, "https://example.org/notes");
            var details = fleetCard.GetVisualDescendants().OfType<TextBlock>().Where(text =>
                text.Text is "Fleet Commander" or "Formup" or "Comms" or "Doctrine").ToArray();
            Assert.Equal(4, details.Length);
            Assert.All(details, label => Assert.Equal(Color.Parse("#E0C06A"), ((ISolidColorBrush)label.Foreground!).Color));
            foreach (var width in new[] { 290, 650 })
            {
                var size = new Size(width, 1500);
                fleetCard.Measure(size);
                fleetCard.Arrange(new Rect(size));
                var position = hurfText.TranslatePoint(new Point(0, hurfText.Bounds.Height), fleetCard);
                var commander = details[0].TranslatePoint(new Point(), fleetCard);
                Assert.True(position!.Value.Y <= commander!.Value.Y);
                Assert.True(hurfText.Bounds.Width <= width);
            }
            portraitImages.Add(Assert.Single(fleetCard.GetVisualDescendants().OfType<Image>()));
        }

        handlers.AllowPortrait.TrySetResult();
        await WaitUntilAsync(() => mainCards[1].Portrait is not null);
        Assert.Null(oldCards[1].Portrait);
        Assert.Equal(2, handlers.Requests.Count);
        Assert.Equal(("POST", "https://esi.evetech.net/universe/ids/"), handlers.Requests[0]);
        Assert.Equal(("GET", "https://images.evetech.net/characters/123/portrait?tenant=tranquility&size=64"),
            handlers.Requests[1]);
        Assert.False(string.IsNullOrWhiteSpace(handlers.CompatibilityDate));
        var portrait = Assert.IsType<Avalonia.Media.Imaging.Bitmap>(mainCards[1].Portrait);
        Assert.Equal(new PixelSize(2, 2), portrait.PixelSize);
        Assert.Same(portrait, floatingCards[1].Portrait);
        Assert.All(portraitImages, image =>
        {
            Assert.True(image.IsVisible);
            Assert.Same(portrait, image.Source);
        });

        static void AssertLink(XmppLinkText text, string url)
        {
            var run = Assert.Single(text.Inlines!.OfType<Run>(), run => run.Text == url);
            Assert.Equal(Color.Parse("#E0C06A"), ((ISolidColorBrush)run.Foreground!).Color);
            Assert.Contains(run.TextDecorations!, decoration => decoration.Location == TextDecorationLocation.Underline);
        }
    }

    [AvaloniaFact]
    public async Task LocustLineupUsesFleetCommanderLayoutAndIndividualLinks()
    {
        const string account = "pilot@example.org";
        var directorbot = XmppAddresses.Directorbot(account);
        const string body = """
            Welcome to Locust Friday USTZ!

            Mining Fleets

            FC: Smeetherson
            Comms: Op 4 https://gnf.lt/2eMgwE2.html

            FC: Gamero
            Comms: Op 6 https://gnf.lt/bO9WiWH.html
            ~~~ This was a broadcast from xiopa_charante to all at 2026-10-09 23:56:13.548604 EVE ~~~
            """;
        var message = new XmppMessage(directorbot, directorbot.Jid, body, DateTimeOffset.UtcNow,
            false, false, false, false, []);
        foreach (var window in new Window[] { new XmppWindow(), new XmppFeedOverlayWindow() })
        {
            var card = Assert.Single(XmppFeedCards.BuildFromMessages([message], account));
            Assert.Equal("Locust Weekend Fleets", card.Title);
            Assert.Equal("Welcome to Locust Friday USTZ!", card.Body);
            Assert.Equal(body, card.Message.Body);
            Assert.Equal(new[] { "Smeetherson", "Gamero" }, card.Lineup.Select(row => row.Name));
            Assert.False(card.HasComms);
            Assert.Equal("https://gnf.lt/bO9WiWH.html", card.Lineup[1].CommsUrl?.AbsoluteUri);
            Assert.All(card.Lineup, row => Assert.True(row.ShowPortraitPlaceholder));

            var template = window.FindControl<ItemsControl>("FeedList")!.ItemTemplate!;
            var control = template.Build(card)!;
            control.DataContext = card;
            control.Width = 290;
            window.Content = control;
            window.Show();
            try
            {
                var rows = Assert.Single(control.GetVisualDescendants().OfType<ItemsControl>());
                await WaitUntilAsync(() => rows.GetVisualDescendants().OfType<Image>().Count() == 2);
                Assert.Equal(2, rows.Items.Count);
                var loader = new XmppFeedPortraitLoader(new RetryPortraitResolver(),
                    NullLogger<XmppFeedPortraitLoader>.Instance, TimeSpan.FromMilliseconds(10));
                loader.Update([card]);
                await WaitUntilAsync(() => card.Lineup.All(row => row.Portrait is not null));
                Assert.All(rows.GetVisualDescendants().OfType<Image>(), image =>
                {
                    Assert.True(image.IsVisible);
                    Assert.Equal(40, image.Width);
                    Assert.Equal(40, image.Height);
                });
                var labels = rows.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Equal(2, labels.Count(label => label == "Fleet Commander"));
                Assert.Equal(2, labels.Count(label => label == "Comms"));
                Assert.DoesNotContain("Formup", labels);
                Assert.DoesNotContain("Doctrine", labels);
                Assert.Single(rows.GetVisualDescendants().OfType<Border>(),
                    border => border.IsVisible && border.Classes.Contains("fleet-divider"));
                var channel = Assert.Single(rows.GetVisualDescendants().OfType<XmppLinkText>(), link =>
                    link.BodyText == "Op 6" && link.Document?.Single().Link == card.Lineup[1].CommsUrl);
                var commander = Assert.Single(rows.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Gamero");
                var details = Assert.IsType<StackPanel>(commander.Parent);
                Assert.Contains(channel, details.GetVisualDescendants());
            }
            finally { window.Close(); }
        }
    }

    private sealed class EmptySettings : ISettingsService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class PortraitResponses(string name = "Feed Test Pilot") : HttpMessageHandler
    {
        public TaskCompletionSource AllowPortrait { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PortraitRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(string Method, string Url)> Requests { get; } = [];
        public string? CompatibilityDate { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.AbsoluteUri));
            if (request.Method == HttpMethod.Post)
            {
                CompatibilityDate = request.Headers.GetValues("X-Compatibility-Date").Single();
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        characters = new[] { new { id = 123, name } }
                    }))
                };
            }
            Assert.False(request.Headers.Contains("X-Compatibility-Date"));
            PortraitRequested.TrySetResult();
            await AllowPortrait.Task.WaitAsync(token);
            return new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(TestPortrait())
            };
        }
    }

    [AvaloniaFact]
    public async Task PortraitRetryUsesTheKnownIdAndWaitsForImageServerBackoff()
    {
        const string account = "pilot@example.org";
        var directorbot = XmppAddresses.Directorbot(account);
        var fleet = new XmppMessage(directorbot, directorbot.Jid,
            "FC Name: Retry Pilot\nPAP Type: Strategic\n~~~ This was a skirmishbot broadcast from bot to all ~~~",
            DateTimeOffset.UtcNow, false, false, true, false, []);
        var cards = XmppFeedCards.BuildFromMessages([fleet], account);
        var resolver = new RetryPortraitResolver();
        var loader = new XmppFeedPortraitLoader(resolver, NullLogger<XmppFeedPortraitLoader>.Instance,
            TimeSpan.FromMilliseconds(10));
        loader.Update(cards);
        Assert.True(cards[0].ShowPortraitPlaceholder);
        await WaitUntilAsync(() => cards[0].Portrait is not null);
        Assert.Equal(1, resolver.NameRequests);
        Assert.Equal(2, resolver.PortraitRequests);
        Assert.True(resolver.SecondRequestAt >= resolver.PortraitRetryNotBefore);
        Assert.False(cards[0].ShowPortraitPlaceholder);
        Assert.False(cards[0].HasBody);
    }

    private sealed class RetryPortraitResolver : IEveCharacterResolver
    {
        public DateTimeOffset PortraitRetryNotBefore { get; private set; }
        public DateTimeOffset SecondRequestAt { get; private set; }
        public int NameRequests { get; private set; }
        public int PortraitRequests { get; private set; }

        public Task<EveCharacterIdentity> ResolveAsync(string server, long characterId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<long?> ResolveIdByNameAsync(string characterName, CancellationToken cancellationToken = default)
        {
            NameRequests++;
            return Task.FromResult<long?>(123);
        }

        public Task<byte[]?> GetPortraitAsync(string server, long characterId, CancellationToken cancellationToken = default)
        {
            if (++PortraitRequests == 1)
            {
                PortraitRetryNotBefore = DateTimeOffset.UtcNow.AddMilliseconds(100);
                return Task.FromResult<byte[]?>(null);
            }
            SecondRequestAt = DateTimeOffset.UtcNow;
            return Task.FromResult<byte[]?>(TestPortrait());
        }
    }

    private static byte[] TestPortrait()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.SteelBlue);
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
        return jpeg.ToArray();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition())
            await Task.Delay(20, timeout.Token);
    }

    [AvaloniaFact]
    public async Task FeedLinksAndCommanderPortraitWorkInBothWindows()
    {
        const string account = "pilot@example.org";
        const string body = "Read https://links.example.org/details.";
        const string commander = "Sir Yohnny";
        var url = new Uri("https://links.example.org/details");
        var directorbot = XmppAddresses.Directorbot(account);
        var database = Path.Combine(Path.GetTempPath(), $"hisa-feed-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
        using var history = new XmppHistoryStore(connectionString);
        using var client = new XmppClientService(new SqliteSettingsService(connectionString), history,
            NullLogger<XmppClientService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, EmptySettings>();
        services.AddHisaEsi();
        // Opt in to one public ESI lookup and image fetch using the same window test.
        if (Environment.GetEnvironmentVariable("HISA_TEST_LIVE_FLEET_PORTRAIT") != "1")
        {
            var responses = new PortraitResponses(commander);
            responses.AllowPortrait.SetResult();
            services.AddHttpClient(EveCharacterResolver.ClientName).ConfigurePrimaryHttpMessageHandler(() => responses);
            services.AddHttpClient(EveCharacterResolver.PortraitClientName).ConfigurePrimaryHttpMessageHandler(() => responses);
        }
        using var provider = services.BuildServiceProvider();
        var loader = new XmppFeedPortraitLoader(provider.GetRequiredService<IEveCharacterResolver>(),
            NullLogger<XmppFeedPortraitLoader>.Instance);
        await client.StartAsync(token);
        try
        {
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            var regular = new XmppMessage(directorbot, directorbot.Jid, body, DateTimeOffset.UtcNow.AddMinutes(-1),
                false, false, false, false, ["links"]) { ShowInFleetFeed = true };
            await history.InsertAsync(account, regular, token);
            await history.InsertAsync(account, regular with
            {
                Body = $"Quick Space Job\n\nFC Name: {commander}\nPAP Type: Strategic\n" +
                       "~~~ This was a skirmishbot broadcast from bot to all ~~~",
                Timestamp = regular.Timestamp.AddSeconds(1),
                IdentityKeys = ["portrait"],
                IsFleetPing = true
            }, token);
            var window = new XmppWindow(client, loader);
            try
            {
                window.Show();
                window.Navigate(directorbot, fleetPing: true);
                await CheckOwnerAsync(window);

                var popout = window.FindControl<Button>("FloatFeedButton")!;
                Assert.True(popout.IsVisible);
                popout.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var floating = Assert.Single(window.OwnedWindows.OfType<XmppFeedOverlayWindow>());
                await CheckOwnerAsync(floating);

                var remember = XmppLinkOpening.ConfirmAsync(floating, client, url);
                var prompt = Assert.Single(floating.OwnedWindows.OfType<XmppDialog>());
                Assert.Single(prompt.Body.GetVisualDescendants().OfType<CheckBox>()).IsChecked = true;
                Press(prompt, "Open");
                Assert.True(await remember.WaitAsync(token));
                Assert.True(client.IsTrustedLinkHost(url.IdnHost));
                Assert.True(await XmppLinkOpening.ConfirmAsync(window, client, url).WaitAsync(token));
                Assert.Empty(window.OwnedWindows.OfType<XmppDialog>());

                var clear = window.FindControl<Button>("ClearFeedButton")!;
                Assert.True(clear.IsVisible);
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntilAsync(() => window.FindControl<ItemsControl>("FeedList")!.Items.Count == 0 &&
                    floating.FindControl<ItemsControl>("FeedList")!.Items.Count == 0);
                Assert.Equal(2, (await client.ReadAsync(directorbot, false)).Count);
            }
            finally { window.Close(); }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            history.Dispose();
            File.Delete(database);
        }

        async Task CheckOwnerAsync(Window owner)
        {
            var feed = owner.FindControl<ItemsControl>("FeedList")!;
            await WaitUntilAsync(() => feed.GetVisualDescendants().OfType<Image>()
                .Any(image => image.IsVisible && image.Source is Avalonia.Media.Imaging.Bitmap));
            var linkText = Assert.Single(feed.GetVisualDescendants().OfType<XmppLinkText>(), text => text.BodyText == body);
            var hit = linkText.TextLayout.HitTestTextPosition(body.IndexOf("https", StringComparison.Ordinal) + 4);
            var point = linkText.TranslatePoint(hit.Center, owner)!.Value;
            owner.MouseMove(point);
            Assert.Equal(url.AbsoluteUri, ToolTip.GetTip(linkText));
            Assert.Equal(StandardCursorType.Hand.ToString(), linkText.Cursor?.ToString());
            owner.MouseDown(point, MouseButton.Left);
            owner.MouseUp(point, MouseButton.Left);
            var dialog = Assert.Single(owner.OwnedWindows.OfType<XmppDialog>());
            Assert.Contains(dialog.Body.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text?.Contains(url.AbsoluteUri, StringComparison.Ordinal) == true);
            Press(dialog, "Cancel");
            Assert.False(client.IsTrustedLinkHost(url.IdnHost));
        }

        static void Press(Window window, string content) =>
            Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, content))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaTheory]
    [InlineData(0d)]
    [InlineData(null)]
    [InlineData(0.25)]
    [InlineData(1.5)]
    [InlineData(12d)]
    public void TranscriptRowsHaveEqualPaddingAroundWrappedContent(double? padding)
    {
        var font = new FontFamily("Inter");
        var conversation = new XmppConversation(XmppConversationKind.Direct, "ally@example.org");
        XmppMessageRow Row(long id, string body)
        {
            var message = new XmppMessage(conversation, conversation.Jid, body, DateTimeOffset.UnixEpoch,
                false, false, false, false, []) { LocalId = id };
            return new(message, "", false, false, "", "", "", Brushes.Transparent, Brushes.Transparent,
                Brushes.White, false, Brushes.SlateGray, new Thickness(0, 1, 0, 0), font, 14);
        }

        var build = typeof(XmppWindow).GetMethod("BuildTranscriptRuns", BindingFlags.Static | BindingFlags.NonPublic)!;
        XmppMessageRow[] rows =
        [
            Row(1, "gyp and Ascenders on a line long enough to wrap"),
            Row(2, "Next") with { IsUnreadStart = true, DividerThickness = default }
        ];
        var runs = (IReadOnlyList<XmppTranscriptRun>)build.Invoke(null, [rows, font, padding])!;
        Assert.Equal(2, Assert.Single(runs, run => run.UnreadDivider).MessageId);
        var transcript = new XmppLinkText
        {
            FontFamily = font, FontSize = 0.001, LayoutFontSize = 14, TextWrapping = TextWrapping.Wrap, Document = runs
        };
        var size = new Size(100, 500);
        transcript.Measure(size);
        transcript.Arrange(new Rect(size));

        var bands = transcript.MessageBounds();
        Assert.Equal(new long[] { 1, 2 }, bands.Select(band => band.Id));
        Assert.Equal(bands[0].Bottom, bands[1].Top, 6);
        transcript.SelectAll();
        Assert.Contains("gyp and Ascenders", transcript.SelectedText);
        Assert.Equal(bands, transcript.MessageBounds());
        transcript.Measure(size);
        transcript.Arrange(new Rect(size));
        Assert.Equal(bands, transcript.MessageBounds());
        transcript.ClearSelection();
        Assert.Equal(bands, transcript.MessageBounds());
        var lines = transcript.TextLayout.TextLines;
        var expectedPadding = padding ?? XmppDisplayPreferences.DefaultMessageBlockPadding;
        if (expectedPadding == 0)
        {
            Assert.True(lines[0].Height > 10);
            return;
        }

        var firstRowLength = runs.Where(run => run.MessageId == 1).Sum(run => run.Text.Length);
        var firstRowLines = lines.TakeWhile(line => line.FirstTextSourceIndex < firstRowLength).ToArray();
        Assert.True(firstRowLines.Length > 3);
        Assert.Equal(firstRowLines[0].Height, firstRowLines[^1].Height, 6);
        Assert.InRange(Math.Abs(expectedPadding - firstRowLines[0].Height), 0, 0.01);
    }

    [AvaloniaFact]
    public async Task RestoresTabsAndPinAndKeepsDraftAndTranscriptPositionDuringNavigation()
    {
        const string account = "pilot@example.org";
        var room = new XmppConversation(XmppConversationKind.Room, "fleet@conference.example.org");
        var direct = new XmppConversation(XmppConversationKind.Direct, "ally@example.org");
        var database = Path.Combine(Path.GetTempPath(), $"hisa-workspace-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
        using var history = new XmppHistoryStore(connectionString);
        using var client = new XmppClientService(new SqliteSettingsService(connectionString), history,
            NullLogger<XmppClientService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        await client.StartAsync(token);
        try
        {
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            await client.SaveRoomAsync(room.Jid, "pilot", false, null, false, false);
            await client.SaveWorkspaceAsync(account, new([room, direct], room, false, 900, 590, Pinned: true));
            var stamp = DateTimeOffset.UtcNow.AddHours(-1);
            foreach (var number in Enumerable.Range(1, 65))
                await history.InsertAsync(account, new(room, room.Jid + "/Ally", $"Message {number}\nSecond line",
                    stamp.AddMinutes(number / 2d), false, false, false, true, [$"room-{number}"]), token);

            var window = new XmppWindow(client);
            try
            {
                window.Show();
                var title = window.FindControl<TextBlock>("ConversationTitle")!;
                var transcript = window.FindControl<XmppLinkText>("TranscriptText")!;
                var scroll = window.FindControl<ScrollViewer>("TranscriptScroll")!;
                var tabStrip = window.FindControl<StackPanel>("TabStrip")!;
                var compose = window.FindControl<TextBox>("Compose")!;
                var sidebar = window.FindControl<Border>("Sidebar")!;
                await Until(() => title.Text == "fleet" && transcript.MessageBounds().Count == 65);
                Assert.Equal(1, Assert.Single(transcript.Document!, run => run.UnreadDivider).MessageId);
                Assert.True(window.Topmost);
                Assert.Equal(3, tabStrip.Children.Count);
                Assert.True(sidebar.IsVisible);

                await Until(() => scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y < 3);
                scroll.Offset = new(0, 155);
                await Until(() => scroll.Offset.Y is > 140 and < 170);
                compose.Text = "room draft";
                compose.Focus();
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
                await Until(() => title.Text == "ally");
                Assert.Equal("", compose.Text);
                compose.Text = "direct draft";

                window.Navigate(room, fleetPing: false);
                await Until(() => title.Text == "fleet" && scroll.Offset.Y is > 125 and < 185);
                Assert.Equal("room draft", compose.Text);
                Assert.Equal(3, tabStrip.Children.Count);

                var anchor = FirstVisible();
                await history.InsertAsync(account, new(room, room.Jid + "/New",
                    "Recovered between older messages\nAn extra line above the reader", stamp.AddSeconds(33),
                    false, true, false, false, ["archive-added"]), token, XmppHistoryDelivery.RoomArchive);
                await history.InsertAsync(account, new(room, room.Jid + "/New",
                    "Newest message", DateTimeOffset.UtcNow, false, false, false, true, ["live-added"]), token);
                await client.SaveRoomAsync(room.Jid, "pilot", false, null, false, false);
                var newMessages = window.FindControl<Button>("NewMessagesButton")!;
                await Until(() => transcript.MessageBounds().Count == 67 && newMessages.IsVisible && FirstVisible().Id == anchor.Id);
                Assert.InRange(Math.Abs(FirstVisible().Top - anchor.Top), 0, 2);
                newMessages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => !newMessages.IsVisible && scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y < 3);

                var roomHeader = Assert.IsType<Border>(tabStrip.Children[1]);
                var roomButtons = Assert.IsType<StackPanel>(roomHeader.Child);
                Assert.IsType<Button>(roomButtons.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, tabStrip.Children.Count);
                window.Navigate(room, fleetPing: false);
                await Until(() => title.Text == "fleet");
                Assert.Equal("room draft", compose.Text);

                var feedFocus = Stopwatch.StartNew();
                window.Navigate(XmppAddresses.Directorbot(account), fleetPing: true);
                await Until(() => title.Text == "Notification Feed");
                Assert.False(window.FindControl<Grid>("ComposeArea")!.IsVisible);

                var feedAlert = new XmppMessage(room, room.Jid + "/Scout", "Room alert in the feed",
                    DateTimeOffset.UtcNow, false, false, false, true, ["feed-alert"]) { ShowInFleetFeed = true };
                await history.InsertAsync(account, feedAlert, token);
                await client.SaveRoomAsync(room.Jid, "pilot", false, null, false, false);
                window.Activate();
                var feedList = window.FindControl<ItemsControl>("FeedList")!;
                await Until(() => feedList.ItemsSource is IReadOnlyList<XmppFeedCardVm> { Count: 1 } cards &&
                    cards[0].IsUnreadStart);
                window.Navigate(direct, fleetPing: false);
                await Until(() => title.Text == "ally");
                Assert.Equal(1, await client.FeedUnreadCountAsync());
                await Until(() => client.FeedUnreadCountAsync().GetAwaiter().GetResult() == 0);
                Assert.True(feedFocus.Elapsed >= TimeSpan.FromSeconds(3));
                window.Navigate(XmppAddresses.Directorbot(account), fleetPing: true);
                await Until(() => feedList.ItemsSource is IReadOnlyList<XmppFeedCardVm> { Count: 1 } cards &&
                    !cards[0].Message.Unread && !cards[0].IsUnreadStart);

                window.Navigate(direct, fleetPing: false);
                await Until(() => title.Text == "ally");
                await history.InsertAsync(account, feedAlert with
                {
                    Body = "A second alert for the floating feed",
                    IdentityKeys = ["floating-alert"]
                }, token);
                var floatingFocus = Stopwatch.StartNew();
                window.ShowFeedOverlay();
                var floating = Assert.IsType<XmppFeedOverlayWindow>(Assert.Single(window.OwnedWindows));
                floating.Activate();
                try
                {
                    var floatingFeed = floating.FindControl<ItemsControl>("FeedList")!;
                    await Until(() => floatingFeed.ItemsSource is IReadOnlyList<XmppFeedCardVm> { Count: 2 } cards &&
                        cards[1].IsUnreadStart);
                    floating.Hide();
                    Assert.Equal(1, await client.FeedUnreadCountAsync());
                    await Until(() => client.FeedUnreadCountAsync().GetAwaiter().GetResult() == 0);
                    Assert.True(floatingFocus.Elapsed >= TimeSpan.FromSeconds(3));
                    floating.Show();
                    await Until(() => floatingFeed.ItemsSource is IReadOnlyList<XmppFeedCardVm> { Count: 2 } cards &&
                        cards.All(card => !card.Message.Unread && !card.IsUnreadStart));
                }
                finally { floating.Close(); }
                window.Navigate(XmppAddresses.Directorbot(account), fleetPing: true);

                window.Width = 535;
                await Until(() => !sidebar.IsVisible);
                window.FindControl<Button>("SidebarToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(sidebar.IsVisible);
                var pinButton = window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("PinButton")!;
                pinButton.IsChecked = false;
                pinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.Topmost);
                await Until(() => client.LoadWorkspaceAsync(account).GetAwaiter().GetResult() is
                    { FleetSelected: true, Pinned: false, Width: < 600 });

                (long Id, double Top) FirstVisible() => transcript.MessageBounds()
                    .Select(row => (row.Id, Top: row.Top - scroll.Offset.Y, Bottom: row.Bottom - scroll.Offset.Y))
                    .Where(row => row.Bottom > 0 && row.Top < scroll.Viewport.Height)
                    .OrderBy(row => row.Top).Select(row => (row.Id, row.Top)).FirstOrDefault();
            }
            finally { window.Close(); }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            history.Dispose();
            File.Delete(database);
        }

        async Task Until(Func<bool> condition)
        {
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(20, token);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaFact]
    public async Task ReordersTabsAndPersistsWorkspaceOrder()
    {
        const string account = "pilot@example.org";
        var room = new XmppConversation(XmppConversationKind.Room, "fleet@conference.example.org");
        var direct = new XmppConversation(XmppConversationKind.Direct, "ally@example.org");
        var database = Path.Combine(Path.GetTempPath(), $"hisa-tab-order-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
        using var history = new XmppHistoryStore(connectionString);
        using var client = new XmppClientService(new SqliteSettingsService(connectionString), history,
            NullLogger<XmppClientService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        await client.StartAsync(token);
        try
        {
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            await client.SaveWorkspaceAsync(account, new([room, direct], room, false, 900, 590));

            var window = new XmppWindow(client);
            try
            {
                window.Show();
                var title = window.FindControl<TextBlock>("ConversationTitle")!;
                var tabStrip = window.FindControl<StackPanel>("TabStrip")!;
                await Until(() => title.Text == "fleet" && tabStrip.Children.Count == 3);

                var reorder = typeof(XmppWindow).GetMethod("TryReorderTabForTests",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.True((bool)reorder.Invoke(window, [direct.Jid, 1])!);
                await Until(() =>
                {
                    var saved = client.LoadWorkspaceAsync(account).GetAwaiter().GetResult();
                    return saved is { Tabs.Count: 2 } &&
                           saved.Tabs[0] == direct &&
                           saved.Tabs[1] == room;
                });

                Assert.Equal(3, tabStrip.Children.Count);
                var firstConversation = Assert.IsType<Border>(tabStrip.Children[1]);
                Assert.Contains("ally", Avalonia.Automation.AutomationProperties.GetName(
                    Assert.IsType<Button>(Assert.IsType<StackPanel>(firstConversation.Child).Children[0]))!,
                    StringComparison.OrdinalIgnoreCase);
            }
            finally { window.Close(); }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            history.Dispose();
            File.Delete(database);
        }

        async Task Until(Func<bool> condition)
        {
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(20, token);
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DragSelectionKeepsRowsStableAndDefersUpdatesUntilFocusMoves(bool split)
    {
        const string account = "pilot@example.org";
        var direct = new XmppConversation(XmppConversationKind.Direct, "ally@example.org");
        var room = new XmppConversation(XmppConversationKind.Room, "fleet@conference.example.org");
        var conversation = split ? room : direct;
        var database = Path.Combine(Path.GetTempPath(), $"hisa-selection-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
        using var history = new XmppHistoryStore(connectionString);
        using var client = new XmppClientService(new SqliteSettingsService(connectionString), history,
            NullLogger<XmppClientService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        await client.StartAsync(token);
        try
        {
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            await client.SaveRoomAsync(room.Jid, "pilot", false, null, false, false);
            await client.SaveWorkspaceAsync(account, new([direct, room], direct, false, 900, 590));
            var stamp = DateTimeOffset.UtcNow.AddMinutes(-1);
            var sender = split ? room.Jid + "/Scout" : direct.Jid;
            var first = new XmppMessage(conversation, sender, "Start " + string.Concat(Enumerable.Repeat("wrapped words ", 20)),
                stamp, false, false, false, false, ["first"]);
            await history.InsertAsync(account, first, token);
            await history.InsertAsync(account, first with
            {
                Body = "Next row and end of selection.", IdentityKeys = ["second"], Timestamp = stamp.AddSeconds(1)
            }, token);

            var window = new XmppWindow(client);
            try
            {
                window.Show();
                var title = window.FindControl<TextBlock>("ConversationTitle")!;
                await WaitUntilAsync(() => title.Text == "ally");
                if (split)
                {
                    const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
                    var tabs = (System.Collections.IEnumerable)typeof(XmppWindow).GetField("_tabs", access)!.GetValue(window)!;
                    var roomTab = tabs.Cast<object>().Single(tab =>
                        Equals(tab.GetType().GetProperty("Conversation")!.GetValue(tab), room));
                    Assert.True((bool)typeof(XmppWindow).GetMethod("MoveTab", access)!.Invoke(window, [roomTab, true])!);
                }

                var transcript = window.FindControl<XmppLinkText>(split ? "SplitTranscriptText" : "TranscriptText")!;
                await WaitUntilAsync(() => transcript.MessageBounds().Count == 2);
                var bounds = transcript.MessageBounds();
                var document = transcript.Document!;
                var text = string.Concat(document.Select(run => run.Text));
                Point Position(int index)
                {
                    var hit = transcript.TextLayout.HitTestTextPosition(index).Center;
                    return transcript.TranslatePoint(hit + new Vector(transcript.Padding.Left, transcript.Padding.Top),
                        window)!.Value;
                }

                var begin = Position(text.IndexOf("Start", StringComparison.Ordinal) + 1);
                var end = Position(text.IndexOf("end of selection.", StringComparison.Ordinal) + 8);
                window.MouseDown(begin, MouseButton.Left);

                await history.InsertAsync(account, first with
                {
                    Body = "Incoming while selected", IdentityKeys = ["incoming"], Timestamp = stamp.AddSeconds(2)
                }, token);
                await client.SaveRoomAsync(room.Jid, "pilot", false, null, false, false);
                var pending = typeof(XmppWindow).GetField(split ? "_pendingSplitRender" : "_pendingTranscriptRender",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                await WaitUntilAsync(() => pending.GetValue(window) is not null);
                Assert.Same(document, transcript.Document);

                window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                window.MouseUp(end, MouseButton.Left);
                Assert.Contains("wrapped words", transcript.SelectedText);
                Assert.Contains("Next row", transcript.SelectedText);
                Assert.Same(document, transcript.Document);
                Assert.Equal(bounds, transcript.MessageBounds());
                transcript.Copy();
                Assert.Equal(transcript.SelectedText, await window.Clipboard!.TryGetTextAsync());

                Assert.True(window.FindControl<TextBox>("Compose")!.Focus());
                await WaitUntilAsync(() => transcript.MessageBounds().Count == 3);
                Assert.Empty(transcript.SelectedText);

                Assert.True(transcript.Focus());
                transcript.SelectAll();
                Assert.NotEmpty(transcript.SelectedText);
                var scroll = window.FindControl<ScrollViewer>(split ? "SplitTranscriptScroll" : "TranscriptScroll")!;
                var blank = scroll.TranslatePoint(new Point(12, scroll.Bounds.Height - 8), window)!.Value;
                window.MouseDown(blank, MouseButton.Left);
                window.MouseUp(blank, MouseButton.Left);
                Assert.Empty(transcript.SelectedText);

                if (!split)
                {
                    transcript.Focus();
                    transcript.SelectAll();
                    window.Navigate(room, fleetPing: false);
                    await WaitUntilAsync(() => title.Text == "fleet" && transcript.MessageBounds().Count == 0);
                    Assert.Empty(transcript.SelectedText);
                }
            }
            finally { window.Close(); }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            history.Dispose();
            File.Delete(database);
        }
    }

    [AvaloniaFact]
    public async Task CollapsingSplitMovesTabsBackWithoutThrowing()
    {
        const string account = "pilot@example.org";
        var room = new XmppConversation(XmppConversationKind.Room, "fleet@conference.example.org");
        var direct = new XmppConversation(XmppConversationKind.Direct, "ally@example.org");
        var database = Path.Combine(Path.GetTempPath(), $"hisa-split-collapse-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
        using var history = new XmppHistoryStore(connectionString);
        using var client = new XmppClientService(new SqliteSettingsService(connectionString), history,
            NullLogger<XmppClientService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        await client.StartAsync(token);
        try
        {
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.Automatic, null, false, false, connect: false);
            await client.SaveWorkspaceAsync(account, new([room, direct], room, false, 900, 590));
            await history.InsertAsync(account, new(direct, direct.Jid, "Unread in the split pane",
                DateTimeOffset.UtcNow, false, false, false, true, ["split-message"]), token);

            var window = new XmppWindow(client);
            try
            {
                window.Show();
                var tabStrip = window.FindControl<StackPanel>("TabStrip")!;
                var splitStrip = window.FindControl<StackPanel>("SplitTabStrip")!;
                var title = window.FindControl<TextBlock>("ConversationTitle")!;
                await Until(() => title.Text == "fleet" && tabStrip.Children.Count == 3);

                var moveTab = typeof(XmppWindow).GetMethod("MoveTab", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var tabsField = typeof(XmppWindow).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var tabs = (System.Collections.IList)tabsField.GetValue(window)!;
                object? directTab = null;
                foreach (var tab in tabs)
                {
                    var conversation = tab.GetType().GetProperty("Conversation")!.GetValue(tab);
                    if (Equals(conversation, direct))
                    {
                        directTab = tab;
                        break;
                    }
                }
                Assert.NotNull(directTab);
                Assert.True((bool)moveTab.Invoke(window, [directTab, true])!);
                await Until(() => splitStrip.Children.Count == 1 && tabStrip.Children.Count == 2);

                PressTab(tabStrip.Children[1]);
                var splitTranscript = window.FindControl<XmppLinkText>("SplitTranscriptText")!;
                await Until(() => splitTranscript.Document?.Any(run => run.UnreadDivider) == true);
                Assert.Single(splitTranscript.Document!, run => run.UnreadDivider);

                var splitFocus = Stopwatch.StartNew();
                PressTab(splitStrip.Children[0]);
                window.Activate();
                await Task.Delay(250, token);
                Assert.Single(splitTranscript.Document!, run => run.UnreadDivider);
                PressTab(tabStrip.Children[1]);
                var later = new XmppMessage(direct, direct.Jid, "Arrived after leaving the split pane",
                    DateTimeOffset.UtcNow, false, false, false, true, ["split-later"]);
                await history.InsertAsync(account, later, token);
                await client.SaveRoomAsync(room.Jid, "pilot", false, null, false, false);
                await Until(() => client.ReadAsync(direct, false).GetAwaiter().GetResult() is
                    [{ Unread: false }, { Unread: true }]);
                Assert.InRange(splitFocus.Elapsed, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4.9));
                PressTab(splitStrip.Children[0]);
                await Until(() => splitTranscript.Document?.All(run => !run.UnreadDivider) == true);

                var feedback = window.FindControl<TextBlock>("Feedback")!;
                foreach (var (composerName, buttonName) in new[] { ("Compose", "SendButton"), ("SplitCompose", "SplitSendButton") })
                {
                    var composer = window.FindControl<TextBox>(composerName)!;
                    var send = window.FindControl<Button>(buttonName)!;
                    composer.Text = "/help";
                    send.IsEnabled = true;
                    send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal("", composer.Text);
                    Assert.Contains("/topic", feedback.Text);
                }

                var splitButton = window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("SplitButton")!;
                splitButton.IsChecked = false;
                var toggleSplit = typeof(XmppWindow).GetMethod("ToggleSplit", BindingFlags.Instance | BindingFlags.NonPublic)!;
                toggleSplit.Invoke(window, [splitButton, new RoutedEventArgs()]);

                Assert.Empty(splitStrip.Children);
                Assert.Equal(3, tabStrip.Children.Count);
                Assert.All(tabStrip.Children, child => Assert.True(((Control)child).Parent == tabStrip));
            }
            finally { window.Close(); }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            history.Dispose();
            File.Delete(database);
        }

        async Task Until(Func<bool> condition)
        {
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(20, token);
                Dispatcher.UIThread.RunJobs();
            }
        }

        static void PressTab(Control header)
        {
            var buttons = Assert.IsType<StackPanel>(Assert.IsType<Border>(header).Child);
            Assert.IsType<Button>(buttons.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    [Fact]
    public void SameTranscript_DetectsUnreadOnlyChanges()
    {
        var conversation = new XmppConversation(XmppConversationKind.Direct, "ally@example.org");
        XmppMessage Row(long id, bool unread) =>
            new(conversation, conversation.Jid, "hi", DateTimeOffset.UnixEpoch, false, false, false, unread, [])
            {
                LocalId = id
            };

        var first = new[] { Row(1, true), Row(2, true) };
        var cleared = new[] { Row(1, false), Row(2, false) };
        var appended = new[] { Row(1, false), Row(2, false), Row(3, true) };

        Assert.True(XmppWindow.SameTranscript(first, [.. first]));
        Assert.False(XmppWindow.SameTranscript(first, cleared));
        Assert.False(XmppWindow.SameTranscript(first, appended));
        Assert.False(XmppWindow.SameTranscript(first, [Row(1, true)]));
    }
}
