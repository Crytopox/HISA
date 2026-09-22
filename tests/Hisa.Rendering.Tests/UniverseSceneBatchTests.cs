using Hisa.Core.Models;
using Hisa.Rendering;

namespace Hisa.Rendering.Tests;

public sealed class UniverseSceneBatchTests
{
    [Fact]
    public void AppendLinks_DedupesUndirectedPairsAndBucketsByRegion()
    {
        var nodes = new Dictionary<long, MapNode>
        {
            [1] = Node(1, constellationId: 10, regionId: 100),
            [2] = Node(2, constellationId: 10, regionId: 100),
            [3] = Node(3, constellationId: 11, regionId: 100),
            [4] = Node(4, constellationId: 20, regionId: 200)
        };
        var links = new List<MapLink>
        {
            Link(1, 2),
            Link(2, 1),
            Link(2, 3),
            Link(3, 4),
            Link(1, 1)
        };
        var sameConstellation = new List<UniverseLinkSegment>();
        var sameRegion = new List<UniverseLinkSegment>();
        var crossRegion = new List<UniverseLinkSegment>();

        UniverseSceneBatch.AppendLinks(links, nodes, sameConstellation, sameRegion, crossRegion);

        Assert.Single(sameConstellation);
        Assert.Single(sameRegion);
        Assert.Single(crossRegion);
        Assert.Equal(UniverseLinkBucket.SameConstellation, UniverseSceneBatch.Classify(nodes[1], nodes[2]));
        Assert.Equal(UniverseLinkBucket.SameRegion, UniverseSceneBatch.Classify(nodes[2], nodes[3]));
        Assert.Equal(UniverseLinkBucket.CrossRegion, UniverseSceneBatch.Classify(nodes[3], nodes[4]));
    }

    [Fact]
    public void ColorBuckets_StayExactUntilThePaletteIsLarge()
    {
        Assert.False(UniverseSceneBatch.ShouldQuantize(UniverseSceneBatch.MaxExactColorBuckets));
        Assert.True(UniverseSceneBatch.ShouldQuantize(UniverseSceneBatch.MaxExactColorBuckets + 1));

        var packed = UniverseSceneBatch.PackColor(16, 32, 48);
        var quantizedA = UniverseSceneBatch.QuantizeColorKey(16, 32, 48);
        var quantizedB = UniverseSceneBatch.QuantizeColorKey(20, 36, 50);
        Assert.NotEqual(packed, quantizedA);
        Assert.Equal(quantizedA, quantizedB);
    }

    [Fact]
    public void RegionCentroids_AverageWorldPositionsPerRegion()
    {
        var nodes = new List<MapNode>
        {
            Node(1, regionId: 7, regionName: "Forge", x: 0, y: 2),
            Node(2, regionId: 7, regionName: "Forge", x: 2, y: 4),
            Node(3, regionId: 8, regionName: "Heimatar", x: 5, y: 5),
            Node(4, regionId: null, regionName: null, x: 9, y: 9)
        };

        var centroids = UniverseSceneBatch.BuildRegionCentroids(nodes);

        Assert.Equal(2, centroids.Count);
        var forge = Assert.Single(centroids, centroid => centroid.RegionId == 7);
        Assert.Equal("Forge", forge.Name);
        Assert.Equal(1, forge.X);
        Assert.Equal(3, forge.Y);
    }

    private static MapNode Node(
        long id,
        int? constellationId = null,
        int? regionId = null,
        string? regionName = "Region",
        double x = 0,
        double y = 0)
    {
        return new MapNode
        {
            Id = id,
            Name = id.ToString(),
            X = x,
            Y = y,
            ConstellationId = constellationId,
            RegionId = regionId,
            RegionName = regionName
        };
    }

    private static MapLink Link(long fromId, long toId) => new() { FromId = fromId, ToId = toId };
}
