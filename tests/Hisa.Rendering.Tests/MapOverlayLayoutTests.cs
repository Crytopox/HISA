using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;
using SkiaSharp;

namespace Hisa.Rendering.Tests;

public sealed partial class MapOverlayLayoutTests
{
    [AvaloniaTheory]
    [InlineData(180)]
    [InlineData(240)]
    [InlineData(360)]
    public void CombinedReportsFitWithoutOverlapsAndKeepLinks(double width)
    {
        var map = CreateMap();
        var layout = map.BuildNodeOverlay(Node, width);
        Assert.InRange(layout.Size.Width, 1, width);
        Assert.NotEmpty(layout.Rows);
        for (var i = 1; i < layout.Rows.Count; i++)
            Assert.True(layout.Rows[i].Top >= layout.Rows[i - 1].Bottom + MapOverlayLayout.RowGap - 0.01);
        foreach (var element in layout.Elements)
        {
            Assert.True(element.Bounds.Left >= MapOverlayLayout.Padding - 0.01);
            Assert.True(element.Bounds.Right <= layout.Size.Width - MapOverlayLayout.Padding + 0.01,
                $"{element.Text ?? element.Url ?? "icon"} ends at {element.Bounds.Right} in {layout.Size.Width}");
            Assert.True(element.Bounds.Bottom <= layout.Size.Height - MapOverlayLayout.Padding + 0.01);
        }
        for (var i = 0; i < layout.Elements.Count; i++)
        for (var j = i + 1; j < layout.Elements.Count; j++)
        {
            var overlap = layout.Elements[i].Bounds.Intersect(layout.Elements[j].Bounds);
            Assert.False(overlap.Width > 0.1 && overlap.Height > 0.1,
                $"Overlapping elements: {layout.Elements[i].Text ?? "icon"} and {layout.Elements[j].Text ?? "icon"}");
        }
        var urls = layout.Elements.Where(e => e.Url is not null).Select(e => e.Url).ToArray();
        Assert.Contains("https://zkillboard.com/kill/123/", urls);
        Assert.Contains("https://zkillboard.com/character/100/", urls);
        Assert.Contains("https://zkillboard.com/corporation/200/", urls);
        Assert.Contains("https://zkillboard.com/alliance/300/", urls);
        var labels = layout.Elements.Where(e => e.Text is not null).Select(e => e.Text).ToArray();
        Assert.True(Array.FindIndex(labels, t => t!.StartsWith("Intel ·")) < Array.FindIndex(labels, t => t!.StartsWith("Killmail ·")));
        Assert.Contains("+2 ships · +3 hostiles", labels);
        Assert.Contains("+4 attackers", labels);
    }

    [AvaloniaFact]
    public void MissingOrganizationsAndMembershipDoNotReserveSlotsOrBlankRows()
    {
        var map = new MapControl { IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>
        {
            [1] = [Intel([new IntelMapHoverHostile { Name = "Pilot", CharacterId = 100 }], "")]
        } };
        var compact = map.BuildNodeOverlay(Node, 360);
        Assert.Single(compact.Elements, e => e.Url is not null);
        Assert.DoesNotContain(compact.Elements, e => e.Text == "");
        var portrait = compact.Elements.Single(e => e.Url?.Contains("character/") == true);
        var name = compact.Elements.Single(e => e.Text == "Pilot");
        Assert.Equal(portrait.Bounds.Right + MapOverlayLayout.InlineGap, name.Bounds.Left, 4);
        Assert.Equal(MapOverlayLayout.IconSize, compact.Rows.Last().Height, 4);
        map.IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>
        {
            [1] = [Intel([new IntelMapHoverHostile { Name = "Pilot", CharacterId = 100,
                CorporationId = 200, AllianceId = 300, CorporationTicker = "CORP", AllianceTicker = "ALLY" }], "")]
        };
        var full = map.BuildNodeOverlay(Node, 360);
        Assert.InRange(compact.Size.Width, 180, 300);
        Assert.InRange(full.Size.Width, compact.Size.Width, 300);
        var fullPortrait = full.Elements.Single(e => e.Url?.Contains("character/") == true);
        Assert.True(fullPortrait.Bounds.Left > portrait.Bounds.Left);
        Assert.Contains(full.Elements, e => e.Text == "[CORP]  [ALLY]");
    }

    [AvaloniaFact]
    public void MessagesRemainOneLineAndRetainHorizontallyScrollableText()
    {
        var message = "E-JCUS 100 hostiles inbound. " + string.Join(" ", Enumerable.Repeat("Long report with ships and pilots", 12));
        var map = new MapControl { IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>
        {
            [1] = [Intel([], message)]
        } };
        var narrow = map.BuildNodeOverlay(Node, 180);
        var wide = map.BuildNodeOverlay(Node, 360);
        Assert.Contains(narrow.Elements, e => e.Text == message);
        var narrowMessage = narrow.Elements.Single(e => e.MarqueeText is not null);
        var wideMessage = wide.Elements.Single(e => e.MarqueeText is not null);
        Assert.Equal(narrowMessage.Bounds.Height, wideMessage.Bounds.Height);
        Assert.True(narrowMessage.MarqueeText!.Width > narrowMessage.Bounds.Width);
        Assert.Equal(narrowMessage.ReportId, wideMessage.ReportId);
        Assert.InRange(narrow.Elements.Single(e => e.Text == message).Bounds.Width, 1, narrow.Size.Width - MapOverlayLayout.Padding * 2 + .01);
    }

    [AvaloniaFact]
    public void LongUnbrokenIdentifiersStayInsideOverlay()
    {
        var value = new string('W', 160);
        var map = new MapControl { IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>
        {
            [1] = [Intel([new IntelMapHoverHostile { Name = value, CorporationTicker = value }], value)]
        } };
        var layout = map.BuildNodeOverlay(Node, 180);
        Assert.All(layout.Elements, element => Assert.True(element.Bounds.Right <= layout.Size.Width - MapOverlayLayout.Padding + 0.01));
    }

    [AvaloniaFact]
    public void TallOverlayScrollsAndRebuildsClippedLinkTargets()
    {
        var map = CreateMap(withLinks: false);
        map.Graph = new MapGraph { Nodes = [Node], Links = [] };
        map.ViewMode = MapViewMode.Region;
        map.SelectedNodeId = Node.Id;
        map.Measure(new Size(400, 240));
        map.Arrange(new Rect(0, 0, 400, 240));
        var layout = map.BuildNodeOverlay(Node, 384);
        var rect = MapOverlayLayout.Place(map.WorldToScreen(new Point(Node.X, Node.Y)), layout.Size, map.Bounds.Size);
        Assert.True(layout.Size.Height > rect.Height);
        var killIcon = layout.Elements.Single(e => e.Url?.Contains("/kill/") == true);
        void RenderFrame()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(400, 240), new Vector(96, 96));
            using var context = bitmap.CreateDrawingContext(true);
            map.Render(context);
        }
        RenderFrame();
        Assert.False(map.ScrollNodeOverlay(new Point(399, 239), -1000));
        Assert.True(map.ScrollNodeOverlay(rect.Center, -1000));
        RenderFrame();
        var maxScroll = layout.Size.Height - rect.Height;
        var visibleLink = killIcon.Bounds.Translate(new Vector(rect.X, rect.Y - maxScroll))
            .Intersect(rect.Deflate(new Thickness(MapOverlayLayout.Padding)));
        Assert.True(visibleLink.Height > 0);
        Assert.Equal("https://zkillboard.com/kill/123/", map.GetNodeOverlayLink(visibleLink.Center));
        Assert.Null(map.GetNodeOverlayLink(new Point(rect.Left - 1, visibleLink.Center.Y)));
        Assert.True(map.ScrollNodeOverlay(rect.Center, 1000));
        RenderFrame();
        Assert.Null(map.GetNodeOverlayLink(visibleLink.Center));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(799, 0)]
    [InlineData(0, 599)]
    [InlineData(799, 599)]
    public void PlacementKeepsViewportInsetAtEveryCorner(double x, double y)
    {
        var rect = MapOverlayLayout.Place(new Point(x, y), new Size(360, 900), new Size(800, 600));
        Assert.True(rect.Left >= 8 && rect.Top >= 8 && rect.Right <= 792 && rect.Bottom <= 592);
        Assert.Equal(584, rect.Height);
    }

    [AvaloniaFact]
    public void BundledInterUsesSlashedZeroAndTabularNumbers()
    {
        var zero = MapTypography.Create("0", 24, Brushes.White);
        var plain = new FormattedText("0", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(MapTypography.Family), 24, Brushes.White);
        Assert.Contains("Inter", new Typeface(MapTypography.Family).GlyphTypeface.FamilyName);
        using var slashed = RenderText(zero);
        using var ordinary = RenderText(plain);
        Assert.False(slashed.Bytes.SequenceEqual(ordinary.Bytes), "The zero feature must change the rendered glyph.");
        var widths = "0123456789".Select(c => MapTypography.Create(c.ToString(), 11, Brushes.White).Width).ToArray();
        Assert.All(widths, w => Assert.Equal(widths[0], w, 4));
    }

    [AvaloniaFact]
    public void RenderDenseAndSparseMapsAtAllRequestedScales()
    {
        var directory = Environment.GetEnvironmentVariable("HISA_MAP_PREVIEW_DIR")
            ?? Path.Combine(Path.GetTempPath(), "Hisa.Rendering.Tests", "map-ui-review");
        Directory.CreateDirectory(directory);
        foreach (var dense in new[] { false, true })
        foreach (var scaling in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var map = CreateMap(withLinks: false);
            var nodes = new List<MapNode> { Node };
            var links = new List<MapLink>();
            var columns = dense ? 18 : 7;
            var rows = dense ? 12 : 5;
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var id = 2 + y * columns + x;
                nodes.Add(new MapNode { Id = id, Name = $"{(x % 2 == 0 ? "O0" : "1I")}-{x:D2}{y % 10}",
                    X = (x + 0.5) / columns, Y = (y + 0.5) / rows });
                if (x > 0) links.Add(new MapLink { FromId = id - 1, ToId = id });
                if (y > 0) links.Add(new MapLink { FromId = id - columns, ToId = id });
            }
            map.Graph = new MapGraph { Nodes = nodes, Links = links };
            map.ViewMode = MapViewMode.Region;
            map.SelectedNodeId = Node.Id;
            map.Measure(new Size(1100, 740));
            map.Arrange(new Rect(0, 0, 1100, 740));
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(1100 * scaling), (int)(740 * scaling)), new Vector(96 * scaling, 96 * scaling));
            using (var context = bitmap.CreateDrawingContext(true))
            {
                map.Render(context);
                // Detached controls have no TopLevel RenderScaling. Draw directly for DPI review
                // rather than enlarging the detached control's 1x bitmap cache.
                map.DrawStaticScene(context, new Rect(map.Bounds.Size));
                map.DrawReportOverlays(context);
            }
            bitmap.Save(Path.Combine(directory, $"{(dense ? "dense" : "sparse")}-{scaling * 100:0}.png"));
        }
        using var atlas = new RenderTargetBitmap(new PixelSize(1500, 190), new Vector(96, 96));
        using (var context = atlas.CreateDrawingContext(true))
        {
            context.FillRectangle(new SolidColorBrush(Color.Parse("#0D131D")), new Rect(0, 0, 1500, 190));
            var y = 12.0;
            foreach (var size in new[] { 10.0, 11.0, 12.0, 24.0 })
            {
                var text = MapTypography.Create($"{size:0} DIP   E-JCUS  O0o  1Il  0123456789   -0.2   36.9m ISK   100 hostiles", size, Brushes.White, FontWeight.Medium);
                context.DrawText(text, new Point(12, y));
                y += text.Height + 10;
            }
        }
        atlas.Save(Path.Combine(directory, "typography.png"));
    }

    private static SKBitmap RenderText(FormattedText text)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(80, 60), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext(true)) context.DrawText(text, new Point(8, 8));
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static readonly MapNode Node = new() { Id = 1, Name = "E-JCUS", X = 0.55, Y = 0.45,
        Security = -0.2, RegionName = "Wicked Creek", ConstellationName = "0FC-ZX" };

    private static IntelMapHoverReport Intel(IReadOnlyList<IntelMapHoverHostile> hostiles, string message) => new()
    {
        TimestampUtc = DateTime.UtcNow.AddMinutes(-1), ReporterName = "Scout", MessageText = message,
        HostileCount = 100, Ships = [], Hostiles = hostiles
    };

    private static MapControl CreateMap(bool withLinks = true)
    {
        var hostile = new IntelMapHoverHostile { Name = "A pilot with an unusually long name O0-1Il",
            CharacterId = withLinks ? 100 : null, CorporationId = withLinks ? 200 : null,
            AllianceId = withLinks ? 300 : null, ShipTypeId = withLinks ? 123 : null, CorporationTicker = "CORP", AllianceTicker = "ALLY" };
        return new MapControl
        {
            InfoBoxShowRegion = true, InfoBoxShowConstellation = true, InfoBoxShowSecurityStatus = true,
            IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>>
            {
                [1] = [new IntelMapHoverReport { TimestampUtc = DateTime.UtcNow.AddSeconds(-20), ReporterName = "Scout",
                    MessageText = "E-JCUS hostile fleet reported moving towards the next gate. 100 pilots, including logistics and tackle.",
                    HostileCount = 100, Ships = [new IntelMapHoverShip { ShipDisplayName = "Osprey Navy Issue", ShipIconKey = "unknown", Count = 63 },
                        new IntelMapHoverShip { ShipDisplayName = "Vulture", ShipIconKey = "unknown", Count = 6 }],
                    Hostiles = [hostile], HiddenHostileCount = 3, HiddenShipCount = 2 }]
            },
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>>
            {
                [1] = [new IntelMapHoverKillmail { TimestampUtc = DateTime.UtcNow.AddMinutes(-2), MessageText = "Capsule destroyed (1 attacker, 36,867,220 ISK).",
                    KillmailUrl = "https://zkillboard.com/kill/123/", VictimName = "Rokyn O0", VictimMembership = "[SNOOO] [CONDI]",
                    VictimCharacterId = withLinks ? 101 : null, VictimShipDisplayName = "Capsule",
                    Attackers = [hostile], HiddenAttackerCount = 4, IskLostLabel = "36.9m ISK" }]
            }
        };
    }
}
