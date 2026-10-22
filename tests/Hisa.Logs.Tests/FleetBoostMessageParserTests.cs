using Hisa.Core.Models;
using Hisa.Logs.FleetBoosts;

namespace Hisa.Logs.Tests;

public class FleetBoostMessageParserTests
{
    private static string Line(string text) => $"\uFEFF[ 2026.10.21 23:30:02 ] Crytopox Haakario > {text}";
    [Fact]
    public void SuppliedExamplesParsePilotTimestampAndMultipleLinks()
    {
        var single = FleetBoostMessageParser.Parse("\uFEFF[ 2026.10.21 23:28:45 ] Crytopox Haakario > Shield Extension Charge")!;
        Assert.Equal("Crytopox Haakario", single.Pilot);
        Assert.Equal(DateTimeKind.Utc, single.TimestampUtc.Kind);
        Assert.Equal("shield-extension", Assert.Single(single.ChargeIds));
        Assert.Null(single.Mindlink);
        var multiple = FleetBoostMessageParser.Parse(Line("Shield Harmonizing Charge  Shield Extension Charge +ML"))!;
        Assert.Equal(new[] { "shield-harmonizing", "shield-extension" }, multiple.ChargeIds);
        Assert.True(multiple.Mindlink);
    }
    [Fact]
    public void CatalogNamesMatchIgnoringCaseWhitespacePunctuationAndDuplicateLinks()
    {
        foreach (var charge in FleetBoostCatalog.Charges)
        {
            var text = charge.Name.ToUpperInvariant().Replace(" ", " \t ");
            var result = FleetBoostMessageParser.Parse(Line($"({text}), [{text}]"))!;
            Assert.Equal(charge.Id, Assert.Single(result.ChargeIds));
        }
    }
    [Theory]
    [InlineData("ML", true)] [InlineData("+ML", true)] [InlineData("mindlink", true)]
    [InlineData("Mind Link", true)] [InlineData("plus mindlink", true)] [InlineData("+ mind link", true)]
    [InlineData("no ML", false)] [InlineData("-ML", false)] [InlineData("no mindlink", false)]
    [InlineData("without mind link", false)] [InlineData("Shield Extension Charge no ML", false)]
    public void MindlinkDeclarationsAreRecognized(string text, bool expected) =>
        Assert.Equal(expected, FleetBoostMessageParser.Parse(Line(text))!.Mindlink);

    [Theory]
    [InlineData("form up")] [InlineData("HTML")] [InlineData("MLpilot joined")]
    [InlineData("Mining Laser Optimization Charge")] [InlineData("Shield Extension Charges")]
    public void UnrelatedMessagesDoNotProduceReports(string text) => Assert.Null(FleetBoostMessageParser.Parse(Line(text)));
    [Fact]
    public void SystemAndInvalidLinesAreIgnored()
    {
        Assert.Null(FleetBoostMessageParser.Parse("[ 2026.10.21 23:30:02 ] EVE System > ML"));
        Assert.Null(FleetBoostMessageParser.Parse("Shield Extension Charge"));
        Assert.Null(FleetBoostMessageParser.Parse("[ 2026.99.99 23:30:02 ] Pilot > ML"));
    }
}
