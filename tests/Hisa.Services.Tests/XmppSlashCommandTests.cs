using Hisa.Core.Models;
using Hisa.Data.Database;
using Hisa.Services.Xmpp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class XmppSlashCommandTests
{
    [Theory]
    [InlineData("/topic Line one\nLine two", "Topic", "Line one\nLine two")]
    [InlineData("/TOPIC --clear", "Topic", "--clear")]
    [InlineData("/server-command hello", "Message", "/server-command hello")]
    [InlineData("//topic hello", "Message", "/topic hello")]
    [InlineData(" /me hello", "Message", " /me hello")]
    [InlineData("/me says hello", "Action", "says hello")]
    public void DispatchPreservesTextAndRecognizesOnlySupportedCommands(string input, string action, string arguments)
    {
        var parsed = XmppSlashCommand.Parse(input);
        Assert.Equal(action, parsed.Action.ToString());
        Assert.Equal(arguments, parsed.Arguments);
    }

    [Fact]
    public void RoomTargetsSupportQuotedNicknames()
    {
        Assert.Equal(("Room Guest", "spam"), XmppSlashCommand.SplitTarget("\"Room Guest\" spam"));
        Assert.Throws<ArgumentException>(() => XmppSlashCommand.SplitTarget("\"Room Guest"));
    }

    [Fact]
    public async Task HelpIsLocalAndRoomCommandsAreNotSentToDirectChats()
    {
        using var history = new XmppHistoryStore("Data Source=:memory:");
        using var client = new XmppClientService(new ProbeMemorySettings(), history,
            NullLogger<XmppClientService>.Instance);
        var direct = new XmppConversation(XmppConversationKind.Direct, "friend@example.org");
        var room = new XmppConversation(XmppConversationKind.Room, "room@conference.example.org");
        Assert.Contains("/topic", (await client.SubmitAsync(direct, "/help")).Feedback);
        Assert.Contains("/topic", (await client.SubmitAsync(room, "/help")).Feedback);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitAsync(direct, "/topic test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitAsync(room, "/topic --clear"));
    }
}
