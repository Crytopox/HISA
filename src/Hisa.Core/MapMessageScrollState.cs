namespace Hisa.Core;

public sealed class MapMessageScrollState
{
    public double Offset { get; private set; }
    private double _pauseSeconds = 1.2;

    public void Advance(TimeSpan elapsed, double maximum, bool paused)
    {
        maximum = Math.Max(0, maximum);
        Offset = Math.Clamp(Offset, 0, maximum);
        if (maximum == 0)
        {
            _pauseSeconds = 1.2;
            return;
        }
        if (paused) return;
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        var waiting = Math.Min(seconds, _pauseSeconds);
        _pauseSeconds -= waiting;
        seconds -= waiting;
        if (seconds <= 0) return;
        if (Offset >= maximum)
        {
            Offset = 0;
            _pauseSeconds = 1.2;
            return;
        }
        Offset = Math.Min(maximum, Offset + seconds * 24);
        if (Offset >= maximum) _pauseSeconds = 1.2;
    }

    public void Seek(double offset, double maximum)
    {
        Offset = Math.Clamp(offset, 0, Math.Max(0, maximum));
        _pauseSeconds = 1.2;
    }
}
