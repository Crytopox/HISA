using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IFleetBoostTracker
{
    event EventHandler? Changed;
    IReadOnlyList<string> Channels { get; }
    FleetBoostSnapshot GetSnapshot(string channel);
    Task ApplySettingsAsync(CancellationToken cancellationToken = default);
    Task StartOrResetAsync(string channel, CancellationToken cancellationToken = default);
    Task RemovePilotAsync(string channel, string pilot, CancellationToken cancellationToken = default);
}
