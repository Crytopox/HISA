using Avalonia;

namespace Hisa.Rendering.Controls;

internal readonly record struct UniverseLabelCandidate(long NodeId, Point Center, Size Size, double NodeRadius, int Priority);
internal readonly record struct UniverseNodeObstacle(Point Center, double Radius);
internal readonly record struct UniversePlacedLabel(long NodeId, Point Origin, Rect Bounds);

internal static class UniverseLabelPlacement
{
    private const double CellSize = 64;
    private const double ScreenMargin = 2;

    internal static IReadOnlyList<UniversePlacedLabel> Place(
        IReadOnlyList<UniverseLabelCandidate> candidates,
        IReadOnlyList<UniverseNodeObstacle> nodes,
        Rect viewport,
        int budget,
        IReadOnlyList<Rect>? reservedRects = null)
    {
        var obstacles = new List<Rect>(nodes.Count + Math.Min(candidates.Count, budget) + (reservedRects?.Count ?? 0));
        var cells = new Dictionary<(int X, int Y), List<int>>();
        if (reservedRects is not null)
        {
            foreach (var reserved in reservedRects) Add(reserved.Inflate(2));
        }
        foreach (var node in nodes)
        {
            var radius = node.Radius + 2;
            Add(new Rect(node.Center.X - radius, node.Center.Y - radius, radius * 2, radius * 2));
        }

        var placed = new List<UniversePlacedLabel>(Math.Min(candidates.Count, budget));
        foreach (var candidate in candidates.OrderBy(c => c.Priority).ThenBy(c => c.NodeId))
        {
            if (placed.Count >= budget)
            {
                break;
            }

            var sideGap = candidate.NodeRadius + 7;
            var xRight = candidate.Center.X + sideGap;
            var xLeft = candidate.Center.X - sideGap - candidate.Size.Width;
            var yBelow = candidate.Center.Y + 6;
            var yAbove = candidate.Center.Y - 6 - candidate.Size.Height;
            for (var side = 0; side < 4; side++)
            {
                var position = new Point(side < 2 ? xRight : xLeft,
                    side % 2 == 0 ? yBelow : yAbove);
                var rect = new Rect(position, candidate.Size);
                if (rect.Left < viewport.Left + ScreenMargin ||
                    rect.Top < viewport.Top + ScreenMargin ||
                    rect.Right > viewport.Right - ScreenMargin ||
                    rect.Bottom > viewport.Bottom - ScreenMargin ||
                    IntersectsExisting(rect))
                {
                    continue;
                }

                Add(rect.Inflate(2));
                placed.Add(new UniversePlacedLabel(candidate.NodeId,
                    new Point(rect.X + 3, rect.Y + 2), rect));
                break;
            }
        }

        return placed;

        void Add(Rect rect)
        {
            var index = obstacles.Count;
            obstacles.Add(rect);
            var minX = (int)Math.Floor(rect.Left / CellSize);
            var maxX = (int)Math.Floor(rect.Right / CellSize);
            var minY = (int)Math.Floor(rect.Top / CellSize);
            var maxY = (int)Math.Floor(rect.Bottom / CellSize);
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            {
                var key = (x, y);
                if (!cells.TryGetValue(key, out var entries))
                {
                    entries = [];
                    cells[key] = entries;
                }
                entries.Add(index);
            }
        }

        bool IntersectsExisting(Rect rect)
        {
            var minX = (int)Math.Floor(rect.Left / CellSize);
            var maxX = (int)Math.Floor(rect.Right / CellSize);
            var minY = (int)Math.Floor(rect.Top / CellSize);
            var maxY = (int)Math.Floor(rect.Bottom / CellSize);
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            {
                if (!cells.TryGetValue((x, y), out var entries)) continue;
                foreach (var index in entries)
                {
                    if (obstacles[index].Intersects(rect)) return true;
                }
            }
            return false;
        }
    }
}
