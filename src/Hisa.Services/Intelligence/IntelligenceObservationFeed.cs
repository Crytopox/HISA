using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Intelligence;

public sealed class IntelligenceObservationFeed : IIntelligenceObservationFeed
{
    private readonly ILogger<IntelligenceObservationFeed> _logger;
    private int _pendingDeliveries;

    public IntelligenceObservationFeed(ILogger<IntelligenceObservationFeed> logger)
    {
        _logger = logger;
    }

    public event EventHandler<KillmailObservation>? ObservationReceived;

    public void Publish(KillmailObservation observation)
    {
        var handler = ObservationReceived;
        if (handler is null)
        {
            return;
        }

        var subscribers = handler.GetInvocationList();
        Interlocked.Increment(ref _pendingDeliveries);
        try
        {
            ThreadPool.QueueUserWorkItem(static state =>
            {
                var delivery = (Delivery)state!;
                delivery.Feed.Deliver(delivery.Subscribers, delivery.Observation);
            }, new Delivery(this, subscribers, observation));
        }
        catch
        {
            Interlocked.Decrement(ref _pendingDeliveries);
            throw;
        }
    }

    internal bool WaitForIdle(TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Volatile.Read(ref _pendingDeliveries) > 0)
        {
            if (Environment.TickCount64 >= until)
            {
                return false;
            }

            Thread.Sleep(1);
        }

        return true;
    }

    private void Deliver(Delegate[] subscribers, KillmailObservation observation)
    {
        try
        {
            foreach (var subscriber in subscribers)
            {
                try
                {
                    ((EventHandler<KillmailObservation>)subscriber).Invoke(this, observation);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Intelligence observation subscriber failed for killmail {KillmailId}.",
                        observation.KillmailId);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingDeliveries);
        }
    }

    private sealed class Delivery(IntelligenceObservationFeed feed, Delegate[] subscribers, KillmailObservation observation)
    {
        public IntelligenceObservationFeed Feed { get; } = feed;
        public Delegate[] Subscribers { get; } = subscribers;
        public KillmailObservation Observation { get; } = observation;
    }
}
