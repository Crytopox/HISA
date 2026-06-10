using Hisa.Core.Models;
using Hisa.Logs.GameLogs;

namespace Hisa.Logs.Tests;

public sealed class GameLogMiningParserTests
{
    [Fact]
    public void TryParseMiningEvent_ParsesNormalYield()
    {
        const string line = "[ 2026.06.09 03:47:46 ] (mining) <color=0x77ffffff>You mined <font size=12><color=#ff8dc169>15<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Prismaticite";

        var parsed = GameLogMiningParser.TryParseMiningEvent(line, out var miningEvent);

        Assert.True(parsed);
        Assert.Equal(MiningLogEventKind.Yield, miningEvent.Kind);
        Assert.Equal(15, miningEvent.Units);
        Assert.Equal("Prismaticite", miningEvent.OreName);
        Assert.False(miningEvent.IsCriticalBonus);
    }

    [Fact]
    public void TryParseMiningEvent_ParsesCriticalYield()
    {
        const string line = "[ 2026.06.09 03:48:11 ] (mining) <color=#fff0ff45>Critical mining success!<color=0x77ffffff><font size=10> You mined an additional <color=#fff0ff45><font size=12>48<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Prismaticite";

        var parsed = GameLogMiningParser.TryParseMiningEvent(line, out var miningEvent);

        Assert.True(parsed);
        Assert.Equal(MiningLogEventKind.Yield, miningEvent.Kind);
        Assert.Equal(48, miningEvent.Units);
        Assert.Equal("Prismaticite", miningEvent.OreName);
        Assert.True(miningEvent.IsCriticalBonus);
    }

    [Fact]
    public void TryParseMiningEvent_ParsesResidue()
    {
        const string line = "[ 2026.06.09 09:41:53 ] (mining) <color=0x77ffffff>Additional <font size=12><color=#ffff454b>154<color=0x77ffffff><font size=10> units depleted from asteroid as residue";

        var parsed = GameLogMiningParser.TryParseMiningEvent(line, out var miningEvent);

        Assert.True(parsed);
        Assert.Equal(MiningLogEventKind.Residue, miningEvent.Kind);
        Assert.Equal(154, miningEvent.Units);
    }

    [Fact]
    public void TryParseMiningEvent_ParsesEfficiencyChange()
    {
        const string line = "[ 2026.06.09 03:49:56 ] (notify) Phased Prismaticite has Phased-Out and can be mined at 10% efficiency.";

        var parsed = GameLogMiningParser.TryParseMiningEvent(line, out var miningEvent);

        Assert.True(parsed);
        Assert.Equal(MiningLogEventKind.SiteEfficiencyChanged, miningEvent.Kind);
        Assert.Equal("Phased Prismaticite", miningEvent.OreName);
        Assert.Equal(10, miningEvent.EfficiencyPercent);
    }
}
