using Hisa.Core;
using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class JumpRangeOverlayCalculatorTests
{
    [Fact]
    public void Build_MarksLowSecSystemsInsideRange()
    {
        var origin = new JumpRangeOriginSettings
        {
            SolarSystemId = 1,
            SolarSystemName = "Home",
            PositionX = 0,
            PositionY = 0,
            PositionZ = 0,
            RangeLy = 5
        };
        var nodes = new[]
        {
            Node(1, 0, 0, 0, 0.0),
            Node(2, 2 * 9_460_000_000_000_000.0, 0, 0, 0.0),
            Node(3, 20 * 9_460_000_000_000_000.0, 0, 0, 0.0),
            Node(4, 2 * 9_460_000_000_000_000.0, 0, 0, 0.8)
        };

        var result = JumpRangeOverlayCalculator.Build(nodes, new Dictionary<long, JumpRangeOriginSettings>
        {
            [1] = origin
        });

        Assert.Contains(1, result.InRangeNodeIds);
        Assert.Contains(2, result.InRangeNodeIds);
        Assert.DoesNotContain(3, result.InRangeNodeIds);
        Assert.DoesNotContain(4, result.InRangeNodeIds);
        Assert.True(result.DistancesByNodeId[2][0].IsInRange);
    }

    private static MapNode Node(long id, double x, double y, double z, double security)
    {
        return new MapNode
        {
            Id = id,
            Name = $"S{id}",
            X = 0,
            Y = 0,
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            Security = security
        };
    }
}
