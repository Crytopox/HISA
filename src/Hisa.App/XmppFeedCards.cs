using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.App;

internal static class XmppFeedCards
{
    private static readonly IBrush DividerAccent = Brush.Parse("#2A3A4E");
    private static readonly IBrush FleetBorder = new SolidColorBrush(Color.Parse("#E0A05A")) { Opacity = 0.45 };
    private static readonly IBrush SimpleBorder = Brush.Parse("#243246");
    private static readonly IBrush CollapseBorder = Brush.Parse("#1C2838");
    private static readonly IBrush PapStrategicChip = Brush.Parse("#6B4A28");
    private static readonly IBrush PapMutedChip = Brush.Parse("#243246");
    private static readonly IBrush PapStrategicFg = Brush.Parse("#F0C78A");
    private static readonly IBrush PapMutedFg = Brush.Parse("#C5D0E0");
    private static readonly IBrush FleetBg = Brush.Parse("#3A3228");
    private static readonly IBrush FleetBar = Brush.Parse("#E0A05A");
    private static readonly IBrush AlertBg = Brush.Parse("#6B5430");
    private static readonly IBrush AlertBar = Brush.Parse("#E0C06A");
    private static readonly IBrush MentionBar = Brush.Parse("#5EB8E8");
    private static readonly IBrush AltRow = Brush.Parse("#1A2A3C");

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
        var parsed = message.IsFleetPing || fromDirectorbot
            ? XmppFleetPingParsing.TryParse(message.Body)
            : null;
        if (parsed is not null)
        {
            var strategic = parsed.PapType is not null &&
                parsed.PapType.Contains("strategic", StringComparison.OrdinalIgnoreCase);
            var lookup = XmppFleetPingParsing.NormalizeFcName(parsed.FcName) ?? parsed.FcName;
            return new(
                message,
                isFleet: true,
                title: "Fleet Ping",
                time: time,
                subtitle: message.FeedSubtitle,
                body: parsed.Hurf,
                papType: parsed.PapType,
                emphasizePap: strategic,
                formup: parsed.FormupLocation,
                doctrine: parsed.Doctrine,
                fcName: parsed.FcName,
                fcLookupName: lookup,
                commsLabel: parsed.CommsLabel,
                commsUrl: parsed.CommsUrl,
                cardBackground: FleetBg,
                accentBar: FleetBar,
                cardBorder: FleetBorder,
                papChipBackground: strategic ? PapStrategicChip : PapMutedChip,
                papChipForeground: strategic ? PapStrategicFg : PapMutedFg,
                notes: parsed.Notes);
        }

        var title = string.IsNullOrWhiteSpace(message.FeedTitle) ? "Alert" : message.FeedTitle!;
        var accent = message.Highlight ? AlertBar :
            message.Mention ? MentionBar : DividerAccent;
        return new(
            message,
            isFleet: false,
            title: title,
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
