using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppFleetPingParsingTests
{
    [Fact]
    public void TryParseLineup_PreservesLocustHurfAndSixCommanderChannels()
    {
        const string padding = "\u200D\u200D\u200D\uFEFF\u200D\uFEFF";
        var body = $"""
            Welcome to Locust Friday USTZ!

            Mining Fleets

            FC:{padding} Smeetherson
            Comms:{padding} Op 4 https://gnf.lt/2eMgwE2.html

            FC:{padding} Gamero
            Comms:{padding} Op 6 https://gnf.lt/bO9WiWH.html

            FC:{padding} Eliana Icegirl
            Comms:{padding} o7 https://gnf.lt/EGcAES9.html

            FC:{padding} Wenchy The Destroyer
            Comms:{padding} Op 8 https://gnf.lt/0Yi1Dua.html

            FC:{padding} Tomcat Muutaras
            Comms:{padding} Op 9 https://gnf.lt/vEALwCF.html

            FC:{padding} EternumNoctis
            Comms:{padding} Op 10 https\://gnf.lt/1oh4Y6V.html
            {padding}
            ~~~ This was a broadcast from xiopa_charante to all at 2026-10-09 23:56:13.548604 EVE ~~~
            """;
        var lineup = Assert.IsType<XmppFleetLineup>(XmppFleetPingParsing.TryParseLineup(body));
        Assert.Equal("Locust Weekend Fleets", lineup.Title);
        Assert.Equal("Welcome to Locust Friday USTZ!", lineup.Hurf);
        Assert.Equal(new XmppFleetCommander[]
        {
            new("Smeetherson", "Op 4", "https://gnf.lt/2eMgwE2.html"),
            new("Gamero", "Op 6", "https://gnf.lt/bO9WiWH.html"),
            new("Eliana Icegirl", "o7", "https://gnf.lt/EGcAES9.html"),
            new("Wenchy The Destroyer", "Op 8", "https://gnf.lt/0Yi1Dua.html"),
            new("Tomcat Muutaras", "Op 9", "https://gnf.lt/vEALwCF.html"),
            new("EternumNoctis", "Op 10", "https://gnf.lt/1oh4Y6V.html")
        }, lineup.Commanders);
        Assert.Equal("Fleet Lineup", XmppFleetPingParsing.TryParseLineup(body.Replace("Mining Fleets", ""))?.Title);
        Assert.Null(XmppFleetPingParsing.TryParseLineup(body.Replace("FC:", "Commander:", StringComparison.Ordinal)));
        Assert.Null(XmppFleetPingParsing.TryParseLineup(body.Replace("23:56:13", "99:56:13")));
    }

    [Fact]
    public void TryParse_ReadsCoordinatorFieldsAndNotesWithDirectorbotPadding()
    {
        const string padding = "\u200D\u200D\u200D\uFEFF\u200D\uFEFF\u200D";
        var body = $"""
            Blue Cap Tackeld ----- Get in Fleet and get ready to undock verry soon

            FC Name:{padding} Cobra Kanwari
            Formup Location:{padding} C-J6MT
            PAP Type:{padding} Strategic
            Comms:{padding} Op 10 https://gnf.lt/1oh4Y6V.html
            Doctrine:{padding} Hammer Fleet (FNI) (Boosters > Ferox Navy Issue > Basilisk > Support)
            Get your Ships Ready to Fight
            {padding}
            ~~~ This was a coord broadcast from sapphire_bloodsbane to all at 2026-11-16 19:11:55.632889 EVE ~~~
            """;

        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParse(body));
        Assert.Equal("Blue Cap Tackeld ----- Get in Fleet and get ready to undock verry soon", parsed.Hurf);
        Assert.Equal("Cobra Kanwari", parsed.FcName);
        Assert.Equal("C-J6MT", parsed.FormupLocation);
        Assert.Equal("Strategic", parsed.PapType);
        Assert.Equal("Op 10", parsed.CommsLabel);
        Assert.Equal("https://gnf.lt/1oh4Y6V.html", parsed.CommsUrl);
        Assert.Equal("Hammer Fleet (FNI) (Boosters > Ferox Navy Issue > Basilisk > Support)", parsed.Doctrine);
        Assert.Equal("Get your Ships Ready to Fight", parsed.Notes);
        Assert.Equal("sapphire_bloodsbane", parsed.BroadcastFrom);
    }

    private const string SampleBody =
        """
        "Hurf"

        FC Name: Vile Rat
        Formup Location: 1DQ1-A
        PAP Type: Strategic
        Comms: Mumble https://example.com/comms
        Doctrine: Apocalypse Navy Issue

        ~~~ This was a skirmishbot broadcast from skirmishbot to goonfleet ~~~
        """;

    [Fact]
    public void TryParse_ReadsStructuredFleetPing()
    {
        var parsed = XmppFleetPingParsing.TryParse(SampleBody);
        Assert.NotNull(parsed);
        Assert.Equal("Hurf", parsed.Hurf);
        Assert.Equal("Vile Rat", parsed.FcName);
        Assert.Equal("1DQ1-A", parsed.FormupLocation);
        Assert.Equal("Strategic", parsed.PapType);
        Assert.Equal("Mumble", parsed.CommsLabel);
        Assert.Equal("https://example.com/comms", parsed.CommsUrl);
        Assert.Equal("Apocalypse Navy Issue", parsed.Doctrine);
        Assert.Null(parsed.Notes);
        Assert.Equal("skirmishbot", parsed.BroadcastFrom);
    }

    [Fact]
    public void TryParse_ReturnsNullForFreeFormDirectorbotNote()
    {
        Assert.Null(XmppFleetPingParsing.TryParse("Directors: form up when ready."));
        Assert.Null(XmppFleetPingParsing.TryParse(
            "Hello fleet\n\n~~~ This was a skirmishbot broadcast from bot to goonfleet ~~~"));
        Assert.Null(XmppFleetPingParsing.TryParse(
            "Hello fleet\n\n~~~ This was a coord broadcast from bot to goonfleet ~~~"));
    }

    [Theory]
    [InlineData("Quick Space Job\n\n", "Quick Space Job")]
    [InlineData("", null)]
    [InlineData("\"First paragraph.\n\nMore at https://example.org/info.\"\n\n", "First paragraph.\n\nMore at https://example.org/info.")]
    [InlineData("First paragraph.\n\nMore at https://example.org/info.\n\n", "First paragraph.\n\nMore at https://example.org/info.")]
    public void TryParse_PreservesTheHurfBeforeFleetFields(string subject, string? expected)
    {
        var body = subject + SampleBody[(SampleBody.IndexOf("FC Name:", StringComparison.Ordinal))..];
        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParse(body));
        Assert.Equal(expected, parsed.Hurf);
    }

    [Theory]
    [InlineData("Vile Rat", "Vile Rat")]
    [InlineData("  Vile   Rat  ", "Vile Rat")]
    [InlineData("Vile Rat (alt)", "Vile Rat")]
    [InlineData("Vile Rat / alt", "Vile Rat")]
    [InlineData("Vile Rat (main) / alt", "Vile Rat")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeFcName_CleansLookupNoise(string? input, string? expected) =>
        Assert.Equal(expected, XmppFleetPingParsing.NormalizeFcName(input));

    [Fact]
    public void TryParse_NormalizesFcNameFromField()
    {
        var body =
            """
            "Hurf"
            FC Name: Vile Rat (skirmish)
            Formup Location: 1DQ1-A
            PAP Type: Strategic
            ~~~ This was a skirmishbot broadcast from bot to goonfleet ~~~
            """;
        var parsed = XmppFleetPingParsing.TryParse(body);
        Assert.NotNull(parsed);
        Assert.Equal("Vile Rat", parsed.FcName);
    }
}
