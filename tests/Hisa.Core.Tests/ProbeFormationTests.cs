using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class ProbeFormationTests
{
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
