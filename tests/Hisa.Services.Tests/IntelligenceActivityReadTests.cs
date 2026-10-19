using System.Diagnostics;
using Hisa.Core.Models.Intelligence;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Hisa.Services.Intelligence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed partial class IntelligenceActivityTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(2, 17)]
    [InlineData(5, 6)]
    public void CustomRanges_RejectNonalignedOrSubBucketWindows(int fromMinute, int toMinute)
        => Assert.Throws<ArgumentException>(() => new ActivityRange(Epoch.AddMinutes(fromMinute), Epoch.AddMinutes(toMinute)).Effective());

    [Fact]
    public async Task ReaderSnapshot_DoesNotBlockConcurrentIngestion_AndKeepsMetricsAndCoverageConsistent()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1)]);
        await db.Repository.StartCollectionSessionAsync("reader-test", Epoch);
        await db.Repository.HeartbeatCollectionAsync("reader-test", Epoch.AddHours(1));
        var snapshotReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Repository.ActivityReadSnapshotReady = async token =>
        {
            snapshotReady.TrySetResult();
            await releaseReader.Task.WaitAsync(token);
        };
        var read = db.Repository.GetMapActivityAsync(Range);
        try
        {
            await snapshotReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var write = Task.Run(async () =>
            {
                var inserted = await db.Repository.InsertBatchAsync([Observation(2)]);
                await db.Repository.RecordGapsAsync([new IntelligenceCollectionGap { Kind = "ConcurrentLoss", RecordedAtUtc = Epoch.AddMinutes(10) }]);
                return inserted;
            });
            Assert.Equal(1, (await write.WaitAsync(TimeSpan.FromSeconds(3))).Inserted);
            Assert.False(read.IsCompleted); // The insert finished while the deliberately slow reader is still paused.
        }
        finally { releaseReader.TrySetResult(); }
        var previous = await read;
        Assert.Equal(1, previous.Total.KillmailCount);
        Assert.Equal(0, previous.Coverage.KnownGapCount);
        Assert.Equal(ActivityCoverageState.Observed, previous.Coverage.State);
        db.Repository.ActivityReadSnapshotReady = null;
        var current = await db.Repository.GetMapActivityAsync(Range);
        Assert.Equal(2, current.Total.KillmailCount);
        Assert.Equal(1, current.Coverage.KnownGapCount);
        Assert.Equal(ActivityCoverageState.Partial, current.Coverage.State);
    }

    [Fact]
    public async Task ReaderAdmission_IsBounded_QueueCancellationReleasesCapacity_AndDisposalClosesReaders()
    {
        await using var db = await Database.Create();
        var readersReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        db.Repository.ActivityReadSnapshotReady = async token =>
        {
            if (Interlocked.Increment(ref active) == SqliteIntelligenceHistoryRepository.MaxActivityReaders) readersReady.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        using var firstCancel = new CancellationTokenSource();
        var first = db.Repository.GetMapActivityAsync(Range, firstCancel.Token);
        var second = db.Repository.GetMapActivityAsync(Range);
        await readersReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var queuedCancel = new CancellationTokenSource();
        var queued = db.Repository.GetMapActivityAsync(Range, queuedCancel.Token);
        queuedCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(2, Volatile.Read(ref active));
        firstCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var thirdReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Repository.ActivityReadSnapshotReady = async token =>
        {
            thirdReady.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var third = db.Repository.GetMapActivityAsync(Range);
        await thirdReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await db.Repository.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => db.Repository.GetMapActivityAsync(Range));
        // No reader handles survive disposal: the fixture can be opened exclusively.
        using var exclusive = new FileStream(db.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task QueryDeadline_CancelsSlowReads_AndUninitializedQueriesDoNotCreateAReadDatabase()
    {
        await using var db = await Database.Create();
        await using var uninitialized = new SqliteIntelligenceHistoryRepository(Path.Combine(db.Directory, "uninitialized.db"), TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => uninitialized.GetMapActivityAsync(Range));
        Assert.False(File.Exists(uninitialized.DatabasePath));

        await using var bounded = new SqliteIntelligenceHistoryRepository(db.Path, TimeSpan.FromSeconds(1), activityQueryTimeout: TimeSpan.FromMilliseconds(100));
        await bounded.InitializeAsync();
        bounded.ActivityReadSnapshotReady = token => Task.Delay(Timeout.Infinite, token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bounded.GetMapActivityAsync(Range));
        bounded.ActivityReadSnapshotReady = null;
        Assert.Equal(0, (await bounded.GetMapActivityAsync(Range)).Total.KillmailCount);
    }

    [Fact]
    public async Task QueryDeadline_InterruptsNativeSqliteExecution_AndDisposesTheConnection()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1)]);
        await using var bounded = new SqliteIntelligenceHistoryRepository(db.Path, TimeSpan.FromSeconds(1), activityQueryTimeout: TimeSpan.FromMilliseconds(150));
        await bounded.InitializeAsync();
        // Exercise interruption inside SQLite's VM, rather than only in the async test seam.
        await db.Sql("""
            ALTER TABLE SystemActivity RENAME TO OriginalSystemActivity;
            CREATE VIEW SystemActivity AS
                WITH RECURSIVE work(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM work WHERE n<100000000)
                SELECT original.* FROM OriginalSystemActivity original CROSS JOIN work;
            """);
        var timer = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bounded.GetMapActivityAsync(Range));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), "Native SQLite work did not stop within the query budget.");
        await db.Sql("DROP VIEW SystemActivity; ALTER TABLE OriginalSystemActivity RENAME TO SystemActivity;");
        Assert.Equal(1, (await bounded.GetMapActivityAsync(Range)).Total.KillmailCount);
    }

    [Fact]
    public async Task QueryShapes_AreIndependent_DeterministicallyOrdered_AndKeepExactDecimalRankings()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([
            Observation(1, 7, value: 9m), Observation(2, 2, value: 100m), Observation(3, 3, value: 100m),
            Observation(4, 7, Epoch.AddMinutes(5), 0.0000000000000001m), Observation(5, 11, value: null)]);
        var summaries = await db.Repository.GetMapActivityAsync(Range);
        Assert.Equal(new long[] { 2, 3, 7, 11 }, summaries.Systems.Select(s => s.SolarSystemId));
        Assert.Equal(209.0000000000000001m, summaries.Total.KnownIskDestroyed);
        Assert.Empty(summaries.Buckets);
        Assert.Empty(summaries.ShipLosses);
        var multi = await db.Repository.GetActivityAsync(Range, [7, 2, 6, 7]);
        Assert.Equal(new long[] { 2, 6, 7 }, multi.Systems.Select(s => s.SolarSystemId));
        Assert.Equal(0, multi.Systems[1].Metrics.KillmailCount);
        Assert.Equal(2, (await db.Repository.GetSystemActivityAsync(7, Range)).Total.KillmailCount);
        Assert.Equal(new long[] { 2, 3, 7 }, (await db.Repository.GetTopSystemsAsync(Range, ActivityRanking.KnownIskDestroyed, 3)).Systems.Select(s => s.SolarSystemId));
        Assert.Equal(new long[] { 7, 2, 3 }, (await db.Repository.GetTopSystemsAsync(Range, ActivityRanking.KillmailCount, 3)).Systems.Select(s => s.SolarSystemId));
        Assert.Equal(5, (await db.Repository.GetShipLossesAsync(Range)).ShipLosses.Sum(s => s.LossCount));

        // A missing breakdown table must not affect unrelated query shapes.
        await db.Sql("DROP TABLE ShipLossActivity;");
        Assert.Equal(5, (await db.Repository.GetMapActivityAsync(Range)).Total.KillmailCount);
        Assert.Single((await db.Repository.GetTopSystemsAsync(Range, ActivityRanking.KillmailCount, 1)).Systems);
        Assert.Single((await db.Repository.GetTopSystemsAsync(Range, ActivityRanking.KnownIskDestroyed, 1)).Systems);
        Assert.Equal(2, (await db.Repository.GetTimeSeriesAsync(7, Range)).Buckets.Count);
    }

    [Fact]
    public async Task TimeSeries_RequiresOneSystem_AndIsBoundedToThirtyDays()
    {
        await using var db = await Database.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => db.Repository.GetActivityAsync(Range, includeBuckets: true));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Repository.GetActivityAsync(Range, [1, 2], true));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Repository.GetTimeSeriesAsync(1, new(Epoch, Epoch.AddDays(31))));
        await db.Sql($"""
            WITH RECURSIVE buckets(n) AS (SELECT 0 UNION ALL SELECT n+1 FROM buckets WHERE n<8639)
            INSERT INTO SystemActivity
            SELECT 1,{Epoch.UtcTicks}+n*{ActivityRange.BucketSize.Ticks},1,0,'1',1,0,0,
                {Epoch.UtcTicks}+n*{ActivityRange.BucketSize.Ticks},{Epoch.UtcTicks}+n*{ActivityRange.BucketSize.Ticks} FROM buckets;
            """);
        var series = await db.Repository.GetTimeSeriesAsync(1, new(Epoch, Epoch.AddDays(30)));
        Assert.Equal(SqliteIntelligenceHistoryRepository.MaxTimeSeriesBuckets, series.Buckets.Count);
        Assert.Equal(8640, series.Total.KillmailCount);
        Assert.True(series.Buckets.Zip(series.Buckets.Skip(1)).All(pair => pair.First.BucketStartUtc < pair.Second.BucketStartUtc));
    }

    [Fact]
    public async Task ExactDecimalQueries_AcceptScientificNotationFromInvariantG29Storage()
    {
        await using var db = await Database.Create();
        const decimal tiny = 0.0000000000000000000000000001m;
        await db.Repository.InsertBatchAsync([Observation(1, value: tiny), Observation(2, value: tiny)]);
        Assert.Equal(tiny * 2, (await db.Repository.GetSystemActivityAsync(1, Range)).Total.KnownIskDestroyed);
        Assert.Equal(tiny * 2, (await db.Repository.GetTimeSeriesAsync(1, Range)).Buckets[0].Metrics.KnownIskDestroyed);
        Assert.Equal(tiny * 2, (await db.Repository.GetTopSystemsAsync(Range, ActivityRanking.KnownIskDestroyed, 1)).Systems[0].Metrics.KnownIskDestroyed);
    }

    [Fact]
    public async Task HostedCatchUp_DeterministicFailurePreservesPendingRaw_AndDoesNotRetryEveryCycle()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1, value: 1000000000000000000000000000m)]);
        await db.Sql($"""
            INSERT INTO Killmail(KillmailId,Source,SolarSystemId,OccurredAtUtcTicks,ReceivedAtUtcTicks,
                VictimShipTypeId,TotalValue,AttackerCount,ActivityAggregated)
            VALUES(2,'R2Z2',1,{Epoch.UtcTicks},{Epoch.UtcTicks},587,'0.01',0,0);
            """);
        using var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        using var service = new IntelligencePersistenceHostedService(db.Repository, feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance, new IntelligencePersistenceOptions
            {
                RecoverySpoolPath = Path.Combine(db.Directory, "spool"),
                MaxBatchWait = TimeSpan.FromMilliseconds(10), CoverageCheckpointInterval = TimeSpan.FromMilliseconds(10),
                RetentionPeriod = TimeSpan.FromDays(36500), AggregateRetentionPeriod = TimeSpan.FromDays(36500), GapRetentionPeriod = TimeSpan.FromDays(36500)
            });
        await service.StartAsync(CancellationToken.None);
        await feed.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntil(async () => (await db.Repository.GetCollectionGapsAsync(20)).Any(g => g.Detail?.Contains("catch-up stopped") == true));
            feed.Publish(Observation(3, system: 2));
            await WaitUntil(async () => await db.Repository.GetKillmailAsync(3) is not null);
            Assert.NotNull(await db.Repository.GetKillmailAsync(2));
            var result = await db.Repository.GetSystemActivityAsync(1, Range);
            Assert.True(result.Coverage.CatchUpPending);
            Assert.Equal(1, result.Total.KillmailCount);
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        }
        Assert.Single(await db.Repository.GetCollectionGapsAsync(20), g => g.Detail?.Contains("catch-up stopped") == true);
        Assert.Equal(1, await db.Scalar("SELECT COUNT(*) FROM Killmail WHERE ActivityAggregated=0;"));
    }

    [Fact]
    public async Task HostedPipeline_ArithmeticOverflowRecordsPermanentFailure_WithoutSpoolingOrRecounting()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1, value: 1000000000000000000000000000m)]);
        using var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        using var service = new IntelligencePersistenceHostedService(db.Repository, feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance, new IntelligencePersistenceOptions
            {
                RecoverySpoolPath = Path.Combine(db.Directory, "spool"), BatchSize = 2,
                MaxBatchWait = TimeSpan.FromMilliseconds(10), CoverageCheckpointInterval = TimeSpan.FromMilliseconds(20),
                RetentionPeriod = TimeSpan.FromDays(36500), AggregateRetentionPeriod = TimeSpan.FromDays(36500), GapRetentionPeriod = TimeSpan.FromDays(36500)
            });
        await service.StartAsync(CancellationToken.None);
        await feed.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(Observation(2, value: 0.01m));
            feed.Publish(Observation(3, system: 2));
            await WaitUntil(async () => await db.Repository.GetKillmailAsync(3) is not null &&
                (await db.Repository.GetCollectionGapsAsync(20)).Any(g => g.KillmailId == 2 && g.Kind == IntelligenceCollectionGapKind.WriteFailure));
            Assert.Null(await db.Repository.GetKillmailAsync(2));
            Assert.Equal(1, (await db.Repository.GetSystemActivityAsync(1, Range)).Total.KillmailCount);
            Assert.Equal(1000000000000000000000000000m, (await db.Repository.GetSystemActivityAsync(1, Range)).Total.KnownIskDestroyed);
            Assert.Equal(0, service.RecoverySpool.ActionableCount);
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        }
        Assert.Single(await db.Repository.GetCollectionGapsAsync(20), g => g.KillmailId == 2 && g.Kind == IntelligenceCollectionGapKind.WriteFailure);
        Assert.Equal(0, service.RecoverySpool.PendingCount);
    }
}
