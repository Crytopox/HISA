using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Intelligence;

public sealed class ZkillNormalizationDiagnostics
{
    internal static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private readonly IIntelligenceCoverageRecorder? _coverageRecorder;
    private readonly Func<long> _utcNowMilliseconds;
    private readonly long[] _counts;
    private readonly long[] _lastLoggedAtMilliseconds;
    private readonly HashSet<long> _reportedSequences = new();
    private readonly Queue<long> _reportedSequenceOrder = new();
    private const int MaxRememberedSequences = 1024;
    private long _dataGaps;

    public ZkillNormalizationDiagnostics(
        ILogger logger,
        Func<long>? utcNowMilliseconds = null,
        IIntelligenceCoverageRecorder? coverageRecorder = null)
    {
        _logger = logger;
        _coverageRecorder = coverageRecorder;
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
        if (!RememberSequence(sequence))
        {
            return;
        }

        _coverageRecorder?.Record(new IntelligenceCollectionGap
        {
            Kind = IntelligenceCollectionGapKind.SkippedSequence,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Source = KillmailObservation.R2Z2SourceId,
            SourceSequence = sequence,
            Detail = $"Skipped malformed JSON after {attempts} attempts."
        });
    }

    public void RecordRejection(long sequence, KillmailNormalizationRejection reason)
    {
        var index = (int)reason;
        var count = Interlocked.Increment(ref _counts[index]);
        if (reason != KillmailNormalizationRejection.MalformedJson && RememberSequence(sequence))
        {
            _coverageRecorder?.Record(new IntelligenceCollectionGap
            {
                Kind = IntelligenceCollectionGapKind.NormalizationRejected,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Source = KillmailObservation.R2Z2SourceId,
                SourceSequence = sequence,
                Detail = reason.ToString()
            });
        }

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

    private bool RememberSequence(long sequence)
    {
        lock (_reportedSequences)
        {
            if (!_reportedSequences.Add(sequence))
            {
                return false;
            }

            _reportedSequenceOrder.Enqueue(sequence);
            if (_reportedSequenceOrder.Count > MaxRememberedSequences)
            {
                _reportedSequences.Remove(_reportedSequenceOrder.Dequeue());
            }

            return true;
        }
    }
}
