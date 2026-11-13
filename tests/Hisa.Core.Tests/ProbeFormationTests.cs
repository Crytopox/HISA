using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class ProbeFormationTests
{
    [Fact]
    public void EstimatedPlacementRecentersEveryProbeAndPreservesSpacing()
    {
        var formation = new ProbeFormation(Guid.NewGuid(), "Perches",
            [new(new(250_000, 200, -10), 1), new(new(500_000, -300, 20), 2), new(new(1_000_000, 100, -40), 3)]);
        var offsets = formation.Probes.Select(p => ProbeFormationMath.EstimatedPlacement(p.Position, formation.Centroid)).ToArray();
        Assert.Equal(-1_000_000d / 3, offsets[0].X, 8);
        Assert.Equal(-250_000d / 3, offsets[1].X, 8);
        Assert.Equal(1_250_000d / 3, offsets[2].X, 8);
        for (var i = 0; i < offsets.Length; i++)
        for (var j = i + 1; j < offsets.Length; j++)
            Assert.InRange((offsets[j] - offsets[i] - (formation.Probes[j].Position - formation.Probes[i].Position)).Length, 0, 1e-8);

        var single = formation with { Probes = [formation.Probes[0]] };
        Assert.Equal(default, ProbeFormationMath.EstimatedPlacement(single.Probes[0].Position, single.Centroid));
        var perch = ProbeFormationMath.CreateSixAxisPerch("Balanced", 500_000);
        Assert.Equal(6, perch.Probes.Count);
        Assert.Equal(default, perch.Centroid);
        Assert.All(perch.Probes, p => Assert.Equal(500_000, p.Position.Length));
    }

    [Fact]
    public void CounterweightBalancesAllAxesAndOnlyMovesTheChosenProbe()
    {
        var formation = ProbeFormationMath.Translate(ProbeFormationMath.Create(), new(1234.125, -5678.25, 9000.5));
        var sparse = formation with { Probes = formation.Probes.Take(3).ToArray() };
        var added = ProbeFormationMath.BalanceWithCounterweight(sparse, null);
        Assert.Equal(sparse.Probes, added.Probes.Take(3));
        Assert.Equal(32 * ProbeFormationMath.AstronomicalUnit, added.Probes[^1].RadiusMeters);
        Assert.InRange(added.Centroid.Length, 0, 1e-8);

        var moved = ProbeFormationMath.BalanceWithCounterweight(formation, 4);
        Assert.InRange(moved.Centroid.Length, 0, 1e-8);
        Assert.Equal(formation.Probes[4].RadiusMeters, moved.Probes[4].RadiusMeters);
        Assert.Equal(formation.Probes.Where((_, i) => i != 4), moved.Probes.Where((_, i) => i != 4));
        Assert.Throws<ArgumentException>(() => ProbeFormationMath.BalanceWithCounterweight(formation, null));
    }

    [Fact]
    public void SubsetTransformsPreserveUnselectedProbesAndSelectedSpacing()
    {
        var f = ProbeFormationMath.Create(); int[] indices = [1,3,5];
        var result = ProbeFormationMath.TransformSelection(f,indices,s => ProbeFormationMath.Translate(s,new(125.5,-300,1000)));
        for (var i = 0; i < 8; i++) {
            if (!indices.Contains(i)) Assert.Equal(f.Probes[i],result.Probes[i]);
            else { Assert.Equal(f.Probes[i].Position + new ProbePosition(125.5,-300,1000),result.Probes[i].Position); Assert.Equal(f.Probes[i].RadiusMeters,result.Probes[i].RadiusMeters); }
        }
        var center = ProbeFormationMath.SelectionCentroid(f,indices);
        var scaled = ProbeFormationMath.TransformSelection(f,indices,s => ProbeFormationMath.Scale(s,2,true));
        Assert.True((center-ProbeFormationMath.SelectionCentroid(scaled,indices)).Length<1e-8);
        Assert.Throws<ArgumentException>(() => ProbeFormationMath.TransformSelection(f,[],s => s));
        Assert.Throws<ArgumentException>(() => ProbeFormationMath.TransformSelection(f,[1,1],s => s));
    }
    [Fact]
    public void RotateUsesRightHandedAxesAndRetainsRadius()
    {
        var f = new ProbeFormation(Guid.NewGuid(), "test", [new(new(1, 0, 0), 123)]);
        var p = Assert.Single(ProbeFormationMath.Rotate(f, ProbeAxis.Y, 90, false).Probes);
        Assert.Equal(0, p.Position.X, 10); Assert.Equal(-1, p.Position.Z, 10); Assert.Equal(123, p.RadiusMeters);
    }
    [Fact]
    public void CenterAndCentroidPivotPreserveGeometry()
    {
        var f = ProbeFormationMath.Translate(ProbeFormationMath.Create(), new(5e8, -3e8, 2e8));
        var centered = ProbeFormationMath.Center(f);
        Assert.True(centered.Centroid.Length < 1e-6);
        var scaled = ProbeFormationMath.Scale(f, .5, true);
        Assert.True((scaled.Centroid - f.Centroid).Length < 1e-6);
        Assert.Equal(f.Probes[0].RadiusMeters, scaled.Probes[0].RadiusMeters);
        Assert.True(((scaled.Probes[0].Position - scaled.Centroid) - (f.Probes[0].Position - f.Centroid) * .5).Length < 1e-6);
    }
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(0)] [InlineData(-1)]
    public void InvalidScalesAreRejected(double factor) => Assert.Throws<ArgumentException>(() => ProbeFormationMath.Scale(ProbeFormationMath.Create(), factor, false));
    [Fact]
    public void InvalidProbeDataAndCountsAreRejected()
    {
        var f = ProbeFormationMath.Create();
        Assert.Throws<ArgumentException>(() => ProbeFormationMath.Validate(f with { Probes = [] }));
        Assert.Throws<ArgumentException>(() => ProbeFormationMath.Validate(f with { Probes = [new(new(double.NaN, 1, 2), 1)] }));
        Assert.Throws<ArgumentException>(() => ProbeFormationMath.Validate(f with { Probes = [new(default, 0)] }));
    }
}
