using System.Collections.Frozen;

namespace Hisa.Core.Models.Intelligence;

public enum HistoricalActivityMetric { Killmails, ConfirmedPvp, KnownIsk, AttackerEntries, LossConcentration }
public sealed record HistoricalOption(string Label, int Value);
public sealed record HistoricalTimelineBin(DateTimeOffset FromUtc, DateTimeOffset ToUtc, long Killmails, double Height);
public sealed record HistoricalShipLoss(string Name, long Count);
public sealed record HistoricalPreferences(bool Enabled = false, HistoricalActivityMetric Metric = HistoricalActivityMetric.Killmails, int Preset = 1);

/// <summary>Immutable, graph-independent visual data. 0 means unavailable/uncertain, 1 observed zero;
/// 2–33 are fixed logarithmic intensity bands. No renderer needs database access.</summary>
public sealed record HistoricalMapSnapshot(ActivityRange Range, HistoricalActivityMetric Metric,
    ActivityCoverage Coverage, FrozenDictionary<long, byte> Levels)
{
    public byte LevelFor(long id) => Levels.GetValueOrDefault(id, Coverage.State == ActivityCoverageState.Observed &&
        !Coverage.AggregateHistoryExpired && !Coverage.GapHistoryExpired && !Coverage.CatchUpPending ? (byte)1 : (byte)0);

    public static HistoricalMapSnapshot Create(ActivityResult result, HistoricalActivityMetric metric)
        => new(result.EffectiveRange, metric, result.Coverage, result.Systems.ToFrozenDictionary(s => s.SolarSystemId,
            s => Level(s.Metrics, result.Total.KillmailCount, metric, result.Coverage)));

    public static byte Level(ActivityMetrics metrics, long totalKills, HistoricalActivityMetric metric, ActivityCoverage coverage)
    {
        if (metric == HistoricalActivityMetric.KnownIsk && metrics.KnownValueCount == 0 && metrics.UnknownValueCount > 0) return 0;
        var value = metric switch
        {
            HistoricalActivityMetric.ConfirmedPvp => metrics.ConfirmedPvpKills,
            HistoricalActivityMetric.KnownIsk => (double)metrics.KnownIskDestroyed,
            HistoricalActivityMetric.AttackerEntries => metrics.AttackerEntries,
            HistoricalActivityMetric.LossConcentration => totalKills > 0 ? 100d * metrics.KillmailCount / totalKills : 0,
            _ => metrics.KillmailCount
        };
        if (value <= 0) return coverage.State == ActivityCoverageState.Observed && !coverage.AggregateHistoryExpired &&
            !coverage.GapHistoryExpired && !coverage.CatchUpPending ? (byte)1 : (byte)0;
        var cap = metric switch { HistoricalActivityMetric.KnownIsk => 1e12, HistoricalActivityMetric.AttackerEntries => 1e5,
            HistoricalActivityMetric.LossConcentration => 100, _ => 1e4 };
        return (byte)(2 + Math.Clamp((int)(31 * Math.Log10(1 + value) / Math.Log10(1 + cap)), 0, 31));
    }

    public static IReadOnlyList<HistoricalTimelineBin> Timeline(ActivityRange range, IReadOnlyList<ActivityBucket> buckets)
    {
        var bucketCount = (int)((range.ToUtc - range.FromUtc).Ticks / ActivityRange.BucketSize.Ticks);
        var stride = Math.Max(1, (int)Math.Ceiling(bucketCount / 96d));
        var counts = new long[(bucketCount + stride - 1) / stride];
        foreach (var bucket in buckets)
        {
            var index = (int)((bucket.BucketStartUtc - range.FromUtc).Ticks / ActivityRange.BucketSize.Ticks);
            if (index >= 0 && index < bucketCount) counts[index / stride] += bucket.Metrics.KillmailCount;
        }
        var max = Math.Max(1, counts.DefaultIfEmpty().Max());
        return counts.Select((count, i) => new HistoricalTimelineBin(
            range.FromUtc.AddTicks(i * (long)stride * ActivityRange.BucketSize.Ticks),
            range.FromUtc.AddTicks(Math.Min(bucketCount, (i + 1) * stride) * ActivityRange.BucketSize.Ticks), count,
            Math.Log10(1d + count) / Math.Log10(1d + max))).ToArray();
    }
}
