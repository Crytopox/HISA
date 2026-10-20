using Hisa.Core;

namespace Hisa.Core.Tests;

public sealed class MapReportPopupControllerTests
{
    private DateTimeOffset _now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private MapReportPopupRequest Request(string id, string system = "E-JCUS", bool historical = false) => new(id, system, _now, historical);

    [Fact]
    public void KillmailSettingKeepsIntelAndDoesNotReplayFilteredArrivals()
    {
        var controller = new MapReportPopupController(() => _now);
        var intel = Request("intel");
        var kill = new MapReportPopupRequest("kill", "E-JCUS", _now, IsKillmail: true);
        var killOnly = new MapReportPopupRequest("kill-only", "SHBF-V", _now, IsKillmail: true);
        controller.Receive([intel, kill, killOnly], true, 10);
        Assert.Equal(2, controller.Active.Count);
        controller.Receive([intel, kill, killOnly], true, 10, showKillmails: false);
        Assert.Equal("intel", Assert.Single(Assert.Single(controller.Active).ReportIds));
        controller.Receive([intel, kill, killOnly], true, 10, showKillmails: true);
        Assert.Equal("intel", Assert.Single(Assert.Single(controller.Active).ReportIds));
        controller.Receive([new("disabled-kill", "SHBF-V", _now, IsKillmail: true)], true, 10, showKillmails: false);
        Assert.Single(controller.Active);
    }

    [Fact]
    public void DismissalKeepsDedupeAndAllowsOnlyANewReportToReopenTheSystem()
    {
        var controller = new MapReportPopupController(() => _now);
        var first = Request("first");
        var second = Request("second", "SHBF-V");
        controller.Receive([first, second], true, 10);
        Assert.True(controller.Dismiss("e-jcus"));
        Assert.False(controller.Dismiss("e-jcus"));
        controller.Receive([first, second], true, 10);
        Assert.Equal("SHBF-V", Assert.Single(controller.Active).SystemName);
        controller.Receive([Request("new")], true, 10);
        Assert.Equal(2, controller.Active.Count);
    }

    [Fact]
    public void ArrivalSequenceOrdersTiesAndOnlyNewAcceptedArrivalsRaiseTheirSystem()
    {
        var controller = new MapReportPopupController(() => _now);
        var first = Request("first", "Z-System");
        var second = Request("second", "A-System");
        controller.Receive([first, second], true, 10);
        var sequence = controller.Active.ToDictionary(p => p.SystemName, p => p.LatestArrivalSequence);
        Assert.True(sequence["Z-System"] < sequence["A-System"]);
        controller.Receive([first, second, Request("history", historical: true)], true, 60);
        controller.Tick();
        controller.SetHovered("Z-System");
        Assert.All(controller.Active, p => Assert.Equal(sequence[p.SystemName], p.LatestArrivalSequence));
        controller.Receive([Request("delayed-kill", "Z-System")], true, 10);
        Assert.True(controller.Active.Single(p => p.SystemName == "Z-System").LatestArrivalSequence > sequence["A-System"]);
    }

    [Fact]
    public void NewArrivalExpiresAndDuplicateUpdatesNeverReplayIt()
    {
        var controller = new MapReportPopupController(() => _now);
        var request = Request("one");
        controller.Receive([request], true, 10);
        _now += TimeSpan.FromSeconds(6);
        controller.Receive([request], true, 60);
        Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(controller.Active).Remaining);
        _now += TimeSpan.FromSeconds(4);
        Assert.Empty(controller.Active);
        controller.Receive([request], true, 60);
        Assert.Empty(controller.Active);
    }

    [Fact]
    public void NewReportsGroupBySystemRestartDurationAndKeepOtherSystems()
    {
        var controller = new MapReportPopupController(() => _now);
        controller.Receive([Request("one"), Request("two", "SHBF-V")], true, 10);
        _now += TimeSpan.FromSeconds(8);
        controller.Receive([Request("three", "e-jcus")], true, 10);
        Assert.Equal(2, controller.Active.Count);
        Assert.Equal(2, controller.Active.Single(p => p.SystemName == "E-JCUS").ReportIds.Count);
        _now += TimeSpan.FromSeconds(3);
        Assert.Equal("E-JCUS", Assert.Single(controller.Active).SystemName);
    }

    [Fact]
    public void HoverPausesRemainingTimeAndResumeDoesNotResetIt()
    {
        var controller = new MapReportPopupController(() => _now);
        controller.Receive([Request("one")], true, 10);
        _now += TimeSpan.FromSeconds(4);
        controller.SetHovered("E-JCUS");
        _now += TimeSpan.FromMinutes(1);
        Assert.Equal(TimeSpan.FromSeconds(6), Assert.Single(controller.Active).Remaining);
        controller.SetHovered(null);
        _now += TimeSpan.FromSeconds(6);
        Assert.Empty(controller.Active);
    }

    [Fact]
    public void HistoricalDisabledAndAlreadyExpiredRequestsAreNotShown()
    {
        var controller = new MapReportPopupController(() => _now);
        var old = Request("old");
        _now += TimeSpan.FromSeconds(11);
        controller.Receive([old, Request("backfill", historical: true)], true, 10);
        Assert.Empty(controller.Active);
        var disabled = Request("disabled");
        controller.Receive([disabled], false, 10);
        controller.Receive([disabled], true, 10);
        Assert.Empty(controller.Active);
        controller.Receive([Request("live")], true, 10);
        Assert.Single(controller.Active);
        controller.Receive([], false, 10);
        Assert.Empty(controller.Active);
    }

    [Fact]
    public void SameReportCanIdentifySeveralSystemsAndClearDropsAllState()
    {
        var controller = new MapReportPopupController(() => _now);
        controller.Receive([Request("one"), Request("one", "SHBF-V")], true, 10);
        Assert.Equal(2, controller.Active.Count);
        controller.Clear();
        Assert.Empty(controller.Active);
    }

    [Fact]
    public void MessageWaitsScrollsPausesAtEndAndSupportsManualSeek()
    {
        var scroll = new MapMessageScrollState();
        scroll.Advance(TimeSpan.FromSeconds(1.2), 48, false);
        Assert.Equal(0, scroll.Offset);
        scroll.Advance(TimeSpan.FromSeconds(1), 48, false);
        Assert.Equal(24, scroll.Offset);
        scroll.Advance(TimeSpan.FromSeconds(10), 48, true);
        Assert.Equal(24, scroll.Offset);
        scroll.Advance(TimeSpan.FromSeconds(1), 48, false);
        Assert.Equal(48, scroll.Offset);
        scroll.Advance(TimeSpan.FromSeconds(1.2), 48, false);
        Assert.Equal(48, scroll.Offset);
        scroll.Advance(TimeSpan.FromMilliseconds(40), 48, false);
        Assert.Equal(0, scroll.Offset);
        scroll.Seek(500, 48);
        Assert.Equal(48, scroll.Offset);
        scroll.Seek(-10, 48);
        Assert.Equal(0, scroll.Offset);
        scroll.Advance(TimeSpan.FromMinutes(1), 0, false);
        Assert.Equal(0, scroll.Offset);
    }
}
