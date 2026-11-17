using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppLinkSpanTests
{
    [Fact]
    public void SplitKeepsPlainTextAndHttpLinks()
    {
        var spans = XmppLinkSpans.Split("see https://example.com/a. and more");
        Assert.Equal(3, spans.Count);
        Assert.Equal("see ", spans[0].Text);
        Assert.Null(spans[0].Url);
        Assert.Equal("https://example.com/a", spans[1].Text);
        Assert.Equal("example.com", spans[1].Url!.Host);
        Assert.Equal(". and more", spans[2].Text);
    }
}
