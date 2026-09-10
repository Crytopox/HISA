using Hisa.Core.Models;
using Hisa.Rendering;

namespace Hisa.Rendering.Tests;

public sealed class MapOverlapCalculatorTests
{
    [Fact]
    public void ExplorationOverlap_CountsSystemsWithinFiveJumps()
    {
        var graph = new MapGraph
        {
            Nodes =
            [
                Node(1, withDetector: true),
                Node(2),
                Node(3),
                Node(4),
                Node(5),
                Node(6),
                Node(7)
            ],
            Links =
            [
                Link(1, 2),
                Link(2, 3),
                Link(3, 4),
                Link(4, 5),
                Link(5, 6),
                Link(6, 7)
            ]
        };

        var (counts, sources) = MapOverlapCalculator.BuildExplorationDetectorOverlap(graph, null);

        Assert.Single(sources);
        Assert.Contains(1, sources);
        Assert.Equal(1, counts[1]);
        Assert.Equal(1, counts[6]);
        Assert.False(counts.ContainsKey(7));
    }

    [Fact]
    public void ExplorationOverlap_HonorsSelectedFilterKeys()
    {
        var graph = new MapGraph
        {
            Nodes =
            [
                Node(1, withDetector: true, detectorTier: 1),
                Node(2)
            ],
            Links = [Link(1, 2)]
        };

        var (ignoredCounts, ignoredSources) = MapOverlapCalculator.BuildExplorationDetectorOverlap(
            graph,
            ["Cynosural Navigation"]);
        var (counts, sources) = MapOverlapCalculator.BuildExplorationDetectorOverlap(
            graph,
            ["Exploration Detector|1"]);

        Assert.Empty(ignoredSources);
        Assert.Empty(ignoredCounts);
        Assert.Single(sources);
        Assert.Equal(1, counts[2]);
    }

    [Fact]
    public void Cache_ReusesResultUntilGraphOrFilterChanges()
    {
        var cache = new MapOverlapCache();
        var graph = new MapGraph
        {
            Nodes = [Node(1, withDetector: true), Node(2)],
            Links = [Link(1, 2)]
        };

        var first = cache.GetExplorationOverlap(graph, ["Exploration Detector|1"]);
        var second = cache.GetExplorationOverlap(graph, ["Exploration Detector|1"]);
        var third = cache.GetExplorationOverlap(graph, ["Exploration Detector|2"]);

        Assert.Same(first.CountsByNodeId, second.CountsByNodeId);
        Assert.Same(first.SourceNodeIds, second.SourceNodeIds);
        Assert.NotSame(first.CountsByNodeId, third.CountsByNodeId);
        Assert.Empty(third.SourceNodeIds);
    }

    [Fact]
    public void NodeOverlapCounts_CountsDuplicateIds()
    {
        var (counts, sources) = MapOverlapCalculator.BuildNodeOverlapCounts([10, 10, 11], [1, 2]);

        Assert.Equal(2, counts[10]);
        Assert.Equal(1, counts[11]);
        Assert.Equal(2, sources.Count);
    }

    private static MapNode Node(long id, bool withDetector = false, int detectorTier = 1)
    {
        return new MapNode
        {
            Id = id,
            Name = $"S{id}",
            X = id,
            Y = 0,
            SovUpgrades = withDetector
                ? [new SovUpgradeEntry { UpgradeName = "Exploration Detector", Tier = detectorTier }]
                : []
        };
    }

    private static MapLink Link(long from, long to) => new() { FromId = from, ToId = to };
}
