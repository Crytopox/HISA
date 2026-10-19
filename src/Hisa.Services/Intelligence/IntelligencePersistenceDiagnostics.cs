using Hisa.Core.Models.Intelligence;

namespace Hisa.Services.Intelligence;

public sealed record IntelligencePersistenceSnapshot(
    long Received,
    long Queued,
    long Committed,
    long Duplicates,
    long PermanentlyRejected,
    long Pending,
    long Lost,
    long QueueOverflow,
    long Undelivered,
    long LoggedOnlyGaps,
    int QueueDepth,
    int PeakQueueDepth);

public sealed class IntelligencePersistenceDiagnostics
{
    private readonly object _gapGate = new();
    private readonly Dictionary<string, CoalescedGap> _coalesced = new(StringComparer.Ordinal);
    private long _received;
    private long _queued;
    private long _committed;
    private long _duplicates;
    private long _permanentlyRejected;
    private long _lost;
    private long _queueOverflow;
    private long _undelivered;
    private long _loggedOnlyGaps;
    private int _peakQueueDepth;

    public long QueueOverflow => Volatile.Read(ref _queueOverflow);

    public bool HasPendingSummaries
    {
        get
        {
            lock (_gapGate)
            {
                return _coalesced.Count > 0;
            }
        }
    }

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

    public void RecordOverflow(KillmailObservation observation)
    {
        Interlocked.Increment(ref _queueOverflow);
        RecordCoalescedGap(new IntelligenceCollectionGap
        {
            Kind = IntelligenceCollectionGapKind.QueueOverflow,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Source = observation.Source,
            SourceSequence = observation.SourceSequence,
            KillmailId = observation.KillmailId,
            Detail = "Persistence queue rejected the observation before it was written."
        });
    }

    public void RecordCoalescedGap(IntelligenceCollectionGap gap)
    {
        lock (_gapGate)
        {
            if (!_coalesced.TryGetValue(gap.Kind, out var existing))
            {
                existing = new CoalescedGap { Kind = gap.Kind };
                _coalesced.Add(gap.Kind, existing);
            }

            existing.Count += gap.Count < 1 ? 1 : gap.Count;
            existing.Source ??= gap.Source;
            existing.KillmailId = gap.KillmailId ?? existing.KillmailId;
            if (gap.SourceSequence is long sequence)
            {
                existing.FirstSequence ??= sequence;
                existing.LastSequence = sequence;
            }

            if (!string.IsNullOrWhiteSpace(gap.Detail))
            {
                existing.Detail = gap.Detail;
            }
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

    public void RecordPermanentlyRejected(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _permanentlyRejected, count);
        }
    }

    public void RecordLost(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _lost, count);
        }
    }

    public void RecordUndelivered(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _undelivered, count);
        }
    }

    public void RecordLoggedOnly(int count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _loggedOnlyGaps, count);
        }
    }

    public void AppendPending(List<IntelligenceCollectionGap> gaps)
    {
        List<CoalescedGap> pending;
        lock (_gapGate)
        {
            if (_coalesced.Count == 0)
            {
                return;
            }

            pending = _coalesced.Values.ToList();
            _coalesced.Clear();
        }

        foreach (var gap in pending)
        {
            gaps.Add(gap.ToRecord());
        }
    }

    public void Restore(IReadOnlyList<IntelligenceCollectionGap> gaps)
    {
        foreach (var gap in gaps)
        {
            RecordCoalescedGap(gap);
        }
    }

    public IntelligencePersistenceSnapshot Snapshot(int queueDepth, int pending)
    {
        return new IntelligencePersistenceSnapshot(
            Volatile.Read(ref _received),
            Volatile.Read(ref _queued),
            Volatile.Read(ref _committed),
            Volatile.Read(ref _duplicates),
            Volatile.Read(ref _permanentlyRejected),
            pending,
            Volatile.Read(ref _lost),
            Volatile.Read(ref _queueOverflow),
            Volatile.Read(ref _undelivered),
            Volatile.Read(ref _loggedOnlyGaps),
            queueDepth,
            Volatile.Read(ref _peakQueueDepth));
    }

    private sealed class CoalescedGap
    {
        public required string Kind { get; init; }
        public string? Source { get; set; }
        public long? FirstSequence { get; set; }
        public long? LastSequence { get; set; }
        public long? KillmailId { get; set; }
        public int Count { get; set; }
        public string? Detail { get; set; }

        public IntelligenceCollectionGap ToRecord()
        {
            var detail = Detail;
            if (Count > 1 && FirstSequence is long first && LastSequence is long last && first != last)
            {
                detail = $"{detail} Sequences {first}..{last}.".Trim();
            }

            return new IntelligenceCollectionGap
            {
                Kind = Kind,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Source = Source,
                SourceSequence = LastSequence,
                KillmailId = KillmailId,
                Count = Count < 1 ? 1 : Count,
                Detail = detail
            };
        }
    }
}
