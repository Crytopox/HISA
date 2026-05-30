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

    [Fact]
    public void Parse_ClrReport_WithAsterisk_ResolvesSystemAndClear()
    {
        var parser = new IntelChatMessageParser(Systems);
        var result = parser.Parse("DG-8VJ* clr");

        Assert.Contains("DG-8VJ", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.IsClear);
        Assert.Contains(IntelAlertType.Clear, result.Alerts);
    }

    [Fact]
    public void Parse_ClearWordReport_ResolvesSystemAndClear()
    {
        var parser = new IntelChatMessageParser(Systems);
        var result = parser.Parse("J-RXYN clear");

        Assert.Contains("J-RXYN", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.IsClear);
        Assert.Contains(IntelAlertType.Clear, result.Alerts);
    }

    [Fact]
    public void Parse_NvReport_ResolvesSystemAndClear()
    {
        var parser = new IntelChatMessageParser(Systems);
        var result = parser.Parse("0-6VZ5  Nervous Energy nv");

        Assert.Contains("0-6VZ5", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.IsClear);
        Assert.Contains(IntelAlertType.Clear, result.Alerts);
    }

    [Fact]
    public void Parse_CountOnlyReport_ResolvesSystem()
    {
        var parser = new IntelChatMessageParser(Systems);
        var result = parser.Parse("GK5Z-T +3");

        Assert.Contains("GK5Z-T", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.False(result.IsClear);
    }

    [Fact]
    public void Parse_CarrierReport_DetectsCapitalClass()
    {
        var parser = new IntelChatMessageParser(Systems);
        var result = parser.Parse("D-P1EH got carrier tackled");

        Assert.False(result.IsClear);
        Assert.Contains(IntelShipClass.Capital, result.ShipClasses);
    }

    [Fact]
    public void Parse_SpikeReport_DetectsSpikeAlert()
    {
        var parser = new IntelChatMessageParser(Systems);
        var result = parser.Parse("RYC-19 spike");

        Assert.Contains("RYC-19", result.Systems, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(IntelAlertType.Spike, result.Alerts);
        Assert.False(result.IsClear);
    }
}
