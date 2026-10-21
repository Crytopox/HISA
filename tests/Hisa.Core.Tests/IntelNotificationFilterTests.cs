using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class IntelNotificationFilterTests
{
    [Theory]
    [InlineData("clear", "E-JCUS CLEAR!", true)]
    [InlineData("clr", "E-JCUS clr", true)]
    [InlineData("clr", "clrs", false)]
    [InlineData("clear", "unclear", false)]
    [InlineData("status", "status_update", false)]
    [InlineData("status", "status2", false)]
    [InlineData("status", "éstatus", false)]
    [InlineData("all clear", "E-JCUS ALL \t clear.", true)]
    [InlineData("all clear", "E-JCUS almost all cleared", false)]
    [InlineData("[clear]", "E-JCUS [clear]", true)]
    [InlineData("a+b", "aab", false)]
    [InlineData("a+b", "E-JCUS a+b!", true)]
    [InlineData("clr.*", "clr hostiles", false)]
    [InlineData("clr.*", "clr.*", true)]
    [InlineData("", "clear", false)]
    public void MatchesLiteralWholeWordsAndPhrases(string entry, string message, bool expected)
    {
        Assert.Equal(expected, new IntelNotificationFilter(entry).Suppresses(Report(message)));
    }

    [Fact]
    public void NormalizesLinesAndLeavesApiKillmailsUnaffected()
    {
        var filter = new IntelNotificationFilter(" clear \r\n\nCLR\nclear\n status \n");
        Assert.Equal(new[] { "clear", "CLR", "status" }, filter.Entries);
        Assert.True(filter.Suppresses(Report("E-JCUS status")));
        Assert.False(filter.Suppresses(Report("status", "zKillboard", "api://zkillboard/1")));
        Assert.False(new IntelNotificationFilter(null).Suppresses(Report("clear")));
    }

    private static IntelChatReport Report(string text, string channel = "Intel", string source = "Intel.txt") => new()
    {
        DedupeKey = "test", TimestampUtc = DateTime.UtcNow, ChannelName = channel, ReporterName = "Scout",
        MessageText = text, SourceFilePath = source, Systems = ["E-JCUS"], ShipClasses = [],
        ReportedShipNames = [], ReportedShipTypeIds = [], Alerts = [], ReportedHostileNames = []
    };
}
