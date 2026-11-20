using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class XmppWindow
{
    private IReadOnlyList<XmppFeedCardVm> _feedCards = [];
    private XmppFeedOverlayWindow? _feedOverlay;
    private int _feedClearVersion;

    private void RenderFeedCards(ConversationTab tab)
    {
        var cards = BuildFeedCards(tab);
        _feedCards = cards;
        FeedList.ItemsSource = cards;
        FeedList.IsVisible = true;
        TranscriptText.IsVisible = false;
        TranscriptText.Document = Array.Empty<XmppTranscriptRun>();
        UpdateFeedPortraits();
    }

    private void RenderTranscriptDocument(IReadOnlyList<XmppMessageRow> rows)
    {
        FeedList.IsVisible = false;
        FeedList.ItemsSource = null;
        _feedCards = [];
        UpdateFeedPortraits();
        TranscriptText.IsVisible = true;
        TranscriptText.Document = BuildDocument(rows);
    }

    private IReadOnlyList<XmppFeedCardVm> BuildFeedCards(ConversationTab tab) =>
        XmppFeedCards.Build(BuildFilteredEntries(tab.Messages), _client.Account?.Jid);

    internal void UpdateFeedPortraits()
    {
        if (_portraitLoader is not { } loader) return;
        IReadOnlyList<XmppFeedCardVm> main = !_closed && IsVisible ? _feedCards : [];
        loader.Update(_feedOverlay is { IsVisible: true } overlay ? [.. main, .. overlay.FeedCards] : main);
    }

    private IEnumerable<(XmppMessage Message, double Top, double Bottom)> RealizedFeedRows(bool readableOnly) =>
        XmppFeedCards.RealizedRows(FeedList, TranscriptScroll, _feedCards, readableOnly);

    private bool TryScrollToFeedMessage(long id)
    {
        for (var index = 0; index < _feedCards.Count; index++)
        {
            if (_feedCards[index].LocalId != id) continue;
            if (FeedList.ContainerFromIndex(index) is not Control container) return false;
            var origin = container.TranslatePoint(new Point(0, 0), TranscriptScroll);
            if (origin is not { } point) return false;
            TranscriptScroll.Offset = new(0, Math.Max(0, TranscriptScroll.Offset.Y + point.Y - 12));
            return true;
        }
        return false;
    }

    private void FlashButton(Button button, string flash)
    {
        var original = button.Content;
        button.Content = flash;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Equals(button.Content, flash))
                button.Content = original;
        };
        timer.Start();
    }

    private async void OpenFeedComms(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Uri url } button && await XmppLinkOpening.OpenAsync(this, _client, url))
            FlashButton(button, "Opening…");
    }

    private void ShowFeedSystemOnMap(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string system } button || string.IsNullOrWhiteSpace(system)) return;
        RequestMapSystem(system.Trim());
        FlashButton(button, "Opening…");
    }

    internal void RequestMapSystem(string system) => MapSystemRequested?.Invoke(system);

    private void OpenFloatingFeed(object? sender, RoutedEventArgs e) => ShowFeedOverlay();

    private async void ClearNotificationFeed(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var tab = _active;
        if (tab.Conversation is not null || _client.Account is null) return;

        await _client.ClearFeedAsync();
        _feedClearVersion++;
        tab.ResetHistory();
        if (_active == tab) RenderTranscript(tab, null, true);
    });

    internal void ShowFeedOverlay()
    {
        if (_closed) return;
        if (_feedOverlay is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Show(this);
            existing.Activate();
            return;
        }

        _feedOverlay = new XmppFeedOverlayWindow(_client, this)
        {
            Topmost = Topmost
        };
        _feedOverlay.Closed += (_, _) =>
        {
            _feedOverlay = null;
            UpdateFeedPortraits();
        };
        _feedOverlay.Show(this);
    }
}
