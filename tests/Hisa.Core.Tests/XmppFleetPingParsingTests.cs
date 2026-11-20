using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppFleetPingParsingTests
{
    [Fact]
    public void TryParseLineup_PreservesMiningHurfAndSixCommanderChannels()
    {
        const string padding = "\u200D\u200D\u200D\uFEFF\u200D\uFEFF";
        var body = $"""
            Welcome to Sample Mining Weekend!

            Mining Fleets

            FC:{padding} Pilot Alpha
            Comms:{padding} Op 4 https://comms.example.test/op4.html

            FC:{padding} Pilot Bravo
            Comms:{padding} Op 6 https://comms.example.test/op6.html

            FC:{padding} Pilot Charlie
            Comms:{padding} o7 https://comms.example.test/op7.html

            FC:{padding} Pilot Delta
            Comms:{padding} Op 8 https://comms.example.test/op8.html

            FC:{padding} Pilot Echo
            Comms:{padding} Op 9 https://comms.example.test/op9.html

            FC:{padding} Pilot Foxtrot
            Comms:{padding} Op 10 https\://comms.example.test/op10.html
            {padding}
            ~~~ This was a broadcast from sample_poster to all at 2026-10-09 23:56:13.548604 EVE ~~~
            """;
        var lineup = Assert.IsType<XmppFleetLineup>(XmppFleetPingParsing.TryParseLineup(body));
        Assert.Equal("Mining Fleet Lineup", lineup.Title);
        Assert.Equal("Welcome to Sample Mining Weekend!", lineup.Hurf);
        Assert.Equal(new XmppFleetCommander[]
        {
            new("Pilot Alpha", "Op 4", "https://comms.example.test/op4.html"),
            new("Pilot Bravo", "Op 6", "https://comms.example.test/op6.html"),
            new("Pilot Charlie", "o7", "https://comms.example.test/op7.html"),
            new("Pilot Delta", "Op 8", "https://comms.example.test/op8.html"),
            new("Pilot Echo", "Op 9", "https://comms.example.test/op9.html"),
            new("Pilot Foxtrot", "Op 10", "https://comms.example.test/op10.html")
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
            Hostiles in staging ----- form up and prepare to undock soon

            FC Name:{padding} Ada Nova
            Formup Location:{padding} Z-TEST1
            PAP Type:{padding} Strategic
            Comms:{padding} Op 10 https://comms.example.test/op10.html
            Doctrine:{padding} Sample Doctrine (SCN) (Boosters > Sample Cruiser > Logistics > Support)
            Get your ships ready
            {padding}
            ~~~ This was a coord broadcast from sample_coord to all at 2026-11-16 19:11:55.632889 EVE ~~~
            """;

        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParse(body));
        Assert.Equal("Hostiles in staging ----- form up and prepare to undock soon", parsed.Hurf);
        Assert.Equal("Ada Nova", parsed.FcName);
        Assert.Equal("Z-TEST1", parsed.FormupLocation);
        Assert.Equal("Strategic", parsed.PapType);
        Assert.Equal("Op 10", parsed.CommsLabel);
        Assert.Equal("https://comms.example.test/op10.html", parsed.CommsUrl);
        Assert.Equal("Sample Doctrine (SCN) (Boosters > Sample Cruiser > Logistics > Support)", parsed.Doctrine);
        Assert.Equal("Get your ships ready", parsed.Notes);
        Assert.Equal("sample_coord", parsed.BroadcastFrom);
    }

    private const string SampleBody =
        """
        "Hurf"

        FC Name: Test FC
        Formup Location: Q-EX01
        PAP Type: Strategic
        Comms: Mumble https://example.com/comms
        Doctrine: Sample Battleship

        ~~~ This was a skirmishbot broadcast from skirmishbot to examplefleet ~~~
        """;

    [Fact]
    public void TryParse_ReadsStructuredFleetPing()
    {
        var parsed = XmppFleetPingParsing.TryParse(SampleBody);
        Assert.NotNull(parsed);
        Assert.Equal("Hurf", parsed.Hurf);
        Assert.Equal("Test FC", parsed.FcName);
        Assert.Equal("Q-EX01", parsed.FormupLocation);
        Assert.Equal("Strategic", parsed.PapType);
        Assert.Equal("Mumble", parsed.CommsLabel);
        Assert.Equal("https://example.com/comms", parsed.CommsUrl);
        Assert.Equal("Sample Battleship", parsed.Doctrine);
        Assert.Null(parsed.Notes);
        Assert.Equal("skirmishbot", parsed.BroadcastFrom);
    }

    [Fact]
    public void TryParse_ReturnsNullForFreeFormDirectorbotNote()
    {
        Assert.Null(XmppFleetPingParsing.TryParse("Directors: form up when ready."));
        Assert.Null(XmppFleetPingParsing.TryParse(
            "Hello fleet\n\n~~~ This was a skirmishbot broadcast from bot to examplefleet ~~~"));
        Assert.Null(XmppFleetPingParsing.TryParse(
            "Hello fleet\n\n~~~ This was a coord broadcast from bot to examplefleet ~~~"));
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
    [InlineData("Test FC", "Test FC")]
    [InlineData("  Test   FC  ", "Test FC")]
    [InlineData("Test FC (alt)", "Test FC")]
    [InlineData("Test FC / alt", "Test FC")]
    [InlineData("Test FC (main) / alt", "Test FC")]
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
            FC Name: Test FC (skirmish)
            Formup Location: Q-EX01
            PAP Type: Strategic
            ~~~ This was a skirmishbot broadcast from bot to examplefleet ~~~
            """;
        var parsed = XmppFleetPingParsing.TryParse(body);
        Assert.NotNull(parsed);
        Assert.Equal("Test FC", parsed.FcName);
    }

    [Fact]
    public void TryParse_StripsInvisibleUnicodeAndPlainBroadcastFooter()
    {
        const string pad = "\u200B\u200D\u200D\u200D\u200D\u200D\uFEFF\u200D\u200D\u200D\uFEFF\u200D\u200D\u200D\uFEFF\u200D\u200D\u200D\uFEFF\u200D\u200D\u200D\u200D\u200D\uFEFF\u200D\u200D\u200D\u200D\u200D\u200D\u200D\u200D";
        var body =
            $"""
            Hostiles are on grid, form up and get in fleet!
            FC Name:{pad} Ada Nova
            Formup Location:{pad} Z-TEST1
            PAP Type:{pad} Strategic
            Comms:{pad} Op 9 https://comms.example.test/op9.html
            Doctrine:{pad} Sample Doctrine (SCN) (Boosters > Sample Cruiser > Logistics > Support)
            {pad}
            ~~~ This was a broadcast from sample_poster to all at 2026-11-17 18:52:41.744359 EVE ~~~
            """;

        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParse(body));
        Assert.Equal("Hostiles are on grid, form up and get in fleet!", parsed.Hurf);
        Assert.Equal("Ada Nova", parsed.FcName);
        Assert.Equal("Z-TEST1", parsed.FormupLocation);
        Assert.Equal("Strategic", parsed.PapType);
        Assert.Equal("Op 9", parsed.CommsLabel);
        Assert.Equal("https://comms.example.test/op9.html", parsed.CommsUrl);
        Assert.Equal("Sample Doctrine (SCN) (Boosters > Sample Cruiser > Logistics > Support)", parsed.Doctrine);
        Assert.Equal("sample_poster", parsed.BroadcastFrom);
    }

    [Theory]
    [InlineData("\nFC Name\u200D\uFEFF:\u200BAda Nova\n")]
    [InlineData("FC Name:\u200DAda Nova\n")]
    [InlineData(" FC Name:\u200DAda Nova\n")]
    public void TryParse_AcceptsZeroWidthAndMissingBreakVariants(string fcLine)
    {
        var body = "Hostiles are on grid..." + fcLine +
                   "PAP Type: Strategic\n" +
                   "~~~ This was a broadcast from sample_poster to all at 2026-11-17 18:52:41.744359 EVE ~~~";
        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParse(body));
        Assert.Equal("Ada Nova", parsed.FcName);
        Assert.Equal("Hostiles are on grid...", parsed.Hurf);
    }

    [Fact]
    public void TimestampedBroadcastBody_IgnoresCoordinatorPaddingAndLineEndings()
    {
        const string body = """
            Hostiles in staging ----- form up and prepare to undock soon

            FC Name: Ada Nova
            Formup Location: Z-TEST1
            PAP Type: Strategic
            Comms: Op 10 https://comms.example.test/op10.html
            Doctrine: Sample Doctrine (SCN) (Boosters > Sample Cruiser > Logistics > Support)
            Get your ships ready
            ~~~ This was a coord broadcast from sample_coord to all at 2026-11-16 19:11:55.632889 EVE ~~~
            """;
        var unix = body.Replace("\r\n", "\n");
        var padded = unix
            .Replace(": Ada", ":\u200D\uFEFF\u200D Ada")
            .Replace(": Sample", ":\uFEFF\u200D Sample")
            .Replace("\n~~~", "\n\u200D\uFEFF\u200D\n~~~");
        var archive = padded.Replace("\n", "\r\n");
        var liveKey = Assert.IsType<string>(XmppFleetPingParsing.TimestampedBroadcastBody(unix));
        Assert.Equal(liveKey, XmppFleetPingParsing.TimestampedBroadcastBody(padded));
        Assert.Equal(liveKey, XmppFleetPingParsing.TimestampedBroadcastBody(archive));
    }

    [Fact]
    public void TryParseBroadcast_ReadsOptKrabWarning()
    {
        const string pad = "\u200D\u200D\u200D\uFEFF\u200D";
        var body = $"""
            droppers in our space - have 911 pings ready to go
            {pad}
            ~~~ This was a guardbees broadcast from the_llama to opt-krab at 2026-11-18 15:38:23.442083 EVE ~~~
            """;
        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParseBroadcast(body));
        Assert.False(parsed.IsStructured);
        Assert.Equal("opt-krab", parsed.BroadcastTo);
        Assert.Equal("the_llama", parsed.BroadcastFrom);
        Assert.Equal("droppers in our space - have 911 pings ready to go", parsed.Hurf);
        Assert.Null(XmppFleetPingParsing.TryParse(body));
        Assert.Equal("Opt-Krab", XmppFleetPingParsing.FeedTitleForTarget(parsed.BroadcastTo, false));
    }

    [Fact]
    public void TryParse_ReadsLocustCricketFleet()
    {
        const string pad = "\u200D\uFEFF";
        var body = $"""
            Let's go for those rocks, you don't live on air!!

            FC Name:{pad} Maese Fresno
            Formup location:{pad} C-J6MT
            Comms:{pad} Mumble -> Sigs/Squad -> Fleet 1
            Doctrine:{pad}  Exhumers > Barges > Expedition Frigates > Mining Frigates > Porpoise > Rorqual
            {pad}
            ~~~ This was a gice broadcast from gamero to locusts at 2026-11-17 15:35:01.412400 EVE ~~~
            """;
        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParse(body));
        Assert.True(parsed.IsStructured);
        Assert.Equal("locusts", parsed.BroadcastTo);
        Assert.Equal("Maese Fresno", parsed.FcName);
        Assert.Equal("C-J6MT", parsed.FormupLocation);
        Assert.Equal("Mumble -> Sigs/Squad -> Fleet 1", parsed.CommsLabel);
        Assert.Null(parsed.CommsUrl);
        Assert.Contains("Exhumers", parsed.Doctrine);
        Assert.Equal("Locust Fleet", XmppFleetPingParsing.FeedTitleForTarget(parsed.BroadcastTo, true));
    }

    [Fact]
    public void TryParse_ReadsBeehiveFleetWithBeehiveComms()
    {
        const string pad = "\u200D\uFEFF";
        var body = $"""
            If you wanna keep this isk train rolling we need more faxes.

            FC:{pad} Vivien Saken
            Doctrine:{pad} SIR (Faxes > SIRorquals > Hictor > Dictor)

            Beehive Comms - https://gnf.lt/2Qzf8iu.html
            {pad}
            ~~~ This was a guardbees broadcast from miles_deep_uta to beehive at 2026-11-19 23:28:34.480475 EVE ~~~
            """;
        var parsed = Assert.IsType<XmppFleetPing>(XmppFleetPingParsing.TryParse(body));
        Assert.Equal("beehive", parsed.BroadcastTo);
        Assert.Equal("Vivien Saken", parsed.FcName);
        Assert.Contains("SIR", parsed.Doctrine);
        Assert.Equal("https://gnf.lt/2Qzf8iu.html", parsed.CommsUrl);
        Assert.Equal("Beehive Fleet", XmppFleetPingParsing.FeedTitleForTarget(parsed.BroadcastTo, true));
    }

    [Fact]
    public void TryParseBroadcast_ReadsAnnouncementAndOptAll()
    {
        var all = XmppFleetPingParsing.TryParseBroadcast("""
            going to do another quick restart of some core services including jabber in a few minutes
            ~~~ This was a broadcast from boris_agnon to all at 2026-11-18 17:15:56.451186 EVE ~~~
            """);
        Assert.NotNull(all);
        Assert.False(all.IsStructured);
        Assert.Equal("all", all.BroadcastTo);
        Assert.Equal("Announcement", XmppFleetPingParsing.FeedTitleForTarget(all.BroadcastTo, false));

        var optAll = XmppFleetPingParsing.TryParseBroadcast("""
            SumoSwarm day 9 - They can't stop the signal Mal https://twitch.tv/asherelias
            ~~~ This was a broadcast from asher_elias to opt-all at 2026-11-17 07:10:26.449836 EVE ~~~
            """);
        Assert.NotNull(optAll);
        Assert.Equal("opt-all", optAll.BroadcastTo);
        Assert.Equal("Opt-All", XmppFleetPingParsing.FeedTitleForTarget(optAll.BroadcastTo, false));
    }
}
