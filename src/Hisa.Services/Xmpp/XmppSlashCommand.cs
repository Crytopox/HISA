using Hisa.Core.Models;

namespace Hisa.Services.Xmpp;

public sealed record XmppSubmissionResult(string? Feedback = null, XmppConversation? OpenConversation = null);

internal enum XmppSlashAction { Message, Help, Action, Join, Leave, Nick, Topic, Invite, Kick, Ban }

internal sealed record XmppSlashCommand(XmppSlashAction Action, string Arguments)
{
    public const string HelpText = "/help, /me <action>, /join <room@service> [nickname], /leave, " +
        "/nick <nickname>, /topic [text | --clear], /invite <JID> [reason], /kick <nickname> [reason], " +
        "/ban <JID or nickname> [reason]. Use quotes around a nickname with spaces and // for a literal slash.";

    public static XmppSlashCommand Parse(string text)
    {
        if (!text.StartsWith('/')) return new(XmppSlashAction.Message, text);
        if (text.StartsWith("//", StringComparison.Ordinal)) return new(XmppSlashAction.Message, text[1..]);
        var (name, arguments) = SplitFirst(text[1..]);
        var action = name.ToLowerInvariant() switch
        {
            "help" => XmppSlashAction.Help,
            "me" => XmppSlashAction.Action,
            "join" => XmppSlashAction.Join,
            "leave" => XmppSlashAction.Leave,
            "nick" => XmppSlashAction.Nick,
            "topic" => XmppSlashAction.Topic,
            "invite" => XmppSlashAction.Invite,
            "kick" => XmppSlashAction.Kick,
            "ban" => XmppSlashAction.Ban,
            _ => XmppSlashAction.Message
        };
        return new(action, action == XmppSlashAction.Message ? text : arguments);
    }

    public static (string Target, string Remainder) SplitTarget(string arguments)
    {
        if (!arguments.StartsWith('"')) return SplitFirst(arguments);
        var lastQuote = arguments.IndexOf('"', 1);
        if (lastQuote < 0 || (lastQuote + 1 < arguments.Length && !char.IsWhiteSpace(arguments[lastQuote + 1])))
            throw new ArgumentException("Close the quoted target before adding a reason.");
        return (arguments[1..lastQuote], arguments[(lastQuote + 1)..].TrimStart());
    }

    private static (string First, string Remainder) SplitFirst(string value)
    {
        var index = 0;
        while (index < value.Length && !char.IsWhiteSpace(value[index])) index++;
        return (value[..index], value[index..].TrimStart());
    }
}
