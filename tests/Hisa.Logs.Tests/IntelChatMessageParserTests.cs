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
        ["capsule"] = IntelShipClass.Capsule
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
    public void Parse_NvReport_ResolvesSystemAndClear()
    {
        var parser = CreateParser();
        var result = parser.Parse("0-6VZ5  Nervous Energy nv");

        Assert.Contains("0-6VZ5", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.IsClear);
        Assert.Contains(IntelAlertType.Clear, result.Alerts);
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
    public void Parse_XCountPattern_DetectsHostileCount()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH x4");

        Assert.Equal(4, result.HostileCount);
    }

    [Fact]
    public void Parse_ClearDuVariant_IsClear()
    {
        var parser = CreateParser();
        var result = parser.Parse("D-P1EH clear du");

        Assert.True(result.IsClear);
        Assert.Equal(0, result.HostileCount);
    }
}
