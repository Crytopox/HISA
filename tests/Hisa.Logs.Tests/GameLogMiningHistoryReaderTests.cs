using Hisa.Logs.GameLogs;

namespace Hisa.Logs.Tests;

public sealed class GameLogMiningHistoryReaderTests
{
    [Fact]
    public async Task ReadAsync_UsesOnlyRegularYieldInTheSameFileForLegacyResidue()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hisa-mining-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            const string header = "------------------------------------------------------------\nListener: Test Miner\n------------------------------------------------------------\n";
            await File.WriteAllTextAsync(Path.Combine(directory, "20260613_120000_1234.txt"), header +
                """
                [ 2026.06.13 12:00:00 ] (mining) You mined <color=#ff8dc169>100<font size=10> units of <font size=12>Veldspar
                [ 2026.06.13 12:00:01 ] (notify) Phased Prismaticite has Phased-Out and can be mined at 10% efficiency.
                [ 2026.06.13 12:00:02 ] (mining) Critical mining success! You mined an additional <font size=12>25<font size=10> units of <font size=12>Prismaticite
                [ 2026.06.13 12:00:03 ] (mining) Additional <color=#ffff454b>10<font size=10> units depleted from Prismaticite as residue
                [ 2026.06.13 12:00:03 ] (mining) Additional <color=#ffff454b>5<font size=10> units depleted from asteroid as residue
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, "20260613_120004_1234.txt"), header +
                "[ 2026.06.13 12:00:04 ] (mining) Additional <color=#ffff454b>7<font size=10> units depleted from asteroid as residue");

            var snapshots = await GameLogMiningHistoryReader.ReadAsync(
                directory, new DateTime(2026, 6, 13, 12, 0, 3, DateTimeKind.Utc));

            var ores = Assert.Single(snapshots).Value.Ores.ToDictionary(x => x.OreName);
            Assert.Equal(2, ores.Count);
            Assert.Equal(5, ores["Veldspar"].WasteUnits);
            Assert.Equal(10, ores["Prismaticite"].WasteUnits);
            Assert.All(ores.Values, ore =>
            {
                Assert.Equal(0, ore.MinedUnits);
                Assert.Equal(0, ore.BonusUnits);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAsync_SeparatesCriticalBonusFromRegularYield()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hisa-mining-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var filePath = Path.Combine(directory, "20260613_120000_1234.txt");
            await File.WriteAllTextAsync(
                filePath,
                """
                ------------------------------------------------------------
                Listener: Test Miner
                ------------------------------------------------------------
                [ 2026.06.13 12:00:00 ] (mining) <color=0x77ffffff>You mined <font size=12><color=#ff8dc169>100<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Prismaticite
                [ 2026.06.13 12:00:05 ] (mining) <color=#fff0ff45>Critical mining success!<color=0x77ffffff><font size=10> You mined an additional <color=#fff0ff45><font size=12>25<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Prismaticite
                [ 2026.06.13 12:00:06 ] (mining) <color=0x77ffffff>Additional <font size=12><color=#ffff454b>10<color=0x77ffffff><font size=10> units depleted from asteroid as residue
                """ + Environment.NewLine);

            var snapshots = await GameLogMiningHistoryReader.ReadAsync(
                directory,
                new DateTime(2026, 6, 13, 11, 59, 0, DateTimeKind.Utc));

            var snapshot = Assert.Single(snapshots);
            var ore = Assert.Single(snapshot.Value.Ores);

            Assert.Equal(100, ore.MinedUnits);
            Assert.Equal(25, ore.BonusUnits);
            Assert.Equal(10, ore.WasteUnits);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
