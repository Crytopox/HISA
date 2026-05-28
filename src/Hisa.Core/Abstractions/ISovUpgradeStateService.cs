using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface ISovUpgradeStateService
{
    event EventHandler? SnapshotUpdated;
    IReadOnlyDictionary<int, IReadOnlyList<SovUpgradeEntry>> CurrentBySystemId { get; }
    Task<IReadOnlyList<SovSystemUpgradeRecord>> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<SovImportResult> ImportFromTextAsync(string rawText, SovImportMode mode, CancellationToken cancellationToken = default);
    Task AddOrUpdateUpgradeAsync(string systemName, string upgradeName, int tier, CancellationToken cancellationToken = default);
    Task RemoveSystemAsync(string systemName, CancellationToken cancellationToken = default);
}
