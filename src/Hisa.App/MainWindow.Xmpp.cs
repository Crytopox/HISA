using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hisa.App.Services;
using Hisa.App.ViewModels;
using Hisa.Core.Models;
using Hisa.Services.Xmpp;

namespace Hisa.App;

public partial class MainWindow
{
    private XmppClientService? _xmpp;
    private XmppFeedPortraitLoader? _xmppPortraitLoader;
    private XmppWindow? _xmppWindow;

    internal void AttachXmpp(XmppClientService client, XmppFeedPortraitLoader portraitLoader)
    {
        _xmpp = client;
        _xmppPortraitLoader = portraitLoader;
        if (_alertPopupWindow is not null)
            _alertPopupWindow.XmppClient = client;
        client.AlertReceived += OnXmppAlert;
        _ = RestoreXmppWindowIfNeededAsync();
    }

    private void DetachXmpp()
    {
        if (_xmpp is { } client) client.AlertReceived -= OnXmppAlert;
        if (_xmppWindow is { IsVisible: true } window)
        {
            try { window.MarkOpenForShutdownAsync().GetAwaiter().GetResult(); }
            catch (Exception) { }
        }
        _xmppWindow?.Close();
    }

    private async Task RestoreXmppWindowIfNeededAsync()
    {
        if (_xmpp is null || _isClosingApp) return;
        try
        {
            await _xmpp.ReadyAsync();
            if (_xmpp.Account is not { } account) return;
            var workspace = await _xmpp.LoadWorkspaceAsync(account.Jid);
            if (workspace?.Open == true)
                Dispatcher.UIThread.Post(() => ShowXmppWindow());
        }
        catch (Exception)
        {
            // Startup restore is best-effort.
        }
    }

    private void OnOpenXmppClicked(object? sender, RoutedEventArgs e) => ShowXmppWindow();

    private void ShowXmppWindow(XmppConversation? conversation = null, bool fleetPing = false)
    {
        if (_xmpp is null || _isClosingApp) return;
        if (_xmppWindow is null)
        {
            _xmppWindow = new(_xmpp, _xmppPortraitLoader);
            _xmppWindow.Closed += (_, _) => _xmppWindow = null;
        }
        if (conversation is not null) _xmppWindow.Navigate(conversation, fleetPing);
        if (_xmppWindow.WindowState == WindowState.Minimized) _xmppWindow.WindowState = WindowState.Normal;
        _xmppWindow.Show();
        _xmppWindow.Activate();
    }

    private void OnXmppAlert(XmppAlert alert) => Dispatcher.UIThread.Post(() =>
    {
        if (_isClosingApp) return;
        if (alert.Sound is not null) AlertSoundPlayer.Play(alert.Sound, alert.Volume);
        if (!alert.Popup || !_alertPopupSettings.Enabled) return;
        var message = alert.Message;
        var preview = message.Body.Length <= 240 ? message.Body : $"{message.Body[..240]}…";
        var title = !string.IsNullOrWhiteSpace(message.FeedTitle) ? message.FeedTitle :
            message.IsFleetPing ? "Fleet ping · Directorbot" :
            message.ShowInFleetFeed ? $"Alert · {message.Conversation.Jid}" : message.Conversation.Jid;
        var feedCard = XmppFeedCards.CreateMessageCard(message, _xmpp?.Account?.Jid);
        if (feedCard.IsFleet)
            _xmppPortraitLoader?.Ensure([feedCard]);
        ShowAlertPopupCard(new AlertPopupCard
        {
            Title = title,
            Details = preview,
            TimestampLabel = message.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(_alertPopupSettings.AutoDismissSeconds),
            OpenConversation = () => ShowXmppWindow(message.Conversation, message.IsFleetPing),
            FeedCard = feedCard.IsFleet ? feedCard : null
        });
    });
}

