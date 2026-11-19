namespace Hisa.App.Tests;

public sealed class XmppTabStripOverflowTests
{
    [Fact]
    public void ShouldUseOverflow_EntersWhenMinTotalExceedsAvailable()
    {
        Assert.True(XmppTabStripOverflow.ShouldUseOverflow(false, minTotal: 500, available: 400, exitSlack: 20));
        Assert.False(XmppTabStripOverflow.ShouldUseOverflow(false, minTotal: 400, available: 400, exitSlack: 20));
    }

    [Fact]
    public void ShouldUseOverflow_StaysUntilClearSlack()
    {
        // Barely fits at min — without hysteresis we would leave overflow and thrash.
        Assert.True(XmppTabStripOverflow.ShouldUseOverflow(true, minTotal: 400, available: 400, exitSlack: 20));
        Assert.True(XmppTabStripOverflow.ShouldUseOverflow(true, minTotal: 400, available: 419, exitSlack: 20));
        Assert.False(XmppTabStripOverflow.ShouldUseOverflow(true, minTotal: 400, available: 420, exitSlack: 20));
    }

    [Fact]
    public void ShouldUseOverflow_BoundaryDoesNotOscillateAcrossChevronReserve()
    {
        const double minTotal = 480;
        const double slack = 20;
        // Simulate: overflowing with available just under minTotal+slack, then
        // a pass that thinks it fits after hiding the chevron (available unchanged).
        Assert.True(XmppTabStripOverflow.ShouldUseOverflow(true, minTotal, available: 490, slack));
        Assert.True(XmppTabStripOverflow.ShouldUseOverflow(true, minTotal, available: 490, slack));
        Assert.False(XmppTabStripOverflow.ShouldUseOverflow(false, minTotal, available: 490, slack));
    }

    [Fact]
    public void SelectVisibleIndices_FirstFitKeepsPrefixWithinBudget()
    {
        double[] widths = [100, 100, 100, 100];
        var visible = XmppTabStripOverflow.SelectVisibleIndices(widths, spacing: 2, budget: 304, activeIndex: 0);
        Assert.Equal(new[] { 0, 1, 2 }, visible);
    }

    [Fact]
    public void SelectVisibleIndices_AlwaysKeepsActiveEvenIfAtEnd()
    {
        double[] widths = [100, 100, 100, 100];
        var visible = XmppTabStripOverflow.SelectVisibleIndices(widths, spacing: 2, budget: 202, activeIndex: 3);
        Assert.Contains(3, visible);
        Assert.True(visible.Count <= 2);
        Assert.Equal(3, visible[^1]);
    }

    [Fact]
    public void SelectVisibleIndices_ActiveAloneWhenNothingElseFits()
    {
        double[] widths = [100, 100, 100];
        var visible = XmppTabStripOverflow.SelectVisibleIndices(widths, spacing: 2, budget: 50, activeIndex: 1);
        Assert.Equal(new[] { 1 }, visible);
    }

    [Fact]
    public void FitCaptionWidth_BinarySearchesLargestFittingWidth()
    {
        // Each unit of caption above 40 adds 10px to total of 3 tabs: total = 120 + 3*(w-40)
        double Measure(double width) => 120 + 3 * (width - 40);
        var fitted = XmppTabStripOverflow.FitCaptionWidth(Measure, minWidth: 40, maxWidth: 140, available: 300);
        Assert.True(Measure(fitted) <= 300);
        Assert.True(Measure(Math.Min(140, fitted + 1)) > 300 || fitted >= 139);
    }
}
