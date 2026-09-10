using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Hisa.Rendering;

internal sealed class MapPenCache
{
    private readonly Dictionary<long, Pen> _pens = [];

    public Pen Get(Color color, double width)
    {
        var key = Pack(color, width);
        if (_pens.TryGetValue(key, out var pen))
        {
            return pen;
        }

        pen = new Pen(new ImmutableSolidColorBrush(color), width);
        _pens[key] = pen;
        return pen;
    }

    public void Clear() => _pens.Clear();

    private static long Pack(Color color, double width)
    {
        var widthMilli = Math.Clamp((int)Math.Round(width * 100.0), 0, 100_000);
        return ((long)color.ToUInt32() << 20) | (uint)widthMilli;
    }
}
