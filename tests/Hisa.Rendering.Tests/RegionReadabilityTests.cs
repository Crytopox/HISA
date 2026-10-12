using Hisa.Rendering.Controls;

namespace Hisa.Rendering.Tests;

public sealed class RegionReadabilityTests
{
    [Theory]
    [InlineData(0.4, 0.0, 0.55)]
    [InlineData(0.4, 1.0, 0.42)]
    [InlineData(0.9, 1.0, 0.565)]
    [InlineData(1.4, 1.0, 0.71)]
    [InlineData(2.4, 1.0, 1.0)]
    [InlineData(3.0, 1.0, 1.0)]
    public void DenseRegionDotsStartSmallerAndGrowSmoothly(double zoom, double density, double expected)
    {
        Assert.Equal(expected, MapControl.GetRegionNodeZoomScaleAt(zoom, density, 2.4), 6);
    }
}
