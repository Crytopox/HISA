using System.Collections.Frozen;
using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Tests;

public sealed class CombatAnalysisFeatureTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly ActivityRange Range = new(Epoch, Epoch.AddHours(1));
    private static readonly ActivityCoverage Unknown = new(ActivityCoverageState.Unknown, TimeSpan.Zero, 0, false);
    private static CombatEngagement Event => Assert.Single(CombatDetector.Detect([new(1, 1, Epoch, 587, null, false), new(2, 1, Epoch.AddMinutes(1), 587, null, false)], 1, Epoch, Unknown, Epoch, new()));
    private static CombatAnalysisFeature Feature(Repo repo) => new(repo, action => { action(); return Task.CompletedTask; }, new Clock());
    [Fact]
    public async Task RapidRangeChangesDebounce_StaleUncooperativeQueriesNeverPublish()
    {
        var repo = new Repo(); await using var feature = Feature(repo);
        feature.IsEnabled = true;
        for (var i = 0; i < 50; i++) feature.SetContext(new(Epoch.AddMinutes(i * 5), Epoch.AddMinutes(i * 5 + 60)), null, id => "One");
        await feature.PendingTask; Assert.Equal(1, repo.Calls);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CombatMapSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Map = (range, _) => { started.TrySetResult(); return release.Task; };
        feature.SetContext(Range, 1, _ => "One"); await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stale = feature.PendingTask;
        var next = new ActivityRange(Epoch.AddHours(1), Epoch.AddHours(2));
        feature.SetContext(next, 2, _ => "Two");
        repo.Map = (range, _) => Task.FromResult(new CombatMapSnapshot(range, new Dictionary<long, byte> { [2] = 2 }.ToFrozenDictionary(), false));
        release.SetResult(new(Range, new Dictionary<long, byte> { [1] = 1 }.ToFrozenDictionary(), false));
        await stale; await feature.PendingTask;
        Assert.Equal(next, feature.Snapshot!.Range); Assert.False(feature.Snapshot.Markers.ContainsKey(1));
        Assert.Equal(2, repo.LastSystem);
    }
    [Fact]
    public async Task SelectionExplainsUncertainty_FocusUsesExistingCallback_AndUnknownMoneyStaysLabeled()
    {
        var repo = new Repo(); await using var feature = Feature(repo);
        feature.SetContext(Range, 1, _ => "One"); feature.IsEnabled = true; await feature.PendingTask;
        feature.SelectedEvent = Assert.Single(feature.Events);
        Assert.Contains("One", feature.Detail); Assert.Contains("unknown-value losses: 2", feature.Detail);
        Assert.Contains("TemporalPattern", feature.Detail); Assert.Contains("Unknown", feature.Detail);
        Assert.Contains("Finalized", feature.Detail); Assert.Contains("expired", feature.Detail);
        long? focus = null; feature.FocusRequested += id => focus = id; feature.FocusCommand.Execute(null); Assert.Equal(1, focus);
    }
    [Fact]
    public async Task TimeoutDisableAndUnavailableMapClearMarkers_AndDisposalCancelsActiveWork()
    {
        var repo = new Repo(); var feature = Feature(repo);
        feature.SetContext(Range, 1, _ => "One"); feature.IsEnabled = true; await feature.PendingTask;
        repo.Map = (_, _) => throw new OperationCanceledException("deadline");
        feature.RefreshCommand.Execute(null); await feature.PendingTask;
        Assert.Contains("timed out", feature.Status); Assert.Null(feature.Snapshot); Assert.False(feature.IsLoading);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Map = async (range, token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new(range, FrozenDictionary<long, byte>.Empty, false); };
        feature.RefreshCommand.Execute(null); await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var pending = feature.PendingTask;
        await feature.DisposeAsync(); await pending; Assert.Null(feature.Snapshot);
        var notifications = 0; feature.PropertyChanged += (_, _) => notifications++;
        feature.IsEnabled = true; feature.RefreshCommand.Execute(null); feature.NextPageCommand.Execute(null);
        Assert.Null(feature.Snapshot); Assert.False(feature.IsEnabled); Assert.Equal(0, notifications);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Epoch.AddDays(3); }
    private sealed class Repo : ICombatAnalysisRepository
    {
        public int Calls;
        public long? LastSystem;
        public Func<ActivityRange, CancellationToken, Task<CombatMapSnapshot>> Map = (range, _) => Task.FromResult(new CombatMapSnapshot(range, new Dictionary<long, byte> { [1] = 1 }.ToFrozenDictionary(), false));
        public Task<CombatMapSnapshot> GetCombatMapAsync(ActivityRange range, CancellationToken cancellationToken = default) { Interlocked.Increment(ref Calls); return Map(range, cancellationToken); }
        public Task<CombatPage> GetCombatEventsAsync(CombatQuery query, CancellationToken cancellationToken = default) { LastSystem = query.SystemId; return Task.FromResult(new CombatPage([Event], null, false, Unknown)); }
        public Task<int> ProcessCombatAsync(DateTimeOffset now, TimeSpan retention, CombatDetectionOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CombatEngagement?> GetCombatEventAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CombatSystemSummary>> GetCombatSystemsAsync(ActivityRange range, int limit = 10000, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
