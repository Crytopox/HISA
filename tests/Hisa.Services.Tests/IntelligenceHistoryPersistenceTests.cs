using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Hisa.Data.Database;
using Hisa.Services.Background;
using Hisa.Services.Intelligence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public sealed class IntelligenceHistoryPersistenceTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initialize_CreatesDatabaseOnFirstRun_AndIsIdempotent()
    {
        await using var database = new TempDatabase();
        Assert.False(File.Exists(database.Path));

        await database.Repository.InitializeAsync();
        await database.Repository.InitializeAsync();
        var health = await database.Repository.GetHealthAsync();

        Assert.True(File.Exists(database.Path));
        Assert.Equal(SqliteIntelligenceDatabaseInitializer.CurrentSchemaVersion, health.SchemaVersion);
        Assert.True(health.WalMode);
        Assert.Equal(0, health.KillmailCount);
        Assert.Equal(database.Path, health.DatabasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var initializer = new SqliteIntelligenceDatabaseInitializer();
        await initializer.InitializeAsync(connection, 2000);
        await initializer.InitializeAsync(connection, 2000);
        Assert.Equal(
            SqliteIntelligenceDatabaseInitializer.CurrentSchemaVersion,
            await SqliteIntelligenceDatabaseInitializer.ReadVersionAsync(connection, CancellationToken.None));
    }

    [Fact]
    public async Task Initialize_RejectsNewerSchemaVersion()
    {
        await using var database = new TempDatabase();
        await database.Repository.InitializeAsync();
        await database.Repository.DisposeAsync();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE SchemaVersion SET Version = 99 WHERE Id = 1;";
            await command.ExecuteNonQueryAsync();
        }

        var reopened = new SqliteIntelligenceHistoryRepository(database.Path, TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.InitializeAsync());
        await reopened.DisposeAsync();
    }

    [Fact]
    public async Task InsertAndReopen_PreservesKillmailParticipantsNullsAndDecimalValue()
    {
        var occurred = Epoch.AddMinutes(5);
        var totalValue = 1234567890123.4567m;
        var observation = Observation(
            42,
            occurred,
            30000142,
            totalValue,
            attackers:
            [
                new KillmailAttacker
                {
                    CharacterId = 9001,
                    CorporationId = 100,
                    AllianceId = null,
                    ShipTypeId = 587,
                    WeaponTypeId = 2456,
                    FinalBlow = true,
                    DamageDone = 1500
                },
                new KillmailAttacker
                {
                    CharacterId = null,
                    CorporationId = null,
                    AllianceId = null,
                    ShipTypeId = null,
                    WeaponTypeId = null,
                    FinalBlow = null,
                    DamageDone = null
                },
                new KillmailAttacker
                {
                    FinalBlow = false,
                    DamageDone = 0
                }
            ],
            victimCharacterId: null,
            victimCorporationId: 98000001,
            victimAllianceId: null,
            victimDamage: null,
            hash: null,
            sourceSequence: null);

        string path;
        var database = new TempDatabase { DeleteOnDispose = false };
        await using (database)
        {
            path = database.Path;
            await database.Repository.InitializeAsync();
            var result = await database.Repository.InsertBatchAsync([observation]);
            Assert.Equal(1, result.Inserted);

            var detail = await database.Repository.GetKillmailAsync(42);
            Assert.NotNull(detail);
            Assert.Equal(occurred, detail.Killmail.OccurredAtUtc);
            Assert.Equal(occurred.AddSeconds(2), detail.Killmail.ReceivedAtUtc);
            Assert.Null(detail.Killmail.Hash);
            Assert.Null(detail.Killmail.SourceSequence);
            Assert.Null(detail.Killmail.VictimCharacterId);
            Assert.Equal(98000001, detail.Killmail.VictimCorporationId);
            Assert.Null(detail.Killmail.VictimAllianceId);
            Assert.Null(detail.Killmail.VictimDamageTaken);
            Assert.Equal(totalValue, detail.Killmail.TotalValue);
            Assert.Equal(3, detail.Killmail.AttackerCount);
            Assert.Equal([0, 1, 2], detail.Participants.Select(participant => participant.ParticipantIndex).ToArray());
            Assert.True(detail.Participants[0].FinalBlow);
            Assert.Null(detail.Participants[1].FinalBlow);
            Assert.False(detail.Participants[2].FinalBlow);
            Assert.Null(detail.Participants[1].DamageDone);
            Assert.Equal(0, detail.Participants[2].DamageDone);
            Assert.Equal(9001, detail.Participants[0].CharacterId);
            Assert.Null(detail.Participants[1].CharacterId);
        }

        var reopened = new SqliteIntelligenceHistoryRepository(path, TimeSpan.FromSeconds(2));
        try
        {
            await reopened.InitializeAsync();
            var health = await reopened.GetHealthAsync();
            var detail = await reopened.GetKillmailAsync(42);
            Assert.Equal(1, health.KillmailCount);
            Assert.Equal(3, health.ParticipantCount);
            Assert.NotNull(detail);
            Assert.Equal(totalValue, detail.Killmail.TotalValue);
            Assert.Equal(3, detail.Participants.Count);
        }
        finally
        {
            await reopened.DisposeAsync();
            TryDeleteDirectory(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public async Task InsertBatch_DuplicateKillmailDoesNotDuplicateParticipants()
    {
        await using var database = new TempDatabase();
        await database.Repository.InitializeAsync();
        var observation = Observation(7, Epoch, attackers: [new KillmailAttacker { FinalBlow = true, DamageDone = 10 }]);

        var first = await database.Repository.InsertBatchAsync([observation, observation]);
        var second = await database.Repository.InsertBatchAsync([observation]);
        var health = await database.Repository.GetHealthAsync();

        Assert.Equal(1, first.Inserted);
        Assert.Equal(1, first.Duplicates);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(1, second.Duplicates);
        Assert.Equal(1, health.KillmailCount);
        Assert.Equal(1, health.ParticipantCount);
    }

    [Fact]
    public async Task InsertBatch_InterruptedTransactionLeavesNoOrphanedParticipants()
    {
        await using var database = new TempDatabase();
        await database.Repository.InitializeAsync();
        database.Repository.AfterKillmailInsert = _ => throw new InvalidOperationException("interrupted");
        var observation = Observation(8, Epoch, attackers: [new KillmailAttacker { DamageDone = 5 }]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Repository.InsertBatchAsync([observation]));
        var health = await database.Repository.GetHealthAsync();

        Assert.Equal(0, health.KillmailCount);
        Assert.Equal(0, health.ParticipantCount);
        Assert.Null(await database.Repository.GetKillmailAsync(8));
    }

    [Fact]
    public async Task Queries_FilterByTimeSystemAndPageInOccurrenceOrder()
    {
        await using var database = new TempDatabase();
        await database.Repository.InitializeAsync();
        var observations = Enumerable.Range(1, 5)
            .Select(id => Observation(id, Epoch.AddMinutes(id), systemId: id % 2 == 0 ? 200L : 100L))
            .ToArray();
        await database.Repository.InsertBatchAsync(observations);

        var range = await database.Repository.GetByTimeRangeAsync(Epoch.AddMinutes(2), Epoch.AddMinutes(4), 10);
        var system = await database.Repository.GetBySolarSystemAsync(100, Epoch, Epoch.AddHours(1), 10);
        var recent = await database.Repository.GetRecentAsync(2, 2);
        var pastEnd = await database.Repository.GetRecentAsync(2, 10);

        Assert.Equal([2L, 3L, 4L], range.Select(record => record.KillmailId).ToArray());
        Assert.Equal([1L, 3L, 5L], system.Select(record => record.KillmailId).ToArray());
        Assert.Equal([3L, 2L], recent.Select(record => record.KillmailId).ToArray());
        Assert.Empty(pastEnd);
    }

    [Fact]
    public async Task DeleteOccurredBefore_RemovesExpiredKillmailsAndParticipantsOnly()
    {
        await using var database = new TempDatabase();
        await database.Repository.InitializeAsync();
        await database.Repository.InsertBatchAsync(
        [
            Observation(1, Epoch, attackers: [new KillmailAttacker { DamageDone = 1 }]),
            Observation(2, Epoch.AddHours(49), attackers: [new KillmailAttacker { DamageDone = 2 }])
        ]);

        var deleted = await database.Repository.DeleteOccurredBeforeAsync(Epoch.AddHours(48), 100);
        var health = await database.Repository.GetHealthAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(1, health.KillmailCount);
        Assert.Equal(1, health.ParticipantCount);
        Assert.Null(await database.Repository.GetKillmailAsync(1));
        Assert.NotNull(await database.Repository.GetKillmailAsync(2));
    }

    [Fact]
    public async Task Persistence_WriteFailureDoesNotFailObservationDelivery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hisa-spool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        var repository = new ThrowingHistoryRepository(Path.Combine(directory, "hisa-intelligence.db"));
        var service = new IntelligencePersistenceHostedService(
            repository,
            feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance,
            new IntelligencePersistenceOptions
            {
                QueueCapacity = 16,
                BatchSize = 4,
                MaxBatchWait = TimeSpan.FromMilliseconds(20),
                MaxWriteAttempts = 2,
                WriteRetryBaseDelay = TimeSpan.FromMilliseconds(1),
                SpoolRetryDelay = TimeSpan.FromMinutes(5),
                RetentionPeriod = TimeSpan.FromDays(36500),
                RetentionInterval = TimeSpan.FromHours(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2)
            });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(Observation(1, Epoch));
            feed.Publish(Observation(2, Epoch.AddMinutes(1)));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            var recorded = await WaitForAsync(() => service.GetDiagnostics().Pending >= 2, TimeSpan.FromSeconds(3));

            Assert.True(recorded);
            Assert.Equal(0, feed.DeliveryFailureCount);
            Assert.Equal(2, service.GetDiagnostics().Received);
            Assert.Equal(0, service.GetDiagnostics().Committed);
            Assert.Equal(0, service.GetDiagnostics().Lost);
            Assert.Equal(0, service.GetDiagnostics().PermanentlyRejected);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await feed.StopAsync(CancellationToken.None);
            await repository.DisposeAsync();
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Persistence_QueueOverflowIsRecordedWithoutBlockingTheCallback()
    {
        var feed = new IntelligenceObservationFeed(
            NullLogger<IntelligenceObservationFeed>.Instance,
            new IntelligenceObservationFeedOptions { Capacity = 32 });
        await using var database = new TempDatabase();
        var repository = new GatedHistoryRepository(database.Repository);
        var service = new IntelligencePersistenceHostedService(
            repository,
            feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance,
            new IntelligencePersistenceOptions
            {
                QueueCapacity = 2,
                BatchSize = 1,
                MaxBatchWait = TimeSpan.FromMilliseconds(10),
                RetentionPeriod = TimeSpan.FromDays(36500),
                RetentionInterval = TimeSpan.FromHours(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2)
            });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        try
        {
            feed.Publish(Observation(1, Epoch));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            await repository.FirstInsertStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var started = Environment.TickCount64;
            feed.Publish(Observation(2, Epoch.AddMinutes(1)));
            feed.Publish(Observation(3, Epoch.AddMinutes(2)));
            feed.Publish(Observation(4, Epoch.AddMinutes(3)));
            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            Assert.True(Environment.TickCount64 - started < 1000);
            Assert.True(service.GetDiagnostics().QueueOverflow >= 1);

            repository.ReleaseInserts.TrySetResult();
            Assert.True(await WaitForAsync(
                () => service.GetDiagnostics().Committed + service.GetDiagnostics().Duplicates >= 3,
                TimeSpan.FromSeconds(3)));
        }
        finally
        {
            repository.ReleaseInserts.TrySetResult();
            await service.StopAsync(CancellationToken.None);
            await feed.StopAsync(CancellationToken.None);
        }

        var health = await database.Repository.GetHealthAsync();
        Assert.True(health.CollectionGapCount >= 1);
    }

    [Fact]
    public async Task Persistence_ShutdownFlushesPendingBatch()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        await using var database = new TempDatabase();
        var service = new IntelligencePersistenceHostedService(
            database.Repository,
            feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance,
            new IntelligencePersistenceOptions
            {
                QueueCapacity = 32,
                BatchSize = 50,
                MaxBatchWait = TimeSpan.FromSeconds(30),
                RetentionPeriod = TimeSpan.FromDays(36500),
                RetentionInterval = TimeSpan.FromHours(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(3)
            });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        feed.Publish(Observation(11, Epoch));
        feed.Publish(Observation(12, Epoch.AddMinutes(1)));
        feed.Publish(Observation(13, Epoch.AddMinutes(2)));
        Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));

        await feed.StopAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var health = await database.Repository.GetHealthAsync();
        Assert.Equal(3, health.KillmailCount);
        Assert.Equal(3, service.GetDiagnostics().Committed);
        Assert.Equal(0, service.GetDiagnostics().Undelivered);
    }

    [Fact]
    public async Task Persistence_SeveralThousandKillmailsReportThroughputQueueAndSize()
    {
        const int count = 4000;
        var feed = new IntelligenceObservationFeed(
            NullLogger<IntelligenceObservationFeed>.Instance,
            new IntelligenceObservationFeedOptions { Capacity = 4096 });
        await using var database = new TempDatabase();
        var service = new IntelligencePersistenceHostedService(
            database.Repository,
            feed,
            NullLogger<IntelligencePersistenceHostedService>.Instance,
            new IntelligencePersistenceOptions
            {
                QueueCapacity = 4096,
                BatchSize = 128,
                MaxBatchWait = TimeSpan.FromMilliseconds(20),
                RetentionPeriod = TimeSpan.FromDays(36500),
                RetentionInterval = TimeSpan.FromHours(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(10)
            });
        await feed.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (var id = 1; id <= count; id++)
            {
                feed.Publish(Observation(
                    id,
                    Epoch.AddSeconds(id),
                    attackers:
                    [
                        new KillmailAttacker { CharacterId = id, FinalBlow = true, DamageDone = id },
                        new KillmailAttacker { CharacterId = id + 1, FinalBlow = false, DamageDone = 1 },
                        new KillmailAttacker { ShipTypeId = 670, WeaponTypeId = 2456, DamageDone = 2 }
                    ]));
            }

            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(5)));
            var stored = await WaitForAsync(
                () => service.GetDiagnostics().Committed + service.GetDiagnostics().Duplicates >= count,
                TimeSpan.FromSeconds(30));
            started.Stop();
            Assert.True(stored);
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        }

        var diagnostics = service.GetDiagnostics();
        var health = await database.Repository.GetHealthAsync();
        var seconds = Math.Max(started.Elapsed.TotalSeconds, 0.001);
        var perSecond = count / seconds;
        Console.WriteLine(
            $"intelligence-history throughput={perSecond:F0}/s peakQueue={diagnostics.PeakQueueDepth} databaseBytes={health.DatabaseSizeBytes} elapsedMs={started.ElapsedMilliseconds} committed={diagnostics.Committed}");
        Assert.Equal(count, health.KillmailCount);
        Assert.Equal(count * 3, health.ParticipantCount);
        Assert.Equal(0, diagnostics.PermanentlyRejected);
        Assert.Equal(0, diagnostics.Lost);
        Assert.Equal(0, diagnostics.Undelivered);
        Assert.True(diagnostics.PeakQueueDepth > 0);
        Assert.True(health.DatabaseSizeBytes > 0);
    }

    private static KillmailObservation Observation(
        long killmailId,
        DateTimeOffset occurredAtUtc,
        long systemId = 30000142,
        decimal? totalValue = 1000m,
        IReadOnlyList<KillmailAttacker>? attackers = null,
        int? victimCharacterId = 2112000001,
        int? victimCorporationId = 98000001,
        int? victimAllianceId = 99000001,
        long? victimDamage = 2500,
        string? hash = "hash",
        long? sourceSequence = 0)
    {
        return new KillmailObservation
        {
            KillmailId = killmailId,
            Source = KillmailObservation.R2Z2SourceId,
            SourceSequence = sourceSequence == 0 ? killmailId : sourceSequence,
            SolarSystemId = systemId,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = occurredAtUtc.AddSeconds(2),
            Hash = hash,
            TotalValue = totalValue,
            Victim = new KillmailVictim
            {
                CharacterId = victimCharacterId,
                CorporationId = victimCorporationId,
                AllianceId = victimAllianceId,
                ShipTypeId = 670,
                DamageTaken = victimDamage
            },
            Attackers = attackers ?? [new KillmailAttacker { CharacterId = 1, FinalBlow = true, DamageDone = 10 }]
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

        public TempDatabase()
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hisa-intel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "hisa-intelligence.db");
            Repository = new SqliteIntelligenceHistoryRepository(Path, TimeSpan.FromSeconds(2));
        }

        public string Path { get; }

        public SqliteIntelligenceHistoryRepository Repository { get; }

        public bool DeleteOnDispose { get; set; } = true;

        public async ValueTask DisposeAsync()
        {
            await Repository.DisposeAsync();
            if (DeleteOnDispose)
            {
                TryDeleteDirectory(_directory);
            }
        }
    }

    private sealed class ThrowingHistoryRepository : IIntelligenceHistoryRepository
    {
        public ThrowingHistoryRepository(string databasePath)
        {
            DatabasePath = databasePath;
        }

        public string DatabasePath { get; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<KillmailWriteResult> InsertBatchAsync(
            IReadOnlyList<KillmailObservation> observations,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("database write failed");

        public Task RecordGapsAsync(IReadOnlyList<IntelligenceCollectionGap> gaps, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteOccurredBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteGapsRecordedBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<KillmailHistoryPage> GetTimeRangePageAsync(
            DateTimeOffset fromUtcInclusive,
            DateTimeOffset toUtcInclusive,
            int limit,
            KillmailHistoryCursor? after,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new KillmailHistoryPage([], null));

        public Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IntelligenceCollectionGap>>([]);

        public Task<long> RecordCoverageDeltaAsync(
            string checkpointName,
            long observedTotal,
            IntelligenceCollectionGap gap,
            CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(
            DateTimeOffset fromUtcInclusive,
            DateTimeOffset toUtcInclusive,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KillmailHistoryRecord>>([]);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(
            long solarSystemId,
            DateTimeOffset fromUtcInclusive,
            DateTimeOffset toUtcInclusive,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<KillmailHistoryRecord>>([]);

        public Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default)
            => Task.FromResult<KillmailHistoryDetail?>(null);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KillmailHistoryRecord>>([]);

        public Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new IntelligenceHistoryHealth(0, 0, 0, 1, true, 0, DatabasePath));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class GatedHistoryRepository : IIntelligenceHistoryRepository
    {
        private readonly IIntelligenceHistoryRepository _inner;
        private int _inserts;

        public GatedHistoryRepository(IIntelligenceHistoryRepository inner)
        {
            _inner = inner;
        }

        public TaskCompletionSource FirstInsertStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseInserts { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DatabasePath => _inner.DatabasePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);

        public async Task<KillmailWriteResult> InsertBatchAsync(
            IReadOnlyList<KillmailObservation> observations,
            CancellationToken cancellationToken = default)
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

        public Task<KillmailHistoryPage> GetTimeRangePageAsync(
            DateTimeOffset fromUtcInclusive,
            DateTimeOffset toUtcInclusive,
            int limit,
            KillmailHistoryCursor? after,
            CancellationToken cancellationToken = default)
            => _inner.GetTimeRangePageAsync(fromUtcInclusive, toUtcInclusive, limit, after, cancellationToken);

        public Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(int limit, CancellationToken cancellationToken = default)
            => _inner.GetCollectionGapsAsync(limit, cancellationToken);

        public Task<long> RecordCoverageDeltaAsync(
            string checkpointName,
            long observedTotal,
            IntelligenceCollectionGap gap,
            CancellationToken cancellationToken = default)
            => _inner.RecordCoverageDeltaAsync(checkpointName, observedTotal, gap, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(
            DateTimeOffset fromUtcInclusive,
            DateTimeOffset toUtcInclusive,
            int limit,
            CancellationToken cancellationToken = default)
            => _inner.GetByTimeRangeAsync(fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(
            long solarSystemId,
            DateTimeOffset fromUtcInclusive,
            DateTimeOffset toUtcInclusive,
            int limit,
            CancellationToken cancellationToken = default)
            => _inner.GetBySolarSystemAsync(solarSystemId, fromUtcInclusive, toUtcInclusive, limit, cancellationToken);

        public Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default)
            => _inner.GetKillmailAsync(killmailId, cancellationToken);

        public Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(int limit, int offset, CancellationToken cancellationToken = default)
            => _inner.GetRecentAsync(limit, offset, cancellationToken);

        public Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
            => _inner.GetHealthAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
