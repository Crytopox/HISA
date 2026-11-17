using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppAlertMatchingTests
{
    private static readonly XmppConversation Direct = new(XmppConversationKind.Direct, "friend@example.org");
    private static readonly XmppConversation Room = new(XmppConversationKind.Room, "fleet@conference.example.org");

    [Fact]
    public void AnywhereScopeMatchesAnyConversation()
    {
        var rule = Rule(XmppAlertSourceScope.Anywhere, Direct);
        Assert.True(XmppAlertMatching.Matches(rule, Message(Room, "hi")));
        Assert.True(XmppAlertMatching.Matches(rule, Message(Direct, "hi")));
    }

    [Fact]
    public void ContainsEqualsAndRegexMatchBodies()
    {
        Assert.True(XmppAlertMatching.Matches(
            Rule(XmppAlertSourceScope.Conversation, Direct, XmppAlertMatch.ContainsText, "form"),
            Message(Direct, "Form up")));
        Assert.True(XmppAlertMatching.Matches(
            Rule(XmppAlertSourceScope.Conversation, Direct, XmppAlertMatch.EqualsText, "form up"),
            Message(Direct, "Form up")));
        Assert.False(XmppAlertMatching.Matches(
            Rule(XmppAlertSourceScope.Conversation, Direct, XmppAlertMatch.EqualsText, "form"),
            Message(Direct, "Form up")));
        Assert.True(XmppAlertMatching.Matches(
            Rule(XmppAlertSourceScope.Conversation, Direct, XmppAlertMatch.Regex, @"^form\s+up$"),
            Message(Direct, "Form up")));
    }

    [Fact]
    public void SenderFilterMatchesNickOrBareJid()
    {
        var rule = Rule(XmppAlertSourceScope.AnyRoom, Room, sender: "Scout");
        Assert.True(XmppAlertMatching.Matches(rule,
            Message(Room, "hostile", "scout@example.org/Scout")));
        Assert.False(XmppAlertMatching.Matches(rule,
            Message(Room, "hostile", "ally@example.org/Other")));
    }

    [Fact]
    public void DirectorbotRuleMatchesInboundMessagesFromItsConfiguredSource()
    {
        var disabled = Rule(XmppAlertSourceScope.Anywhere, Direct) with { Enabled = false };
        Assert.False(XmppAlertMatching.Matches(disabled, Message(Direct, "hi")));

        var director = XmppAddresses.Directorbot("pilot@goonfleet.com");
        var fleet = Rule(XmppAlertSourceScope.Conversation, director) with { FleetPings = true };
        var broadcast = Message(director, "hi");
        Assert.False(XmppAlertMatching.Matches(fleet, Message(Direct, "hi") with { IsFleetPing = true }));
        Assert.False(XmppAlertMatching.Matches(fleet, broadcast with { Outgoing = true }));
        Assert.True(XmppAlertMatching.Matches(fleet, broadcast));
        Assert.True(XmppAlertMatching.Matches(fleet, broadcast with { IsFleetPing = true }));
    }

    [Fact]
    public void ShouldHighlightMatchesHistoryWithoutStoredFlag()
    {
        var rule = Rule(XmppAlertSourceScope.Conversation, Room, XmppAlertMatch.ContainsText, "!bping all")
            with { Highlight = true };
        var message = Message(Room, "please !bping all now", "room@conference.example.org/Scout");
        Assert.False(message.Highlight);
        Assert.True(XmppAlertMatching.ShouldHighlight([rule], message));
        Assert.False(XmppAlertMatching.ShouldHighlight([rule with { Highlight = false }], message));
        Assert.False(XmppAlertMatching.ShouldHighlight([rule], message with { Outgoing = true }));
    }

    private static XmppAlertRule Rule(XmppAlertSourceScope scope, XmppConversation source,
        XmppAlertMatch match = XmppAlertMatch.AnyMessage, string? text = null, string? sender = null) =>
        new(Guid.NewGuid(), source, match, text, true, null, SourceScope: scope, Sender: sender);

    private static XmppMessage Message(XmppConversation conversation, string body, string? sender = null) =>
        new(conversation, sender ?? conversation.Jid, body, DateTimeOffset.UtcNow, false, false, false, true, ["k"]);
}
