using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Xmpp;

public sealed partial class XmppClientService : BackgroundService
{
    private const string SettingsKey = "Xmpp.Client";
    private const string DisplayPreferencesKey = "Xmpp.DisplayPreferences";
    private const string AlertSoundPreferencesKey = "Xmpp.AlertSoundPreferences";
    private const string OwnPresencePreferencesKey = "Xmpp.OwnPresence";
    private const string OwnAvatarHashKey = "Xmpp.OwnAvatarHash";
    private const string MessageFiltersKey = "Xmpp.MessageFilters";
    private const string TrustedLinkDomainsKey = "Xmpp.TrustedLinkDomains";
    private const string PendingCredentialRemoval = "Removal of a saved Jabber password is pending. HISA will retry when the credential store is available.";
    private readonly ISettingsService _settings;
    private readonly XmppHistoryStore _history;
    private readonly ILogger<XmppClientService> _logger;
    private readonly IXmppCredentialStore _credentials;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _desktopReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<(string Account, string Room), string> _roomPasswords = [];
    private readonly object _roomPasswordGate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, XmppRoomStatus> _roomStatus = new(StringComparer.Ordinal);
    private readonly object _topicSync = new();
    private readonly HashSet<string> _topicSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<XmppTopicChange> _topicChanges = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<XmppOccupant>> _occupants =
        new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (XmppContactProfile Profile, DateTimeOffset CachedAt)> _profiles =
        new(StringComparer.Ordinal);
    private SavedSettings _saved = new(null, [], []);
    private XmppDisplayPreferences _displayPreferences = new();
    private XmppAlertSoundPreferences _alertSoundPreferences = new();
    private XmppOwnPresencePreferences _ownPresence = new();
    private string? _ownAvatarHash;
    private IReadOnlyList<XmppMessageFilter> _messageFilters = [];
    private HashSet<string> _trustedLinkHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> _ruleCooldownUntil = new();
    private CancellationToken _appToken;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _session;
    private Channel<Request>? _requests;
    private (string Account, string Password)? _sessionCredential;
    private string _status = "Disconnected";
    private IReadOnlyList<XmppContact> _contacts = [];
    private IReadOnlySet<string> _joinedRooms = new HashSet<string>();
    private bool _online;

    public XmppClientService(ISettingsService settings, XmppHistoryStore history, ILogger<XmppClientService> logger)
        : this(settings, history, logger, new XmppCredentialStore()) { }

    internal XmppClientService(ISettingsService settings, XmppHistoryStore history,
        ILogger<XmppClientService> logger, IXmppCredentialStore credentials)
    {
        _settings = settings;
        _history = history;
        _logger = logger;
        _credentials = credentials;
    }

    public XmppAccount? Account => Volatile.Read(ref _saved).Account;
    public IReadOnlyList<XmppRoom> Rooms => Volatile.Read(ref _saved).Rooms;
    public IReadOnlyList<XmppAlertRule> Rules => Volatile.Read(ref _saved).Rules;
    public IReadOnlyList<XmppContact> Contacts => Volatile.Read(ref _contacts);
    public IReadOnlySet<string> JoinedRooms => Volatile.Read(ref _joinedRooms);
    public string Status => Volatile.Read(ref _status);
    public bool Online => Volatile.Read(ref _online);
    public XmppDisplayPreferences DisplayPreferences => Volatile.Read(ref _displayPreferences);
    public XmppAlertSoundPreferences AlertSoundPreferences => Volatile.Read(ref _alertSoundPreferences);
    public XmppOwnPresencePreferences OwnPresencePreferences => Volatile.Read(ref _ownPresence);
    public string? OwnAvatarHash => Volatile.Read(ref _ownAvatarHash);
    public IReadOnlyList<XmppMessageFilter> MessageFilters => Volatile.Read(ref _messageFilters);
    public bool IsTrustedLinkHost(string host) => _trustedLinkHosts.Contains(host);

    public async Task TrustLinkHostAsync(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        await ReadyAsync();
        _trustedLinkHosts.Add(host.Trim());
        await _settings.SetAsync(TrustedLinkDomainsKey, _trustedLinkHosts.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(), _appToken);
    }
    public XmppRoomStatus RoomStatus(string jid) => _roomStatus.GetValueOrDefault(jid) ?? new();
    public IReadOnlyList<XmppOccupant> Occupants(string roomJid) =>
        _occupants.GetValueOrDefault(roomJid) ?? Array.Empty<XmppOccupant>();

    public event Action? StateChanged;
    public event Action<XmppMessage>? MessageReceived;
    public event Action<XmppAlert>? AlertReceived;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _appToken = stoppingToken;
        try
        {
            await _history.InitializeAsync(stoppingToken);
            await _history.PruneAsync(stoppingToken);
            _saved = await _settings.GetAsync<SavedSettings>(SettingsKey, stoppingToken) ?? _saved;
            _displayPreferences = await _settings.GetAsync<XmppDisplayPreferences>(DisplayPreferencesKey, stoppingToken) ?? new();
            _alertSoundPreferences = await _settings.GetAsync<XmppAlertSoundPreferences>(AlertSoundPreferencesKey, stoppingToken) ?? new();
            _ownPresence = await _settings.GetAsync<XmppOwnPresencePreferences>(OwnPresencePreferencesKey, stoppingToken) ?? new();
            if (_ownPresence.Show is XmppPresenceShow.Offline)
                _ownPresence = _ownPresence with { Show = XmppPresenceShow.Online };
            _ownAvatarHash = await _settings.GetAsync<string>(OwnAvatarHashKey, stoppingToken);
            _messageFilters = await _settings.GetAsync<List<XmppMessageFilter>>(MessageFiltersKey, stoppingToken) ?? [];
            _trustedLinkHosts = new(
                await _settings.GetAsync<List<string>>(TrustedLinkDomainsKey, stoppingToken) ?? [],
                StringComparer.OrdinalIgnoreCase);
            if (_saved.Account is { } configured)
            {
                var rules = ApplySystemAlertRules(configured.Jid, _saved.Rules);
                if (!ReferenceEquals(rules, _saved.Rules))
                    await SaveAsync(_saved with { Rules = rules });
            }
            _ready.TrySetResult();
            if (_saved.Account is { AutoConnect: true, RememberPassword: true } account)
            {
                await _desktopReady.Task.WaitAsync(stoppingToken);
                await _gate.WaitAsync(stoppingToken);
                try
                {
                    if (Account == account && _session is null && !IsCredentialPending(AccountKey(account.Jid)))
                    {
                        var password = await Task.Run(() => _credentials.Read(AccountKey(account.Jid)), stoppingToken);
                        if (password is null) SetState("Enter your Jabber password to connect.");
                        else
                        {
                            _sessionCredential = (account.Jid, password);
                            StartSession(account, password);
                        }
                    }
                }
                finally { _gate.Release(); }
            }
            StateChanged?.Invoke();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _ready.TrySetCanceled(stoppingToken);
            return;
        }
        catch (Exception ex)
        {
            LogFailure("Initial setup", ex);
            SetState("Jabber local storage could not be opened.");
            _ready.TrySetException(new InvalidOperationException(Status));
            return;
        }

        try
        {
            if (_saved.PendingCredentialDeletes.Count > 0)
            {
                await _desktopReady.Task.WaitAsync(stoppingToken);
                await RetryCredentialCleanupAsync(stoppingToken);
            }
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await _history.PruneAsync(stoppingToken); StateChanged?.Invoke(); }
                catch (Exception ex) when (ex is not OperationCanceledException) { LogFailure("History cleanup", ex); }
                await RetryCredentialCleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { if (_session is not null) await _session; }
    }

    public Task ReadyAsync() => _ready.Task;
    public void DesktopReady() => _desktopReady.TrySetResult();

    public async Task<string?> ConfigureAsync(string jid, string? host, ushort port, XmppTlsMode mode,
        string? password, bool remember, bool autoConnect, bool connect)
    {
        var address = XmppAddresses.Bare(jid.Trim());
        host = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        if (address is null || jid.Contains('/')) throw new ArgumentException("Enter an account address like name@example.org.");
        if (!XmppEndpoint.ValidHost(host)) throw new ArgumentException("Enter a valid server host.");
        if (host is not null && Uri.CheckHostName(host) == UriHostNameType.Dns)
            host = new System.Globalization.IdnMapping { UseStd3AsciiRules = true }.GetAscii(host);
        if (password?.Contains('\0') == true) throw new ArgumentException("The password contains an unsupported character.");

        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            await DeletePendingCredentialsAsync();
            var prior = _saved;
            var old = prior.Account;
            var sameAccount = old?.Jid == address;
            var key = AccountKey(address);
            var alreadySaved = sameAccount && old!.RememberPassword && !IsCredentialPending(key);
            var account = new XmppAccount(address, host, port, mode, remember && alreadySaved, remember && alreadySaved && autoConnect,
                sameAccount ? old!.Resource : $"HISA-{Guid.NewGuid():N}"[..13]);
            var currentSecret = password;
            if (string.IsNullOrEmpty(currentSecret) && sameAccount)
            {
                currentSecret = _sessionCredential is { } cached && cached.Account == address ? cached.Password : null;
                if (currentSecret is null && alreadySaved)
                    currentSecret = await Task.Run(() => _credentials.Read(key));
            }
            var sameConnection = sameAccount && old!.Host == account.Host && old.Port == port && old.TlsMode == mode &&
                string.IsNullOrEmpty(password);
            if (!sameConnection) await StopSessionAsync();
            var pending = prior.PendingCredentialDeletes.ToHashSet(StringComparer.Ordinal);
            if (old?.RememberPassword == true && (!sameAccount || !remember))
                pending.Add(AccountKey(old.Jid));
            if (!sameAccount && old is not null)
                foreach (var room in prior.Rooms.Where(room => room.HasPassword))
                    pending.Add(RoomKey(old.Jid, room.Jid));

            var savePassword = remember && !string.IsNullOrEmpty(currentSecret) && (!alreadySaved || !string.IsNullOrEmpty(password));
            if (savePassword)
                account = account with { RememberPassword = false, AutoConnect = false };
            var settings = new SavedSettings(account, sameAccount ? Rooms : [],
                ApplySystemAlertRules(address, sameAccount ? Rules : [])) { PendingCredentialDeletes = [.. pending] };
            if (savePassword)
                await SaveCredentialAsync(key, currentSecret!, settings,
                    settings with { Account = account with { RememberPassword = true, AutoConnect = autoConnect } });
            else
                await SaveAsync(settings);

            account = Account!;
            if (!sameAccount && old is not null)
            {
                Volatile.Write(ref _contacts, []);
                _roomStatus.Clear();
                _occupants.Clear();
                _profiles.Clear();
                lock (_roomPasswordGate) _roomPasswords.Clear();
            }

            _sessionCredential = string.IsNullOrEmpty(currentSecret) ? null : (address, currentSecret);
            if (connect && (_session is null || _session.IsCompleted))
            {
                if (string.IsNullOrEmpty(currentSecret)) SetState("Enter your Jabber password to connect.");
                else StartSession(account, currentSecret);
            }
            var notice = remember && !account.RememberPassword
                ? "HISA could not save the password securely. Any entered password is available for this session only." : null;
            return await CredentialFeedbackAsync(notice);
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await ReadyAsync();
        await _gate.WaitAsync();
        try { await StopSessionAsync(); SetState("Disconnected"); }
        finally { _gate.Release(); }
    }

    public async Task<string?> SaveRoomAsync(string jid, string nickname, bool autoJoin, string? password,
        bool rememberPassword, bool join)
    {
        var roomJid = XmppAddresses.Bare(jid.Trim());
        if (roomJid is null || jid.Contains('/')) throw new ArgumentException("Enter a room address like room@conference.example.org.");
        nickname = ValidateRoomNickname(nickname);
        if (password is not null)
        {
            try { System.Xml.XmlConvert.VerifyXmlChars(password); }
            catch (System.Xml.XmlException) { throw new ArgumentException("The room password contains unsupported characters."); }
        }
        await ReadyAsync();
        await _gate.WaitAsync();
        XmppRoom room;
        string? warning = null;
        try
        {
            await DeletePendingCredentialsAsync();
            var account = RequireAccount();
            var old = Rooms.FirstOrDefault(item => item.Jid == roomJid);
            var key = RoomKey(account.Jid, roomJid);
            var savePassword = rememberPassword && !string.IsNullOrEmpty(password);
            room = new(roomJid, nickname, autoJoin, rememberPassword && !savePassword && old?.HasPassword == true, old?.Group);
            var otherRooms = Rooms.Where(item => item.Jid != roomJid).ToArray();
            var settings = _saved with { Rooms = [.. otherRooms, room] };
            if (!rememberPassword && old?.HasPassword == true)
                settings = AddPendingCredential(settings, key);
            if (savePassword)
            {
                var remembered = room with { HasPassword = true };
                if (await SaveCredentialAsync(key, password!, settings, settings with { Rooms = [.. otherRooms, remembered] }))
                    room = remembered;
                else
                    warning = "The room password is available for this HISA session only.";
            }
            else
                await SaveAsync(settings);
            lock (_roomPasswordGate)
            {
                if (!string.IsNullOrEmpty(password)) _roomPasswords[(account.Jid, roomJid)] = password;
                else if (!rememberPassword && old?.HasPassword == true) _roomPasswords.Remove((account.Jid, roomJid));
            }
            warning = await CredentialFeedbackAsync(warning);
        }
        finally { _gate.Release(); }

        if (join) await RequestAsync(async protocol => { await protocol.JoinAsync(room); return true; });
        return warning;
    }

    public async Task<string?> RemoveRoomAsync(string jid)
    {
        await ReadyAsync();
        if (Online) await LeaveRoomAsync(jid);
        await _gate.WaitAsync();
        try
        {
            var account = RequireAccount();
            var settings = _saved with { Rooms = Rooms.Where(item => item.Jid != jid).ToArray() };
            if (Rooms.FirstOrDefault(room => room.Jid == jid)?.HasPassword == true)
                settings = AddPendingCredential(settings, RoomKey(account.Jid, jid));
            await SaveAsync(settings);
            lock (_roomPasswordGate) _roomPasswords.Remove((account.Jid, jid));
            return await CredentialFeedbackAsync(null);
        }
        finally { _gate.Release(); }
    }

    public async Task SetRoomGroupAsync(string jid, string? group)
    {
        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            var room = Rooms.FirstOrDefault(item => item.Jid == jid)
                ?? throw new ArgumentException("Save the room before setting a folder.");
            var name = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
            if (name is { Length: > 80 }) throw new ArgumentException("Folder names can be at most 80 characters.");
            await SaveAsync(_saved with
            {
                Rooms = Rooms.Select(item => item.Jid == jid ? item with { Group = name } : item).ToArray()
            });
        }
        finally { _gate.Release(); }
    }

    public async Task MoveRoomAsync(string jid, int delta)
    {
        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            _ = RequireAccount();
            var rooms = Rooms.ToList();
            var index = rooms.FindIndex(room => room.Jid == jid);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= rooms.Count) return;
            (rooms[index], rooms[target]) = (rooms[target], rooms[index]);
            await SaveAsync(_saved with { Rooms = rooms.ToArray() });
        }
        finally { _gate.Release(); }
    }

    public Task LeaveRoomAsync(string jid) => RequestAsync(protocol =>
    {
        protocol.Leave(jid);
        return Task.FromResult(true);
    });

    public Task JoinSavedRoomAsync(string jid)
    {
        var room = Rooms.FirstOrDefault(room => room.Jid == jid) ??
            throw new InvalidOperationException("Save the room before joining.");
        return RequestAsync(async protocol => { await protocol.JoinAsync(room); return true; });
    }

    public Task<IReadOnlyList<XmppDiscoveredRoom>> BrowseAsync(string? service) =>
        RequestAsync(protocol => protocol.BrowseAsync(string.IsNullOrWhiteSpace(service) ? null : service.Trim()));

    public Task SendAsync(XmppConversation conversation, string body)
    {
        ValidateMessageText(body);
        if (XmppAddresses.Bare(conversation.Jid) != conversation.Jid)
            throw new ArgumentException("The conversation address is invalid.");
        return RequestAsync(async protocol => { await protocol.SendAsync(conversation, body); return true; });
    }

    private static void ValidateMessageText(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || System.Text.Encoding.UTF8.GetByteCount(body) > 64 * 1024)
            throw new ArgumentException("Enter a message up to 64 KB.");
        try { System.Xml.XmlConvert.VerifyXmlChars(body); }
        catch (System.Xml.XmlException) { throw new ArgumentException("The message contains unsupported characters."); }
    }

    private static string ValidateRoomNickname(string nickname)
    {
        nickname = nickname.Trim();
        if (nickname.Length == 0 || nickname.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(nickname) > 1023)
            throw new ArgumentException("Enter a valid room nickname.");
        return nickname;
    }

    public Task UpdateContactAsync(string jid, string action)
    {
        var address = XmppAddresses.Bare(jid.Trim());
        if (address is null || jid.Contains('/')) throw new ArgumentException("Enter a valid contact address.");
        return RequestAsync(async protocol => { await protocol.UpdateContactAsync(address, action); return true; });
    }

    public async Task SaveRuleAsync(XmppAlertRule rule)
    {
        if (rule.SourceScope == XmppAlertSourceScope.Conversation &&
            XmppAddresses.Bare(rule.Source.Jid) != rule.Source.Jid)
            throw new ArgumentException("Enter a valid alert source.");
        if (rule.Match is XmppAlertMatch.ContainsText or XmppAlertMatch.EqualsText or XmppAlertMatch.Regex &&
            string.IsNullOrWhiteSpace(rule.Text))
            throw new ArgumentException("Enter the text to match.");
        if (rule.Match == XmppAlertMatch.Regex)
        {
            try { _ = new System.Text.RegularExpressions.Regex(rule.Text!); }
            catch (ArgumentException) { throw new ArgumentException("Enter a valid regular expression."); }
        }
        if (rule.Enabled && !rule.Popup && rule.Sound is null && !rule.ShowInFleetFeed && !rule.Highlight)
            throw new ArgumentException("Select a popup, sound, highlight, or notification-feed action.");
        if (rule.CooldownSeconds < 0) throw new ArgumentException("Cooldown cannot be negative.");
        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            var account = RequireAccount();
            var rules = Rules.ToList();
            var index = rules.FindIndex(item => item.Id == rule.Id);
            if (index >= 0 && rules[index].FleetPings)
                rule = rule with
                {
                    Source = XmppAddresses.Directorbot(account.Jid),
                    Match = XmppAlertMatch.AnyMessage,
                    Text = null,
                    FleetPings = true,
                    SourceScope = XmppAlertSourceScope.Conversation,
                    Sender = null,
                    ShowInFleetFeed = true,
                    FeedTitle = string.IsNullOrWhiteSpace(rule.FeedTitle) ? "Fleet ping" : rule.FeedTitle
                };
            else
                rule = rule with
                {
                    FleetPings = false,
                    CooldownSeconds = Math.Clamp(rule.CooldownSeconds, 0, 86_400),
                    FeedTitle = string.IsNullOrWhiteSpace(rule.FeedTitle) ? null : rule.FeedTitle.Trim(),
                    FeedSubtitle = string.IsNullOrWhiteSpace(rule.FeedSubtitle) ? null : rule.FeedSubtitle.Trim()
                };
            if (index < 0) rules.Add(rule);
            else rules[index] = rule;
            await SaveAsync(_saved with { Rules = rules });
        }
        finally { _gate.Release(); }
    }

    public async Task SaveMessageFiltersAsync(IReadOnlyList<XmppMessageFilter> filters)
    {
        await ReadyAsync();
        var cleaned = filters
            .Where(filter => !string.IsNullOrWhiteSpace(filter.Sender))
            .Select(filter => filter with { Sender = filter.Sender.Trim() })
            .ToArray();
        Volatile.Write(ref _messageFilters, cleaned);
        await _settings.SetAsync(MessageFiltersKey, cleaned, _appToken);
        StateChanged?.Invoke();
    }

    public async Task RemoveRuleAsync(Guid id)
    {
        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            if (Rules.Any(rule => rule.Id == id && rule.FleetPings))
                throw new InvalidOperationException("Edit the Directorbot rule to disable it.");
            await SaveAsync(_saved with { Rules = Rules.Where(rule => rule.Id != id).ToArray() });
        }
        finally { _gate.Release(); }
    }

    public Task<IReadOnlyList<XmppMessage>> ReadAsync(XmppConversation? conversation, bool pings, XmppMessage? before = null,
        XmppMessage? from = null) =>
        _history.ReadAsync(RequireAccount().Jid, conversation, pings, before?.Timestamp, beforeId: before?.LocalId ?? 0, from: from);
    public Task<IReadOnlyDictionary<XmppConversation, int>> CountsAsync() => _history.CountsAsync(RequireAccount().Jid);
    public Task<bool> HasEarlierAsync(XmppConversation? conversation, bool pings, XmppMessage before) =>
        _history.HasEarlierAsync(RequireAccount().Jid, pings || conversation is null
            ? XmppAddresses.Directorbot(RequireAccount().Jid) : conversation, pings, before);
    public Task<int> MarkReadAsync(XmppConversation conversation, long throughId) =>
        _history.MarkReadAsync(RequireAccount().Jid, conversation, throughId: throughId);

    public Task<int> FeedUnreadCountAsync() => _history.FeedUnreadCountAsync(RequireAccount().Jid, _appToken);
    public async Task<int> MarkVisibleAsync(IReadOnlyList<XmppMessage> visible, bool feed)
    {
        var account = RequireAccount().Jid;
        var changed = 0;
        foreach (var source in visible.GroupBy(message => message.Conversation))
            changed += await _history.MarkVisibleAsync(account, source.Key, source.ToArray(), _appToken,
                advanceRoomReadPosition: !feed);
        if (changed > 0) StateChanged?.Invoke();
        return changed;
    }

    public Task<XmppWorkspace?> LoadWorkspaceAsync(string account) =>
        _settings.GetAsync<XmppWorkspace>("Xmpp.Workspace/" + account, _appToken);

    public Task SaveWorkspaceAsync(string account, XmppWorkspace workspace) =>
        _settings.SetAsync("Xmpp.Workspace/" + account, workspace, _appToken);

    public async Task SaveDisplayPreferencesAsync(XmppDisplayPreferences preferences)
    {
        await ReadyAsync();
        Volatile.Write(ref _displayPreferences, preferences);
        await _settings.SetAsync(DisplayPreferencesKey, preferences, _appToken);
        StateChanged?.Invoke();
    }

    public async Task SaveAlertSoundPreferencesAsync(XmppAlertSoundPreferences preferences)
    {
        await ReadyAsync();
        preferences = preferences with
        {
            SoftPingDirectVolume = Math.Clamp(preferences.SoftPingDirectVolume, 0, 1),
            SoftPingDirectSound = string.IsNullOrWhiteSpace(preferences.SoftPingDirectSound)
                ? "soft-message.wav" : preferences.SoftPingDirectSound.Trim(),
            SoftPingMentionVolume = Math.Clamp(preferences.SoftPingMentionVolume, 0, 1),
            SoftPingMentionSound = string.IsNullOrWhiteSpace(preferences.SoftPingMentionSound)
                ? "soft-mention.wav" : preferences.SoftPingMentionSound.Trim()
        };
        Volatile.Write(ref _alertSoundPreferences, preferences);
        await _settings.SetAsync(AlertSoundPreferencesKey, preferences, _appToken);
        StateChanged?.Invoke();
    }

    public async Task ResetPreferencesAsync()
    {
        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            var display = new XmppDisplayPreferences();
            var sounds = new XmppAlertSoundPreferences();
            Volatile.Write(ref _displayPreferences, display);
            Volatile.Write(ref _alertSoundPreferences, sounds);
            Volatile.Write(ref _messageFilters, Array.Empty<XmppMessageFilter>());
            await _settings.SetAsync(DisplayPreferencesKey, display, _appToken);
            await _settings.SetAsync(AlertSoundPreferencesKey, sounds, _appToken);
            await _settings.SetAsync(MessageFiltersKey, Array.Empty<XmppMessageFilter>(), _appToken);
            var rules = Account is { } account
                ? ApplySystemAlertRules(account.Jid, [])
                : Array.Empty<XmppAlertRule>();
            await SaveAsync(_saved with { Rules = rules });
        }
        finally { _gate.Release(); }
    }

    public async Task SetOwnPresenceAsync(XmppPresenceShow show, string? statusText = null)
    {
        if (show is XmppPresenceShow.Offline)
            throw new ArgumentException("Choose an available presence.");
        await ReadyAsync();
        var preferences = new XmppOwnPresencePreferences(show,
            string.IsNullOrWhiteSpace(statusText) ? null : statusText.Trim());
        SaveOwnPresencePreferences(preferences);
        await _settings.SetAsync(OwnPresencePreferencesKey, preferences, _appToken);
        if (Online)
            await RequestAsync(async protocol => { await protocol.BroadcastOwnPresenceAsync(); return true; });
    }

    public Task SetAvatarAsync(byte[] bytes, string contentType) =>
        RequestAsync(async protocol => { await protocol.SetAvatarAsync(bytes, contentType); return true; });

    public Task ClearAvatarAsync() =>
        RequestAsync(async protocol => { await protocol.SetAvatarAsync(null, null); return true; });

    internal void SaveOwnPresencePreferences(XmppOwnPresencePreferences preferences)
    {
        if (preferences.Show is XmppPresenceShow.Offline)
            preferences = preferences with { Show = XmppPresenceShow.Online };
        Volatile.Write(ref _ownPresence, preferences);
        StateChanged?.Invoke();
    }

    internal void RememberOwnAvatar(byte[]? bytes, string? contentType)
    {
        var hash = bytes is { Length: > 0 } ? XmppProfileParsing.PhotoSha1Hex(bytes) : null;
        Volatile.Write(ref _ownAvatarHash, hash);
        if (Account is { } account)
        {
            if (bytes is { Length: > 0 })
                _profiles[account.Jid] = (new(account.Jid, bytes, contentType, Online: true,
                    Show: OwnPresencePreferences.Show, StatusText: OwnPresencePreferences.StatusText), DateTimeOffset.UtcNow);
            else
                _profiles.TryRemove(account.Jid, out _);
        }
        _ = PersistOwnAvatarHashAsync(hash);
        StateChanged?.Invoke();
    }

    private async Task PersistOwnAvatarHashAsync(string? hash)
    {
        try { await _settings.SetAsync(OwnAvatarHashKey, hash, _appToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { LogFailure("Avatar hash save", ex); }
    }

    public async Task<XmppContactProfile> EnsureProfileAsync(string jid)
    {
        var address = XmppAddresses.Bare(jid) ?? throw new ArgumentException("Enter a valid contact address.");
        if (_profiles.TryGetValue(address, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(30))
        {
            var contact = Contacts.FirstOrDefault(item => item.Jid == address);
            if (contact is { Online: true })
                return cached.Profile with
                {
                    Online = true,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    Show = contact.EffectiveShow,
                    StatusText = contact.StatusText ?? cached.Profile.StatusText
                };
            return cached.Profile with
            {
                Online = contact?.Online == true,
                Show = contact?.EffectiveShow ?? XmppPresenceShow.Offline
            };
        }

        if (!Online)
        {
            var offline = Contacts.FirstOrDefault(item => item.Jid == address);
            return new(address, Online: offline?.Online == true, Show: offline?.EffectiveShow ?? XmppPresenceShow.Offline,
                StatusText: offline?.StatusText);
        }

        try
        {
            var profile = await RequestAsync(protocol => protocol.FetchProfileAsync(address));
            _profiles[address] = (profile, DateTimeOffset.UtcNow);
            return profile;
        }
        catch (Exception)
        {
            var fallback = Contacts.FirstOrDefault(item => item.Jid == address);
            return new(address, Online: fallback?.Online == true,
                Show: fallback?.EffectiveShow ?? XmppPresenceShow.Offline, StatusText: fallback?.StatusText);
        }
    }

    public async Task ClearHistoryAsync()
    {
        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            var account = RequireAccount();
            await _history.ClearAsync(account.Jid, _appToken);
            StateChanged?.Invoke();
        }
        finally { _gate.Release(); }
    }

    public async Task ClearFeedAsync()
    {
        await ReadyAsync();
        await _gate.WaitAsync();
        try
        {
            await _history.ClearFeedAsync(RequireAccount().Jid, _appToken);
            StateChanged?.Invoke();
        }
        finally { _gate.Release(); }
    }

    private XmppAccount RequireAccount() => Account ?? throw new InvalidOperationException("Set up a Jabber account first.");

    private static IReadOnlyList<XmppAlertRule> ApplySystemAlertRules(string account, IReadOnlyList<XmppAlertRule> existing)
    {
        if (!XmppAddresses.IsGoonfleet(account)) return existing;
        var rules = IncludeFleetRule(account, existing);
        return IncludeDelveBpingRule(rules);
    }

    private static IReadOnlyList<XmppAlertRule> IncludeFleetRule(string account, IReadOnlyList<XmppAlertRule> existing)
    {
        if (existing.Any(rule => rule.FleetPings)) return existing;

        var source = XmppAddresses.Directorbot(account);
        var rules = existing.ToList();
        var index = rules.FindIndex(rule => rule.Source == source && rule.Match == XmppAlertMatch.AnyMessage);
        if (index >= 0)
            rules[index] = rules[index] with
            {
                FleetPings = true,
                ShowInFleetFeed = true,
                FeedTitle = string.IsNullOrWhiteSpace(rules[index].FeedTitle) ||
                    rules[index].FeedTitle.Equals("Fleet ping", StringComparison.OrdinalIgnoreCase)
                    ? "Directorbot" : rules[index].FeedTitle
            };
        else
            rules.Insert(0, new(Guid.NewGuid(), source, XmppAlertMatch.AnyMessage, null,
                true, "soft-alert.wav", FleetPings: true, ShowInFleetFeed: true, FeedTitle: "Directorbot"));
        return rules;
    }

    private static IReadOnlyList<XmppAlertRule> IncludeDelveBpingRule(IReadOnlyList<XmppAlertRule> existing)
    {
        var source = XmppAddresses.Delve911;
        if (existing.Any(rule => rule.Source == source && rule.Match == XmppAlertMatch.ContainsText &&
                string.Equals(rule.Text, "!bping all", StringComparison.OrdinalIgnoreCase)))
            return existing;

        var rules = existing.ToList();
        rules.Add(new(Guid.NewGuid(), source, XmppAlertMatch.ContainsText, "!bping all",
            true, "soft-alert.wav", ShowInFleetFeed: true, Highlight: true,
            FeedTitle: "Delve !bping", FeedSubtitle: "{sender}: {body}"));
        return rules;
    }

    private async Task SaveAsync(SavedSettings settings)
    {
        await _settings.SetAsync(SettingsKey, settings, _appToken);
        Volatile.Write(ref _saved, settings);
        StateChanged?.Invoke();
    }

    private bool IsCredentialPending(string key) =>
        _saved.PendingCredentialDeletes.Contains(key, StringComparer.Ordinal);

    private static SavedSettings AddPendingCredential(SavedSettings settings, string key) =>
        settings.PendingCredentialDeletes.Contains(key, StringComparer.Ordinal)
            ? settings : settings with { PendingCredentialDeletes = [.. settings.PendingCredentialDeletes, key] };

    // Credential changes and deletion retries run under _gate. The settings write is
    // the journal for the separate OS vault operation.
    private async Task<bool> SaveCredentialAsync(string key, string password, SavedSettings sessionOnly, SavedSettings remembered)
    {
        var pending = IsCredentialPending(key);
        await SaveAsync(AddPendingCredential(sessionOnly, key));
        if (pending || !await Task.Run(() => _credentials.Write(key, password))) return false;
        await SaveAsync(remembered);
        return true;
    }

    private async Task DeletePendingCredentialsAsync()
    {
        var keys = _saved.PendingCredentialDeletes;
        if (keys.Count == 0) return;
        var remaining = await Task.Run(() => keys.Where(key => !_credentials.Delete(key)).ToArray());
        if (remaining.Length != keys.Count)
            await SaveAsync(_saved with { PendingCredentialDeletes = remaining });
    }

    private async Task RetryCredentialCleanupAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { await DeletePendingCredentialsAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { LogFailure("Credential cleanup", ex); }
        finally { _gate.Release(); }
    }

    private async Task<string?> CredentialFeedbackAsync(string? notice)
    {
        await DeletePendingCredentialsAsync();
        return _saved.PendingCredentialDeletes.Count == 0 ? notice :
            notice is null ? PendingCredentialRemoval : $"{notice} {PendingCredentialRemoval}";
    }

    private void StartSession(XmppAccount account, string password)
    {
        var requests = Channel.CreateUnbounded<Request>(new UnboundedChannelOptions { SingleReader = true });
        Volatile.Write(ref _requests, requests);
        _sessionCancellation?.Dispose();
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_appToken);
        var token = _sessionCancellation.Token;
        SetState("Connecting…");
        _session = Task.Run(() => RunAsync(account, password, requests, token));
    }

    private async Task StopSessionAsync()
    {
        _sessionCancellation?.Cancel();
        if (_session is not null) await _session;
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _session = null;
    }

    private async Task<T> RequestAsync<T>(Func<XmppProtocolSession, Task<T>> handler)
    {
        var channel = Volatile.Read(ref _requests);
        if (!Online || channel is null) throw new InvalidOperationException("Connect to Jabber first.");
        var result = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new Request(async protocol => await handler(protocol), result);
        if (!channel.Writer.TryWrite(request)) throw new InvalidOperationException("The Jabber session ended.");
        return (T)(await result.Task)!;
    }

    private async Task RunAsync(XmppAccount account, string password, Channel<Request> requests, CancellationToken token)
    {
        string? caFile = null;
        var followedRooms = new Dictionary<string, XmppRoom>(StringComparer.Ordinal);
        try
        {
            caFile = CreateTrustFile();
            using var native = new LibstropheSession(account, password, caFile);
            var endpoints = XmppEndpoint.Candidates(account);
            var failures = 0;
            while (!token.IsCancellationRequested)
            {
                string? configurationError = null;
                foreach (var endpoint in endpoints)
                {
                    token.ThrowIfCancellationRequested();
                    native.Connect(endpoint);
                    using var protocol = new XmppProtocolSession(this, native, account.Jid, followedRooms, token);
                    try { await protocol.RunAsync(requests.Reader); }
                    finally
                    {
                        if (token.IsCancellationRequested) native.Close();
                        else native.Abort();
                    }
                    ResetPresence();
                    if (native.PermanentFailure is { } error)
                    {
                        configurationError = error;
                        if (!error.Contains("did not offer", StringComparison.Ordinal)) break;
                    }
                    if (native.StreamEstablished)
                    {
                        failures = 0;
                        configurationError = native.PermanentFailure;
                        break;
                    }
                }

                if (configurationError is not null)
                {
                    SetState(configurationError);
                    return;
                }

                var seconds = Math.Min(60, Math.Pow(2, Math.Min(failures++, 6)) * (0.75 + Random.Shared.NextDouble() * 0.5));
                SetState($"Connection lost. Retrying in {Math.Ceiling(seconds)} s…");
                RejectPending(requests.Reader);
                await Task.Delay(TimeSpan.FromSeconds(seconds), token);
                SetState("Reconnecting…");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { SetState("Disconnected"); }
        catch (Exception ex)
        {
            LogFailure("Session stopped", ex);
            SetState(ex switch
            {
                DllNotFoundException or EntryPointNotFoundException or BadImageFormatException =>
                    "The Jabber native library is missing or incompatible with this build.",
                _ => "The Jabber session stopped. Check the settings and try Connect again."
            });
        }
        finally
        {
            if (caFile is not null && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            {
                try { File.Delete(caFile); }
                catch (IOException) { }
            }
            ResetPresence();
            requests.Writer.TryComplete();
            RejectPending(requests.Reader);
        }
    }

    private void ResetPresence()
    {
        Volatile.Write(ref _contacts, Contacts.Select(contact => contact with { Online = false }).ToArray());
        Volatile.Write(ref _joinedRooms, new HashSet<string>());
        Volatile.Write(ref _online, false);
        _occupants.Clear();
        StateChanged?.Invoke();
    }

    internal void SetState(string description, bool online = false)
    {
        Volatile.Write(ref _status, description);
        Volatile.Write(ref _online, online);
        StateChanged?.Invoke();
    }

    internal void PublishPresence(IEnumerable<XmppContact> contacts, IEnumerable<string> joinedRooms)
    {
        Volatile.Write(ref _contacts, contacts.OrderByDescending(contact => contact.Online)
            .ThenBy(contact => contact.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        Volatile.Write(ref _joinedRooms, joinedRooms.ToHashSet(StringComparer.Ordinal));
        StateChanged?.Invoke();
    }

    public readonly record struct XmppTopicChange(string Room, string? Nick, string? Topic);

    internal void SetRoomTopic(string room, string? topic, DateTimeOffset? stamp, string? nick, bool announce)
    {
        lock (_topicSync)
        {
            var existing = _roomStatus.GetValueOrDefault(room) ?? new XmppRoomStatus();
            var seen = _topicSeen.Contains(room);
            var same = seen && string.Equals(existing.Topic, topic, StringComparison.Ordinal);
            var when = same ? existing.TopicChangedUtc ?? stamp : stamp ?? (seen ? DateTimeOffset.UtcNow : stamp);
            _roomStatus[room] = existing with
            {
                Topic = topic,
                TopicChangedUtc = when,
                TopicAuthor = same ? existing.TopicAuthor : nick ?? existing.TopicAuthor
            };
            if (announce && seen && !same)
                _topicChanges.Enqueue(new XmppTopicChange(room, nick, topic));
            _topicSeen.Add(room);
        }
        StateChanged?.Invoke();
    }

    public bool TryDequeueTopicChange(out XmppTopicChange change)
    {
        lock (_topicSync)
        {
            if (_topicChanges.Count == 0)
            {
                change = default;
                return false;
            }
            change = _topicChanges.Dequeue();
            return true;
        }
    }

    internal void SetRoomDetail(string room, string? detail)
    {
        _roomStatus.AddOrUpdate(room, new XmppRoomStatus(Detail: detail), (_, status) => status with { Detail = detail });
        StateChanged?.Invoke();
    }

    internal void SetOccupants(string room, IReadOnlyList<XmppOccupant> occupants)
    {
        _occupants[room] = XmppOccupantOrder.Sort(occupants).ToArray();
        StateChanged?.Invoke();
    }

    internal void ClearOccupants(string room)
    {
        _occupants.TryRemove(room, out _);
        StateChanged?.Invoke();
    }

    internal string? RoomPassword(string account, XmppRoom room)
    {
        var key = RoomKey(account, room.Jid);
        lock (_roomPasswordGate)
        {
            if (Account?.Jid != account) return null;
            if (_roomPasswords.TryGetValue((account, room.Jid), out var password)) return password;
        }
        bool CanRead(SavedSettings settings) => settings.Account?.Jid == account &&
            settings.Rooms.Any(item => item.Jid == room.Jid && item.HasPassword) &&
            !settings.PendingCredentialDeletes.Contains(key, StringComparer.Ordinal);

        if (!CanRead(_saved)) return null;
        var savedPassword = _credentials.Read(key);
        return CanRead(_saved) ? savedPassword : null;
    }

    internal async Task RecordAsync(string account, XmppMessage message, string? nickname, CancellationToken token,
        XmppHistoryDelivery delivery = XmppHistoryDelivery.None, DateTimeOffset? readAtJoin = null)
    {
        var directorbot = XmppAddresses.Directorbot(account);
        var fromDirectorbot = !message.Outgoing && message.Conversation == directorbot &&
            XmppAddresses.Bare(message.Sender) == directorbot.Jid;
        var fleetPing = fromDirectorbot ? XmppFleetPingParsing.TryParse(message.Body) : null;
        message = message with
        {
            IsFleetPing = fleetPing is not null,
            Mention = !message.Outgoing && nickname is not null && XmppAddresses.Mentions(message.Body, nickname)
        };
        var matched = message.Outgoing || message.Recovered
            ? Array.Empty<XmppAlertRule>()
            : Rules.Where(rule => XmppAlertMatching.Matches(rule, message)).ToArray();
        var feedRule = matched.FirstOrDefault(rule => rule.ShowInFleetFeed);
        var showInFeed = fromDirectorbot || feedRule is not null;
        message = message with
        {
            ShowInFleetFeed = showInFeed,
            Highlight = matched.Any(rule => rule.Highlight) || fleetPing is not null,
            FeedTitle = showInFeed
                ? (fleetPing is not null
                    ? "Fleet ping"
                    : (string.IsNullOrWhiteSpace(feedRule?.FeedTitle)
                        ? (fromDirectorbot ? "Directorbot" : "Alert")
                        : feedRule!.FeedTitle))
                : null,
            FeedSubtitle = showInFeed && !string.IsNullOrWhiteSpace(feedRule?.FeedSubtitle)
                ? XmppAlertFeedFormatting.Apply(feedRule.FeedSubtitle, message)
                : null
        };
        if (!await _history.InsertAsync(account, message, token, delivery, readAtJoin)) return;
        MessageReceived?.Invoke(message);
        if (message.Recovered || message.Outgoing) return;

        var now = DateTimeOffset.UtcNow;
        var notificationRules = matched.Where(rule => rule.Popup || rule.Sound is not null).ToArray();
        var alertable = notificationRules
            .Where(rule =>
            {
                if (rule.CooldownSeconds <= 0) return true;
                if (_ruleCooldownUntil.TryGetValue(rule.Id, out var until) && until > now) return false;
                _ruleCooldownUntil[rule.Id] = now.AddSeconds(rule.CooldownSeconds);
                return true;
            }).ToArray();
        var sound = alertable.FirstOrDefault(rule => rule.Sound is not null);
        if (alertable.Any(rule => rule.Popup) || sound is not null)
            AlertReceived?.Invoke(new(message, alertable.Any(rule => rule.Popup), sound?.Sound, sound?.Volume ?? 0.4));
        else if (notificationRules.Length == 0 && !message.ShowInFleetFeed)
        {
            var soft = AlertSoundPreferences;
            if (message.Mention && soft.SoftPingMentions)
                AlertReceived?.Invoke(new(message, false, soft.SoftPingMentionSound, soft.SoftPingMentionVolume));
            else if (message.Conversation.Kind == XmppConversationKind.Direct &&
                     soft.SoftPingDirectMessages)
                AlertReceived?.Invoke(new(message, false, soft.SoftPingDirectSound, soft.SoftPingDirectVolume));
        }
    }

    internal async Task<(DateTimeOffset Stamp, bool New)> BaselineAsync(string account, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var created = await _history.EnsureCheckpointAsync(account, "$baseline", now, token);
        var stamp = await _history.GetCheckpointAsync(account, "$baseline", now, token);
        return (stamp, created > 0);
    }

    internal async Task<DateTimeOffset> ArchiveStartAsync(string account, string archive, DateTimeOffset baseline, CancellationToken token)
    {
        var position = await _history.GetCheckpointAsync(account, archive, baseline, token);
        var cleared = await _history.GetCheckpointAsync(account, "$cleared", position, token);
        return cleared > position ? cleared : position;
    }

    internal Task SaveArchivePositionAsync(string account, string archive, DateTimeOffset stamp, CancellationToken token) =>
        _history.SaveCheckpointAsync(account, archive, stamp, token);

    internal Task<DateTimeOffset> HistoryClearBoundaryAsync(string account, CancellationToken token) =>
        _history.GetCheckpointAsync(account, "$cleared", DateTimeOffset.MinValue, token, clampToRetention: false);

    internal Task<DateTimeOffset> RoomReadPositionAsync(string account, string room, CancellationToken token) =>
        _history.GetRoomReadPositionAsync(account, room, token);

    private void LogFailure(string operation, Exception ex) =>
        _logger.LogWarning("XMPP {Operation} ({ErrorType})", operation, ex.GetType().Name);

    private static void RejectPending(ChannelReader<Request> requests)
    {
        while (requests.TryRead(out var request))
            request.Result.TrySetException(new InvalidOperationException("The Jabber connection ended."));
    }

    private static string AccountKey(string account) => "account/" + account;
    private static string RoomKey(string account, string room) => $"room/{account}/{room}";

    private static string? CreateTrustFile()
    {
        if (OperatingSystem.IsLinux())
        {
            string[] bundles = ["/etc/ssl/certs/ca-certificates.crt", "/etc/pki/ca-trust/extracted/pem/tls-ca-bundle.pem",
                "/etc/pki/tls/certs/ca-bundle.crt", "/etc/ssl/cert.pem"];
            return bundles.FirstOrDefault(File.Exists);
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return null;
        var path = Path.Combine(Path.GetTempPath(), $"hisa-xmpp-roots-{Guid.NewGuid():N}.pem");
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly);
            foreach (var certificate in store.Certificates)
            {
                using (certificate) roots.Add(certificate.ExportCertificatePem());
            }
        }
        if (roots.Count == 0) throw new InvalidOperationException("The platform trust store is empty.");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file);
        foreach (var root in roots) writer.WriteLine(root);
        return path;
    }

    internal sealed record Request(Func<XmppProtocolSession, Task<object?>> Handle, TaskCompletionSource<object?> Result);
    public sealed record SavedSettings(XmppAccount? Account, IReadOnlyList<XmppRoom> Rooms, IReadOnlyList<XmppAlertRule> Rules)
    {
        public IReadOnlyList<string> PendingCredentialDeletes { get; init; } = [];
    }
}

