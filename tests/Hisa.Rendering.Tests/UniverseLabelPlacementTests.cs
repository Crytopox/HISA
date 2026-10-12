using Avalonia;
using Hisa.Rendering.Controls;

namespace Hisa.Rendering.Tests;

public sealed class UniverseLabelPlacementTests
{
    [Fact]
    public void ChoosesNonOverlappingPositionAwayFromNodesAndScreenEdges()
    {
        var candidates = new[]
        {
            new UniverseLabelCandidate(1, new Point(125, 50), new Size(26, 16), 5, 2),
            new UniverseLabelCandidate(2, new Point(30, 50), new Size(26, 16), 5, 2)
        };
        var nodes = candidates.Select(c => new UniverseNodeObstacle(c.Center, c.NodeRadius)).ToArray();

        var placed = UniverseLabelPlacement.Place(candidates, nodes, new Rect(0, 0, 150, 100), 10);

        Assert.Equal(2, placed.Count);
        Assert.All(placed, label => Assert.True(label.Bounds.Left >= 2 && label.Bounds.Right <= 148));
        Assert.False(placed[0].Bounds.Intersects(placed[1].Bounds));
        foreach (var label in placed)
        foreach (var node in nodes)
        {
            var radius = node.Radius + 2;
            Assert.False(label.Bounds.Intersects(new Rect(node.Center.X - radius,
                node.Center.Y - radius, radius * 2, radius * 2)));
        }
    }

    [Fact]
    public void HighlightedLabelWinsSpaceIndependentOfInputOrder()
    {
        var candidates = new[]
        {
            new UniverseLabelCandidate(2, new Point(50, 50), new Size(80, 30), 5, 2),
            new UniverseLabelCandidate(1, new Point(50, 50), new Size(80, 30), 5, 0)
        };
        var nodes = new[] { new UniverseNodeObstacle(new Point(50, 50), 5) };

        var placed = UniverseLabelPlacement.Place(candidates, nodes, new Rect(0, 0, 150, 110), 1);

        Assert.Single(placed);
        Assert.Equal(1, placed[0].NodeId);
    }

    [Fact]
    public void LabelsAvoidReservedMapBadges()
    {
        var candidate = new UniverseLabelCandidate(1, new Point(50, 50), new Size(35, 16), 5, 0);
        var reserved = new Rect(60, 54, 40, 22);

        var placed = UniverseLabelPlacement.Place([candidate],
            [new UniverseNodeObstacle(candidate.Center, 5)],
            new Rect(0, 0, 130, 100), 1, [reserved]);

        Assert.Single(placed);
        Assert.False(placed[0].Bounds.Intersects(reserved));
    }

    [Theory]
    [InlineData(0.4, 0.38)]
    [InlineData(1.0, 0.38)]
    [InlineData(2.0, 0.50)]
    [InlineData(5.4, 0.78)]
    [InlineData(12.0, 1.0)]
    [InlineData(30.0, 1.0)]
    public void UniverseNodeSizeIncreasesSmoothlyWithZoom(double zoom, double expected)
    {
        Assert.Equal(expected, MapControl.GetUniverseNodeZoomScaleAt(zoom), 6);
    }
}
