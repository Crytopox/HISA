using Avalonia;
using Avalonia.Media;
using Hisa.Core.Models;

namespace Hisa.Rendering;

internal enum UniverseLinkBucket
{
    SameConstellation,
    SameRegion,
    CrossRegion
}

internal readonly record struct UniverseLinkSegment(double X1, double Y1, double X2, double Y2);

internal readonly record struct UniverseRegionCentroid(int RegionId, string Name, double X, double Y);

internal static class UniverseSceneBatch
{
    public const int MaxExactColorBuckets = 48;
    public const int ColorQuantizationBands = 32;

    public static bool ShouldQuantize(int distinctColorCount) => distinctColorCount > MaxExactColorBuckets;

    public static uint PackColor(byte r, byte g, byte b) => ((uint)r << 16) | ((uint)g << 8) | b;

    public static uint QuantizeColorKey(byte r, byte g, byte b)
    {
        const int bands = ColorQuantizationBands;
        var qr = QuantizeChannel(r, bands);
        var qg = QuantizeChannel(g, bands);
        var qb = QuantizeChannel(b, bands);
        return ((uint)qr << 10) | ((uint)qg << 5) | (uint)qb;
    }

    public static UniverseLinkBucket Classify(MapNode from, MapNode to)
    {
        if (from.ConstellationId is not null &&
            to.ConstellationId is not null &&
            from.ConstellationId == to.ConstellationId)
        {
            return UniverseLinkBucket.SameConstellation;
        }

        if (from.RegionId is not null &&
            to.RegionId is not null &&
            from.RegionId == to.RegionId)
        {
            return UniverseLinkBucket.SameRegion;
        }

        return UniverseLinkBucket.CrossRegion;
    }

    public static void AppendLinks(
        IReadOnlyList<MapLink> links,
        IReadOnlyDictionary<long, MapNode> nodesById,
        List<UniverseLinkSegment> sameConstellation,
        List<UniverseLinkSegment> sameRegion,
        List<UniverseLinkSegment> crossRegion)
    {
        var seen = new HashSet<(long Lo, long Hi)>();
        foreach (var link in links)
        {
            if (!nodesById.TryGetValue(link.FromId, out var from) ||
                !nodesById.TryGetValue(link.ToId, out var to) ||
                link.FromId == link.ToId)
            {
                continue;
            }

            var lo = Math.Min(link.FromId, link.ToId);
            var hi = Math.Max(link.FromId, link.ToId);
            if (!seen.Add((lo, hi)))
            {
                continue;
            }

            var segment = new UniverseLinkSegment(from.X, from.Y, to.X, to.Y);
            switch (Classify(from, to))
            {
                case UniverseLinkBucket.SameConstellation:
                    sameConstellation.Add(segment);
                    break;
                case UniverseLinkBucket.SameRegion:
                    sameRegion.Add(segment);
                    break;
                default:
                    crossRegion.Add(segment);
                    break;
            }
        }
    }

    public static List<UniverseRegionCentroid> BuildRegionCentroids(IReadOnlyList<MapNode> nodes)
    {
        var sums = new Dictionary<int, (double X, double Y, int Count, string Name)>();
        foreach (var node in nodes)
        {
            if (node.RegionId is not int regionId || string.IsNullOrWhiteSpace(node.RegionName))
            {
                continue;
            }

            if (sums.TryGetValue(regionId, out var sum))
            {
                sums[regionId] = (sum.X + node.X, sum.Y + node.Y, sum.Count + 1, sum.Name);
            }
            else
            {
                sums[regionId] = (node.X, node.Y, 1, node.RegionName);
            }
        }

        var result = new List<UniverseRegionCentroid>(sums.Count);
        foreach (var (regionId, sum) in sums)
        {
            result.Add(new UniverseRegionCentroid(regionId, sum.Name, sum.X / sum.Count, sum.Y / sum.Count));
        }

        return result;
    }

    public static StreamGeometry? CreateSegmentGeometry(IReadOnlyList<UniverseLinkSegment> segments)
    {
        if (segments.Count == 0)
        {
            return null;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var segment in segments)
            {
                context.BeginFigure(new Point(segment.X1, segment.Y1), false);
                context.LineTo(new Point(segment.X2, segment.Y2));
                context.EndFigure(false);
            }
        }

        return geometry;
    }

    public static void AddCircle(StreamGeometryContext context, Point center, double radius)
    {
        if (radius <= 0)
        {
            return;
        }

        var start = new Point(center.X, center.Y - radius);
        context.BeginFigure(start, true);
        context.ArcTo(new Point(center.X, center.Y + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.ArcTo(start, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        context.EndFigure(true);
    }

    private static int QuantizeChannel(byte value, int bands)
    {
        var quantized = (value * bands) >> 8;
        if (quantized >= bands)
        {
            quantized = bands - 1;
        }

        return quantized;
    }
}
