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
    private readonly Channel<QueuedWork> _channel;
    private int _subscribed;
    private int _feedCoverageRecorded;
    private DateTime _nextRetentionUtc;

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
            options.RetentionPeriod <= TimeSpan.Zero ||
            options.RetentionInterval <= TimeSpan.Zero ||
            options.MaxWriteAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Persistence timings and write attempts must be positive.");
        }

        _repository = repository;
        _feed = feed;
        _logger = logger;
        _options = options;
        _nextRetentionUtc = DateTime.UtcNow;
        _channel = Channel.CreateBounded<QueuedWork>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public IntelligencePersistenceSnapshot GetDiagnostics() => _diagnostics.Snapshot(_channel.Reader.Count);

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
        if (_channel.Reader.Count > 0 || DateTime.UtcNow >= _nextRetentionUtc || _diagnostics.HasPendingSummaries)
        {
            return true;
        }

        var delay = _nextRetentionUtc - DateTime.UtcNow;
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (delay > TimeSpan.Zero)
        {
            wake.CancelAfter(delay);
        }

        try
        {
            return await _channel.Reader.WaitToReadAsync(wake.Token).ConfigureAwait(false)
                || _channel.Reader.Count > 0
                || _diagnostics.HasPendingSummaries;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return _channel.Reader.Count > 0 || DateTime.UtcNow >= _nextRetentionUtc || _diagnostics.HasPendingSummaries;
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

        var summaryStart = gaps.Count;
        _diagnostics.AppendPending(gaps);
        if (gaps.Count == 0)
        {
            await RunRetentionIfDueAsync(cancellationToken).ConfigureAwait(false);
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
                _diagnostics.RecordFailed(1);
                _logger.LogError(ex, "Failed to persist killmail {KillmailId}.", observation.KillmailId);
                await TryRecordWriteFailureAsync(observation, ex, cancellationToken).ConfigureAwait(false);
            }
        }

        batch.Clear();
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
        using var timeout = new CancellationTokenSource(_options.ShutdownFlushTimeout);
        try
        {
            Drain(observations, gaps, int.MaxValue);
            if (observations.Count > 0)
            {
                await CommitBatchAsync(observations, timeout.Token).ConfigureAwait(false);
                observations.Clear();
            }

            _diagnostics.AppendPending(gaps);
            if (gaps.Count > 0)
            {
                await _repository.RecordGapsAsync(gaps, timeout.Token).ConfigureAwait(false);
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

        if (observations.Count > 0)
        {
            await RecordUndeliveredAsync(observations, timeout.Token).ConfigureAwait(false);
        }

        await RecordFeedCoverageAsync(timeout.Token).ConfigureAwait(false);
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
        try
        {
            await _repository.RecordGapsAsync(gaps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record {Count} undelivered intelligence killmails.", observations.Count);
        }

        observations.Clear();
    }

    private async Task RecordFeedCoverageAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _feedCoverageRecorded, 1) != 0 ||
            _feed is not IntelligenceObservationFeed feed)
        {
            return;
        }

        var gaps = new List<IntelligenceCollectionGap>(2);
        if (feed.RejectedEnqueueCount > 0)
        {
            gaps.Add(new IntelligenceCollectionGap
            {
                Kind = IntelligenceCollectionGapKind.ObservationRejected,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Source = KillmailObservation.R2Z2SourceId,
                Count = ToCount(feed.RejectedEnqueueCount),
                Detail = "Observation feed rejected publishes because its queue was full."
            });
        }

        if (feed.UndeliveredCount > 0)
        {
            gaps.Add(new IntelligenceCollectionGap
            {
                Kind = IntelligenceCollectionGapKind.ObservationUndelivered,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                Source = KillmailObservation.R2Z2SourceId,
                Count = ToCount(feed.UndeliveredCount),
                Detail = "Observation feed stopped before these observations were delivered."
            });
        }

        if (gaps.Count == 0)
        {
            return;
        }

        try
        {
            await _repository.RecordGapsAsync(gaps, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record observation-feed coverage gaps.");
        }
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
            _logger.LogWarning(ex, "Failed to record a write failure for killmail {KillmailId}.", observation.KillmailId);
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

            _diagnostics.RecordOverflow(observation.KillmailId);
            _logger.LogWarning(
                "Intelligence persistence queue is full. Dropped killmail {KillmailId} at sequence {Sequence}. Capacity: {Capacity}.",
                observation.KillmailId,
                observation.SourceSequence,
                _options.QueueCapacity);
        }
        catch (Exception ex)
        {
            _diagnostics.RecordOverflow(observation.KillmailId);
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

    private TimeSpan Backoff(int attempt)
    {
        var shift = Math.Min(attempt - 1, 6);
        var delay = TimeSpan.FromTicks(_options.WriteRetryBaseDelay.Ticks << shift);
        return delay <= _options.WriteRetryMaxDelay ? delay : _options.WriteRetryMaxDelay;
    }

    private static bool IsTransient(Exception exception)
    {
        return exception is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6;
    }

    private static int ToCount(long value) => value > int.MaxValue ? int.MaxValue : (int)value;

    private abstract record QueuedWork;

    private sealed record QueuedObservation(KillmailObservation Observation) : QueuedWork;

    private sealed record QueuedGap(IntelligenceCollectionGap Gap) : QueuedWork;
}
