using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Tests;

public sealed class CombatDetectorTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly ActivityCoverage Unknown = new(ActivityCoverageState.Unknown, TimeSpan.Zero, 0, false);
    private static CombatInput Row(long id, double minutes, bool pvp = true, decimal? value = null) => new(id, 1, Epoch.AddMinutes(minutes), 587, value, pvp);
    private static IReadOnlyList<CombatEngagement> Detect(params CombatInput[] rows) => CombatDetector.Detect(rows, 1, Epoch, Unknown, Epoch.AddHours(6), new());

    [Fact]
    public void NearbyKillsGroup_DuplicatesDeduplicate_OrderIsDeterministic_AndUnknownsStayUnknown()
    {
        var rows = new[] { Row(3, 3), Row(1, 1, false), Row(2, 2, false), Row(1, 1, false) };
        var item = Assert.Single(Detect(rows));
        Assert.Equal(3, item.KillmailCount); Assert.Equal(3, item.UnknownValueCount); Assert.Equal(0, item.KnownIskDestroyed);
        Assert.Equal(new long[] { 1, 2, 3 }, item.MemberKillmailIds); Assert.Equal(EngagementConfidence.TemporalPattern, item.Evidence.Strength);
        Assert.Equal(ActivityCoverageState.Unknown, item.Coverage.State);
        Assert.Equal(item.Id, Assert.Single(Detect(rows.Reverse().ToArray())).Id);
    }
    [Fact]
    public void SingleExpensiveLossAndIsolatedLossesDoNotBecomeBattles()
    {
        Assert.Empty(Detect(Row(1, 1, value: 100_000_000_000m)));
        Assert.Empty(Detect(Row(1, 1), Row(2, 20), Row(3, 39)));
        Assert.Equal(EngagementClassification.SmallEngagement, Assert.Single(Detect(Row(1, 1, value: 1_000_000_000m), Row(2, 2))).Classification);
    }
    [Fact]
    public void QuietGapSeparatesEvents_LateBridgeMergesAndMaximumDurationSplits()
    {
        Assert.Equal(2, Detect(Row(1, 0), Row(2, 1), Row(3, 20), Row(4, 21)).Count);
        var merged = Assert.Single(Detect(Row(1, 0), Row(2, 1), Row(3, 20), Row(4, 21), Row(5, 10)));
        Assert.Equal(5, merged.MemberKillmailIds.Count);
        var chain = Detect(Enumerable.Range(0, 30).Select(i => Row(i + 1, i * 10)).ToArray());
        Assert.True(chain.Count > 1); Assert.All(chain.Where(e => e.Classification != EngagementClassification.PossibleCampActivity), item => Assert.True(item.LastUtc - item.FirstUtc <= TimeSpan.FromHours(2)));
    }
    [Fact]
    public void FixedPartitionBoundariesAreExplicit_AndClassificationDoesNotChangeAnchoredIdentifier()
    {
        var rows = new[] { Row(1, 358), Row(2, 359), Row(3, 360), Row(4, 361) };
        Assert.Equal(new long[] { 1, 2 }, Assert.Single(Detect(rows)).MemberKillmailIds);
        var next = Assert.Single(CombatDetector.Detect(rows, 1, Epoch.AddHours(6), Unknown, Epoch.AddHours(7), new()));
        Assert.Equal(new long[] { 3, 4 }, next.MemberKillmailIds);
        var small = Assert.Single(Detect(Row(1, 0), Row(2, 1)));
        var significant = Assert.Single(Detect(Enumerable.Range(0, 6).Select(i => Row(i + 1, i)).ToArray()));
        Assert.Equal(small.Id, significant.Id); Assert.Equal(EngagementClassification.SignificantEngagement, significant.Classification);
    }
    [Fact]
    public void BattleScaleAndCoverageAreIndependent_AndSustainedPatternIsNotSingleBurst()
    {
        var burst = Detect(Enumerable.Range(0, 30).Select(i => Row(i + 1, i / 20d)).ToArray());
        Assert.Equal(EngagementClassification.SustainedBattle, Assert.Single(burst).Classification);
        Assert.DoesNotContain(burst, e => e.Classification == EngagementClassification.PossibleCampActivity);
        var spread = Detect(Enumerable.Range(0, 8).Select(i => Row(i + 1, i * 16)).ToArray());
        var camp = Assert.Single(spread, e => e.Classification == EngagementClassification.PossibleCampActivity);
        Assert.Contains("No gate/grid", camp.Evidence.Explanation); Assert.Equal("Possible Camp Activity", camp.Label);
        Assert.Equal(EngagementConfidence.CorroboratedPvpPattern, camp.Evidence.Strength); Assert.Equal(ActivityCoverageState.Unknown, camp.Coverage.State);
        Assert.True(camp.IsCurrentCandidate(camp.LastUtc.AddMinutes(20))); Assert.False(camp.IsCurrentCandidate(camp.LastUtc.AddHours(1)));
        Assert.False(camp.IsPreliminary(Epoch.AddHours(49))); Assert.True(camp.RawDetailsExpired(Epoch.AddHours(51)));
    }
    [Fact]
    public void UnknownParticipantsCannotSupportCampClassification_AndCancellationIsHonored()
    {
        Assert.DoesNotContain(Detect(Enumerable.Range(0, 8).Select(i => Row(i + 1, i * 16, false)).ToArray()), e => e.Classification == EngagementClassification.PossibleCampActivity);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => CombatDetector.Detect([Row(1, 0)], 1, Epoch, Unknown, Epoch, new(), cancellationToken: cancel.Token));
    }
    [Fact]
    public async Task ShutdownAwaitsCleanupAndHasBoundedTimeout()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdown = BoundedCleanup.RunAsync(() => completion.Task, TimeSpan.FromSeconds(2));
        Assert.False(shutdown.IsCompleted); completion.SetResult(); Assert.True(await shutdown);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.False(await BoundedCleanup.RunAsync(() => blocked.Task, TimeSpan.FromMilliseconds(20)));
        blocked.SetResult();
    }
}
