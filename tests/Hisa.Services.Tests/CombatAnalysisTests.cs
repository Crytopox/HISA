using System.Diagnostics;
using Hisa.Core.Models.Intelligence;
using Hisa.Data.Database;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;
using Hisa.Services.Intelligence;
using Hisa.Services.Background;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed partial class IntelligenceActivityTests
{
    private static readonly CombatDetectionOptions CombatOptions = new();
    [Fact]
    public async Task Combat_HostedPersistenceAndAnalysisPipeline_CompletesAndCancelsCleanly()
    {
        await using var db = await Database.Create();
        var now = DateTimeOffset.UtcNow;
        var from = ActivityRange.BucketStart(now.AddMinutes(-10));
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var options = new IntelligencePersistenceOptions { RecoverySpoolPath = Path.Combine(db.Directory, "spool"), MaxBatchWait = TimeSpan.FromMilliseconds(10) };
        using var persistence = new IntelligencePersistenceHostedService(db.Repository, feed, NullLogger<IntelligencePersistenceHostedService>.Instance, options);
        using var analysis = new CombatAnalysisHostedService(db.Repository, db.Repository, Options.Create(options), Options.Create(CombatOptions), NullLogger<CombatAnalysisHostedService>.Instance);
        await feed.StartAsync(CancellationToken.None); await persistence.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(Observation(1, occurred: from)); feed.Publish(Observation(2, occurred: from.AddMinutes(1)));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (persistence.GetDiagnostics().Committed < 2) await Task.Delay(10, deadline.Token);
            await analysis.StartAsync(CancellationToken.None);
            while ((await db.Repository.GetCombatEventsAsync(new(new(from, from.AddMinutes(15))), deadline.Token)).Events.Count == 0)
                await Task.Delay(20, deadline.Token);
            Assert.Equal(2, persistence.GetDiagnostics().Committed); Assert.Equal(0, persistence.GetDiagnostics().PermanentlyRejected);
            Assert.Equal(2, Assert.Single((await db.Repository.GetCombatEventsAsync(new(new(from, from.AddMinutes(15))))).Events).KillmailCount);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await analysis.StopAsync(stop.Token); await feed.StopAsync(stop.Token); await persistence.StopAsync(stop.Token); feed.Dispose();
        }
    }
    private static async Task DrainCombat(Database db, DateTimeOffset? now = null)
    {
        for (var iteration = 0; iteration < 100; iteration++)
            if (await db.Repository.ProcessCombatAsync(now ?? Epoch.AddHours(8), TimeSpan.FromDays(30), CombatOptions) == 0) return;
        Assert.Fail("Unexpected combat work backlog.");
    }

    [Fact]
    public async Task Combat_LateBridgeReplacesSummariesAndMembershipAtomically_DuplicateAndRestartAreIdempotent()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1, occurred: Epoch), Observation(2, occurred: Epoch.AddMinutes(1)),
            Observation(3, occurred: Epoch.AddMinutes(20)), Observation(4, occurred: Epoch.AddMinutes(21))]);
        await DrainCombat(db);
        var before = await db.Repository.GetCombatEventsAsync(new(Range));
        Assert.Equal(2, before.Events.Count); Assert.False(before.Pending);
        await db.Repository.InsertBatchAsync([Observation(5, occurred: Epoch.AddMinutes(10))]);
        Assert.True((await db.Repository.GetCombatEventsAsync(new(Range))).Pending);
        await DrainCombat(db);
        var merged = Assert.Single((await db.Repository.GetCombatEventsAsync(new(Range))).Events);
        Assert.Equal(new long[] { 1, 2, 5, 3, 4 }, merged.MemberKillmailIds);
        Assert.Equal(5m, merged.KnownIskDestroyed);
        Assert.Null(await db.Repository.GetCombatEventAsync(before.Events[1].Id));
        await db.Repository.InsertBatchAsync([Observation(5)]);
        Assert.Equal(0, await db.Scalar("SELECT COUNT(*) FROM CombatDirty;"));
        await db.Repository.DisposeAsync(); db.Reopen(); await db.Repository.InitializeAsync();
        await DrainCombat(db);
        Assert.Equal(merged.Id, Assert.Single((await db.Repository.GetCombatEventsAsync(new(Range))).Events).Id);
        var detail = await db.Repository.GetCombatEventAsync(merged.Id);
        Assert.Equal(merged.MemberKillmailIds, detail!.MemberKillmailIds);
    }

    [Fact]
    public async Task Combat_LateEarlierLossCanSplitPriorMembership_WithNoOrphanedSummary()
    {
        await using var db = await Database.Create();
        var options = CombatOptions with { MaximumEngagementDuration = TimeSpan.FromMinutes(20) };
        await db.Repository.InsertBatchAsync(Enumerable.Range(1, 5).Select(i => Observation(i, occurred: Epoch.AddMinutes(i * 10))).ToArray());
        while (await db.Repository.ProcessCombatAsync(Epoch.AddHours(8), TimeSpan.FromDays(30), options) > 0) { }
        var before = (await db.Repository.GetCombatEventsAsync(new(Range))).Events;
        Assert.Equal(new long[] { 1, 2, 3 }, before[0].MemberKillmailIds);
        await db.Repository.InsertBatchAsync([Observation(6, occurred: Epoch.AddMinutes(5))]);
        while (await db.Repository.ProcessCombatAsync(Epoch.AddHours(8), TimeSpan.FromDays(30), options) > 0) { }
        var after = (await db.Repository.GetCombatEventsAsync(new(Range))).Events.Where(e => e.Classification != EngagementClassification.PossibleCampActivity).ToArray();
        Assert.Equal(2, after.Length);
        Assert.Equal(new long[] { 6, 1, 2 }, after[0].MemberKillmailIds);
        Assert.Equal(new long[] { 3, 4, 5 }, after[1].MemberKillmailIds);
        Assert.Null(await db.Repository.GetCombatEventAsync(before[1].Id));
        Assert.Equal(6, after.SelectMany(e => e.MemberKillmailIds).Distinct().Count());
    }

    [Fact]
    public async Task Combat_MigrationV3SeedsOnlyExistingDetail_AndRetainedSummariesSurviveRawExpiry()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1), Observation(2, occurred: Epoch.AddMinutes(1))]);
        await db.Repository.DisposeAsync();
        await db.Sql("DROP TABLE CombatEvent; DROP TABLE CombatDirty; DROP TABLE CombatAnalysisIssue; UPDATE SchemaVersion SET Version=3;");
        db.Reopen(); await db.Repository.InitializeAsync();
        Assert.Equal(4, (await db.Repository.GetHealthAsync()).SchemaVersion);
        Assert.Equal(1, await db.Scalar("SELECT COUNT(*) FROM CombatDirty;"));
        await DrainCombat(db);
        var summary = Assert.Single((await db.Repository.GetCombatEventsAsync(new(Range))).Events);
        await db.Repository.DeleteOccurredBeforeAsync(Epoch.AddHours(1), 100);
        Assert.Null(await db.Repository.GetKillmailAsync(1));
        // An arrival in an expired, partially deleted partition cannot replace a retained summary with truncated data.
        await db.Repository.InsertBatchAsync([Observation(3, occurred: Epoch.AddMinutes(2))]);
        await DrainCombat(db, Epoch.AddDays(3));
        var retained = Assert.Single((await db.Repository.GetCombatEventsAsync(new(Range))).Events);
        Assert.Equal(summary.MemberKillmailIds, retained.MemberKillmailIds);
        Assert.True((await db.Repository.GetCombatEventsAsync(new(Range))).Pending);
        Assert.True(retained.RawDetailsExpired(Epoch.AddDays(3)));
        await DrainCombat(db, Epoch.AddDays(31));
        Assert.Empty((await db.Repository.GetCombatEventsAsync(new(Range))).Events);
    }

    [Fact]
    public async Task Combat_QueriesAreBoundedHalfOpenStable_AndCampDoesNotDoubleCountRanking()
    {
        await using var db = await Database.Create();
        var rows = Enumerable.Range(0, 8).Select(i => Observation(i + 1, occurred: Epoch.AddMinutes(i * 16), value: i == 0 ? null : i));
        await db.Repository.InsertBatchAsync(rows.Concat([Observation(20, 2), Observation(21, 2, Epoch.AddMinutes(1)),
            Observation(30, 3), Observation(31, 3, Epoch.AddMinutes(1))]).ToArray());
        await DrainCombat(db);
        var wide = new ActivityRange(Epoch, Epoch.AddHours(3));
        var camp = Assert.Single((await db.Repository.GetCombatEventsAsync(new(wide, Classification: EngagementClassification.PossibleCampActivity))).Events);
        Assert.Equal(1, camp.UnknownValueCount); Assert.Equal(28m, camp.KnownIskDestroyed);
        var first = await db.Repository.GetCombatEventsAsync(new(wide, Limit: 1));
        Assert.NotNull(first.Next);
        var second = await db.Repository.GetCombatEventsAsync(new(wide, Limit: 1, After: first.Next));
        Assert.NotEqual(first.Events[0].Id, second.Events[0].Id);
        Assert.Equal(2, Assert.Single((await db.Repository.GetCombatEventsAsync(new(wide, SystemId: 2))).Events).KillmailCount);
        Assert.Empty((await db.Repository.GetCombatEventsAsync(new(wide, MinimumKnownIsk: 100))).Events);
        Assert.Empty((await db.Repository.GetCombatEventsAsync(new(new(Epoch.AddHours(3), Epoch.AddHours(4))))).Events);
        var map = await db.Repository.GetCombatMapAsync(wide);
        Assert.Equal(4, map.Markers[1]); Assert.Equal(1, map.Markers[2]);
        var ranks = await db.Repository.GetCombatSystemsAsync(wide);
        Assert.Equal(new long[] { 2, 3 }, ranks.Select(r => r.SolarSystemId)); // Camp and engagement layers are independent evidence.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.Repository.GetCombatEventsAsync(new(wide, Limit: 201)));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Repository.GetCombatMapAsync(wide, cancelled.Token));
    }

    [Fact]
    public async Task Combat_ReadComputeDoesNotHoldWriterGate_ConcurrentArrivalInvalidatesOptimisticRevision()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1), Observation(2, occurred: Epoch.AddMinutes(1))]);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Repository.ActivityReadSnapshotReady = async token => { ready.TrySetResult(); await release.Task.WaitAsync(token); };
        var analysis = db.Repository.ProcessCombatAsync(Epoch.AddHours(8), TimeSpan.FromDays(30), CombatOptions);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await db.Repository.InsertBatchAsync([Observation(3, occurred: Epoch.AddMinutes(2))]).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(analysis.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await analysis; db.Repository.ActivityReadSnapshotReady = null;
        Assert.Equal(0, await db.Scalar("SELECT COUNT(*) FROM CombatEvent;"));
        Assert.Equal(1, await db.Scalar("SELECT COUNT(*) FROM CombatDirty;"));
        await DrainCombat(db);
        Assert.Equal(3, Assert.Single((await db.Repository.GetCombatEventsAsync(new(Range))).Events).KillmailCount);
    }

    [Fact]
    public async Task Combat_HighVolumePartitionFailsExplicitly_WithoutTruncatedSafeLookingFindings()
    {
        await using var db = await Database.Create();
        await db.Sql($"""
            WITH RECURSIVE ids(n) AS(SELECT 1 UNION ALL SELECT n+1 FROM ids WHERE n<4097)
            INSERT INTO Killmail(KillmailId,Source,SolarSystemId,OccurredAtUtcTicks,ReceivedAtUtcTicks,VictimShipTypeId,AttackerCount,ActivityAggregated)
                SELECT n,'synthetic',1,{Epoch.UtcTicks},{Epoch.UtcTicks},587,0,1 FROM ids;
            INSERT INTO CombatDirty(SystemId,PartitionTicks) VALUES(1,{Epoch.UtcTicks});
            """);
        await DrainCombat(db);
        var map = await db.Repository.GetCombatMapAsync(Range);
        Assert.True(map.Incomplete); Assert.Empty(map.Markers);
        Assert.True((await db.Repository.GetCombatEventsAsync(new(Range))).Pending);
        Assert.Equal(4097, await db.Scalar("SELECT COUNT(*) FROM Killmail;"));
    }

    [Fact]
    public async Task Combat_ExactOverflowIsRecordedOnce_WithoutDiscardingRawOrInventingZeroActivity()
    {
        await using var db = await Database.Create();
        await db.Repository.InsertBatchAsync([Observation(1, occurred: Epoch, value: decimal.MaxValue),
            Observation(2, occurred: Epoch.AddMinutes(5), value: decimal.MaxValue)]);
        await DrainCombat(db);
        var page = await db.Repository.GetCombatEventsAsync(new(Range));
        Assert.Empty(page.Events); Assert.True(page.Pending);
        Assert.Equal(2, await db.Scalar("SELECT COUNT(*) FROM Killmail;"));
        Assert.Equal(0, await db.Scalar("SELECT COUNT(*) FROM CombatDirty;"));
        Assert.Equal(1, await db.Scalar("SELECT COUNT(*) FROM CombatAnalysisIssue;"));
    }

    [Fact]
    public async Task Combat_CoverageIsRefreshedAfterLaterCollectionGap()
    {
        await using var db = await Database.Create();
        await db.Repository.StartCollectionSessionAsync("combat", Epoch);
        await db.Repository.HeartbeatCollectionAsync("combat", Epoch.AddHours(1));
        await db.Repository.InsertBatchAsync([Observation(1), Observation(2, occurred: Epoch.AddMinutes(1))]);
        await DrainCombat(db);
        Assert.Equal(ActivityCoverageState.Observed, (await db.Repository.GetCombatEventsAsync(new(Range))).Coverage.State);
        // A narrow observed query must not upgrade an event whose wider source partition was only partly covered.
        Assert.Equal(ActivityCoverageState.Partial, Assert.Single((await db.Repository.GetCombatEventsAsync(new(Range))).Events).Coverage.State);
        await db.Repository.RecordGapsAsync([new() { Kind = "Offline", RecordedAtUtc = Epoch.AddMinutes(20), Count = 1 }]);
        var page = await db.Repository.GetCombatEventsAsync(new(Range));
        Assert.Equal(ActivityCoverageState.Partial, Assert.Single(page.Events).Coverage.State);
    }
}
