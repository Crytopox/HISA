using System.Net;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Hisa.Core.Models;

namespace Hisa.Services.Xmpp;

internal sealed class LibstropheSession : IDisposable
{
    private readonly Native.ConnectionHandler _connectionHandler;
    private readonly Native.StanzaHandler _stanzaHandler;
    private readonly Native.CertHandler _certHandler;
    private readonly Native.LogHandler _logHandler;
    private IntPtr _logger;
    private IntPtr _context;
    private IntPtr _connection;
    private bool _initialized;
    private bool _transportCertificateFailed;

    public Queue<XElement> Incoming { get; } = new();
    public bool Connected { get; private set; }
    public bool Disconnected { get; private set; }
    public bool Resumed { get; private set; }
    public bool StreamEstablished { get; private set; }
    public string? PermanentFailure { get; private set; }

    public LibstropheSession(XmppAccount account, string password, string? caFile, XmppEndpoint? endpoint = null)
    {
        _connectionHandler = OnConnection;
        _stanzaHandler = OnStanza;
        _certHandler = RejectCertificate;
        _logHandler = OnNativeLog;
        try
        {
            Native.xmpp_initialize();
            _initialized = true;
            if (Native.xmpp_version_check(0, 14) == 0)
                throw new InvalidOperationException("The installed Jabber library is unsupported.");

            _logger = Marshal.AllocHGlobal(2 * IntPtr.Size);
            Marshal.WriteIntPtr(_logger, Marshal.GetFunctionPointerForDelegate(_logHandler));
            Marshal.WriteIntPtr(_logger, IntPtr.Size, IntPtr.Zero);
            _context = Native.xmpp_ctx_new(IntPtr.Zero, _logger);
            if (_context == IntPtr.Zero) throw new InvalidOperationException("Could not initialize Jabber.");
            _connection = Native.xmpp_conn_new(_context);
            if (_connection == IntPtr.Zero) throw new InvalidOperationException("Could not initialize the Jabber connection.");

            Native.xmpp_conn_set_certfail_handler(_connection, _certHandler);
            if (caFile is not null) Native.xmpp_conn_set_cafile(_connection, caFile);
            Native.xmpp_conn_set_jid(_connection, $"{account.Jid}/{account.Resource}");
            Native.xmpp_conn_set_pass(_connection, password);
            Native.xmpp_handler_add(_connection, _stanzaHandler, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (endpoint is not null) Connect(endpoint);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Connect(XmppEndpoint endpoint)
    {
        Incoming.Clear();
        Connected = Disconnected = StreamEstablished = Resumed = _transportCertificateFailed = false;
        PermanentFailure = null;
        // Keep the connection object across retries. Its XEP-0198 state
        // includes the bound JID, which libstrophe's serialized state omits.
        const int mandatoryTls = 1 << 1;
        const int directTls = 1 << 2;
        if (Native.xmpp_conn_set_flags(_connection, new CLong(mandatoryTls | (endpoint.DirectTls ? directTls : 0))) != 0)
            throw new InvalidOperationException("Could not require secure Jabber transport.");
        Disconnected = Native.xmpp_connect_client(_connection, endpoint.Host, endpoint.Port, _connectionHandler, IntPtr.Zero) != 0;
    }

    public void Abort()
    {
        if (!Disconnected) Native.xmpp_hisa_abort(_connection);
    }

    public void Poll() => Native.xmpp_run_once(_context, new CULong(35));

    public void Send(XElement stanza)
    {
        var handle = Native.xmpp_stanza_new_from_string(_context, stanza.ToString(SaveOptions.DisableFormatting));
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Could not prepare a Jabber stanza.");
        try { Native.xmpp_send(_connection, handle); }
        finally { Native.xmpp_stanza_release(handle); }
    }

    public void Close()
    {
        if (Disconnected) return;
        Native.xmpp_disconnect(_connection);
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!Disconnected && started.Elapsed < TimeSpan.FromMilliseconds(300)) Poll();
    }

    private void OnConnection(IntPtr connection, int state, int error, IntPtr streamError, IntPtr data)
    {
        Connected = state == 0 && Native.xmpp_conn_is_secured(connection) != 0;
        StreamEstablished |= Connected;
        Disconnected = state is 2 or 3 || (state == 0 && !Connected);
        if (state == 0 && !Connected)
            PermanentFailure = "The server did not establish a secure Jabber connection.";
        if (streamError != IntPtr.Zero && Marshal.ReadInt32(streamError) is 2 or 12 or 13)
            PermanentFailure = "The server rejected this Jabber session. Check the account and server settings.";
    }

    private int OnStanza(IntPtr connection, IntPtr stanza, IntPtr data)
    {
        if (Native.xmpp_stanza_to_text(stanza, out var buffer, out var length) != 0) return 1;
        try
        {
            if (length > 1024 * 1024 || Incoming.Count >= 1024)
            {
                Disconnected = true;
                PermanentFailure = "An incoming Jabber stanza exceeded the processing limit.";
                return 1;
            }

            var xml = Marshal.PtrToStringUTF8(buffer, checked((int)length));
            if (xml is null) return 1;
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            });
            Incoming.Enqueue(XElement.Load(reader, LoadOptions.PreserveWhitespace));
        }
        catch (XmlException)
        {
            // A stanza that cannot be represented safely is ignored.
        }
        finally { Native.xmpp_free(_context, buffer); }
        return 1;
    }

    private int RejectCertificate(IntPtr certificate, IntPtr error)
    {
        _transportCertificateFailed = true;
        PermanentFailure = "The Jabber server certificate could not be verified.";
        return 0;
    }

    private void OnNativeLog(IntPtr data, int level, IntPtr areaPointer, IntPtr messagePointer)
    {
        // Consume fixed library status markers only. Never log raw library output.
        var area = Marshal.PtrToStringUTF8(areaPointer);
        if (area == "hisa")
        {
            Resumed = Marshal.PtrToStringUTF8(messagePointer) == "stream-resumed";
            return;
        }
        if (level != 3 || _transportCertificateFailed || area is not ("auth" or "xmpp" or "tls")) return;
        var message = Marshal.PtrToStringUTF8(messagePointer);
        if (area == "auth" && message == "Cannot authenticate with known methods")
            PermanentFailure = "Jabber authentication failed. Check the password and server settings.";
        else if (area == "xmpp" && message?.StartsWith("TLS is not supported, but set as", StringComparison.Ordinal) == true)
            PermanentFailure = "The server did not offer the required TLS connection.";
    }

    public void Dispose()
    {
        if (_connection != IntPtr.Zero) Native.xmpp_conn_release(_connection);
        if (_context != IntPtr.Zero) Native.xmpp_ctx_free(_context);
        if (_logger != IntPtr.Zero) Marshal.FreeHGlobal(_logger);
        if (_initialized) Native.xmpp_shutdown();
        _connection = _context = _logger = IntPtr.Zero;
        _initialized = false;
        GC.KeepAlive(this);
    }

    private static class Native
    {
        private const string Library = "hisa-strophe";
        private const CallingConvention Cdecl = CallingConvention.Cdecl;

        static Native()
        {
            NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, _, _) =>
            {
                if (name != Library) return IntPtr.Zero;
                var file = OperatingSystem.IsWindows() ? "hisa-strophe.dll"
                    : OperatingSystem.IsMacOS() ? "libhisa-strophe.dylib" : "libhisa-strophe.so";
                return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "XmppNative", file));
            });
        }

        [UnmanagedFunctionPointer(Cdecl)] internal delegate void ConnectionHandler(IntPtr conn, int state, int error, IntPtr streamError, IntPtr data);
        [UnmanagedFunctionPointer(Cdecl)] internal delegate int StanzaHandler(IntPtr conn, IntPtr stanza, IntPtr data);
        [UnmanagedFunctionPointer(Cdecl)] internal delegate int CertHandler(IntPtr cert, IntPtr error);
        [UnmanagedFunctionPointer(Cdecl)] internal delegate void LogHandler(IntPtr data, int level, IntPtr area, IntPtr message);

        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_initialize();
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_shutdown();
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern int xmpp_version_check(int major, int minor);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern IntPtr xmpp_ctx_new(IntPtr mem, IntPtr log);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_ctx_free(IntPtr ctx);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_free(IntPtr ctx, IntPtr memory);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern IntPtr xmpp_conn_new(IntPtr ctx);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern int xmpp_conn_release(IntPtr conn);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern int xmpp_conn_set_flags(IntPtr conn, CLong flags);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern int xmpp_conn_is_secured(IntPtr conn);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_conn_set_certfail_handler(IntPtr conn, CertHandler callback);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_conn_set_cafile(IntPtr conn, [MarshalAs(UnmanagedType.LPUTF8Str)] string file);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_conn_set_jid(IntPtr conn, [MarshalAs(UnmanagedType.LPUTF8Str)] string jid);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_conn_set_pass(IntPtr conn, [MarshalAs(UnmanagedType.LPUTF8Str)] string password);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_hisa_abort(IntPtr conn);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern int xmpp_connect_client(IntPtr conn, [MarshalAs(UnmanagedType.LPUTF8Str)] string? host, ushort port, ConnectionHandler handler, IntPtr data);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_disconnect(IntPtr conn);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_run_once(IntPtr context, CULong timeout);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_handler_add(IntPtr conn, StanzaHandler handler, IntPtr ns, IntPtr name, IntPtr type, IntPtr data);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern IntPtr xmpp_stanza_new_from_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string xml);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern int xmpp_stanza_release(IntPtr stanza);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern int xmpp_stanza_to_text(IntPtr stanza, out IntPtr buffer, out nuint length);
        [DllImport(Library, CallingConvention = Cdecl)] internal static extern void xmpp_send(IntPtr conn, IntPtr stanza);
    }
}

internal sealed record XmppEndpoint(string? Host, ushort Port, bool DirectTls)
{
    public static IReadOnlyList<XmppEndpoint> Candidates(XmppAccount account)
    {
        var direct = account.TlsMode == XmppTlsMode.DirectTls;
        if (account.Host is not null || account.Port != 0)
            return [new(account.Host ?? XmppAddresses.Domain(account.Jid), account.Port, direct)];
        if (account.TlsMode == XmppTlsMode.Automatic)
            return [new(null, 0, false), new(null, 0, true)];
        return [new(null, 0, direct)];
    }

    public static bool ValidHost(string? host) => host is null ||
        Uri.CheckHostName(host) != UriHostNameType.Unknown || IPAddress.TryParse(host, out _);
}

