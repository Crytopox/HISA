using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Microsoft.Extensions.Logging;

namespace Hisa.Services.Intelligence;

public sealed class IntelligenceObservationFeed : IIntelligenceObservationFeed
{
    private readonly ILogger<IntelligenceObservationFeed> _logger;

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

        foreach (var subscriber in handler.GetInvocationList())
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
}
