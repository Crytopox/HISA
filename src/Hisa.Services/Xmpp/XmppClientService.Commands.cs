using Hisa.Core.Models;

namespace Hisa.Services.Xmpp;

public sealed partial class XmppClientService
{
    public async Task<XmppSubmissionResult> SubmitAsync(XmppConversation conversation, string text)
    {
        ValidateMessageText(text);
        if (XmppAddresses.Bare(conversation.Jid) != conversation.Jid)
            throw new ArgumentException("The conversation address is invalid.");
        var command = XmppSlashCommand.Parse(text);
        switch (command.Action)
        {
            case XmppSlashAction.Message:
                await SendAsync(conversation, command.Arguments);
                return new();
            case XmppSlashAction.Help:
                RequireNoArguments(command, "/help");
                return new(XmppSlashCommand.HelpText);
            case XmppSlashAction.Action:
                if (command.Arguments.Length == 0) throw new ArgumentException("Usage: /me <action>.");
                await SendAsync(conversation, "/me " + command.Arguments);
                return new();
            case XmppSlashAction.Join:
                return await JoinByCommandAsync(command.Arguments);
            default:
                return await SubmitRoomCommandAsync(conversation, command);
        }
    }

    private async Task<XmppSubmissionResult> JoinByCommandAsync(string arguments)
    {
        var (target, nickname) = XmppSlashCommand.SplitTarget(arguments);
        var jid = target.Contains('/') ? null : XmppAddresses.Bare(target);
        if (jid is null) throw new ArgumentException("Usage: /join <room@service> [nickname].");
        _ = RequireAccount();
        if (!Online) throw new InvalidOperationException("Connect to Jabber before joining a room.");
        var saved = Rooms.FirstOrDefault(room => room.Jid == jid);
        if (JoinedRooms.Contains(jid))
        {
            if (nickname.Length != 0) await ChangeRoomNicknameAsync(jid, nickname);
        }
        else if (nickname.Length == 0 && saved is not null)
            await JoinSavedRoomAsync(jid);
        else
            await SaveRoomAsync(jid, nickname.Length == 0 ? RequireAccount().Jid.Split('@', 2)[0] : nickname,
                saved?.AutoJoin == true, null, saved?.HasPassword == true, join: true);
        return new(OpenConversation: new(XmppConversationKind.Room, jid));
    }

    private async Task<XmppSubmissionResult> SubmitRoomCommandAsync(XmppConversation conversation, XmppSlashCommand command)
    {
        var room = conversation.Jid;
        if (conversation.Kind != XmppConversationKind.Room || !JoinedRooms.Contains(room))
            throw new InvalidOperationException("Join a room before running this command.");

        switch (command.Action)
        {
            case XmppSlashAction.Leave:
                RequireNoArguments(command, "/leave");
                await LeaveRoomAsync(room);
                return new("Left the room.");
            case XmppSlashAction.Nick:
                await ChangeRoomNicknameAsync(room, command.Arguments);
                return new("Room nickname updated.");
            case XmppSlashAction.Topic:
                if (command.Arguments.Length == 0) return new(RoomStatus(room).Topic ?? "The room has no topic.");
                var topic = command.Arguments.Trim() == "--clear" ? "" : command.Arguments;
                await RequestAsync(async protocol => { await protocol.SetTopicAsync(room, topic); return true; });
                return new(topic.Length == 0 ? "Room topic cleared." : "Room topic updated.");
        }

        var (target, reason) = XmppSlashCommand.SplitTarget(command.Arguments);
        switch (command.Action)
        {
            case XmppSlashAction.Invite:
                var jid = target.Contains('/') ? null : XmppAddresses.Bare(target);
                if (jid is null) throw new ArgumentException("Usage: /invite <JID> [reason].");
                await RequestAsync(protocol =>
                {
                    protocol.InviteToRoom(room, jid, reason);
                    return Task.FromResult(true);
                });
                return new("Room invitation submitted.");
            case XmppSlashAction.Kick:
                var nickname = ValidateRoomNickname(target);
                await RequestAsync(async protocol => { await protocol.KickAsync(room, nickname, reason); return true; });
                return new("Room occupant kicked.");
            case XmppSlashAction.Ban:
                if (string.IsNullOrWhiteSpace(target))
                    throw new ArgumentException("Usage: /ban <bare JID or nickname> [reason].");
                await RequestAsync(async protocol => { await protocol.BanAsync(room, target, reason); return true; });
                return new("User banned from the room.");
            default:
                throw new ArgumentException("Unsupported room command. Use /help to see available commands.");
        }
    }

    private Task ChangeRoomNicknameAsync(string room, string nickname)
    {
        var validated = ValidateRoomNickname(nickname);
        return RequestAsync(async protocol => { await protocol.ChangeNicknameAsync(room, validated); return true; });
    }

    private static void RequireNoArguments(XmppSlashCommand command, string name)
    {
        if (command.Arguments.Length != 0) throw new ArgumentException($"Usage: {name}.");
    }
}
