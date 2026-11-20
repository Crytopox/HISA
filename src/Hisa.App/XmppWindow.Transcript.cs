using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class XmppWindow
{
    private List<XmppMessageRow> BuildRows(ConversationTab tab, bool feedView)
    {
        var prefs = _client.DisplayPreferences;
        var fontFamily = new FontFamily(string.IsNullOrWhiteSpace(prefs.FontFamily) ? "Inter" : prefs.FontFamily);
        var fontSize = Math.Clamp(prefs.MessageFontSize, 11, 18);
        var highlightRules = _client.Rules.Where(rule => rule.Enabled && rule.Highlight).ToArray();
        var rows = new List<XmppMessageRow>();
        XmppMessage? previous = null;
        var unreadDivider = false;
        var index = 0;
        var source = tab.TopicNotices.Count == 0
            ? tab.Messages
            : tab.Messages.Concat(tab.TopicNotices).OrderBy(message => message.Timestamp).ThenBy(message => message.LocalId).ToArray();
        foreach (var entry in BuildFilteredEntries(source))
        {
            if (entry.Collapse is { } collapse)
            {
                rows.Add(XmppMessageRow.Collapse(collapse.Key, collapse.Label, collapse.Representative,
                    fontFamily, fontSize));
                previous = null;
                continue;
            }

            var message = entry.Message!;
            var nickname = message.Conversation.Kind == XmppConversationKind.Room
                ? _client.Rooms.FirstOrDefault(room => room.Jid == message.Conversation.Jid)?.Nickname
                : null;
            // Live rule state only — ignore stored Highlight so toggling the rule (and
            // clearing the old fleet-ping force-flag) takes effect in Directorbot chat.
            var highlight = XmppAlertMatching.ShouldHighlight(highlightRules, message, nickname);
            var dated = previous is null || previous.Timestamp.LocalDateTime.Date != message.Timestamp.LocalDateTime.Date;
            var firstUnread = message.Unread && !unreadDivider;
            unreadDivider |= message.Unread;
            var sameSpeaker = previous is not null && message.Outgoing == previous.Outgoing &&
                (message.Conversation.Kind == XmppConversationKind.Direct ||
                 string.Equals(message.Sender, previous.Sender, StringComparison.Ordinal));
            var grouped = !feedView && !prefs.AlwaysShowMessageHeaders && !dated && !firstUnread && previous is not null &&
                !highlight && !IsActionMessage(message) && !IsActionMessage(previous) && sameSpeaker &&
                message.Timestamp - previous.Timestamp <= TimeSpan.FromMinutes(3);
            var sender = DisplaySender(message);
            var peacetimeFleet = message.IsFleetPing &&
                XmppFeedCards.IsPeacetimePap(XmppFleetPingParsing.TryParse(message.Body)?.PapType);
            var accent = highlight
                ? (message.IsDelveBping ? DelveHelpPing :
                    message.IsFleetPing ? (peacetimeFleet ? PeacetimeFleetPing : FleetPing) :
                    AlertHighlight)
                : message.Mention ? Mention : Brushes.Transparent;
            var bar = highlight
                ? (message.IsDelveBping ? DelveHelpBar :
                    message.IsFleetPing ? (peacetimeFleet ? PeacetimeFleetBar : FleetBar) :
                    AlertHighlightBar)
                : message.Mention ? MentionBar : Brushes.Transparent;
            var rowBg = highlight || message.Mention ? accent :
                prefs.AlternatingMessageColors && index % 2 == 1 ? AltRow : Brushes.Transparent;
            var divider = prefs.ShowMessageDividers && previous is not null && !firstUnread
                ? new Thickness(0, 1, 0, 0) : new Thickness(0);
            var time = grouped ? "" : FormatMessageTime(message.Timestamp);
            var senderLabel = grouped ? "" : sender;
            var senderJid = ResolveSenderJid(message, sender);
            var feedTitle = feedView
                ? (string.IsNullOrWhiteSpace(message.FeedTitle)
                    ? (message.IsFleetPing ? "Fleet ping" : "Alert")
                    : message.FeedTitle)
                : "";
            var feedSubtitle = feedView ? message.FeedSubtitle ?? "" : "";
            rows.Add(new(message, dated ? message.Timestamp.ToLocalTime().ToString("ddd, MMM d, yyyy") : "",
                firstUnread, !grouped, sender, time, senderLabel, accent, bar,
                message.Outgoing ? Active : SenderColor(sender),
                !grouped, rowBg, divider, fontFamily, fontSize, senderJid,
                IsFeedRow: feedView, FeedTitle: feedTitle, FeedSubtitle: feedSubtitle, Html: message.Html));
            previous = message;
            index++;
        }
        return rows;
    }

    private void RenderTranscript(ConversationTab tab, ScrollAnchor? anchor, bool follow)
    {
        if (_renderedTab == tab && TranscriptText.IsSelecting)
        {
            _pendingTranscriptRender = (tab, anchor, follow);
            return;
        }
        _pendingTranscriptRender = null;
        if (_renderedTab != tab) TranscriptText.EndSelection();
        var rows = BuildRows(tab, tab.Conversation is null);
        _renderedTab = tab;
        tab.Follow = follow;
        _restoringScroll = true;
        var generation = ++_scrollGeneration;
        var scrollToId = _pendingScrollToLocalId;
        if (scrollToId is { } pendingId && rows.Any(row => row.Message.LocalId == pendingId))
            _pendingScrollToLocalId = null;
        ApplyDisplayFonts();
        if (tab.Conversation is null) RenderFeedCards(tab);
        else RenderTranscriptDocument(rows);
        OlderButton.IsVisible = tab.HasEarlier;
        NewMessagesButton.IsVisible = tab.NewMessages;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _scrollGeneration || _closed) return;
            if (scrollToId is not { } id || !TryScrollToMessage(id))
            {
                if (follow) TranscriptScroll.ScrollToEnd();
                else if (anchor is not null) RestoreAnchor(anchor);
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _scrollGeneration || _closed) return;
                if (scrollToId is null && follow) TranscriptScroll.ScrollToEnd();
                _restoringScroll = false;
                VisibleRowsChanged();
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

    private void ObserveTranscriptSelection(XmppLinkText text)
    {
        text.PropertyChanged += (_, change) =>
        {
            if (change.Property == SelectableTextBlock.SelectionStartProperty ||
                change.Property == SelectableTextBlock.SelectionEndProperty)
                QueuePendingTranscriptRenders();
        };
        text.PointerCaptureLost += (_, _) => QueuePendingTranscriptRenders();
        text.AddHandler(PointerReleasedEvent, (_, _) => QueuePendingTranscriptRenders(),
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void QueuePendingTranscriptRenders()
    {
        if (_transcriptFlushQueued || (_pendingTranscriptRender is null && _pendingSplitRender is null))
            return;
        _transcriptFlushQueued = true;
        // Selection endpoints can change separately within a single input event.
        Dispatcher.UIThread.Post(() =>
        {
            _transcriptFlushQueued = false;
            if (!_closed) FlushPendingTranscriptRenders();
        }, DispatcherPriority.Background);
    }

    private void FlushPendingTranscriptRenders()
    {
        if (_pendingTranscriptRender is { } pending && !TranscriptText.IsSelecting)
        {
            _pendingTranscriptRender = null;
            if (pending.Tab == _active)
            {
                var follow = pending.Follow && _active.Follow && NearBottom();
                RenderTranscript(pending.Tab, follow ? null : CaptureAnchor() ?? pending.Anchor, follow);
            }
        }
        if (_pendingSplitRender is { } split && !SplitTranscriptText.IsSelecting)
        {
            _pendingSplitRender = null;
            if (_splitTab == split && SplitPane.IsVisible) RenderSplit(split);
        }
    }

    private bool TryScrollToMessage(long id)
    {
        if (FeedList.IsVisible) return TryScrollToFeedMessage(id);
        foreach (var bounds in TranscriptText.MessageBounds())
        {
            if (bounds.Id != id) continue;
            TranscriptScroll.Offset = new(0, Math.Max(0, bounds.Top - 12));
            return true;
        }
        return false;
    }

    private void RestoreAnchor(ScrollAnchor anchor, bool split = false)
    {
        if (!split && FeedList.IsVisible)
        {
            foreach (var row in RealizedFeedRows(readableOnly: false))
            {
                if (row.Message.LocalId != anchor.Id) continue;
                TranscriptScroll.Offset = new(0, Math.Max(0, row.Top - anchor.Top + TranscriptScroll.Offset.Y));
                return;
            }
            return;
        }
        var viewer = split ? SplitTranscriptScroll : TranscriptScroll;
        var text = split ? SplitTranscriptText : TranscriptText;
        foreach (var bounds in text.MessageBounds())
        {
            if (bounds.Id != anchor.Id) continue;
            viewer.Offset = new(0, Math.Max(0, bounds.Top - anchor.Top));
            return;
        }
    }

    private IEnumerable<(XmppMessage? Message, (string Key, string Label, XmppMessage Representative)? Collapse)>
        BuildFilteredEntries(IReadOnlyList<XmppMessage> messages)
    {
        var filters = _client.MessageFilters.Where(filter => filter.Enabled).ToArray();
        if (filters.Length == 0)
        {
            foreach (var message in messages)
                yield return (message, null);
            yield break;
        }

        var index = 0;
        while (index < messages.Count)
        {
            var message = messages[index];
            if (XmppMessageFilters.Matching(filters, message, XmppMessageFilterAction.Hide) is not null)
            {
                index++;
                continue;
            }

            var collapse = XmppMessageFilters.Matching(filters, message, XmppMessageFilterAction.Collapse);
            if (collapse is null || message.Outgoing)
            {
                yield return (message, null);
                index++;
                continue;
            }

            var start = index;
            index++;
            while (index < messages.Count &&
                   XmppMessageFilters.Matching(filters, messages[index], XmppMessageFilterAction.Collapse) is { } next &&
                   next.Id == collapse.Id &&
                   XmppMessageFilters.SameCollapseSender(messages[start], messages[index]) &&
                   !messages[index].Outgoing)
                index++;

            var run = messages.Skip(start).Take(index - start).ToArray();
            var key = $"{collapse.Id}:{run[0].LocalId}:{run[^1].LocalId}";
            if (_expandedFilterKeys.Contains(key))
            {
                foreach (var item in run)
                    yield return (item, null);
            }
            else
            {
                var label = run.Length == 1
                    ? $"1 message from {DisplaySender(run[0])} — expand"
                    : $"{run.Length} messages from {DisplaySender(run[0])} — expand";
                yield return (null, (key, label, run[0]));
            }
        }
    }

    private void ExpandCollapsedFilter(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || string.IsNullOrWhiteSpace(key)) return;
        _expandedFilterKeys.Add(key);
        RenderTranscript(_active, CaptureAnchor(), _active.Follow);
    }

    private static string DisplaySender(XmppMessage message) => XmppAlertFeedFormatting.DisplaySender(message);

    private void GoToFeedSource(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not XmppMessage message) return;
        FlashButton(button, "Opened");
        Navigate(message.Conversation, false, message.LocalId);
    }

    private async void CopyFeedBody(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not XmppMessage message) return;
        try
        {
            if (GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(message.Body);
                FlashButton(button, "Copied");
            }
        }
        catch
        {
            SetFeedback("Could not copy the message.");
        }
    }

    private string? ResolveSenderJid(XmppMessage message, string displaySender)
    {
        if (message.Outgoing) return null;
        if (message.Conversation.Kind == XmppConversationKind.Direct)
            return message.Conversation.Jid;
        var bare = XmppAddresses.Bare(message.Sender);
        if (bare is not null && !string.Equals(bare, message.Conversation.Jid, StringComparison.OrdinalIgnoreCase))
            return bare;
        return _client.Occupants(message.Conversation.Jid)
            .FirstOrDefault(occupant => occupant.Nick.Equals(displaySender, StringComparison.OrdinalIgnoreCase))
            ?.Jid is { } jid
            ? XmppAddresses.Bare(jid)
            : null;
    }

    private IEnumerable<(XmppMessage Message, double Top, double Bottom)> RealizedRows(bool split = false,
        bool readableOnly = false)
    {
        if (!split && FeedList.IsVisible)
        {
            foreach (var row in RealizedFeedRows(readableOnly))
                yield return row;
            yield break;
        }
        var tab = split ? _renderedSplitTab : _renderedTab;
        if (tab is null) yield break;
        var text = split ? SplitTranscriptText : TranscriptText;
        var offset = (split ? SplitTranscriptScroll : TranscriptScroll).Offset.Y;
        var messages = readableOnly ? tab.Messages : tab.Messages.Concat(tab.TopicNotices);
        var byId = messages.ToDictionary(message => message.LocalId);
        var collapsed = readableOnly
            ? text.Document?.Where(run => run.Action?.StartsWith("collapse:", StringComparison.Ordinal) == true)
                .Select(run => run.MessageId).ToHashSet()
            : null;
        foreach (var bounds in text.MessageBounds())
        {
            if (!byId.TryGetValue(bounds.Id, out var message) || collapsed?.Contains(bounds.Id) == true) continue;
            yield return (message, bounds.Top - offset, bounds.Bottom - offset);
        }
    }

    private ScrollAnchor? CaptureAnchor(bool split = false) => RealizedRows(split).Where(row => row.Bottom > 0 &&
        row.Top < (split ? SplitTranscriptScroll : TranscriptScroll).Viewport.Height).OrderBy(row => row.Top)
        .Select(row => new ScrollAnchor(row.Message.LocalId, row.Top)).FirstOrDefault();

    private bool NearBottom() => NearBottom(TranscriptScroll);

    private static bool NearBottom(ScrollViewer scroll) =>
        scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y <= 36;

    private void TranscriptScrolled(object? sender, ScrollChangedEventArgs args)
    {
        if (_restoringScroll) return;
        _active.Follow = NearBottom();
        if (_active.Follow) NewMessagesButton.IsVisible = _active.NewMessages = false;
        VisibleRowsChanged();
    }

    private void ObserveVisibleRows()
    {
        if (_client.Account?.Jid is not { } account || account != _workspaceAccount) return;
        var split = ReadingSplit;
        var tab = split ? _splitTab! : _active;
        if (split ? _restoringSplitScroll || _renderedSplitTab != tab : _restoringScroll || _renderedTab != tab)
            return;
        var unread = tab.Messages.Where(message => message.Unread).ToArray();
        if (unread.Length == 0) return;
        _visibleChanged = false;
        var ids = unread.Select(message => message.LocalId).ToArray();
        _readFocus.Observe(account, feed: tab.Conversation is null, unread);
        if (split) _lastSplitVisible = ids;
        else _lastVisible = ids;
    }

    private async void SendMessage(object? sender, RoutedEventArgs args) => await SendAsync();

    private Task SendAsync() => SendComposerAsync(Compose, SendButton, _active, split: false);

    private async Task SendComposerAsync(TextBox composer, Button button, ConversationTab? tab, bool split)
    {
        if (_sending || !button.IsEnabled || tab?.Conversation is not { } conversation ||
            string.IsNullOrWhiteSpace(composer.Text)) return;
        var body = composer.Text;
        _sending = true;
        button.IsEnabled = false;
        SetFeedback(null);
        try
        {
            var result = await _client.SubmitAsync(conversation, body);
            var sameTab = (split ? _splitTab : _active) == tab;
            if (sameTab && composer.Text == body) composer.Text = "";
            if (!split && tab.Draft == body) tab.Draft = "";
            SetFeedback(result.Feedback);
            if (sameTab && result.OpenConversation is { } destination)
            {
                FocusPane(split);
                Navigate(destination, fleetPing: false);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { SetFeedback(ex.Message); }
        catch (Exception) { SetFeedback("The message could not be sent."); }
        finally { _sending = false; Invalidate(); }
    }

    private static bool IsActionMessage(XmppMessage message) =>
        message.Body.Length > 4 && message.Body.StartsWith("/me ", StringComparison.Ordinal) &&
        string.IsNullOrEmpty(message.Html);

    private async void LoadEarlier(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        var tab = _active;
        if (tab.Messages.FirstOrDefault() is not { } first) return;
        var earlier = await _client.ReadAsync(tab.Conversation, tab.Conversation is null, first);
        if (tab != _active || tab.Messages.FirstOrDefault()?.LocalId != first.LocalId) return;
        tab.HasEarlier = earlier.Count == PageSize;
        if (earlier.Count == 0) { OlderButton.IsVisible = false; return; }
        var scrollAnchor = CaptureAnchor();
        tab.Messages = earlier.Concat(tab.Messages).ToArray();
        tab.HistoryExpanded = true;
        RenderTranscript(tab, scrollAnchor, false);
    });

    private void JumpToLatest(object? sender, RoutedEventArgs args)
    {
        _active.Follow = true;
        _active.NewMessages = false;
        NewMessagesButton.IsVisible = false;
        if (_active.Messages.Count > 0) TranscriptScroll.ScrollToEnd();
        Invalidate();
    }

    internal async Task ClearLocalHistoryAsync(bool confirm = true)
    {
        if (confirm && !await XmppDialog.ConfirmAsync(this, "Clear local history",
                "Remove saved messages and unread counts from this device?"))
            return;
        _readFocus.ClearPending();
        await _client.ClearHistoryAsync();
        _readFocus.ClearPending();
        foreach (var tab in _tabs.Concat(_closedTabs.Values))
            tab.ResetHistory();
        _lastVisible = [];
        _expandedFilterKeys.Clear();
        RenderTranscript(_active, null, true);
        if (SplitPane.IsVisible && _splitTab is { } split) RenderSplit(split);
    }

    private void TranscriptAction(object? sender, RoutedEventArgs e)
    {
        if (sender is not XmppLinkText { ActivatedAction: { } action }) return;
        if (action.StartsWith("collapse:", StringComparison.Ordinal))
        {
            _expandedFilterKeys.Add(action["collapse:".Length..]);
            var split = ReferenceEquals(sender, SplitTranscriptText);
            if (split && _splitTab is { } splitTab) RenderSplit(splitTab);
            else RenderTranscript(_active, CaptureAnchor(), _active.Follow);
            return;
        }
        if (!long.TryParse(action.Split(':').ElementAtOrDefault(1), out var id)) return;
        var message = FindLoadedMessage(id);
        if (message is null) return;
        if (action.StartsWith("feed-goto:", StringComparison.Ordinal))
            Navigate(message.Conversation, false, message.LocalId);
        else if (action.StartsWith("feed-copy:", StringComparison.Ordinal))
            _ = CopyTextAsync(message.Body);
    }

    private XmppMessage? FindLoadedMessage(long id)
    {
        foreach (var tab in _tabs)
        {
            var found = tab.Messages.FirstOrDefault(message => message.LocalId == id)
                ?? tab.TopicNotices.FirstOrDefault(message => message.LocalId == id);
            if (found is not null) return found;
        }
        return null;
    }

    private async Task CopyTextAsync(string text)
    {
        try
        {
            if (GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text);
        }
        catch
        {
            SetFeedback("Could not copy the message.");
        }
    }

    private IReadOnlyList<XmppTranscriptRun> BuildDocument(IReadOnlyList<XmppMessageRow> rows) =>
        BuildTranscriptRuns(rows, TranscriptText.FontFamily, _client.DisplayPreferences.MessageBlockPadding);

    private static IReadOnlyList<XmppTranscriptRun> BuildTranscriptRuns(
        IReadOnlyList<XmppMessageRow> rows, FontFamily fontFamily, double? padding)
    {
        var runs = new List<XmppTranscriptRun>();
        var timestamp = Brush.Parse("#9BABC2");
        var pad = XmppDisplayPreferences.NormalizeMessageBlockPadding(padding);
        var spacer = pad > 0 ? SpacerFontSize(fontFamily, pad) : 0;
        foreach (var row in rows)
        {
            if (runs.Count > 0 && !runs[^1].Text.EndsWith('\n'))
                runs.Add(new() { Text = "\n" });
            if (row.HasDate)
                runs.Add(new()
                {
                    Text = row.DateLabel + "\n",
                    Foreground = Brushes.Transparent,
                    DateRule = true,
                    DateLabel = row.DateLabel
                });
            if (row.IsCollapseSummary)
            {
                runs.Add(new()
                {
                    Text = row.CollapseLabel + "\n",
                    Foreground = Active,
                    Underline = true,
                    Action = "collapse:" + row.CollapseKey,
                    MessageId = row.Message.LocalId
                });
                continue;
            }

            var highlight = ReferenceEquals(row.RowBackground, Brushes.Transparent) ? null : row.RowBackground;
            var bar = row.HasAccentBar ? row.AccentBar : null;
            var divider = row.DividerThickness.Top > 0;
            // Use the same standalone line on both sides. The content's line break
            // is separate so its metrics cannot become part of either padding line.
            var blockStart = runs.Count;
            var paddingLine = pad > 0 ? new XmppTranscriptRun
            {
                Text = "\u00a0\n", FontSize = spacer, Foreground = Brushes.Transparent,
                Highlight = highlight, Bar = bar, Divider = divider, MessageId = row.Message.LocalId
            } : null;
            if (paddingLine is not null)
                runs.Add(paddingLine);
            var contentStart = runs.Count;
            if (row.ShowFeedChrome)
            {
                runs.Add(new() { Text = row.FeedTitle, Foreground = Primary, Bold = true, Highlight = highlight, Bar = bar, Divider = divider, MessageId = row.Message.LocalId });
                if (row.HasFeedSubtitle)
                    runs.Add(new() { Text = "\n" + row.FeedSubtitle, Foreground = Secondary, Highlight = highlight, Bar = bar, Divider = divider, MessageId = row.Message.LocalId });
                runs.Add(new()
                {
                    Text = "  Go to source",
                    Foreground = Active,
                    Underline = true,
                    Action = "feed-goto:" + row.Message.LocalId,
                    Highlight = highlight,
                    Bar = bar,
                    Divider = divider,
                    MessageId = row.Message.LocalId
                });
                runs.Add(new()
                {
                    Text = "  Copy",
                    Foreground = Active,
                    Underline = true,
                    Action = "feed-copy:" + row.Message.LocalId,
                    Highlight = highlight,
                    Bar = bar,
                    Divider = divider,
                    MessageId = row.Message.LocalId
                });
                runs.Add(new() { Text = "\n", Highlight = highlight, Bar = bar, Divider = divider, MessageId = row.Message.LocalId });
            }
            if (!string.IsNullOrEmpty(row.Time))
                runs.Add(new() { Text = $"[{row.Time}] ", Foreground = timestamp, Highlight = highlight, Bar = bar, Divider = divider, MessageId = row.Message.LocalId });
            var action = !row.IsFeedRow && IsActionMessage(row.Message);
            if (!string.IsNullOrEmpty(row.SenderLabel))
                runs.Add(new()
                {
                    Text = action ? $"* {row.SenderLabel} " : $"{row.SenderLabel}: ",
                    Foreground = row.SenderBrush,
                    Bold = true,
                    SenderJid = row.CanOpenSender ? row.SenderJid : null,
                    Highlight = highlight,
                    Bar = bar,
                    Divider = divider,
                    MessageId = row.Message.LocalId
                });
            AppendBody(runs, action ? row.Body[4..] : row.Body, row.Html, highlight, bar, divider, row.Message.LocalId);
            if (runs.Count == contentStart || !runs[^1].Text.EndsWith('\n'))
                runs.Add(new() { Text = "\n", MessageId = row.Message.LocalId, FontSize = XmppLinkText.TranscriptBaseFontSize,
                    Highlight = highlight, Bar = bar, Divider = divider });
            if (paddingLine is not null)
                runs.Add(paddingLine);
            if (row.IsUnreadStart)
                runs[blockStart] = runs[blockStart] with { UnreadDivider = true };
        }
        return runs;
    }

    private static double SpacerFontSize(FontFamily fontFamily, double pixels)
    {
        var typeface = new Typeface(fontFamily);
        if (FontManager.Current is not { } fonts || !fonts.TryGetGlyphTypeface(typeface, out var face)) return pixels;
        var metrics = face.Metrics;
        var units = (metrics.Descent - metrics.Ascent + metrics.LineGap) / (double)metrics.DesignEmHeight;
        return units <= 0.01 ? pixels : pixels / units;
    }

    private static void AppendBody(List<XmppTranscriptRun> runs, string body, string? html, IBrush? highlight,
        IBrush? bar, bool divider, long id)
    {
        var formatted = XmppHtmlFormatting.Parse(html);
        if (formatted is { Count: > 0 })
        {
            foreach (var run in formatted)
            {
                if (run.Link is not null && Uri.TryCreate(run.Link, UriKind.Absolute, out var url))
                {
                    runs.Add(new()
                    {
                        Text = run.Text, Link = url, Underline = true, Bold = run.Bold, Highlight = highlight,
                        Bar = bar, Divider = divider, MessageId = id, FontSize = run.FontSize
                    });
                    continue;
                }
                foreach (var span in XmppLinkSpans.Split(run.Text))
                    runs.Add(new()
                    {
                        Text = span.Text,
                        Link = span.Url,
                        Underline = run.Underline || span.Url is not null,
                        Bold = run.Bold,
                        Highlight = highlight,
                        Bar = bar,
                        Divider = divider,
                        MessageId = id,
                        FontSize = run.FontSize
                    });
            }
            return;
        }

        foreach (var span in XmppLinkSpans.Split(body))
            runs.Add(new()
            {
                Text = span.Text,
                Link = span.Url,
                Underline = span.Url is not null,
                Highlight = highlight,
                Bar = bar,
                Divider = divider,
                MessageId = id
            });
    }
}

public sealed record XmppMessageRow(XmppMessage Message, string DateLabel, bool IsUnreadStart, bool ShowSender,
    string Sender, string Time, string SenderLabel, IBrush Accent, IBrush AccentBar, IBrush SenderBrush,
    bool SpeakerStart, IBrush RowBackground, Thickness DividerThickness, FontFamily FontFamily, double FontSize,
    string? SenderJid = null, bool IsCollapseSummary = false, string? CollapseKey = null, string CollapseLabel = "",
    bool IsFeedRow = false, string FeedTitle = "", string FeedSubtitle = "", string? Html = null)
{
    public string? HtmlBody => Html;
    public bool HasDate => DateLabel.Length > 0;
    public string Body => Message.Body;
    public bool HasAccentBar => !IsCollapseSummary && !ReferenceEquals(AccentBar, Brushes.Transparent) &&
        !ReferenceEquals(Accent, Brushes.Transparent);
    public Thickness BlockMargin => SpeakerStart ? new(0, 2, 0, 0) : default;
    public bool CanOpenSender => !IsCollapseSummary && !string.IsNullOrWhiteSpace(SenderJid) &&
        !string.IsNullOrEmpty(SenderLabel);
    public bool ShowMessage => !IsCollapseSummary;
    public bool ShowFeedChrome => IsFeedRow && !IsCollapseSummary;
    public bool HasFeedSubtitle => !string.IsNullOrWhiteSpace(FeedSubtitle);

    public static XmppMessageRow Collapse(string key, string label, XmppMessage representative,
        FontFamily fontFamily, double fontSize) =>
        new(representative, "", false, false, "", "", "", Brushes.Transparent, Brushes.Transparent,
            Brushes.Transparent, true, Brushes.Transparent, new Thickness(0), fontFamily, fontSize,
            IsCollapseSummary: true, CollapseKey: key, CollapseLabel: label);
}
