using System.Diagnostics;
using System.Text.Json.Nodes;
using Hisa.Core.Models.Intelligence;
using Hisa.Data.Database;
using Hisa.Services.Intelligence;
using Hisa.Services.Background;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Hisa.Services.Tests;

[Collection("Historical activity")]
public sealed partial class IntelligenceActivityTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Epoch = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static ActivityRange Range => new(Epoch, Epoch.AddHours(1));

    [Fact]
    public void Buckets_AreUtc_HalfOpen_AndAdjacentCustomRangesDoNotOverlap()
    {
        Assert.Equal(Epoch, ActivityRange.BucketStart(Epoch.AddMinutes(4).AddSeconds(59)));
        Assert.Equal(Epoch.AddMinutes(5), ActivityRange.BucketStart(Epoch.AddMinutes(5)));
        Assert.Equal(Epoch, ActivityRange.BucketStart(Epoch.ToOffset(TimeSpan.FromHours(-6))));
        var left = new ActivityRange(Epoch, Epoch.AddMinutes(5)).Effective();
        var right = new ActivityRange(Epoch.AddMinutes(5), Epoch.AddMinutes(15)).Effective();
        Assert.Equal(left.ToUtc, right.FromUtc);
        Assert.Equal(Epoch, ActivityRange.Last(TimeSpan.FromMinutes(15), Epoch.AddMinutes(2)).ToUtc);
        foreach (var window in ActivityRange.CommonWindows)
            Assert.Equal(window, ActivityRange.Last(window, Epoch).Effective().ToUtc - ActivityRange.Last(window, Epoch).Effective().FromUtc);
        Assert.Throws<ArgumentException>(() => new ActivityRange(Epoch, Epoch.AddDays(31)).Effective());
    }

    [Fact]
    public async Task Aggregates_SeparateIds_Systems_NullMoney_AndConservativePvp()
    {
        await using var db = await Database.Create();
        var a = Observation(1, value: 1234567890123.4567m);
        await db.Repository.InsertBatchAsync([a, Observation(2, value: 0.0001m), Observation(3, value: null),
            Observation(4, system: 2, value: 10), Observation(5, occurred: Epoch.AddMinutes(5), value: 1)]);
        var duplicate = await db.Repository.InsertBatchAsync([a]);
        Assert.Equal(1, duplicate.Duplicates);
        var result = await db.Repository.GetActivityAsync(Range, [1, 2, 3]);
        Assert.Equal(5, result.Total.KillmailCount);
        Assert.Equal(1234567890134.4568m, result.Total.KnownIskDestroyed);
        Assert.Equal(4, result.Total.KnownValueCount);
        Assert.Equal(1, result.Total.UnknownValueCount);
        Assert.Equal(5, result.Total.ConfirmedPvpKills);
        Assert.Equal(0, result.Total.OtherKills);
        Assert.Empty(result.Buckets);
        var series = await db.Repository.GetTimeSeriesAsync(1, Range);
        Assert.Equal(2, series.Buckets.Count);
        Assert.Equal(3, series.Buckets.Single(b => b.BucketStartUtc == Epoch).Metrics.KillmailCount);
        Assert.Equal(0, result.Systems.Single(s => s.SolarSystemId == 3).Metrics.KillmailCount);
        Assert.Equal(ActivityCoverageState.Unknown, result.Coverage.State);
        Assert.Equal(1, (await db.Repository.GetTopSystemsAsync(Range, ActivityRanking.KillmailCount, 1)).Systems[0].SolarSystemId);
        Assert.Equal(1, (await db.Repository.GetTopSystemsAsync(Range, ActivityRanking.KnownIskDestroyed, 1)).Systems[0].SolarSystemId);
        Assert.Empty(result.ShipLosses);
        Assert.Equal(5, (await db.Repository.GetShipLossesAsync(Range)).ShipLosses.Sum(s => s.LossCount));

        await db.Repository.InsertBatchAsync([
            Observation(6) with { Attackers = [new KillmailAttacker { CorporationId = 99 }] },
            Observation(7) with { Victim = new KillmailVictim { ShipTypeId = 587 }, Attackers = [new KillmailAttacker { CharacterId = 10 }] },
            Observation(8) with { Attackers = [new KillmailAttacker { CharacterId = 100 }] }]);
        Assert.Equal(3, (await db.Repository.GetActivityAsync(Range)).Total.OtherKills);
        Assert.Equal(3, (await db.Repository.GetActivityAsync(new(Epoch, Epoch.AddMinutes(5)))).Total.OtherKills);
        Assert.Equal(1, (await db.Repository.GetActivityAsync(new(Epoch.AddMinutes(5), Epoch.AddMinutes(10)))).Total.KillmailCount);
    }

    [Fact]
    public async Task MoneyPrecisionLoss_IsRejectedAtomicallyRatherThanRounded()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1, value: 1000000000000000000000000000m)]);
        await Assert.ThrowsAsync<OverflowException>(() => db.Repository.InsertBatchAsync([Observation(2, value: 0.01m)]));
        Assert.Null(await db.Repository.GetKillmailAsync(2));
        Assert.Equal(1, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
    }

    [Fact]
    public async Task BatchFailure_AndCancellation_RollBackRawAndAggregates()
    {
        await using var db = await Database.Create();
        db.Repository.AfterKillmailInsert = id => { if (id == 2) throw new InvalidOperationException("interrupted"); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Repository.InsertBatchAsync([Observation(1), Observation(2)]));
        Assert.Equal(0, (await db.Repository.GetHealthAsync()).KillmailCount);
        Assert.Equal(0, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
        db.Repository.AfterKillmailInsert = null;
        using var cancel = new CancellationTokenSource();
        db.Repository.AfterKillmailInsert = _ => cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Repository.InsertBatchAsync([Observation(1)], cancel.Token));
        Assert.Equal(0, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Repository.CatchUpActivityAsync(10, cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Repository.GetActivityAsync(Range, cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task LateArrival_AndReplayAfterDetailRetention_DoNotDoubleCount()
    {
        await using var db = await Database.Create();
        var kill = Observation(1) with { ReceivedAtUtc = Epoch.AddDays(2) };
        await db.Repository.InsertBatchAsync([kill]);
        Assert.Equal(Epoch, (await db.Repository.GetTimeSeriesAsync(1, Range)).Buckets[0].BucketStartUtc);
        Assert.Equal(1, await db.Repository.DeleteOccurredBeforeAsync(Epoch.AddDays(1), 10));
        Assert.Equal(1, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
        var spool = new IntelligenceRecoverySpool(Path.Combine(db.Directory, "spool"), 10);
        Assert.True(spool.TryAppend(kill));
        foreach (var entry in spool.ReadBatch(10).Entries)
        {
            await db.Repository.InsertBatchAsync([entry.Observation!]);
            await db.Repository.InsertBatchAsync([entry.Observation!]);
            spool.Acknowledge(entry.Path);
        }
        Assert.Equal(1, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
    }

    [Fact]
    public async Task UpgradeV2_CatchUpIsBounded_ResumesAcrossRestart_AndProtectsRetention()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync(Enumerable.Range(1, 7).Select(i => Observation(i)).ToArray());
        await db.Repository.DisposeAsync();
        // Downgrade the fixture to the exact v2 shape while preserving existing raw records.
        await db.Sql("""
            DROP TABLE ShipLossActivity; DROP TABLE SystemActivity; DROP TABLE ActivityDedup; DROP TABLE CollectionSession;
            DROP INDEX IX_Killmail_ActivityPending;
            ALTER TABLE Killmail DROP COLUMN ActivityAggregated;
            UPDATE SchemaVersion SET Version=2;
            """);
        db.Reopen();
        await db.Repository.InitializeAsync();
        Assert.Equal(3, (await db.Repository.GetHealthAsync()).SchemaVersion);
        Assert.True((await db.Repository.GetActivityAsync(Range)).Coverage.CatchUpPending);
        Assert.Equal(0, await db.Repository.DeleteOccurredBeforeAsync(Epoch.AddDays(1), 100));
        Assert.Equal(2, await db.Repository.CatchUpActivityAsync(2));
        await db.Repository.DisposeAsync();
        db.Reopen();
        await db.Repository.InitializeAsync();
        await db.Repository.InsertBatchAsync([Observation(20)]);
        Assert.Equal(3, await db.Repository.CatchUpActivityAsync(3));
        Assert.Equal(2, await db.Repository.CatchUpActivityAsync(3));
        Assert.Equal(0, await db.Repository.CatchUpActivityAsync(3));
        var result = await db.Repository.GetActivityAsync(Range);
        Assert.False(result.Coverage.CatchUpPending);
        Assert.Equal(8, result.Total.KillmailCount);
        Assert.Equal(8, await db.Repository.DeleteOccurredBeforeAsync(Epoch.AddDays(1), 100));
        Assert.Equal(8, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
    }

    [Fact]
    public async Task AggregateRetention_IsBounded_AndLateExpiredReplayCannotResurrectHistory()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1), Observation(2, system: 2), Observation(3, occurred: Epoch.AddDays(1))]);
        Assert.Equal(1, await db.Repository.DeleteActivityBeforeAsync(Epoch.AddHours(1), 1));
        Assert.Equal(1, await db.Repository.DeleteActivityBeforeAsync(Epoch.AddHours(1), 1));
        Assert.Equal(0, await db.Repository.DeleteActivityBeforeAsync(Epoch.AddHours(1), 1));
        Assert.Equal(0, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
        await db.Repository.InsertBatchAsync([Observation(99)]);
        Assert.Equal(0, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
        Assert.True((await db.Repository.GetActivityAsync(Range)).Coverage.AggregateHistoryExpired);
        Assert.Equal(1, (await db.Repository.GetActivityAsync(new(Epoch.AddDays(1), Epoch.AddDays(1).AddHours(1)))).Total.KillmailCount);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.Repository.CatchUpActivityAsync(1001));
    }

    [Fact]
    public async Task Coverage_SeparatesObservedZero_Offline_Interrupted_AndReceptionGaps()
    {
        await using var db = await Database.Create();
        Assert.Equal(ActivityCoverageState.Unknown, (await db.Repository.GetActivityAsync(Range, [1])).Coverage.State);
        await db.Repository.StartCollectionSessionAsync("first", Epoch);
        await db.Repository.HeartbeatCollectionAsync("first", Epoch.AddHours(1));
        var observed = await db.Repository.GetActivityAsync(Range, [1]);
        Assert.Equal(ActivityCoverageState.Observed, observed.Coverage.State);
        Assert.False(observed.Coverage.SourceCompletenessGuaranteed);
        Assert.Equal(0, observed.Total.KillmailCount);
        await db.Repository.EndCollectionSessionAsync("first", Epoch.AddHours(1));
        await db.Repository.StartCollectionSessionAsync("crashed", Epoch.AddHours(2));
        await db.Repository.HeartbeatCollectionAsync("crashed", Epoch.AddHours(3));
        await db.Repository.DisposeAsync(); db.Reopen(); await db.Repository.InitializeAsync();
        await db.Repository.StartCollectionSessionAsync("restart", Epoch.AddHours(4));
        await db.Repository.HeartbeatCollectionAsync("restart", Epoch.AddHours(5));
        Assert.Equal(ActivityCoverageState.Partial, (await db.Repository.GetActivityAsync(new(Epoch, Epoch.AddHours(5)))).Coverage.State);
        Assert.Equal(ActivityCoverageState.Unknown, (await db.Repository.GetActivityAsync(new(Epoch.AddHours(3), Epoch.AddHours(4)))).Coverage.State);
        await db.Repository.RecordGapsAsync([new IntelligenceCollectionGap { Kind = "SkippedSequence", RecordedAtUtc = Epoch.AddMinutes(30), Count = 2 }]);
        var gap = (await db.Repository.GetActivityAsync(Range)).Coverage;
        Assert.Equal(ActivityCoverageState.Partial, gap.State);
        Assert.Equal(2, gap.KnownGapCount);
        Assert.Equal(TimeSpan.FromHours(1), gap.ObservedDuration);
        // A reception gap does not change the occurrence-time counters.
        Assert.Equal(0, (await db.Repository.GetActivityAsync(Range)).Total.KillmailCount);
        await db.Repository.DeleteGapsRecordedBeforeAsync(Epoch.AddDays(1), 100);
        var afterGapExpiry = (await db.Repository.GetActivityAsync(Range)).Coverage;
        Assert.Equal(0, afterGapExpiry.KnownGapCount);
        Assert.True(afterGapExpiry.GapHistoryExpired);
        Assert.Equal(ActivityCoverageState.Partial, afterGapExpiry.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectionWorker_UsesSourceEvidence_AndSplitsOnInterruptions(bool explicitInterruption)
    {
        await using var db = await Database.Create();
        using var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        using var service = new IntelligencePersistenceHostedService(db.Repository, feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance, new IntelligencePersistenceOptions
            {
                RecoverySpoolPath = Path.Combine(db.Directory, "spool"),
                MaxBatchWait = TimeSpan.FromMilliseconds(5), CoverageCheckpointInterval = TimeSpan.FromMilliseconds(10),
                RetentionPeriod = TimeSpan.FromDays(36500), AggregateRetentionPeriod = TimeSpan.FromDays(36500),
                GapRetentionPeriod = TimeSpan.FromDays(36500),
                CollectionHeartbeatMaxGap = explicitInterruption ? TimeSpan.FromMinutes(20) : TimeSpan.FromMinutes(10)
            });
        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(ActivityCoverageState.Unknown, (await db.Repository.GetActivityAsync(Range)).Coverage.State);
            service.CollectionHeartbeat(Epoch);
            await WaitUntil(async () => await db.Scalar("SELECT COUNT(*) FROM CollectionSession;") == 1);
            service.CollectionHeartbeat(Epoch.AddMinutes(10));
            await WaitUntil(async () => (await db.Repository.GetActivityAsync(new(Epoch, Epoch.AddMinutes(5)))).Coverage.State == ActivityCoverageState.Observed);
            if (explicitInterruption) service.CollectionInterrupted(Epoch.AddMinutes(11));
            var next = explicitInterruption ? Epoch.AddMinutes(15) : Epoch.AddMinutes(25);
            service.CollectionHeartbeat(next);
            await WaitUntil(async () => await db.Scalar("SELECT COUNT(*) FROM CollectionSession;") == 2);
            service.CollectionHeartbeat(next.AddMinutes(5));
            await WaitUntil(async () => (await db.Repository.GetActivityAsync(new(next, next.AddMinutes(5)))).Coverage.State == ActivityCoverageState.Observed);
            Assert.Equal(ActivityCoverageState.Partial, (await db.Repository.GetActivityAsync(new(Epoch, Epoch.AddMinutes(30)))).Coverage.State);
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Assert.Equal(0, await db.Scalar("SELECT COUNT(*) FROM CollectionSession WHERE EndUtcTicks IS NULL;"));
    }

    private static async Task WaitUntil(Func<Task<bool>> predicate)
    {
        var deadline = Stopwatch.StartNew();
        while (!await predicate())
        {
            Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(3), "Collection evidence was not persisted.");
            await Task.Delay(10);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("{}")]
    [InlineData("[42]")]
    public async Task RecoverySpool_UnexpectedAttackerStructuresAreCorrupt(string attackers)
    {
        await using var db = await Database.Create();
        var spool = new IntelligenceRecoverySpool(Path.Combine(db.Directory, "spool"), 10);
        Assert.True(spool.TryAppend(Observation(1)));
        var path = Path.Combine(spool.DirectoryPath, "1.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        document["attackers"] = JsonNode.Parse(attackers);
        await File.WriteAllTextAsync(path, document.ToJsonString());
        Assert.Equal(SpoolEntryStatus.Corrupt, Assert.Single(spool.ReadBatch(10).Entries).Status);
    }

    [Fact]
    public async Task RecoverySpool_RealFileAtDirectoryPathIsAnAccessFailure()
    {
        await using var db = await Database.Create();
        var path = Path.Combine(db.Directory, "not-a-directory");
        await File.WriteAllTextAsync(path, "blocked");
        var spool = new IntelligenceRecoverySpool(path, 10);
        Assert.False(spool.TryRefresh());
        Assert.True(spool.ReadBatch(10).AccessFailed);
        var nested = new IntelligenceRecoverySpool(Path.Combine(path, "child"), 10);
        Assert.False(nested.TryRefresh());
        Assert.True(nested.ReadBatch(10).AccessFailed);
        var missing = new IntelligenceRecoverySpool(Path.Combine(db.Directory, "missing", "child"), 10);
        Assert.True(missing.TryRefresh());
        Assert.False(missing.ReadBatch(10).AccessFailed);
    }

    [Fact]
    public async Task SyntheticPerformance_QueriesTenThousandKills_AndReportsAggregateGrowth()
    {
        await using var db = await Database.Create();
        await db.Sql("PRAGMA wal_checkpoint(TRUNCATE);");
        var before = new FileInfo(db.Path).Length;
        var stopwatch = Stopwatch.StartNew();
        for (var batch = 0; batch < 100; batch++)
            await db.Repository.InsertBatchAsync(Enumerable.Range(batch*100+1, 100)
                .Select(i => Observation(i, i%100+1, Epoch.AddMinutes(i%360), 123.4567m)).ToArray());
        var writeMs = stopwatch.Elapsed.TotalMilliseconds;
        var latencies = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            stopwatch.Restart();
            var result = await db.Repository.GetActivityAsync(new(Epoch, Epoch.AddHours(6)));
            latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            Assert.Equal(10000, result.Total.KillmailCount);
            Assert.Equal(1234567m, result.Total.KnownIskDestroyed);
        }
        await db.Sql("PRAGMA wal_checkpoint(TRUNCATE);");
        var growth = new FileInfo(db.Path).Length-before;
        // Measure the retained aggregates and dedup index after detail expiry and compaction.
        while (await db.Repository.DeleteOccurredBeforeAsync(Epoch.AddDays(1), 1000) > 0) { }
        await db.Sql("VACUUM;");
        await db.Sql("PRAGMA wal_checkpoint(TRUNCATE);");
        var aggregateBytes = new FileInfo(db.Path).Length-before;
        latencies.Sort();
        output.WriteLine($"10,000 kills / 100 systems / 6 hours: ingest={writeMs:F1} ms; all-system query median={latencies[10]:F2} ms; p95={latencies[18]:F2} ms; database growth={growth} bytes; aggregate+dedup pages={aggregateBytes} bytes.");
        Assert.True(aggregateBytes > 0);
    }

    private static KillmailObservation Observation(long id, long system = 1, DateTimeOffset? occurred = null, decimal? value = 1m) => new()
    {
        KillmailId = id, Source = "R2Z2", SolarSystemId = system, OccurredAtUtc = occurred ?? Epoch,
        ReceivedAtUtc = Epoch.AddHours(1), Victim = new KillmailVictim { CharacterId = 100, ShipTypeId = 587 },
        Attackers = [new KillmailAttacker { CharacterId = 200 }], TotalValue = value
    };

    private sealed class Database : IAsyncDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hisa-activity-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "history.db");
        public SqliteIntelligenceHistoryRepository Repository { get; private set; } = null!;
        public static async Task<Database> Create()
        {
            var db = new Database(); db.Reopen(); await db.Repository.InitializeAsync(); return db;
        }
        public void Reopen() => Repository = new(Path, TimeSpan.FromSeconds(2));
        public async Task<long> Scalar(string sql)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand(); command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        public async Task Sql(string sql) => await Scalar(sql);
        public async ValueTask DisposeAsync()
        {
            await Repository.DisposeAsync();
            // Windows can briefly retain a file lock after SQLite closes its handles.
            // Retry cleanup, but fail if the lock persists rather than hiding a leak.
            for (var attempt = 0; ; attempt++)
            {
                try { System.IO.Directory.Delete(Directory, recursive: true); break; }
                catch (IOException) when (attempt < 5) { await Task.Delay(50); }
            }
        }
    }
}
