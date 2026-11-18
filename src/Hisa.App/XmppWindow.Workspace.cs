using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hisa.Core.Models;
using Hisa.Services.Xmpp;

namespace Hisa.App;

public partial class XmppWindow
{
    private const int PageSize = 250;
    private const string RoomsGroupKey = "rooms";
    private const string ContactsGroupKey = "contacts";
    private static readonly IBrush Primary = Brush.Parse("#E8EEF7");
    private static readonly IBrush Secondary = Brush.Parse("#9AA8BC");
    private static readonly IBrush Active = Brush.Parse("#5EB8E8");
    private static readonly IBrush SelectedTab = Brush.Parse("#253F58");
    private static readonly IBrush InactiveTab = Brush.Parse("#151E2B");
    private static readonly IBrush Mention = Brush.Parse("#1A3550");
    private static readonly IBrush FleetPing = Brush.Parse("#3A3228");
    private static readonly IBrush AlertHighlight = Brush.Parse("#6B5430");
    private static readonly IBrush MentionBar = Brush.Parse("#5EB8E8");
    private static readonly IBrush FleetBar = Brush.Parse("#E0A05A");
    private static readonly IBrush AlertHighlightBar = Brush.Parse("#E0C06A");
    private static readonly IBrush OnlineDot = Brush.Parse("#3DCF8E");
    private static readonly IBrush AwayDot = Brush.Parse("#E0B35A");
    private static readonly IBrush DndDot = Brush.Parse("#E07070");
    private static readonly IBrush RequestDot = Brush.Parse("#E0B35A");
    private static readonly IBrush OfflineDot = Brush.Parse("#6B7789");
    private static readonly IBrush JoinedDot = Brush.Parse("#5EB8E8");
    private static readonly IBrush JoiningDot = Brush.Parse("#E0B35A");
    private static readonly IBrush NotJoinedDot = Brush.Parse("#6B7789");
    private static readonly IBrush HeadingColor = Brush.Parse("#A9B9CE");
    private static readonly IBrush UnreadFg = Brush.Parse("#F5D0D0");
    private static readonly IBrush UnreadBg = Brush.Parse("#8B2E2E");
    private static readonly IBrush UnreadCaption = Brush.Parse("#E88A8A");
    private static readonly IBrush TabCaption = Brush.Parse("#E8EEF7");
    private static readonly IBrush AltRow = Brush.Parse("#1A2A3C");
    private static readonly IBrush OwnerIcon = Brush.Parse("#E0B35A");
    private static readonly IBrush AdminIcon = Brush.Parse("#5B9BD5");
    private static readonly IBrush ParticipantIcon = Brush.Parse("#7A8FA8");
    private static readonly IBrush[] SenderPalette =
    [
        Brush.Parse("#7EC8E8"), Brush.Parse("#8FDBB0"), Brush.Parse("#E0B35A"),
        Brush.Parse("#D4A0E8"), Brush.Parse("#F0A0A0"), Brush.Parse("#A0C4E8"),
        Brush.Parse("#C5D67A"), Brush.Parse("#E8A87C")
    ];
    private static readonly StreamGeometry ChevronRight = StreamGeometry.Parse("M9,6 L15,12 L9,18");
    private static readonly StreamGeometry ChevronDown = StreamGeometry.Parse("M6,9 L12,15 L18,9");
    // Filled glyphs sized for a 24×24 viewbox so they stay crisp at ~14px.
    private static readonly StreamGeometry IconFlag = StreamGeometry.Parse(
        "M4,3 H6 V21 H4 Z M6,3 H20 L16.5,8 L20,13 H6 Z");
    private static readonly StreamGeometry IconStar = StreamGeometry.Parse(
        "M12,2.2 L14.9,9.1 L22.4,9.5 L16.7,14.2 L18.5,21.5 L12,17.6 L5.5,21.5 L7.3,14.2 L1.6,9.5 L9.1,9.1 Z");
    private static readonly StreamGeometry IconParticipant = StreamGeometry.Parse(
        "M12,11.5 A3.6,3.6 0 1 0 12,11.49 Z M6.2,20.5 C6.2,16.9 8.9,14.8 12,14.8 C15.1,14.8 17.8,16.9 17.8,20.5 Z");
    private const double DefaultSidebarWidth = 200;
    private const double DefaultOccupantWidth = 168;
    private const double TabCaptionMaxWidth = 140;
    private const double TabCaptionMinWidth = 48;
    private const double TabStripSpacing = 2;
    private const double TabOverflowButtonWidth = 24;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly DispatcherTimer _cardTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _cardCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _tabLayoutTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly XmppClientService _client = null!;
    private readonly List<ConversationTab> _tabs = [];
    private readonly Dictionary<XmppConversation, ConversationTab> _closedTabs = [];
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedTopics = new(StringComparer.Ordinal);
    private ConversationTab _active = new(null);
    private ConversationTab? _splitTab;
    private bool _splitFocus;
    private readonly List<XmppConversation?> _splitMemory = [];
    private long _topicNoticeId = -1;
    private DateTime _topicAgeRendered;
    private DateTime _feedAgeRendered;
    private ConversationTab? _dragTab;
    private Point? _dragStart;
    private bool _dragging;
    private bool _suppressTabClick;
    private IPointer? _dragPointer;
    private Border? _tabDragGhost;
    private Border? _tabInsertMarker;
    private bool _dragTargetSplit;
    private int _dragInsertIndex = -1;
    private XmppConversation? _selected => _active.Conversation;
    private (XmppConversation Conversation, bool Fleet, long? ScrollToLocalId)? _pendingNavigation;
    private long? _pendingScrollToLocalId;
    private IReadOnlyDictionary<XmppConversation, int> _counts = new Dictionary<XmppConversation, int>();
    private int _feedUnread;
    private string? _workspaceAccount;
    private bool _ready, _closed, _busy, _refreshing, _saving, _changingSource, _restoringScroll, _sending;
    private bool? _sidebarOverride;
    private bool _occupantsOverride;
    private bool _windowOpen = true;
    private double _sidebarWidth = DefaultSidebarWidth;
    private double _occupantWidth = DefaultOccupantWidth;
    private DateTime _saveAfter;
    private int _dirty = 1;
    private bool _visibleChanged = true;
    private readonly XmppReadFocusDelay _readFocus = new();
    private int _scrollGeneration;
    private long[] _lastVisible = [];
    private long[] _lastSplitVisible = [];
    private ConversationTab? _renderedTab;
    private ConversationTab? _renderedSplitTab;
    private bool _restoringSplitScroll;
    private int _splitScrollGeneration;
    private readonly XmppFeedPortraitLoader? _portraitLoader;
    private (ConversationTab Tab, ScrollAnchor? Anchor, bool Follow)? _pendingTranscriptRender;
    private ConversationTab? _pendingSplitRender;
    private bool _transcriptFlushQueued;
    private PixelPoint? _normalPosition;
    private XmppSidebarRow? _cardRow;
    private Control? _cardAnchor;
    private int _cardGeneration;
    private XmppConversation? _roomMenuTarget;
    private bool _cardPointerInside;
    private string[] _nickMatches = [];
    private int _nickMatchIndex = -1;
    private string _nickPrefix = "";
    private int _nickTokenStart = -1;
    private int _nickReplaceEnd = -1;
    private bool _nickCompleting;
    private string _occupantJidSignature = "";
    private int _headerProfileGeneration;
    private sealed record ResizeGesture(WindowEdge Edge, Point PointerStart, PixelPoint WindowStart,
        Size WindowSize, double Scaling, Control Handle, IPointer Pointer);
    private ResizeGesture? _resizeGesture;
    private readonly HashSet<string> _expandedFilterKeys = new(StringComparer.OrdinalIgnoreCase);

    public XmppWindow() => InitializeComponent();

    internal XmppWindow(XmppClientService client, XmppFeedPortraitLoader? portraitLoader = null) : this()
    {
        _client = client;
        _portraitLoader = portraitLoader;
        _refreshTimer.Tick += RefreshTick;
        _cardTimer.Tick += ContactCardTick;
        _cardCloseTimer.Tick += ContactCardCloseTick;
        _tabLayoutTimer.Tick += (_, _) =>
        {
            _tabLayoutTimer.Stop();
            LayoutAllTabStrips();
        };
        _client.StateChanged += Invalidate;
        _client.MessageReceived += OnMessage;
        AddHandler(PointerPressedEvent, XmppLinkText.ClearSelectionOnOutsideClick, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnResizePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnResizePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnTabDragPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnTabDragPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += async (_, _) =>
        {
            if (_ready) return;
            await RunAsync(async () =>
            {
                await _client.ReadyAsync();
                ApplyDisplayFonts();
                await RestoreWorkspaceAsync();
                _ready = true;
                LayoutAllTabStrips();
                if (_pendingNavigation is { } navigation)
                {
                    _pendingNavigation = null;
                    Navigate(navigation.Conversation, navigation.Fleet, navigation.ScrollToLocalId);
                }
                _refreshTimer.Start();
                if (_client.Account is null) await ShowAccountAsync();
            });
        };
        Activated += (_, _) => UpdateReadFocus();
        Deactivated += (_, _) => _readFocus.Update(null);
        SizeChanged += (_, _) =>
        {
            ApplySidebar();
            QueueTabLayout();
            if (WindowState == WindowState.Normal) QueueWorkspaceSave();
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty)
            {
                UpdateFeedPortraits();
                UpdateReadFocus();
            }
            if (args.Property == WindowStateProperty)
            {
                ApplyChromeState();
                UpdateReadFocus();
            }
        };
        ApplyChromeState();
        SetFeedback(null);
        PositionChanged += (_, args) =>
        {
            if (WindowState != WindowState.Normal) return;
            _normalPosition = args.Point;
            QueueWorkspaceSave();
        };
        Closing += (_, args) =>
        {
            if (args.IsProgrammatic || args.CloseReason != WindowCloseReason.WindowClosing) return;
            args.Cancel = true;
            _windowOpen = false;
            _feedOverlay?.Close();
            Hide();
            _ = SaveWorkspaceAsync();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _feedOverlay?.Close();
            UpdateFeedPortraits();
            _refreshTimer.Stop();
            _cardTimer.Stop();
            _cardCloseTimer.Stop();
            _tabLayoutTimer.Stop();
            CancelTabDrag();
            _client.StateChanged -= Invalidate;
            _client.MessageReceived -= OnMessage;
        };
        Opened += (_, _) =>
        {
            if (_ready) { _windowOpen = true; QueueWorkspaceSave(); }
        };
        AddHandler(KeyDownEvent, WorkspaceKeyDown, RoutingStrategies.Tunnel);
        Compose.AddHandler(KeyDownEvent, ComposeKeyDown, RoutingStrategies.Tunnel);
        SplitCompose.AddHandler(KeyDownEvent, ComposeKeyDown, RoutingStrategies.Tunnel);
        TranscriptScroll.SizeChanged += (_, _) =>
        {
            FitTranscript(TranscriptScroll, TranscriptText);
            FitTranscript(TranscriptScroll, FeedList);
        };
        SplitTranscriptScroll.SizeChanged += (_, _) => FitTranscript(SplitTranscriptScroll, SplitTranscriptText);
        OccupantsToggle.IsChecked = _client.DisplayPreferences.ShowOccupantSidebar;
        TranscriptScroll.ScrollChanged += TranscriptScrolled;
        SplitTranscriptScroll.ScrollChanged += (_, _) => VisibleRowsChanged();
        TranscriptScroll.GotFocus += (_, _) => FocusPane(split: false);
        SplitPane.GotFocus += (_, _) => FocusPane(split: true);
        Compose.GotFocus += (_, _) => FocusPane(split: false);
        ObserveTranscriptSelection(TranscriptText);
        ObserveTranscriptSelection(SplitTranscriptText);
        TabStripHost.SizeChanged += (_, _) => QueueTabLayout();
        SplitPane.SizeChanged += (_, _) => QueueTabLayout();
        Sidebar.SizeChanged += (_, _) =>
        {
            if (Sidebar.IsVisible && Sidebar.Bounds.Width > 0)
            {
                _sidebarWidth = Math.Clamp(Sidebar.Bounds.Width, DefaultSidebarWidth * 0.5, 360);
                QueueWorkspaceSave();
            }
        };
        OccupantSidebar.SizeChanged += (_, _) =>
        {
            if (!OccupantSidebar.IsVisible) return;
            PersistSplitterWidths();
        };
    }

    public void Navigate(XmppConversation conversation, bool fleetPing, long? scrollToLocalId = null)
    {
        if (!_ready) { _pendingNavigation = (conversation, fleetPing, scrollToLocalId); return; }
        _pendingScrollToLocalId = scrollToLocalId;
        Select(fleetPing ? null : conversation);
    }

    private void Invalidate() => Interlocked.Exchange(ref _dirty, 1);

    private void OnMessage(XmppMessage message)
    {
        Invalidate();
        if (!_ready || _closed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!message.Outgoing && !message.Recovered && !message.IsFleetPing &&
                message.Conversation.Kind == XmppConversationKind.Direct)
                EnsureTab(message.Conversation);
            _ = RefreshNowAsync();
        }, DispatcherPriority.Normal);
    }

    private async Task RefreshNowAsync()
    {
        if (!_ready || _closed || _refreshing || !IsVisible) return;
        Interlocked.Exchange(ref _dirty, 0);
        _refreshing = true;
        try { await RefreshAsync(); }
        catch (Exception) { SetFeedback("The chat workspace could not refresh."); }
        finally { _refreshing = false; }
    }

    private async void RefreshTick(object? sender, EventArgs args)
    {
        if (!_ready || _closed || _refreshing) return;
        if (_saveAfter != default && DateTime.UtcNow >= _saveAfter) await SaveWorkspaceAsync();
        if (_closed || _refreshing) return;
        var refresh = IsVisible && Interlocked.Exchange(ref _dirty, 0) != 0;
        var ageDue = IsVisible && (TopicPanel.IsVisible || SplitTopicPanel.IsVisible) &&
            DateTime.UtcNow - _topicAgeRendered > TimeSpan.FromSeconds(30);
        var feedAgeDue = IsVisible && FeedList.IsVisible &&
            DateTime.UtcNow - _feedAgeRendered > TimeSpan.FromSeconds(10);
        UpdateReadFocus();
        if (!refresh && !_readFocus.HasDueRows && !ageDue && !feedAgeDue) return;
        _refreshing = true;
        try
        {
            if (refresh) await RefreshAsync();
            if (IsVisible && (TopicPanel.IsVisible || SplitTopicPanel.IsVisible) &&
                DateTime.UtcNow - _topicAgeRendered > TimeSpan.FromSeconds(30))
            {
                _topicAgeRendered = DateTime.UtcNow;
                RefreshTopicAge();
            }
            if (IsVisible && FeedList.IsVisible &&
                DateTime.UtcNow - _feedAgeRendered > TimeSpan.FromSeconds(10))
            {
                _feedAgeRendered = DateTime.UtcNow;
                XmppFeedCards.RefreshAges(_feedCards);
            }
            UpdateReadFocus();
            if (!_closed && _readFocus.HasDueRows) await _readFocus.MarkDueRowsAsync(_client);
        }
        catch (Exception) { SetFeedback("The chat workspace could not refresh."); }
        finally { _refreshing = false; }
    }

    private async Task RestoreWorkspaceAsync()
    {
        _saveAfter = default;
        _workspaceAccount = _client.Account?.Jid;
        var saved = _workspaceAccount is { } account ? await _client.LoadWorkspaceAsync(account) : null;
        TabStrip.Children.Clear();
        SplitTabStrip.Children.Clear();
        _tabs.Clear();
        _closedTabs.Clear();
        _collapsedGroups.Clear();
        _collapsedTopics.Clear();
        _tabs.Add(new(null));
        if (saved is not null)
        {
            _tabs.AddRange(saved.Tabs.Distinct().Select(conversation => new ConversationTab(conversation)));
            Width = Math.Clamp(saved.Width, MinWidth, 4000);
            Height = Math.Clamp(saved.Height, MinHeight, 3000);
            if (saved is { X: { } x, Y: { } y } &&
                Screens.All.Any(screen => screen.WorkingArea.Contains(new PixelPoint(x + 30, y + 15))))
                Position = new(x, y);
            PinButton.IsChecked = Topmost = saved.Pinned;
            if (saved.CollapsedGroups is { Count: > 0 } collapsed)
                foreach (var key in collapsed) _collapsedGroups.Add(key);
            if (saved.CollapsedTopics is { Count: > 0 } topics)
                foreach (var key in topics) _collapsedTopics.Add(key);
            _sidebarWidth = Math.Clamp(saved.SidebarWidth, DefaultSidebarWidth * 0.5, 360);
            _occupantWidth = Math.Clamp(saved.OccupantWidth, DefaultOccupantWidth * 0.5, 280);
            _windowOpen = saved.Open;
        }
        else
        {
            PinButton.IsChecked = Topmost = false;
            _windowOpen = true;
        }
        OccupantsToggle.IsChecked = _client.DisplayPreferences.ShowOccupantSidebar;
        _active = saved is { FleetSelected: false } ? _tabs.Find(tab => tab.Conversation == saved.Selected) ?? _tabs[0] : _tabs[0];
        _counts = new Dictionary<XmppConversation, int>();
        _feedUnread = 0;
        _renderedTab = null;
        _lastVisible = [];
        Compose.Text = "";
        ApplySidebar();
        ApplyOccupantWidth();
        RenderTabs();
        UpdateConversation();
        RenderTranscript(_active, null, true);
        Invalidate();
    }

    private void QueueWorkspaceSave()
    {
        if (_ready && _workspaceAccount is not null)
            _saveAfter = DateTime.UtcNow.AddMilliseconds(350);
    }

    private async Task SaveWorkspaceAsync()
    {
        if (_workspaceAccount is not { } account || _saving) return;
        _saving = true;
        _saveAfter = default;
        try
        {
            PersistSplitterWidths(save: false);
            await _client.SaveWorkspaceAsync(account, new(_tabs.Skip(1).Select(tab => tab.Conversation!).ToArray(),
                _selected, _selected is null, Width, Height, (_normalPosition ?? Position).X,
                (_normalPosition ?? Position).Y, Topmost,
                _collapsedGroups.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                _collapsedTopics.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                _windowOpen && IsVisible, _sidebarWidth, _occupantWidth));
        }
        catch (Exception) { SetFeedback("The chat workspace settings could not be saved."); }
        finally { _saving = false; }
    }

    private void PersistSplitterWidths(bool save = true)
    {
        if (WorkspaceGrid.ColumnDefinitions.Count >= 5)
        {
            var left = WorkspaceGrid.ColumnDefinitions[0].Width;
            if (left.IsAbsolute && left.Value > 0)
                _sidebarWidth = Math.Clamp(left.Value, DefaultSidebarWidth * 0.5, 360);
            var right = WorkspaceGrid.ColumnDefinitions[4].Width;
            if (right.IsAbsolute && right.Value > 0)
                _occupantWidth = Math.Clamp(right.Value, DefaultOccupantWidth * 0.5, 280);
        }
        if (save) QueueWorkspaceSave();
    }

    private async Task RefreshAsync()
    {
        var account = _client.Account;
        ToolTip.SetTip(OptionsButton, string.IsNullOrEmpty(_client.Status) ? "Options" : _client.Status);
        ConnectButton.IsVisible = !_client.Online;
        NewChatButton.IsEnabled = AddRoomButton.IsEnabled = account is not null;
        AddContactButton.IsEnabled = _client.Online;
        UpdateOwnPresenceControl();
        var tab = _active;
        if (account is null)
        {
            UpdateConversation();
            return;
        }

        var first = tab.Messages.FirstOrDefault();
        var feedClearVersion = _feedClearVersion;
        var counts = await _client.CountsAsync();
        var feedUnread = await _client.FeedUnreadCountAsync();
        var follow = tab.Follow && (_renderedTab != tab || NearBottom());
        var useLatestPage = first is null || (follow && !tab.HistoryExpanded);
        var messages = await _client.ReadAsync(tab.Conversation, tab.Conversation is null, from: useLatestPage ? null : first);
        var hasEarlier = useLatestPage ? messages.Count == PageSize :
            tab.HasEarlier || await _client.HasEarlierAsync(tab.Conversation, tab.Conversation is null, first!);
        if (_closed || tab != _active || feedClearVersion != _feedClearVersion ||
            account.Jid != _workspaceAccount || account.Jid != _client.Account?.Jid)
        {
            Invalidate();
            return;
        }

        var noticed = DrainTopicChanges();
        _counts = counts;
        _feedUnread = feedUnread;
        UpdateSidebar();
        RenderTabs();
        UpdateConversation();
        tab.HasEarlier = hasEarlier;
        if (!SameTranscript(tab.Messages, messages) || noticed || _renderedTab != tab)
        {
            var anchor = _renderedTab == tab ? CaptureAnchor() : tab.Anchor;
            if (!follow && tab.Messages.LastOrDefault() is { } latest &&
                messages.Any(message => message.Timestamp > latest.Timestamp ||
                    (message.Timestamp == latest.Timestamp && message.LocalId > latest.LocalId)))
                tab.NewMessages = true;
            tab.Messages = messages;
            RenderTranscript(tab, anchor, follow);
        }
        OlderButton.IsVisible = tab.HasEarlier;
        NewMessagesButton.IsVisible = tab.NewMessages;
        await RefreshSplitAsync();
    }

    private async Task RefreshSplitAsync()
    {
        var hasSplit = _tabs.Any(tab => tab.InSplit);
        SplitEmptyHint.IsVisible = !hasSplit;
        if (SplitPane.IsVisible != true || _splitTab is not { } split || _client.Account is null)
        {
            _renderedSplitTab = null;
            if (_splitTab is null) UpdateSplitTopic();
            SplitCompose.IsEnabled = false;
            SplitSendButton.IsEnabled = false;
            return;
        }
        var messages = await _client.ReadAsync(split.Conversation, split.Conversation is null);
        if (_splitTab != split) return;
        var changed = _renderedSplitTab != split || !SameTranscript(split.Messages, messages);
        split.Messages = messages;
        if (changed) RenderSplit(split);
        var canSend = split.Conversation is not null && _client.Online;
        SplitSendButton.IsEnabled = canSend && !_sending;
        SplitCompose.IsEnabled = split.Conversation is not null;
        UpdateSplitTopic();
    }

    private void RenderSplit(ConversationTab tab)
    {
        if (_renderedSplitTab == tab && SplitTranscriptText.IsSelecting)
        {
            _pendingSplitRender = tab;
            return;
        }
        _pendingSplitRender = null;
        if (_renderedSplitTab != tab) SplitTranscriptText.EndSelection();
        var follow = _renderedSplitTab != tab || NearBottom(SplitTranscriptScroll);
        var anchor = follow ? null : CaptureAnchor(split: true);
        _renderedSplitTab = tab;
        var rows = BuildRows(tab, tab.Conversation is null);
        SplitTranscriptText.Document = BuildDocument(rows);
        _restoringSplitScroll = true;
        var generation = ++_splitScrollGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed || generation != _splitScrollGeneration) return;
            if (_splitTab == tab)
            {
                if (follow) SplitTranscriptScroll.ScrollToEnd();
                else if (anchor is not null) RestoreAnchor(anchor, split: true);
            }
            _restoringSplitScroll = false;
            VisibleRowsChanged();
        }, DispatcherPriority.Loaded);
    }

    private void ToggleSplit(object? sender, RoutedEventArgs args)
    {
        var show = SplitButton.IsChecked == true;
        if (!show)
        {
            _splitMemory.Clear();
            _splitMemory.AddRange(_tabs.Where(tab => tab.InSplit).Select(tab => tab.Conversation));
            var current = _active;
            foreach (var tab in _tabs.Where(tab => tab.InSplit).ToList())
                tab.InSplit = false;
            _splitTab = null;
            FocusPane(split: false);
            SyncTabStripChildren(false);
            SyncTabStripChildren(true);
            if (_tabs.Contains(current))
                ActivateTab(current);
            else
                RenderTabs();
        }
        else
        {
            foreach (var conversation in _splitMemory)
            {
                var tab = _tabs.Find(item => item.Conversation == conversation);
                if (tab is { InSplit: false })
                    MoveTab(tab, true);
            }
            _splitTab ??= _tabs.FirstOrDefault(tab => tab.InSplit);
        }
        ApplySplitLayout(show);
        if (show) Invalidate();
    }

    private static void FitTranscript(ScrollViewer scroll, Control text)
    {
        var width = scroll.Viewport.Width;
        if (width < 1) width = scroll.Bounds.Width;
        if (width > 1) text.MinWidth = width;
    }

    private void ApplySplitLayout(bool show)
    {
        var opening = show && SplitPane.IsVisible != true;
        SplitPane.IsVisible = SplitSplitter.IsVisible = show;
        TranscriptSplit.RowDefinitions[1].Height = show ? new GridLength(6) : new GridLength(0);
        TranscriptSplit.RowDefinitions[0].MinHeight = show ? 140 : 0;
        TranscriptSplit.RowDefinitions[2].MinHeight = show ? 140 : 0;
        if (opening)
        {
            TranscriptSplit.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            TranscriptSplit.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        }
        else if (!show)
        {
            TranscriptSplit.RowDefinitions[2].Height = new GridLength(0);
        }
        SplitEmptyHint.IsVisible = show && !_tabs.Any(tab => tab.InSplit);
    }

    private void FocusTopPane(object? sender, PointerPressedEventArgs args) => FocusPane(split: false);
    private void FocusBottomPane(object? sender, PointerPressedEventArgs args) => FocusPane(split: true);

    private void FocusPane(bool split)
    {
        _splitFocus = split;
        VisibleRowsChanged();
    }

    private bool ReadingSplit => _splitFocus && SplitPane.IsVisible && _splitTab is not null;

    private void UpdateReadFocus()
    {
        var focused = !_closed && IsVisible && IsActive && WindowState != WindowState.Minimized;
        _visibleChanged |= _readFocus.Update(focused ? (ReadingSplit ? _splitTab : _active) : null);
        if (focused && _visibleChanged) ObserveVisibleRows();
    }

    private void VisibleRowsChanged()
    {
        _visibleChanged = true;
        UpdateReadFocus();
    }

    private async void SendSplitMessage(object? sender, RoutedEventArgs args) => await SendSplitAsync();

    private Task SendSplitAsync() => SendComposerAsync(SplitCompose, SplitSendButton, _splitTab, split: true);

    private bool DrainTopicChanges()
    {
        var affected = false;
        while (_client.TryDequeueTopicChange(out var change))
        {
            var tab = _tabs.Find(item => item.Conversation is { Kind: XmppConversationKind.Room } room &&
                room.Jid.Equals(change.Room, StringComparison.OrdinalIgnoreCase));
            if (tab?.Conversation is not { } conversation) continue;
            var nick = string.IsNullOrWhiteSpace(change.Nick) ? "Someone" : change.Nick;
            var body = string.IsNullOrEmpty(change.Topic)
                ? $"{nick} cleared the channel topic."
                : $"{nick} changed the channel topic: {change.Topic}";
            tab.TopicNotices.Add(new XmppMessage(
                conversation,
                conversation.Jid + "/Channel topic",
                body,
                DateTimeOffset.UtcNow,
                false, true, false, false, [])
            { LocalId = _topicNoticeId-- });
            if (tab == _active || tab == _splitTab) affected = true;
        }
        return affected;
    }

    internal static bool SameTranscript(IReadOnlyList<XmppMessage> old, IReadOnlyList<XmppMessage> current) =>
        old.Count == current.Count && old.Zip(current).All(pair =>
            pair.First.LocalId == pair.Second.LocalId && pair.First.Unread == pair.Second.Unread);

    private void UpdateSidebar()
    {
        if (_client.Account is not { } account) return;
        var filter = FilterBox.Text?.Trim() ?? "";
        var filtering = filter.Length > 0;
        bool Match(string label, string jid) => !filtering ||
            label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            jid.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var ping = XmppAddresses.Directorbot(account.Jid);
        var rows = new List<XmppSidebarRow>();
        if (Match("Notification Feed", ping.Jid) || Match("Fleet Pings", ping.Jid))
            rows.Add(Item(ping, "Notification Feed", "Alerts and fleet pings", Active, fleet: true, OnlineDot));

        var rooms = _client.Rooms.ToList();
        if (_selected is { Kind: XmppConversationKind.Room } activeRoom &&
            rooms.All(room => room.Jid != activeRoom.Jid))
            rooms.Add(new(activeRoom.Jid, "", false));
        var visibleRooms = rooms.Where(room => Match(ShortName(room.Jid), room.Jid)).ToArray();
        XmppSidebarRow RoomItem(XmppRoom room, bool inFolder = false)
        {
            var joined = _client.JoinedRooms.Contains(room.Jid);
            var detail = _client.RoomStatus(room.Jid).Detail;
            var joining = detail == "Joining…";
            var dot = joined ? JoinedDot : joining ? JoiningDot : NotJoinedDot;
            var hint = detail is null ? room.Jid : $"{room.Jid}\n{detail}";
            return Item(new(XmppConversationKind.Room, room.Jid), ShortName(room.Jid), hint,
                joined ? Primary : Secondary, indent: true, dot: dot, showActions: true, deep: inFolder);
        }
        if (visibleRooms.Length > 0 || !filtering)
        {
            var roomsExpanded = filtering || !_collapsedGroups.Contains(RoomsGroupKey);
            rows.Add(GroupHeader(RoomsGroupKey, $"Rooms ({visibleRooms.Length})", "Rooms", roomsExpanded));
            if (roomsExpanded)
            {
                rows.AddRange(visibleRooms.Where(room => string.IsNullOrWhiteSpace(room.Group)).Select(room => RoomItem(room)));
                var folders = visibleRooms
                    .Where(room => !string.IsNullOrWhiteSpace(room.Group))
                    .GroupBy(room => room.Group!, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
                foreach (var folder in folders)
                {
                    var key = "roomg:" + folder.Key;
                    var expanded = filtering || !_collapsedGroups.Contains(key);
                    rows.Add(GroupHeader(key, $"{folder.Key} ({folder.Count()})", folder.Key, expanded, indent: true));
                    if (expanded) rows.AddRange(folder.Select(room => RoomItem(room, inFolder: true)));
                }
            }
        }

        var contacts = _client.Contacts.ToDictionary(contact => contact.Jid);
        var recent = _counts.Keys.Where(source => source.Kind == XmppConversationKind.Direct);
        var allContacts = contacts.Keys.Concat(recent.Select(source => source.Jid)).Distinct()
            .Select(jid => contacts.GetValueOrDefault(jid) ?? new(jid, ShortName(jid), false))
            .Where(contact => Match(contact.Name, contact.Jid))
            .OrderByDescending(contact => contact.Online)
            .ThenBy(contact => contact.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var grouped = new SortedDictionary<string, List<XmppContact>>(StringComparer.OrdinalIgnoreCase);
        var ungrouped = new List<XmppContact>();
        foreach (var contact in allContacts)
        {
            var groups = contact.RosterGroups;
            if (groups.Count == 0) ungrouped.Add(contact);
            else foreach (var group in groups)
            {
                if (!grouped.TryGetValue(group, out var list))
                    grouped[group] = list = [];
                list.Add(contact);
            }
        }

        if ((grouped.Count > 0 || ungrouped.Count > 0 || !filtering) && (visibleRooms.Length > 0 || !filtering))
            rows.Add(XmppSidebarRow.Divider());

        foreach (var (name, members) in grouped)
        {
            var key = "g:" + name;
            var online = members.Count(member => member.Online);
            var expanded = filtering || !_collapsedGroups.Contains(key);
            rows.Add(GroupHeader(key, $"{name} ({online}/{members.Count})", name, expanded));
            if (expanded)
                rows.AddRange(members.Select(ContactRow));
        }

        if (ungrouped.Count > 0 || (!filtering && grouped.Count == 0))
        {
            var contactsExpanded = filtering || !_collapsedGroups.Contains(ContactsGroupKey);
            rows.Add(GroupHeader(ContactsGroupKey,
                $"Contacts ({ungrouped.Count(c => c.Online)}/{ungrouped.Count})", "Contacts", contactsExpanded));
            if (contactsExpanded)
                rows.AddRange(ungrouped.Select(ContactRow));
        }

        if (Sources.ItemsSource is XmppSidebarRow[] previous && previous.SequenceEqual(rows)) return;
        _changingSource = true;
        Sources.ItemsSource = rows.ToArray();
        _changingSource = false;
    }

    private XmppSidebarRow ContactRow(XmppContact contact)
    {
        var dot = contact.SubscriptionRequested ? RequestDot : PresenceBrush(contact.EffectiveShow);
        // Empty Hint avoids a native ToolTip competing with ContactCardPopup.
        return Item(new(XmppConversationKind.Direct, contact.Jid), contact.Name, "",
            contact.Online ? Primary : Secondary, indent: true, dot: dot, showActions: true);
    }

    private static IBrush PresenceBrush(XmppPresenceShow show) => show switch
    {
        XmppPresenceShow.Chat or XmppPresenceShow.Online => OnlineDot,
        XmppPresenceShow.Away or XmppPresenceShow.ExtendedAway => AwayDot,
        XmppPresenceShow.DoNotDisturb => DndDot,
        _ => OfflineDot
    };

    private static string PresenceLabel(XmppPresenceShow show) => show switch
    {
        XmppPresenceShow.Chat => "Free to chat",
        XmppPresenceShow.Online => "Online",
        XmppPresenceShow.Away => "Away",
        XmppPresenceShow.ExtendedAway => "Extended away",
        XmppPresenceShow.DoNotDisturb => "Do not disturb",
        _ => "Offline"
    };

    private XmppSidebarRow Item(XmppConversation source, string label, string hint, IBrush color,
        bool fleet = false, IBrush? dot = null, bool indent = false, bool showActions = false, bool deep = false) =>
        new(source, fleet, label, hint, CountLabel(fleet ? _feedUnread : _counts.GetValueOrDefault(source)), color, FontWeight.Normal,
            XmppSidebarKind.Item, null, true, indent, dot ?? OfflineDot, ShowDot: true,
            Chevron: ChevronRight, ShowActions: showActions, DeepIndent: deep);

    private static XmppSidebarRow GroupHeader(string key, string label, string fullName, bool expanded, bool indent = false) =>
        new(null, false, label, fullName, "", HeadingColor, FontWeight.SemiBold, XmppSidebarKind.Group, key, expanded,
            Indent: indent, ShowChevron: true, Chevron: expanded ? ChevronDown : ChevronRight);

    private static string CountLabel(int unread) => unread switch { 0 => "", > 99 => "99+", _ => unread.ToString() };
    private static string ShortName(string jid) => jid.Split('@', 2)[0];

    private void UpdateConversation()
    {
        var room = _selected is { Kind: XmppConversationKind.Room };
        var jid = _selected?.Jid;
        var joined = room && _client.JoinedRooms.Contains(jid!);
        var saved = room && _client.Rooms.Any(entry => entry.Jid == jid);
        var status = room ? _client.RoomStatus(jid!) : new XmppRoomStatus();
        var contact = room ? null : _client.Contacts.FirstOrDefault(item => item.Jid == jid);
        ConversationTitle.Text = _client.Account is null ? "Set up your alliance account" :
            jid is not null
                ? (room ? ShortName(jid) : contact?.Name ?? (_client.Account is { } account &&
                    jid == XmppAddresses.Directorbot(account.Jid).Jid ? "Directorbot" : ShortName(jid)))
                : "Notification Feed";
        var detail = room
            ? status.Detail ?? (joined ? null : "Not joined")
            : contact?.SubscriptionRequested == true ? "Contact request" : null;
        RoomDetail.Text = detail ?? "";
        RoomDetail.IsVisible = !string.IsNullOrEmpty(detail);
        ToolTip.SetTip(ConversationTitle, status.Detail ?? jid);
        ConversationHeader.IsVisible = !room;
        if (!room && jid is not null)
        {
            ConversationAddress.Text = jid;
            ConversationAddress.IsVisible = true;
            ApplyConversationPresence(contact?.EffectiveShow ?? XmppPresenceShow.Offline, null, contact?.StatusText);
            _ = RefreshConversationPresenceAsync(jid, contact);
        }
        else
        {
            ConversationAddress.IsVisible = false;
            ConversationPresence.IsVisible = false;
        }
        RoomButton.IsVisible = room;
        JoinButton.IsVisible = false;
        LeaveButton.IsVisible = joined;
        ForgetRoomButton.IsVisible = saved;
        RemoveContactButton.IsVisible = contact is not null;
        AcceptContactButton.IsVisible = DeclineContactButton.IsVisible = contact?.SubscriptionRequested == true;
        RemoveContactButton.IsEnabled = AcceptContactButton.IsEnabled = DeclineContactButton.IsEnabled = _client.Online;
        ConversationMenu.IsVisible = jid is not null;
        FloatFeedButton.IsVisible = jid is null && _client.Account is not null;
        ClearFeedButton.IsVisible = FloatFeedButton.IsVisible;
        FilterSenderButton.IsVisible = jid is not null;
        ComposeArea.IsVisible = jid is not null;
        SendButton.IsEnabled = _client.Online && (!room || joined) && !_sending;

        UpdateTopic(jid, status.Topic);
        UpdateSplitTopic();
        UpdateOccupants(room, joined, jid);
    }

    private void ApplyConversationPresence(XmppPresenceShow show, string? lastSeenDetail, string? statusText)
    {
        var label = PresenceLabel(show);
        ConversationPresence.Text = lastSeenDetail is null ? label : $"{label} · {lastSeenDetail}";
        ConversationPresence.Foreground = PresenceBrush(show);
        ConversationPresence.IsVisible = true;
        if (!string.IsNullOrWhiteSpace(statusText) &&
            !string.Equals(statusText, lastSeenDetail, StringComparison.OrdinalIgnoreCase))
            ToolTip.SetTip(ConversationPresence, statusText);
        else ToolTip.SetTip(ConversationPresence, null);
    }

    private static bool IsValidLastSeen(DateTimeOffset? stamp) =>
        stamp is { Year: >= 2000 };

    private static string? PresenceDetail(XmppPresenceShow show, DateTimeOffset? lastSeenUtc, Func<DateTimeOffset, string> format)
    {
        if (show is XmppPresenceShow.Online or XmppPresenceShow.Chat) return "Active now";
        if (show is XmppPresenceShow.Away or XmppPresenceShow.ExtendedAway or XmppPresenceShow.DoNotDisturb)
            return null;
        return IsValidLastSeen(lastSeenUtc) ? $"Last seen {format(lastSeenUtc!.Value)}" : null;
    }

    private async Task RefreshConversationPresenceAsync(string jid, XmppContact? contact)
    {
        var generation = ++_headerProfileGeneration;
        try
        {
            var profile = await _client.EnsureProfileAsync(jid);
            if (generation != _headerProfileGeneration || _selected?.Jid != jid) return;
            var show = profile.Online ? profile.Show == XmppPresenceShow.Offline ? XmppPresenceShow.Online : profile.Show
                : XmppPresenceShow.Offline;
            if (contact is not null) show = contact.EffectiveShow;
            var detail = PresenceDetail(show, profile.LastSeenUtc, FormatLastSeen);
            ApplyConversationPresence(show, detail, profile.StatusText ?? contact?.StatusText);
        }
        catch
        {
            if (generation == _headerProfileGeneration && _selected?.Jid == jid)
                ApplyConversationPresence(contact?.EffectiveShow ?? XmppPresenceShow.Offline, null, contact?.StatusText);
        }
    }

    private void UpdateTopic(string? jid, string? topic) =>
        ApplyTopic(jid, topic, _selected, TopicPanel, TopicPreview, TopicBody, TopicAge, TopicAgeCollapsed,
            TopicCollapsed, TopicExpanded);

    private void UpdateSplitTopic()
    {
        var conversation = _splitTab?.Conversation;
        var topic = conversation?.Kind == XmppConversationKind.Room ? _client.RoomStatus(conversation.Jid).Topic : null;
        ApplyTopic(conversation?.Jid, topic, conversation, SplitTopicPanel, SplitTopicPreview, SplitTopicBody,
            SplitTopicAge, SplitTopicAgeCollapsed, SplitTopicCollapsed, SplitTopicExpanded);
    }

    private void ApplyTopic(string? jid, string? topic, XmppConversation? conversation, Border panel,
        XmppLinkText preview, XmppLinkText body, TextBlock age, TextBlock ageCollapsed, Grid collapsed, Grid expanded)
    {
        var show = !string.IsNullOrEmpty(topic) && conversation is { Kind: XmppConversationKind.Room };
        panel.IsVisible = show;
        if (!show)
        {
            age.Text = ageCollapsed.Text = "";
            age.IsVisible = ageCollapsed.IsVisible = false;
            return;
        }
        var flat = string.Join(' ', topic!.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        preview.BodyText = flat;
        preview.DefaultTip = topic;
        body.BodyText = topic;
        body.DefaultTip = topic;
        ToolTip.SetTip(collapsed, topic);
        ToolTip.SetTip(expanded, topic);
        var open = jid is not null && !_collapsedTopics.Contains(jid);
        collapsed.IsVisible = !open;
        expanded.IsVisible = open;
        var status = _client.RoomStatus(jid!);
        var label = XmppTopicAge.Format(status.TopicChangedUtc, status.TopicAuthor);
        age.Text = ageCollapsed.Text = label ?? "";
        age.IsVisible = open && label is not null;
        ageCollapsed.IsVisible = !open && label is not null;
        _topicAgeRendered = DateTime.UtcNow;
    }

    private void RefreshTopicAge()
    {
        if (_selected is { Kind: XmppConversationKind.Room } room)
            ApplyTopic(room.Jid, _client.RoomStatus(room.Jid).Topic, room, TopicPanel, TopicPreview, TopicBody,
                TopicAge, TopicAgeCollapsed, TopicCollapsed, TopicExpanded);
        UpdateSplitTopic();
    }

    private void ToggleTopic(object? sender, RoutedEventArgs args)
    {
        if (!_ready) return;
        var split = sender is Control { Tag: "split" };
        var room = split ? _splitTab?.Conversation : _selected;
        if (room is not { Kind: XmppConversationKind.Room }) return;
        if (_collapsedTopics.Remove(room.Jid) == false) _collapsedTopics.Add(room.Jid);
        if (split) UpdateSplitTopic();
        else UpdateTopic(room.Jid, _client.RoomStatus(room.Jid).Topic);
        QueueWorkspaceSave();
    }

    private void UpdateOccupants(bool room, bool joined, string? jid)
    {
        OccupantsToggle.IsVisible = room && joined;
        if (room && joined && !_occupantsOverride)
            OccupantsToggle.IsChecked = _client.DisplayPreferences.ShowOccupantSidebar;
        var want = OccupantsToggle.IsChecked == true;
        OccupantSidebar.IsVisible = OccupantSplitter.IsVisible = room && joined && want;
        ApplyOccupantWidth();
        if (!(room && joined && jid is not null))
        {
            Occupants.ItemsSource = null;
            _occupantJidSignature = "";
            return;
        }

        var people = _client.Occupants(jid);
        var signature = string.Join('\n', people
            .OrderBy(occupant => occupant.Nick, StringComparer.OrdinalIgnoreCase)
            .Select(occupant => $"{occupant.Nick}\0{occupant.Jid}"));
        if (signature != _occupantJidSignature)
        {
            var shouldRefresh = _occupantJidSignature.Length > 0 ||
                people.Any(occupant => !string.IsNullOrWhiteSpace(occupant.Jid));
            _occupantJidSignature = signature;
            if (shouldRefresh && _selected?.Jid == jid && _renderedTab == _active && _active.Messages.Count > 0)
                RenderTranscript(_active, CaptureAnchor(), _active.Follow);
        }

        if (!OccupantSidebar.IsVisible)
        {
            Occupants.ItemsSource = null;
            return;
        }

        OccupantHeader.Text = $"{people.Count} {(people.Count == 1 ? "person" : "people")} in room";
        Occupants.ItemsSource = people.Select(occupant =>
        {
            var (icon, brush) = OccupantGlyph(occupant);
            var bare = XmppAddresses.Bare(occupant.Jid);
            return new XmppOccupantRow(occupant.Nick, icon, brush, SenderColor(occupant.Nick), bare);
        }).ToArray();
    }

    private void ApplyOccupantWidth()
    {
        if (WorkspaceGrid.ColumnDefinitions.Count < 5) return;
        WorkspaceGrid.ColumnDefinitions[4].Width = OccupantSidebar.IsVisible
            ? new GridLength(_occupantWidth)
            : new GridLength(0);
        WorkspaceGrid.ColumnDefinitions[4].MinWidth = OccupantSidebar.IsVisible ? DefaultOccupantWidth * 0.5 : 0;
        WorkspaceGrid.ColumnDefinitions[4].MaxWidth = OccupantSidebar.IsVisible ? 280 : 0;
    }

    private static (StreamGeometry Icon, IBrush Brush) OccupantGlyph(XmppOccupant occupant)
    {
        // Pidgin-style MUC tiers: owner = flag, admin/moderator = star, everyone else = person.
        var affiliation = occupant.Affiliation.ToLowerInvariant();
        if (affiliation == "owner") return (IconFlag, AdminIcon);
        if (affiliation == "admin" ||
            occupant.Role.Equals("moderator", StringComparison.OrdinalIgnoreCase))
            return (IconStar, OwnerIcon);
        return (IconParticipant, ParticipantIcon);
    }

    private void RenderTabs()
    {
        foreach (var tab in _tabs)
        {
            EnsureHeaderOnStrip(tab);
            var label = tab.Conversation is { } conversation ? TabName(conversation) : "Notification Feed";
            var count = CountLabel(UnreadCount(tab));
            var unread = count.Length > 0;
            tab.Caption!.Text = label;
            if (tab.UnreadPip is { } pip)
            {
                if (tab.Conversation is { Kind: XmppConversationKind.Direct } direct)
                {
                    var contact = _client.Contacts.FirstOrDefault(item => item.Jid == direct.Jid);
                    pip.Background = PresenceBrush(contact?.EffectiveShow ?? XmppPresenceShow.Offline);
                    pip.IsVisible = true;
                }
                else
                {
                    pip.Background = Active;
                    pip.IsVisible = unread;
                }
            }
            if (tab.UnreadBadge is { } badge)
            {
                badge.IsVisible = unread;
                if (badge.Child is TextBlock text) text.Text = count;
            }
            if (tab.Caption is { } caption)
                caption.Foreground = unread ? UnreadCaption : TabCaption;
            Avalonia.Automation.AutomationProperties.SetName(tab.SelectButton!,
                unread ? $"{label} {count}" : label);
            var active = tab.InSplit ? tab == _splitTab : tab == _active;
            tab.Header!.Background = active ? SelectedTab : InactiveTab;
            tab.Header.Opacity = active || unread ? 1 : 0.85;
            if (tab.MoveItem is { } move)
            {
                move.Header = tab.InSplit ? "Move to top" : "Move to split view";
                move.IsEnabled = tab.InSplit || _tabs.Count(item => !item.InSplit) > 1;
            }
        }
        LayoutAllTabStrips();
    }

    private string TabName(XmppConversation source)
    {
        if (source.Kind == XmppConversationKind.Room) return ShortName(source.Jid);
        if (_client.Account is { } account && source == XmppAddresses.Directorbot(account.Jid))
            return "Directorbot";
        return _client.Contacts.FirstOrDefault(contact => contact.Jid == source.Jid)?.Name ?? ShortName(source.Jid);
    }

    private void BuildTabHeader(ConversationTab tab)
    {
        var caption = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = TabCaptionMaxWidth
        };
        var pip = new Border
        {
            Width = 7, Height = 7, CornerRadius = new(4), Background = Active,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new(0, 0, 2, 0)
        };
        var badgeText = new TextBlock
        {
            FontSize = 10, Foreground = UnreadFg,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Border
        {
            Background = UnreadBg, CornerRadius = new(7), Padding = new(4, 0), MinWidth = 14, Height = 14,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Child = badgeText
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(pip);
        content.Children.Add(caption);
        content.Children.Add(badge);
        var select = new Button
        {
            Content = content, Padding = new(8, 4), Background = Brushes.Transparent, BorderThickness = new(0)
        };
        ToolTip.SetTip(select, tab.Conversation?.Jid ?? "Notification Feed");
        select.Click += (_, _) =>
        {
            if (_suppressTabClick)
            {
                _suppressTabClick = false;
                return;
            }
            if (tab.InSplit)
            {
                _splitTab = tab;
                FocusPane(split: true);
                RenderTabs();
                Invalidate();
                return;
            }
            FocusPane(split: false);
            ActivateTab(tab);
        };
        if (tab.Conversation is not null)
        {
            var move = new MenuItem { Header = tab.InSplit ? "Move to top" : "Move to split view" };
            move.Click += (_, _) => MoveTab(tab, !tab.InSplit);
            select.ContextMenu = new ContextMenu { Items = { move } };
            tab.MoveItem = move;
        }
        else
        {
            var floatFeed = new MenuItem { Header = "Open floating feed" };
            floatFeed.Click += (_, _) => ShowFeedOverlay();
            select.ContextMenu = new ContextMenu { Items = { floatFeed } };
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(select);
        if (tab.Conversation is { } conversation)
        {
            var close = new Button
            {
                Content = "×", FontSize = 12, Padding = new(4, 0), MinWidth = 20, MinHeight = 22,
                Background = Brushes.Transparent, BorderThickness = new(0),
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center
            };
            close.Classes.Add("icon");
            ToolTip.SetTip(close, $"Close {conversation.Jid}");
            Avalonia.Automation.AutomationProperties.SetName(close, $"Close {conversation.Jid}");
            close.Click += (_, _) => CloseTab(tab);
            buttons.Children.Add(close);
        }
        tab.SelectButton = select;
        tab.Caption = caption;
        tab.UnreadBadge = badge;
        tab.UnreadPip = pip;
        tab.Header = new Border { Child = buttons, CornerRadius = new(3), Padding = new(1) };
        PrepareTabDrag(tab);
    }

    private static string TabKey(ConversationTab tab) => tab.Conversation?.Jid ?? "\0feed";

    private void PrepareTabDrag(ConversationTab tab)
    {
        if (tab.Conversation is null || tab.SelectButton is not { } select) return;
        select.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(select).Properties.IsLeftButtonPressed) return;
            CancelTabDrag();
            _dragTab = tab;
            _dragStart = e.GetPosition(this);
            _dragging = false;
            _dragPointer = e.Pointer;
        }, RoutingStrategies.Tunnel);
    }

    private void OnTabDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTab is null || _dragPointer is null || e.Pointer != _dragPointer || _dragStart is not { } start)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CancelTabDrag();
            return;
        }

        var pos = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - start.X) + Math.Abs(pos.Y - start.Y) < 8) return;
            _dragging = true;
            _suppressTabClick = true;
            e.Pointer.Capture(this);
            if (_dragTab.Header is { } header) header.Opacity = 0.4;
            EnsureTabDragVisuals();
        }

        UpdateTabDragVisuals(pos);
        e.Handled = true;
    }

    private void OnTabDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragPointer is null || e.Pointer != _dragPointer) return;
        var tab = _dragTab;
        var wasDragging = _dragging;
        var targetSplit = _dragTargetSplit;
        var insertIndex = _dragInsertIndex;
        CancelTabDrag();
        if (!wasDragging || tab is null) return;
        _suppressTabClick = true;
        e.Handled = true;
        DropTabAt(tab, targetSplit, insertIndex);
    }

    private void CancelTabDrag()
    {
        if (_dragTab?.Header is { } header)
            header.Opacity = _dragTab == _active || _dragTab == _splitTab || HasUnread(_dragTab) ? 1 : 0.85;
        if (_tabDragGhost is not null) _tabDragGhost.IsVisible = false;
        if (_tabInsertMarker is not null) _tabInsertMarker.IsVisible = false;
        if (_dragPointer is { } pointer && pointer.Captured == this)
            pointer.Capture(null);
        _dragTab = null;
        _dragStart = null;
        _dragging = false;
        _dragPointer = null;
        _suppressTabClick = false;
        _dragInsertIndex = -1;
    }

    private void EnsureTabDragVisuals()
    {
        if (_tabDragGhost is null)
        {
            _tabDragGhost = new Border
            {
                Background = SelectedTab,
                BorderBrush = Active,
                BorderThickness = new(1),
                CornerRadius = new(3),
                Padding = new(8, 4),
                Opacity = 0.92,
                Child = new TextBlock
                {
                    FontSize = 12,
                    Foreground = TabCaption,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = TabCaptionMaxWidth
                }
            };
            _tabInsertMarker = new Border
            {
                Width = 2,
                Background = Active,
                CornerRadius = new(1),
                IsVisible = false
            };
            TabDragLayer.Children.Add(_tabInsertMarker);
            TabDragLayer.Children.Add(_tabDragGhost);
        }

        if (_tabDragGhost.Child is TextBlock label)
            label.Text = _dragTab?.Caption?.Text ?? "Tab";
        _tabDragGhost.IsVisible = true;
    }

    private void UpdateTabDragVisuals(Point windowPos)
    {
        if (_tabDragGhost is null || _tabInsertMarker is null || _dragTab is null) return;

        Canvas.SetLeft(_tabDragGhost, windowPos.X + 12);
        Canvas.SetTop(_tabDragGhost, windowPos.Y + 8);

        var targetSplit = SplitPane.IsVisible && PointOver(SplitTabStrip.Parent as Control ?? SplitTabStrip, windowPos);
        if (!targetSplit && !PointOver(TabStripHost, windowPos) && _dragTab.InSplit)
            targetSplit = true;
        _dragTargetSplit = targetSplit;

        var strip = targetSplit ? SplitTabStrip : TabStrip;
        var localX = LocalX(strip, windowPos);
        _dragInsertIndex = ComputeInsertIndex(_dragTab, targetSplit, localX);

        if (strip.TransformToVisual(TabDragLayer) is not { } transform)
        {
            _tabInsertMarker.IsVisible = false;
            return;
        }

        var origin = transform.Transform(new Point(0, 0));
        var x = origin.X;
        var ordered = StripTabs(targetSplit).Where(tab => tab.Header is { IsVisible: true }).ToList();
        for (var index = 0; index < ordered.Count && index < _dragInsertIndex; index++)
        {
            if (ordered[index].Header is not { } header) continue;
            x += header.Bounds.Width + TabStripSpacing;
        }
        if (_dragInsertIndex > 0) x -= TabStripSpacing / 2;

        var height = Math.Max(22, ordered.FirstOrDefault()?.Header?.Bounds.Height ?? 22);
        _tabInsertMarker.Height = height;
        Canvas.SetLeft(_tabInsertMarker, x);
        Canvas.SetTop(_tabInsertMarker, origin.Y);
        _tabInsertMarker.IsVisible = true;
    }

    private int ComputeInsertIndex(ConversationTab tab, bool targetSplit, double pointerX)
    {
        var ordered = StripTabs(targetSplit).ToList();
        if (ordered.Count == 0) return 0;
        var insert = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Header is not { IsVisible: true } header) continue;
            var mid = header.Bounds.X + header.Bounds.Width / 2;
            if (pointerX >= mid) insert = index + 1;
        }

        if (tab.InSplit == targetSplit)
        {
            var from = ordered.IndexOf(tab);
            if (from >= 0 && insert > from) insert--;
        }

        var min = targetSplit ? 0 : 1;
        if (!targetSplit && ordered[0].Conversation is null) min = 1;
        return Math.Clamp(insert, min, Math.Max(min, ordered.Count - (tab.InSplit == targetSplit ? 1 : 0)));
    }

    private bool HasUnread(ConversationTab tab) => UnreadCount(tab) > 0;

    private int UnreadCount(ConversationTab tab) => tab.Conversation is { } source
        ? _counts.GetValueOrDefault(source) : _feedUnread;

    private void DropTabAt(ConversationTab tab, bool targetSplit, int insertIndex)
    {
        if (tab.InSplit != targetSplit)
        {
            if (!MoveTab(tab, targetSplit)) return;
        }

        var ordered = StripTabs(targetSplit).ToList();
        var from = ordered.IndexOf(tab);
        if (from < 0) return;
        insertIndex = Math.Clamp(insertIndex, targetSplit ? 0 : 1, ordered.Count - 1);
        if (!targetSplit && ordered[0].Conversation is null && insertIndex < 1) insertIndex = 1;
        if (insertIndex == from)
        {
            LayoutAllTabStrips();
            return;
        }

        ordered.RemoveAt(from);
        ordered.Insert(insertIndex, tab);
        if (!targetSplit)
        {
            var feed = _tabs.FirstOrDefault(item => item.Conversation is null);
            var others = ordered.Where(item => item.Conversation is not null).ToList();
            var split = _tabs.Where(item => item.InSplit).ToList();
            _tabs.Clear();
            if (feed is not null) _tabs.Add(feed);
            _tabs.AddRange(others);
            _tabs.AddRange(split.Where(item => !_tabs.Contains(item)));
        }
        else
        {
            var top = _tabs.Where(item => !item.InSplit).ToList();
            _tabs.Clear();
            _tabs.AddRange(top);
            _tabs.AddRange(ordered);
        }

        SyncTabStripChildren(targetSplit);
        LayoutAllTabStrips();
        QueueWorkspaceSave();
    }

    private bool PointOver(Control control, Point windowPos)
    {
        if (!control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) return false;
        if (control.TransformToVisual(this) is not { } transform) return false;
        var origin = transform.Transform(new Point(0, 0));
        var rect = new Rect(origin, control.Bounds.Size);
        return rect.Contains(windowPos);
    }

    private double LocalX(Control control, Point windowPos)
    {
        if (control.TransformToVisual(this) is not { } transform) return 0;
        var origin = transform.Transform(new Point(0, 0));
        return windowPos.X - origin.X;
    }

    private void ReorderTab(ConversationTab tab, Panel strip, double pointerX)
    {
        if (tab.Conversation is null) return;
        var ordered = StripTabs(tab.InSplit).ToList();
        var from = ordered.IndexOf(tab);
        if (from < 0) return;

        var insert = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Header is not { IsVisible: true } header) continue;
            var mid = header.Bounds.X + header.Bounds.Width / 2;
            if (pointerX >= mid) insert = index + 1;
        }
        if (insert > from) insert--;
        insert = Math.Clamp(insert, tab.InSplit ? 0 : 1, ordered.Count - 1);
        if (!tab.InSplit && ordered[0].Conversation is null && insert < 1) insert = 1;
        if (insert == from) return;

        ordered.RemoveAt(from);
        ordered.Insert(insert, tab);
        if (!tab.InSplit)
        {
            var feed = _tabs.FirstOrDefault(item => item.Conversation is null);
            var others = ordered.Where(item => item.Conversation is not null).ToList();
            var split = _tabs.Where(item => item.InSplit).ToList();
            _tabs.Clear();
            if (feed is not null) _tabs.Add(feed);
            _tabs.AddRange(others);
            _tabs.AddRange(split.Where(item => !_tabs.Contains(item)));
        }
        else
        {
            var top = _tabs.Where(item => !item.InSplit).ToList();
            _tabs.Clear();
            _tabs.AddRange(top);
            _tabs.AddRange(ordered);
        }

        SyncTabStripChildren(tab.InSplit);
        QueueWorkspaceSave();
    }

    private IEnumerable<ConversationTab> StripTabs(bool split) =>
        _tabs.Where(tab => tab.InSplit == split);

    private void SyncTabStripChildren(bool split)
    {
        var strip = split ? SplitTabStrip : TabStrip;
        var desired = StripTabs(split).ToList();
        foreach (var child in strip.Children.OfType<Control>().ToList())
        {
            if (desired.All(tab => !ReferenceEquals(tab.Header, child)))
                strip.Children.Remove(child);
        }
        foreach (var tab in desired)
            EnsureHeaderOnStrip(tab);
        // Keep visual order aligned with _tabs.
        for (var index = 0; index < desired.Count; index++)
        {
            if (desired[index].Header is not { } header) continue;
            var current = strip.Children.IndexOf(header);
            if (current >= 0 && current != index)
                strip.Children.Move(current, index);
        }
    }

    private void EnsureHeaderOnStrip(ConversationTab tab)
    {
        if (tab.Header is null) BuildTabHeader(tab);
        if (tab.Header is not { } header) return;
        var strip = tab.InSplit ? SplitTabStrip : TabStrip;
        if (ReferenceEquals(header.Parent, strip)) return;
        (header.Parent as Panel)?.Children.Remove(header);
        strip.Children.Add(header);
    }

    private void QueueTabLayout()
    {
        _tabLayoutTimer.Stop();
        _tabLayoutTimer.Start();
    }

    private void LayoutAllTabStrips()
    {
        LayoutTabStrip(TabStrip, TabOverflowButton, false);
        LayoutTabStrip(SplitTabStrip, SplitTabOverflowButton, true);
    }

    private void LayoutTabStrip(StackPanel strip, Button overflow, bool split)
    {
        var tabs = StripTabs(split).Where(tab => tab.Header is not null).ToList();
        if (tabs.Count == 0)
        {
            overflow.IsVisible = false;
            return;
        }

        var host = strip.Parent as Control ?? strip;
        var available = host.Bounds.Width;
        if (available <= 0) return;

        foreach (var tab in tabs)
        {
            tab.Header!.IsVisible = true;
            if (tab.Caption is { } caption) caption.MaxWidth = TabCaptionMaxWidth;
        }

        double MeasureTotal(double captionWidth)
        {
            var total = 0d;
            for (var index = 0; index < tabs.Count; index++)
            {
                if (tabs[index].Caption is { } caption) caption.MaxWidth = captionWidth;
                var header = tabs[index].Header!;
                header.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                total += header.DesiredSize.Width;
                if (index > 0) total += TabStripSpacing;
            }
            return total;
        }

        var desired = MeasureTotal(TabCaptionMaxWidth);
        if (desired <= available)
        {
            overflow.IsVisible = false;
            return;
        }

        var minTotal = MeasureTotal(TabCaptionMinWidth);
        if (minTotal <= available)
        {
            overflow.IsVisible = false;
            var low = TabCaptionMinWidth;
            var high = TabCaptionMaxWidth;
            for (var step = 0; step < 12; step++)
            {
                var mid = (low + high) / 2;
                if (MeasureTotal(mid) <= available) low = mid;
                else high = mid;
            }
            MeasureTotal(low);
            return;
        }

        overflow.IsVisible = true;
        var budget = Math.Max(0, available - TabOverflowButtonWidth);
        var active = split ? _splitTab : _active;
        var visible = new List<ConversationTab>();
        var used = 0d;

        double WidthOf(ConversationTab tab)
        {
            if (tab.Caption is { } caption) caption.MaxWidth = TabCaptionMinWidth;
            tab.Header!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return tab.Header.DesiredSize.Width;
        }

        foreach (var tab in tabs)
        {
            var width = WidthOf(tab);
            var next = visible.Count == 0 ? width : used + TabStripSpacing + width;
            if (next <= budget || (active is not null && tab == active && visible.Count == 0))
            {
                visible.Add(tab);
                used = next;
            }
        }

        if (active is not null && tabs.Contains(active) && !visible.Contains(active))
        {
            while (visible.Count > 0 && !visible.Contains(active))
            {
                visible.RemoveAt(visible.Count - 1);
                used = 0;
                for (var index = 0; index < visible.Count; index++)
                {
                    used += WidthOf(visible[index]);
                    if (index > 0) used += TabStripSpacing;
                }
                var width = WidthOf(active);
                var withActive = visible.Count == 0 ? width : used + TabStripSpacing + width;
                if (withActive <= budget || visible.Count == 0)
                {
                    visible.Add(active);
                    used = withActive;
                }
            }
            if (!visible.Contains(active)) visible.Add(active);
        }

        var visibleSet = visible.ToHashSet();
        foreach (var tab in tabs)
            tab.Header!.IsVisible = visibleSet.Contains(tab);
        MeasureTotal(TabCaptionMinWidth);
    }

    private void ShowTabOverflow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var split = ReferenceEquals(button, SplitTabOverflowButton);
        var hidden = StripTabs(split).Where(tab => tab.Header is { IsVisible: false }).ToList();
        if (hidden.Count == 0) return;
        var menu = new MenuFlyout();
        foreach (var tab in hidden)
        {
            var label = tab.Conversation is { } conversation ? TabName(conversation) : "Notification Feed";
            var count = CountLabel(UnreadCount(tab));
            var item = new MenuItem { Header = count.Length > 0 ? $"{label} ({count})" : label };
            item.Click += (_, _) =>
            {
                if (split)
                {
                    _splitTab = tab;
                    FocusPane(split: true);
                    RenderTabs();
                    Invalidate();
                }
                else
                {
                    FocusPane(split: false);
                    ActivateTab(tab);
                }
                LayoutAllTabStrips();
            };
            menu.Items.Add(item);
        }
        FlyoutBase.SetAttachedFlyout(button, menu);
        FlyoutBase.ShowAttachedFlyout(button);
    }

    internal bool TryReorderTabForTests(string tabKey, int targetIndexAmongStrip)
    {
        var tab = _tabs.Find(item => TabKey(item) == tabKey);
        if (tab is null) return false;
        var ordered = StripTabs(tab.InSplit).ToList();
        var from = ordered.IndexOf(tab);
        if (from < 0 || targetIndexAmongStrip < 0 || targetIndexAmongStrip >= ordered.Count) return false;
        if (from == targetIndexAmongStrip) return true;
        if (!tab.InSplit && targetIndexAmongStrip < 1 && ordered[0].Conversation is null) return false;

        ordered.RemoveAt(from);
        ordered.Insert(targetIndexAmongStrip, tab);
        if (!tab.InSplit)
        {
            var feed = _tabs.FirstOrDefault(item => item.Conversation is null);
            var others = ordered.Where(item => item.Conversation is not null).ToList();
            var split = _tabs.Where(item => item.InSplit).ToList();
            _tabs.Clear();
            if (feed is not null) _tabs.Add(feed);
            _tabs.AddRange(others);
            _tabs.AddRange(split.Where(item => !_tabs.Contains(item)));
        }
        else
        {
            var top = _tabs.Where(item => !item.InSplit).ToList();
            _tabs.Clear();
            _tabs.AddRange(top);
            _tabs.AddRange(ordered);
        }

        SyncTabStripChildren(tab.InSplit);
        LayoutAllTabStrips();
        QueueWorkspaceSave();
        return true;
    }

    private bool MoveTab(ConversationTab tab, bool toSplit)
    {
        if (tab.InSplit == toSplit) return true;
        if (toSplit && _tabs.Count(item => !item.InSplit) <= 1) return false;
        tab.InSplit = toSplit;
        ConversationTab? activate = null;
        if (toSplit)
        {
            if (_active == tab)
                activate = _tabs.First(item => !item.InSplit);
            _splitTab = tab;
            if (SplitButton.IsChecked != true)
            {
                SplitButton.IsChecked = true;
                ApplySplitLayout(true);
            }
            FocusPane(split: true);
        }
        else
        {
            if (_splitTab == tab) _splitTab = _tabs.FirstOrDefault(item => item.InSplit);
            FocusPane(split: false);
            activate = tab;
        }
        SyncTabStripChildren(false);
        SyncTabStripChildren(true);
        if (activate is not null)
            ActivateTab(activate);
        else
            RenderTabs();
        QueueWorkspaceSave();
        Invalidate();
        return true;
    }

    private void Select(XmppConversation? conversation)
    {
        var tab = EnsureTab(conversation);
        if (conversation is null)
            FocusPane(split: false);
        else if (_splitFocus && SplitPane.IsVisible)
        {
            if (!tab.InSplit && !MoveTab(tab, true))
                ActivateTab(tab);
            else
            {
                _splitTab = tab;
                FocusPane(split: true);
                RenderTabs();
                Invalidate();
            }
            return;
        }
        if (tab.InSplit) MoveTab(tab, false);
        else ActivateTab(tab);
    }

    private ConversationTab EnsureTab(XmppConversation? conversation)
    {
        var tab = _tabs.Find(item => item.Conversation == conversation);
        if (tab is not null) return tab;
        tab = conversation is not null && _closedTabs.Remove(conversation, out var closed) ? closed : new(conversation);
        _tabs.Add(tab);
        if (tab.Header is null) BuildTabHeader(tab);
        SyncTabStripChildren(tab.InSplit);
        RenderTabs();
        QueueWorkspaceSave();
        return tab;
    }

    private void ActivateTab(ConversationTab tab)
    {
        if (_active == tab) return;
        _active.Draft = Compose.Text ?? "";
        if (!_restoringScroll)
        {
            _active.Anchor = CaptureAnchor();
            _active.Follow = NearBottom();
        }
        _active = tab;
        UpdateReadFocus();
        _lastVisible = [];
        Compose.Text = tab.Draft;
        ResetNickCompletion();
        RenderTabs();
        UpdateConversation();
        if (XmppDisplayPreferences.NormalizeScrollToLatestOnTabSwitch(
                _client.DisplayPreferences.ScrollToLatestOnTabSwitch))
            RenderTranscript(tab, null, follow: true);
        else
            RenderTranscript(tab, tab.Anchor, tab.Follow);
        tab.Header?.BringIntoView();
        QueueWorkspaceSave();
        Invalidate();
    }

    private void CloseTab(ConversationTab tab)
    {
        if (tab.Conversation is null) return;
        if (tab.InSplit)
        {
            _tabs.Remove(tab);
            if (tab.Header is { } splitHeader)
            {
                SplitTabStrip.Children.Remove(splitHeader);
                tab.Header = null;
            }
            if (_splitTab == tab) _splitTab = _tabs.FirstOrDefault(item => item.InSplit);
            _closedTabs[tab.Conversation] = tab;
            RenderTabs();
            QueueWorkspaceSave();
            Invalidate();
            return;
        }
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;
        var top = _tabs.Where(item => !item.InSplit).ToList();
        if (top.Count <= 1) return;
        if (_active == tab)
            ActivateTab(top.Last(item => item != tab));
        _tabs.Remove(tab);
        if (tab.Header is { } header)
        {
            TabStrip.Children.Remove(header);
            tab.Header = null;
        }
        _closedTabs[tab.Conversation] = tab;
        SyncTabStripChildren(false);
        LayoutAllTabStrips();
        QueueWorkspaceSave();
    }

    private async void WorkspaceKeyDown(object? sender, KeyEventArgs args)
    {
        if (!_ready || !args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (args.Key == Key.A && (TranscriptScroll.IsKeyboardFocusWithin || SplitTranscriptScroll.IsKeyboardFocusWithin))
        {
            var transcript = SplitTranscriptScroll.IsKeyboardFocusWithin ? SplitTranscriptText : TranscriptText;
            transcript.Focus();
            transcript.SelectAll();
            args.Handled = true;
            return;
        }
        if (args.Key == Key.Tab && _tabs.Count > 1)
        {
            args.Handled = true;
            var direction = args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            var next = (_tabs.IndexOf(_active) + direction + _tabs.Count) % _tabs.Count;
            ActivateTab(_tabs[next]);
        }
        else if (args.Key == Key.Enter && (Compose.IsFocused || SplitCompose.IsFocused))
        {
            args.Handled = true;
            await (SplitCompose.IsFocused ? SendSplitAsync() : SendAsync());
        }
    }

    private async void SourceChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (!_ready || _changingSource || Sources.SelectedItem is not XmppSidebarRow row) return;
        if (row.Kind == XmppSidebarKind.Group && row.GroupKey is { } key)
        {
            if (_collapsedGroups.Remove(key) == false) _collapsedGroups.Add(key);
            Sources.SelectedIndex = -1;
            UpdateSidebar();
            QueueWorkspaceSave();
            return;
        }
        if (row.Kind == XmppSidebarKind.Divider || row is not { Source: { } source })
        {
            Sources.SelectedIndex = -1;
            return;
        }
        Select(row.Fleet ? null : source);
        Sources.SelectedIndex = -1;
        if (_client.Online && source.Kind == XmppConversationKind.Room && !_client.JoinedRooms.Contains(source.Jid))
            await JoinRoomAsync(source);
    }

    private async void JoinCurrentRoom(object? sender, RoutedEventArgs args)
    {
        var target = _roomMenuTarget ?? _selected;
        if (target is { Kind: XmppConversationKind.Room } source) await JoinRoomAsync(source);
    }

    private Task JoinRoomAsync(XmppConversation source) => RunAsync(() => _client.JoinSavedRoomAsync(source.Jid));
    private void FilterChanged(object? sender, TextChangedEventArgs args) { if (_ready) UpdateSidebar(); }

    private void ToggleSidebar(object? sender, RoutedEventArgs args)
    {
        _sidebarOverride = !Sidebar.IsVisible;
        ApplySidebar();
        if (Sidebar.IsVisible) FilterBox.Focus();
    }

    private void ToggleOccupants(object? sender, RoutedEventArgs args)
    {
        _occupantsOverride = true;
        UpdateConversation();
    }

    private void ApplySidebar()
    {
        var show = _sidebarOverride ?? Bounds.Width >= 710;
        Sidebar.IsVisible = SidebarSplitter.IsVisible = show;
        WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(show ? _sidebarWidth : 0);
        WorkspaceGrid.ColumnDefinitions[0].MinWidth = show ? DefaultSidebarWidth * 0.5 : 0;
        WorkspaceGrid.ColumnDefinitions[0].MaxWidth = show ? 360 : 0;
        Avalonia.Automation.AutomationProperties.SetName(SidebarToggle, show ? "Hide conversations" : "Show conversations");
        SidebarToggle.IsChecked = show;
    }

    private void TogglePin(object? sender, RoutedEventArgs args)
    {
        Topmost = PinButton.IsChecked == true;
        QueueWorkspaceSave();
    }

    private void RoomRowActions(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: XmppSidebarRow { Source: { } source } } button)
            return;
        args.Handled = true;
        var menu = new MenuFlyout();
        if (source.Kind == XmppConversationKind.Room)
        {
            _roomMenuTarget = source;
            var joined = _client.JoinedRooms.Contains(source.Jid);
            var saved = _client.Rooms.Any(room => room.Jid == source.Jid);
            var detail = _client.RoomStatus(source.Jid).Detail;
            var rooms = _client.Rooms.ToList();
            var index = rooms.FindIndex(room => room.Jid == source.Jid);
            menu.Items.Add(new MenuItem { Header = "Room settings", Command = new SimpleCommand(() => _ = RunAsync(() => EditRoomAsync(source.Jid))) });
            if (saved)
                menu.Items.Add(new MenuItem { Header = "Set folder…", Command = new SimpleCommand(() => _ = RunAsync(() => SetRoomFolderAsync(source.Jid))) });
            if (saved && !joined && detail != "Joining…")
                menu.Items.Add(new MenuItem { Header = "Join", IsEnabled = _client.Online, Command = new SimpleCommand(() => _ = JoinRoomAsync(source)) });
            if (joined)
                menu.Items.Add(new MenuItem { Header = "Leave room", Command = new SimpleCommand(() => _ = RunAsync(() => _client.LeaveRoomAsync(source.Jid))) });
            if (saved)
                menu.Items.Add(new MenuItem { Header = "Forget room", Command = new SimpleCommand(() => _ = ForgetRoomAtAsync(source)) });
            if (index > 0)
                menu.Items.Add(new MenuItem { Header = "Move up", Command = new SimpleCommand(() => _ = RunAsync(() => _client.MoveRoomAsync(source.Jid, -1))) });
            if (index >= 0 && index < rooms.Count - 1)
                menu.Items.Add(new MenuItem { Header = "Move down", Command = new SimpleCommand(() => _ = RunAsync(() => _client.MoveRoomAsync(source.Jid, 1))) });
        }
        else if (source.Kind == XmppConversationKind.Direct)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Remove contact",
                IsEnabled = _client.Online,
                Command = new SimpleCommand(() => _ = RunAsync(() => RemoveContactAtAsync(source)))
            });
        }
        else return;

        FlyoutBase.SetAttachedFlyout(button, menu);
        FlyoutBase.ShowAttachedFlyout(button);
    }

    private async Task ForgetRoomAtAsync(XmppConversation source)
    {
        if (!await XmppDialog.ConfirmAsync(this, "Forget room", $"Remove {source.Jid} from saved rooms?")) return;
        SetFeedback(await _client.RemoveRoomAsync(source.Jid));
        if (_selected == source) Select(null);
    }

    internal async Task RemoveContactAtAsync(XmppConversation source)
    {
        if (!await XmppDialog.ConfirmAsync(this, "Remove contact",
                $"Remove {source.Jid} from your contact list?")) return;
        await _client.UpdateContactAsync(source.Jid, "remove");
        var tab = _tabs.Find(item => item.Conversation == source);
        if (tab is not null) CloseTab(tab);
        else if (_selected == source) Select(null);
    }

    private void SidebarRowEntered(object? sender, PointerEventArgs args)
    {
        if (sender is not Control control || control.DataContext is not XmppSidebarRow row) return;
        if (row.Kind is XmppSidebarKind.Group or XmppSidebarKind.Divider) return;
        if (row is not { Source.Kind: XmppConversationKind.Direct }) return;
        _cardCloseTimer.Stop();
        _cardRow = row;
        _cardAnchor = control;
        _cardTimer.Stop();
        _cardTimer.Start();
    }

    private void SidebarRowExited(object? sender, PointerEventArgs args)
    {
        _cardTimer.Stop();
        _cardCloseTimer.Stop();
        _cardCloseTimer.Start();
    }

    private void ContactCardEntered(object? sender, PointerEventArgs args)
    {
        _cardPointerInside = true;
        _cardCloseTimer.Stop();
    }

    private void ContactCardExited(object? sender, PointerEventArgs args)
    {
        _cardPointerInside = false;
        _cardCloseTimer.Stop();
        _cardCloseTimer.Start();
    }

    private void ContactCardCloseTick(object? sender, EventArgs args)
    {
        _cardCloseTimer.Stop();
        if (_cardPointerInside) return;
        _cardRow = null;
        ContactCardPopup.IsOpen = false;
    }

    private async void ContactCardTick(object? sender, EventArgs args)
    {
        _cardTimer.Stop();
        if (_cardRow is not { Source: { Kind: XmppConversationKind.Direct } source } || _cardAnchor is null) return;
        var generation = ++_cardGeneration;
        var contact = _client.Contacts.FirstOrDefault(item => item.Jid == source.Jid);
        ContactCardName.Text = contact?.Name ?? ShortName(source.Jid);
        ContactCardJid.Text = source.Jid;
        ContactCardInitials.Text = Initials(ContactCardName.Text);
        ContactCardAvatar.Source = null;
        ContactCardAvatar.IsVisible = false;
        ContactCardInitials.IsVisible = true;
        var initialShow = contact?.EffectiveShow ?? XmppPresenceShow.Offline;
        ApplyContactCardPresence(contact?.SubscriptionRequested == true, initialShow,
            PresenceDetail(initialShow, null, FormatLastSeen) ?? (initialShow == XmppPresenceShow.Offline ? "Loading…" : null),
            contact?.StatusText);
        ContactCardPopup.PlacementTarget = _cardAnchor;
        ContactCardPopup.IsOpen = true;
        try
        {
            var profile = await _client.EnsureProfileAsync(source.Jid);
            if (generation != _cardGeneration || !ContactCardPopup.IsOpen) return;
            var show = profile.Online
                ? (profile.Show == XmppPresenceShow.Offline ? XmppPresenceShow.Online : profile.Show)
                : XmppPresenceShow.Offline;
            if (contact is not null) show = contact.EffectiveShow;
            var detail = PresenceDetail(show, profile.LastSeenUtc, FormatLastSeen);
            ApplyContactCardPresence(false, show, detail, profile.StatusText);
            if (profile.AvatarBytes is { Length: > 0 } bytes)
            {
                await using var stream = new MemoryStream(bytes);
                ContactCardAvatar.Source = new Bitmap(stream);
                ContactCardAvatar.IsVisible = true;
                ContactCardInitials.IsVisible = false;
            }
        }
        catch
        {
            if (generation == _cardGeneration)
                ApplyContactCardPresence(false, XmppPresenceShow.Offline, null, null);
        }
    }

    private void ApplyContactCardPresence(bool request, XmppPresenceShow show, string? detail, string? statusText)
    {
        if (request)
        {
            ContactCardPresence.Text = "Contact request";
            ContactCardPresence.Foreground = RequestDot;
        }
        else
        {
            ContactCardPresence.Text = string.IsNullOrWhiteSpace(detail)
                ? PresenceLabel(show)
                : $"{PresenceLabel(show)} · {detail}";
            ContactCardPresence.Foreground = PresenceBrush(show);
        }

        if (!string.IsNullOrWhiteSpace(statusText) &&
            !string.Equals(statusText, detail, StringComparison.OrdinalIgnoreCase))
        {
            ContactCardStatus.Text = statusText;
            ContactCardStatus.IsVisible = true;
        }
        else ContactCardStatus.IsVisible = false;
    }

    private void ApplyDisplayFonts()
    {
        var prefs = _client.DisplayPreferences;
        var family = string.IsNullOrWhiteSpace(prefs.FontFamily) ? "Inter" : prefs.FontFamily;
        var size = Math.Clamp(prefs.MessageFontSize, 11, 18);
        var font = new FontFamily(family);
        TranscriptText.FontFamily = font;
        TranscriptText.LayoutFontSize = size;
        TranscriptText.FontSize = XmppLinkText.TranscriptBaseFontSize;
        SplitTranscriptText.FontFamily = font;
        SplitTranscriptText.LayoutFontSize = size;
        SplitTranscriptText.FontSize = XmppLinkText.TranscriptBaseFontSize;
        Compose.FontFamily = font;
        Compose.FontSize = size;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (args.Source is Visual source && IsTabChrome(source)) return;
        BeginMoveDrag(args);
    }

    private bool IsTabChrome(Visual source)
    {
        foreach (var tab in _tabs)
        {
            if (tab.Header is null) continue;
            if (ReferenceEquals(source, tab.Header) || tab.Header.IsVisualAncestorOf(source))
                return true;
        }
        if (ReferenceEquals(source, TabOverflowButton) || TabOverflowButton.IsVisualAncestorOf(source))
            return true;
        return ReferenceEquals(source, SplitTabOverflowButton) || SplitTabOverflowButton.IsVisualAncestorOf(source);
    }

    private void OnMinimize(object? sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs args)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseWindow(object? sender, RoutedEventArgs args) => Close();

    private void OnResizePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (WindowState != WindowState.Normal ||
            !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { Tag: string tag } handle ||
            !Enum.TryParse<WindowEdge>(tag, out var edge)) return;
        EndResize();
        _resizeGesture = new(edge, GetResizePointerPosition(args), Position, Bounds.Size, RenderScaling, handle, args.Pointer);
        handle.PointerCaptureLost += OnResizePointerCaptureLost;
        args.Pointer.Capture(handle);
        args.Handled = true;
    }

    private void OnResizePointerMoved(object? sender, PointerEventArgs args)
    {
        if (_resizeGesture is not { } resize || args.Pointer != resize.Pointer) return;
        var point = GetResizePointerPosition(args);
        var dx = (point.X - resize.PointerStart.X) / resize.Scaling;
        var dy = (point.Y - resize.PointerStart.Y) / resize.Scaling;
        var west = resize.Edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
        var east = resize.Edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
        var north = resize.Edge is WindowEdge.North or WindowEdge.NorthEast or WindowEdge.NorthWest;
        var south = resize.Edge is WindowEdge.South or WindowEdge.SouthEast or WindowEdge.SouthWest;
        var width = Math.Max(MinWidth, resize.WindowSize.Width + (west ? -dx : east ? dx : 0));
        var height = Math.Max(MinHeight, resize.WindowSize.Height + (north ? -dy : south ? dy : 0));
        Width = width;
        Height = height;
        if (west || north)
            Position = new PixelPoint(
                resize.WindowStart.X + (west ? (int)Math.Round((resize.WindowSize.Width - width) * resize.Scaling) : 0),
                resize.WindowStart.Y + (north ? (int)Math.Round((resize.WindowSize.Height - height) * resize.Scaling) : 0));
        args.Handled = true;
    }

    private void OnResizePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_resizeGesture is null || args.Pointer != _resizeGesture.Pointer) return;
        EndResize();
        args.Handled = true;
    }

    private void OnResizePointerCaptureLost(object? sender, PointerCaptureLostEventArgs args) => EndResize();

    private Point GetResizePointerPosition(PointerEventArgs args)
    {
        var origin = this.PointToScreen(default);
        var point = args.GetPosition(this);
        return new Point(origin.X + point.X * RenderScaling, origin.Y + point.Y * RenderScaling);
    }

    private void EndResize()
    {
        if (_resizeGesture is not { } resize) return;
        _resizeGesture = null;
        resize.Handle.PointerCaptureLost -= OnResizePointerCaptureLost;
        if (resize.Pointer.Captured == resize.Handle) resize.Pointer.Capture(null);
        QueueWorkspaceSave();
    }

    private void ApplyChromeState()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "❐" : "□";
        ChromeBorder.CornerRadius = maximized ? default : new CornerRadius(6);
        foreach (var grip in new Control[]
                 {
                     ResizeNorth, ResizeSouth, ResizeWest, ResizeEast,
                     ResizeNorthWest, ResizeNorthEast, ResizeSouthWest, ResizeSouthEast
                 })
            grip.IsVisible = !maximized;
    }

    private void SetFeedback(string? text)
    {
        Feedback.Text = text ?? "";
        Feedback.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private void UpdateOwnPresenceControl()
    {
        var online = _client.Online;
        OwnStatusButton.IsEnabled = online;
        var show = online ? _client.OwnPresencePreferences.Show : XmppPresenceShow.Offline;
        if (show is XmppPresenceShow.Offline && online) show = XmppPresenceShow.Online;
        OwnStatusDot.Background = PresenceBrush(show);
        OwnStatusLabel.Text = online ? PresenceLabel(show) : "Offline";
        OwnStatusLabel.Foreground = online ? Brush.Parse("#E8EEF7") : Brush.Parse("#6B7789");
    }

    private async void SetOwnStatusOnline(object? sender, RoutedEventArgs args) =>
        await RunAsync(() => _client.SetOwnPresenceAsync(XmppPresenceShow.Online));
    private async void SetOwnStatusChat(object? sender, RoutedEventArgs args) =>
        await RunAsync(() => _client.SetOwnPresenceAsync(XmppPresenceShow.Chat));
    private async void SetOwnStatusAway(object? sender, RoutedEventArgs args) =>
        await RunAsync(() => _client.SetOwnPresenceAsync(XmppPresenceShow.Away));
    private async void SetOwnStatusXa(object? sender, RoutedEventArgs args) =>
        await RunAsync(() => _client.SetOwnPresenceAsync(XmppPresenceShow.ExtendedAway));
    private async void SetOwnStatusDnd(object? sender, RoutedEventArgs args) =>
        await RunAsync(() => _client.SetOwnPresenceAsync(XmppPresenceShow.DoNotDisturb));

    private void OccupantDoubleTapped(object? sender, TappedEventArgs args)
    {
        var row = (args.Source as Control)?.GetSelfAndVisualAncestors()
            .Select(visual => visual is Control control ? control.DataContext : null)
            .OfType<XmppOccupantRow>()
            .FirstOrDefault()
            ?? Occupants.SelectedItem as XmppOccupantRow;
        if (row?.Jid is not { Length: > 0 } jid) return;
        OpenDirectChat(jid);
        args.Handled = true;
    }

    private void MessageSenderActivated(object? sender, RoutedEventArgs args)
    {
        if (sender is not XmppLinkText text) return;
        var jid = text.ActivatedSenderJid;
        if (string.IsNullOrWhiteSpace(jid)) return;
        OpenDirectChat(jid);
        args.Handled = true;
    }

    private void OpenDirectChat(string jid)
    {
        var bare = XmppAddresses.Bare(jid);
        if (bare is null) return;
        Select(new(XmppConversationKind.Direct, bare));
    }

    private async void ComposeKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (args.Key == Key.Tab && ReferenceEquals(sender, Compose))
        {
            TryCompleteNick();
            args.Handled = true;
            return;
        }
        if (args.Key == Key.Enter && !args.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            args.Handled = true;
            await (ReferenceEquals(sender, SplitCompose) ? SendSplitAsync() : SendAsync());
        }
    }

    private void ComposeTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (_nickCompleting || _nickMatchIndex < 0) return;
        ResetNickCompletion();
    }

    private bool TryCompleteNick()
    {
        var text = Compose.Text ?? "";
        var caret = Math.Clamp(Compose.CaretIndex, 0, text.Length);
        var cycling = _nickMatchIndex >= 0 && caret == _nickReplaceEnd && _nickTokenStart >= 0;
        var tokenStart = cycling ? _nickTokenStart : caret;
        if (!cycling)
        {
            while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1])) tokenStart--;
        }

        var prefix = cycling ? _nickPrefix : text[tokenStart..caret].TrimEnd(':');
        if (!cycling)
        {
            var candidates = NickCandidates()
                .Where(nick => prefix.Length == 0 || nick.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(nick => nick, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0) return false;
            _nickMatches = candidates;
            _nickMatchIndex = 0;
            _nickPrefix = prefix;
            _nickTokenStart = tokenStart;
        }
        else
        {
            _nickMatchIndex = (_nickMatchIndex + 1) % _nickMatches.Length;
            // Replace from original token start through current caret.
            text = Compose.Text ?? "";
        }

        var pick = _nickMatches[_nickMatchIndex];
        var atStart = text.AsSpan(0, _nickTokenStart).Trim().IsEmpty;
        var insertion = atStart ? pick + ": " : pick + " ";
        var replaceEnd = cycling ? caret : caret;
        if (!cycling)
        {
            while (replaceEnd < text.Length && (text[replaceEnd] == ':' || text[replaceEnd] == ' ')) replaceEnd++;
        }

        _nickCompleting = true;
        try
        {
            Compose.Text = text[.._nickTokenStart] + insertion + text[replaceEnd..];
            Compose.CaretIndex = _nickTokenStart + insertion.Length;
            _nickReplaceEnd = Compose.CaretIndex;
        }
        finally
        {
            _nickCompleting = false;
        }
        return true;
    }

    private IEnumerable<string> NickCandidates()
    {
        if (_selected is { Kind: XmppConversationKind.Room } room)
            return _client.Occupants(room.Jid).Select(occupant => occupant.Nick);
        if (_selected is not { Kind: XmppConversationKind.Direct } direct)
            return [];
        var name = _client.Contacts.FirstOrDefault(contact => contact.Jid == direct.Jid)?.Name;
        var shortName = ShortName(direct.Jid);
        if (!string.IsNullOrWhiteSpace(name) &&
            !name.Equals(shortName, StringComparison.OrdinalIgnoreCase))
            return [name, shortName];
        return [string.IsNullOrWhiteSpace(name) ? shortName : name];
    }

    private void ResetNickCompletion()
    {
        _nickMatches = [];
        _nickMatchIndex = -1;
        _nickPrefix = "";
        _nickTokenStart = -1;
        _nickReplaceEnd = -1;
    }

    internal Task MarkOpenForShutdownAsync()
    {
        _windowOpen = IsVisible;
        return SaveWorkspaceAsync();
    }

    private string FormatLastSeen(DateTimeOffset stamp)
    {
        var local = stamp.ToLocalTime();
        var prefs = _client.DisplayPreferences;
        var time = prefs.Use12HourClock
            ? local.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)
            : local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"{local:yyyy-MM-dd} {time}";
    }

    private static string Initials(string name)
    {
        var parts = name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return string.Concat(parts[0].AsSpan(0, 1), parts[^1].AsSpan(0, 1)).ToUpperInvariant();
    }

    internal static IBrush SenderColor(string sender)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in sender)
                hash = (hash ^ c) * 16777619;
            return SenderPalette[(int)(hash % (uint)SenderPalette.Length)];
        }
    }

    private string FormatMessageTime(DateTimeOffset stamp)
    {
        var local = stamp.ToLocalTime();
        return _client.DisplayPreferences.Use12HourClock
            ? local.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)
            : local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private sealed class ConversationTab(XmppConversation? conversation)
    {
        public XmppConversation? Conversation { get; } = conversation;
        public IReadOnlyList<XmppMessage> Messages { get; set; } = [];
        public string Draft { get; set; } = "";
        public ScrollAnchor? Anchor { get; set; }
        public bool Follow { get; set; } = true;
        public bool NewMessages { get; set; }
        public bool HasEarlier { get; set; }
        public bool HistoryExpanded { get; set; }
        public bool InSplit { get; set; }
        public List<XmppMessage> TopicNotices { get; } = [];
        public Border? Header { get; set; }
        public Button? SelectButton { get; set; }
        public MenuItem? MoveItem { get; set; }
        public TextBlock? Caption { get; set; }
        public Border? UnreadBadge { get; set; }
        public Border? UnreadPip { get; set; }

        public void ResetHistory()
        {
            Messages = [];
            Anchor = null;
            HasEarlier = NewMessages = HistoryExpanded = false;
            Follow = true;
        }
    }

    private sealed record ScrollAnchor(long Id, double Top);

    private sealed class SimpleCommand(Action action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
#pragma warning disable CS0067
        private void OnCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
#pragma warning restore CS0067
    }
}

public enum XmppSidebarKind { Item, Group, Divider }

public sealed record XmppSidebarRow(XmppConversation? Source, bool Fleet, string Label, string Hint,
    string UnreadLabel, IBrush Foreground, FontWeight Weight, XmppSidebarKind Kind = XmppSidebarKind.Item,
    string? GroupKey = null, bool Expanded = true, bool Indent = false, IBrush? Dot = null,
    bool ShowDot = false, bool ShowChevron = false, StreamGeometry? Chevron = null, bool ShowActions = false,
    bool DeepIndent = false)
{
    public bool HasUnread => UnreadLabel.Length > 0;
    public bool IsDivider => Kind == XmppSidebarKind.Divider;
    public bool ShowContent => Kind != XmppSidebarKind.Divider;
    public Thickness IconMargin => ShowChevron ? new(DeepIndent ? 24 : Indent ? 12 : 0, 1, 4, 0) : default;
    public Thickness ContentMargin => ShowDot ? new(DeepIndent ? 24 : Indent ? 12 : 0, 0, 0, 0) : default;
    public string? Tip => string.IsNullOrWhiteSpace(Hint) ? null : Hint;

    public static XmppSidebarRow Divider() =>
        new(null, false, "", "", "", Brushes.Transparent, FontWeight.Normal, XmppSidebarKind.Divider);
}

public sealed record XmppOccupantRow(string Nick, StreamGeometry Icon, IBrush IconBrush, IBrush NickBrush,
    string? Jid = null)
{
    public string? OpenTip => string.IsNullOrWhiteSpace(Jid) ? null : "Double-click to open chat";
}
