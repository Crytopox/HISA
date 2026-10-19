using System.Text.Json;
using System.Threading.Channels;
using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Hisa.Services.Intelligence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Services.Background;

public sealed class IntelligencePersistenceHostedService : BackgroundService, IIntelligenceCoverageRecorder
{
    private readonly IIntelligenceHistoryRepository _repository;
    private readonly IIntelligenceObservationFeed _feed;
    private readonly ILogger<IntelligencePersistenceHostedService> _logger;
    private readonly IntelligencePersistenceOptions _options;
    private readonly IntelligencePersistenceDiagnostics _diagnostics = new();
    private readonly IntelligenceRecoverySpool _spool;
    private readonly Channel<QueuedWork> _channel;
    private int _subscribed;
    private DateTime _nextRetentionUtc;
    private DateTime _nextCoverageCheckpointUtc;
    private DateTime _nextSpoolRetryUtc = DateTime.MinValue;

    public IntelligencePersistenceHostedService(
        IIntelligenceHistoryRepository repository,
        IIntelligenceObservationFeed feed,
        ILogger<IntelligencePersistenceHostedService> logger,
        IOptions<IntelligencePersistenceOptions> options)
        : this(repository, feed, logger, options.Value)
    {
    }

    public IntelligencePersistenceHostedService(
        IIntelligenceHistoryRepository repository,
        IIntelligenceObservationFeed feed,
        ILogger<IntelligencePersistenceHostedService> logger,
        IntelligencePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(options);
        if (options.QueueCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Persistence queue capacity must be at least 1.");
        }

        if (options.BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Persistence batch size must be at least 1.");
        }

        if (options.MaxBatchWait <= TimeSpan.Zero ||
            options.ShutdownFlushTimeout <= TimeSpan.Zero ||
            options.ShutdownGapReportTimeout <= TimeSpan.Zero ||
            options.RetentionPeriod <= TimeSpan.Zero ||
            options.GapRetentionPeriod <= TimeSpan.Zero ||
            options.RetentionInterval <= TimeSpan.Zero ||
            options.SpoolRetryDelay <= TimeSpan.Zero ||
            options.CoverageCheckpointInterval <= TimeSpan.Zero ||
            options.MaxWriteAttempts < 1 ||
            options.MaxSpoolEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Persistence timings and write attempts must be positive.");
        }

        _repository = repository;
        _feed = feed;
        _logger = logger;
        _options = options;
        _nextRetentionUtc = DateTime.UtcNow;
        _nextCoverageCheckpointUtc = DateTime.UtcNow;
        _spool = new IntelligenceRecoverySpool(
            IntelligencePersistenceOptions.ResolveSpoolDirectory(options, repository.DatabasePath),
            options.MaxSpoolEntries);
        _channel = Channel.CreateBounded<QueuedWork>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public IntelligencePersistenceSnapshot GetDiagnostics() => _diagnostics.Snapshot(_channel.Reader.Count, _spool.PendingCount);

    public void Record(IntelligenceCollectionGap gap)
    {
        try
        {
            if (_channel.Writer.TryWrite(new QueuedGap(gap)))
            {
                _diagnostics.NoteDepth(_channel.Reader.Count);
                return;
            }

            _diagnostics.RecordCoalescedGap(gap);
            _logger.LogWarning(
                "Intelligence persistence queue is full. Coalesced coverage gap {Kind} at sequence {Sequence}.",
                gap.Kind,
                gap.SourceSequence);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordCoalescedGap(gap);
            _logger.LogWarning(ex, "Failed to queue intelligence coverage gap {Kind}.", gap.Kind);
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _spool.Scan();
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            _feed.ObservationReceived += OnObservationReceived;
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        _channel.Writer.TryComplete();
        try
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Intelligence persistence shutdown exceeded the {FlushTimeoutSeconds} second flush limit.",
                _options.ShutdownFlushTimeout.TotalSeconds);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var observations = new List<KillmailObservation>(_options.BatchSize);
        var gaps = new List<IntelligenceCollectionGap>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (observations.Count == 0 && gaps.Count == 0)
                {
                    var hasWork = await WaitForWorkAsync(stoppingToken).ConfigureAwait(false);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!hasWork && _channel.Reader.Completion.IsCompleted)
                    {
                        break;
                    }
                }

                await CollectBatchAsync(observations, gaps, stoppingToken).ConfigureAwait(false);
                await PersistCycleAsync(observations, gaps, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await ShutdownFlushAsync(observations, gaps).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForWorkAsync(CancellationToken stoppingToken)
    {
        if (HasScheduledWork())
        {
            return true;
        }

        var delay = NextWakeUtc() - DateTime.UtcNow;
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (delay > TimeSpan.Zero)
        {
            wake.CancelAfter(delay);
        }

        try
        {
            return await _channel.Reader.WaitToReadAsync(wake.Token).ConfigureAwait(false) || HasScheduledWork();
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return HasScheduledWork();
        }
    }

    private async Task CollectBatchAsync(
        List<KillmailObservation> observations,
        List<IntelligenceCollectionGap> gaps,
        CancellationToken stoppingToken)
    {
        Drain(observations, gaps, _options.BatchSize);
        if (observations.Count >= _options.BatchSize)
        {
            return;
        }

        var deadline = Environment.TickCount64 + (long)_options.MaxBatchWait.TotalMilliseconds;
        while (observations.Count < _options.BatchSize)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                break;
            }

            using var slice = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            slice.CancelAfter(TimeSpan.FromMilliseconds(remaining));
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(slice.Token).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                break;
            }

            Drain(observations, gaps, _options.BatchSize);
        }
    }

    private async Task PersistCycleAsync(
        List<KillmailObservation> observations,
        List<IntelligenceCollectionGap> gaps,
        CancellationToken cancellationToken)
    {
        if (observations.Count > 0)
        {
            await CommitBatchAsync(observations, cancellationToken).ConfigureAwait(false);
            observations.Clear();
        }

        await ReplaySpoolAsync(cancellationToken).ConfigureAwait(false);

        var summaryStart = gaps.Count;
        _diagnostics.AppendPending(gaps);
        if (gaps.Count == 0)
        {
            await RunRetentionIfDueAsync(cancellationToken).ConfigureAwait(false);
            await CheckpointFeedAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _repository.RecordGapsAsync(gaps, cancellationToken).ConfigureAwait(false);
            gaps.Clear();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record {Count} intelligence coverage gaps.", gaps.Count);
            if (gaps.Count > summaryStart)
            {
                _diagnostics.Restore(gaps.GetRange(summaryStart, gaps.Count - summaryStart));
                gaps.RemoveRange(summaryStart, gaps.Count - summaryStart);
            }

            await Task.Delay(Backoff(1), cancellationToken).ConfigureAwait(false);
        }

        await CheckpointFeedAsync(cancellationToken).ConfigureAwait(false);
        await RunRetentionIfDueAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CommitBatchAsync(List<KillmailObservation> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await _repository.InsertBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                _diagnostics.AddWrite(result);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < _options.MaxWriteAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Transient intelligence database error while writing {Count} killmails. Attempt {Attempt} of {MaxAttempts}.",
                    batch.Count,
                    attempt,
                    _options.MaxWriteAttempts);
                await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Intelligence batch insert failed for {Count} killmails. Writing the batch one killmail at a time.",
                    batch.Count);
                await CommitIndividuallyAsync(batch, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task CommitIndividuallyAsync(List<KillmailObservation> batch, CancellationToken cancellationToken)
    {
        var remaining = new List<KillmailObservation>();
        for (var index = 0; index < batch.Count; index++)
        {
            var observation = batch[index];
            try
            {
                var result = await InsertWithRetryAsync(observation, cancellationToken).ConfigureAwait(false);
                _diagnostics.AddWrite(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                remaining.Add(observation);
                for (var rest = index + 1; rest < batch.Count; rest++)
                {
                    remaining.Add(batch[rest]);
                }

                batch.Clear();
                batch.AddRange(remaining);
                throw;
            }
            catch (Exception ex)
            {
                await ParkFailedObservationAsync(observation, ex, cancellationToken).ConfigureAwait(false);
            }
        }

        batch.Clear();
    }

    private async Task ParkFailedObservationAsync(
        KillmailObservation observation,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (Classify(exception) == PersistenceFailure.Permanent)
        {
            _diagnostics.RecordPermanentlyRejected(1);
            _logger.LogError(exception, "Permanently rejected killmail {KillmailId}.", observation.KillmailId);
            await TryRecordWriteFailureAsync(observation, exception, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_spool.TryAppend(observation))
        {
            _nextSpoolRetryUtc = DateTime.UtcNow + _options.SpoolRetryDelay;
            _logger.LogWarning(
                exception,
                "Parked killmail {KillmailId} in the recovery spool after a storage failure.",
                observation.KillmailId);
            return;
        }

        _diagnostics.RecordLost(1);
        _logger.LogError(
            exception,
            "Lost killmail {KillmailId}. The database and recovery spool are both unavailable.",
            observation.KillmailId);
        await TryRecordWriteFailureAsync(observation, exception, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaySpoolAsync(CancellationToken cancellationToken)
    {
        if (_spool.PendingCount <= 0 || DateTime.UtcNow < _nextSpoolRetryUtc)
        {
            return;
        }

        foreach (var entry in _spool.ReadBatch(_options.BatchSize))
        {
            if (entry.Observation is null || entry.Observation.KillmailId <= 0)
            {
                _spool.Acknowledge(entry.Path);
                _diagnostics.RecordPermanentlyRejected(1);
                _logger.LogError("Permanently rejected an unreadable recovery spool entry at {Path}.", entry.Path);
                continue;
            }

            try
            {
                var result = await _repository.InsertBatchAsync([entry.Observation], cancellationToken).ConfigureAwait(false);
                _spool.Acknowledge(entry.Path);
                _diagnostics.AddWrite(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (Classify(ex) == PersistenceFailure.Permanent)
            {
                _spool.Acknowledge(entry.Path);
                _diagnostics.RecordPermanentlyRejected(1);
                _logger.LogError(ex, "Permanently rejected spooled killmail {KillmailId}.", entry.Observation.KillmailId);
                await TryRecordWriteFailureAsync(entry.Observation, ex, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _nextSpoolRetryUtc = DateTime.UtcNow + _options.SpoolRetryDelay;
                _logger.LogWarning(ex, "Recovery spool replay is waiting for the intelligence database.");
                return;
            }
        }
    }

    private async Task<KillmailWriteResult> InsertWithRetryAsync(
        KillmailObservation observation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _repository.InsertBatchAsync([observation], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < _options.MaxWriteAttempts)
            {
                await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RunRetentionIfDueAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _nextRetentionUtc)
        {
            return;
        }

        _nextRetentionUtc = DateTime.UtcNow + _options.RetentionInterval;
        try
        {
            var cutoff = DateTimeOffset.UtcNow - _options.RetentionPeriod;
            var deleted = 0;
            for (var batch = 0; batch < _options.MaxRetentionBatchesPerCycle; batch++)
            {
                var count = await _repository.DeleteOccurredBeforeAsync(
                    cutoff,
                    _options.RetentionBatchSize,
                    cancellationToken).ConfigureAwait(false);
                deleted += count;
                if (count < _options.RetentionBatchSize)
                {
                    break;
                }
            }

            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Deleted {Count} intelligence killmails that occurred before {Cutoff:o}.",
                    deleted,
                    cutoff);
            }

            var gapCutoff = DateTimeOffset.UtcNow - _options.GapRetentionPeriod;
            var gapsDeleted = 0;
            for (var batch = 0; batch < _options.MaxRetentionBatchesPerCycle; batch++)
            {
                var count = await _repository.DeleteGapsRecordedBeforeAsync(
                    gapCutoff,
                    _options.RetentionBatchSize,
                    cancellationToken).ConfigureAwait(false);
                gapsDeleted += count;
                if (count < _options.RetentionBatchSize)
                {
                    break;
                }
            }

            if (gapsDeleted > 0)
            {
                _logger.LogInformation(
                    "Deleted {Count} intelligence coverage gaps recorded before {Cutoff:o}.",
                    gapsDeleted,
                    gapCutoff);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Intelligence retention cleanup failed.");
        }
    }

    private async Task ShutdownFlushAsync(
        List<KillmailObservation> observations,
        List<IntelligenceCollectionGap> gaps)
    {
        using (var flush = new CancellationTokenSource(_options.ShutdownFlushTimeout))
        {
            try
            {
                Drain(observations, gaps, int.MaxValue);
                if (observations.Count > 0)
                {
                    await CommitBatchAsync(observations, flush.Token).ConfigureAwait(false);
                    observations.Clear();
                }

                await ReplaySpoolAsync(flush.Token).ConfigureAwait(false);
                _diagnostics.AppendPending(gaps);
                if (gaps.Count > 0)
                {
                    await _repository.RecordGapsAsync(gaps, flush.Token).ConfigureAwait(false);
                    gaps.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Intelligence persistence shutdown flush did not commit {KillmailCount} killmails and {GapCount} coverage gaps.",
                    observations.Count,
                    gaps.Count);
            }
        }

        using var report = new CancellationTokenSource(_options.ShutdownGapReportTimeout);
        var unspooled = new List<KillmailObservation>();
        foreach (var observation in observations)
        {
            if (_spool.TryAppend(observation))
            {
                continue;
            }

            unspooled.Add(observation);
        }

        observations.Clear();
        if (unspooled.Count > 0)
        {
            await RecordUndeliveredAsync(unspooled, report.Token).ConfigureAwait(false);
        }

        if (gaps.Count > 0)
        {
            await TryRecordGapsAsync(gaps, report.Token).ConfigureAwait(false);
        }

        await CheckpointFeedAsync(report.Token).ConfigureAwait(false);
    }

    private async Task RecordUndeliveredAsync(List<KillmailObservation> observations, CancellationToken cancellationToken)
    {
        _diagnostics.RecordUndelivered(observations.Count);
        var gaps = observations.Select(observation => new IntelligenceCollectionGap
        {
            Kind = IntelligenceCollectionGapKind.ShutdownUndelivered,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Source = observation.Source,
            SourceSequence = observation.SourceSequence,
            KillmailId = observation.KillmailId,
            Detail = "Persistence shutdown ended before this killmail was committed."
        }).ToArray();
        if (await TryRecordGapsAsync(gaps, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        _logger.LogWarning(
            "Shutdown coverage for {Count} killmails was logged but not persisted.",
            observations.Count);
    }

    private async Task<bool> TryRecordGapsAsync(
        IReadOnlyList<IntelligenceCollectionGap> gaps,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RecordGapsAsync(gaps, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.RecordLoggedOnly(gaps.Count);
            _logger.LogWarning(
                ex,
                "{Count} intelligence coverage gaps were logged but not persisted.",
                gaps.Count);
            return false;
        }
    }

    private async Task CheckpointFeedAsync(CancellationToken cancellationToken)
    {
        if (_feed is not IntelligenceObservationFeed feed)
        {
            return;
        }

        try
        {
            await RecordFeedDeltaAsync(
                IntelligenceCollectionGapKind.ObservationRejected,
                feed.RejectedEnqueueCount,
                "Observation feed rejected publishes because its queue was full.",
                cancellationToken).ConfigureAwait(false);
            await RecordFeedDeltaAsync(
                IntelligenceCollectionGapKind.ObservationUndelivered,
                feed.UndeliveredCount,
                "Observation feed stopped before these observations were delivered.",
                cancellationToken).ConfigureAwait(false);
            _nextCoverageCheckpointUtc = DateTime.UtcNow + _options.CoverageCheckpointInterval;
        }
        catch (Exception ex)
        {
            _diagnostics.RecordLoggedOnly(1);
            _logger.LogWarning(ex, "Observation-feed coverage delta was logged but not persisted.");
        }
    }

    private async Task RecordFeedDeltaAsync(
        string kind,
        long observedTotal,
        string detail,
        CancellationToken cancellationToken)
    {
        if (observedTotal <= 0)
        {
            return;
        }

        await _repository.RecordCoverageDeltaAsync(
            kind,
            observedTotal,
            new IntelligenceCollectionGap
            {
                Kind = kind,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Source = KillmailObservation.R2Z2SourceId,
                Detail = detail
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task TryRecordWriteFailureAsync(
        KillmailObservation observation,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RecordGapsAsync(
            [
                new IntelligenceCollectionGap
                {
                    Kind = IntelligenceCollectionGapKind.WriteFailure,
                    RecordedAtUtc = DateTimeOffset.UtcNow,
                    Source = observation.Source,
                    SourceSequence = observation.SourceSequence,
                    KillmailId = observation.KillmailId,
                    Detail = exception.Message
                }
            ], cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordLoggedOnly(1);
            _logger.LogWarning(ex, "Coverage gap for killmail {KillmailId} was logged but not persisted.", observation.KillmailId);
        }
    }

    private void Drain(List<KillmailObservation> observations, List<IntelligenceCollectionGap> gaps, int maxObservations)
    {
        while (_channel.Reader.TryPeek(out var peeked))
        {
            if (peeked is QueuedObservation && observations.Count >= maxObservations)
            {
                return;
            }

            if (!_channel.Reader.TryRead(out var work))
            {
                return;
            }

            switch (work)
            {
                case QueuedObservation queued:
                    observations.Add(queued.Observation);
                    break;
                case QueuedGap queued:
                    gaps.Add(queued.Gap);
                    break;
            }
        }
    }

    private void OnObservationReceived(object? sender, KillmailObservation observation)
    {
        _diagnostics.RecordReceived();
        try
        {
            if (_channel.Writer.TryWrite(new QueuedObservation(observation)))
            {
                _diagnostics.RecordQueued();
                _diagnostics.NoteDepth(_channel.Reader.Count);
                return;
            }

            _diagnostics.RecordOverflow(observation);
            _logger.LogWarning(
                "Intelligence persistence queue is full. Dropped killmail {KillmailId} at sequence {Sequence}. Capacity: {Capacity}.",
                observation.KillmailId,
                observation.SourceSequence,
                _options.QueueCapacity);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordOverflow(observation);
            _logger.LogWarning(ex, "Failed to queue killmail {KillmailId} for persistence.", observation.KillmailId);
        }
    }

    private void Unsubscribe()
    {
        if (Interlocked.Exchange(ref _subscribed, 0) == 1)
        {
            _feed.ObservationReceived -= OnObservationReceived;
        }
    }

    private bool HasScheduledWork()
    {
        var now = DateTime.UtcNow;
        return _channel.Reader.Count > 0
            || now >= _nextRetentionUtc
            || now >= _nextCoverageCheckpointUtc
            || (_spool.PendingCount > 0 && now >= _nextSpoolRetryUtc)
            || _diagnostics.HasPendingSummaries;
    }

    private DateTime NextWakeUtc()
    {
        var next = _nextRetentionUtc;
        if (_nextCoverageCheckpointUtc < next)
        {
            next = _nextCoverageCheckpointUtc;
        }

        if (_spool.PendingCount > 0 && _nextSpoolRetryUtc < next)
        {
            next = _nextSpoolRetryUtc;
        }

        return next;
    }

    private TimeSpan Backoff(int attempt)
    {
        var shift = Math.Min(attempt - 1, 6);
        var delay = TimeSpan.FromTicks(_options.WriteRetryBaseDelay.Ticks << shift);
        return delay <= _options.WriteRetryMaxDelay ? delay : _options.WriteRetryMaxDelay;
    }

    private static bool IsTransient(Exception exception) => Classify(exception) == PersistenceFailure.Transient;

    private static PersistenceFailure Classify(Exception exception)
    {
        if (exception is SqliteException sqlite)
        {
            if (sqlite.SqliteErrorCode is 5 or 6)
            {
                return PersistenceFailure.Transient;
            }

            if (sqlite.SqliteErrorCode == 19)
            {
                return PersistenceFailure.Permanent;
            }

            return PersistenceFailure.Storage;
        }

        if (exception is JsonException or FormatException or ArgumentException)
        {
            return PersistenceFailure.Permanent;
        }

        return PersistenceFailure.Storage;
    }

    private enum PersistenceFailure
    {
        Transient,
        Permanent,
        Storage
    }

    private abstract record QueuedWork;

    private sealed record QueuedObservation(KillmailObservation Observation) : QueuedWork;

    private sealed record QueuedGap(IntelligenceCollectionGap Gap) : QueuedWork;
}
