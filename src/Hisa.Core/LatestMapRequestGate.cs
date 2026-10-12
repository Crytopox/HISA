namespace Hisa.Core;

internal sealed class LatestMapRequestGate
{
    private readonly object _sync = new();
    private long _version;
    private CancellationTokenSource? _current;

    internal readonly record struct Request(long Version, CancellationTokenSource Source)
    {
        public CancellationToken Token => Source.Token;
    }

    public Request Begin()
    {
        lock (_sync)
        {
            _current?.Cancel();
            var source = new CancellationTokenSource();
            _current = source;
            return new Request(++_version, source);
        }
    }

    public bool IsCurrent(Request request)
    {
        lock (_sync)
        {
            return request.Version == _version &&
                   ReferenceEquals(request.Source, _current) &&
                   !request.Source.IsCancellationRequested;
        }
    }

    public void Complete(Request request)
    {
        lock (_sync)
        {
            if (ReferenceEquals(request.Source, _current))
            {
                _current = null;
            }
        }

        request.Source.Dispose();
    }
}
