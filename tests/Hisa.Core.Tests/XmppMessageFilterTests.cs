using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppMessageFilterTests
{
    private static readonly XmppConversation Direct = new(XmppConversationKind.Direct, "bot@example.org");
    private static readonly XmppConversation Room = new(XmppConversationKind.Room, "ops@conference.example.org");

    [Fact]
    public void CollapseSenderKeyIgnoresResourceAndMatchesDmJid()
    {
        var a = Msg(Direct, "bot@example.org/res1", "one");
        var b = Msg(Direct, "bot@example.org/res2", "two");
        Assert.True(XmppMessageFilters.SameCollapseSender(a, b));
        Assert.True(XmppMessageFilters.SenderMatches("bot@example.org", a));
        Assert.Equal("bot@example.org", XmppMessageFilters.SenderKey(a));
    }

    [Fact]
    public void RoomCollapseUsesNick()
    {
        var a = Msg(Room, "ops@conference.example.org/Scout", "one");
        var b = Msg(Room, "ops@conference.example.org/Scout", "two");
        var other = Msg(Room, "ops@conference.example.org/Other", "three");
        Assert.True(XmppMessageFilters.SameCollapseSender(a, b));
        Assert.False(XmppMessageFilters.SameCollapseSender(a, other));
        Assert.True(XmppMessageFilters.SenderMatches("Scout", a));
    }

    [Fact]
    public void FeedTemplateReplacesPlaceholders()
    {
        var message = Msg(Room, "ops@conference.example.org/Scout", "!bping all");
        var text = XmppAlertFeedFormatting.Apply("{sender}: {body} @ {conversation}", message);
        Assert.Equal("Scout: !bping all @ ops@conference.example.org", text);
    }

    private static XmppMessage Msg(XmppConversation conversation, string sender, string body) =>
        new(conversation, sender, body, DateTimeOffset.UtcNow, false, false, false, true, ["k"]);
}
