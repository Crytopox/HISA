using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Hisa.Core;
using Hisa.Core.Models;

namespace Hisa.Rendering.Controls;

public sealed partial class MapControl
{
    private static readonly IBrush OverlayTextBrush = new SolidColorBrush(Color.Parse("#EAF0F8"));
    private static readonly IBrush OverlayAgeBrush = new SolidColorBrush(Color.Parse("#D7E8FF"));
    private static readonly IBrush OverlayMutedBrush = new SolidColorBrush(Color.Parse("#A6B4C6"));
    private sealed record OverlayIcon(Action<DrawingContext, Rect> Draw, string? Url = null,
        string? Tooltip = null, double Width = MapOverlayLayout.IconSize, string? Role = null, double Height = MapOverlayLayout.IconSize);

    internal MapOverlayLayout BuildNodeOverlay(MapNode node, double maxWidth, IReadOnlySet<string>? popupReports = null, double minimumWidth = MapOverlayLayout.MinWidth)
    {
        RefreshReportOverlayAges();
        var cacheKey = (node, maxWidth, minimumWidth, popupReports is null ? "manual" : string.Join("|", popupReports.Order(StringComparer.Ordinal)));
        if (_overlayLayouts.TryGetValue(cacheKey, out var cached)) return cached;
        var reduced = popupReports is not null;
        const double iconSize = MapOverlayLayout.IconSize;
        const double gap = MapOverlayLayout.InlineGap;
        var rows = new List<(Func<double, MapOverlayRow> Measure, double Gap)>();
        string? currentReportId = null;
        void Add(Func<double, MapOverlayRow> measure, double before = MapOverlayLayout.RowGap)
        {
            var reportId = currentReportId;
            rows.Add((width =>
            {
                var row = measure(width);
                for (var i = 0; i < row.Elements.Count; i++) row.Elements[i] = row.Elements[i] with { ReportId = reportId };
                return row;
            }, before));
        }
        FormattedText Text(string value, double width, bool muted = false, FontWeight weight = FontWeight.Normal,
            double size = MapTypography.BodySize, int lines = int.MaxValue, IBrush? brush = null) =>
            MapTypography.Create(value, size, brush ?? (muted ? OverlayMutedBrush : OverlayTextBrush), weight, width, lines);

        void AddText(string? value, bool muted = false, double before = MapOverlayLayout.RowGap, IBrush? brush = null)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            Add(width =>
            {
                var row = new MapOverlayRow();
                row.AddText(value, Text(value, width, muted, brush: brush));
                return row;
            }, before);
        }

        void Separator()
        {
            Add(width =>
            {
                var row = new MapOverlayRow { PreferredWidth = 0 };
                row.Elements.Add(new(new Rect(0, 0, width, 1), (context, rect) => context.FillRectangle(
                    new SolidColorBrush(Color.Parse("#344052")), rect), Role: "separator"));
                return row;
            }, MapOverlayLayout.SectionGap);
        }

        void AddMessage(string value)
        {
            value = CompactOverlayMessage(value);
            if (value.Length == 0) return;
            Add(width =>
            {
                var row = new MapOverlayRow { PreferredWidth = 0 };
                var text = Text(value, double.PositiveInfinity, muted: true);
                row.Elements.Add(new(new Rect(0, 0, width, text.Height + 5), (_, _) => { }, Text: value, MarqueeText: text));
                return row;
            });
        }

        void AddHeading(string title, string? right = null, IBrush? titleBrush = null, bool system = false,
            OverlayIcon? icon = null, double before = MapOverlayLayout.RowGap)
        {
            Add(width =>
            {
                var row = new MapOverlayRow();
                var offset = icon is null ? 0 : icon.Width + gap;
                var rightText = string.IsNullOrWhiteSpace(right) ? null : Text(right, width,
                    weight: system ? FontWeight.Normal : FontWeight.Medium,
                    size: system ? MapTypography.HeadingSize : MapTypography.SecondarySize,
                    lines: 1, brush: system ? GetCachedBrush(MapTypography.ReadableColor(GetSecurityColor(node))) : OverlayAgeBrush);
                var rightWidth = rightText?.Width ?? 0;
                if (!system && rightText is not null)
                    rightWidth = Math.Max(rightWidth, Text("59m 59s ago", width, weight: FontWeight.Medium,
                        size: MapTypography.SecondarySize).Width);
                var closeSpace = system && reduced ? 20 : 0;
                var reserved = (rightText is null ? 0 : rightWidth + MapOverlayLayout.SectionGap) + closeSpace;
                var titleText = Text(title, width - offset - reserved, weight: FontWeight.SemiBold,
                    size: system ? MapTypography.HeadingSize : MapTypography.BodySize, lines: 2, brush: titleBrush);
                var height = Math.Max(titleText.Height, Math.Max(rightText?.Height ?? 0, icon?.Height ?? 0));
                if (system && reduced)
                {
                    row.MinimumHeight = 20; // Keep the close target clear of metadata.
                    height = Math.Max(height, row.MinimumHeight);
                }
                row.PreferredWidth = offset + Text(title, double.PositiveInfinity, weight: FontWeight.SemiBold,
                    size: system ? MapTypography.HeadingSize : MapTypography.BodySize).Width + reserved;
                var actualWidth = double.IsFinite(width) ? width : offset + titleText.Width + reserved;
                if (icon is not null)
                    row.Elements.Add(new(new Rect(0, (height - icon.Height) / 2, icon.Width, icon.Height), icon.Draw, icon.Url, icon.Tooltip));
                row.AddText(title, titleText, offset, (height - titleText.Height) / 2, title);
                if (rightText is not null)
                    row.AddText(right!, rightText, actualWidth - closeSpace - rightText.Width, (height - rightText.Height) / 2);
                return row;
            }, before);
        }

        void AddIconText(string label, OverlayIcon icon, bool muted = false, FontWeight weight = FontWeight.Normal, IBrush? brush = null)
        {
            Add(width =>
            {
                var row = new MapOverlayRow();
                var text = Text(label, width - icon.Width - gap, muted, weight, brush: brush);
                var height = Math.Max(iconSize, text.Height);
                row.Elements.Add(new(new Rect(0, (height - iconSize) / 2, icon.Width, iconSize), icon.Draw, icon.Url, icon.Tooltip));
                row.AddText(label, text, icon.Width + gap, (height - text.Height) / 2, label);
                return row;
            });
        }

        OverlayIcon ShipIcon(IntelMapHoverShip ship) => new((context, rect) => DrawIntelShipIcon(context, ship, rect.TopLeft, iconSize),
            Tooltip: ship.ShipDisplayName, Role: "ship");

        void AddIdentity(IntelMapHoverHostile identity, string? membershipOverride = null, bool loss = false)
        {
            var icons = new List<OverlayIcon>();
            if (identity.ShipTypeId is > 0 || !string.Equals(identity.ShipDisplayName, "Unknown", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(identity.ShipDisplayName))
                icons.Add(ShipIcon(new IntelMapHoverShip { ShipDisplayName = identity.ShipDisplayName,
                    ShipIconKey = identity.ShipIconKey, ShipTypeId = identity.ShipTypeId }));
            if (identity.CorporationId is int corporation && corporation > 0)
                icons.Add(new((context, rect) => DrawOrganizationLogoChip(context, rect.X, rect.Y, iconSize, "corporations", corporation),
                    $"https://zkillboard.com/corporation/{corporation}/", identity.CorporationTicker, Role: "corporation"));
            if (identity.AllianceId is int alliance && alliance > 0)
                icons.Add(new((context, rect) => DrawOrganizationLogoChip(context, rect.X, rect.Y, iconSize, "alliances", alliance),
                    $"https://zkillboard.com/alliance/{alliance}/", identity.AllianceTicker, Role: "alliance"));
            icons.Add(new((context, rect) => DrawCharacterPortraitChip(context, rect.X, rect.Y, iconSize,
                identity.CharacterId ?? 0, identity.Name), identity.CharacterId is > 0
                    ? $"https://zkillboard.com/character/{identity.CharacterId}/" : null, identity.Name, Role: "portrait"));
            var membership = membershipOverride ?? BuildIntelMembershipTickerSummary(identity);
            Add(width =>
            {
                var row = new MapOverlayRow();
                var inset = loss ? 4 : 0;
                var lead = inset + icons.Sum(i => i.Width + gap);
                var stacked = width < lead + 48;
                var textX = stacked ? inset : lead;
                var text = Text(identity.Name, width - textX,
                    weight: FontWeight.Medium, lines: 2);
                var membershipText = string.IsNullOrWhiteSpace(membership) ? null
                    : Text(membership, width - textX, muted: true,
                        size: MapTypography.SecondarySize, lines: 2);
                var textHeight = text.Height + (membershipText?.Height ?? 0);
                var height = Math.Max(iconSize, textHeight);
                var textY = stacked ? iconSize + MapOverlayLayout.RowGap : (height - textHeight) / 2;
                var x = (double)inset;
                var y = stacked ? 0 : (height - iconSize) / 2;
                foreach (var item in icons)
                {
                    if (stacked && x > inset && x + item.Width > width)
                    {
                        x = inset;
                        y += iconSize + MapOverlayLayout.RowGap;
                    }
                    row.Elements.Add(new(new Rect(x, y, item.Width, iconSize), item.Draw, item.Url, item.Tooltip, Role: item.Role));
                    x += item.Width + gap;
                }
                if (stacked) textY = y + iconSize + MapOverlayLayout.RowGap;
                row.AddText(identity.Name, text, textX, textY, identity.Name);
                if (membershipText is not null)
                    row.AddText(membership, membershipText, textX, textY + text.Height, membership);
                if (loss)
                {
                    var rowHeight = row.Height;
                    row.Elements.Add(new(new Rect(0, 0, 2, rowHeight), (context, rect) =>
                        context.FillRectangle(new SolidColorBrush(Color.Parse("#C77878")), rect), Role: "loss-accent"));
                }
                return row;
            });
        }

        AddHeading(node.Name, InfoBoxShowSecurityStatus && node.Security is { } security
            ? RoundSecurityForDisplay(security).ToString("0.0", CultureInfo.InvariantCulture) : null, system: true, before: 0);
        var location = new List<string>();
        if (InfoBoxShowRegion && !string.IsNullOrWhiteSpace(node.RegionName)) location.Add(node.RegionName);
        if (InfoBoxShowConstellation && !string.IsNullOrWhiteSpace(node.ConstellationName)) location.Add(node.ConstellationName);
        AddText(string.Join(" · ", location), muted: true);
        if (!reduced)
        {
            if (InfoBoxShowStarClass) AddText(GetStarClassDisplayValue(node), muted: true);
            if (InfoBoxShowIncursionIcon && node.HasActiveIncursion) AddText("Incursion: Active");
            if (ShowUserSystemMarks && UserSystemMarksByNodeId?.TryGetValue(node.Id, out var mark) == true)
                AddText($"Mark: {(mark.ShowLabel && !string.IsNullOrWhiteSpace(mark.Label) ? mark.Label : mark.IconKind is { } kind ? SystemMarkIcons.GetDisplayName(kind) : "Marked")}");
            foreach (var storm in node.StormEffects.OrderByDescending(s => s.Strength).ThenBy(s => s.Type))
                AddText($"Storm: {storm.Strength} {storm.Type}");

            foreach (var line in BuildAnsiblexHoverLines(node))
            {
                // Keep direction arrows, zone colors and cost pills, wrapping individual pieces as necessary.
                Add(width => MeasureAnsiblexOverlayRow(line, width));
            }
            if (InfoBoxShowJumpRangeLy && JumpRangeDistancesByNodeId?.TryGetValue(node.Id, out var distances) == true)
                foreach (var distance in distances.OrderBy(d => d.DistanceLy))
                {
                    var label = distance.DistanceLy <= 0 ? $"{distance.OriginSystemName}: 0.00 LY (origin)"
                        : $"{distance.OriginSystemName}: {distance.DistanceLy:0.00} LY / {distance.MaxLy:0.0} LY{(distance.IsInRange ? " (in)" : " (out)")}";
                    AddIconText(label, new((context, rect) =>
                    {
                        var bitmap = distance.IsInRange ? JumpRangeInRangeIcon.Value : JumpRangeOutRangeIcon.Value;
                        if (bitmap is not null) DrawBitmap(context, bitmap, rect.TopLeft, iconSize);
                    }), brush: _jumpRangeOriginColorByNodeId.TryGetValue(distance.OriginNodeId, out var color) ? GetCachedBrush(MapTypography.ReadableColor(color)) : null);
                }
            if (InfoBoxShowSovUpgradeIcon)
            {
                foreach (var upgrade in GetVisibleSovUpgrades(node.SovUpgrades, OverlaySovUpgradeFilterKeys).OrderBy(u => u.UpgradeName, StringComparer.OrdinalIgnoreCase))
                    AddIconText(IsSingleLevelSovUpgrade(upgrade.UpgradeName) ? upgrade.UpgradeName : $"{upgrade.UpgradeName} {upgrade.Tier}",
                        new((context, rect) => DrawSovUpgradeIcon(context, upgrade, rect.TopLeft, iconSize,
                            upgrade.MiningSiteStatus == MiningSiteStatus.Available ? 1 : 0.38)));
                if (_overlayExplorationOverlapByNodeId.TryGetValue(node.Id, out var overlap) && overlap > 0)
                {
                    var source = GetNodeExplorationDetector(node, OverlaySovUpgradeFilterKeys) ?? new SovUpgradeEntry { UpgradeName = "Exploration Detector", Tier = 1 };
                    AddIconText($"Exploration overlap x{overlap}", new((context, rect) => DrawSovUpgradeIcon(context, source,
                        rect.TopLeft, iconSize, _overlayExplorationSourceNodeIds.Contains(node.Id) ? 1 : 0.5)));
                }
            }
            foreach (var wh in node.HubWormholeConnections.OrderBy(c => c.HubType))
                Add(width => MeasureWormholeOverlayRow(wh, width));

            if (ShowInfoBoxCharacterPresence && CharacterPresenceCharacterIdsByNodeId?.TryGetValue(node.Id, out var ids) == true && ids.Count > 0)
            {
                var portraits = new List<OverlayIcon>();
                IReadOnlyList<string>? names = null;
                if (CharacterPresenceNamesByNodeId is not null) CharacterPresenceNamesByNodeId.TryGetValue(node.Id, out names);
                var count = Math.Min(Math.Clamp(CharacterPresenceHoverMaxNames, 1, 12), ids.Count);
                for (var i = 0; i < count; i++)
                {
                    var id = ids[i];
                    var name = names is not null && i < names.Count ? names[i] : null;
                    portraits.Add(new((context, rect) => DrawCharacterPortraitChip(context, rect.X, rect.Y, iconSize, id, name), Tooltip: name));
                }
                Add(width => MeasureOverlayIconFlow(portraits, width));
                if (ids.Count > count) AddText($"+{ids.Count - count}", muted: true);
            }

        }
        if (!reduced)
        {
            var footer = new List<OverlayIcon>();
            if (InfoBoxShowA0StarIcon && IsA0BlueSmall(node)) footer.Add(new((c, r) => DrawA0Icon(c, r.TopLeft, iconSize), Tooltip: "A0 star"));
            if (InfoBoxShowJoveObservatoryIcon && node.HasJoveObservatory) footer.Add(new((c, r) => DrawJoveObservatoryIcon(c, r.TopLeft, iconSize), Tooltip: "Jove Observatory"));
            if (InfoBoxShowIceBeltsIcon && node.IceFieldCount > 0) footer.Add(new((c, r) => DrawIceFieldIcon(c, r.TopLeft, iconSize), Tooltip: "Ice fields"));
            if (InfoBoxShowStormIcon && node.StormEffects.Count > 0) footer.Add(new((c, r) => DrawStormIcon(c, node, r.TopLeft, iconSize), Tooltip: "Storm"));
            if (InfoBoxShowWormholeIcon && node.HubWormholeConnections.Count > 0) footer.Add(new((c, r) => DrawHubWormholeIcon(c, node, r.TopLeft, iconSize), Tooltip: "Wormholes"));
            // Incursion and activity counts appear once; the count icons retain the full values.
            void CountIcon(bool enabled, int value, Func<Avalonia.Media.Imaging.Bitmap?> bitmap, string label)
            {
                if (!enabled || value <= 0) return;
                var text = MapTypography.Create(value.ToString(CultureInfo.InvariantCulture), MapTypography.SecondarySize, OverlayTextBrush, FontWeight.SemiBold);
                footer.Add(new((context, rect) =>
                {
                    if (bitmap() is { } image) DrawBitmap(context, image, rect.TopLeft, iconSize);
                    context.DrawText(text, new Point(rect.X + iconSize + gap, rect.Y + (iconSize - text.Height) / 2));
                }, Tooltip: $"{label}: {value}", Width: iconSize + gap + text.Width));
            }
            CountIcon(InfoBoxShowSystemJumps, node.SystemJumps, () => SystemJumpsIcon.Value, "Jumps");
            CountIcon(InfoBoxShowShipKills, node.ShipKills, () => ShipKillsIcon.Value, "Ship kills");
            CountIcon(InfoBoxShowPodKills, node.PodKills, () => PodKillsIcon.Value, "Pod kills");
            CountIcon(InfoBoxShowNpcKills, node.NpcKills, () => NpcKillsIcon.Value, "NPC kills");
            if (footer.Count > 0) Add(width => MeasureOverlayIconFlow(footer, width), MapOverlayLayout.SectionGap);
            if (InfoBoxShowPlanets)
            {
                var planets = GetVisiblePlanetTypeIds(node.PlanetTypeIds, OverlayPlanetFilterTypeIds)
                    .Select(id => new OverlayIcon((context, rect) =>
                    {
                        if (GetPlanetTypeIcon(id) is { } bitmap) DrawBitmap(context, bitmap, rect.TopLeft, iconSize);
                    })).ToList();
                if (planets.Count > 0) Add(width => MeasureOverlayIconFlow(planets, width, PlanetIconsPerRow));
            }
        }

        var intelReports = IntelRecentReportsByNodeId?.GetValueOrDefault(node.Id) ?? [];
        var killReports = ZkillRecentReportsByNodeId?.GetValueOrDefault(node.Id) ?? [];
        void AddIntel(IntelMapHoverReport intel)
        {
            AddHeading($"Intel · {intel.HostileCount} hostile{(intel.HostileCount == 1 ? "" : "s")}", FormatRelativeAge(intel.TimestampUtc),
                GetCachedBrush(MapTypography.ReadableColor(GetIntelHostileColor(intel.HostileCount))));
            foreach (var ship in intel.Ships.Where(s => !string.IsNullOrWhiteSpace(s.ShipDisplayName) && !string.Equals(s.ShipDisplayName, "Unknown", StringComparison.OrdinalIgnoreCase)))
                AddIconText($"{ship.Count}× {ship.ShipDisplayName}", ShipIcon(ship), weight: FontWeight.Medium);
            foreach (var hostile in intel.Hostiles.Take(4)) AddIdentity(hostile);
            var overflow = new List<string>();
            if (intel.HiddenShipCount > 0) overflow.Add($"+{intel.HiddenShipCount} ships");
            if (intel.HiddenHostileCount > 0) overflow.Add($"+{intel.HiddenHostileCount} hostiles");
            AddText(string.Join(" · ", overflow), muted: true);
            AddMessage(intel.MessageText);
        }
        void AddKill(IntelMapHoverKillmail kill)
        {
            var count = kill.AttackerCount > 0 ? kill.AttackerCount : kill.Attackers.Count + kill.HiddenAttackerCount;
            var value = kill.IskLostLabel.Replace("ISK Lost: ", "", StringComparison.OrdinalIgnoreCase);
            AddHeading($"Killmail · {count} involved", FormatRelativeAge(kill.TimestampUtc), icon: new((context, rect) =>
            {
                if (KillmailIcon.Value is { } bitmap) DrawBitmap(context, bitmap, rect.TopLeft, MapOverlayLayout.HeadingIconSize);
            }, string.IsNullOrWhiteSpace(kill.KillmailUrl) ? null : kill.KillmailUrl, "Open killmail",
                Width: MapOverlayLayout.HeadingIconSize, Height: MapOverlayLayout.HeadingIconSize));
            var lossLabel = $"Lost · {kill.VictimShipDisplayName}";
            if (!string.IsNullOrWhiteSpace(value))
                lossLabel += $" | {value}" + (value.TrimEnd().EndsWith("ISK", StringComparison.OrdinalIgnoreCase) ? "" : " ISK");
            AddText(lossLabel, brush: new SolidColorBrush(Color.Parse("#E8A0A0")));
            AddIdentity(new IntelMapHoverHostile { Name = kill.VictimName, CharacterId = kill.VictimCharacterId,
                CorporationId = kill.VictimCorporationId, AllianceId = kill.VictimAllianceId,
                ShipTypeId = kill.VictimShipTypeId, ShipDisplayName = kill.VictimShipDisplayName }, kill.VictimMembership, loss: true);
            if (kill.Attackers.Count > 0 || kill.HiddenAttackerCount > 0)
            {
                Separator();
            }
            foreach (var attacker in kill.Attackers.Take(3)) AddIdentity(attacker);
            if (kill.HiddenAttackerCount > 0) AddText($"+{kill.HiddenAttackerCount} attackers", muted: true);
        }
        var timeline = intelReports.Select(intel => (intel.TimestampUtc, Id: IntelReportId(intel), Intel: (IntelMapHoverReport?)intel, Kill: (IntelMapHoverKillmail?)null))
            .Concat(killReports.Select(kill => (kill.TimestampUtc, Id: KillReportId(kill), Intel: (IntelMapHoverReport?)null, Kill: (IntelMapHoverKillmail?)kill)))
            .Where(entry => popupReports is null || popupReports.Contains(entry.Id))
            .OrderByDescending(entry => entry.TimestampUtc).ThenBy(entry => entry.Id, StringComparer.Ordinal);
        foreach (var entry in timeline)
        {
            currentReportId = entry.Id;
            Separator();
            if (entry.Intel is not null) AddIntel(entry.Intel);
            if (entry.Kill is not null) AddKill(entry.Kill);
        }
        currentReportId = null;

        var layout = MapOverlayLayout.Measure(rows, maxWidth, minimumWidth);
        _overlayLayouts[cacheKey] = layout;
        return layout;
    }

    private static string IntelReportId(IntelMapHoverReport report) => string.IsNullOrEmpty(report.ReportId)
        ? $"intel:{report.TimestampUtc.Ticks}:{report.ReporterName}:{report.MessageText}" : report.ReportId;
    private static string KillReportId(IntelMapHoverKillmail report) => string.IsNullOrEmpty(report.ReportId)
        ? $"kill:{report.KillmailUrl}:{report.TimestampUtc.Ticks}" : report.ReportId;

    private static string CompactOverlayMessage(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static MapOverlayRow FlowOverlayParts(IReadOnlyList<(Size Size, Action<DrawingContext, Rect> Draw)> parts, double width)
    {
        var row = new MapOverlayRow();
        double x = 0, y = 0, lineHeight = 0;
        foreach (var part in parts)
        {
            if (x > 0 && x + part.Size.Width > width)
            {
                x = 0;
                y += lineHeight + MapOverlayLayout.RowGap;
                lineHeight = 0;
            }
            row.Elements.Add(new(new Rect(x, y, part.Size.Width, part.Size.Height), part.Draw));
            x += part.Size.Width + MapOverlayLayout.InlineGap;
            lineHeight = Math.Max(lineHeight, part.Size.Height);
        }
        return row;
    }

    private MapOverlayRow MeasureAnsiblexOverlayRow(AnsiblexHoverLine line, double width)
    {
        var parts = new List<(Size Size, Action<DrawingContext, Rect> Draw)>();
        var chip = CreateZoneChipText(line.Zone);
        const double height = 18;
        parts.Add((new Size(chip.Width + 8, height), (context, rect) =>
        {
            context.FillRectangle(GetCachedBrush(Color.Parse(AnsiblexZones.ColorHex(line.Zone))), rect, 3);
            context.DrawText(chip, new Point(rect.X + 4, rect.Y + (height - chip.Height) / 2));
        }));
        void AddTextPart(FormattedText? text)
        {
            if (text is null) return;
            if (double.IsFinite(width)) text.MaxTextWidth = Math.Max(1, width);
            var partHeight = Math.Max(height, text.Height);
            parts.Add((new Size(text.Width, partHeight), (context, rect) =>
                context.DrawText(text, new Point(rect.X, rect.Y + (partHeight - text.Height) / 2))));
        }
        AddTextPart(line.Label);
        if (line.ShowCapitalMark)
            parts.Add((new Size(8, height), (context, rect) => context.DrawEllipse(null, AnsiblexCapitalRingPen,
                new Point(rect.X + 4, rect.Y + height / 2), 3.5, 3.5)));
        AddTextPart(line.Meta);
        AddTextPart(line.Ship);
        void AddCost(FormattedText? cost, int zone, bool pointsRight)
        {
            if (cost is null) return;
            if (double.IsFinite(width)) cost.MaxTextWidth = Math.Max(1, width - 5 - AnsiblexDirectionArrowWidth - 5 - 6);
            var partHeight = Math.Max(height, cost.Height);
            parts.Add((new Size(AnsiblexCostPillWidth(cost), partHeight), (context, rect) =>
                DrawAnsiblexCostPill(context, rect.TopLeft, cost, zone, partHeight, pointsRight)));
        }
        AddCost(line.Outbound, line.OutboundZone, true);
        AddCost(line.Inbound, line.InboundZone, false);
        return FlowOverlayParts(parts, width);
    }

    private static MapOverlayRow MeasureWormholeOverlayRow(HubWormholeConnection wh, double width)
    {
        var parts = new List<(Size Size, Action<DrawingContext, Rect> Draw)>();
        void Part(string label, string color)
        {
            var text = MapTypography.Create(label, MapTypography.BodySize, new SolidColorBrush(Color.Parse(color)), maxWidth: width);
            parts.Add((new Size(text.Width, text.Height), (context, rect) => context.DrawText(text, rect.TopLeft)));
        }
        Part($"{wh.HubType}", "#EAF0F8");
        Part($"[{(string.IsNullOrWhiteSpace(wh.InSignature) ? "-" : wh.InSignature)}]", "#5EDC8A");
        Part($"[{(string.IsNullOrWhiteSpace(wh.OutSignature) ? "-" : wh.OutSignature)}]", "#F07171");
        Part($"[{GetWormholeMassShort(wh.MaxShipSize)}]", "#F5C06A");
        return FlowOverlayParts(parts, width);
    }

    private static MapOverlayRow MeasureOverlayIconFlow(IReadOnlyList<OverlayIcon> icons, double width, int maxColumns = int.MaxValue)
    {
        var row = new MapOverlayRow();
        double x = 0, y = 0;
        var column = 0;
        foreach (var icon in icons)
        {
            if (x > 0 && (x + icon.Width > width || column >= maxColumns))
            {
                x = 0;
                column = 0;
                y += MapOverlayLayout.IconSize + MapOverlayLayout.RowGap;
            }
            row.Elements.Add(new(new Rect(x, y, icon.Width, MapOverlayLayout.IconSize), icon.Draw, icon.Url, icon.Tooltip));
            x += icon.Width + MapOverlayLayout.InlineGap;
            column++;
        }
        return row;
    }

}
