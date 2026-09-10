using Avalonia;

namespace Hisa.Rendering;

internal sealed class ScreenPointGrid
{
    private const double DefaultCellSize = 48.0;
    private List<int>[] _cells = [];
    private int _columns;
    private int _rows;
    private double _cellSize = DefaultCellSize;
    private double _originX;
    private double _originY;
    private int _pointCount;

    public void Rebuild(Point[] points, int count, Size bounds, double cellSize = DefaultCellSize)
    {
        _pointCount = count;
        _cellSize = Math.Max(16.0, cellSize);
        _originX = -_cellSize;
        _originY = -_cellSize;
        var width = Math.Max(_cellSize, bounds.Width + (_cellSize * 2));
        var height = Math.Max(_cellSize, bounds.Height + (_cellSize * 2));
        _columns = Math.Max(1, (int)Math.Ceiling(width / _cellSize));
        _rows = Math.Max(1, (int)Math.Ceiling(height / _cellSize));
        var cellCount = _columns * _rows;
        if (_cells.Length != cellCount)
        {
            _cells = new List<int>[cellCount];
        }

        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] ??= [];
            _cells[i].Clear();
        }

        for (var i = 0; i < count; i++)
        {
            var index = CellIndex(points[i]);
            if (index < 0)
            {
                continue;
            }

            _cells[index].Add(i);
        }
    }

    public int? FindClosestIndex(Point query, double threshold, Point[] points)
    {
        if (_pointCount == 0 || _cells.Length == 0)
        {
            return null;
        }

        var thresholdSq = threshold * threshold;
        var bestDistSq = thresholdSq;
        int? bestIndex = null;
        var minCol = Col(query.X - threshold);
        var maxCol = Col(query.X + threshold);
        var minRow = Row(query.Y - threshold);
        var maxRow = Row(query.Y + threshold);

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                var cell = _cells[(row * _columns) + col];
                for (var i = 0; i < cell.Count; i++)
                {
                    var index = cell[i];
                    if ((uint)index >= (uint)points.Length)
                    {
                        continue;
                    }

                    var p = points[index];
                    var dx = p.X - query.X;
                    var dy = p.Y - query.Y;
                    var distSq = (dx * dx) + (dy * dy);
                    if (distSq <= bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIndex = index;
                    }
                }
            }
        }

        return bestIndex;
    }

    private int CellIndex(Point point)
    {
        var col = Col(point.X);
        var row = Row(point.Y);
        if ((uint)col >= (uint)_columns || (uint)row >= (uint)_rows)
        {
            return -1;
        }

        return (row * _columns) + col;
    }

    private int Col(double x)
    {
        var col = (int)Math.Floor((x - _originX) / _cellSize);
        return Math.Clamp(col, 0, Math.Max(0, _columns - 1));
    }

    private int Row(double y)
    {
        var row = (int)Math.Floor((y - _originY) / _cellSize);
        return Math.Clamp(row, 0, Math.Max(0, _rows - 1));
    }
}
