using Hisa.Core.Models;

namespace Hisa.Rendering;

public static class MapOverlapCalculator
{
    public const int ExplorationDetectorRange = 5;

    public static (Dictionary<long, int> CountsByNodeId, HashSet<long> SourceNodeIds) BuildExplorationDetectorOverlap(
        MapGraph? graph,
        IEnumerable<string>? selectedKeys)
    {
        if (graph is null || graph.Nodes.Count == 0)
        {
            return ([], []);
        }

        var sourceNodeIds = new HashSet<long>();
        foreach (var node in graph.Nodes)
        {
            if (HasExplorationDetector(node, selectedKeys))
            {
                sourceNodeIds.Add(node.Id);
            }
        }

        if (sourceNodeIds.Count == 0)
        {
            return ([], []);
        }

        var adjacency = new Dictionary<long, List<long>>(graph.Nodes.Count);
        foreach (var node in graph.Nodes)
        {
            adjacency[node.Id] = [];
        }

        foreach (var link in graph.Links)
        {
            if (!adjacency.TryGetValue(link.FromId, out var fromList) ||
                !adjacency.TryGetValue(link.ToId, out var toList))
            {
                continue;
            }

            fromList.Add(link.ToId);
            toList.Add(link.FromId);
        }

        var counts = new Dictionary<long, int>();
        var visited = new HashSet<long>();
        var queue = new Queue<(long NodeId, int Depth)>();
        foreach (var source in sourceNodeIds)
        {
            visited.Clear();
            visited.Add(source);
            queue.Clear();
            queue.Enqueue((source, 0));

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                counts[current] = counts.TryGetValue(current, out var existing) ? existing + 1 : 1;
                if (depth >= ExplorationDetectorRange)
                {
                    continue;
                }

                foreach (var next in adjacency[current])
                {
                    if (!visited.Add(next))
                    {
                        continue;
                    }

                    queue.Enqueue((next, depth + 1));
                }
            }
        }

        return (counts, sourceNodeIds);
    }

    public static (Dictionary<long, int> CountsByNodeId, HashSet<long> SourceNodeIds) BuildNodeOverlapCounts(
        IEnumerable<long>? nodeIds,
        IEnumerable<long>? sourceNodeIds)
    {
        var counts = new Dictionary<long, int>();
        if (nodeIds is not null)
        {
            foreach (var nodeId in nodeIds)
            {
                counts[nodeId] = counts.TryGetValue(nodeId, out var existing) ? existing + 1 : 1;
            }
        }

        var sources = sourceNodeIds is null
            ? []
            : sourceNodeIds.ToHashSet();
        return (counts, sources);
    }

    public static bool HasExplorationDetector(MapNode node, IEnumerable<string>? selectedKeys)
    {
        return SovUpgradeFilters.GetVisible(node.SovUpgrades, selectedKeys)
            .Any(u => u.UpgradeName.Equals(SovUpgradeFilters.ExplorationDetectorName, StringComparison.OrdinalIgnoreCase));
    }
}
