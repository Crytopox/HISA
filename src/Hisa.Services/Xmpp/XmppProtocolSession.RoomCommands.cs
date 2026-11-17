using System.Xml.Linq;
using Hisa.Core.Models;

namespace Hisa.Services.Xmpp;

internal sealed partial class XmppProtocolSession
{
    private static readonly XNamespace MucAdmin = "http://jabber.org/protocol/muc#admin";
    private static readonly XNamespace StanzaErrors = "urn:ietf:params:xml:ns:xmpp-stanzas";
    private PendingRoomCommand? _pendingRoomCommand;
    private readonly Dictionary<string, (string Room, long Expires)> _roomInvites = new(StringComparer.Ordinal);

    public async Task SetTopicAsync(string room, string subject)
    {
        var joined = RequireJoinedRoom(room);
        subject = subject.Replace("\r\n", "\n").Replace('\r', '\n');
        var id = NewId();
        var ownAddress = $"{room}/{joined.Nickname}";
        var stanza = new XElement("message", new XAttribute("to", room), new XAttribute("type", "groupchat"),
            new XAttribute("id", id), new XElement("subject", subject));
        await SendRoomCommandAsync(room, id, stanza, "change the room topic", reply =>
            IsClientStanza(reply, "message") && Attr(reply, "type") == "groupchat" &&
            !reply.Elements().Any(element => element.Name.LocalName == "delay" ||
                IsClientStanza(element, "body") || IsClientStanza(element, "thread")) &&
            (Attr(reply, "from") == ownAddress || Attr(reply, "from") == room) &&
            ClientChildValue(reply, "subject") == subject);
    }

    public async Task ChangeNicknameAsync(string room, string nickname)
    {
        var joined = RequireJoinedRoom(room);
        if (joined.Nickname == nickname) return;
        var address = $"{room}/{nickname}";
        var id = NewId();
        var stanza = BuildAvailablePresence(address);
        stanza.SetAttributeValue("id", id);
        await SendRoomCommandAsync(room, id, stanza, "change the room nickname", reply =>
            IsClientStanza(reply, "presence") && Attr(reply, "type") is null && Attr(reply, "from") == address &&
            reply.Element(MucUser + "x")?.Elements(MucUser + "status").Any(status => Attr(status, "code") == "110") == true);
        if (_joined.TryGetValue(room, out var current))
            _joined[room] = followedRooms[room] = current with { Nickname = nickname };
    }

    public void InviteToRoom(string room, string jid, string reason)
    {
        RequireJoinedRoom(room);
        var invite = new XElement(MucUser + "invite", new XAttribute("to", jid));
        if (reason.Length != 0) invite.Add(new XElement(MucUser + "reason", reason));
        var id = NewId();
        stream.Send(new XElement("message", new XAttribute("to", room), new XAttribute("id", id),
            new XElement(MucUser + "x", invite)));
        var now = _clock.ElapsedMilliseconds;
        foreach (var expired in _roomInvites.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToArray())
            _roomInvites.Remove(expired);
        if (_roomInvites.Count >= 32) _roomInvites.Remove(_roomInvites.Keys.First());
        _roomInvites[id] = (room, now + 8_000);
    }

    public Task KickAsync(string room, string nickname, string reason) =>
        UpdateRoomOccupantAsync(room, new XElement(MucAdmin + "item",
            new XAttribute("nick", nickname), new XAttribute("role", "none")), reason, "kick the room occupant");

    public Task BanAsync(string room, string target, string reason)
    {
        RequireJoinedRoom(room);
        var jid = target.Contains('@') && !target.Contains('/') ? XmppAddresses.Bare(target) : null;
        if (jid is null && _occupants.TryGetValue(room, out var occupants) && occupants.TryGetValue(target, out var occupant))
            jid = XmppAddresses.Bare(occupant.Jid);
        if (jid is null)
            throw new ArgumentException("Enter a bare Jabber address or a nickname whose Jabber address the room exposes.");
        return UpdateRoomOccupantAsync(room, new XElement(MucAdmin + "item",
            new XAttribute("jid", jid), new XAttribute("affiliation", "outcast")), reason, "ban the user");
    }

    private async Task UpdateRoomOccupantAsync(string room, XElement item, string reason, string action)
    {
        RequireJoinedRoom(room);
        if (reason.Length != 0) item.Add(new XElement(MucAdmin + "reason", reason));
        var response = await IqAsync("set", room, new XElement(MucAdmin + "query", item), includeErrors: true);
        if (response is null) throw new InvalidOperationException($"The server did not confirm the request to {action}.");
        if (Attr(response, "type") != "result") throw new InvalidOperationException(RoomCommandError(response, action));
    }

    private XmppRoom RequireJoinedRoom(string room) => _joined.GetValueOrDefault(room) ??
        throw new InvalidOperationException("Join the room before running this command.");

    private async Task SendRoomCommandAsync(string room, string id, XElement stanza, string action,
        Func<XElement, bool> succeeded)
    {
        var pending = new PendingRoomCommand(room, id, stanza.Name.LocalName, succeeded);
        _pendingRoomCommand = pending;
        try
        {
            stream.Send(stanza);
            var deadline = _clock.ElapsedMilliseconds + 8_000;
            while (pending.Reply is null && !_lost && !stream.Disconnected && _clock.ElapsedMilliseconds < deadline)
                await PumpAsync();
            if (pending.Reply is null) throw new InvalidOperationException($"The server did not confirm the request to {action}.");
            if (Attr(pending.Reply, "type") == "error")
                throw new InvalidOperationException(RoomCommandError(pending.Reply, action));
        }
        finally { _pendingRoomCommand = null; }
    }

    private void ObserveRoomCommandReply(XElement stanza)
    {
        var id = Attr(stanza, "id");
        var room = XmppAddresses.Bare(Attr(stanza, "from"));
        var error = Attr(stanza, "type") == "error";
        if (_pendingRoomCommand is { Reply: null } pending && room == pending.Room &&
            (pending.Succeeded(stanza) || (error && id == pending.Id && IsClientStanza(stanza, pending.StanzaName))))
            pending.Reply = stanza;

        if (error && id is not null && IsClientStanza(stanza, "message") &&
            _roomInvites.TryGetValue(id, out var invite) && room == invite.Room)
        {
            _roomInvites.Remove(id);
            if (_clock.ElapsedMilliseconds <= invite.Expires)
                client.SetRoomDetail(invite.Room, RoomCommandError(stanza, "invite the user"));
        }
    }

    private static string RoomCommandError(XElement stanza, string action)
    {
        var condition = stanza.Elements().FirstOrDefault(element => IsClientStanza(element, "error"))?
            .Elements().FirstOrDefault(element => element.Name.Namespace == StanzaErrors && element.Name.LocalName != "text")?
            .Name.LocalName;
        return condition switch
        {
            "forbidden" or "not-authorized" or "not-allowed" => $"The server denied permission to {action}.",
            "conflict" => "That room nickname is already in use.",
            null => $"The server rejected the request to {action}.",
            _ => $"The server rejected the request to {action} ({condition})."
        };
    }

    private sealed record PendingRoomCommand(string Room, string Id, string StanzaName, Func<XElement, bool> Succeeded)
    {
        public XElement? Reply { get; set; }
    }
}
