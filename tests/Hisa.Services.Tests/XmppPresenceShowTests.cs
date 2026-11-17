using System.Xml.Linq;
using Hisa.Core.Models;
using Hisa.Services.Xmpp;

namespace Hisa.Services.Tests;

public sealed class XmppPresenceShowTests
{
    [Theory]
    [InlineData("away", XmppPresenceShow.Away)]
    [InlineData("dnd", XmppPresenceShow.DoNotDisturb)]
    [InlineData("xa", XmppPresenceShow.ExtendedAway)]
    [InlineData("chat", XmppPresenceShow.Chat)]
    [InlineData(null, XmppPresenceShow.Online)]
    [InlineData("", XmppPresenceShow.Online)]
    public void ParseShow_MapsKnownValues(string? show, XmppPresenceShow expected) =>
        Assert.Equal(expected, XmppProtocolSession.ParseShow(show));

    [Fact]
    public void ClientChildValue_ReadsUnnamespacedShow()
    {
        var presence = XElement.Parse("<presence from='a@example.org/res'><show>away</show><status>brb</status></presence>");
        Assert.Equal("away", XmppProtocolSession.ClientChildValue(presence, "show"));
        Assert.Equal("brb", XmppProtocolSession.ClientChildValue(presence, "status"));
        Assert.Equal(XmppPresenceShow.Away, XmppProtocolSession.ParseShow(
            XmppProtocolSession.ClientChildValue(presence, "show")));
    }

    [Fact]
    public void ClientChildValue_ReadsJabberClientNamespacedShow()
    {
        var presence = XElement.Parse(
            "<presence xmlns='jabber:client' from='a@example.org/res'><show>dnd</show></presence>");
        Assert.Equal("dnd", XmppProtocolSession.ClientChildValue(presence, "show"));
        Assert.Equal(XmppPresenceShow.DoNotDisturb, XmppProtocolSession.ParseShow(
            XmppProtocolSession.ClientChildValue(presence, "show")));
    }
}
