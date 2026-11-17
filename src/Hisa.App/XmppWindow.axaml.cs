using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class XmppWindow : Window
{
    internal XmppConversation? SelectedConversation => _selected;

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _closed) return;
        _busy = true;
        SetFeedback(null);
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { SetFeedback(ex.Message); }
        catch (Exception) { SetFeedback("The Jabber action could not be completed."); }
        finally { _busy = false; Invalidate(); }
    }

    private async void EditAccount(object? sender, RoutedEventArgs e) =>
        await RunAsync(() => ShowPreferencesAsync(XmppPreferencesSection.Account));

    private async void ShowPreferences(object? sender, RoutedEventArgs e) =>
        await RunAsync(() => ShowPreferencesAsync(XmppPreferencesSection.Display));

    private async void OnLinkActivated(object? sender, XmppLinkActivatedEventArgs e) =>
        await XmppLinkOpening.OpenAsync(this, _client, e.Url);

    private async void FilterThisSender(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var prefill = _selected?.Kind == XmppConversationKind.Direct ? _selected.Jid : null;
        await ShowPreferencesAsync(XmppPreferencesSection.MessageFilters, prefill);
    });

    private async void Connect(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_client.Account is not { } account)
        {
            await ShowPreferencesAsync(XmppPreferencesSection.Account);
            return;
        }
        SetFeedback(await _client.ConfigureAsync(account.Jid, account.Host, account.Port, account.TlsMode,
            null, account.RememberPassword, account.AutoConnect, true));
        if (_client.Status.StartsWith("Enter your Jabber password", StringComparison.Ordinal))
            await ShowPreferencesAsync(XmppPreferencesSection.Account);
    });

    private async Task ShowAccountAsync() => await ShowPreferencesAsync(XmppPreferencesSection.Account);

    internal async Task ShowPreferencesAsync(XmppPreferencesSection section, string? prefillFilterSender = null)
    {
        var dialog = new XmppPreferencesWindow(_client, this, section, prefillFilterSender)
        {
            Topmost = Topmost
        };
        await dialog.ShowDialog(this);
        if (!dialog.DisplaySaved && string.IsNullOrWhiteSpace(prefillFilterSender)) return;
        _occupantsOverride = false;
        OccupantsToggle.IsChecked = _client.DisplayPreferences.ShowOccupantSidebar;
        ApplyDisplayFonts();
        UpdateConversation();
        RenderTranscript(_active, CaptureAnchor(), _active.Follow);
        if (_splitTab is { } split)
            RenderSplit(split);
        UpdateOwnPresenceControl();
    }

    internal Task PrepareAccountSaveAsync() => SaveWorkspaceAsync();

    internal void SetPreferencesFeedback(string? text) => SetFeedback(text);

    internal Task ReloadWorkspaceAfterAccountChangeAsync() => RestoreWorkspaceAsync();

    private async void EditRoom(object? sender, RoutedEventArgs e) =>
        await RunAsync(() => EditRoomAsync(_selected?.Jid));

    private async void AddRoom(object? sender, RoutedEventArgs e) =>
        await RunAsync(() => EditRoomAsync(null));

    private async Task EditRoomAsync(string? address)
    {
        var account = _client.Account ?? throw new InvalidOperationException("Set up a Jabber account first.");
        var prior = _client.Rooms.FirstOrDefault(room => room.Jid == address);
        var dialog = new XmppDialog(prior is null ? "Join Room" : "Room settings");
        var room = dialog.Text("Room", address?.Split('@', 2)[0]);
        var server = dialog.Text("Server", address is null ? XmppAddresses.ConferenceServer(account.Jid) : XmppAddresses.Domain(address));
        room.IsReadOnly = server.IsReadOnly = prior is not null;
        var handle = dialog.Text("Handle", prior?.Nickname ?? account.Jid.Split('@', 2)[0]);
        var password = dialog.Text("Password", null, "Optional", secret: true);
        var remember = dialog.Check("Remember room password", prior?.HasPassword == true);
        var auto = dialog.Check("Auto-join on connect", prior?.AutoJoin == true);
        dialog.Opened += (_, _) => (prior is null ? room : handle).Focus();
        if (prior is null)
        {
            dialog.Action("Room List", async () =>
            {
                var host = server.Text?.Trim();
                if (string.IsNullOrEmpty(host)) throw new ArgumentException("Enter a conference server.");
                dialog.Status.Text = "Looking for rooms…";
                if (await BrowseRoomsAsync(dialog, host, room.Text) is { } selected)
                {
                    room.Text = selected.Jid.Split('@', 2)[0];
                    server.Text = XmppAddresses.Domain(selected.Jid);
                    handle.Focus();
                }
                dialog.Status.Text = "";
            }).IsEnabled = _client.Online;
        }
        await dialog.OpenAsync(this, _client.Online ? "Join" : "Save room", async () =>
        {
            var jid = XmppAddresses.RoomAddress(room.Text ?? "", server.Text ?? "") ??
                throw new ArgumentException("Enter a room name and a valid conference server.");
            SetFeedback(await _client.SaveRoomAsync(jid, handle.Text ?? "", auto.IsChecked == true,
                password.Text, remember.IsChecked == true, _client.Online));
            password.Text = "";
            Select(new(XmppConversationKind.Room, jid));
            return true;
        }, submitLast: true);
    }

    private async Task<XmppDiscoveredRoom?> BrowseRoomsAsync(Window owner, string host, string? roomName)
    {
        var dialog = new XmppDialog("Room List");
        var rooms = await _client.BrowseAsync(host);
        var rows = rooms.Select(room => new RoomRow(room)).ToArray();
        var items = new ListBox { Height = 235 };
        dialog.Add(items);
        items.ItemsSource = rows;
        items.SelectedItem = rows.FirstOrDefault(row => row.Room.Jid == XmppAddresses.RoomAddress(roomName ?? "", host));
        if (rows.Length == 0)
            dialog.Add(new TextBlock { Text = "This server listed no rooms. You can enter a room name directly." });
        XmppDiscoveredRoom? chosen = null;
        await dialog.OpenAsync(owner, rows.Length == 0 ? "Done" : "Choose room", () =>
        {
            if (rows.Length == 0) return Task.FromResult(true);
            if (items.SelectedItem is not RoomRow row) throw new ArgumentException("Select a room.");
            chosen = row.Room;
            return Task.FromResult(true);
        });
        return chosen;
    }

    private async Task SetRoomFolderAsync(string jid)
    {
        var current = _client.Rooms.FirstOrDefault(room => room.Jid == jid)?.Group;
        var dialog = new XmppDialog("Room folder");
        var name = dialog.Text("Folder", current, "Empty removes the folder");
        await dialog.OpenAsync(this, "Save", async () =>
        {
            await _client.SetRoomGroupAsync(jid, name.Text);
            return true;
        });
    }

    private async void LeaveRoom(object? sender, RoutedEventArgs e) =>
        await RunAsync(async () => { if (_selected is { } conversation) await _client.LeaveRoomAsync(conversation.Jid); });

    private async void ForgetRoom(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_selected is not { } conversation ||
            !await XmppDialog.ConfirmAsync(this, "Forget room", $"Remove {conversation.Jid} from saved rooms?")) return;
        SetFeedback(await _client.RemoveRoomAsync(conversation.Jid));
        Select(null);
    });

    private async void NewChat(object? sender, RoutedEventArgs e) => await RunAsync(() => PromptContactAsync(add: false));
    private async void AddContact(object? sender, RoutedEventArgs e) => await RunAsync(() => PromptContactAsync(add: true));
    private async void RemoveContact(object? sender, RoutedEventArgs e) => await RunAsync(() => ContactActionAsync("remove"));
    private async void AcceptContact(object? sender, RoutedEventArgs e) => await RunAsync(() => ContactActionAsync("accept"));
    private async void DeclineContact(object? sender, RoutedEventArgs e) => await RunAsync(() => ContactActionAsync("decline"));

    private async Task PromptContactAsync(bool add)
    {
        var dialog = new XmppDialog(add ? "Add contact" : "New chat");
        var jid = dialog.Text("Contact address", null, "name@example.org");
        await dialog.OpenAsync(this, add ? "Add" : "Open chat", async () =>
        {
            var address = XmppAddresses.Bare(jid.Text?.Trim());
            if (address is null || jid.Text?.Contains('/') == true || address == _client.Account?.Jid)
                throw new ArgumentException("Enter a valid contact address.");
            if (add) await _client.UpdateContactAsync(address, "add");
            Select(new(XmppConversationKind.Direct, address));
            return true;
        });
    }

    private async Task ContactActionAsync(string action)
    {
        if (_selected is not { } conversation) return;
        if (action == "remove")
        {
            await RemoveContactAtAsync(conversation);
            return;
        }
        await _client.UpdateContactAsync(conversation.Jid, action);
    }

    private sealed record RoomRow(XmppDiscoveredRoom Room) { public override string ToString() => $"{Room.Name} · {Room.Jid}"; }
}
