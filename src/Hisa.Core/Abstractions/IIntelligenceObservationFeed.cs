using Hisa.Core.Models.Intelligence;

namespace Hisa.Core.Abstractions;

public interface IIntelligenceObservationFeed
{
    event EventHandler<KillmailObservation>? ObservationReceived;

    void Publish(KillmailObservation observation);
}
