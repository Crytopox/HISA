using Hisa.Core.Models.Intelligence;
using Hisa.Services.Intelligence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hisa.Services.Tests;

public class IntelligenceObservationFeedTests
{
    [Fact]
    public async Task Publish_DeliversObservationsInOrder()
    {
        var feed = CreateFeed();
        try
        {
            var received = new List<long>();
            feed.ObservationReceived += (_, observation) => received.Add(observation.KillmailId);

            feed.Publish(Observation(1));
            feed.Publish(Observation(2));
            feed.Publish(Observation(3));

            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            Assert.Equal([1L, 2L, 3L], received);
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Publish_DoesNotInvokeSubscriberConcurrently()
    {
        var feed = CreateFeed();
        try
        {
            var current = 0;
            var maxOverlap = 0;
            feed.ObservationReceived += (_, _) =>
            {
                var now = Interlocked.Increment(ref current);
                UpdateMax(ref maxOverlap, now);
                Thread.Sleep(40);
                Interlocked.Decrement(ref current);
            };

            feed.Publish(Observation(1));
            feed.Publish(Observation(2));
            feed.Publish(Observation(3));

            Assert.True(feed.WaitForIdle(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, maxOverlap);
        }
        finally
        {
            await feed.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Publish_StopsAtCapacityAndCountsRejections()
    {
        var feed = new IntelligenceObservationFeed(
            NullLogger<IntelligenceObservationFeed>.Instance,
            new IntelligenceObservationFeedOptions { Capacity = 2 });

        for (var id = 1; id <= 10; id++)
        {
            feed.Publish(Observation(id));
        }

        Assert.Equal(2, feed.Capacity);
        Assert.Equal(2, feed.QueueDepth);
        Assert.Equal(8, feed.RejectedEnqueueCount);
    }

    [Fact]
    public async Task StopAsync_ReturnsWhileSubscriberIsBlockedAndCountsUndeliveredObservations()
    {
        var feed = new IntelligenceObservationFeed(
            NullLogger<IntelligenceObservationFeed>.Instance,
            new IntelligenceObservationFeedOptions
            {
                Capacity = 4,
                ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200)
            });
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        feed.ObservationReceived += (_, _) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };

        await feed.StartAsync(CancellationToken.None);
        feed.Publish(Observation(1));
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        feed.Publish(Observation(2));

        var stopTask = feed.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2)));
        release.Set();
        Assert.Same(stopTask, completed);
        await stopTask;
        Assert.True(feed.UndeliveredCount >= 1);
    }

    private static IntelligenceObservationFeed CreateFeed()
    {
        var feed = new IntelligenceObservationFeed(NullLogger<IntelligenceObservationFeed>.Instance);
        feed.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return feed;
    }

    private static KillmailObservation Observation(long killmailId)
    {
        return new KillmailObservation
        {
            KillmailId = killmailId,
            Source = KillmailObservation.R2Z2SourceId,
            SourceSequence = killmailId,
            SolarSystemId = 30000142,
            OccurredAtUtc = DateTimeOffset.UnixEpoch,
            ReceivedAtUtc = DateTimeOffset.UnixEpoch,
            Victim = new KillmailVictim { ShipTypeId = 670 },
            Attackers = []
        };
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        while (true)
        {
            var seen = Volatile.Read(ref target);
            if (candidate <= seen || Interlocked.CompareExchange(ref target, candidate, seen) == seen)
            {
                return;
            }
        }
    }
}
