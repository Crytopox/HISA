using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppFeedAgeTests
{
    [Theory]
    [InlineData(10, "just now")]
    [InlineData(44, "just now")]
    [InlineData(90, "2m ago")]
    [InlineData(5 * 60, "5m ago")]
    [InlineData(90 * 60, "2h ago")]
    [InlineData(5 * 3600, "5h ago")]
    [InlineData(36 * 3600, "2d ago")]
    [InlineData(3 * 86400, "3d ago")]
    public void Format_UsesCompactRelativeBuckets(int ageSeconds, string expected)
    {
        var now = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        Assert.Equal(expected, XmppFeedAge.Format(now.AddSeconds(-ageSeconds), now));
    }

    [Fact]
    public void Format_ChangesBucketAsTimeAdvances()
    {
        var stamped = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        Assert.Equal("just now", XmppFeedAge.Format(stamped, stamped.AddSeconds(10)));
        Assert.Equal("1m ago", XmppFeedAge.Format(stamped, stamped.AddMinutes(1)));
        Assert.Equal("2h ago", XmppFeedAge.Format(stamped, stamped.AddHours(2)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NormalizeScrollToLatestOnTabSwitch_DefaultsOn(bool? value, bool expected) =>
        Assert.Equal(expected, XmppDisplayPreferences.NormalizeScrollToLatestOnTabSwitch(value));

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NormalizeLeaveRoomOnTabClose_DefaultsOn(bool? value, bool expected) =>
        Assert.Equal(expected, XmppDisplayPreferences.NormalizeLeaveRoomOnTabClose(value));
}
