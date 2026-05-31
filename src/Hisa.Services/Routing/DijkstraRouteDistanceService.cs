using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.Services.Routing;

public sealed class DijkstraRouteDistanceService : IRouteDistanceService
{
    public RoutingPathResult FindShortestPath(RoutingRequest request)
    {
        if (request.Graph.Nodes.Count == 0 ||
            request.SourceSystemIds.Count == 0 ||
            !request.Graph.Nodes.Any(n => n.Id == request.TargetSystemId))
        {
            return new RoutingPathResult { Found = false };
        }

        var adjacency = BuildAdjacency(
            request.Graph,
            request.IncludeAnsiblexLinks ? request.AnsiblexLinks : [],
            request.CostMode,
            request.AnsiblexCostMultiplier);

        var distances = new Dictionary<long, double>();
        var previous = new Dictionary<long, long>();
        var hops = new Dictionary<long, int>();
        var queue = new PriorityQueue<long, double>();
        var sourceSet = request.SourceSystemIds.ToHashSet();

        foreach (var source in sourceSet)
        {
            if (!adjacency.ContainsKey(source))
            {
                continue;
            }

            distances[source] = 0;
            hops[source] = 0;
            queue.Enqueue(source, 0);
        }

        while (queue.TryDequeue(out var current, out var currentDistance))
        {
            if (!distances.TryGetValue(current, out var knownDistance) || currentDistance > knownDistance)
            {
                continue;
            }

            if (current == request.TargetSystemId)
            {
                break;
            }

            if (!adjacency.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var (next, weight) in neighbors)
            {
                var candidate = knownDistance + weight;
                if (distances.TryGetValue(next, out var existing) && candidate >= existing)
                {
                    continue;
                }

                distances[next] = candidate;
                previous[next] = current;
                var nextHops = hops.TryGetValue(current, out var currentHops) ? currentHops + 1 : 1;
                hops[next] = nextHops;
                queue.Enqueue(next, candidate);
            }
        }

        if (!distances.TryGetValue(request.TargetSystemId, out var totalCost))
        {
            return new RoutingPathResult { Found = false };
        }

        var path = new List<long>();
        var cursor = request.TargetSystemId;
        path.Add(cursor);
        while (!sourceSet.Contains(cursor) && previous.TryGetValue(cursor, out var parent))
        {
            cursor = parent;
            path.Add(cursor);
        }

        path.Reverse();
        return new RoutingPathResult
        {
            Found = true,
            TotalCost = totalCost,
            HopCount = hops.TryGetValue(request.TargetSystemId, out var hopCount) ? hopCount : Math.Max(0, path.Count - 1),
            NodeIds = path
        };
    }

    public IReadOnlyDictionary<long, double> ComputeDistances(RoutingDistancesRequest request)
    {
        if (request.Graph.Nodes.Count == 0 || request.SourceSystemIds.Count == 0)
        {
            return new Dictionary<long, double>();
        }

        var adjacency = BuildAdjacency(
            request.Graph,
            request.IncludeAnsiblexLinks ? request.AnsiblexLinks : [],
            request.CostMode,
            request.AnsiblexCostMultiplier);

        var distances = new Dictionary<long, double>();
        var queue = new PriorityQueue<long, double>();
        foreach (var source in request.SourceSystemIds)
        {
            if (!adjacency.ContainsKey(source))
            {
                continue;
            }

            distances[source] = 0;
            queue.Enqueue(source, 0);
        }

        while (queue.TryDequeue(out var current, out var currentDistance))
        {
            if (!distances.TryGetValue(current, out var knownDistance) || currentDistance > knownDistance)
            {
                continue;
            }

            if (!adjacency.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var (next, weight) in neighbors)
            {
                var candidate = knownDistance + weight;
                if (distances.TryGetValue(next, out var existing) && candidate >= existing)
                {
                    continue;
                }

                distances[next] = candidate;
                queue.Enqueue(next, candidate);
            }
        }

        return distances;
    }

    private static Dictionary<long, List<(long ToId, double Weight)>> BuildAdjacency(
        MapGraph graph,
        IReadOnlyList<AnsiblexLinkEntry> ansiblexLinks,
        RoutingCostMode costMode,
        double ansiblexCostMultiplier)
    {
        var nodeById = graph.Nodes.ToDictionary(n => n.Id);
        var adjacency = nodeById.Keys.ToDictionary(id => id, _ => new List<(long ToId, double Weight)>());

        foreach (var link in graph.Links)
        {
            if (!nodeById.TryGetValue(link.FromId, out var from) ||
                !nodeById.TryGetValue(link.ToId, out var to))
            {
                continue;
            }

            var weight = ComputeWeight(from, to, costMode);
            adjacency[link.FromId].Add((link.ToId, weight));
            adjacency[link.ToId].Add((link.FromId, weight));
        }

        foreach (var ansi in ansiblexLinks)
        {
            var fromId = ansi.FromSolarSystemId;
            var toId = ansi.ToSolarSystemId;
            if (!nodeById.TryGetValue(fromId, out var from) ||
                !nodeById.TryGetValue(toId, out var to))
            {
                continue;
            }

            var baseWeight = ComputeWeight(from, to, costMode);
            var weight = Math.Max(0.0001, baseWeight * Math.Max(0.0001, ansiblexCostMultiplier));
            adjacency[fromId].Add((toId, weight));
            adjacency[toId].Add((fromId, weight));
        }

        return adjacency;
    }

    private static double ComputeWeight(MapNode from, MapNode to, RoutingCostMode mode)
    {
        return mode switch
        {
            RoutingCostMode.HopCount => 1.0,
            RoutingCostMode.Euclidean => ComputeEuclideanDistance(from, to),
            _ => 1.0
        };
    }

    private static double ComputeEuclideanDistance(MapNode from, MapNode to)
    {
        if (from.PositionX is { } fx && from.PositionY is { } fy && from.PositionZ is { } fz &&
            to.PositionX is { } tx && to.PositionY is { } ty && to.PositionZ is { } tz)
        {
            var dx3 = fx - tx;
            var dy3 = fy - ty;
            var dz3 = fz - tz;
            return Math.Max(0.0001, Math.Sqrt((dx3 * dx3) + (dy3 * dy3) + (dz3 * dz3)));
        }

        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Max(0.0001, Math.Sqrt((dx * dx) + (dy * dy)));
    }
}
