using Hisa.Core;
using Hisa.Core.Models;

namespace Hisa.Core.Tests;

public sealed class LatestMapRequestGateTests
{
    [Fact]
    public async Task RapidCoordinateChangesOnlyCommitFinalGraph()
    {
        var gate = new LatestMapRequestGate();
        (MapCoordinateMode Mode, MapGraph Graph)? displayed = null;

        async Task LoadAsync(MapCoordinateMode mode, Task<MapGraph> pending)
        {
            var request = gate.Begin();
            try
            {
                var graph = await pending;
                if (gate.IsCurrent(request))
                {
                    displayed = (mode, graph);
                }
            }
            finally
            {
                gate.Complete(request);
            }
        }

        var first = new TaskCompletionSource<MapGraph>();
        var second = new TaskCompletionSource<MapGraph>();
        var third = new TaskCompletionSource<MapGraph>();
        var firstLoad = LoadAsync(MapCoordinateMode.ThreeDProjectedXZ, first.Task);
        var secondLoad = LoadAsync(MapCoordinateMode.SdePlanarXY, second.Task);
        var thirdLoad = LoadAsync(MapCoordinateMode.ThreeDProjectedXZ, third.Task);
        var finalGraph = GraphAt(0.8);

        third.SetResult(finalGraph);
        await thirdLoad;
        second.SetResult(GraphAt(0.4));
        first.SetResult(GraphAt(0.2));
        await Task.WhenAll(firstLoad, secondLoad);

        Assert.Equal(MapCoordinateMode.ThreeDProjectedXZ, displayed?.Mode);
        Assert.Same(finalGraph, displayed?.Graph);
    }

    private static MapGraph GraphAt(double x) => new()
    {
        Nodes = [new MapNode { Id = 1, Name = "Test", X = x, Y = 0.5 }],
        Links = []
    };
}
