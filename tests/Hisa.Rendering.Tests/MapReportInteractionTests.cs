using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hisa.Core;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;
using SkiaSharp;

namespace Hisa.Rendering.Tests;

public sealed partial class MapOverlayLayoutTests
{
    [AvaloniaFact]
    public void ReportAgesRefreshFromTheClockWithoutNewDataMouseMovementOrAnimations()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var intel = new IntelMapHoverReport { ReportId = "age-intel", TimestampUtc = now.AddSeconds(-59),
            ReporterName = "Scout", MessageText = "", Ships = [], Hostiles = [] };
        var kill = new IntelMapHoverKillmail { ReportId = "age-kill", TimestampUtc = now.AddSeconds(-125),
            MessageText = "", KillmailUrl = "https://zkillboard.com/kill/age/", VictimName = "Pilot",
            VictimMembership = "", VictimShipDisplayName = "Ares", Attackers = [], IskLostLabel = "24m ISK" };
        var map = new MapControl { ReportOverlayUtcNow = () => now, SuspendAnimations = true,
            Graph = new MapGraph { Nodes = [Node], Links = [] }, ViewMode = MapViewMode.Region, SelectedNodeId = 1,
            IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [intel] },
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [kill] } };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = map };
        try
        {
            window.Show();
            ArrangeForReports(map);
            using (Paint(map)) { }
            Assert.True(map.IsReportAgeTimerRunning);
            var first = map.BuildNodeOverlay(Node, 784);
            Assert.Contains(first.Elements, e => e.Text == "59s ago");
            Assert.Contains(first.Elements, e => e.Text == "2m 05s ago");
            Assert.Same(first, map.BuildNodeOverlay(Node, 784));
            var bounds = Assert.Single(map.VisibleReportOverlays).Bounds;
            map.UpdateReportOverlayHover(bounds.Center);
            now = now.AddSeconds(1);
            Assert.True(map.RefreshReportOverlayAges());
            Assert.False(map.RefreshReportOverlayAges());
            using (Paint(map)) { }
            var next = map.BuildNodeOverlay(Node, 784);
            Assert.NotSame(first, next);
            Assert.Contains(next.Elements, e => e.Text == "1m 00s ago");
            Assert.Contains(next.Elements, e => e.Text == "2m 06s ago");
            Assert.Equal(bounds, Assert.Single(map.VisibleReportOverlays).Bounds);
            map.SelectedNodeId = null;
            using (Paint(map)) { }
            Assert.False(map.IsReportAgeTimerRunning);
            map.SelectedNodeId = 1;
            using (Paint(map)) { }
            Assert.True(map.IsReportAgeTimerRunning);
        }
        finally { window.Close(); }
        Assert.False(map.IsReportAgeTimerRunning);
    }

    [AvaloniaFact]
    public void VictimAttackerAndIntelShipIconsUseTheirResolvedNamesForTooltips()
    {
        var map = new MapControl
        {
            IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [new()
            {
                ReportId = "intel-names", TimestampUtc = DateTime.UtcNow, ReporterName = "Scout", MessageText = "report",
                Ships = [new() { ShipDisplayName = "Osprey Navy Issue", ShipTypeId = 114, ShipIconKey = "cruiser", Count = 3 }],
                Hostiles = [new() { Name = "Pilot", ShipTypeId = 111, ShipDisplayName = "Ares" }]
            }] },
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [new()
            {
                ReportId = "kill-names", TimestampUtc = DateTime.UtcNow, MessageText = "report", KillmailUrl = "https://zkillboard.com/kill/1/",
                VictimName = "Victim", VictimMembership = "", VictimShipTypeId = 112, VictimShipDisplayName = "Huginn",
                Attackers = [new() { Name = "Attacker", ShipTypeId = 113, ShipDisplayName = "Claw" }], IskLostLabel = "24m ISK"
            }] }
        };
        var tooltips = map.BuildNodeOverlay(Node, 360).Elements.Select(e => e.Tooltip).ToArray();
        foreach (var name in new[] { "Ares", "Huginn", "Claw", "Osprey Navy Issue" }) Assert.Contains(name, tooltips);
        Assert.DoesNotContain(tooltips, t => t?.StartsWith("Type ") == true);
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void OverlayClipsKeepLaterRowsAndFollowingDrawingAtTheSameDpi(double scaling)
    {
        var map = CreateMap(withLinks: false);
        map.Graph = new MapGraph { Nodes = [Node], Links = [] };
        map.ViewMode = MapViewMode.Region;
        map.SelectedNodeId = 1;
        ArrangeForReports(map);
        var layout = map.BuildNodeOverlay(Node, 784);
        var rect = MapOverlayLayout.Place(map.WorldToScreen(new Point(Node.X, Node.Y)),
            new Size(layout.Size.Width, Math.Min(480, layout.Size.Height)), map.Bounds.Size);
        var loss = layout.Elements.First(e => e.Role == "loss-accent").Bounds.Translate(new Vector(rect.X, rect.Y));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(800 * scaling), (int)(600 * scaling)), new Vector(96 * scaling, 96 * scaling));
        using (var context = bitmap.CreateDrawingContext(true))
        {
            map.Render(context);
            map.DrawStaticScene(context, new Rect(map.Bounds.Size));
                map.DrawReportOverlays(context);
            context.FillRectangle(Brushes.Magenta, new Rect(730, 540, 8, 8));
        }
        using var pixels = DecodeBitmap(bitmap);
        var accent = pixels.GetPixel((int)(loss.Center.X * scaling), (int)(loss.Center.Y * scaling));
        Assert.True(accent.Red > 150 && accent.Green < 150 && accent.Blue < 150,
            $"Loss accent moved after the message clip at {scaling}x: {accent}");
        var marker = pixels.GetPixel((int)(734 * scaling), (int)(544 * scaling));
        Assert.True(marker.Red > 220 && marker.Blue > 220 && marker.Green < 30,
            $"Overlay clip changed the following drawing transform at {scaling}x: {marker}");
    }

    [AvaloniaFact]
    public void KillmailIdentifiesLossAndUsesTheCompleteAttackerCount()
    {
        var kill = new IntelMapHoverKillmail
        {
            ReportId = "loss", TimestampUtc = DateTime.UtcNow, AttackerCount = 31,
            MessageText = "Killmail: Osprey destroyed (31 attackers, 24000000 ISK)",
            KillmailUrl = "https://zkillboard.com/kill/1/", IskLostLabel = "ISK Lost: 24m",
            VictimShipDisplayName = "Osprey", VictimShipTypeId = 111, VictimName = "Victim",
            VictimCharacterId = 100, VictimCorporationId = 200, VictimAllianceId = 300,
            VictimMembership = "[CORP] [ALLY]",
            Attackers = [new IntelMapHoverHostile { Name = "Attacker", ShipTypeId = 222, CorporationId = 201,
                AllianceId = 301, CharacterId = 101 }], HiddenAttackerCount = 7
        };
        var map = new MapControl { ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [kill] } };
        var layout = map.BuildNodeOverlay(Node, 360);
        Assert.Contains(layout.Elements, e => e.Text == "Killmail · 31 involved");
        Assert.Contains(layout.Elements, e => e.Text == "Lost · Osprey | 24m ISK");
        Assert.DoesNotContain(layout.Elements, e => e.Text == "Involved");
        Assert.DoesNotContain(layout.Elements, e => e.Text == kill.MessageText);
        Assert.Contains(layout.Elements, e => e.Role == "loss-accent");
        var icons = layout.Elements.Where(e => e.Role is "ship" or "corporation" or "alliance" or "portrait").ToArray();
        Assert.Equal(new[] { "ship", "corporation", "alliance", "portrait", "ship", "corporation", "alliance", "portrait" }, icons.Select(e => e.Role));
        for (var i = 1; i < 4; i++) Assert.True(icons[i].Bounds.Left > icons[i - 1].Bounds.Left);
        Assert.True(layout.Elements.Single(e => e.Text == "Victim").Bounds.Left > icons[3].Bounds.Right);
    }

    [AvaloniaFact]
    public void MixedHistoryIsNewestFirstAndReducedCardsExcludeUnrelatedFields()
    {
        var map = new MapControl
        {
            InfoBoxShowRegion = true, InfoBoxShowSecurityStatus = true, InfoBoxShowSovUpgradeIcon = true,
            InfoBoxShowSystemJumps = true,
            IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [Report("old", -3), Report("new", -1)] },
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [Kill("middle", -2)] }
        };
        var node = new MapNode { Id = 1, Name = Node.Name, X = Node.X, Y = Node.Y, Security = -0.2,
            RegionName = "Wicked Creek", SystemJumps = 500,
            SovUpgrades = [new SovUpgradeEntry { UpgradeName = "Exploration Detector", Tier = 1 }] };
        var full = map.BuildNodeOverlay(node, 360);
        Assert.Equal(new[] { "new", "middle", "old" }, full.Reports.OrderBy(p => p.Value.Top).Select(p => p.Key));
        Assert.Contains(full.Elements, e => e.Text?.StartsWith("Exploration Detector") == true);
        var reduced = map.BuildNodeOverlay(node, 360, new HashSet<string> { "new" });
        Assert.Equal("new", Assert.Single(reduced.Reports).Key);
        Assert.Contains(reduced.Elements, e => e.Text == "Wicked Creek");
        Assert.DoesNotContain(reduced.Elements, e => e.Text?.StartsWith("Exploration Detector") == true || e.Tooltip == "Jumps: 500");
        Assert.True(full.Elements.Count(e => e.Role == "separator") >= 3);
        Assert.InRange(full.Size.Width, 180, 300);
        Assert.InRange(reduced.Size.Width, 180, 300);
    }

    [AvaloniaFact]
    public void NewReportsPreserveTheVisibleHistoryAndVerticalThumbCanBeDragged()
    {
        var map = HistoryMap(8);
        using (Paint(map)) { }
        var oldLayout = map.BuildNodeOverlay(Node, 784);
        var rect = MapOverlayLayout.Place(map.WorldToScreen(new Point(Node.X, Node.Y)),
            new Size(oldLayout.Size.Width, Math.Min(480, oldLayout.Size.Height)), map.Bounds.Size);
        Assert.True(map.ScrollNodeOverlay(rect.Center, -7));
        using (Paint(map)) { }
        var before = FindLink(map, "https://zkillboard.com/kill/k3/");
        Assert.NotNull(before);
        var history = map.ZkillRecentReportsByNodeId![1].ToList();
        history.Add(Kill("new-arrival", 1));
        map.ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = history };
        using (Paint(map)) { }
        Assert.Equal(before, FindLink(map, "https://zkillboard.com/kill/k3/"));
        Assert.True(map.BeginOverlayDrag(new Point(rect.Right - 4, rect.Top + 8)));
        Assert.True(map.DragOverlayScroll(new Point(rect.Right - 4, rect.Bottom)));
        Assert.True(map.EndOverlayDrag(new Point(rect.Right - 4, rect.Bottom)));
        using (Paint(map)) { }
        Assert.NotNull(FindLink(map, "https://zkillboard.com/kill/k7/"));
        Assert.Null(FindLink(map, "https://zkillboard.com/kill/new-arrival/"));
    }

    [AvaloniaFact]
    public void MessageWheelAndHorizontalThumbScrollWithoutChangingSelection()
    {
        var map = new MapControl { Graph = new MapGraph { Nodes = [Node], Links = [] }, ViewMode = MapViewMode.Region,
            SelectedNodeId = 1, IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [Report("message", 0)] } };
        ArrangeForReports(map);
        var layout = map.BuildNodeOverlay(Node, 784);
        var rect = MapOverlayLayout.Place(map.WorldToScreen(new Point(Node.X, Node.Y)), layout.Size, map.Bounds.Size);
        var message = layout.Elements.Single(e => e.MarqueeText is not null).Bounds.Translate(new Vector(rect.X, rect.Y));
        map.UpdateReportOverlayHover(message.Center);
        using var initial = Paint(map);
        Assert.True(map.HandleOverlayWheel(message.Center, 0, -2, true));
        using var wheeled = Paint(map);
        using var first = DecodeBitmap(initial);
        using var second = DecodeBitmap(wheeled);
        Assert.False(first.Bytes.SequenceEqual(second.Bytes));
        Assert.True(map.BeginOverlayDrag(new Point(message.Right - 2, message.Bottom - 2)));
        Assert.True(map.DragOverlayScroll(new Point(message.Left + 2, message.Bottom - 2)));
        Assert.True(map.EndOverlayDrag(message.Center));
        Assert.Equal(1, map.SelectedNodeId);
    }

    [AvaloniaFact]
    public void AutomaticPopupsShowEachSystemDoNotSelectAndCanBeDisabledOrCleared()
    {
        var second = new MapNode { Id = 2, Name = "SHBF-V", X = 0.1, Y = 0.1 };
        var map = new MapControl { Graph = new MapGraph { Nodes = [Node, second], Links = [] }, ViewMode = MapViewMode.Region,
            ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [1] = [Kill("one", 0)], [2] = [Kill("two", 0)] } };
        ArrangeForReports(map);
        map.ReportPopupRequests = [new("one", Node.Name, DateTimeOffset.UtcNow), new("two", second.Name, DateTimeOffset.UtcNow)];
        using (Paint(map)) { }
        Assert.NotNull(FindLink(map, "https://zkillboard.com/kill/one/"));
        Assert.NotNull(FindLink(map, "https://zkillboard.com/kill/two/"));
        Assert.Null(map.SelectedNodeId);
        map.ReportPopupsEnabled = false;
        using (Paint(map)) { }
        Assert.Null(FindLink(map, "https://zkillboard.com/kill/one/"));
        map.ReportPopupsEnabled = true;
        using (Paint(map)) { }
        Assert.Null(FindLink(map, "https://zkillboard.com/kill/one/"));
        map.SelectedNodeId = 1;
        using (Paint(map)) { }
        Assert.NotNull(FindLink(map, "https://zkillboard.com/kill/one/"));
        map.ReportPopupRequests = [];
        using (Paint(map)) { }
        Assert.NotNull(FindLink(map, "https://zkillboard.com/kill/one/"));
    }

    [AvaloniaFact]
    public void CoincidentPopupsShareHoverPlacementAndClicksFollowArrivalOrder()
    {
        var nodes = Enumerable.Range(1, 3).Select(i => new MapNode { Id = i, Name = $"System {i}", X = .5, Y = .5 }).ToArray();
        var map = new MapControl { Graph = new MapGraph { Nodes = nodes, Links = [] }, ViewMode = MapViewMode.Region,
            ZkillRecentReportsByNodeId = nodes.ToDictionary(n => n.Id, n => (IReadOnlyList<IntelMapHoverKillmail>)[Kill($"c{n.Id}", -1)]),
            ReportPopupRequests = nodes.Select(n => new MapReportPopupRequest($"c{n.Id}", n.Name, DateTimeOffset.UtcNow)).ToArray() };
        ArrangeForReports(map);
        using (Paint(map)) { }
        var views = map.VisibleReportOverlays.ToArray();
        Assert.Equal(new long[] { 1, 2, 3 }, views.Select(v => v.NodeId));
        Assert.All(views, v => Assert.Equal(views[0].Bounds, v.Bounds));
        var anchor = map.WorldToScreen(new Point(.5, .5));
        Assert.Equal(MapOverlayLayout.Place(anchor, views[0].Bounds.Size, map.Bounds.Size), views[0].Bounds);
        var link = FindLink(map, "https://zkillboard.com/kill/c3/");
        Assert.NotNull(link);
        Assert.Equal("https://zkillboard.com/kill/c3/", map.GetNodeOverlayLink(link.Value));
        Assert.Null(FindLink(map, "https://zkillboard.com/kill/c1/"));
        Assert.True(map.DismissReportPopupAt(MapControl.PopupCloseBounds(views[2].Bounds).Center));
        using (Paint(map)) { }
        Assert.Equal(2, map.VisibleReportOverlays.Count);
        Assert.All(map.VisibleReportOverlays, v => Assert.Equal(views[0].Bounds, v.Bounds));
        Assert.NotNull(FindLink(map, "https://zkillboard.com/kill/c2/"));
    }

    [AvaloniaFact]
    public void RenderHistoryAndAutomaticPopupPreviewsAtRequestedScales()
    {
        var directory = Environment.GetEnvironmentVariable("HISA_MAP_PREVIEW_DIR")
            ?? Path.Combine(Path.GetTempPath(), "Hisa.Rendering.Tests", "map-ui-review");
        Directory.CreateDirectory(directory);
        foreach (var scaling in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (var automatic in new[] { false, true })
        {
            var map = CreateMap(withLinks: false);
            var second = new MapNode { Id = 2, Name = "SHBF-V", X = 0.1, Y = 0.2,
                RegionName = "Insmother", ConstellationName = "QA-P7J", Security = -0.3 };
            map.Graph = new MapGraph { Nodes = [Node, second], Links = [new MapLink { FromId = 1, ToId = 2 }] };
            map.ViewMode = MapViewMode.Region;
            if (automatic)
            {
                map.IntelRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverReport>> { [1] = [Report("new-intel", 0)] };
                map.ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>> { [2] = [Kill("new-kill", 0)] };
                map.ReportPopupRequests = [new("new-intel", Node.Name, DateTimeOffset.UtcNow), new("new-kill", second.Name, DateTimeOffset.UtcNow)];
            }
            else
            {
                var existing = map.ZkillRecentReportsByNodeId![1];
                map.ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>>
                { [1] = existing.Concat(Enumerable.Range(3, 5).Select(i => Kill($"older-{i}", -i))).ToList() };
                map.SelectedNodeId = 1;
            }
            ArrangeForReports(map);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(800 * scaling), (int)(600 * scaling)), new Vector(96 * scaling, 96 * scaling));
            using (var context = bitmap.CreateDrawingContext(true))
            {
                map.Render(context);
                map.DrawStaticScene(context, new Rect(map.Bounds.Size));
                map.DrawReportOverlays(context);
            }
            bitmap.Save(Path.Combine(directory, $"{(automatic ? "automatic" : "history")}-{scaling * 100:0}.png"));
        }
    }

    private static IntelMapHoverReport Report(string id, int minutes) => new()
    {
        ReportId = id, TimestampUtc = DateTime.UtcNow.AddMinutes(minutes), ReporterName = "Scout", HostileCount = 10,
        MessageText = "E-JCUS reported hostiles " + string.Join(" ", Enumerable.Repeat("Naga Falcon Osprey next gate", 6)), Ships = [], Hostiles = []
    };

    private static IntelMapHoverKillmail Kill(string id, int minutes) => new()
    {
        ReportId = id, TimestampUtc = DateTime.UtcNow.AddMinutes(minutes), MessageText = "Redundant killmail footer",
        KillmailUrl = $"https://zkillboard.com/kill/{id}/", VictimName = $"Victim {id}", VictimMembership = "[CORP] [ALLY]",
        VictimShipDisplayName = "Osprey", Attackers = [], AttackerCount = 7, IskLostLabel = "24m ISK"
    };

    private static MapControl HistoryMap(int count)
    {
        var map = new MapControl { Graph = new MapGraph { Nodes = [Node], Links = [] }, ViewMode = MapViewMode.Region,
            SelectedNodeId = 1, ZkillRecentReportsByNodeId = new Dictionary<long, IReadOnlyList<IntelMapHoverKillmail>>
            { [1] = Enumerable.Range(0, count).Select(i => Kill($"k{i}", -i)).ToList() } };
        ArrangeForReports(map);
        return map;
    }

    private static void ArrangeForReports(MapControl map)
    {
        map.Measure(new Size(800, 600));
        map.Arrange(new Rect(0, 0, 800, 600));
    }

    private static RenderTargetBitmap Paint(MapControl map)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(800, 600), new Vector(96, 96));
        using var context = bitmap.CreateDrawingContext(true);
        map.Render(context);
        return bitmap;
    }

    private static SKBitmap DecodeBitmap(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static Point? FindLink(MapControl map, string url)
    {
        for (var y = 0; y < 600; y += 3)
        for (var x = 0; x < 800; x += 3)
            if (map.GetNodeOverlayLink(new Point(x, y)) == url) return new Point(x, y);
        return null;
    }
}
