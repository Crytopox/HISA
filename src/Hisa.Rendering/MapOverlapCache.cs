using Hisa.Core.Models;

namespace Hisa.Rendering;

public sealed class MapOverlapCache
{
    private MapGraph? _graph;
    private string? _signature;
    private Dictionary<long, int> _counts = [];
    private HashSet<long> _sources = [];

    public (Dictionary<long, int> CountsByNodeId, HashSet<long> SourceNodeIds) GetExplorationOverlap(
        MapGraph? graph,
        IEnumerable<string>? selectedKeys)
    {
        var signature = SovUpgradeFilters.BuildSignature(selectedKeys);
        if (ReferenceEquals(_graph, graph) && _signature == signature)
        {
            return (_counts, _sources);
        }

        var built = MapOverlapCalculator.BuildExplorationDetectorOverlap(graph, selectedKeys);
        _graph = graph;
        _signature = signature;
        _counts = built.CountsByNodeId;
        _sources = built.SourceNodeIds;
        return built;
    }

    public void Clear()
    {
        _graph = null;
        _signature = null;
        _counts = [];
        _sources = [];
    }
}
