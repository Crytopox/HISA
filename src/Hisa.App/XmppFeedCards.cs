using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.App;

internal static class XmppFeedCards
{
    private static readonly IBrush DividerAccent = Brush.Parse("#2A3A4E");
    private static readonly IBrush FleetBorder = new SolidColorBrush(Color.Parse("#E0A05A")) { Opacity = 0.45 };
    private static readonly IBrush PeacetimeBorder = new SolidColorBrush(Color.Parse("#6BA3C8")) { Opacity = 0.45 };
    private static readonly IBrush DelveBorder = new SolidColorBrush(Color.Parse("#E06060")) { Opacity = 0.45 };
    private static readonly IBrush SimpleBorder = Brush.Parse("#243246");
    private static readonly IBrush CollapseBorder = Brush.Parse("#1C2838");
    private static readonly IBrush PapStrategicChip = Brush.Parse("#6B4A28");
    private static readonly IBrush PapMutedChip = Brush.Parse("#243246");
    private static readonly IBrush PapPeacetimeChip = Brush.Parse("#2A4058");
    private static readonly IBrush PapStrategicFg = Brush.Parse("#F0C78A");
    private static readonly IBrush PapMutedFg = Brush.Parse("#C5D0E0");
    private static readonly IBrush PapPeacetimeFg = Brush.Parse("#A8D0E8");
    private static readonly IBrush FleetBg = Brush.Parse("#3A3228");
    private static readonly IBrush FleetBar = Brush.Parse("#E0A05A");
    private static readonly IBrush PeacetimeBg = Brush.Parse("#243248");
    private static readonly IBrush PeacetimeBar = Brush.Parse("#6BA3C8");
    private static readonly IBrush DelveBg = Brush.Parse("#3A2428");
    private static readonly IBrush DelveBar = Brush.Parse("#E06060");
    private static readonly IBrush LocustBg = Brush.Parse("#322438");
    private static readonly IBrush LocustBar = Brush.Parse("#D070C0");
    private static readonly IBrush LocustBorder = new SolidColorBrush(Color.Parse("#D070C0")) { Opacity = 0.45 };
    private static readonly IBrush BeehiveBg = Brush.Parse("#3A3420");
    private static readonly IBrush BeehiveBar = Brush.Parse("#E0B85A");
    private static readonly IBrush BeehiveBorder = new SolidColorBrush(Color.Parse("#E0B85A")) { Opacity = 0.45 };
    private static readonly IBrush OptKrabBg = Brush.Parse("#3A2A20");
    private static readonly IBrush OptKrabBar = Brush.Parse("#E08040");
    private static readonly IBrush OptKrabBorder = new SolidColorBrush(Color.Parse("#E08040")) { Opacity = 0.45 };
    private static readonly IBrush AnnounceBg = Brush.Parse("#243048");
    private static readonly IBrush AnnounceBar = Brush.Parse("#6B8EC8");
    private static readonly IBrush AnnounceBorder = new SolidColorBrush(Color.Parse("#6B8EC8")) { Opacity = 0.45 };
    private static readonly IBrush OptAllBg = Brush.Parse("#1E2630");
    private static readonly IBrush OptAllBar = Brush.Parse("#6B7789");
    private static readonly IBrush OptAllBorder = new SolidColorBrush(Color.Parse("#6B7789")) { Opacity = 0.35 };
    private static readonly IBrush AlertBg = Brush.Parse("#6B5430");
    private static readonly IBrush AlertBar = Brush.Parse("#E0C06A");
    private static readonly IBrush MentionBar = Brush.Parse("#5EB8E8");
    private static readonly IBrush AltRow = Brush.Parse("#1A2A3C");

    public static IBrush FleetAccentBar(bool peacetime) => peacetime ? PeacetimeBar : FleetBar;
    public static IBrush FleetAccentBackground(bool peacetime) => peacetime ? PeacetimeBg : FleetBg;

    public static bool IsPeacetimePap(string? papType) =>
        papType is not null &&
        papType.Contains("peacetime", StringComparison.OrdinalIgnoreCase) &&
        !papType.Contains("strategic", StringComparison.OrdinalIgnoreCase);

    private static (IBrush Bg, IBrush Bar, IBrush Border) StructuredTheme(string? broadcastTo, bool peacetime) =>
        broadcastTo switch
        {
            "locusts" => (LocustBg, LocustBar, LocustBorder),
            "beehive" => (BeehiveBg, BeehiveBar, BeehiveBorder),
            _ when peacetime => (PeacetimeBg, PeacetimeBar, PeacetimeBorder),
            _ => (FleetBg, FleetBar, FleetBorder)
        };

    private static (IBrush Bg, IBrush Bar, IBrush Border)? NoteTheme(string? broadcastTo) =>
        broadcastTo switch
        {
            "opt-krab" => (OptKrabBg, OptKrabBar, OptKrabBorder),
            "all" => (AnnounceBg, AnnounceBar, AnnounceBorder),
            "opt-all" => (OptAllBg, OptAllBar, OptAllBorder),
            _ => null
        };

    public static IReadOnlyList<XmppFeedCardVm> Build(
        IEnumerable<(XmppMessage? Message, (string Key, string Label, XmppMessage Representative)? Collapse)> entries,
        string? accountJid)
    {
        var cards = new List<XmppFeedCardVm>();
        foreach (var entry in entries)
        {
            if (entry.Collapse is { } collapse)
            {
                cards.Add(new(
                    collapse.Representative,
                    isFleet: false,
                    title: collapse.Label,
                    time: "",
                    subtitle: null,
                    body: null,
                    papType: null,
                    emphasizePap: false,
                    formup: null,
                    doctrine: null,
                    fcName: null,
                    fcLookupName: null,
                    commsLabel: null,
                    commsUrl: null,
                    cardBackground: AltRow,
                    accentBar: Brushes.Transparent,
                    cardBorder: CollapseBorder,
                    papChipBackground: PapMutedChip,
                    papChipForeground: PapMutedFg,
                    isCollapseSummary: true,
                    collapseKey: collapse.Key));
                continue;
            }

            var message = entry.Message!;
            cards.Add(CreateMessageCard(message, accountJid));
        }

        if (cards.FirstOrDefault(card => !card.IsCollapseSummary && card.Message.Unread) is { } firstUnread)
            firstUnread.IsUnreadStart = true;
        return cards;
    }

    public static IReadOnlyList<XmppFeedCardVm> BuildFromMessages(
        IEnumerable<XmppMessage> messages, string? accountJid) =>
        Build(messages.Select(message => ((XmppMessage?)message, ((string, string, XmppMessage)?)null)),
            accountJid);

    public static void RefreshAges(IEnumerable<XmppFeedCardVm> cards, DateTimeOffset? now = null)
    {
        foreach (var card in cards)
            card.RefreshAge(now);
    }

    public static IEnumerable<(XmppMessage Message, double Top, double Bottom)> RealizedRows(
        ItemsControl list, ScrollViewer scroll, IReadOnlyList<XmppFeedCardVm> cards, bool readableOnly)
    {
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (readableOnly && card.IsCollapseSummary) continue;
            if (list.ContainerFromIndex(index) is not Control { Bounds.Height: > 0 } container ||
                container.TranslatePoint(new Point(), scroll) is not { } point)
                continue;
            yield return (card.Message, point.Y, point.Y + container.Bounds.Height);
        }
    }

    public static XmppFeedCardVm CreateMessageCard(XmppMessage message, string? accountJid)
    {
        var directorbot = accountJid is null ? null : XmppAddresses.Directorbot(accountJid);
        var fromDirectorbot = directorbot is not null && message.Conversation == directorbot;
        var time = XmppFeedAge.Format(message.Timestamp);
        if (fromDirectorbot && !message.Outgoing && XmppFleetPingParsing.TryParseLineup(message.Body) is { } lineup)
        {
            return new(message, isFleet: true, title: lineup.Title, time: time, subtitle: null, body: lineup.Hurf,
                papType: null, emphasizePap: false, formup: null, doctrine: null, fcName: null, fcLookupName: null,
                commsLabel: null, commsUrl: null, cardBackground: FleetBg, accentBar: FleetBar, cardBorder: FleetBorder,
                papChipBackground: PapMutedChip, papChipForeground: PapMutedFg,
                lineup: lineup.Commanders.Select((commander, index) => new XmppFeedCommanderVm(commander.Name,
                    commander.Name, commander.CommsLabel, commander.CommsUrl)
                    { HasDivider = index > 0 }).ToArray());
        }
        var broadcast = message.IsFleetPing || fromDirectorbot || message.BroadcastTo is not null
            ? XmppFleetPingParsing.TryParseBroadcast(message.Body)
            : null;
        var target = message.BroadcastTo ?? broadcast?.BroadcastTo;
        if (broadcast is { IsStructured: true })
        {
            var strategic = broadcast.PapType is not null &&
                broadcast.PapType.Contains("strategic", StringComparison.OrdinalIgnoreCase);
            var peacetime = IsPeacetimePap(broadcast.PapType);
            var theme = StructuredTheme(target, peacetime);
            var lookup = XmppFleetPingParsing.NormalizeFcName(broadcast.FcName) ?? broadcast.FcName;
            var title = string.IsNullOrWhiteSpace(message.FeedTitle)
                ? XmppFleetPingParsing.FeedTitleForTarget(target, structured: true)
                : message.FeedTitle!;
            return new(
                message,
                isFleet: true,
                title: title,
                time: time,
                subtitle: message.FeedSubtitle,
                body: broadcast.Hurf,
                papType: broadcast.PapType,
                emphasizePap: strategic,
                formup: broadcast.FormupLocation,
                doctrine: broadcast.Doctrine,
                fcName: broadcast.FcName,
                fcLookupName: lookup,
                commsLabel: broadcast.CommsLabel,
                commsUrl: broadcast.CommsUrl,
                cardBackground: theme.Bg,
                accentBar: theme.Bar,
                cardBorder: theme.Border,
                papChipBackground: strategic ? PapStrategicChip : peacetime ? PapPeacetimeChip : PapMutedChip,
                papChipForeground: strategic ? PapStrategicFg : peacetime ? PapPeacetimeFg : PapMutedFg,
                notes: broadcast.Notes);
        }

        var delve = message.IsDelveBping || message.Conversation == XmppAddresses.Delve911
            ? XmppDelveBpingParsing.TryParse(message.Body)
            : null;
        if (delve is not null)
        {
            var title = string.IsNullOrWhiteSpace(delve.Title)
                ? (string.IsNullOrWhiteSpace(message.FeedTitle) ? "Delve911" : message.FeedTitle!)
                : delve.Title!;
            return new(
                message,
                isFleet: false,
                title: title,
                time: time,
                subtitle: message.FeedSubtitle,
                body: null,
                papType: null,
                emphasizePap: false,
                formup: null,
                doctrine: null,
                fcName: null,
                fcLookupName: null,
                commsLabel: null,
                commsUrl: null,
                cardBackground: DelveBg,
                accentBar: DelveBar,
                cardBorder: DelveBorder,
                papChipBackground: PapMutedChip,
                papChipForeground: PapMutedFg,
                isDelveHelp: true,
                delvePilot: delve.Pilot,
                delveSystem: delve.System,
                delveLocation: delve.Location,
                delveCyno: delve.Cyno,
                mapSystem: delve.MapSystem);
        }

        if (broadcast is { IsStructured: false } && NoteTheme(target) is { } noteTheme)
        {
            var title = string.IsNullOrWhiteSpace(message.FeedTitle)
                ? XmppFleetPingParsing.FeedTitleForTarget(target, structured: false)
                : message.FeedTitle!;
            return new(
                message,
                isFleet: false,
                title: title,
                time: time,
                subtitle: message.FeedSubtitle,
                body: broadcast.Hurf ?? message.Body,
                papType: null,
                emphasizePap: false,
                formup: null,
                doctrine: null,
                fcName: null,
                fcLookupName: null,
                commsLabel: null,
                commsUrl: null,
                cardBackground: noteTheme.Bg,
                accentBar: noteTheme.Bar,
                cardBorder: noteTheme.Border,
                papChipBackground: PapMutedChip,
                papChipForeground: PapMutedFg);
        }

        var alertTitle = string.IsNullOrWhiteSpace(message.FeedTitle) ? "Alert" : message.FeedTitle!;
        var accent = message.Highlight ? AlertBar :
            message.Mention ? MentionBar : DividerAccent;
        return new(
            message,
            isFleet: false,
            title: alertTitle,
            time: time,
            subtitle: message.FeedSubtitle,
            body: message.Body,
            papType: null,
            emphasizePap: false,
            formup: null,
            doctrine: null,
            fcName: null,
            fcLookupName: null,
            commsLabel: null,
            commsUrl: null,
            cardBackground: message.Highlight || message.Mention ? AlertBg : AltRow,
            accentBar: accent,
            cardBorder: SimpleBorder,
            papChipBackground: PapMutedChip,
            papChipForeground: PapMutedFg);
    }
}
