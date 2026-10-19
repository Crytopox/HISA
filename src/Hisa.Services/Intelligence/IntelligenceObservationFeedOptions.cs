namespace Hisa.Services.Intelligence;

public sealed class IntelligenceObservationFeedOptions
{
    public const int DefaultCapacity = 256;

    // Reject new observations when the queue is full. TryWrite fails closed;
    // observations are counted and logged, never dropped by the channel.
    public int Capacity { get; set; } = DefaultCapacity;

    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
