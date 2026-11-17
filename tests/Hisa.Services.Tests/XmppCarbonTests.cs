using System.Xml.Linq;
using Hisa.Services.Xmpp;

namespace Hisa.Services.Tests;

public sealed class XmppCarbonTests
{
    private const string Account = "me@example.org";

    [Fact]
    public void TryUnwrapCarbon_ReadsReceivedPayload()
    {
        var stanza = XElement.Parse("""
            <message xmlns='jabber:client' from='me@example.org'>
              <received xmlns='urn:xmpp:carbons:2'>
                <forwarded xmlns='urn:xmpp:forward:0'>
                  <message xmlns='jabber:client' from='friend@example.org/phone' to='me@example.org' type='chat'>
                    <body>hello</body>
                  </message>
                </forwarded>
              </received>
            </message>
            """);
        var carbon = XmppProtocolSession.TryUnwrapCarbon(stanza, Account);
        Assert.NotNull(carbon);
        Assert.Equal("hello", carbon!.Value.Message.Element("{jabber:client}body")?.Value);
        Assert.Equal("friend@example.org/phone", (string?)carbon.Value.Message.Attribute("from"));
    }

    [Fact]
    public void TryUnwrapCarbon_ReadsSentPayload()
    {
        var stanza = XElement.Parse("""
            <message xmlns='jabber:client' from='me@example.org'>
              <sent xmlns='urn:xmpp:carbons:2'>
                <forwarded xmlns='urn:xmpp:forward:0'>
                  <message xmlns='jabber:client' from='me@example.org/hisa' to='friend@example.org' type='chat'>
                    <body>sent copy</body>
                  </message>
                </forwarded>
              </sent>
            </message>
            """);
        var carbon = XmppProtocolSession.TryUnwrapCarbon(stanza, Account);
        Assert.NotNull(carbon);
        Assert.Equal("sent copy", carbon!.Value.Message.Elements().First(e => e.Name.LocalName == "body").Value);
    }

    [Fact]
    public void TryUnwrapCarbon_ReturnsNullForNormalChat()
    {
        var stanza = XElement.Parse("<message from='a@example.org' type='chat'><body>hi</body></message>");
        Assert.Null(XmppProtocolSession.TryUnwrapCarbon(stanza, Account));
    }

    [Theory]
    [InlineData(null, "urn:xmpp:carbons:2", "urn:xmpp:forward:0", "jabber:client")]
    [InlineData("other@example.org", "urn:xmpp:carbons:2", "urn:xmpp:forward:0", "jabber:client")]
    [InlineData("example.org", "urn:xmpp:carbons:2", "urn:xmpp:forward:0", "jabber:client")]
    [InlineData("me@example.org/other", "urn:xmpp:carbons:2", "urn:xmpp:forward:0", "jabber:client")]
    [InlineData(Account, "urn:other", "urn:xmpp:forward:0", "jabber:client")]
    [InlineData(Account, "urn:xmpp:carbons:2", "urn:other", "jabber:client")]
    [InlineData(Account, "urn:xmpp:carbons:2", "urn:xmpp:forward:0", "urn:xmpp:forward:0")]
    public void TryUnwrapCarbon_RejectsInvalidSenderOrNamespace(
        string? from, string carbonNamespace, string forwardNamespace, string messageNamespace)
    {
        var stanza = XElement.Parse($"""
            <message xmlns='jabber:client'>
              <received xmlns='{carbonNamespace}'>
                <forwarded xmlns='{forwardNamespace}'>
                  <message xmlns='{messageNamespace}' from='directorbot@example.org'><body>fixture</body></message>
                </forwarded>
              </received>
            </message>
            """);
        stanza.SetAttributeValue("from", from);
        Assert.Null(XmppProtocolSession.TryUnwrapCarbon(stanza, Account));
    }
}
