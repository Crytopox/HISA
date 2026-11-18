using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hisa.Core.Models;

public enum XmppTlsMode { Automatic, StartTls, DirectTls }
public enum XmppConversationKind { Direct, Room }

public sealed record XmppAccount(string Jid, string? Host, ushort Port, XmppTlsMode TlsMode,
    bool RememberPassword, bool AutoConnect, string Resource);
public sealed record XmppRoom(string Jid, string Nickname, bool AutoJoin, bool HasPassword = false, string? Group = null);
public enum XmppPresenceShow { Offline = 0, Online = 1, Away = 2, ExtendedAway = 3, DoNotDisturb = 4, Chat = 5 }

public sealed record XmppContact(string Jid, string Name, bool Online, bool SubscriptionRequested = false,
    IReadOnlyList<string>? Groups = null, XmppPresenceShow Show = XmppPresenceShow.Offline,
    string? StatusText = null)
{
    public IReadOnlyList<string> RosterGroups => Groups is { Count: > 0 } ? Groups : [];
    public XmppPresenceShow EffectiveShow => Online
        ? Show == XmppPresenceShow.Offline ? XmppPresenceShow.Online : Show
        : XmppPresenceShow.Offline;
}
public sealed record XmppDiscoveredRoom(string Jid, string Name);
public sealed record XmppConversation(XmppConversationKind Kind, string Jid);
public enum XmppHistoryDelivery { None, RoomJoin, RoomArchive }
public sealed record XmppRoomStatus(string? Topic = null, string? Detail = null,
    DateTimeOffset? TopicChangedUtc = null, string? TopicAuthor = null);

public static class XmppTopicAge
{
    public static string? Format(DateTimeOffset? changedUtc, string? author = null, DateTimeOffset? now = null)
    {
        if (changedUtc is not { } at) return null;
        var delta = (now ?? DateTimeOffset.UtcNow) - at;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        string age;
        if (delta < TimeSpan.FromSeconds(45)) age = "Updated just now";
        else if (delta < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
            age = minutes == 1 ? "Updated 1 minute ago" : $"Updated {minutes} minutes ago";
        }
        else if (delta < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            age = hours == 1 ? "Updated 1 hour ago" : $"Updated {hours} hours ago";
        }
        else
        {
            var days = Math.Max(1, (int)Math.Round(delta.TotalDays));
            age = days == 1 ? "Updated 1 day ago" : $"Updated {days} days ago";
        }

        var shortName = ShortAuthor(author);
        return shortName is null ? age : $"{age} by {shortName}";
    }

    public static string? ShortAuthor(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return null;
        var value = author.Trim();
        var slash = value.IndexOf('/');
        if (slash >= 0 && slash < value.Length - 1) value = value[(slash + 1)..];
        var at = value.IndexOf('@');
        if (at > 0) value = value[..at];
        value = value.Trim();
        return value.Length == 0 ? null : value;
    }
}
public sealed record XmppOccupant(string Nick, string? Jid, string Affiliation, string Role);
public sealed record XmppContactProfile(string Jid, byte[]? AvatarBytes = null, string? AvatarType = null,
    DateTimeOffset? LastSeenUtc = null, string? StatusText = null, bool Online = false,
    XmppPresenceShow Show = XmppPresenceShow.Offline);
public sealed record XmppDisplayPreferences(
    bool Use12HourClock = false,
    bool ShowMessageDividers = true,
    bool AlternatingMessageColors = false,
    bool ShowOccupantSidebar = true,
    string FontFamily = "Inter",
    double MessageFontSize = 13,
    bool AlwaysShowMessageHeaders = true,
    double? MessageBlockPadding = null,
    bool? ScrollToLatestOnTabSwitch = null,
    bool? LeaveRoomOnTabClose = null)
{
    public const double DefaultMessageBlockPadding = 2;
    public const double MaxMessageBlockPadding = 12;

    public static double NormalizeMessageBlockPadding(double? padding) =>
        padding is double value && double.IsFinite(value)
            ? Math.Clamp(value, 0, MaxMessageBlockPadding)
            : DefaultMessageBlockPadding;

    public static bool NormalizeScrollToLatestOnTabSwitch(bool? value) => value != false;

    public static bool NormalizeLeaveRoomOnTabClose(bool? value) => value != false;
}
public sealed record XmppAlertSoundPreferences(
    bool SoftPingDirectMessages = true,
    double SoftPingDirectVolume = 0.25,
    string SoftPingDirectSound = "pidgin_receive.wav",
    bool SoftPingMentions = true,
    double SoftPingMentionVolume = 0.25,
    string SoftPingMentionSound = "regular_notification.wav");
public sealed record XmppOwnPresencePreferences(
    XmppPresenceShow Show = XmppPresenceShow.Online,
    string? StatusText = null);
public sealed record XmppWorkspace(IReadOnlyList<XmppConversation> Tabs, XmppConversation? Selected,
    bool FleetSelected, double Width = 900, double Height = 600, int? X = null, int? Y = null, bool Pinned = false,
    IReadOnlyList<string>? CollapsedGroups = null, IReadOnlyList<string>? CollapsedTopics = null,
    bool Open = false, double SidebarWidth = 200, double OccupantWidth = 168);

public static class XmppOccupantOrder
{
    public static int AffiliationRank(string affiliation) => affiliation.ToLowerInvariant() switch
    {
        "owner" => 0,
        "admin" => 1,
        "member" => 2,
        _ => 3
    };

    public static int RoleRank(string role) => role.ToLowerInvariant() switch
    {
        "moderator" => 0,
        "participant" => 1,
        "visitor" => 2,
        _ => 3
    };

    public static IOrderedEnumerable<XmppOccupant> Sort(IEnumerable<XmppOccupant> occupants) =>
        occupants.OrderBy(occupant => AffiliationRank(occupant.Affiliation))
            .ThenBy(occupant => RoleRank(occupant.Role))
            .ThenBy(occupant => occupant.Nick, StringComparer.OrdinalIgnoreCase);
}

public sealed record XmppMessage(XmppConversation Conversation, string Sender, string Body,
    DateTimeOffset Timestamp, bool Outgoing, bool Recovered, bool IsFleetPing, bool Unread,
    IReadOnlyList<string> IdentityKeys)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long LocalId { get; init; }
    public bool Mention { get; init; }
    public bool ShowInFleetFeed { get; init; }
    public bool Highlight { get; init; }
    public string? FeedTitle { get; init; }
    public string? FeedSubtitle { get; init; }
    public string? Html { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int? JoinHistoryOrdinal { get; init; }
}

public enum XmppAlertMatch { AnyMessage, ContainsText, NicknameMention, EqualsText, Regex }
public enum XmppAlertSourceScope { Conversation = 0, Anywhere = 1, AnyDirect = 2, AnyRoom = 3 }
public enum XmppMessageFilterAction { Collapse = 0, Hide = 1 }

public sealed record XmppAlertRule(
    Guid Id,
    XmppConversation Source,
    XmppAlertMatch Match,
    string? Text,
    bool Popup,
    string? Sound,
    double Volume = 0.4,
    bool Enabled = true,
    bool FleetPings = false,
    XmppAlertSourceScope SourceScope = XmppAlertSourceScope.Conversation,
    string? Sender = null,
    int CooldownSeconds = 0,
    bool ShowInFleetFeed = false,
    bool Highlight = false,
    string? FeedTitle = null,
    string? FeedSubtitle = null);

public sealed record XmppAlert(XmppMessage Message, bool Popup, string? Sound, double Volume);

public sealed record XmppMessageFilter(
    Guid Id,
    string Sender,
    XmppMessageFilterAction Action = XmppMessageFilterAction.Collapse,
    XmppConversation? Scope = null,
    bool Enabled = true);

public static class XmppAddresses
{
    public static string? Bare(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var bare = address.Split('/', 2)[0];
        var separator = bare.IndexOf('@');
        if (separator <= 0 || separator != bare.LastIndexOf('@') || separator == bare.Length - 1)
            return null;

        var node = bare[..separator];
        if (node.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "\"&'/:<>@".Contains(c)))
            return null;

        try
        {
            var domain = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(bare[(separator + 1)..]);
            var local = node.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Normalize();
            if (local.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "\"&'/:<>@".Contains(c)))
                return null;
            return Encoding.UTF8.GetByteCount(local) <= 1023 ? $"{local}@{domain.ToLowerInvariant()}" : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static string Domain(string bareJid) => bareJid[(bareJid.LastIndexOf('@') + 1)..];

    public static XmppConversation Directorbot(string accountJid) =>
        new(XmppConversationKind.Direct, $"directorbot@{Domain(accountJid)}");

    public static bool IsGoonfleet(string accountJid) =>
        Domain(accountJid).Equals("goonfleet.com", StringComparison.OrdinalIgnoreCase);

    public static XmppConversation Delve911 =>
        new(XmppConversationKind.Room, "delve911@conference.goonfleet.com");

    public static string ConferenceServer(string accountJid) => $"conference.{Domain(accountJid)}";

    public static string? RoomAddress(string room, string server)
    {
        room = room.Trim();
        server = server.Trim();
        return room.Contains('/') || server.Contains('/') || Uri.CheckHostName(server) != UriHostNameType.Dns
            ? null : Bare($"{room}@{server}");
    }

    public static bool Mentions(string body, string nickname) =>
        nickname.Length > 0 && Regex.IsMatch(body,
            $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(nickname)}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}

public static class XmppAlertMatching
{
    public static bool Matches(XmppAlertRule rule, XmppMessage message, string? nickname = null)
    {
        if (!rule.Enabled) return false;
        // System Directorbot rule: all inbound Directorbot traffic (feed/alerts), not only parsed fleet cards.
        if (rule.FleetPings)
            return !message.Outgoing && MatchesSource(rule, message.Conversation);
        if (!MatchesSource(rule, message.Conversation)) return false;
        if (!MatchesSender(rule.Sender, message)) return false;
        return rule.Match switch
        {
            XmppAlertMatch.AnyMessage => true,
            XmppAlertMatch.ContainsText => rule.Text is { Length: > 0 } text &&
                message.Body.Contains(text, StringComparison.OrdinalIgnoreCase),
            XmppAlertMatch.EqualsText => rule.Text is { Length: > 0 } text &&
                message.Body.Equals(text, StringComparison.OrdinalIgnoreCase),
            XmppAlertMatch.Regex => MatchesRegex(message.Body, rule.Text),
            XmppAlertMatch.NicknameMention => message.Mention ||
                (nickname is { Length: > 0 } && XmppAddresses.Mentions(message.Body, nickname)),
            _ => false
        };
    }

    public static bool ShouldHighlight(IEnumerable<XmppAlertRule> rules, XmppMessage message, string? nickname = null) =>
        !message.Outgoing && rules.Any(rule => rule.Highlight && Matches(rule, message, nickname));

    private static bool MatchesSource(XmppAlertRule rule, XmppConversation conversation) =>
        rule.SourceScope switch
        {
            XmppAlertSourceScope.Anywhere => true,
            XmppAlertSourceScope.AnyDirect => conversation.Kind == XmppConversationKind.Direct,
            XmppAlertSourceScope.AnyRoom => conversation.Kind == XmppConversationKind.Room,
            _ => rule.Source == conversation
        };

    private static bool MatchesSender(string? filter, XmppMessage message)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var needle = filter.Trim();
        var bare = XmppAddresses.Bare(message.Sender) ?? message.Sender;
        if (bare.Equals(needle, StringComparison.OrdinalIgnoreCase)) return true;
        var nick = message.Sender.Contains('/') ? message.Sender[(message.Sender.IndexOf('/') + 1)..] :
            bare.Contains('@') ? bare[..bare.IndexOf('@')] : bare;
        return nick.Equals(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRegex(string body, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        try
        {
            return Regex.IsMatch(body, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }
}

public sealed record XmppFormattedRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false,
    double? FontSize = null, string? Link = null);

public static class XmppHtmlFormatting
{
    private static readonly System.Xml.Linq.XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    public static IReadOnlyList<XmppFormattedRun>? Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        try
        {
            var root = System.Xml.Linq.XElement.Parse($"<root>{html}</root>");
            var runs = new List<XmppFormattedRun>();
            Walk(root, runs, false, false, false, null);
            return runs.Count == 0 ? null : runs;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static void Walk(System.Xml.Linq.XElement element, List<XmppFormattedRun> runs,
        bool bold, bool italic, bool underline, double? size)
    {
        foreach (var node in element.Nodes())
        {
            if (node is System.Xml.Linq.XText text)
            {
                if (text.Value.Length > 0)
                    runs.Add(new(text.Value, bold, italic, underline, size));
                continue;
            }
            if (node is not System.Xml.Linq.XElement child) continue;
            var name = child.Name.LocalName.ToLowerInvariant();
            if (name is "script" or "style" or "img") continue;
            if (name == "br")
            {
                runs.Add(new("\n", bold, italic, underline, size));
                continue;
            }
            var nextBold = bold || name is "b" or "strong";
            var nextItalic = italic || name is "i" or "em";
            var nextUnderline = underline || name == "u";
            var nextSize = size ?? ParseSize(child);
            if (name == "a" && child.Attribute("href")?.Value is { Length: > 0 } href &&
                Uri.TryCreate(href, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            {
                var label = string.Concat(child.DescendantNodes().OfType<System.Xml.Linq.XText>().Select(item => item.Value));
                if (string.IsNullOrEmpty(label)) label = href;
                runs.Add(new(label, nextBold, nextItalic, true, nextSize, href));
                continue;
            }
            Walk(child, runs, nextBold, nextItalic, nextUnderline, nextSize);
        }
    }

    private static double? ParseSize(System.Xml.Linq.XElement element)
    {
        var raw = element.Attribute("size")?.Value ?? element.Attribute("style")?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"(\d+(?:\.\d+)?)");
        if (!match.Success || !double.TryParse(match.Groups[1].Value, out var size)) return null;
        if (raw.Contains("font-size", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("font", StringComparison.OrdinalIgnoreCase)
            || element.Attribute("size") is not null)
            return Math.Clamp(size, 10, 22);
        return null;
    }
}

public readonly record struct XmppLinkSpan(string Text, Uri? Url);

public static partial class XmppLinkSpans
{
    public static IReadOnlyList<XmppLinkSpan> Split(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var spans = new List<XmppLinkSpan>();
        var cursor = 0;
        foreach (Match match in HttpPattern().Matches(text))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', '!', '?', ']', '}');
            while (candidate.EndsWith(')') && candidate.Count(c => c == ')') > candidate.Count(c => c == '('))
                candidate = candidate[..^1];
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                continue;
            if (match.Index > cursor)
                spans.Add(new(text[cursor..match.Index], null));
            spans.Add(new(candidate, url));
            cursor = match.Index + candidate.Length;
        }
        if (cursor < text.Length)
            spans.Add(new(text[cursor..], null));
        return spans;
    }

    [GeneratedRegex("""https?://[^\s<>"']+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpPattern();
}

public static class XmppAlertFeedFormatting
{
    public static string Apply(string? template, XmppMessage message)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        var sender = DisplaySender(message);
        return template
            .Replace("{sender}", sender, StringComparison.OrdinalIgnoreCase)
            .Replace("{body}", message.Body, StringComparison.OrdinalIgnoreCase)
            .Replace("{conversation}", message.Conversation.Jid, StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplaySender(XmppMessage message)
    {
        if (message.Outgoing) return "You";
        var separator = message.Sender.IndexOf('/');
        if (message.Conversation.Kind == XmppConversationKind.Room)
            return separator < 0 ? "Room announcement" : message.Sender[(separator + 1)..];
        var bare = message.Conversation.Jid;
        var at = bare.IndexOf('@');
        return at > 0 ? bare[..at] : bare;
    }
}

public static class XmppMessageFilters
{
    public static bool SenderMatches(string filter, XmppMessage message)
    {
        if (string.IsNullOrWhiteSpace(filter)) return false;
        var needle = filter.Trim();
        var bare = XmppAddresses.Bare(message.Sender) ?? message.Sender;
        if (bare.Equals(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Conversation.Kind == XmppConversationKind.Direct &&
            message.Conversation.Jid.Equals(needle, StringComparison.OrdinalIgnoreCase))
            return true;
        return SenderKey(message).Equals(needle, StringComparison.OrdinalIgnoreCase);
    }

    public static string SenderKey(XmppMessage message)
    {
        if (message.Conversation.Kind == XmppConversationKind.Direct)
            return message.Conversation.Jid;
        var bare = XmppAddresses.Bare(message.Sender) ?? message.Sender;
        if (message.Sender.Contains('/'))
            return message.Sender[(message.Sender.IndexOf('/') + 1)..];
        var at = bare.IndexOf('@');
        return at > 0 ? bare[..at] : bare;
    }

    public static bool SameCollapseSender(XmppMessage left, XmppMessage right) =>
        left.Outgoing == right.Outgoing &&
        left.Conversation == right.Conversation &&
        SenderKey(left).Equals(SenderKey(right), StringComparison.OrdinalIgnoreCase);

    public static XmppMessageFilter? Matching(IReadOnlyList<XmppMessageFilter> filters, XmppMessage message,
        XmppMessageFilterAction action) =>
        filters.FirstOrDefault(filter =>
            filter.Enabled &&
            filter.Action == action &&
            (filter.Scope is null || filter.Scope == message.Conversation) &&
            SenderMatches(filter.Sender, message));
}

