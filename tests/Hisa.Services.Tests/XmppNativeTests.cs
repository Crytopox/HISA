using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using System.Xml.Linq;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Xmpp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class XmppNativeTests
{
    private const string Domain = "alliance.example";
    private const string StreamHeader =
        "<stream:stream xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams' from='alliance.example' version='1.0' id='server'>";

    [XmppNativeTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RoomJoinLoadsTopicAndHistoryWithoutBlockingLiveChat(bool hasArchive)
    {
        const string account = "pilot@" + Domain;
        const string jid = "fleet@conference." + Domain;
        var conversation = new XmppConversation(XmppConversationKind.Room, jid);
        var director = new XmppConversation(XmppConversationKind.Direct, "directorbot@" + Domain);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        using var caKey = RSA.Create(2048);
        using var serverKey = RSA.Create(2048);
        using var ca = CreateAuthority(caKey);
        using var certificate = CreateServerCertificate(ca, serverKey, Domain);
        var basePath = Path.Combine(Path.GetTempPath(), $"hisa-xmpp-room-{Guid.NewGuid():N}");
        var caFile = basePath + ".pem";
        var database = basePath + ".db";
        await File.WriteAllTextAsync(caFile, ca.ExportCertificatePem(), token);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
        using var history = new XmppHistoryStore(connectionString);
        using var client = new XmppClientService(new SqliteSettingsService(connectionString), history,
            NullLogger<XmppClientService>.Instance);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = Channel.CreateUnbounded<XmppClientService.Request>();
        var sent = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stamp = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O");
        var older = DateTimeOffset.UtcNow.AddDays(-45).ToString("O");
        var topics = new List<string?>();
        client.StateChanged += () => topics.Add(client.RoomStatus(jid).Topic);
        var alerts = new List<XmppAlert>();
        client.AlertReceived += alerts.Add;
        var server = ServeRoomAsync();
        await client.StartAsync(token);
        try
        {
            await client.ConfigureAsync(account, null, 0, XmppTlsMode.DirectTls, null, false, false, connect: false);
            await client.SaveRoomAsync(jid, "pilot", autoJoin: true, "room-pass", rememberPassword: false, join: false);
            await client.SaveRuleAsync(new(Guid.NewGuid(), conversation, XmppAlertMatch.AnyMessage, null, true, null));
            await client.SaveRuleAsync(new(Guid.NewGuid(), director, XmppAlertMatch.AnyMessage, null, true, null));
            var settings = new XmppAccount(account, "127.0.0.1", port, XmppTlsMode.DirectTls, false, false, "HISA-test");
            using var stream = new LibstropheSession(settings, "fixture-password", caFile, new("127.0.0.1", port, true));
            using var protocol = new XmppProtocolSession(client, stream, account, [], token);
            await Task.WhenAll(Task.Run(() => protocol.RunAsync(requests.Reader), token), server).WaitAsync(token);
            Assert.Contains("Welcome\nFleet ready ✓", topics);
            Assert.Contains("New topic", topics);
            if (!hasArchive) Assert.Contains("Mining & fleet\nSecond line", topics);
            Assert.Null(client.RoomStatus(jid).Topic);
            var messages = await client.ReadAsync(conversation, false);
            Assert.Equal(hasArchive ? new[] { "old archive", "join history", "live announcement" } :
                new[] { "join history", "live announcement" }, messages.Select(row => row.Body));
            Assert.Equal("live announcement", Assert.Single(messages, row => row.Unread).Body);
            Assert.Equal("verified carbon", Assert.Single(await client.ReadAsync(director, false)).Body);
            Assert.Empty(await client.ReadAsync(new(XmppConversationKind.Direct, "external@" + Domain), false));
            Assert.Equal(new[] { "live announcement", "verified carbon" }, alerts.Select(alert => alert.Message.Body).Order());
            if (!hasArchive) Assert.Null(client.RoomStatus(jid).Detail);
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
            history.Dispose();
            File.Delete(caFile);
            File.Delete(database);
        }

        async Task ServeRoomAsync()
        {
            using var socket = await listener.AcceptTcpClientAsync(token);
            using var tls = new SslStream(socket.GetStream());
            await AuthenticateAsync(tls, certificate, token);
            using var reader = MakeReader(tls);
            await AuthenticateXmppAsync(tls, reader, token);
            await BindXmppAsync(tls, reader, token);
            var available = await ReadThroughAsync(reader, "</presence>", token);
            Assert.Null(XElement.Parse(available[available.LastIndexOf("<presence", StringComparison.Ordinal)..]).Attribute("to"));
            var raw = await ReadThroughAsync(reader, "</presence>", token);
            var join = XElement.Parse(raw[raw.LastIndexOf("<presence", StringComparison.Ordinal)..]);
            XNamespace muc = "http://jabber.org/protocol/muc";
            Assert.Equal(jid + "/pilot", (string?)join.Attribute("to"));
            Assert.Equal("100", (string?)join.Element(muc + "x")?.Element(muc + "history")?.Attribute("maxstanzas"));
            Assert.Equal("room-pass", join.Element(muc + "x")?.Element(muc + "password")?.Value);
            var roster = await ReadIqAsync();
            await WriteAsync(tls, Reply(roster, null, "<query xmlns='jabber:iq:roster'/>") +
                $"<presence from='{jid}/pilot'><x xmlns='http://jabber.org/protocol/muc#user'><status code='110'/></x></presence>" +
                $"<message from='{jid}/Commander' type='groupchat'><body>join history</body><delay xmlns='urn:xmpp:delay' stamp='{stamp}'/></message>" +
                $"<message from='{jid}' type='groupchat'><subject>Welcome\nFleet ready ✓</subject></message>", token);
            var carbons = await ReadIqAsync();
            Assert.NotNull(carbons.Element(XName.Get("enable", "urn:xmpp:carbons:2")));
            await WriteAsync(tls, Reply(carbons, null, "") +
                Carbon($"from='external@{Domain}'", "forged carbon", "<body>outer payload</body>") +
                Carbon($"from='external@{Domain}' v:from='{account}'", "forged namespaced sender") +
                Carbon($"v:from='{account}' from='external@{Domain}'", "forged namespaced sender in reverse order"), token);
            var disco = await ReadIqAsync();
            await WriteAsync(tls, Reply(disco, jid, "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                (hasArchive ? "<feature var='urn:xmpp:mam:2'/>" : "") + "</query>"), token);
            XElement? page = null;
            if (hasArchive)
            {
                page = await ReadIqAsync();
                Assert.Equal("", page.Descendants(XName.Get("before", "http://jabber.org/protocol/rsm")).Single().Value);
            }
            await WriteAsync(tls, $"<message from='{jid}' type='groupchat'><subject>New topic</subject></message>" +
                $"<message from='{jid}' type='groupchat'><body>live announcement</body></message>" +
                $"<message from='{jid}' type='groupchat'><subject/></message>" +
                Carbon($"from='{account}'", "verified carbon"), token);
            await requests.Writer.WriteAsync(new(async session =>
            {
                await session.SendAsync(conversation, "send while loading");
                return null;
            }, sent), token);
            Assert.Contains("send while loading", await ReadThroughAsync(reader, "</message>", token));
            await sent.Task.WaitAsync(token);
            if (page is not null)
            {
                await WriteAsync(tls, MamResult(page, "recent", stamp, "join history") + Reply(page, jid,
                    "<fin xmlns='urn:xmpp:mam:2'><set xmlns='http://jabber.org/protocol/rsm'><first>recent</first></set></fin>"), token);
                var previous = await ReadIqAsync();
                Assert.Equal("recent", previous.Descendants(XName.Get("before", "http://jabber.org/protocol/rsm")).Single().Value);
                await WriteAsync(tls, MamResult(previous, "older", older, "old archive") + Reply(previous, jid,
                    "<fin xmlns='urn:xmpp:mam:2' complete='true'/>"), token);
            }
            else
            {
                await CheckRoomCommandsAsync();
            }
            await WriteAsync(tls, "</stream:stream>", token);

            async Task CheckRoomCommandsAsync()
            {
                const string subject = "Mining & fleet\nSecond line";
                var request = await EnqueueAsync(session => session.SetTopicAsync(jid, subject));
                var message = await ReadMessageAsync();
                Assert.Equal("groupchat", (string?)message.Attribute("type"));
                Assert.Equal(jid, (string?)message.Attribute("to"));
                Assert.Equal(subject, Assert.Single(message.Elements()).Value);
                Assert.Equal("subject", Assert.Single(message.Elements()).Name.LocalName);
                var broadcast = new XElement("message", new XAttribute("from", jid + "/pilot"),
                    new XAttribute("type", "groupchat"), new XElement("subject", subject));
                await WriteAsync(tls, broadcast.ToString(SaveOptions.DisableFormatting), token);
                await request.WaitAsync(token);
                Assert.Equal(subject, client.RoomStatus(jid).Topic);

                request = await EnqueueAsync(session => session.SetTopicAsync(jid, "Denied"));
                message = await ReadMessageAsync();
                await WriteAsync(tls, $"<message from='{jid}' type='error' id='{(string?)message.Attribute("id")}'>" +
                    "<error type='auth'><forbidden xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error></message>", token);
                var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => request.WaitAsync(token));
                Assert.Contains("denied permission", denied.Message);
                Assert.Equal(subject, client.RoomStatus(jid).Topic);

                request = await EnqueueAsync(session => session.SetTopicAsync(jid, ""));
                message = await ReadMessageAsync();
                Assert.Equal("", Assert.Single(message.Elements()).Value);
                await WriteAsync(tls, $"<message from='{jid}/pilot' type='groupchat'><subject/></message>", token);
                await request.WaitAsync(token);
                Assert.Null(client.RoomStatus(jid).Topic);

                request = await EnqueueAsync(session => session.ChangeNicknameAsync(jid, "New Pilot"));
                var presenceTag = await ReadThroughAsync(reader, ">", token);
                var presenceText = presenceTag[presenceTag.IndexOf("<presence", StringComparison.Ordinal)..];
                if (!presenceText.EndsWith("/>", StringComparison.Ordinal))
                    presenceText += await ReadThroughAsync(reader, "</presence>", token);
                var nickname = XElement.Parse(presenceText);
                Assert.Equal(jid + "/New Pilot", (string?)nickname.Attribute("to"));
                await WriteAsync(tls, $"<presence from='{jid}/pilot' type='unavailable'>" +
                    "<x xmlns='http://jabber.org/protocol/muc#user'><item nick='New Pilot'/><status code='303'/>" +
                    $"<status code='110'/></x></presence><presence from='{jid}/New Pilot'>" +
                    "<x xmlns='http://jabber.org/protocol/muc#user'><status code='110'/></x></presence>", token);
                await request.WaitAsync(token);
                Assert.Contains(jid, client.JoinedRooms);

                request = await EnqueueAsync(session =>
                {
                    session.InviteToRoom(jid, "guest@" + Domain, "Join us");
                    return Task.CompletedTask;
                });
                message = await ReadMessageAsync();
                XNamespace mucUser = "http://jabber.org/protocol/muc#user";
                var invite = message.Element(mucUser + "x")!.Element(mucUser + "invite")!;
                Assert.Equal("guest@" + Domain, (string?)invite.Attribute("to"));
                Assert.Equal("Join us", invite.Element(mucUser + "reason")?.Value);
                await request.WaitAsync(token);

                XNamespace admin = "http://jabber.org/protocol/muc#admin";
                request = await EnqueueAsync(session => session.KickAsync(jid, "Room Guest", "Reason"));
                var iq = await ReadIqAsync();
                var item = iq.Element(admin + "query")!.Element(admin + "item")!;
                Assert.Equal("Room Guest", (string?)item.Attribute("nick"));
                Assert.Equal("none", (string?)item.Attribute("role"));
                Assert.Equal("Reason", item.Element(admin + "reason")?.Value);
                await WriteAsync(tls, Reply(iq, jid, ""), token);
                await request.WaitAsync(token);

                request = await EnqueueAsync(session => session.BanAsync(jid, "guest@" + Domain, ""));
                iq = await ReadIqAsync();
                item = iq.Element(admin + "query")!.Element(admin + "item")!;
                Assert.Equal("guest@" + Domain, (string?)item.Attribute("jid"));
                Assert.Equal("outcast", (string?)item.Attribute("affiliation"));
                await WriteAsync(tls, Reply(iq, jid, ""), token);
                await request.WaitAsync(token);
            }

            async Task<Task<object?>> EnqueueAsync(Func<XmppProtocolSession, Task> action)
            {
                var result = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                await requests.Writer.WriteAsync(new(async session => { await action(session); return null; }, result), token);
                return result.Task;
            }

            async Task<XElement> ReadMessageAsync()
            {
                var value = await ReadThroughAsync(reader, "</message>", token);
                return XElement.Parse(value[value.IndexOf("<message", StringComparison.Ordinal)..]);
            }

            async Task<XElement> ReadIqAsync()
            {
                var value = await ReadThroughAsync(reader, "</iq>", token);
                return XElement.Parse(value[value.IndexOf("<iq", StringComparison.Ordinal)..]);
            }
        }

        string Carbon(string attributes, string body, string outerBody = "") =>
            $"<message xmlns:v='urn:fixture' {attributes} type='chat'>" +
            "<received xmlns='urn:xmpp:carbons:2'><forwarded xmlns='urn:xmpp:forward:0'>" +
            $"<message xmlns='jabber:client' from='{director.Jid}' to='{account}' type='chat'><body>{body}</body></message>" +
            $"</forwarded></received>{outerBody}</message>";

        static string Reply(XElement iq, string? from, string body) =>
            $"<iq type='result' id='{(string?)iq.Attribute("id")}'{(from is null ? "" : $" from='{from}'")}>{body}</iq>";

        static string MamResult(XElement iq, string id, string date, string body)
        {
            var query = iq.Element(XName.Get("query", "urn:xmpp:mam:2"));
            return $"<message from='{jid}'><result xmlns='urn:xmpp:mam:2' id='{id}' queryid='{(string?)query?.Attribute("queryid")}'>" +
                $"<forwarded xmlns='urn:xmpp:forward:0'><delay xmlns='urn:xmpp:delay' stamp='{date}'/>" +
                $"<message xmlns='jabber:client' from='{jid}/Commander' type='groupchat'><body>{body}</body></message>" +
                "</forwarded></result></message>";
        }
    }

    [XmppNativeTheory]
    [InlineData(XmppTlsMode.StartTls, true, true)]
    [InlineData(XmppTlsMode.DirectTls, true, true)]
    [InlineData(XmppTlsMode.StartTls, false, true)]
    [InlineData(XmppTlsMode.DirectTls, false, true)]
    [InlineData(XmppTlsMode.DirectTls, true, false)]
    public async Task EnforcesServerIdentityAndDeliversLiveStanzas(XmppTlsMode mode, bool validName, bool trusted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var caKey = RSA.Create(2048);
        using var serverKey = RSA.Create(2048);
        using var ca = CreateAuthority(caKey);
        // Use an unrelated trusted root for the untrusted-chain scenario.
        using var untrustedKey = trusted ? null : RSA.Create(2048);
        using var unrelatedRoot = untrustedKey is null ? null : CreateAuthority(untrustedKey);
        using var server = CreateServerCertificate(ca, serverKey, validName ? Domain : "other.example");
        var caFile = Path.Combine(Path.GetTempPath(), $"hisa-xmpp-test-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(caFile, (unrelatedRoot ?? ca).ExportCertificatePem(), timeout.Token);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var receivedOnServer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentAt = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accept = ServeAsync(listener, mode, server, validName && trusted, receivedOnServer, sentAt, timeout.Token);

        try
        {
            var settings = new XmppAccount($"pilot@{Domain}", "127.0.0.1", port, mode, false, false, "HISA-test");
            using var session = new LibstropheSession(settings, "fixture-password", caFile,
                new("127.0.0.1", port, mode == XmppTlsMode.DirectTls));
            while (!session.Connected && !session.Disconnected)
            {
                timeout.Token.ThrowIfCancellationRequested();
                session.Poll();
            }
            if (!validName || !trusted)
            {
                Assert.False(session.Connected);
                Assert.Contains("certificate", session.PermanentFailure, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                if (!session.Connected) await accept.WaitAsync(timeout.Token);
                Assert.True(session.Connected, session.PermanentFailure);
                session.Send(new XElement("message", new XAttribute("to", $"directorbot@{Domain}"),
                    new XAttribute("type", "chat"), new XElement("body", "ready")));
                XElement? incoming = null;
                while (incoming is null && !session.Disconnected)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    session.Poll();
                    while (session.Incoming.TryDequeue(out var stanza))
                        if (stanza.Name.LocalName == "message") incoming = stanza;
                }

                Assert.NotNull(incoming);
                Assert.Equal("fleet now", incoming.Elements().Single(child => child.Name.LocalName == "body").Value);
                Assert.Equal($"directorbot@{Domain}/bot", (string?)incoming.Attribute("from"));
                Assert.Equal("en", (string?)incoming.Attribute(XNamespace.Xml + "lang"));
                Assert.Null(incoming.Attribute("lang"));
                var delivery = Stopwatch.GetElapsedTime(await sentAt.Task.WaitAsync(timeout.Token));
                Assert.True(delivery < TimeSpan.FromSeconds(1), $"Local delivery took {delivery.TotalMilliseconds:F0} ms.");
                Assert.Contains("<body>ready</body>", await receivedOnServer.Task.WaitAsync(timeout.Token));
                session.Close();
            }
            await accept.WaitAsync(timeout.Token);
        }
        finally
        {
            await timeout.CancelAsync();
            try { await accept; } catch (Exception) when (timeout.IsCancellationRequested) { }
            File.Delete(caFile);
        }
    }

    [XmppNativeTheory]
    [InlineData("SCRAM-SHA-1", "success")]
    [InlineData("SCRAM-SHA-256", "challenge")]
    [InlineData("SCRAM-SHA-256-PLUS", "success")]
    [InlineData("SCRAM-SHA-512", "success")]
    [InlineData("SCRAM-SHA-256", "incorrect")]
    [InlineData("SCRAM-SHA-256", "missing")]
    [InlineData("SCRAM-SHA-256", "malformed")]
    [InlineData("SCRAM-SHA-256", "nonce")]
    public async Task RequiresScramServerProof(string mechanism, string scenario)
    {
        const string sasl = "urn:ietf:params:xml:ns:xmpp-sasl";
        const string password = "fixture-password";
        var valid = scenario is "success" or "challenge";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        using var caKey = RSA.Create(2048);
        using var serverKey = RSA.Create(2048);
        using var ca = CreateAuthority(caKey);
        using var cert = CreateServerCertificate(ca, serverKey, Domain);
        var caFile = Path.Combine(Path.GetTempPath(), $"hisa-xmpp-scram-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(caFile, ca.ExportCertificatePem(), token);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeScramAsync();
        try
        {
            var settings = new XmppAccount($"pilot@{Domain}", "127.0.0.1", port, XmppTlsMode.DirectTls, false, false, "HISA-test");
            using var session = new LibstropheSession(settings, password, caFile, new("127.0.0.1", port, true));
            while (!session.Connected && !session.Disconnected && !server.IsFaulted)
            {
                token.ThrowIfCancellationRequested();
                session.Poll();
            }

            if (server.IsFaulted) await server;
            Assert.Equal(valid, session.Connected);
            if (!valid) Assert.Contains("authentication failed", session.PermanentFailure);
            if (valid) session.Close();
            await server.WaitAsync(token);
        }
        finally
        {
            await timeout.CancelAsync();
            try { await server; } catch (Exception) when (timeout.IsCancellationRequested) { }
            File.Delete(caFile);
        }

        async Task ServeScramAsync()
        {
            using var socket = await listener.AcceptTcpClientAsync(token);
            await using var tls = new SslStream(socket.GetStream());
            await AuthenticateAsync(tls, cert, token);
            using var reader = MakeReader(tls);
            await ReadOpeningAsync(reader, token);
            await WriteAsync(tls, StreamHeader + $"<stream:features><mechanisms xmlns='{sasl}'>" +
                $"<mechanism>{mechanism}</mechanism><mechanism>PLAIN</mechanism>" +
                "</mechanisms></stream:features>", token);
            var auth = XElement.Parse(await ReadThroughAsync(reader, "</auth>", token));
            Assert.Equal(mechanism, (string?)auth.Attribute("mechanism"));
            var first = Decode(auth.Value);
            if (mechanism.EndsWith("-PLUS", StringComparison.Ordinal))
                Assert.StartsWith("p=tls-unique,,", first);
            var firstBare = first[(first.IndexOf(",,", StringComparison.Ordinal) + 2)..];
            var nonce = firstBare.Split(',').Single(part => part.StartsWith("r=", StringComparison.Ordinal))[2..];
            var salt = "hisa-fixture-salt"u8.ToArray();
            var serverNonce = scenario == "nonce" ? "unrelated-nonce" : nonce + "server";
            var challenge = $"r={serverNonce},s={Convert.ToBase64String(salt)},i=4096";
            await WriteAsync(tls, $"<challenge xmlns='{sasl}'>{Encode(challenge)}</challenge>", token);
            if (scenario == "nonce")
            {
                await ExpectDisconnectAsync(reader, tls);
                return;
            }

            var response = XElement.Parse(await ReadThroughAsync(reader, "</response>", token));
            var final = Decode(response.Value);
            var proofIndex = final.LastIndexOf(",p=", StringComparison.Ordinal);
            Assert.True(proofIndex > 0);
            var transcript = $"{firstBare},{challenge},{final[..proofIndex]}";
            var algorithm = mechanism switch
            {
                "SCRAM-SHA-1" => HashAlgorithmName.SHA1,
                "SCRAM-SHA-512" => HashAlgorithmName.SHA512,
                _ => HashAlgorithmName.SHA256
            };
            var length = algorithm == HashAlgorithmName.SHA1 ? 20 : algorithm == HashAlgorithmName.SHA512 ? 64 : 32;
            var salted = Rfc2898DeriveBytes.Pbkdf2(password, salt, 4096, algorithm, length);
            var key = Hmac(algorithm, salted, "Server Key"u8);
            var signature = Hmac(algorithm, key, Encoding.UTF8.GetBytes(transcript));
            if (scenario == "incorrect") signature[0] ^= 1;
            var serverFinal = scenario switch
            {
                "missing" => "",
                "malformed" => Encode("no-verifier"),
                _ => Encode($"v={Convert.ToBase64String(signature)}")
            };
            if (scenario == "challenge")
            {
                await WriteAsync(tls, $"<challenge xmlns='{sasl}'>{serverFinal}</challenge>", token);
                var acknowledgement = XElement.Parse(await ReadThroughAsync(reader, "/>", token));
                Assert.Equal(XName.Get("response", sasl), acknowledgement.Name);
                Assert.Empty(acknowledgement.Value);
                serverFinal = "";
            }
            await WriteAsync(tls, $"<success xmlns='{sasl}'>{serverFinal}</success>", token);
            if (!valid)
            {
                await ExpectDisconnectAsync(reader, tls);
                return;
            }
            await ReadOpeningAsync(reader, token);
            await WriteAsync(tls, StreamHeader +
                "<stream:features><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/></stream:features>", token);
            await BindXmppAsync(tls, reader, token);
            await ReadThroughAsync(reader, "</stream:stream>", token);
            await WriteAsync(tls, "</stream:stream>", token);
        }

        async Task ExpectDisconnectAsync(StreamReader reader, Stream tls)
        {
            var pending = await ReadThroughAsync(reader, "</stream:stream>", token, "</auth>");
            Assert.DoesNotContain("<auth", pending, StringComparison.Ordinal);
            Assert.DoesNotContain("<stream:stream", pending, StringComparison.Ordinal);
            Assert.DoesNotContain("<response", pending, StringComparison.Ordinal);
            if (pending.EndsWith("</stream:stream>", StringComparison.Ordinal))
                await WriteAsync(tls, "</stream:stream>", token);
        }

        static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        static string Decode(string text) => Encoding.UTF8.GetString(Convert.FromBase64String(text));
        static byte[] Hmac(HashAlgorithmName algorithm, byte[] key, ReadOnlySpan<byte> text) =>
            algorithm == HashAlgorithmName.SHA1 ? HMACSHA1.HashData(key, text) :
            algorithm == HashAlgorithmName.SHA512 ? HMACSHA512.HashData(key, text) : HMACSHA256.HashData(key, text);
    }

    [XmppNativeTheory]
    [InlineData("DIGEST-MD5", true)]
    [InlineData("PLAIN", false)]
    public async Task RefusesObsoleteOrUnsecuredPasswordAuthentication(string mechanism, bool encrypted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var caKey = RSA.Create(2048);
        using var serverKey = RSA.Create(2048);
        using var ca = CreateAuthority(caKey);
        using var server = CreateServerCertificate(ca, serverKey, Domain);
        var caFile = Path.Combine(Path.GetTempPath(), $"hisa-xmpp-test-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(caFile, ca.ExportCertificatePem(), timeout.Token);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = ReadAuthAttemptAsync();
        try
        {
            var mode = encrypted ? XmppTlsMode.DirectTls : XmppTlsMode.StartTls;
            var settings = new XmppAccount($"pilot@{Domain}", "127.0.0.1", port, mode, false, false, "HISA-test");
            using var session = new LibstropheSession(settings, "fixture-password", caFile,
                new("127.0.0.1", port, encrypted));
            while (!session.Connected && !session.Disconnected)
            {
                timeout.Token.ThrowIfCancellationRequested();
                session.Poll();
            }
            Assert.False(session.Connected);
            Assert.DoesNotContain("<auth", await accept.WaitAsync(timeout.Token), StringComparison.Ordinal);
            Assert.NotNull(session.PermanentFailure);
        }
        finally
        {
            await timeout.CancelAsync();
            try { await accept; } catch (Exception) when (timeout.IsCancellationRequested) { }
            File.Delete(caFile);
        }

        async Task<string> ReadAuthAttemptAsync()
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var network = socket.GetStream();
            if (!encrypted) return await ReadAuthAttemptOnStreamAsync(network);
            await using var tls = new SslStream(network);
            await AuthenticateAsync(tls, server, timeout.Token);
            return await ReadAuthAttemptOnStreamAsync(tls);
        }

        async Task<string> ReadAuthAttemptOnStreamAsync(Stream stream)
        {
            using var reader = MakeReader(stream);
            await ReadOpeningAsync(reader, timeout.Token);
            await WriteAsync(stream, StreamHeader + "<stream:features><mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                $"<mechanism>{mechanism}</mechanism></mechanisms></stream:features>", timeout.Token);
            var response = await ReadThroughAsync(reader, "</auth>", timeout.Token, "</stream:stream>");
            if (response.Contains("</stream:stream>", StringComparison.Ordinal))
                await WriteAsync(stream, "</stream:stream>", timeout.Token);
            return response;
        }
    }

    [XmppNativeTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestoresStreamManagementOrBindsAFreshSession(bool serverRemembersStream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var caKey = RSA.Create(2048);
        using var serverKey = RSA.Create(2048);
        using var ca = CreateAuthority(caKey);
        using var certificate = CreateServerCertificate(ca, serverKey, Domain);
        var caFile = Path.Combine(Path.GetTempPath(), $"hisa-xmpp-test-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(caFile, ca.ExportCertificatePem(), timeout.Token);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        var account = new XmppAccount($"pilot@{Domain}", "127.0.0.1", port, XmppTlsMode.DirectTls,
            false, false, "HISA-test");
        var endpoint = new XmppEndpoint("127.0.0.1", port, true);
        var server = ServeStreamManagementAsync();
        try
        {
            using var session = new LibstropheSession(account, "fixture-password", caFile, endpoint);
            while (!session.Disconnected)
            {
                timeout.Token.ThrowIfCancellationRequested();
                session.Poll();
            }
            Assert.True(session.StreamEstablished, session.PermanentFailure);

            using var closedPort = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            closedPort.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            session.Connect(endpoint with { Port = (ushort)((IPEndPoint)closedPort.LocalEndPoint!).Port });
            while (!session.Disconnected)
            {
                timeout.Token.ThrowIfCancellationRequested();
                session.Poll();
            }

            session.Connect(endpoint);
            while (!session.Connected && !session.Disconnected)
            {
                timeout.Token.ThrowIfCancellationRequested();
                session.Poll();
            }
            if (!session.Connected) await server.WaitAsync(timeout.Token);
            Assert.True(session.Connected, session.PermanentFailure);
            Assert.Equal(serverRemembersStream, session.Resumed);
            session.Close();
            await server.WaitAsync(timeout.Token);
        }
        finally
        {
            await timeout.CancelAsync();
            try { await server; } catch (Exception) when (timeout.IsCancellationRequested) { }
            File.Delete(caFile);
        }

        async Task ServeStreamManagementAsync()
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var tls = new SslStream(socket.GetStream());
                await AuthenticateAsync(tls, certificate, timeout.Token);
                using var reader = MakeReader(tls);
                await AuthenticateXmppAsync(tls, reader, timeout.Token, streamManagement: true);
                if (attempt == 1)
                {
                    var resume = await ReadThroughAsync(reader, "/>", timeout.Token);
                    Assert.Contains("<resume ", resume, StringComparison.Ordinal);
                    Assert.Contains("previd=\"fixture-sm\"", resume, StringComparison.Ordinal);
                    if (serverRemembersStream)
                        await WriteAsync(tls, "<resumed xmlns='urn:xmpp:sm:3' previd='fixture-sm' h='0'/>", timeout.Token);
                    else
                        await WriteAsync(tls, "<failed xmlns='urn:xmpp:sm:3'><item-not-found xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></failed>", timeout.Token);
                }
                if (attempt == 0 || !serverRemembersStream)
                {
                    await BindXmppAsync(tls, reader, timeout.Token);
                    Assert.Contains("<enable ", await ReadThroughAsync(reader, "/>", timeout.Token), StringComparison.Ordinal);
                    await WriteAsync(tls, "<enabled xmlns='urn:xmpp:sm:3' id='fixture-sm' resume='true'/>", timeout.Token);
                }
                if (attempt == 1)
                {
                    await ReadThroughAsync(reader, "</stream:stream>", timeout.Token);
                    await WriteAsync(tls, "</stream:stream>", timeout.Token);
                }
            }
        }
    }

    private static async Task ServeAsync(TcpListener listener, XmppTlsMode mode, X509Certificate2 certificate,
        bool expectedSuccess, TaskCompletionSource<string> received, TaskCompletionSource<long> sentAt, CancellationToken token)
    {
        using var socket = await listener.AcceptTcpClientAsync(token);
        await using var network = socket.GetStream();
        if (mode == XmppTlsMode.StartTls)
        {
            using var plaintext = MakeReader(network);
            await ReadOpeningAsync(plaintext, token);
            await WriteAsync(network, StreamHeader +
                "<stream:features><starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'><required/></starttls></stream:features>", token);
            Assert.Contains("<starttls", await ReadThroughAsync(plaintext, "/>", token));
            await WriteAsync(network, "<proceed xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>", token);
        }

        await using var tls = new SslStream(network);
        try { await AuthenticateAsync(tls, certificate, token); }
        catch (Exception ex) when (!expectedSuccess && ex is AuthenticationException or IOException) { return; }
        if (!expectedSuccess) return;
        using var reader = MakeReader(tls);
        await AuthenticateXmppAsync(tls, reader, token);
        await BindXmppAsync(tls, reader, token);
        received.TrySetResult(await ReadThroughAsync(reader, "</message>", token));
        sentAt.TrySetResult(Stopwatch.GetTimestamp());
        await WriteAsync(tls, $"<message type='chat' from='directorbot@{Domain}/bot' to='pilot@{Domain}/HISA-test' id='ping' xml:lang='en'>" +
            "<body>fleet now</body></message>", token);
        await ReadThroughAsync(reader, "</stream:stream>", token);
        await WriteAsync(tls, "</stream:stream>", token);
    }

    private static async Task AuthenticateXmppAsync(Stream tls, StreamReader reader, CancellationToken token, bool streamManagement = false)
    {
        await ReadOpeningAsync(reader, token);
        await WriteAsync(tls, StreamHeader +
            "<stream:features><mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'><mechanism>PLAIN</mechanism></mechanisms></stream:features>", token);
        Assert.Contains("mechanism=\"PLAIN\"", await ReadThroughAsync(reader, "</auth>", token));
        await WriteAsync(tls, "<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>", token);
        await ReadOpeningAsync(reader, token);
        await WriteAsync(tls, StreamHeader +
            "<stream:features><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/>" +
            (streamManagement ? "<sm xmlns='urn:xmpp:sm:3'/>" : "") + "</stream:features>", token);
    }

    private static async Task BindXmppAsync(Stream tls, StreamReader reader, CancellationToken token)
    {
        var rawBinding = await ReadThroughAsync(reader, "</iq>", token);
        var binding = XElement.Parse(rawBinding[rawBinding.IndexOf("<iq", StringComparison.Ordinal)..]);
        var id = (string?)binding.Attribute("id");
        Assert.NotNull(id);
        await WriteAsync(tls, $"<iq type='result' id='{id}'><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>" +
            $"<jid>pilot@{Domain}/HISA-test</jid></bind></iq>", token);
    }

    private static Task AuthenticateAsync(SslStream tls, X509Certificate2 certificate, CancellationToken token) =>
        tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = SslProtocols.Tls12
        }, token);

    private static StreamReader MakeReader(Stream stream) => new(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

    private static async Task WriteAsync(Stream stream, string content, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), token);
        await stream.FlushAsync(token);
    }

    private static async Task ReadOpeningAsync(StreamReader reader, CancellationToken token)
    {
        var opening = await ReadThroughAsync(reader, ">", token);
        if (opening.StartsWith("<?xml", StringComparison.Ordinal)) opening = await ReadThroughAsync(reader, ">", token);
        Assert.StartsWith("<stream:stream", opening, StringComparison.Ordinal);
    }

    private static async Task<string> ReadThroughAsync(StreamReader reader, string end, CancellationToken token, string? alternative = null)
    {
        var result = new StringBuilder();
        var character = new char[1];
        while (result.Length < 8192 && await reader.ReadAsync(character.AsMemory(), token) > 0)
        {
            result.Append(character[0]);
            if (character[0] == '>')
            {
                var current = result.ToString();
                if (current.EndsWith(end, StringComparison.Ordinal) ||
                    alternative is not null && current.EndsWith(alternative, StringComparison.Ordinal)) break;
            }
        }
        return result.ToString();
    }

    private static X509Certificate2 CreateAuthority(RSA key)
    {
        var request = new CertificateRequest("CN=HISA XMPP transport test CA", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 ca, RSA key, string dns)
    {
        var request = new CertificateRequest($"CN={dns}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dns);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using var certificate = request.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(20), [1, 2, 3]);
        if (!OperatingSystem.IsWindows()) return certificate.CopyWithPrivateKey(key);
        // Schannel needs a key container. The import owns it until disposal.
        using var ephemeral = certificate.CopyWithPrivateKey(key);
        var pfx = ephemeral.Export(X509ContentType.Pfx, "fixture");
        try { return X509CertificateLoader.LoadPkcs12(pfx, "fixture", X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
}

public sealed class XmppNativeTheoryAttribute : TheoryAttribute
{
    public XmppNativeTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("HISA_TEST_XMPP_NATIVE") != "1")
            Skip = "Set HISA_TEST_XMPP_NATIVE=1 and supply the packaged library to run the transport tests.";
    }
}

