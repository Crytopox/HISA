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
}
