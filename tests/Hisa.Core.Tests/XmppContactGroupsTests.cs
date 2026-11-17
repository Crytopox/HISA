using System.Xml.Linq;
using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppContactGroupsTests
{
    [Fact]
    public void RosterGroups_ReturnsEmpty_WhenGroupsNullOrEmpty()
    {
        Assert.Empty(new XmppContact("a@example.org", "A", true).RosterGroups);
        Assert.Empty(new XmppContact("a@example.org", "A", true, Groups: []).RosterGroups);
    }

    [Fact]
    public void RosterGroups_PreservesConfiguredGroups()
    {
        var contact = new XmppContact("a@example.org", "A", true, Groups: ["Director Team - SkyTeam", "Friends"]);
        Assert.Equal(new[] { "Director Team - SkyTeam", "Friends" }, contact.RosterGroups);
    }

    [Fact]
    public void Workspace_AcceptsCollapsedGroupsAndTopics()
    {
        var workspace = new XmppWorkspace([], null, true,
            CollapsedGroups: ["rooms", "g:Director Team - Finance"],
            CollapsedTopics: ["fleet@conference.example.org"]);
        Assert.Equal(new[] { "rooms", "g:Director Team - Finance" }, workspace.CollapsedGroups);
        Assert.Equal(new[] { "fleet@conference.example.org" }, workspace.CollapsedTopics);
    }

    [Fact]
    public void OccupantOrder_SortsByAffiliationRoleThenNick()
    {
        var sorted = XmppOccupantOrder.Sort(
        [
            new("zeta", null, "none", "participant"),
            new("alpha", null, "owner", "moderator"),
            new("beta", null, "admin", "moderator"),
            new("gamma", null, "none", "moderator"),
            new("delta", null, "member", "participant")
        ]).Select(occupant => occupant.Nick).ToArray();
        Assert.Equal(new[] { "alpha", "beta", "delta", "gamma", "zeta" }, sorted);
    }

    [Fact]
    public void LastSeenFromSeconds_ParsesValidValues()
    {
        var now = DateTimeOffset.Parse("2026-01-15T12:00:00Z");
        Assert.Equal(now.AddSeconds(-90), XmppProfileParsing.LastSeenFromSeconds("90", now));
        Assert.Null(XmppProfileParsing.LastSeenFromSeconds("nope", now));
    }

    [Fact]
    public void ReadVCardPhoto_DecodesBinval()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var vcard = new XElement(XmppProfileParsing.VCard + "vCard",
            new XElement(XmppProfileParsing.VCard + "PHOTO",
                new XElement(XmppProfileParsing.VCard + "TYPE", "image/png"),
                new XElement(XmppProfileParsing.VCard + "BINVAL", Convert.ToBase64String(bytes))));
        var (photo, type) = XmppProfileParsing.ReadVCardPhoto(vcard);
        Assert.Equal(bytes, photo);
        Assert.Equal("image/png", type);
    }
}
