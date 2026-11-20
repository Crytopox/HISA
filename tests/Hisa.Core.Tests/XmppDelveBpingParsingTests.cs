using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class XmppDelveBpingParsingTests
{
    [Fact]
    public void TryParse_ReadsCanonicalMultilineForm()
    {
        const string body = """
            !bping all Rorqual Tackled
            Rorqual Name: Skorhard
            System: SHBF-V
            Location In System (Anomaly or Moon): RHJ-188
            Name of Cyno (On perch, 300km away):Lemmec
            """;
        var parsed = Assert.IsType<XmppDelveBping>(XmppDelveBpingParsing.TryParse(body));
        Assert.Equal("Rorqual Tackled", parsed.Title);
        Assert.Equal("Skorhard", parsed.Pilot);
        Assert.Equal("SHBF-V", parsed.System);
        Assert.Equal("RHJ-188", parsed.Location);
        Assert.Equal("Lemmec", parsed.Cyno);
        Assert.Equal("SHBF-V", parsed.MapSystem);
    }

    [Fact]
    public void TryParse_ReadsGluedSingleLineForm()
    {
        const string body =
            "!bping all Rorqual Tackled  Rorqual Name: Dravan Ragoth  System: KZFV-4  " +
            "Location In System (Anomaly or Moon): Enormous Mercoxit Deposit  " +
            "Name of Cyno(On perch, 300km away):  Shou Tuckerr";
        var parsed = Assert.IsType<XmppDelveBping>(XmppDelveBpingParsing.TryParse(body));
        Assert.Equal("Rorqual Tackled", parsed.Title);
        Assert.Equal("Dravan Ragoth", parsed.Pilot);
        Assert.Equal("KZFV-4", parsed.System);
        Assert.Equal("Enormous Mercoxit Deposit", parsed.Location);
        Assert.Equal("Shou Tuckerr", parsed.Cyno);
    }

    [Fact]
    public void TryParse_ReadsAlternateLabelsAndSemicolons()
    {
        const string body = """
            !bping all    Rorqual tackled
            Rorq Pilot: Mac Awesome
            System:   5-MQQ7
            Location;  Large hezorime
            Cyno:   wardar
            """;
        var parsed = Assert.IsType<XmppDelveBping>(XmppDelveBpingParsing.TryParse(body));
        Assert.Equal("Rorqual tackled", parsed.Title);
        Assert.Equal("Mac Awesome", parsed.Pilot);
        Assert.Equal("5-MQQ7", parsed.System);
        Assert.Equal("Large hezorime", parsed.Location);
        Assert.Equal("wardar", parsed.Cyno);
    }

    [Fact]
    public void TryParse_ReadsCynoPerchAndMultilineSystem()
    {
        const string body = """
            !bping All Rorqual tackled

            Rorqual pilot: Sezahell

            System :
            6BJH-3
            Location : p7m3

            Cyno Perch : Kosinaki Katelo
            """;
        var parsed = Assert.IsType<XmppDelveBping>(XmppDelveBpingParsing.TryParse(body));
        Assert.Equal("Rorqual tackled", parsed.Title);
        Assert.Equal("Sezahell", parsed.Pilot);
        Assert.Equal("6BJH-3", parsed.System);
        Assert.Equal("p7m3", parsed.Location);
        Assert.Equal("Kosinaki Katelo", parsed.Cyno);
    }

    [Fact]
    public void TryParse_ReadsParentheticalSystemAndMissingLocation()
    {
        const string body = """
            !bping all Rorqual Tackled
            Rorqual Name: Umka-TeddyBear
            System: M-MBRT (FDQ-260)
            Name of Cyno: Umka-TeddyBear-Help
            """;
        var parsed = Assert.IsType<XmppDelveBping>(XmppDelveBpingParsing.TryParse(body));
        Assert.Equal("Umka-TeddyBear", parsed.Pilot);
        Assert.Equal("M-MBRT (FDQ-260)", parsed.System);
        Assert.Null(parsed.Location);
        Assert.Equal("Umka-TeddyBear-Help", parsed.Cyno);
        Assert.Equal("M-MBRT", parsed.MapSystem);
    }

    [Fact]
    public void TryParse_AllowsBlankLinesBetweenFields()
    {
        const string body = """
            !bping all Rorqual Tackled
            Rorqual Name: Chiefra
            System:  OAIG-0

             Location In System (Anomaly or Moon): Large Hezorime
            Name of Cyno (On perch, 300km away): Kunay
            """;
        var parsed = Assert.IsType<XmppDelveBping>(XmppDelveBpingParsing.TryParse(body));
        Assert.Equal("Chiefra", parsed.Pilot);
        Assert.Equal("OAIG-0", parsed.System);
        Assert.Equal("Large Hezorime", parsed.Location);
        Assert.Equal("Kunay", parsed.Cyno);
    }

    [Fact]
    public void TryParse_ReturnsNullWithoutBpingPrefix()
    {
        Assert.Null(XmppDelveBpingParsing.TryParse("Rorqual Name: Someone\nSystem: Q-EX01"));
        Assert.Null(XmppDelveBpingParsing.TryParse("!bping fleet stuff"));
        Assert.Null(XmppDelveBpingParsing.TryParse(""));
    }

    [Fact]
    public void PrimarySystemName_StripsParentheticalAndTakesToken()
    {
        Assert.Equal("M-MBRT", XmppDelveBpingParsing.PrimarySystemName("M-MBRT (FDQ-260)"));
        Assert.Equal("SHBF-V", XmppDelveBpingParsing.PrimarySystemName("SHBF-V"));
        Assert.Null(XmppDelveBpingParsing.PrimarySystemName("  "));
    }
}
