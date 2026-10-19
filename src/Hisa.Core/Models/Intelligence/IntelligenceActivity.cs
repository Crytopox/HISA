namespace Hisa.Core.Models.Intelligence;

public enum ActivityCoverageState { Unknown, Partial, Observed }
public enum ActivityRanking { KillmailCount, KnownIskDestroyed }

/// <summary>Local reception evidence only; never a guarantee of complete source coverage.</summary>
public sealed record ActivityCoverage(ActivityCoverageState State, TimeSpan ObservedDuration,
    long KnownGapCount, bool CatchUpPending, bool SourceCompletenessGuaranteed = false,
    bool AggregateHistoryExpired = false, bool GapHistoryExpired = false);

public sealed record ActivityMetrics(long KillmailCount, long ConfirmedPvpKills, long OtherKills,
    decimal KnownIskDestroyed, long KnownValueCount, long UnknownValueCount, long AttackerEntries,
    DateTimeOffset? FirstOccurrenceUtc, DateTimeOffset? LastOccurrenceUtc)
{
    public static ActivityMetrics Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, null, null);
}

public sealed record SystemActivity(long SolarSystemId, ActivityMetrics Metrics);
public sealed record ActivityBucket(long SolarSystemId, DateTimeOffset BucketStartUtc, ActivityMetrics Metrics);
public sealed record ShipLossActivity(long SolarSystemId, int VictimShipTypeId, long LossCount);
public sealed record RankedActivityResult(ActivityRange RequestedRange, ActivityRange EffectiveRange,
    IReadOnlyList<SystemActivity> Systems, ActivityCoverage Coverage);
public sealed record ActivityResult(ActivityRange RequestedRange, ActivityRange EffectiveRange,
    IReadOnlyList<SystemActivity> Systems, IReadOnlyList<ActivityBucket> Buckets,
    IReadOnlyList<ShipLossActivity> ShipLosses, ActivityMetrics Total, ActivityCoverage Coverage);

/// <summary>Half-open UTC range of bucket starts. Non-aligned endpoints advance to the next bucket
/// boundary, preserving disjoint adjacent queries; the effective interval is always returned.</summary>
public readonly record struct ActivityRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc)
{
    public static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(5);
    public static readonly IReadOnlyList<TimeSpan> CommonWindows = new[]
    {
        TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6),
        TimeSpan.FromHours(24), TimeSpan.FromDays(7), TimeSpan.FromDays(30)
    };
    public static DateTimeOffset BucketStart(DateTimeOffset time) => new(
        time.UtcTicks - time.UtcTicks % BucketSize.Ticks, TimeSpan.Zero);
    /// <summary>Common rolling windows end at the last completed bucket boundary.</summary>
    public static ActivityRange Last(TimeSpan window, DateTimeOffset now)
    {
        var end = BucketStart(now);
        return new(end - window, end);
    }
    public ActivityRange Effective()
    {
        if (FromUtc.Offset != TimeSpan.Zero || ToUtc.Offset != TimeSpan.Zero ||
            ToUtc <= FromUtc || ToUtc - FromUtc > TimeSpan.FromDays(30))
            throw new ArgumentException("Use a positive UTC range of at most 30 days.");
        var end = BucketStart(ToUtc);
        var start = BucketStart(FromUtc);
        return new(start == FromUtc ? start : start + BucketSize, end == ToUtc ? end : end + BucketSize);
    }
}
