using Hisa.Core.Models;
using Hisa.Logs.IntelChatLogs;

namespace Hisa.Logs.Tests;

public sealed class IntelChatMessageParserTests
{
    private static readonly IReadOnlyDictionary<string, long> Systems = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        ["38NZ-1"] = 300001,
        ["DG-8VJ"] = 300002,
        ["J-RXYN"] = 300003,
        ["W-MF6J"] = 300004,
        ["6-L4YC"] = 300005,
        ["5E-CMA"] = 300006,
        ["H6-EYX"] = 300007,
        ["GK5Z-T"] = 300008,
        ["0-6VZ5"] = 300009,
        ["D-P1EH"] = 300010,
        ["RYC-19"] = 300011
    };

    private static readonly IReadOnlyDictionary<string, IntelShipClass> Ships = new Dictionary<string, IntelShipClass>(StringComparer.OrdinalIgnoreCase)
    {
        ["malediction"] = IntelShipClass.Frigate,
        ["hecate"] = IntelShipClass.Destroyer,
        ["loki"] = IntelShipClass.Cruiser,
        ["raven"] = IntelShipClass.Battleship,
        ["nidhoggur"] = IntelShipClass.Capital,
        ["capsule"] = IntelShipClass.Capsule,
        ["astero"] = IntelShipClass.Frigate,
        ["nyx"] = IntelShipClass.Supercapital
    };

    private static IntelChatMessageParser CreateParser() => new(Systems, Ships, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["pod"] = "capsule"
    });

    [Fact]
    public void Parse_ClrReport_WithAsterisk_ResolvesSystemAndClear()
    {
        var parser = CreateParser();
        var result = parser.Parse("DG-8VJ* clr");

        Assert.Contains("DG-8VJ", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.IsClear);
        Assert.Contains(IntelAlertType.Clear, result.Alerts);
    }

    [Fact]
    public void Parse_ClearWordReport_ResolvesSystemAndClear()
    {
        var parser = CreateParser();
        var result = parser.Parse("J-RXYN clear");

        Assert.Contains("J-RXYN", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.IsClear);
        Assert.Contains(IntelAlertType.Clear, result.Alerts);
    }

    [Fact]
    public void Parse_NvReport_ResolvesSystemWithoutClearing()
    {
        var parser = CreateParser();
        var result = parser.Parse("0-6VZ5  Nervous Energy nv");

        Assert.Contains("0-6VZ5", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.False(result.IsClear);
        Assert.DoesNotContain(IntelAlertType.Clear, result.Alerts);
    }

    [Fact]
    public void Parse_CountOnlyReport_ResolvesSystem()
    {
        var parser = CreateParser();
        var result = parser.Parse("GK5Z-T +3");

        Assert.Contains("GK5Z-T", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.False(result.IsClear);
        Assert.Equal(3, result.HostileCount);
    }

    [Fact]
    public void Parse_CarrierReport_DetectsCapitalClass()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH got nidhoggur tackled");

        Assert.False(result.IsClear);
        Assert.Contains(IntelShipClass.Capital, result.ShipClasses);
    }

    [Fact]
    public void Parse_SpikeReport_DetectsSpikeAlert()
    {
        var parser = CreateParser();
        var result = parser.Parse("RYC-19 spike");

        Assert.Contains("RYC-19", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(IntelAlertType.Spike, result.Alerts);
        Assert.False(result.IsClear);
    }

    [Fact]
    public void Parse_ShipNameWithCount_DuplicatesShipClass()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH 2x hecate and loki");

        Assert.Equal(3, result.ShipClasses.Count);
        Assert.Equal(2, result.ShipClasses.Count(x => x == IntelShipClass.Destroyer));
        Assert.Equal(1, result.ShipClasses.Count(x => x == IntelShipClass.Cruiser));
    }

    [Fact]
    public void Parse_PodAliasAndPlural_MatchesCapsule()
    {
        var parser = CreateParser();
        var result = parser.Parse("GK5Z-T both pods");

        Assert.Equal(2, result.ShipClasses.Count);
        Assert.All(result.ShipClasses, shipClass => Assert.Equal(IntelShipClass.Capsule, shipClass));
    }

    [Fact]
    public void Parse_CharacterNames_InfersHostileCount()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH John Smith Jane Doe");

        Assert.Equal(2, result.HostileCount);
    }

    [Fact]
    public void Parse_ExplicitCount_WinsOverNameHeuristics()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH John Smith +5");

        Assert.Equal(5, result.HostileCount);
    }

    [Fact]
    public void Parse_ShipOnlyReport_DoesNotInventPilotNames()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH 2x hecate and loki");

        Assert.Empty(result.HostileNames);
    }

    [Fact]
    public void Parse_BareXCountPattern_IsNotTreatedAsHostileCount()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH x4");

        Assert.Equal(0, result.HostileCount);
        Assert.Equal(0, result.ExplicitHostileCount);
    }

    [Fact]
    public void Parse_ClearDuVariant_IsClear()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH clear du");

        Assert.True(result.IsClear);
        Assert.Equal(0, result.HostileCount);
    }

    [Fact]
    public void Parse_NoVisualAcronyms_AreNotDetectedAsPilotNames()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH nv no visual clear");

        Assert.Empty(result.HostileNames);
    }

    [Fact]
    public void Parse_CommonIntelAcronyms_AreIgnoredAsPilotNames()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH GC spike WT in gate");

        Assert.Empty(result.HostileNames);
    }

    [Theory]
    [InlineData("D-P1EH Askulen Akasa Soikutsu Astero", "Askulen Akasa Soikutsu")]
    [InlineData("D-P1EH Liam Liam Liam", "Liam Liam Liam")]
    [InlineData("D-P1EH shinestar 02 Von-TheAurora1", "shinestar 02")]
    [InlineData("D-P1EH 0314227 Kevin Teiniks", "0314227")]
    public void Parse_NameCandidates_PreserveOneToThreeWordReportedNames(string message, string expectedCandidate)
    {
        var parser = CreateParser();
        var result = parser.Parse(message);

        Assert.Contains(expectedCandidate, result.HostileNameCandidates, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("D-P1EH Astero")]
    [InlineData("D-P1EH Nyx")]
    [InlineData("D-P1EH 2x Astero")]
    public void Parse_KnownShipHulls_AreNotNameCandidates(string message)
    {
        var parser = CreateParser();
        var result = parser.Parse(message);

        Assert.DoesNotContain("Astero", result.HostileNameCandidates, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nyx", result.HostileNameCandidates, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Astero", result.HostileNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nyx", result.HostileNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ThreeWordNameFollowedByShipHull_KeepsNameAndShip()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH Askulen Akasa Soikutsu Astero");

        Assert.Contains("Askulen Akasa Soikutsu", result.HostileNameCandidates, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Astero", result.HostileNameCandidates, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Soikutsu Astero", result.HostileNameCandidates, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Astero", result.ShipNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WormholeSignatureAndBareXCount_DoNotCreateHostileCount()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH C1-C3 WH x4 50% mass");

        Assert.Equal(0, result.HostileCount);
        Assert.DoesNotContain("C1-C3", result.HostileNameCandidates, StringComparer.OrdinalIgnoreCase);
    }
}
