using Hisa.Core;

namespace Hisa.Core.Tests;

public sealed class AnsiblexZonesTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(5.1, 2)]
    [InlineData(10, 2)]
    [InlineData(10.1, 3)]
    [InlineData(15, 3)]
    [InlineData(15.1, 4)]
    [InlineData(20, 4)]
    [InlineData(20.1, 5)]
    [InlineData(40, 5)]
    public void ZoneFromDistanceLy_UsesClosedInnerEdges(double distanceLy, int expectedZone)
    {
        Assert.Equal(expectedZone, AnsiblexZones.ZoneFromDistanceLy(distanceLy));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 6)]
    [InlineData(4, 9)]
    [InlineData(5, 15)]
    public void MultiplierForZone_MatchesForceProjectionBands(int zone, int expected)
    {
        Assert.Equal(expected, AnsiblexZones.MultiplierForZone(zone));
    }

    [Fact]
    public void CostTj_UsesDestinationZoneAndLeavesZoneOneFree()
    {
        Assert.Equal(0, AnsiblexZones.CostTj(1, 1));
        Assert.Equal(78, AnsiblexZones.CostTj(13, 3));
        Assert.Equal(162, AnsiblexZones.CostTj(18, 4));
        Assert.Equal(38, AnsiblexZones.CostTj(19, 2));
        Assert.Equal(114, AnsiblexZones.CostTj(19, 3));
    }

    [Fact]
    public void JumpsFromFullCapacitor_FloorsCapacityOverDestinationCost()
    {
        Assert.Equal(6, AnsiblexZones.JumpsFromFullCapacitor(AnsiblexZones.CostTj(13, 5)));
        Assert.Equal(0, AnsiblexZones.JumpsFromFullCapacitor(0));
    }

    [Fact]
    public void DistanceLy_UsesTheSameMeterDivisorAsJumpRange()
    {
        Assert.Equal(9_460_000_000_000_000.0, AnsiblexZones.MetersPerLightYear);
        Assert.Equal(1, AnsiblexZones.DistanceLy(0, 0, 0, AnsiblexZones.MetersPerLightYear, 0, 0), 6);
        Assert.Equal(1, AnsiblexZones.ZoneFor(0, 0, 0, 0, 0, 0).Zone);
    }

    [Fact]
    public void GetShipClassOrDefault_FallsBackToStrategicCruiser()
    {
        Assert.Equal(13, AnsiblexZones.GetShipClassOrDefault("strategic cruiser").BaseCostTj);
        Assert.Equal(AnsiblexZones.DefaultShipClassName, AnsiblexZones.GetShipClassOrDefault("Titan").Name);
        Assert.Equal(19, AnsiblexZones.GetShipClassOrDefault("Rorqual").BaseCostTj);
        Assert.DoesNotContain(AnsiblexZones.ShipClasses, ship => ship.Name.Contains("Titan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlanningSummary_ReportsJumpsIntoTheHighestPaidZone()
    {
        var ship = AnsiblexZones.GetShipClassOrDefault("Strategic Cruiser");
        var summary = AnsiblexZones.BuildPlanningSummary("Jita", ship, 5);

        Assert.Contains("6 jumps", summary, StringComparison.Ordinal);
        Assert.Contains("zone 5", summary, StringComparison.Ordinal);
        Assert.Contains("195 TJ", summary, StringComparison.Ordinal);
        Assert.Contains("1250 TJ", summary, StringComparison.Ordinal);
    }
}
