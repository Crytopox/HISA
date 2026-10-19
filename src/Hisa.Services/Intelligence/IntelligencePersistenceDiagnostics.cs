using Hisa.Core.Models.Intelligence;

namespace Hisa.Services.Intelligence;

public sealed record IntelligencePersistenceSnapshot(
    long Received,
    long Queued,
    long Committed,
    long Duplicates,
    long Failed,
    long QueueOverflow,
    long Undelivered,
    int QueueDepth,
    int PeakQueueDepth);

public sealed class IntelligencePersistenceDiagnostics
{
    private long _received;
    private long _queued;
    private long _committed;
    private long _duplicates;
    private long _failed;
    private long _queueOverflow;
    private long _undelivered;
    private int _peakQueueDepth;
    private long _pendingOverflows;
    private long _pendingCoalescedGaps;
    private long _lastOverflowKillmailId = long.MinValue;
    private long _lastCoalescedSequence = long.MinValue;

    public long QueueOverflow => Volatile.Read(ref _queueOverflow);

    public bool HasPendingSummaries =>
        Volatile.Read(ref _pendingOverflows) > 0 || Volatile.Read(ref _pendingCoalescedGaps) > 0;

    public void RecordReceived() => Interlocked.Increment(ref _received);

    public void RecordQueued() => Interlocked.Increment(ref _queued);

    public void NoteDepth(int depth)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref _peakQueueDepth);
            if (depth <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _peakQueueDepth, depth, seen) != seen);
    }

    public void RecordOverflow(long? killmailId)
    {
        Interlocked.Increment(ref _queueOverflow);
        Interlocked.Increment(ref _pendingOverflows);
        if (killmailId is long id)
        {
            Interlocked.Exchange(ref _lastOverflowKillmailId, id);
        }
    }

    public void RecordCoalescedGap(IntelligenceCollectionGap gap)
    {
        Interlocked.Increment(ref _pendingCoalescedGaps);
        if (gap.SourceSequence is long sequence)
        {
            Interlocked.Exchange(ref _lastCoalescedSequence, sequence);
        }
    }

    public void AddWrite(KillmailWriteResult result)
    {
        if (result.Inserted > 0)
        {
            Interlocked.Add(ref _committed, result.Inserted);
        }

        if (result.Duplicates > 0)
        {
            Interlocked.Add(ref _duplicates, result.Duplicates);
        }
    }

    public void RecordFailed(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _failed, count);
        }
    }

    public void RecordUndelivered(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _undelivered, count);
        }
    }

    public void AppendPending(List<IntelligenceCollectionGap> gaps)
    {
        var overflows = Interlocked.Exchange(ref _pendingOverflows, 0);
        if (overflows > 0)
        {
            var killmailId = Interlocked.Exchange(ref _lastOverflowKillmailId, long.MinValue);
            gaps.Add(new IntelligenceCollectionGap
            {
                Kind = IntelligenceCollectionGapKind.QueueOverflow,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                KillmailId = killmailId == long.MinValue ? null : killmailId,
                Count = ToCount(overflows),
                Detail = "Persistence queue rejected observations before they were written."
            });
        }

        var coalesced = Interlocked.Exchange(ref _pendingCoalescedGaps, 0);
        if (coalesced > 0)
        {
            var sequence = Interlocked.Exchange(ref _lastCoalescedSequence, long.MinValue);
            gaps.Add(new IntelligenceCollectionGap
            {
                Kind = IntelligenceCollectionGapKind.SkippedSequence,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Source = KillmailObservation.R2Z2SourceId,
                SourceSequence = sequence == long.MinValue ? null : sequence,
                Count = ToCount(coalesced),
                Detail = "Coverage gaps were coalesced because the persistence queue was full."
            });
        }
    }

    public void Restore(IReadOnlyList<IntelligenceCollectionGap> gaps)
    {
        foreach (var gap in gaps)
        {
            if (gap.Kind == IntelligenceCollectionGapKind.QueueOverflow)
            {
                Interlocked.Add(ref _pendingOverflows, gap.Count);
                if (gap.KillmailId is long killmailId)
                {
                    Interlocked.Exchange(ref _lastOverflowKillmailId, killmailId);
                }
            }
            else if (gap.Kind == IntelligenceCollectionGapKind.SkippedSequence &&
                     gap.Detail?.Contains("coalesced", StringComparison.Ordinal) == true)
            {
                Interlocked.Add(ref _pendingCoalescedGaps, gap.Count);
                if (gap.SourceSequence is long sequence)
                {
                    Interlocked.Exchange(ref _lastCoalescedSequence, sequence);
                }
            }
        }
    }

    public IntelligencePersistenceSnapshot Snapshot(int queueDepth)
    {
        return new IntelligencePersistenceSnapshot(
            Volatile.Read(ref _received),
            Volatile.Read(ref _queued),
            Volatile.Read(ref _committed),
            Volatile.Read(ref _duplicates),
            Volatile.Read(ref _failed),
            Volatile.Read(ref _queueOverflow),
            Volatile.Read(ref _undelivered),
            queueDepth,
            Volatile.Read(ref _peakQueueDepth));
    }

    private static int ToCount(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
