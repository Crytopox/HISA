using Microsoft.Extensions.Logging;

namespace Hisa.Services.Intelligence;

public sealed class ZkillNormalizationDiagnostics
{
    internal static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly Func<long> _utcNowMilliseconds;
    private readonly long[] _counts;
    private readonly long[] _lastLoggedAtMilliseconds;
    private long _dataGaps;

    public ZkillNormalizationDiagnostics(ILogger logger, Func<long>? utcNowMilliseconds = null)
    {
        _logger = logger;
        _utcNowMilliseconds = utcNowMilliseconds ?? (() => Environment.TickCount64);
        var reasons = Enum.GetValues<KillmailNormalizationRejection>().Length;
        _counts = new long[reasons];
        _lastLoggedAtMilliseconds = new long[reasons];
        Array.Fill(_lastLoggedAtMilliseconds, long.MinValue);
    }

    public long RejectionCount(KillmailNormalizationRejection reason)
    {
        return Volatile.Read(ref _counts[(int)reason]);
    }

    public long DataGapCount => Volatile.Read(ref _dataGaps);

    public void RecordDataGap(long sequence, int attempts)
    {
        var count = Interlocked.Increment(ref _dataGaps);
        _logger.LogWarning(
            "Skipping malformed R2Z2 sequence {Sequence} after {Attempts} attempts. DataGapCount: {DataGapCount}.",
            sequence,
            attempts,
            count);
    }

    public void RecordRejection(long sequence, KillmailNormalizationRejection reason)
    {
        var index = (int)reason;
        var count = Interlocked.Increment(ref _counts[index]);
        var now = _utcNowMilliseconds();
        var last = Volatile.Read(ref _lastLoggedAtMilliseconds[index]);
        if (last != long.MinValue && now - last < LogInterval.TotalMilliseconds)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastLoggedAtMilliseconds[index], now, last) != last)
        {
            return;
        }

        _logger.LogWarning(
            "Rejected R2Z2 observation at sequence {Sequence}. Reason: {Reason}. RejectionCount: {RejectionCount}.",
            sequence,
            reason,
            count);
    }
}
