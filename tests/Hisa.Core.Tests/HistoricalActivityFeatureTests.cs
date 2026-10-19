using System.Collections.Concurrent;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Tests;

public sealed class HistoricalActivityFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 2, 34, TimeSpan.Zero);
    private static HistoricalActivityFeature Feature(Repo repo, Settings? settings = null, TimeSpan? retention = null)
        => new(repo, settings ?? new(), action => { action(); return Task.CompletedTask; }, retention ?? TimeSpan.FromDays(30),
            (_, _) => Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string> { [587] = "Rifter" }), new Clock());

    [Theory]
    [InlineData(0, 15)] [InlineData(1, 60)] [InlineData(2, 360)] [InlineData(3, 1440)] [InlineData(4, 10080)] [InlineData(5, 43200)]
    public async Task Presets_AlignUtc_MoveFixedWindows_AndClampRetention(int preset, int minutes)
    {
        await using var feature = Feature(new());
        feature.PresetIndex = preset;
        Assert.Equal(TimeSpan.FromMinutes(minutes), feature.Range.ToUtc - feature.Range.FromUtc);
        Assert.Equal(ActivityRange.BucketStart(Now), feature.Range.ToUtc);
        Assert.Equal(feature.Range, feature.Range.Effective());
        var latest = feature.Range;
        feature.PreviousCommand.Execute(null);
        Assert.Equal(latest.ToUtc.AddMinutes(feature.MaxOffsetBuckets > 0 ? -5 : 0), feature.Range.ToUtc);
        feature.NextCommand.Execute(null);
        Assert.Equal(latest, feature.Range);
        feature.OffsetBuckets = int.MaxValue;
        Assert.True(feature.Range.FromUtc >= ActivityRange.BucketStart(Now).AddDays(-30));
        Assert.Equal(feature.MaxOffsetBuckets, feature.OffsetBuckets);
        Assert.Contains("UTC", feature.RangeText);
        Assert.Contains("Local", feature.LocalRangeText);
    }

    [Fact]
    public async Task ExistingUsersDefaultOff_AndPreferencesRestoreRollingWindowsWithoutOffsets()
    {
        var repo = new Repo(); var settings = new Settings();
        await using (var feature = Feature(repo, settings))
        {
            await feature.InitializeAsync();
            Assert.False(feature.IsEnabled); Assert.Empty(repo.MapRequests);
            feature.IsEnabled = true; feature.PresetIndex = 2; feature.MetricIndex = 3;
            feature.OffsetBuckets = 10;
            await feature.PendingMapTask;
            for (var offset = 11; offset < 40; offset++) feature.OffsetBuckets = offset;
            Assert.Equal(0, settings.Writes);
        }
        await using var restored = Feature(new(), settings);
        await restored.InitializeAsync(); await restored.PendingMapTask;
        Assert.True(restored.IsEnabled); Assert.Equal(2, restored.PresetIndex); Assert.Equal(3, restored.MetricIndex);
        Assert.Equal(0, restored.OffsetBuckets); Assert.Equal(ActivityRange.BucketStart(Now), restored.Range.ToUtc);
    }

    [Fact]
    public async Task RapidScrubbing_Debounces_AndMetricOrContextChangesReuseSummary()
    {
        var repo = new Repo(); await using var feature = Feature(repo);
        feature.IsEnabled = true;
        for (var i = 1; i <= 40; i++) feature.OffsetBuckets = i;
        await feature.PendingMapTask;
        Assert.Single(repo.MapRequests);
        var snapshot = feature.Snapshot;
        feature.SetMapContext(Graph(), MapViewMode.Region, null);
        feature.SetMapContext(Graph(), MapViewMode.Universe, null);
        feature.SetMapContext(Graph(), MapViewMode.UniverseRegions, null);
        Assert.Contains("hidden", feature.ContextNote);
        Assert.Same(snapshot, feature.Snapshot);
        feature.MetricIndex = 3;
        await feature.PendingMapTask;
        Assert.Single(repo.MapRequests);
        Assert.Equal(HistoricalActivityMetric.AttackerEntries, feature.Snapshot!.Metric);
        feature.IsEnabled = false;
        Assert.Null(feature.Snapshot); Assert.False(feature.IsLoading);
    }

    [Fact]
    public async Task SupersededRequest_CancelsImmediately_AndCannotPublishEvenIfRepositoryIgnoresCancellation()
    {
        var repo = new Repo();
        var first = new TaskCompletionSource<ActivityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Map = (_, _) => first.Task;
        await using var feature = Feature(repo);
        feature.IsEnabled = true;
        await WaitFor(() => repo.MapRequests.Count == 1);
        var old = feature.PendingMapTask;
        feature.OffsetBuckets = 1;
        Assert.True(repo.MapRequests.First().Token.IsCancellationRequested);
        repo.Map = (range, _) => Task.FromResult(Result(range));
        first.SetResult(Result(repo.MapRequests.First().Range));
        await old; await feature.PendingMapTask;
        Assert.Equal(feature.Range, feature.Snapshot!.Range);
        Assert.Equal(2, repo.MapRequests.Count);
        Assert.False(feature.IsLoading);
    }

    [Fact]
    public async Task TimeoutAndErrors_KeepPreviousMapAndLabelItsRange()
    {
        var repo = new Repo(); await using var feature = Feature(repo);
        feature.IsEnabled = true; await feature.PendingMapTask;
        var previous = feature.Snapshot;
        repo.Map = (_, _) => throw new OperationCanceledException("SQLite deadline");
        feature.OffsetBuckets = 1;
        Assert.True(feature.IsLoading);
        await feature.PendingMapTask;
        Assert.Same(previous, feature.Snapshot); Assert.False(feature.IsLoading);
        Assert.Contains("timed out", feature.Status);
        Assert.Contains(previous!.Range.ToUtc.ToString("MMM dd HH:mm"), feature.DisplayedRangeText);
        repo.Map = (_, _) => throw new InvalidOperationException("read failure");
        feature.OffsetBuckets = 2; await feature.PendingMapTask;
        Assert.Contains("failed", feature.Status); Assert.Same(previous, feature.Snapshot);
    }

    [Fact]
    public async Task SelectionChanges_CancelOldDetails_ShipBreakdownIsOnDemand_AndNamesResolveLocally()
    {
        var repo = new Repo(); await using var feature = Feature(repo);
        feature.IsEnabled = true; feature.SetMapContext(Graph(), MapViewMode.Region, 1);
        await feature.PendingMapTask; await feature.PendingDetailTask;
        Assert.Contains("One", feature.DetailText); Assert.Contains("Unknown-value kills: 2", feature.DetailText);
        Assert.Contains("not unique", feature.DetailText); Assert.Contains("UTC", feature.DetailText);
        Assert.NotEmpty(feature.Timeline); Assert.InRange(feature.Timeline.Count, 1, 96);
        Assert.Equal(0, repo.ShipRequests);
        feature.LoadShipsCommand.Execute(null); await feature.PendingShipTask;
        Assert.Equal("Rifter", Assert.Single(feature.Ships).Name);
        var stale = new TaskCompletionSource<ActivityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Series = (_, _, _) => stale.Task;
        feature.SetMapContext(Graph(), MapViewMode.Region, 2);
        await WaitFor(() => repo.SeriesRequests >= 2);
        var old = feature.PendingDetailTask;
        feature.SetMapContext(Graph(), MapViewMode.Region, 1);
        repo.Series = (_, range, _) => Task.FromResult(Result(range));
        stale.SetResult(Result(feature.Range));
        await old; await feature.PendingDetailTask;
        Assert.Contains("One", feature.DetailText); Assert.DoesNotContain("Two", feature.DetailText);
    }

    [Fact]
    public async Task DisabledOrDisposedFeaturesCannotPublish_AfterCancelledQueriesComplete()
    {
        var repo = new Repo(); var result = new TaskCompletionSource<ActivityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Map = (_, _) => result.Task;
        var feature = Feature(repo); feature.IsEnabled = true;
        await WaitFor(() => repo.MapRequests.Count > 0);
        var pending = feature.PendingMapTask;
        feature.IsEnabled = false;
        result.SetResult(Result(feature.Range)); await pending;
        Assert.Null(feature.Snapshot);
        await feature.DisposeAsync();
        feature.IsEnabled = true;
        Assert.False(feature.IsEnabled); Assert.Single(repo.MapRequests);
    }

    [Theory]
    [InlineData(ActivityCoverageState.Unknown, false)] [InlineData(ActivityCoverageState.Partial, false)] [InlineData(ActivityCoverageState.Observed, false)] [InlineData(ActivityCoverageState.Observed, true)]
    public async Task EmptyAndExpiredHistory_DoesNotBecomeConfirmedInactivity(ActivityCoverageState state, bool expired)
    {
        var repo = new Repo { Map = (range, _) => Task.FromResult(new ActivityResult(range, range, [], [], [], ActivityMetrics.Zero,
            new(state, TimeSpan.Zero, 0, false, AggregateHistoryExpired: expired))) };
        await using var feature = Feature(repo); feature.IsEnabled = true; await feature.PendingMapTask;
        Assert.Equal(state == ActivityCoverageState.Observed && !expired ? 1 : 0, feature.Snapshot!.LevelFor(99));
        if (expired) Assert.Contains("expired", feature.Status);
        else if (state != ActivityCoverageState.Observed) Assert.Contains("not confirmed", feature.Status);
    }

    [Fact]
    public async Task Disposal_CancelsActiveReads_DrainsWork_AndDisablesCommandsWithoutPublishing()
    {
        var repo = new Repo();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.Map = async (range, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Result(range);
        };
        var feature = Feature(repo);
        await feature.InitializeAsync(); feature.IsEnabled = true;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var task = feature.PendingMapTask;
        await feature.DisposeAsync(); await task;
        Assert.True(repo.MapRequests.First().Token.IsCancellationRequested);
        Assert.Null(feature.Snapshot); Assert.False(feature.IsEnabled); Assert.False(feature.IsLoading);
        var range = feature.Range;
        feature.LatestCommand.Execute(null); feature.OffsetBuckets = 20; feature.IsEnabled = true;
        Assert.Equal(range, feature.Range); Assert.False(feature.IsEnabled); Assert.Single(repo.MapRequests);
    }

    [Fact]
    public async Task ConfiguredRetention_ClampsWindows_AndDisablingDoesNotTouchGraphOrLivePreferences()
    {
        var settings = new Settings(); var repo = new Repo(); var graph = Graph();
        await using var feature = Feature(repo, settings, TimeSpan.FromDays(7));
        feature.SetMapContext(graph, MapViewMode.Region, 1); feature.PresetIndex = 5;
        Assert.Equal(TimeSpan.FromDays(7), feature.Range.ToUtc - feature.Range.FromUtc);
        Assert.Contains("limited", feature.WindowNote); Assert.Equal(0, feature.MaxOffsetBuckets);
        await feature.InitializeAsync(); feature.IsEnabled = true; await feature.PendingMapTask;
        feature.IsEnabled = false;
        Assert.Equal(1, graph.Nodes[0].Id); Assert.Equal(0.25, graph.Nodes[0].X);
        Assert.All(settings.Keys, key => Assert.Equal("Map.Historical.Preferences", key));
    }

    [Fact]
    public void DeterministicIntensity_HandlesOutliers_NullMoney_AndCoverage()
    {
        var observed = new ActivityCoverage(ActivityCoverageState.Observed, TimeSpan.FromHours(1), 0, false);
        var unknown = observed with { State = ActivityCoverageState.Unknown };
        Assert.Equal(1, HistoricalMapSnapshot.Level(ActivityMetrics.Zero, 0, HistoricalActivityMetric.Killmails, observed));
        Assert.Equal(0, HistoricalMapSnapshot.Level(ActivityMetrics.Zero, 0, HistoricalActivityMetric.Killmails, unknown));
        var metric = Metrics(4);
        Assert.Equal(0, HistoricalMapSnapshot.Level(metric with { KnownValueCount = 0, KnownIskDestroyed = 0 }, 4, HistoricalActivityMetric.KnownIsk, observed));
        var low = HistoricalMapSnapshot.Level(metric, 4, HistoricalActivityMetric.Killmails, observed);
        Assert.InRange(low, (byte)2, (byte)32);
        Assert.Equal(33, HistoricalMapSnapshot.Level(Metrics(long.MaxValue), long.MaxValue, HistoricalActivityMetric.Killmails, observed));
        Assert.Equal(low, HistoricalMapSnapshot.Level(metric, long.MaxValue, HistoricalActivityMetric.Killmails, observed));
        Assert.Equal(33, HistoricalMapSnapshot.Level(metric, 4, HistoricalActivityMetric.LossConcentration, observed));
    }

    [Fact]
    public void LongTimelines_AreBounded_IncludeEmptyIntervals_AndConserveCounts()
    {
        var end = ActivityRange.BucketStart(Now); var range = new ActivityRange(end.AddDays(-30), end);
        var buckets = Enumerable.Range(0, 8640).Where(i => i % 2 == 0)
            .Select(i => new ActivityBucket(1, range.FromUtc.AddMinutes(i * 5), Metrics(2))).ToArray();
        var timeline = HistoricalMapSnapshot.Timeline(range, buckets);
        Assert.Equal(96, timeline.Count); Assert.Equal(8640, timeline.Sum(bin => bin.Killmails));
        Assert.Equal(range.FromUtc, timeline[0].FromUtc); Assert.Equal(range.ToUtc, timeline[^1].ToUtc);
    }

    private static async Task WaitFor(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private static MapGraph Graph() => new() { Nodes = [new() { Id = 1, Name = "One", X = 0.25, Y = 0.25 }, new() { Id = 2, Name = "Two", X = 0.75, Y = 0.75 }], Links = [] };
    private static ActivityMetrics Metrics(long kills) => new(kills, 1, Math.Max(0, kills - 1), 123.45m, 2, 2, 7, ActivityRange.BucketStart(Now).AddMinutes(-5), ActivityRange.BucketStart(Now).AddMinutes(-1));
    private static ActivityResult Result(ActivityRange range) => new(range, range, [new(1, Metrics(4))], [new(1, range.FromUtc, Metrics(4))], [], Metrics(4), new(ActivityCoverageState.Observed, range.ToUtc - range.FromUtc, 0, false));
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Settings : ISettingsService
    {
        private object? _value;
        public int Writes;
        public ConcurrentQueue<string> Keys { get; } = new();
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(_value is T typed ? typed : default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _value = value; Keys.Enqueue(key); Interlocked.Increment(ref Writes); return Task.CompletedTask; }
    }
    private sealed class Repo : IIntelligenceActivityRepository
    {
        public Func<ActivityRange, CancellationToken, Task<ActivityResult>> Map = (range, _) => Task.FromResult(Result(range));
        public Func<long, ActivityRange, CancellationToken, Task<ActivityResult>> Series = (_, range, _) => Task.FromResult(Result(range));
        public ConcurrentQueue<(ActivityRange Range, CancellationToken Token)> MapRequests { get; } = new();
        public int SeriesRequests, ShipRequests;
        public Task<ActivityResult> GetMapActivityAsync(ActivityRange range, CancellationToken cancellationToken = default) { MapRequests.Enqueue((range, cancellationToken)); return Map(range, cancellationToken); }
        public Task<ActivityResult> GetTimeSeriesAsync(long solarSystemId, ActivityRange range, CancellationToken cancellationToken = default) { Interlocked.Increment(ref SeriesRequests); return Series(solarSystemId, range, cancellationToken); }
        public Task<ShipLossActivityResult> GetShipLossesAsync(ActivityRange range, IReadOnlyCollection<long>? systems = null, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref ShipRequests); return Task.FromResult(new ShipLossActivityResult(range, range, [new(systems!.First(), 587, 3)], new(ActivityCoverageState.Observed, TimeSpan.FromHours(1), 0, false))); }
        public Task<ActivityResult> GetActivityAsync(ActivityRange range, IReadOnlyCollection<long>? systems = null, bool includeBuckets = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ActivityResult> GetSystemActivityAsync(long solarSystemId, ActivityRange range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RankedActivityResult> GetTopSystemsAsync(ActivityRange range, ActivityRanking ranking, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CatchUpActivityAsync(int maxRows, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> DeleteActivityBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task HeartbeatCollectionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EndCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
