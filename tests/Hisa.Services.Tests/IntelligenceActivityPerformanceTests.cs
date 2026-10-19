using System.Diagnostics;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Services.Tests;

[CollectionDefinition("Historical activity", DisableParallelization = true)]
public sealed class HistoricalActivityCollection;

public sealed partial class IntelligenceActivityTests
{
    [Fact]
    public async Task ThirtyDayPerformance_ReportsGroupedQueriesMemoryAndConcurrentIngestion()
    {
        await using var db = await Database.Create();
        const int systems = 4000, days = 30, dailyBuckets = 4, killsPerBucket = 4;
        const long bucketRows = systems * days * dailyBuckets;
        const long representedKills = bucketRows * killsPerBucket;
        var range = new ActivityRange(Epoch, Epoch.AddDays(days));
        // Seed the production materialized shape directly, isolating historical query costs
        // from the time/memory of generating 1.92 million raw killmail objects.
        await db.Sql($"""
            BEGIN;
            WITH RECURSIVE systems(s) AS (SELECT 1 UNION ALL SELECT s+1 FROM systems WHERE s<{systems}),
                days(d) AS (SELECT 0 UNION ALL SELECT d+1 FROM days WHERE d<29),
                slots(p) AS (SELECT 0 UNION ALL SELECT p+1 FROM slots WHERE p<3)
            INSERT INTO SystemActivity
            SELECT s, {Epoch.UtcTicks}+(d*288+p*72+s%12)*{ActivityRange.BucketSize.Ticks},
                4, 2, CAST(s*100+d AS TEXT)||'.0001', 3, 1, 12,
                {Epoch.UtcTicks}+(d*288+p*72+s%12)*{ActivityRange.BucketSize.Ticks}+{TimeSpan.TicksPerSecond},
                {Epoch.UtcTicks}+(d*288+p*72+s%12)*{ActivityRange.BucketSize.Ticks}+{4 * TimeSpan.TicksPerSecond}
            FROM systems CROSS JOIN days CROSS JOIN slots;
            INSERT INTO ShipLossActivity SELECT SolarSystemId, BucketStartUtcTicks, 587, KillmailCount FROM SystemActivity;
            COMMIT;
            PRAGMA wal_checkpoint(TRUNCATE);
            """);
        Assert.Equal(bucketRows, await db.Scalar("SELECT COUNT(*) FROM SystemActivity;"));
        var first = await db.Repository.GetMapActivityAsync(range);
        Assert.Equal(representedKills, first.Total.KillmailCount);
        Assert.Equal(96030960048m, first.Total.KnownIskDestroyed);
        Assert.Equal(systems, first.Systems.Count);
        Assert.Empty(first.Buckets);
        Assert.Empty(first.ShipLosses);

        var nextId = 10000000L;
        var baselineIngestion = new List<double>();
        for (var sample = 0; sample < 5; sample++)
        {
            var batch = Enumerable.Range(0, 64).Select(i => Observation(nextId++, i % systems + 1)).ToArray();
            var timer = Stopwatch.StartNew();
            await db.Repository.InsertBatchAsync(batch);
            baselineIngestion.Add(timer.Elapsed.TotalMilliseconds);
        }
        await db.Repository.GetTopSystemsAsync(range, ActivityRanking.KillmailCount, 20);
        await db.Repository.GetTopSystemsAsync(range, ActivityRanking.KnownIskDestroyed, 20);
        await db.Repository.GetTimeSeriesAsync(systems, range);

        var mapLatency = new List<double>();
        var killRankingLatency = new List<double>();
        var iskRankingLatency = new List<double>();
        var seriesLatency = new List<double>();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var initialWorkingSet = process.WorkingSet64;
        var initialHeap = GC.GetTotalMemory(false);
        var peakWorkingSet = initialWorkingSet;
        var peakHeap = initialHeap;
        var allocationsBefore = GC.GetTotalAllocatedBytes(precise: true);
        using var stopSampling = new CancellationTokenSource();
        var memorySampler = Task.Run(async () =>
        {
            while (!stopSampling.IsCancellationRequested)
            {
                process.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
                await Task.Delay(2);
            }
        });
        try
        {
            for (var sample = 0; sample < 5; sample++)
            {
                var timer = Stopwatch.StartNew();
                var map = await db.Repository.GetMapActivityAsync(range);
                mapLatency.Add(timer.Elapsed.TotalMilliseconds);
                Assert.Equal(representedKills + 320, map.Total.KillmailCount);
                Assert.Equal(systems, map.Systems.Count);
                timer.Restart();
                Assert.Equal(20, (await db.Repository.GetTopSystemsAsync(range, ActivityRanking.KillmailCount, 20)).Systems.Count);
                killRankingLatency.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart();
                var ranked = await db.Repository.GetTopSystemsAsync(range, ActivityRanking.KnownIskDestroyed, 20);
                iskRankingLatency.Add(timer.Elapsed.TotalMilliseconds);
                Assert.Equal(systems, ranked.Systems[0].SolarSystemId);
                timer.Restart();
                Assert.Equal(120, (await db.Repository.GetTimeSeriesAsync(systems, range)).Buckets.Count);
                seriesLatency.Add(timer.Elapsed.TotalMilliseconds);
            }
        }
        finally
        {
            stopSampling.Cancel();
            await memorySampler;
        }
        var queryAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocationsBefore;
        var concurrentIngestion = new List<double>();
        for (var sample = 0; sample < 5; sample++)
        {
            var snapshotReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var startWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            db.Repository.ActivityReadSnapshotReady = async token =>
            {
                snapshotReady.TrySetResult();
                await startWork.Task.WaitAsync(token);
            };
            var read = db.Repository.GetMapActivityAsync(range);
            await snapshotReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var batch = Enumerable.Range(0, 64).Select(i => Observation(nextId++, i % systems + 1)).ToArray();
            var timer = Stopwatch.StartNew();
            var insert = Task.Run(() => db.Repository.InsertBatchAsync(batch));
            startWork.TrySetResult();
            Assert.Equal(64, (await insert).Inserted);
            concurrentIngestion.Add(timer.Elapsed.TotalMilliseconds);
            Assert.Equal(representedKills + 320 + sample * 64, (await read).Total.KillmailCount);
        }
        db.Repository.ActivityReadSnapshotReady = null;
        var after = await db.Repository.GetMapActivityAsync(range);
        Assert.Equal(representedKills + 640, after.Total.KillmailCount);
        output.WriteLine($"30-day fixture: systems={systems}; activeBuckets={bucketRows}; killsPerBucket={killsPerBucket}; representedKills={representedKills}; databaseBytes={new FileInfo(db.Path).Length}");
        output.WriteLine($"all-system summary {Latency(mapLatency)}; top kills {Latency(killRankingLatency)}; top ISK {Latency(iskRankingLatency)}; single-system series (120 buckets) {Latency(seriesLatency)}");
        output.WriteLine($"64-kill batch, no read: {Latency(baselineIngestion)}; concurrent WAL historical summary: {Latency(concurrentIngestion)}");
        output.WriteLine($"Query workload memory sampled every 2 ms: peak process working set={peakWorkingSet} bytes (delta={peakWorkingSet-initialWorkingSet}); peak managed heap={peakHeap} bytes (delta={peakHeap-initialHeap}); cumulative managed allocation for 20 queries plus sampling={queryAllocatedBytes} bytes.");
    }

    private static string Latency(List<double> samples)
    {
        samples.Sort();
        return $"median={samples[samples.Count/2]:F2} ms, max={samples[^1]:F2} ms (n={samples.Count})";
    }
}
