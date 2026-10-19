using System.Threading.Channels;
using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hisa.Services.Intelligence;

public sealed class IntelligenceObservationFeed : BackgroundService, IIntelligenceObservationFeed
{
    private readonly ILogger<IntelligenceObservationFeed> _logger;
    private readonly IntelligenceObservationFeedOptions _options;
    private readonly Channel<KillmailObservation> _channel;
    private readonly object _readGate = new();
    private int _outstanding;
    private int _stopReading;
    private long _rejectedEnqueues;
    private long _deliveryFailures;
    private long _undelivered;
    private long _drainDeadlineTicks = long.MaxValue;

    public IntelligenceObservationFeed(ILogger<IntelligenceObservationFeed> logger)
        : this(logger, new IntelligenceObservationFeedOptions())
    {
    }

    public IntelligenceObservationFeed(
        ILogger<IntelligenceObservationFeed> logger,
        IOptions<IntelligenceObservationFeedOptions> options)
        : this(logger, options.Value)
    {
    }

    public IntelligenceObservationFeed(
        ILogger<IntelligenceObservationFeed> logger,
        IntelligenceObservationFeedOptions options)
    {
        if (options.Capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Observation queue capacity must be at least 1.");
        }

        if (options.ShutdownDrainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Observation shutdown drain timeout must be positive.");
        }

        _logger = logger;
        _options = options;
        _channel = Channel.CreateBounded<KillmailObservation>(new BoundedChannelOptions(options.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public event EventHandler<KillmailObservation>? ObservationReceived;

    public int Capacity => _options.Capacity;

    public int QueueDepth => _channel.Reader.Count;

    public long RejectedEnqueueCount => Volatile.Read(ref _rejectedEnqueues);

    public long DeliveryFailureCount => Volatile.Read(ref _deliveryFailures);

    public long UndeliveredCount => Volatile.Read(ref _undelivered);

    public void Publish(KillmailObservation observation)
    {
        Interlocked.Increment(ref _outstanding);
        if (_channel.Writer.TryWrite(observation))
        {
            return;
        }

        Interlocked.Decrement(ref _outstanding);
        var rejected = Interlocked.Increment(ref _rejectedEnqueues);
        _logger.LogWarning(
            "Observation queue is full. Rejected killmail {KillmailId} at sequence {Sequence}. Capacity: {Capacity}. QueueDepth: {QueueDepth}. RejectedEnqueueCount: {RejectedEnqueueCount}.",
            observation.KillmailId,
            observation.SourceSequence,
            _options.Capacity,
            QueueDepth,
            rejected);
    }

    internal bool WaitForIdle(TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Volatile.Read(ref _outstanding) > 0)
        {
            if (Environment.TickCount64 >= until)
            {
                return false;
            }

            Thread.Sleep(1);
        }

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (Volatile.Read(ref _stopReading) == 0)
            {
                if (!await _channel.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    return;
                }

                if (!DrainAvailable())
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Observation feed consumer failed.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var drainTimeout = _options.ShutdownDrainTimeout;
        Volatile.Write(ref _drainDeadlineTicks, Environment.TickCount64 + (long)drainTimeout.TotalMilliseconds);
        _channel.Writer.TryComplete();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(drainTimeout);
        try
        {
            await base.StopAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Observation feed shutdown exceeded the {DrainTimeoutSeconds} second drain limit.",
                drainTimeout.TotalSeconds);
        }

        AbandonQueued();
    }

    private bool DrainAvailable()
    {
        while (Volatile.Read(ref _stopReading) == 0)
        {
            KillmailObservation observation;
            lock (_readGate)
            {
                if (Volatile.Read(ref _stopReading) != 0 || !_channel.Reader.TryRead(out var next))
                {
                    return true;
                }

                observation = next;
            }

            Deliver(observation);
            if (Environment.TickCount64 >= Volatile.Read(ref _drainDeadlineTicks))
            {
                Volatile.Write(ref _stopReading, 1);
                return false;
            }
        }

        return false;
    }

    private void Deliver(KillmailObservation observation)
    {
        try
        {
            var handler = ObservationReceived;
            if (handler is null)
            {
                return;
            }

            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((EventHandler<KillmailObservation>)subscriber).Invoke(this, observation);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _deliveryFailures);
                    _logger.LogWarning(
                        ex,
                        "Intelligence observation subscriber failed for killmail {KillmailId}.",
                        observation.KillmailId);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _outstanding);
        }
    }

    private void AbandonQueued()
    {
        Volatile.Write(ref _stopReading, 1);
        var abandoned = 0;
        lock (_readGate)
        {
            while (_channel.Reader.TryRead(out var observation))
            {
                abandoned++;
                Interlocked.Decrement(ref _outstanding);
                _logger.LogWarning(
                    "Undelivered observation at shutdown. KillmailId: {KillmailId}. Sequence: {Sequence}.",
                    observation.KillmailId,
                    observation.SourceSequence);
            }
        }

        if (abandoned == 0)
        {
            return;
        }

        var total = Interlocked.Add(ref _undelivered, abandoned);
        _logger.LogWarning(
            "Observation feed shutdown left {AbandonedCount} observations undelivered. UndeliveredCount: {UndeliveredCount}.",
            abandoned,
            total);
    }
}
