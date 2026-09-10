using Hisa.Core.Models;
using Hisa.Rendering;

namespace Hisa.Rendering.Tests;

public sealed class VoronoiCacheStoreTests
{
    [Fact]
    public void ComputeKey_IsStableForSameGraphContent()
    {
        var first = VoronoiCacheStore.ComputeKey(CreateGraph());
        var second = VoronoiCacheStore.ComputeKey(CreateGraph());

        Assert.Equal(first, second);
        Assert.False(string.IsNullOrWhiteSpace(first));
    }

    [Fact]
    public void ComputeKey_ChangesWhenANodeMoves()
    {
        var moved = new MapGraph
        {
            Nodes =
            [
                new MapNode { Id = 1, Name = "A", X = 1, Y = 0 },
                new MapNode { Id = 2, Name = "B", X = 0, Y = 2 }
            ],
            Links = [new MapLink { FromId = 1, ToId = 2 }]
        };

        Assert.NotEqual(VoronoiCacheStore.ComputeKey(CreateGraph()), VoronoiCacheStore.ComputeKey(moved));
    }

    [Fact]
    public void MemoryCache_EvictsOldestEntries()
    {
        var store = new VoronoiCacheStore();
        for (var i = 0; i < VoronoiCacheStore.MaxMemoryEntries + 2; i++)
        {
            store.Set($"k{i}", new Dictionary<long, IReadOnlyList<(double X, double Y)>>
            {
                [i] = [(i, i)]
            });
        }

        Assert.Equal(VoronoiCacheStore.MaxMemoryEntries, store.Count);
        Assert.False(store.TryGet("k0", out _));
        Assert.True(store.TryGet("k2", out _));
        Assert.True(store.TryGet($"k{VoronoiCacheStore.MaxMemoryEntries + 1}", out _));
    }

    private static MapGraph CreateGraph()
    {
        return new MapGraph
        {
            Nodes =
            [
                new MapNode { Id = 1, Name = "A", X = 0, Y = 0 },
                new MapNode { Id = 2, Name = "B", X = 0, Y = 1 }
            ],
            Links = [new MapLink { FromId = 1, ToId = 2 }]
        };
    }
}
