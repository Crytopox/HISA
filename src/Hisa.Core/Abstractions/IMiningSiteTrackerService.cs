using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IMiningSiteTrackerService
{
    event EventHandler? ReportsUpdated;
    IReadOnlyDictionary<string, MiningSiteReport> CurrentReports { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SetClearedAsync(int solarSystemId, string upgradeName, int tier, CancellationToken cancellationToken = default);
    Task SetMissingAsync(int solarSystemId, string upgradeName, int tier, TimeSpan reminderDelay, CancellationToken cancellationToken = default);
    Task MarkAvailableAsync(int solarSystemId, string upgradeName, int tier, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MiningSiteReportRecord>> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MiningSiteReport>> GetDueReportsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<bool> MarkAlertEmittedAsync(MiningSiteReport report, DateTime emittedAtUtc, CancellationToken cancellationToken = default);
}
