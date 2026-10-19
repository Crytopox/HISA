using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Hisa.Services.Intelligence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class IntelligencePersistenceReliabilityTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Spool_ReplaysAfterTemporaryOutageWithoutReportingFailureAsSuccess()
    {
        await using var database = new TempDatabase();
        var flaky = new FlakyHistoryRepository(database.Repository) { RemainingFailures = 20 };
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(flaky, feed, new IntelligencePersistenceOptions
        {
            MaxWriteAttempts = 2,
            WriteRetryBaseDelay = TimeSpan.FromMilliseconds(1),
            SpoolRetryDelay = TimeSpan.FromMilliseconds(30),
            RecoverySpoolPath = database.SpoolDirectory
        });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(Observation(70, Epoch, attackers: [new KillmailAttacker { FinalBlow = true, DamageDone = 9 }]));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            Assert.True(await WaitForAsync(() => service.GetDiagnostics().Pending >= 1, TimeSpan.FromSeconds(3)));
            Assert.Equal(0, service.GetDiagnostics().Committed);
            Assert.Equal(0, service.GetDiagnostics().Lost);

            flaky.RemainingFailures = 0;
            Assert.True(await WaitForAsync(() => service.GetDiagnostics().Committed >= 1 && service.GetDiagnostics().Pending == 0, TimeSpan.FromSeconds(3)));
            var detail = await database.Repository.GetKillmailAsync(70);
            Assert.NotNull(detail);
            Assert.Equal(9, detail.Participants[0].DamageDone);
            Assert.Equal(0, service.GetDiagnostics().PermanentlyRejected);
        }
        finally
        {
            flaky.RemainingFailures = 0;
            await service.StopAsync(CancellationToken.None);
            await feed.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Spool_ReplaysOnRestartAndDuplicateReplayIsIdempotent()
    {
        await using var database = new TempDatabase();
        await database.Repository.InitializeAsync();
        await database.Repository.InsertBatchAsync([Observation(1, Epoch)]);
        var spool = new IntelligenceRecoverySpool(database.SpoolDirectory, 10);
        Assert.True(spool.TryAppend(Observation(1, Epoch, attackers: [new KillmailAttacker { DamageDone = 4 }])));
        Assert.True(spool.TryAppend(Observation(2, Epoch.AddMinutes(1), attackers: [new KillmailAttacker { DamageDone = 8 }])));

        var first = CreateService(database.Repository, new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance), new IntelligencePersistenceOptions
        {
            RecoverySpoolPath = database.SpoolDirectory,
            SpoolRetryDelay = TimeSpan.FromMilliseconds(10)
        });
        await first.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await WaitForAsync(() => first.GetDiagnostics().Pending == 0, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            await first.StopAsync(CancellationToken.None);
        }

        var health = await database.Repository.GetHealthAsync();
        var replayed = await database.Repository.GetKillmailAsync(2);
        var original = await database.Repository.GetKillmailAsync(1);
        Assert.Equal(2, health.KillmailCount);
        Assert.Equal(2, health.ParticipantCount);
        Assert.NotNull(replayed);
        Assert.Equal(8, replayed.Participants[0].DamageDone);
        Assert.NotNull(original);
        Assert.Single(original.Participants);

        Assert.True(spool.TryAppend(Observation(2, Epoch.AddMinutes(1))));
        var second = CreateService(database.Repository, new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance), new IntelligencePersistenceOptions
        {
            RecoverySpoolPath = database.SpoolDirectory,
            SpoolRetryDelay = TimeSpan.FromMilliseconds(10)
        });
        await second.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await WaitForAsync(() => second.GetDiagnostics().Pending == 0, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, (await database.Repository.GetHealthAsync()).KillmailCount);
        Assert.Equal(2, (await database.Repository.GetHealthAsync()).ParticipantCount);
    }

    [Fact]
    public async Task CoalescedGaps_PreserveKindSourceAndSequenceRange()
    {
        await using var database = new TempDatabase();
        var repository = new GatedHistoryRepository(database.Repository);
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(repository, feed, new IntelligencePersistenceOptions
        {
            QueueCapacity = 1,
            BatchSize = 1,
            MaxBatchWait = TimeSpan.FromMilliseconds(10),
            RecoverySpoolPath = database.SpoolDirectory
        });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(Observation(1, Epoch));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            await repository.FirstInsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            feed.Publish(Observation(2, Epoch.AddMinutes(1)));
            feed.Publish(Observation(3, Epoch.AddMinutes(2), sourceSequence: 90));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            service.Record(Gap(IntelligenceCollectionGapKind.SkippedSequence, 44));
            service.Record(Gap(IntelligenceCollectionGapKind.SkippedSequence, 49));
            repository.ReleaseInserts.TrySetResult();
            Assert.True(await WaitForAsync(async () =>
            {
                var gaps = await database.Repository.GetCollectionGapsAsync(20);
                return gaps.Any(gap => gap.Kind == IntelligenceCollectionGapKind.SkippedSequence && gap.Count == 2);
            }, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            repository.ReleaseInserts.TrySetResult();
            await service.StopAsync(CancellationToken.None);
            await feed.StopAsync(CancellationToken.None);
        }

        var stored = await database.Repository.GetCollectionGapsAsync(20);
        var skipped = Assert.Single(stored, gap => gap.Kind == IntelligenceCollectionGapKind.SkippedSequence);
        Assert.Equal(KillmailObservation.R2Z2SourceId, skipped.Source);
        Assert.Equal(49, skipped.SourceSequence);
        Assert.Equal(2, skipped.Count);
        Assert.Contains("44..49", skipped.Detail, StringComparison.Ordinal);
        var overflow = Assert.Single(stored, gap => gap.Kind == IntelligenceCollectionGapKind.QueueOverflow);
        Assert.Equal(KillmailObservation.R2Z2SourceId, overflow.Source);
        Assert.Equal(90, overflow.SourceSequence);
    }

    [Fact]
    public async Task FeedOverflowCheckpoint_SurvivesRestartWithoutDuplicateDelta()
    {
        await using var database = new TempDatabase();
        var feed = new IntelligenceObservationFeed(
            NullLogger<IntelligenceObservationFeed>.Instance,
            new IntelligenceObservationFeedOptions { Capacity = 1 });
        var service = CreateService(database.Repository, feed, new IntelligencePersistenceOptions
        {
            CoverageCheckpointInterval = TimeSpan.FromMilliseconds(20),
            RecoverySpoolPath = database.SpoolDirectory
        });
        await service.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(Observation(1, Epoch));
            feed.Publish(Observation(2, Epoch));
            feed.Publish(Observation(3, Epoch));
            feed.Publish(Observation(4, Epoch));
            Assert.True(await WaitForAsync(async () =>
            {
                var gaps = await database.Repository.GetCollectionGapsAsync(10);
                return gaps.Any(gap => gap.Kind == IntelligenceCollectionGapKind.ObservationRejected && gap.Count == feed.RejectedEnqueueCount);
            }, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var before = await database.Repository.GetCollectionGapsAsync(10);
        var rejected = Assert.Single(before, gap => gap.Kind == IntelligenceCollectionGapKind.ObservationRejected);
        Assert.Equal(3, rejected.Count);

        var restartedFeed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var restarted = CreateService(database.Repository, restartedFeed, new IntelligencePersistenceOptions
        {
            CoverageCheckpointInterval = TimeSpan.FromMilliseconds(20),
            RecoverySpoolPath = database.SpoolDirectory
        });
        await restarted.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(150);
        }
        finally
        {
            await restarted.StopAsync(CancellationToken.None);
        }

        var after = await database.Repository.GetCollectionGapsAsync(10);
        var rejectedAfter = after.Where(gap => gap.Kind == IntelligenceCollectionGapKind.ObservationRejected).ToArray();
        Assert.Single(rejectedAfter);
        Assert.Equal(3, rejectedAfter[0].Count);
    }

    [Fact]
    public async Task Shutdown_ExpiredFlushTokenStillPersistsUndeliveredGaps()
    {
        await using var database = new TempDatabase();
        var blocking = new BlockingHistoryRepository(database.Repository);
        File.WriteAllText(database.SpoolBlocker, "x");
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(blocking, feed, new IntelligencePersistenceOptions
        {
            BatchSize = 1,
            MaxBatchWait = TimeSpan.FromMilliseconds(10),
            ShutdownFlushTimeout = TimeSpan.FromMilliseconds(200),
            ShutdownGapReportTimeout = TimeSpan.FromSeconds(1),
            RecoverySpoolPath = Path.Combine(database.SpoolBlocker, "spool")
        });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        var started = Environment.TickCount64;
        try
        {
            feed.Publish(Observation(11, Epoch, sourceSequence: 11));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            await blocking.InsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(Environment.TickCount64 - started < 3000);
        Assert.Equal(0, service.GetDiagnostics().Committed);
        Assert.Equal(0, service.GetDiagnostics().LoggedOnlyGaps);
        var gaps = await database.Repository.GetCollectionGapsAsync(10);
        var undelivered = Assert.Single(gaps, gap => gap.Kind == IntelligenceCollectionGapKind.ShutdownUndelivered);
        Assert.Equal(11, undelivered.KillmailId);
        Assert.Equal(11, undelivered.SourceSequence);
    }

    [Fact]
    public async Task Shutdown_FailedGapReportIsLoggedOnlyAndDoesNotHang()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, "x");
        var repository = new UnavailableHistoryRepository(Path.Combine(directory, "hisa-intelligence.db"));
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var service = CreateService(repository, feed, new IntelligencePersistenceOptions
        {
            MaxWriteAttempts = 1,
            WriteRetryBaseDelay = TimeSpan.FromMilliseconds(1),
            ShutdownFlushTimeout = TimeSpan.FromMilliseconds(300),
            ShutdownGapReportTimeout = TimeSpan.FromMilliseconds(300),
            RecoverySpoolPath = Path.Combine(blocker, "spool")
        });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        var started = Environment.TickCount64;
        try
        {
            feed.Publish(Observation(4, Epoch));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            Assert.True(await WaitForAsync(() => service.GetDiagnostics().Lost >= 1, TimeSpan.FromSeconds(3)));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await feed.StopAsync(CancellationToken.None);
            TryDeleteDirectory(directory);
        }

        Assert.True(Environment.TickCount64 - started < 3000);
        var diagnostics = service.GetDiagnostics();
        Assert.Equal(0, diagnostics.Committed);
        Assert.True(diagnostics.Lost >= 1);
        Assert.True(diagnostics.LoggedOnlyGaps >= 1);
    }

    [Fact]
    public async Task TimeRangePages_EnumerateEveryRecordOncePastTheQueryLimit()
    {
        var database = new TempDatabase(maxQueryLimit: 3);
        await using (database)
        {
            await database.Repository.InitializeAsync();
            var shared = Epoch.AddMinutes(1);
            await database.Repository.InsertBatchAsync(
            [
                Observation(1, Epoch),
                Observation(2, shared),
                Observation(5, shared),
                Observation(3, shared),
                Observation(4, Epoch.AddMinutes(2)),
                Observation(6, Epoch.AddMinutes(3)),
                Observation(7, Epoch.AddMinutes(3)),
                Observation(8, Epoch.AddMinutes(4))
            ]);

            var bounded = await database.Repository.GetByTimeRangeAsync(Epoch, Epoch.AddHours(1), 100);
            Assert.Equal(3, bounded.Count);

            var seen = new List<long>();
            KillmailHistoryCursor? cursor = null;
            var pages = 0;
            do
            {
                var page = await database.Repository.GetTimeRangePageAsync(Epoch, Epoch.AddHours(1), 100, cursor);
                pages++;
                seen.AddRange(page.Records.Select(record => record.KillmailId));
                cursor = page.NextCursor;
                Assert.True(page.Records.Count <= 3);
            }
            while (cursor is not null);

            Assert.Equal([1L, 2L, 3L, 5L, 4L, 6L, 7L, 8L], seen);
            Assert.Equal(3, pages);
        }
    }

    [Fact]
    public async Task GapRetention_KeepsCoveragePastKillmailDetailRetention()
    {
        await using var database = new TempDatabase();
        await database.Repository.InitializeAsync();
        await database.Repository.RecordGapsAsync(
        [
            new IntelligenceCollectionGap
            {
                Kind = IntelligenceCollectionGapKind.SkippedSequence,
                RecordedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
                Source = KillmailObservation.R2Z2SourceId,
                SourceSequence = 15,
                Count = 1
            }
        ]);

        await database.Repository.DeleteOccurredBeforeAsync(DateTimeOffset.UtcNow, 100);
        Assert.Single(await database.Repository.GetCollectionGapsAsync(10));

        Assert.Equal(0, await database.Repository.DeleteGapsRecordedBeforeAsync(DateTimeOffset.UtcNow.AddDays(-30), 100));
        Assert.Single(await database.Repository.GetCollectionGapsAsync(10));

        Assert.Equal(1, await database.Repository.DeleteGapsRecordedBeforeAsync(DateTimeOffset.UtcNow, 100));
        Assert.Empty(await database.Repository.GetCollectionGapsAsync(10));
    }

    private static IntelligencePersistenceHostedService CreateService(
        IIntelligenceHistoryRepository repository,
        IIntelligenceObservationFeed feed,
        IntelligencePersistenceOptions options)
    {
        options.QueueCapacity = options.QueueCapacity == 0 ? 16 : options.QueueCapacity;
        options.BatchSize = options.BatchSize == 0 ? 8 : options.BatchSize;
        options.MaxBatchWait = options.MaxBatchWait == TimeSpan.Zero ? TimeSpan.FromMilliseconds(20) : options.MaxBatchWait;
        options.RetentionPeriod = TimeSpan.FromDays(36500);
        options.GapRetentionPeriod = TimeSpan.FromDays(36500);
        options.RetentionInterval = TimeSpan.FromHours(1);
        options.ShutdownFlushTimeout = options.ShutdownFlushTimeout == TimeSpan.Zero
            ? TimeSpan.FromSeconds(2)
            : options.ShutdownFlushTimeout;
        options.ShutdownGapReportTimeout = options.ShutdownGapReportTimeout == TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : options.ShutdownGapReportTimeout;
        options.CoverageCheckpointInterval = options.CoverageCheckpointInterval == TimeSpan.Zero
            ? TimeSpan.FromHours(1)
            : options.CoverageCheckpointInterval;
        return new IntelligencePersistenceHostedService(
            repository,
            feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance,
            options);
    }

    private static IntelligenceCollectionGap Gap(string kind, long sequence)
    {
        return new IntelligenceCollectionGap
        {
            Kind = kind,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Source = KillmailObservation.R2Z2SourceId,
            SourceSequence = sequence,
            Detail = "malformed sequence"
        };
    }

    private static KillmailObservation Observation(
        long killmailId,
        DateTimeOffset occurredAtUtc,
        IReadOnlyList<KillmailAttacker>? attackers = null,
        long? sourceSequence = null)
    {
        return new KillmailObservation
        {
            KillmailId = killmailId,
            Source = KillmailObservation.R2Z2SourceId,
            SourceSequence = sourceSequence ?? killmailId,
            SolarSystemId = 30000142,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = occurredAtUtc.AddSeconds(2),
            Victim = new KillmailVictim { ShipTypeId = 670 },
            Attackers = attackers ?? [new KillmailAttacker { FinalBlow = true, DamageDone = 1 }]
        };
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= until)
            {
                return false;
            }

            await Task.Delay(10);
        }

        return true;
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!await condition())
        {
            if (Environment.TickCount64 >= until)
            {
                return false;
            }

            await Task.Delay(15);
        }

        return true;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class TempDatabase : IAsyncDisposable
    {
        private readonly string _directory;

        public TempDatabase(int maxQueryLimit = 1000)
        {
            _directory = Path.Combine(Path.GetTempPath(), "hisa-intel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            DatabasePath = Path.Combine(_directory, "hisa-intelligence.db");
            SpoolDirectory = Path.Combine(_directory, "spool");
            SpoolBlocker = Path.Combine(_directory, "blocker");
            Repository = new SqliteIntelligenceHistoryRepository(DatabasePath, TimeSpan.FromSeconds(2), maxQueryLimit);
        }

        public string DatabasePath { get; }

        public string SpoolDirectory { get; }

        public string SpoolBlocker { get; }

        public SqliteIntelligenceHistoryRepository Repository { get; }

        public async ValueTask DisposeAsync()
        {
            await Repository.DisposeAsync();
            TryDeleteDirectory(_directory);
        }
    }

    private sealed class FlakyHistoryRepository : IIntelligenceHistoryRepository
    {
        private readonly IIntelligenceHistoryRepository _inner;
        private int _remainingFailures;

        public FlakyHistoryRepository(IIntelligenceHistoryRepository inner)
        {
            _inner = inner;
        }

        public int RemainingFailures
        {
            get => Volatile.Read(ref _remainingFailures);
            set => Volatile.Write(ref _remainingFailures, value);
        }

        public string DatabasePath => _inner.DatabasePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);

        public Task<KillmailWriteResult> InsertBatchAsync(
            IReadOnlyList<KillmailObservation> observations,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new InvalidOperationException("temporary database outage");
            }

            return _inner.InsertBatchAsync(observations, cancellationToken);
        }

        public Task RecordGapsAsync(IReadOnlyList<IntelligenceCollectionGap> gaps, CancellationToken cancellationToken = default)
            => _inner.RecordGapsAsync(gaps, cancellationToken);

        public Task<int> DeleteOccurredBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => _inner.DeleteOccurredBeforeAsync(cutoffUtc, maxRows, cancellationToken);

        public Task<int> DeleteGapsRecordedBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => _inner.DeleteGapsRecordedBeforeAsync(cutoffUtc, maxRows, cancellationToken);

        public Task<KillmailHistoryPage> GetTimeRangePageAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, KillmailHistoryCursor? after, CancellationToken cancellationToken = default)
            => _inner.GetTimeRangePageAsync(fromUtcInclusive, toUtcInclusive, limit, after, cancellationToken);

        public Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(int limit, CancellationToken cancellationToken = default)
            => _inner.GetCollectionGapsAsync(limit, cancellationToken);

        public Task<long> RecordCoverageDeltaAsync(string checkpointName, long observedTotal, IntelligenceCollectionGap gap, CancellationToken cancellationToken = default)
            => _inner.RecordCoverageDeltaAsync(checkpointName, observedTotal, gap, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => _inner.GetByTimeRangeAsync(fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(long solarSystemId, DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => _inner.GetBySolarSystemAsync(solarSystemId, fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default)
            => _inner.GetKillmailAsync(killmailId, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default)
            => _inner.GetRecentAsync(limit, offset, cancellationToken);

        public Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
            => _inner.GetHealthAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class GatedHistoryRepository : IIntelligenceHistoryRepository
    {
        private readonly IIntelligenceHistoryRepository _inner;
        private int _inserts;

        public GatedHistoryRepository(IIntelligenceHistoryRepository inner) => _inner = inner;

        public TaskCompletionSource FirstInsertStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseInserts { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DatabasePath => _inner.DatabasePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);

        public async Task<KillmailWriteResult> InsertBatchAsync(IReadOnlyList<KillmailObservation> observations, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _inserts) == 1)
            {
                FirstInsertStarted.TrySetResult();
                await ReleaseInserts.Task.WaitAsync(cancellationToken);
            }

            return await _inner.InsertBatchAsync(observations, cancellationToken);
        }

        public Task RecordGapsAsync(IReadOnlyList<IntelligenceCollectionGap> gaps, CancellationToken cancellationToken = default)
            => _inner.RecordGapsAsync(gaps, cancellationToken);

        public Task<int> DeleteOccurredBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => _inner.DeleteOccurredBeforeAsync(cutoffUtc, maxRows, cancellationToken);

        public Task<int> DeleteGapsRecordedBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => _inner.DeleteGapsRecordedBeforeAsync(cutoffUtc, maxRows, cancellationToken);

        public Task<KillmailHistoryPage> GetTimeRangePageAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, KillmailHistoryCursor? after, CancellationToken cancellationToken = default)
            => _inner.GetTimeRangePageAsync(fromUtcInclusive, toUtcInclusive, limit, after, cancellationToken);

        public Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(int limit, CancellationToken cancellationToken = default)
            => _inner.GetCollectionGapsAsync(limit, cancellationToken);

        public Task<long> RecordCoverageDeltaAsync(string checkpointName, long observedTotal, IntelligenceCollectionGap gap, CancellationToken cancellationToken = default)
            => _inner.RecordCoverageDeltaAsync(checkpointName, observedTotal, gap, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => _inner.GetByTimeRangeAsync(fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(long solarSystemId, DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => _inner.GetBySolarSystemAsync(solarSystemId, fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default)
            => _inner.GetKillmailAsync(killmailId, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default)
            => _inner.GetRecentAsync(limit, offset, cancellationToken);

        public Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
            => _inner.GetHealthAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingHistoryRepository : IIntelligenceHistoryRepository
    {
        private readonly IIntelligenceHistoryRepository _inner;

        public BlockingHistoryRepository(IIntelligenceHistoryRepository inner) => _inner = inner;

        public TaskCompletionSource InsertStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DatabasePath => _inner.DatabasePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);

        public async Task<KillmailWriteResult> InsertBatchAsync(IReadOnlyList<KillmailObservation> observations, CancellationToken cancellationToken = default)
        {
            InsertStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new KillmailWriteResult(0, 0, 0);
        }

        public Task RecordGapsAsync(IReadOnlyList<IntelligenceCollectionGap> gaps, CancellationToken cancellationToken = default)
            => _inner.RecordGapsAsync(gaps, cancellationToken);

        public Task<int> DeleteOccurredBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => _inner.DeleteOccurredBeforeAsync(cutoffUtc, maxRows, cancellationToken);

        public Task<int> DeleteGapsRecordedBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => _inner.DeleteGapsRecordedBeforeAsync(cutoffUtc, maxRows, cancellationToken);

        public Task<KillmailHistoryPage> GetTimeRangePageAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, KillmailHistoryCursor? after, CancellationToken cancellationToken = default)
            => _inner.GetTimeRangePageAsync(fromUtcInclusive, toUtcInclusive, limit, after, cancellationToken);

        public Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(int limit, CancellationToken cancellationToken = default)
            => _inner.GetCollectionGapsAsync(limit, cancellationToken);

        public Task<long> RecordCoverageDeltaAsync(string checkpointName, long observedTotal, IntelligenceCollectionGap gap, CancellationToken cancellationToken = default)
            => _inner.RecordCoverageDeltaAsync(checkpointName, observedTotal, gap, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => _inner.GetByTimeRangeAsync(fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(long solarSystemId, DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => _inner.GetBySolarSystemAsync(solarSystemId, fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default)
            => _inner.GetKillmailAsync(killmailId, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default)
            => _inner.GetRecentAsync(limit, offset, cancellationToken);

        public Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
            => _inner.GetHealthAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnavailableHistoryRepository : IIntelligenceHistoryRepository
    {
        public UnavailableHistoryRepository(string databasePath) => DatabasePath = databasePath;

        public string DatabasePath { get; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<KillmailWriteResult> InsertBatchAsync(IReadOnlyList<KillmailObservation> observations, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("database unavailable");

        public Task RecordGapsAsync(IReadOnlyList<IntelligenceCollectionGap> gaps, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("gap store unavailable");

        public Task<int> DeleteOccurredBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteGapsRecordedBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<KillmailHistoryPage> GetTimeRangePageAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, KillmailHistoryCursor? after, CancellationToken cancellationToken = default)
            => Task.FromResult(new KillmailHistoryPage([], null));

        public Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IntelligenceCollectionGap>>([]);

        public Task<long> RecordCoverageDeltaAsync(string checkpointName, long observedTotal, IntelligenceCollectionGap gap, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("checkpoint unavailable");

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KillmailHistoryRecord>>([]);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(long solarSystemId, DateTimeOffset fromUtcInclusive, DateTimeOffset toUtcInclusive, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KillmailHistoryRecord>>([]);

        public Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default)
            => Task.FromResult<KillmailHistoryDetail?>(null);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KillmailHistoryRecord>>([]);

        public Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new IntelligenceHistoryHealth(0, 0, 0, 1, false, 0, DatabasePath));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
