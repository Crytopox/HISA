using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hisa.Core;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;

namespace Hisa.Rendering.Tests;

public sealed partial class MapOverlayLayoutTests
{
    [AvaloniaFact]
    public void CompactReportsUseSmallerIconsAndSingleGapsAfterSeparators()
    {
        var map = CreateMap();
        var layout = map.BuildNodeOverlay(Node, 784);
        Assert.All(layout.Elements.Where(e => e.Role is "ship" or "portrait" or "corporation" or "alliance"),
            e => Assert.Equal(new Size(20, 20), e.Bounds.Size));
        var killIcon = layout.Elements.Single(e => e.Url == "https://zkillboard.com/kill/123/");
        Assert.Equal(new Size(16, 16), killIcon.Bounds.Size);
        foreach (var separator in layout.Elements.Where(e => e.Role == "separator"))
        {
            var next = layout.Rows.First(r => r.Top > separator.Bounds.Top);
            Assert.Equal(1, next.Top - separator.Bounds.Bottom, 6);
        }
        Assert.Equal(4, layout.Rows[0].Left);
        Assert.Equal(4, layout.Rows[0].Top);
        // One report fits a short card while preserving the lost ship and identity.
        map = new MapControl { ZkillRecentReportsByNodeId =
            new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [Kill("compact", -1)] } };
        layout = map.BuildNodeOverlay(Node, 784);
        Assert.True(layout.Size.Height < 110);
        Assert.Contains(layout.Elements, e => e.Text == "Lost · Osprey | 24m ISK");
        Assert.Contains(layout.Elements, e => e.Text == "Victim compact");
        var popup = map.BuildNodeOverlay(Node, 784, new HashSet<string> { "compact" });
        var close = MapControl.PopupCloseBounds(new Rect(popup.Size));
        Assert.True(popup.Elements.Single(e => e.Text == "Wicked Creek · 0FC-ZX").Bounds.Top >= close.Bottom);
        Assert.Equal(10, MapTypography.LabelSize);
        Assert.Equal(11, MapTypography.BodySize);
        Assert.Equal(12, MapTypography.HeadingSize);
    }

    [AvaloniaFact]
    public void UniversePopupSettingPreservesRegionalPopupsAndManualOverlays()
    {
        var map = CrowdedMap();
        map.ReportPopupsShowInUniverse = false;
        using (Paint(map)) { }
        Assert.Equal(10, map.VisibleReportOverlays.Count);
        map.ViewMode = MapViewMode.Universe;
        using (Paint(map)) { }
        Assert.Empty(map.VisibleReportOverlays);
        map.SelectedNodeId = 1;
        using (Paint(map)) { }
        Assert.False(Assert.Single(map.VisibleReportOverlays).Automatic);
        map.SelectedNodeId = null;
        map.ReportPopupsShowInUniverse = true;
        using (Paint(map)) { }
        Assert.Equal(10, map.VisibleReportOverlays.Count);
    }

    [AvaloniaFact]
    public void KillmailPopupSettingDoesNotSuppressIntelOrManualHistory()
    {
        var map = new MapControl { Graph = new MapGraph { Nodes = [Node], Links = [] }, ViewMode = MapViewMode.Region,
            ReportPopupsShowKillmails = false,
            IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [Report("intel", 0)] },
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [Kill("kill", 0)] },
            ReportPopupRequests = [new("intel", Node.Name, DateTimeOffset.UtcNow), new("kill", Node.Name, DateTimeOffset.UtcNow, IsKillmail: true)] };
        ArrangeForReports(map);
        using (Paint(map)) { }
        Assert.Single(map.VisibleReportOverlays);
        Assert.Null(FindLink(map, "https://zkillboard.com/kill/kill/"));
        map.ReportPopupsShowKillmails = true;
        using (Paint(map)) { }
        Assert.Null(FindLink(map, "https://zkillboard.com/kill/kill/"));
        map.SelectedNodeId = 1;
        using (Paint(map)) { }
        Assert.NotNull(FindLink(map, "https://zkillboard.com/kill/kill/"));
    }

    [AvaloniaFact]
    public void UniverseLabelsAndLinksAreCompositedBehindThePopupSurface()
    {
        var nodes = new[] {
            new MapNode { Id = 1, Name = "E-JCUS", X = .4, Y = .4, RegionId = 1, RegionName = "Wicked Creek" },
            new MapNode { Id = 2, Name = "SHBF-V", X = .7, Y = .55, RegionId = 1, RegionName = "Wicked Creek" } };
        var map = new MapControl { Graph = new MapGraph { Nodes = nodes, Links = [new MapLink { FromId = 1, ToId = 2 }] },
            ViewMode = MapViewMode.Universe, EnableLinkAnimations = false,
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [Kill("one", 0)] },
            ReportPopupRequests = [new("one", nodes[0].Name, DateTimeOffset.UtcNow)] };
        ArrangeForReports(map);
        using var actual = Paint(map);
        var card = Assert.Single(map.VisibleReportOverlays).Bounds;
        var layout = map.BuildNodeOverlay(nodes[0], 784, new HashSet<string> { "one" });
        var content = layout.Elements.Select(e => e.Bounds.Translate(new Vector(card.X, card.Y))).ToArray();
        using var background = new RenderTargetBitmap(new PixelSize(800, 600), new Vector(96, 96));
        using (var context = background.CreateDrawingContext(true)) map.DrawStaticScene(context, new Rect(map.Bounds.Size));
        using var result = DecodeBitmap(actual);
        using var underlay = DecodeBitmap(background);
        var checkedPixels = 0;
        for (var y = (int)card.Top + 4; y < card.Bottom - 4; y++)
        for (var x = (int)card.Left + 4; x < card.Right - 4; x++)
        {
            var point = new Point(x, y);
            if (content.Any(r => r.Inflate(2).Contains(point)) || MapControl.PopupCloseBounds(card).Contains(point)) continue;
            var source = underlay.GetPixel(x, y);
            if (Math.Max(source.Red, Math.Max(source.Green, source.Blue)) < 80) continue;
            var pixel = result.GetPixel(x, y);
            Assert.InRange(Math.Abs(pixel.Red - (16 * 230 + source.Red * 25) / 255.0), 0, 3);
            Assert.InRange(Math.Abs(pixel.Green - (23 * 230 + source.Green * 25) / 255.0), 0, 3);
            Assert.InRange(Math.Abs(pixel.Blue - (34 * 230 + source.Blue * 25) / 255.0), 0, 3);
            checkedPixels++;
        }
        Assert.True(checkedPixels > 0, "Expected map labels or links beneath a blank area of the popup.");
    }

    [AvaloniaFact]
    public void PopupStartsAtHoverPositionAndCloseDismissesOnlyItsOwnSystem()
    {
        var map = new MapControl { Graph = new MapGraph { Nodes = [Node], Links = [] }, ViewMode = MapViewMode.Region,
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [Kill("one", 0)] },
            ReportPopupRequests = [new("one", Node.Name, DateTimeOffset.UtcNow)] };
        ArrangeForReports(map);
        using (Paint(map)) { }
        var card = Assert.Single(map.VisibleReportOverlays);
        var anchor = map.WorldToScreen(new Point(Node.X, Node.Y));
        Assert.Equal(MapOverlayLayout.Place(anchor, card.Bounds.Size, map.Bounds.Size), card.Bounds);
        Assert.False(card.Bounds.Inflate(6).Contains(anchor));
        Assert.True(map.DismissReportPopupAt(MapControl.PopupCloseBounds(card.Bounds).Center));
        using (Paint(map)) { }
        Assert.Empty(map.VisibleReportOverlays);
        map.ReportPopupRequests = map.ReportPopupRequests.ToArray();
        using (Paint(map)) { }
        Assert.Empty(map.VisibleReportOverlays);
        map.SelectedNodeId = 1;
        using (Paint(map)) { }
        Assert.False(map.DismissReportPopupAt(MapControl.PopupCloseBounds(Assert.Single(map.VisibleReportOverlays).Bounds).Center));
    }

    [AvaloniaFact]
    public void OpeningAndClosingManualCardsNeverRepositionsAutomaticCards()
    {
        var map = CrowdedMap();
        using (Paint(map)) { }
        var before = map.VisibleReportOverlays.ToDictionary(v => v.NodeId, v => v.Bounds);
        map.SelectedNodeId = 1;
        using (Paint(map)) { }
        Assert.All(map.VisibleReportOverlays.Where(v => v.Automatic), v => Assert.Equal(before[v.NodeId], v.Bounds));
        Assert.False(map.VisibleReportOverlays.Last().Automatic);
        map.SelectedNodeId = null;
        using (Paint(map)) { }
        Assert.All(map.VisibleReportOverlays, v => Assert.Equal(before[v.NodeId], v.Bounds));
        var top = map.VisibleReportOverlays.Last();
        Assert.True(map.DismissReportPopupAt(MapControl.PopupCloseBounds(top.Bounds).Center));
        using (Paint(map)) { }
        Assert.Equal(9, map.VisibleReportOverlays.Count);
    }

    [AvaloniaFact]
    public void AllSystemFieldsPrecedeReportsAndMessagesDoNotWidenCards()
    {
        var map = new MapControl { InfoBoxShowSystemJumps = true, InfoBoxShowPlanets = true,
            IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [Report("intel", 0)] } };
        var node = new MapNode { Id = 1, X = 0.5, Y = 0.5, Name = "E-JCUS", SystemJumps = 123, PlanetTypeIds = [11, 12] };
        var layout = map.BuildNodeOverlay(node, 784);
        var firstReport = layout.Reports.Values.Min(r => r.Top);
        Assert.All(layout.Elements.Where(e => e.ReportId is null), e => Assert.True(e.Bounds.Bottom < firstReport));
        Assert.Contains(layout.Elements, e => e.Tooltip == "Jumps: 123");
        map.IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] =
            [new IntelMapHoverReport { ReportId = "short", TimestampUtc = DateTime.UtcNow, ReporterName = "Scout", HostileCount = 10,
                MessageText = "short", Ships = [], Hostiles = [] }] };
        Assert.Equal(layout.Size.Width, map.BuildNodeOverlay(node, 784).Size.Width);
        Assert.InRange(layout.Size.Width, 180, 300);
        Assert.Equal(124, map.BuildNodeOverlay(node, 124).Size.Width);
    }

    [AvaloniaFact]
    public void OpenWidthsGrowForEnrichmentAndDoNotShrinkUntilClosed()
    {
        var map = new MapControl { Graph = new MapGraph { Nodes = [Node], Links = [] }, ViewMode = MapViewMode.Region,
            SelectedNodeId = 1, IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [Report("report", 0)] } };
        ArrangeForReports(map);
        using (Paint(map)) { }
        var narrow = Assert.Single(map.VisibleReportOverlays).Bounds.Width;
        var rich = new IntelMapHoverReport { ReportId = "report", TimestampUtc = DateTime.UtcNow, ReporterName = "Scout",
            HostileCount = 10, MessageText = "text", Ships = [], Hostiles = [new IntelMapHoverHostile {
                Name = "Very long character identity with more information", CorporationId = 1, AllianceId = 2, ShipTypeId = 3 }] };
        map.IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [rich] };
        using (Paint(map)) { }
        var wide = Assert.Single(map.VisibleReportOverlays).Bounds.Width;
        Assert.True(wide > narrow);
        map.IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [Report("report", 0)] };
        using (Paint(map)) { }
        Assert.Equal(wide, Assert.Single(map.VisibleReportOverlays).Bounds.Width);
        map.SelectedNodeId = null;
        using (Paint(map)) { }
        map.SelectedNodeId = 1;
        using (Paint(map)) { }
        Assert.Equal(narrow, Assert.Single(map.VisibleReportOverlays).Bounds.Width);
    }

    [AvaloniaFact]
    public void CrowdedCardsKeepEveryPopupStayStableAndStackByArrival()
    {
        var map = CrowdedMap();
        using (Paint(map)) { }
        var before = map.VisibleReportOverlays.ToArray();
        Assert.Equal(10, before.Length);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), before.Select(v => v.NodeId));
        using (Paint(map)) { }
        Assert.Equal(before, map.VisibleReportOverlays);
        var exposed = before.First(v => before.Where(x => x.NodeId > v.NodeId).All(x => !x.Bounds.Contains(v.Bounds.TopLeft + new Vector(3, 3))));
        map.UpdateReportOverlayHover(exposed.Bounds.TopLeft + new Vector(3, 3));
        using (Paint(map)) { }
        Assert.Equal(exposed.NodeId, map.VisibleReportOverlays.Last().NodeId);
        map.UpdateReportOverlayHover(new Point(-1, -1));
        using (Paint(map)) { }
        Assert.Equal(before, map.VisibleReportOverlays);
        var newest = before.Last();
        var link = FindLink(map, "https://zkillboard.com/kill/c10/");
        Assert.NotNull(link);
        Assert.True(newest.Bounds.Contains(link.Value));
        var history = map.ZkillRecentReportsByNodeId!.ToDictionary(p => p.Key, p => p.Value);
        history[1] = [Kill("c1", -1), Kill("delayed", -100)];
        map.ZkillRecentReportsByNodeId = history;
        map.ReportPopupRequests = map.ReportPopupRequests.Append(new("delayed", "System 1", DateTimeOffset.UtcNow)).ToArray();
        using (Paint(map)) { }
        Assert.Equal(1, map.VisibleReportOverlays.Last().NodeId);
        Assert.All(map.VisibleReportOverlays.Where(v => v.NodeId != 1),
            v => Assert.Equal(before.Single(old => old.NodeId == v.NodeId).Bounds, v.Bounds));
        map.SelectedNodeId = 1;
        using (Paint(map)) { }
        Assert.False(map.VisibleReportOverlays.Last().Automatic);
        Assert.Equal(10, map.VisibleReportOverlays.Count);
    }

    [AvaloniaFact]
    public void PanZoomAndViewportChangesRetainLocalCardsAndInset()
    {
        var map = CrowdedMap();
        using (Paint(map)) { }
        map.PanBy(20, 15);
        map.ZoomBy(1.25);
        using (Paint(map)) { }
        Assert.Equal(10, map.VisibleReportOverlays.Count);
        map.Arrange(new Rect(0, 0, 600, 450));
        using (Paint(map)) { }
        Assert.Equal(10, map.VisibleReportOverlays.Count);
        Assert.All(map.VisibleReportOverlays, v => Assert.True(v.Bounds.Left >= 8 && v.Bounds.Top >= 8
            && v.Bounds.Right <= 592 && v.Bounds.Bottom <= 442));
        var placements = map.VisibleReportOverlays.ToArray();
        using (Paint(map)) { }
        Assert.Equal(placements, map.VisibleReportOverlays);
    }

    [AvaloniaFact]
    public void WidthGrowthPreservesTheVisibleReportScrollAnchor()
    {
        var map = HistoryMap(8);
        using (Paint(map)) { }
        var original = Assert.Single(map.VisibleReportOverlays).Bounds;
        Assert.True(map.ScrollNodeOverlay(original.Center, -7));
        using (Paint(map)) { }
        var before = FindLink(map, "https://zkillboard.com/kill/k3/");
        Assert.NotNull(before);
        var history = map.ZkillRecentReportsByNodeId![1].ToList();
        history.Add(new IntelMapHoverKillmail { ReportId = "enriched-old", MessageText = "", TimestampUtc = DateTime.UtcNow.AddHours(-1),
            KillmailUrl = "https://zkillboard.com/kill/old/", VictimName = new string('W', 100),
            VictimMembership = "", VictimShipDisplayName = "Osprey", Attackers = [], AttackerCount = 7, IskLostLabel = "24m ISK" });
        map.ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = history };
        using (Paint(map)) { }
        var current = Assert.Single(map.VisibleReportOverlays).Bounds;
        Assert.True(current.Width > original.Width);
        var after = FindLink(map, "https://zkillboard.com/kill/k3/");
        Assert.NotNull(after);
        Assert.InRange(Math.Abs((before.Value.Y - original.Y) - (after.Value.Y - current.Y)), 0, 3);
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(780, 580)]
    [InlineData(400, 300)]
    public void StandardHoverPlacementStaysNearSystemsAndInsideViewport(double x, double y)
    {
        var anchor = new Point(x, y);
        var viewport = new Size(800, 600);
        var size = new Size(260, 190);
        var placed = MapOverlayLayout.Place(anchor, size, viewport);
        Assert.True(placed.Left >= 8 && placed.Top >= 8 && placed.Right <= 792 && placed.Bottom <= 592);
        Assert.True(placed.Left <= anchor.X + 110 && placed.Right >= anchor.X - 110);
        Assert.True(placed.Top <= anchor.Y + 104 && placed.Bottom >= anchor.Y - 104);
        Assert.Equal(placed, MapOverlayLayout.Place(anchor, size, viewport));
    }

    [AvaloniaFact]
    public void RenderCrowdedCompactPopupsAtEveryRequestedScale()
    {
        var directory = Environment.GetEnvironmentVariable("HISA_MAP_PREVIEW_DIR")
            ?? Path.Combine(Path.GetTempPath(), "Hisa.Rendering.Tests", "map-ui-review");
        Directory.CreateDirectory(directory);
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var map = CrowdedMap();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(800 * scale), (int)(600 * scale)), new Vector(96 * scale, 96 * scale));
            using (var context = bitmap.CreateDrawingContext(true))
            {
                map.Render(context);
                map.DrawStaticScene(context, new Rect(map.Bounds.Size));
                map.DrawReportOverlays(context);
            }
            Assert.Equal(10, map.VisibleReportOverlays.Count);
            bitmap.Save(Path.Combine(directory, $"crowded-{scale * 100:0}.png"));
        }
    }

    private static MapControl CrowdedMap()
    {
        var nodes = Enumerable.Range(1, 10).Select(i => new MapNode { Id = i, Name = $"System {i}",
            X = .5 + (i % 3) * .015, Y = .5 + (i % 2) * .015, Security = -.2 }).ToArray();
        var map = new MapControl { Graph = new MapGraph { Nodes = nodes, Links = [] }, ViewMode = MapViewMode.Region,
            ZkillRecentReportsByNodeId = nodes.ToDictionary(n => n.Id, n => (IReadOnlyList<IntelMapHoverKillmail>)[Kill($"c{n.Id}", -(int)n.Id)]) };
        ArrangeForReports(map);
        map.ReportPopupRequests = nodes.Select(n => new MapReportPopupRequest($"c{n.Id}", n.Name, DateTimeOffset.UtcNow)).ToArray();
        return map;
    }
}


