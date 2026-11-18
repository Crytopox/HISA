using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hisa.Core.Models;
using Hisa.Services.Xmpp;

namespace Hisa.App;

public partial class XmppFeedOverlayWindow : Window
{
    private readonly XmppClientService _client = null!;
    private readonly XmppWindow _owner = null!;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private IReadOnlyList<XmppFeedCardVm> _cards = [];
    private string? _cardsAccount;
    private bool _closed;
    private bool _refreshing;
    private bool _visibleChanged = true;
    private readonly XmppReadFocusDelay _readFocus = new();
    private int _dirty = 1;
    private DateTime _feedAgeRendered;
    private sealed record ResizeGesture(WindowEdge Edge, Point PointerStart, PixelPoint WindowStart,
        Size WindowSize, double Scaling, Control Handle, IPointer Pointer);
    private ResizeGesture? _resizeGesture;

    public XmppFeedOverlayWindow() => InitializeComponent();

    internal IReadOnlyList<XmppFeedCardVm> FeedCards => _cards;

    public XmppFeedOverlayWindow(XmppClientService client, XmppWindow owner) : this()
    {
        _client = client;
        _owner = owner;
        PinToggle.IsChecked = owner.Topmost;
        Topmost = owner.Topmost;
        _client.StateChanged += OnClientStateChanged;
        _client.MessageReceived += OnMessageReceived;
        AddHandler(PointerPressedEvent, XmppLinkText.ClearSelectionOnOutsideClick, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnResizePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnResizePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Activated += (_, _) => UpdateReadFocus();
        Deactivated += (_, _) => _readFocus.Update(null);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty || args.Property == IsVisibleProperty)
                UpdateReadFocus();
        };
        FeedScroll.ScrollChanged += (_, _) => VisibleRowsChanged();
        _refreshTimer.Tick += RefreshTick;
        Opened += (_, _) => _refreshTimer.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _refreshTimer.Stop();
            EndResize();
            _client.StateChanged -= OnClientStateChanged;
            _client.MessageReceived -= OnMessageReceived;
        };
    }

    private void OnClientStateChanged() => Interlocked.Exchange(ref _dirty, 1);

    private void OnMessageReceived(XmppMessage message)
    {
        if (message.ShowInFleetFeed || message.IsFleetPing)
            OnClientStateChanged();
    }

    private async void RefreshTick(object? sender, EventArgs e)
    {
        if (_refreshing || _closed) return;
        var refresh = IsVisible && Interlocked.Exchange(ref _dirty, 0) != 0;
        var ageDue = IsVisible && DateTime.UtcNow - _feedAgeRendered > TimeSpan.FromSeconds(10);
        UpdateReadFocus();
        if (!refresh && !_readFocus.HasDueRows && !ageDue) return;
        _refreshing = true;
        try
        {
            if (refresh) await RefreshAsync();
            if (IsVisible && DateTime.UtcNow - _feedAgeRendered > TimeSpan.FromSeconds(10))
            {
                _feedAgeRendered = DateTime.UtcNow;
                XmppFeedCards.RefreshAges(_cards);
            }
            UpdateReadFocus();
            if (!_closed && _readFocus.HasDueRows) await _readFocus.MarkDueRowsAsync(_client);
        }
        catch
        {
            // Overlay refresh is best-effort.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateReadFocus()
    {
        var focused = !_closed && IsVisible && IsActive && WindowState != WindowState.Minimized;
        _visibleChanged |= _readFocus.Update(focused ? this : null);
        if (!focused || !_visibleChanged || _cardsAccount is not { } account || _client.Account?.Jid != account) return;
        _visibleChanged = false;
        var unread = _cards
            .Where(card => !card.IsCollapseSummary && card.Message.Unread)
            .Select(card => card.Message);
        _readFocus.Observe(account, feed: true, unread);
    }

    private void VisibleRowsChanged()
    {
        _visibleChanged = true;
        UpdateReadFocus();
    }

    private async Task RefreshAsync()
    {
        var account = _client.Account?.Jid;
        var messages = account is null ? [] : await _client.ReadAsync(null, true);
        if (_closed || account != _client.Account?.Jid ||
            (account == _cardsAccount && XmppWindow.SameTranscript(_cards.Select(card => card.Message).ToArray(), messages)))
            return;

        var offset = FeedScroll.Offset;
        _cardsAccount = account;
        _cards = XmppFeedCards.BuildFromMessages(messages, account);
        FeedList.ItemsSource = _cards;
        _owner.UpdateFeedPortraits();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_closed) return;
            FeedScroll.Offset = offset;
            VisibleRowsChanged();
        }, DispatcherPriority.Loaded);
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { Tag: string tag } handle ||
            !Enum.TryParse<WindowEdge>(tag, out var edge))
            return;
        EndResize();
        _resizeGesture = new(edge, GetResizePointerPosition(e), Position, Bounds.Size, RenderScaling, handle, e.Pointer);
        handle.PointerCaptureLost += OnResizePointerCaptureLost;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnResizePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeGesture is not { } resize || e.Pointer != resize.Pointer) return;
        var point = GetResizePointerPosition(e);
        var dx = (point.X - resize.PointerStart.X) / resize.Scaling;
        var dy = (point.Y - resize.PointerStart.Y) / resize.Scaling;
        var east = resize.Edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
        var south = resize.Edge is WindowEdge.South or WindowEdge.SouthEast or WindowEdge.SouthWest;
        Width = Math.Max(MinWidth, resize.WindowSize.Width + (east ? dx : 0));
        Height = Math.Max(MinHeight, resize.WindowSize.Height + (south ? dy : 0));
        e.Handled = true;
    }

    private void OnResizePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizeGesture is null || e.Pointer != _resizeGesture.Pointer) return;
        EndResize();
        e.Handled = true;
    }

    private void OnResizePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndResize();

    private Point GetResizePointerPosition(PointerEventArgs e)
    {
        var origin = this.PointToScreen(default);
        var point = e.GetPosition(this);
        return new Point(origin.X + point.X * RenderScaling, origin.Y + point.Y * RenderScaling);
    }

    private void EndResize()
    {
        if (_resizeGesture is not { } resize) return;
        _resizeGesture = null;
        resize.Handle.PointerCaptureLost -= OnResizePointerCaptureLost;
        if (resize.Pointer.Captured == resize.Handle)
            resize.Pointer.Capture(null);
    }

    private void OnPinChanged(object? sender, RoutedEventArgs e) =>
        Topmost = PinToggle.IsChecked == true;

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenMessage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not XmppMessage message) return;
        FlashButton(button, "Opened");
        _owner.Navigate(message.Conversation, false, message.LocalId);
        _owner.Activate();
    }

    private async void OnCopyRaw(object? sender, RoutedEventArgs e)
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
            // Ignore clipboard failures in overlay.
        }
    }

    private async void OnCardLinkActivated(object? sender, XmppLinkActivatedEventArgs e) =>
        await XmppLinkOpening.OpenAsync(this, _client, e.Url);

    private async void OnOpenMumble(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Uri url } button && await XmppLinkOpening.OpenAsync(this, _client, url))
            FlashButton(button, "Opening…");
    }

    private static void FlashButton(Button button, string flash)
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
}
