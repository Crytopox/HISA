using Hisa.Core.Models;

namespace Hisa.Core;

public sealed class JumpRangeOverlayResult
{
    public required List<long> InRangeNodeIds { get; init; }
    public required Dictionary<long, List<long>> MembershipByNodeId { get; init; }
    public required Dictionary<long, List<JumpRangeDistanceDisplay>> DistancesByNodeId { get; init; }
}

public static class JumpRangeOverlayCalculator
{
    private const double MetersPerLightYear = 9_460_000_000_000_000.0;

    public static JumpRangeOverlayResult Build(
        IReadOnlyList<MapNode> nodes,
        IReadOnlyDictionary<long, JumpRangeOriginSettings> origins)
    {
        var inRange = new List<long>();
        var membership = new Dictionary<long, List<long>>();
        var distances = new Dictionary<long, List<JumpRangeDistanceDisplay>>();
        if (nodes.Count == 0 || origins.Count == 0)
        {
            return new JumpRangeOverlayResult
            {
                InRangeNodeIds = inRange,
                MembershipByNodeId = membership,
                DistancesByNodeId = distances
            };
        }

        var originList = origins.Values.ToArray();
        foreach (var targetNode in nodes)
        {
            if (targetNode.PositionX is not double toX ||
                targetNode.PositionY is not double toY ||
                targetNode.PositionZ is not double toZ)
            {
                continue;
            }

            foreach (var origin in originList)
            {
                double distanceLy;
                if (origin.SolarSystemId == targetNode.Id)
                {
                    distanceLy = 0;
                }
                else
                {
                    var dx = toX - origin.PositionX;
                    var dy = toY - origin.PositionY;
                    var dz = toZ - origin.PositionZ;
                    var rangeMeters = origin.RangeLy * MetersPerLightYear;
                    var distSq = (dx * dx) + (dy * dy) + (dz * dz);
                    if (distSq > rangeMeters * rangeMeters * 4.0 && origin.SolarSystemId != targetNode.Id)
                    {
                        // Far outside this origin: skip hover distance and in-range membership.
                        continue;
                    }

                    distanceLy = Math.Sqrt(distSq) / MetersPerLightYear;
                }

                AddDistance(distances, targetNode.Id, origin, distanceLy);
                var isInRange = distanceLy == 0 || (distanceLy > 0 && distanceLy < origin.RangeLy);
                if (isInRange && (targetNode.Security is null || targetNode.Security.Value <= 0.45))
                {
                    inRange.Add(targetNode.Id);
                    if (!membership.TryGetValue(targetNode.Id, out var sourceList))
                    {
                        sourceList = [];
                        membership[targetNode.Id] = sourceList;
                    }

                    sourceList.Add(origin.SolarSystemId);
                }
            }
        }

        foreach (var targetId in distances.Keys.ToList())
        {
            distances[targetId] = distances[targetId]
                .OrderBy(x => x.DistanceLy)
                .ToList();
        }

        return new JumpRangeOverlayResult
        {
            InRangeNodeIds = inRange,
            MembershipByNodeId = membership,
            DistancesByNodeId = distances
        };
    }

    private static void AddDistance(
        Dictionary<long, List<JumpRangeDistanceDisplay>> distances,
        long targetNodeId,
        JumpRangeOriginSettings origin,
        double distanceLy)
    {
        if (!distances.TryGetValue(targetNodeId, out var values))
        {
            values = [];
            distances[targetNodeId] = values;
        }

        values.Add(new JumpRangeDistanceDisplay
        {
            OriginNodeId = origin.SolarSystemId,
            OriginSystemName = origin.SolarSystemName,
            DistanceLy = distanceLy,
            MaxLy = origin.RangeLy,
            IsInRange = distanceLy == 0 || (distanceLy > 0 && distanceLy < origin.RangeLy)
        });
    }
}
