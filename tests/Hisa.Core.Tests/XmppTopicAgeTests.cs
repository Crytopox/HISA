using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppTopicAgeTests
{
    [Fact]
    public void Format_IncludesShortAuthorWhenPresent()
    {
        var now = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        var age = XmppTopicAge.Format(now.AddMinutes(-5), "pilot@goonfleet.com/Vile Rat", now);
        Assert.Equal("Updated 5 minutes ago by Vile Rat", age);
    }

    [Fact]
    public void Format_OmitsByClauseWhenAuthorMissing()
    {
        var now = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        var age = XmppTopicAge.Format(now.AddMinutes(-5), null, now);
        Assert.Equal("Updated 5 minutes ago", age);
    }

    [Theory]
    [InlineData("room@conference.goonfleet.com/Nick", "Nick")]
    [InlineData("pilot@goonfleet.com", "pilot")]
    [InlineData("JustNick", "JustNick")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void ShortAuthor_UsesNickOrLocalPart(string? author, string? expected) =>
        Assert.Equal(expected, XmppTopicAge.ShortAuthor(author));
}
