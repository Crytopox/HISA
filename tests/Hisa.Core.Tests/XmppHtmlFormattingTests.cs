using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppHtmlFormattingTests
{
    [Fact]
    public void ParsesBoldItalicSizeAndLink()
    {
        var runs = XmppHtmlFormatting.Parse("<b>Hi</b> <a href=\"https://example.com\">site</a><br/>");
        Assert.NotNull(runs);
        Assert.Contains(runs, run => run.Text == "Hi" && run.Bold);
        Assert.Contains(runs, run => run.Link == "https://example.com" && run.Text == "site");
        Assert.Contains(runs, run => run.Text == "\n");
    }
}
