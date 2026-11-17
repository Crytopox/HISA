using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using System.Xml.Linq;
using Hisa.Core.Models;

namespace Hisa.Services.Xmpp;

internal sealed partial class XmppProtocolSession(
    XmppClientService client, LibstropheSession stream, string account,
    Dictionary<string, XmppRoom> followedRooms, CancellationToken token) : IDisposable
{
    private static readonly XNamespace ClientNamespace = "jabber:client";
    private static readonly XNamespace Roster = "jabber:iq:roster";
    private static readonly XNamespace Ping = "urn:xmpp:ping";
    private static readonly XNamespace Muc = "http://jabber.org/protocol/muc";
    private static readonly XNamespace MucUser = "http://jabber.org/protocol/muc#user";
    private static readonly XNamespace DiscoInfo = "http://jabber.org/protocol/disco#info";
    private static readonly XNamespace DiscoItems = "http://jabber.org/protocol/disco#items";
    private static readonly XNamespace Mam = "urn:xmpp:mam:2";
    private static readonly XNamespace Forms = "jabber:x:data";
    private static readonly XNamespace Rsm = "http://jabber.org/protocol/rsm";
    private static readonly XNamespace Delay = "urn:xmpp:delay";
    private static readonly XNamespace Sid = "urn:xmpp:sid:0";
    private static readonly XNamespace Forward = "urn:xmpp:forward:0";
    private static readonly XNamespace Carbons = "urn:xmpp:carbons:2";
    private static readonly XNamespace VCardTemp = XmppProfileParsing.VCard;
    private static readonly XNamespace LastActivity = XmppProfileParsing.LastActivity;
    private readonly Dictionary<string, XmppContact> _contacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, ResourcePresence>> _presentResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, XmppOccupant>> _occupants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _wentOfflineAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XmppRoom> _joining = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XmppRoom> _joined = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _joinStarted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _roomReadAtJoin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<(string Sender, string Body, long Stamp), int>> _joinCopies = new(StringComparer.Ordinal);
    private readonly Queue<ArchiveFetch> _archiveQueue = new();
    private readonly HashSet<string> _archived = new(StringComparer.Ordinal);
    private readonly Queue<RoomArchive> _roomArchives = new();
    private RoomArchive? _roomArchive;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastInbound;
    private string? _pingId;
    private long _pingStarted;
    private string? _replayBoundary;
    private bool _lost;
    private string? _waitingId;
    private string? _waitingFrom;
    private XElement? _iqResult;
    private string? _archiveQuery;
    private string? _archiveJid;
    private bool _partialArchive;
    private DateTimeOffset _baseline;
    private bool _initialLogin;
    private string Domain => XmppAddresses.Domain(account);

    public async Task RunAsync(ChannelReader<XmppClientService.Request> requests)
    {
        while (!stream.Connected && !stream.Disconnected && _clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            token.ThrowIfCancellationRequested();
            stream.Poll();
        }
        if (!stream.Connected || stream.Disconnected) return;

        (_baseline, _initialLogin) = await client.BaselineAsync(account, token);

        if (stream.Resumed)
        {
            foreach (var (jid, room) in followedRooms)
            {
                _joined.Add(jid, room);
                _roomReadAtJoin[jid] = await client.RoomReadPositionAsync(account, jid, token);
                QueueRoomArchive(jid);
            }
            // A server round trip creates a boundary after replayed stream stanzas.
            _replayBoundary = StartPing();
        }
        else
        {
            BroadcastOwnPresence();
            foreach (var room in client.Rooms.Where(room => room.AutoJoin).Concat(followedRooms.Values)
                .DistinctBy(room => room.Jid).ToArray())
                await JoinAsync(room);
        }

        PublishPresence();
        PublishStatus();
        if (await IqAsync("get", null, new XElement(Roster + "query")) is { } roster)
            ApplyRoster(roster.Element(Roster + "query"), push: false);
        await IqAsync("set", null, new XElement(Carbons + "enable"));
        if (!stream.Resumed && !_initialLogin) ScheduleArchive(account);

        while (!stream.Disconnected && !_lost)
        {
            await PumpAsync();
            if (_lost || stream.Disconnected) break;
            if (requests.TryRead(out var request))
            {
                try { request.Result.TrySetResult(await request.Handle(this)); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    request.Result.TrySetException(ex);
                }
                catch
                {
                    request.Result.TrySetException(new InvalidOperationException("The Jabber request could not complete."));
                    throw;
                }
            }
            await AdvanceRoomArchiveAsync();
            if (_archiveQueue.TryDequeue(out var archive))
                await CatchUpAsync(archive);
        }
    }

    private async Task PumpAsync()
    {
        token.ThrowIfCancellationRequested();
        stream.Poll();
        while (stream.Incoming.TryDequeue(out var stanza))
        {
            _lastInbound = _clock.ElapsedMilliseconds;
            if (IsClientStanza(stanza, "message")) await HandleMessageAsync(stanza);
            else if (IsClientStanza(stanza, "presence")) HandlePresence(stanza);
            else if (IsClientStanza(stanza, "iq")) HandleIq(stanza);
            ObserveRoomCommandReply(stanza);
        }

        var now = _clock.ElapsedMilliseconds;
        foreach (var jid in _joinStarted.Where(pair => now - pair.Value > 15_000).Select(pair => pair.Key).ToArray())
        {
            _joinStarted.Remove(jid);
            _joining.Remove(jid);
            _roomReadAtJoin.Remove(jid);
            _joinCopies.Remove(jid);
            client.SetRoomDetail(jid, "Join timed out. Check the room settings and try again.");
        }
        if (stream.Disconnected) return;
        if (_pingId is not null && now - _pingStarted >= 10_000) _lost = true;
        else if (_pingId is null && now - _lastInbound >= 30_000) StartPing();
    }

    public async Task JoinAsync(XmppRoom room)
    {
        if (_joined.TryGetValue(room.Jid, out var joined) && joined.Nickname == room.Nickname) return;
        if (_joining.TryGetValue(room.Jid, out var joining) && joining.Nickname == room.Nickname) return;
        if (_joined.ContainsKey(room.Jid) || _joining.ContainsKey(room.Jid)) Leave(room.Jid);
        var password = client.RoomPassword(account, room);
        if (room.HasPassword && password is null)
        {
            client.SetRoomDetail(room.Jid, "Enter the password in Room settings to join.");
            return;
        }
        _roomReadAtJoin[room.Jid] = await client.RoomReadPositionAsync(account, room.Jid, token);
        _joinCopies[room.Jid] = [];
        var extension = new XElement(Muc + "x", new XElement(Muc + "history", new XAttribute("maxstanzas", 100)));
        if (!string.IsNullOrEmpty(password)) extension.Add(new XElement(Muc + "password", password));
        stream.Send(BuildAvailablePresence($"{room.Jid}/{room.Nickname}", extension));
        _joining[room.Jid] = room;
        _joinStarted[room.Jid] = _clock.ElapsedMilliseconds;
        client.SetRoomDetail(room.Jid, "Joining…");
    }

    public void Leave(string jid)
    {
        if (_joined.TryGetValue(jid, out var room) || _joining.TryGetValue(jid, out room))
            stream.Send(new XElement("presence", new XAttribute("to", $"{jid}/{room.Nickname}"),
                new XAttribute("type", "unavailable")));
        _joining.Remove(jid);
        _joined.Remove(jid);
        _joinStarted.Remove(jid);
        _roomReadAtJoin.Remove(jid);
        _joinCopies.Remove(jid);
        _occupants.Remove(jid);
        followedRooms.Remove(jid);
        CancelRoomArchive(jid);
        client.ClearOccupants(jid);
        client.SetRoomDetail(jid, null);
        PublishPresence();
    }

    public async Task SendAsync(XmppConversation conversation, string body)
    {
        var room = conversation.Kind == XmppConversationKind.Room;
        if (room && !_joined.ContainsKey(conversation.Jid))
            throw new InvalidOperationException("Join the room before sending messages.");
        if (!room && conversation.Jid == account) throw new ArgumentException("Choose another contact.");
        var id = NewId();
        stream.Send(new XElement("message", new XAttribute("to", conversation.Jid),
            new XAttribute("type", room ? "groupchat" : "chat"), new XAttribute("id", id),
            new XElement("body", body), new XElement(Sid + "origin-id", new XAttribute("id", id))));
        // Room messages appear when the server echoes them. For a DM we record
        // local submission and never interpret it as delivery to the recipient.
        if (!room)
            await client.RecordAsync(account, new(conversation, account, body, DateTimeOffset.UtcNow,
                true, false, false, false, [ScopedKey(conversation, "origin", account, id)]), null, token);
    }

    public async Task UpdateContactAsync(string jid, string action)
    {
        if (jid == account) throw new ArgumentException("Choose another contact.");
        if (action is "add" or "remove")
        {
            var item = new XElement(Roster + "item", new XAttribute("jid", jid));
            if (action == "remove") item.Add(new XAttribute("subscription", "remove"));
            else if (_contacts.TryGetValue(jid, out var existing))
            {
                if (!string.Equals(existing.Name, jid, StringComparison.Ordinal))
                    item.Add(new XAttribute("name", existing.Name));
                foreach (var group in existing.RosterGroups)
                    item.Add(new XElement(Roster + "group", group));
            }
            if (await IqAsync("set", null, new XElement(Roster + "query", item)) is null)
                throw new InvalidOperationException("The server did not accept the roster change.");
            ApplyRoster(new XElement(Roster + "query", item), push: true);
            if (action == "add") SendPresence(jid, "subscribe");
        }
        else if (action is "accept" or "decline")
        {
            SendPresence(jid, action == "accept" ? "subscribed" : "unsubscribed");
            if (_contacts.TryGetValue(jid, out var contact))
                _contacts[jid] = contact with { SubscriptionRequested = false };
            PublishPresence();
        }
        else throw new ArgumentException("Unknown contact action.");
    }

    public async Task<IReadOnlyList<XmppDiscoveredRoom>> BrowseAsync(string? service)
    {
        var services = new List<string>();
        if (service is not null)
        {
            if (Uri.CheckHostName(service) != UriHostNameType.Dns) throw new ArgumentException("Enter a valid conference host.");
            services.Add(new IdnMapping().GetAscii(service).ToLowerInvariant());
        }
        else
        {
            var result = await IqAsync("get", Domain, new XElement(DiscoItems + "query"));
            foreach (var candidate in result?.Element(DiscoItems + "query")?.Elements(DiscoItems + "item")
                         .Select(item => Attr(item, "jid")).Where(jid => Uri.CheckHostName(jid) == UriHostNameType.Dns)
                         .Distinct().Take(16) ?? [])
            {
                var info = await IqAsync("get", candidate, new XElement(DiscoInfo + "query"));
                if (info?.Element(DiscoInfo + "query")?.Elements(DiscoInfo + "feature")
                        .Any(feature => Attr(feature, "var") == Muc.NamespaceName) == true)
                    services.Add(candidate!);
                if (_lost || stream.Disconnected) break;
            }
        }
        var rooms = new Dictionary<string, XmppDiscoveredRoom>(StringComparer.Ordinal);
        foreach (var host in services)
        {
            var response = await IqAsync("get", host, new XElement(DiscoItems + "query"));
            foreach (var item in response?.Element(DiscoItems + "query")?.Elements(DiscoItems + "item") ?? [])
            {
                var address = XmppAddresses.Bare(Attr(item, "jid"));
                if (address is not null && XmppAddresses.Domain(address) == host)
                    rooms.TryAdd(address, new(address, Attr(item, "name") ?? address));
            }
        }
        return rooms.Values.OrderBy(room => room.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ScheduleArchive(string jid)
    {
        if (_archived.Add(jid)) _archiveQueue.Enqueue(new ArchiveFetch(jid));
    }

    private void QueueRoomArchive(string jid)
    {
        CancelRoomArchive(jid);
        _roomArchives.Enqueue(new RoomArchive(jid, DateTimeOffset.UtcNow));
        client.SetRoomDetail(jid, "Loading room history…");
    }

    private void CancelRoomArchive(string jid)
    {
        if (_roomArchive?.Jid == jid) _roomArchive = null;
        var remaining = _roomArchives.Count;
        while (remaining-- > 0 && _roomArchives.TryDequeue(out var request))
            if (request.Jid != jid) _roomArchives.Enqueue(request);
    }

    private async Task AdvanceRoomArchiveAsync()
    {
        if (_roomArchive is not null)
        {
            if (_clock.ElapsedMilliseconds < _roomArchive.Deadline) return;
            client.SetRoomDetail(_roomArchive.Jid, null);
            _roomArchive = null;
        }
        if (!_roomArchives.TryDequeue(out var archive) || !_joined.ContainsKey(archive.Jid)) return;
        archive.IqId = NewId();
        archive.Deadline = _clock.ElapsedMilliseconds + 8000;
        _roomArchive = archive;
        if (!archive.Discovered)
        {
            stream.Send(BuildIq("get", archive.Jid, archive.IqId, new XElement(DiscoInfo + "query")));
            return;
        }

        archive.QueryId = NewId();
        var boundary = await client.HistoryClearBoundaryAsync(account, token);
        if (boundary >= archive.End)
        {
            _roomArchive = null;
            client.SetRoomDetail(archive.Jid, null);
            return;
        }
        var fields = new XElement(Forms + "x", new XAttribute("type", "submit"),
            FormField("FORM_TYPE", Mam.NamespaceName, hidden: true),
            FormField("end", archive.End.ToString("O", CultureInfo.InvariantCulture)),
            boundary == DateTimeOffset.MinValue ? null :
                FormField("start", boundary.AddMilliseconds(1).ToString("O", CultureInfo.InvariantCulture)));
        stream.Send(BuildIq("set", archive.Jid, archive.IqId, new XElement(Mam + "query",
            new XAttribute("queryid", archive.QueryId), fields,
            new XElement(Rsm + "set", new XElement(Rsm + "max", 50), new XElement(Rsm + "before", archive.Before)))));
    }

    private void FinishRoomArchivePage(RoomArchive archive, XElement result)
    {
        _roomArchive = null;
        if (!archive.Discovered)
        {
            if (HasMam(result))
            {
                archive.Discovered = true;
                _roomArchives.Enqueue(archive);
            }
            else client.SetRoomDetail(archive.Jid, null);
            return;
        }

        archive.QueryId = null;
        var finish = result.Element(Mam + "fin");
        if (Attr(finish, "complete") is "true" or "1")
            client.SetRoomDetail(archive.Jid, null);
        else if (finish?.Element(Rsm + "set")?.Element(Rsm + "first")?.Value is { Length: > 0 and <= 1024 } previous &&
            archive.Cursors.Add(previous))
        {
            archive.Before = previous;
            _roomArchives.Enqueue(archive);
        }
        else client.SetRoomDetail(archive.Jid, null);
    }

    private async Task CatchUpAsync(ArchiveFetch archive)
    {
        var jid = archive.Jid;
        var isRoom = jid != account;
        var target = isRoom ? jid : null;
        if (archive.Start is null)
        {
            var supported = await IqAsync("get", target ?? Domain, new XElement(DiscoInfo + "query"));
            // Some servers advertise account archive support on the account JID only.
            if (!HasMam(supported) && !isRoom)
                supported = await IqAsync("get", account, new XElement(DiscoInfo + "query"));
            if (!HasMam(supported))
            {
                _partialArchive = true;
                PublishStatus();
                return;
            }
            archive.End = DateTimeOffset.UtcNow;
            archive.Start = await client.ArchiveStartAsync(account, jid, _baseline, token);
        }

        _archiveJid = jid;
        try
        {
            _archiveQuery = NewId();
            var request = new XElement(Mam + "query", new XAttribute("queryid", _archiveQuery),
                new XElement(Forms + "x", new XAttribute("type", "submit"),
                    FormField("FORM_TYPE", Mam.NamespaceName, hidden: true),
                    FormField("start", archive.Start.Value.ToString("O", CultureInfo.InvariantCulture)),
                    FormField("end", archive.End.ToString("O", CultureInfo.InvariantCulture))),
                new XElement(Rsm + "set", new XElement(Rsm + "max", 50),
                    archive.After is null ? null : new XElement(Rsm + "after", archive.After)));
            var result = await IqAsync("set", target, request);
            var finish = result?.Element(Mam + "fin");
            if (Attr(finish, "complete") is "true" or "1")
                await client.SaveArchivePositionAsync(account, jid, archive.End, token);
            else if (finish?.Element(Rsm + "set")?.Element(Rsm + "last")?.Value is { Length: > 0 and <= 1024 } next &&
                archive.Cursors.Count < 5000 && archive.Cursors.Add(next))
            {
                archive.After = next;
                _archiveQueue.Enqueue(archive);
            }
            else
            {
                _partialArchive = true;
                PublishStatus();
            }
        }
        finally { _archiveJid = _archiveQuery = null; }
    }

    private static bool HasMam(XElement? response) =>
        response?.Element(DiscoInfo + "query")?.Elements(DiscoInfo + "feature")
            .Any(feature => Attr(feature, "var") == Mam.NamespaceName) == true;

    private async Task HandleMessageAsync(XElement stanza)
    {
        if (stanza.Elements().Any(IsCarbonEnvelope))
        {
            if (TryUnwrapCarbon(stanza, account) is { } carbon)
                await RecordMessageAsync(carbon.Message, recovered: false, carbon.Delay, null, null);
            return;
        }

        var archive = stanza.Element(Mam + "result");
        if (archive is not null)
        {
            if (_roomArchive is { QueryId: not null } roomArchive &&
                Attr(archive, "queryid") == roomArchive.QueryId)
            {
                if (!_joined.ContainsKey(roomArchive.Jid) || Attr(stanza, "from") != roomArchive.Jid) return;
                var roomForwarded = archive.Element(Forward + "forwarded");
                var roomMessage = roomForwarded?.Elements().FirstOrDefault(element => IsClientStanza(element, "message"));
                if (roomMessage is not null && roomForwarded!.Element(Delay + "delay") is { } archiveDelay)
                    await RecordMessageAsync(roomMessage, true, archiveDelay, roomArchive.Jid, Attr(archive, "id"));
                return;
            }
            if (_archiveQuery is null || Attr(archive, "queryid") != _archiveQuery || _archiveJid is null) return;
            var archiveSender = Attr(stanza, "from");
            if (_archiveJid == account ? !IsOwnArchive(archiveSender) : archiveSender != _archiveJid) return;
            var forwarded = archive.Element(Forward + "forwarded");
            var inner = forwarded?.Elements().FirstOrDefault(element => IsClientStanza(element, "message"));
            if (inner is not null)
                await RecordMessageAsync(inner, true, forwarded!.Element(Delay + "delay"), _archiveJid, Attr(archive, "id"));
            return;
        }

        await RecordMessageAsync(stanza, _replayBoundary is not null, stanza.Element(Delay + "delay")
            ?? stanza.Elements().FirstOrDefault(child => child.Name.LocalName == "delay"), null, null);
    }

    private static bool IsCarbonEnvelope(XElement child) =>
        child.Name.LocalName is "received" or "sent" &&
        (child.Name.Namespace == Carbons || child.Elements().Any(element => element.Name.LocalName == "forwarded"));

    internal static (XElement Message, XElement? Delay)? TryUnwrapCarbon(XElement stanza, string account)
    {
        if (!string.Equals(Attr(stanza, "from"), account, StringComparison.Ordinal)) return null;

        var wrappers = stanza.Elements().Where(IsCarbonEnvelope).Take(2).ToArray();
        if (wrappers.Length != 1 || wrappers[0].Name.Namespace != Carbons) return null;
        var forwarded = wrappers[0].Elements(Forward + "forwarded").Take(2).ToArray();
        if (forwarded.Length != 1) return null;
        var messages = forwarded[0].Elements(ClientNamespace + "message").Take(2).ToArray();
        return messages.Length == 1 ? (messages[0], forwarded[0].Element(Delay + "delay")) : null;
    }

    private static string? ExtractXhtml(XElement message)
    {
        var html = message.Elements().FirstOrDefault(child =>
            child.Name.LocalName == "html" &&
            child.Name.NamespaceName.Contains("xhtml-im", StringComparison.OrdinalIgnoreCase));
        var body = html?.Elements().FirstOrDefault(child => child.Name.LocalName == "body");
        if (body is null) return null;
        var fragment = string.Concat(body.Nodes().Select(node => node.ToString()));
        return fragment.Length is > 0 and <= 64 * 1024 ? fragment : null;
    }

    private async Task RecordMessageAsync(XElement message, bool recovered, XElement? delay, string? archive, string? archiveId)
    {
        var type = Attr(message, "type");
        if (type is not (null or "normal" or "chat" or "groupchat")) return;
        var bodyElement = message.Elements().FirstOrDefault(child => IsClientStanza(child, "body"));
        var fullFrom = Attr(message, "from");
        var from = XmppAddresses.Bare(fullFrom);
        if (from is null || fullFrom is null) return;
        var to = XmppAddresses.Bare(Attr(message, "to"));
        var kind = type == "groupchat" ? XmppConversationKind.Room : XmppConversationKind.Direct;
        XmppRoom? room = null;
        bool outgoing;
        string address;
        if (kind == XmppConversationKind.Room)
        {
            if (!_joined.TryGetValue(from, out room) && !_joining.TryGetValue(from, out room)) return;
            if (archive is not null && archive != from) return;
            var subjectElement = message.Elements().FirstOrDefault(child => IsClientStanza(child, "subject"));
            if (archive is null && subjectElement is { } subject && subject.Value.Length <= 64 * 1024)
            {
                DateTimeOffset? subjectStamp = null;
                if (delay is not null &&
                    DateTimeOffset.TryParse(Attr(delay, "stamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var subjectAt) &&
                    subjectAt <= DateTimeOffset.UtcNow)
                    subjectStamp = subjectAt;
                var slash = fullFrom.IndexOf('/');
                var nick = slash >= 0 ? fullFrom[(slash + 1)..] : null;
                var topic = subject.Value.Length == 0 ? null : subject.Value;
                var subjectOnly = bodyElement is null && !message.Elements().Any(child => IsClientStanza(child, "thread"));
                client.SetRoomTopic(from, topic, subjectStamp, nick, announce: subjectOnly);
                if (subjectOnly)
                {
                    _joinCopies.Remove(from);
                    return;
                }
            }
            var separator = fullFrom.IndexOf('/');
            outgoing = separator >= 0 && fullFrom[(separator + 1)..] == room.Nickname;
            address = from;
        }
        else
        {
            if (archive is not null && archive != account) return;
            if (from == account && to is not null && to != account) { address = to; outgoing = true; }
            else
            {
                if (to is not null && to != account) return;
                // Room occupant private messages cannot be represented as a bare-JID conversation.
                if (_joined.ContainsKey(from) || _joining.ContainsKey(from)) return;
                address = from;
                outgoing = false;
            }
        }

        var body = bodyElement?.Value;
        if (string.IsNullOrEmpty(body) || body.Length > 64 * 1024) return;
        var html = ExtractXhtml(message);
        if (kind == XmppConversationKind.Direct && delay is not null && _initialLogin && archive is null &&
            DateTimeOffset.TryParse(Attr(delay, "stamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var old) &&
            old < _baseline) return;

        var now = DateTimeOffset.UtcNow;
        var validDelay = DateTimeOffset.TryParse(Attr(delay, "stamp"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var received) && received <= now;
        if (kind == XmppConversationKind.Room && delay is not null && !validDelay) return;
        var stamp = validDelay ? received : now;
        recovered |= delay is not null;
        var conversation = new XmppConversation(kind, address);
        var authority = kind == XmppConversationKind.Room ? address : account;
        var keys = new List<string>();
        foreach (var stable in message.Elements(Sid + "stanza-id"))
        {
            if (Attr(stable, "by") == authority && Attr(stable, "id") is { Length: > 0 and <= 1024 } stableId)
                keys.Add(ScopedKey(conversation, "stable", authority, stableId));
        }
        if (archiveId is { Length: > 0 and <= 1024 })
            keys.Add(ScopedKey(conversation, "stable", authority, archiveId));
        var origin = Attr(message.Element(Sid + "origin-id"), "id");
        var senderScope = kind == XmppConversationKind.Room || !outgoing ? fullFrom : account;
        if (origin is { Length: > 0 and <= 1024 })
            keys.Add(ScopedKey(conversation, "origin", senderScope, origin));
        var messageId = Attr(message, "id");
        if (messageId is { Length: > 0 and <= 1024 })
            keys.Add(ScopedKey(conversation, "message", senderScope, messageId));
        var delivery = kind == XmppConversationKind.Room && recovered
            ? archive is null ? XmppHistoryDelivery.RoomJoin : XmppHistoryDelivery.RoomArchive
            : XmppHistoryDelivery.None;
        int? joinOrdinal = null;
        if (delivery == XmppHistoryDelivery.RoomJoin && keys.Count == 0 &&
            _joinCopies.TryGetValue(address, out var copies))
        {
            var copy = (fullFrom, body, stamp.ToUnixTimeMilliseconds());
            if (copies.TryGetValue(copy, out var occurrence) || copies.Count < 100)
                joinOrdinal = copies[copy] = occurrence + 1;
        }
        var mentionNick = room?.Nickname;
        if (mentionNick is null && kind == XmppConversationKind.Direct)
        {
            var separator = account.IndexOf('@');
            mentionNick = separator > 0 ? account[..separator] : account;
        }
        await client.RecordAsync(account, new(conversation, fullFrom, body, stamp, outgoing, recovered, false,
            !outgoing, keys.Distinct().ToArray()) { JoinHistoryOrdinal = joinOrdinal, Html = html }, mentionNick, token, delivery,
            kind == XmppConversationKind.Room ? _roomReadAtJoin.GetValueOrDefault(address, DateTimeOffset.MinValue) : null);
    }

    public async Task<XmppContactProfile> FetchProfileAsync(string jid)
    {
        var contact = _contacts.GetValueOrDefault(jid);
        var online = contact?.Online == true ||
            (_presentResources.TryGetValue(jid, out var live) && live.Count > 0);
        var show = contact?.EffectiveShow ?? (online ? XmppPresenceShow.Online : XmppPresenceShow.Offline);
        DateTimeOffset? lastSeen = online ? DateTimeOffset.UtcNow : null;
        string? statusText = online ? contact?.StatusText : null;
        if (!online)
        {
            var last = await IqAsync("get", jid, new XElement(LastActivity + "query"));
            var query = last?.Element(LastActivity + "query");
            lastSeen = XmppProfileParsing.LastSeenFromSeconds(Attr(query, "seconds"), DateTimeOffset.UtcNow)
                ?? (_wentOfflineAt.TryGetValue(jid, out var wentOffline) ? wentOffline : null);
            statusText = string.IsNullOrWhiteSpace(query?.Value) ? null : query!.Value.Trim();
        }

        byte[]? avatar = null;
        string? avatarType = null;
        var vcard = await IqAsync("get", jid, new XElement(VCardTemp + "vCard"));
        if (vcard is not null)
            (avatar, avatarType) = XmppProfileParsing.ReadVCardPhoto(
                vcard.Element(VCardTemp + "vCard") ?? vcard.Elements()
                    .FirstOrDefault(element => element.Name.LocalName.Equals("vCard", StringComparison.OrdinalIgnoreCase)));

        return new(jid, avatar, avatarType, lastSeen, statusText, online, show);
    }

    private void HandlePresence(XElement stanza)
    {
        var fullFrom = Attr(stanza, "from");
        var from = XmppAddresses.Bare(fullFrom);
        if (from is null || fullFrom is null || from == account) return;
        var type = Attr(stanza, "type");
        if (_joining.TryGetValue(from, out var room) || _joined.TryGetValue(from, out room))
        {
            var extension = stanza.Element(MucUser + "x");
            var self = extension?.Elements(MucUser + "status").Any(status => Attr(status, "code") == "110") == true;
            if (type == "error" && _joining.ContainsKey(from) &&
                (fullFrom == $"{from}/{room.Nickname}" || fullFrom == from))
            {
                _joining.Remove(from);
                _joinStarted.Remove(from);
                _roomReadAtJoin.Remove(from);
                _joinCopies.Remove(from);
                _occupants.Remove(from);
                client.ClearOccupants(from);
                client.SetRoomDetail(from, "Could not join. Check the room settings, access, and password.");
            }
            else if (type == "unavailable" && _joined.ContainsKey(from) && fullFrom == $"{from}/{room.Nickname}" &&
                extension?.Elements(MucUser + "status").Any(status => Attr(status, "code") == "303") == true &&
                Attr(extension.Element(MucUser + "item"), "nick") is { Length: > 0 } newNick)
            {
                _joined[from] = followedRooms[from] = room with { Nickname = newNick };
            }
            else if (self && type == "unavailable")
            {
                _joined.Remove(from);
                _roomReadAtJoin.Remove(from);
                _joinCopies.Remove(from);
                _occupants.Remove(from);
                followedRooms.Remove(from);
                CancelRoomArchive(from);
                client.ClearOccupants(from);
                client.SetRoomDetail(from, null);
                PublishPresence();
            }
            else if (self && type is null && _joining.Remove(from))
            {
                _joinStarted.Remove(from);
                var separator = fullFrom.IndexOf('/');
                if (separator >= 0) room = room with { Nickname = fullFrom[(separator + 1)..] };
                _joined[from] = followedRooms[from] = room;
                PublishStatus();
                QueueRoomArchive(from);
                PublishPresence();
            }

            ApplyOccupantPresence(from, fullFrom, type, extension);
            return;
        }

        if (type == "subscribe")
        {
            var existing = _contacts.GetValueOrDefault(from, new(from, from, false));
            _contacts[from] = existing with { SubscriptionRequested = true };
            PublishPresence();
            return;
        }
        if (type is not (null or "unavailable")) return;
        if (!_presentResources.TryGetValue(from, out var resources))
        {
            if (type is not null || _presentResources.Count >= 5000) return;
            _presentResources[from] = resources = new(StringComparer.Ordinal);
        }
        if (type is null && (resources.ContainsKey(fullFrom) || resources.Count < 64))
        {
            resources[fullFrom] = new(ParseShow(ClientChildValue(stanza, "show")),
                PresenceStatus(stanza));
        }
        else if (type == "unavailable") resources.Remove(fullFrom);
        if (resources.Count == 0) _presentResources.Remove(from);
        if (!_contacts.TryGetValue(from, out var contact)) return;
        var nowOnline = resources.Count > 0;
        var show = BestShow(resources.Values);
        var statusText = BestStatus(resources.Values);
        if (contact.Online == nowOnline && contact.Show == show &&
            string.Equals(contact.StatusText, statusText, StringComparison.Ordinal)) return;
        if (!nowOnline) _wentOfflineAt[from] = DateTimeOffset.UtcNow;
        else _wentOfflineAt.Remove(from);
        _contacts[from] = contact with
        {
            Online = nowOnline,
            Show = show,
            StatusText = statusText
        };
        PublishPresence();
    }

    private static string? PresenceStatus(XElement stanza)
    {
        var status = ClientChildValue(stanza, "status")?.Trim();
        return string.IsNullOrWhiteSpace(status) ? null : status;
    }

    internal static string? ClientChildValue(XElement stanza, string localName) =>
        stanza.Elements().FirstOrDefault(child => IsClientStanza(child, localName))?.Value;

    internal static XmppPresenceShow ParseShow(string? show) => show?.Trim().ToLowerInvariant() switch
    {
        "chat" => XmppPresenceShow.Chat,
        "away" => XmppPresenceShow.Away,
        "xa" => XmppPresenceShow.ExtendedAway,
        "dnd" => XmppPresenceShow.DoNotDisturb,
        _ => XmppPresenceShow.Online
    };

    private static XmppPresenceShow BestShow(IEnumerable<ResourcePresence> resources)
    {
        XmppPresenceShow? best = null;
        foreach (var resource in resources)
        {
            if (best is null || PresenceRank(resource.Show) > PresenceRank(best.Value))
                best = resource.Show;
        }
        return best ?? XmppPresenceShow.Offline;
    }

    private static string? BestStatus(IEnumerable<ResourcePresence> resources) =>
        resources.OrderByDescending(resource => PresenceRank(resource.Show))
            .Select(resource => resource.StatusText)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

    // Prefer available / chat over away / dnd when multiple resources are online.
    private static int PresenceRank(XmppPresenceShow show) => show switch
    {
        XmppPresenceShow.Chat => 5,
        XmppPresenceShow.Online => 4,
        XmppPresenceShow.Away => 3,
        XmppPresenceShow.ExtendedAway => 2,
        XmppPresenceShow.DoNotDisturb => 1,
        _ => 0
    };

    private readonly record struct ResourcePresence(XmppPresenceShow Show, string? StatusText);

    private void ApplyOccupantPresence(string roomJid, string fullFrom, string? type, XElement? extension)
    {
        var separator = fullFrom.IndexOf('/');
        if (separator < 0 || separator == fullFrom.Length - 1) return;
        var nick = fullFrom[(separator + 1)..];
        if (!_occupants.TryGetValue(roomJid, out var roomOccupants))
            _occupants[roomJid] = roomOccupants = new(StringComparer.Ordinal);

        var item = extension?.Element(MucUser + "item");
        var nickChange = extension?.Elements(MucUser + "status").Any(status => Attr(status, "code") == "303") == true;
        if (type == "unavailable")
        {
            if (nickChange && Attr(item, "nick") is { Length: > 0 } newNick)
            {
                if (roomOccupants.Remove(nick, out var prior))
                    roomOccupants[newNick] = prior with { Nick = newNick };
            }
            else roomOccupants.Remove(nick);
            client.SetOccupants(roomJid, roomOccupants.Values.ToArray());
            return;
        }

        if (type is not null) return;
        var affiliation = Attr(item, "affiliation") ?? "none";
        var role = Attr(item, "role") ?? "participant";
        var jid = XmppAddresses.Bare(Attr(item, "jid"));
        roomOccupants[nick] = new(nick, jid, affiliation, role);
        client.SetOccupants(roomJid, roomOccupants.Values.ToArray());
    }

    private void HandleIq(XElement stanza)
    {
        var id = Attr(stanza, "id");
        if (string.IsNullOrEmpty(id)) return;
        var type = Attr(stanza, "type");
        var from = Attr(stanza, "from");
        if (id == _pingId && type is "result" or "error" && IsServer(from))
        {
            _pingId = null;
            if (id == _replayBoundary) _replayBoundary = null;
        }
        else if (id == _waitingId && type is "result" or "error" && ExpectedSender(from, _waitingFrom))
        {
            _iqResult = stanza;
        }
        else if (_roomArchive is { } archive && id == archive.IqId && type is "result" or "error" && from == archive.Jid)
        {
            FinishRoomArchivePage(archive, stanza);
        }
        else if (type == "get" && stanza.Element(Ping + "ping") is not null)
        {
            stream.Send(BuildIq("result", from, id, null));
        }
        else if (type == "set" && IsOwnArchive(from) && stanza.Element(Roster + "query") is { } push)
        {
            ApplyRoster(push, push: true);
            stream.Send(BuildIq("result", from, id, null));
        }
    }

    private void ApplyRoster(XElement? roster, bool push)
    {
        if (roster is null) return;
        if (!push)
        {
            foreach (var address in _contacts.Where(pair => !pair.Value.SubscriptionRequested)
                         .Select(pair => pair.Key).ToArray())
                _contacts.Remove(address);
        }
        foreach (var item in roster.Elements(Roster + "item"))
        {
            var address = XmppAddresses.Bare(Attr(item, "jid"));
            if (address is null || address == account) continue;
            if (Attr(item, "subscription") == "remove") { _contacts.Remove(address); _presentResources.Remove(address); }
            else
            {
                var groups = item.Elements(Roster + "group")
                    .Select(group => group.Value.Trim())
                    .Where(group => group.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var prior = _contacts.GetValueOrDefault(address);
                var online = _presentResources.TryGetValue(address, out var resources) && resources.Count > 0;
                _contacts[address] = new(address, Attr(item, "name") ?? address, online,
                    prior?.SubscriptionRequested == true, groups,
                    online ? BestShow(resources!.Values) : XmppPresenceShow.Offline,
                    online ? BestStatus(resources!.Values) : null);
            }
        }
        PublishPresence();
    }

    private async Task<XElement?> IqAsync(string type, string? target, XElement payload, bool includeErrors = false)
    {
        if (stream.Disconnected || _lost) return null;
        var id = NewId();
        _waitingId = id;
        _waitingFrom = target;
        _iqResult = null;
        try
        {
            stream.Send(BuildIq(type, target, id, payload));
            var deadline = _clock.ElapsedMilliseconds + 8_000;
            while (_iqResult is null && !_lost && !stream.Disconnected && _clock.ElapsedMilliseconds < deadline)
                await PumpAsync();
            return includeErrors || Attr(_iqResult, "type") == "result" ? _iqResult : null;
        }
        finally { _waitingId = _waitingFrom = null; _iqResult = null; }
    }

    private string StartPing()
    {
        var id = NewId();
        _pingId = id;
        _pingStarted = _clock.ElapsedMilliseconds;
        stream.Send(BuildIq("get", Domain, id, new XElement(Ping + "ping")));
        return id;
    }

    private bool IsServer(string? from) => string.IsNullOrEmpty(from) || from == Domain || from == account;
    private bool IsOwnArchive(string? from) => string.IsNullOrEmpty(from) || from == account;
    private bool ExpectedSender(string? from, string? expected) =>
        expected is null || expected == Domain || expected == account ? IsServer(from) : from == expected;

    public Task BroadcastOwnPresenceAsync()
    {
        BroadcastOwnPresence(includeRooms: true);
        return Task.CompletedTask;
    }

    public async Task SetAvatarAsync(byte[]? bytes, string? contentType)
    {
        if (bytes is { Length: > 0 })
        {
            if (bytes.Length > XmppProfileParsing.MaxAvatarBytes)
                throw new ArgumentException("Avatar must be 256 KB or smaller.");
            if (string.IsNullOrWhiteSpace(contentType) ||
                contentType is not ("image/jpeg" or "image/png" or "image/gif"))
                throw new ArgumentException("Use a JPEG, PNG, or GIF avatar.");
        }

        var existing = await IqAsync("get", account, new XElement(VCardTemp + "vCard"));
        var current = existing?.Element(VCardTemp + "vCard") ?? existing?.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("vCard", StringComparison.OrdinalIgnoreCase));
        var next = XmppProfileParsing.ApplyPhoto(current, bytes is { Length: > 0 } ? bytes : null,
            bytes is { Length: > 0 } ? contentType : null);
        if (await IqAsync("set", account, next) is null)
            throw new InvalidOperationException("The server rejected the avatar update.");

        client.RememberOwnAvatar(bytes is { Length: > 0 } ? bytes : null, contentType);
        BroadcastOwnPresence(includeRooms: true);
    }

    private void PublishStatus() => client.SetState(
        _partialArchive ? "Connected · Some server history is unavailable" : "Connected", online: true);

    private void PublishPresence() => client.PublishPresence(_contacts.Values, _joined.Keys);

    private void BroadcastOwnPresence(bool includeRooms = false)
    {
        stream.Send(BuildAvailablePresence());
        if (!includeRooms) return;
        foreach (var room in _joined.Values.Concat(_joining.Values).DistinctBy(room => room.Jid))
            stream.Send(BuildAvailablePresence($"{room.Jid}/{room.Nickname}"));
    }

    private XElement BuildAvailablePresence(string? to = null, params object?[] extras)
    {
        var prefs = client.OwnPresencePreferences;
        var presence = new XElement("presence", new XElement("priority", "10"));
        if (to is not null) presence.Add(new XAttribute("to", to));
        var show = ShowElementValue(prefs.Show);
        if (show is not null) presence.Add(new XElement("show", show));
        if (!string.IsNullOrWhiteSpace(prefs.StatusText))
            presence.Add(new XElement("status", prefs.StatusText));
        presence.Add(new XElement(XmppProfileParsing.VCardUpdate + "x",
            new XElement(XmppProfileParsing.VCardUpdate + "photo", client.OwnAvatarHash ?? "")));
        foreach (var extra in extras)
            if (extra is not null) presence.Add(extra);
        return presence;
    }

    private static string? ShowElementValue(XmppPresenceShow show) => show switch
    {
        XmppPresenceShow.Chat => "chat",
        XmppPresenceShow.Away => "away",
        XmppPresenceShow.ExtendedAway => "xa",
        XmppPresenceShow.DoNotDisturb => "dnd",
        _ => null
    };

    private void SendPresence(string target, string type) => stream.Send(new XElement("presence",
        new XAttribute("to", target), new XAttribute("type", type)));

    private static XElement BuildIq(string type, string? target, string id, XElement? payload) =>
        new("iq", new XAttribute("type", type), new XAttribute("id", id),
            target is null ? null : new XAttribute("to", target), payload);

    private static XElement FormField(string name, string value, bool hidden = false) =>
        new(Forms + "field", new XAttribute("var", name), hidden ? new XAttribute("type", "hidden") : null,
            new XElement(Forms + "value", value));

    private static bool IsClientStanza(XElement stanza, string name) =>
        stanza.Name.LocalName == name && (stanza.Name.Namespace == XNamespace.None || stanza.Name.Namespace == ClientNamespace);
    private static string? Attr(XElement? node, string name) => (string?)node?.Attribute(name);
    private static string NewId() => Guid.NewGuid().ToString("N");
    private static string ScopedKey(XmppConversation conversation, string type, string sender, string id) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { conversation.Kind.ToString(), conversation.Jid, type, sender, id });

    private sealed class RoomArchive(string jid, DateTimeOffset end)
    {
        public string Jid { get; } = jid;
        public DateTimeOffset End { get; } = end;
        public bool Discovered { get; set; }
        public string? Before { get; set; }
        public string? IqId { get; set; }
        public string? QueryId { get; set; }
        public long Deadline { get; set; }
        public HashSet<string> Cursors { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ArchiveFetch(string jid)
    {
        public string Jid { get; } = jid;
        public DateTimeOffset? Start { get; set; }
        public DateTimeOffset End { get; set; }
        public string? After { get; set; }
        public HashSet<string> Cursors { get; } = new(StringComparer.Ordinal);
    }

    public void Dispose()
    {
        if (!stream.StreamEstablished) return;
        // Preserve roster JIDs for the next refresh, but drop stale online state.
        client.PublishPresence(_contacts.Values.Select(contact => contact with { Online = false }), []);
    }
}

