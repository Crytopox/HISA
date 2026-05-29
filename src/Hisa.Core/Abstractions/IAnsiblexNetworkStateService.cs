using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IAnsiblexNetworkStateService
{
    event EventHandler? SnapshotUpdated;
    IReadOnlyList<AnsiblexLinkEntry> CurrentLinks { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AnsiblexLinkRecord>> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<AnsiblexImportResult> ImportFromTextAsync(string rawText, SovImportMode mode, CancellationToken cancellationToken = default);
    Task AddOrUpdateLinkAsync(string fromSystemName, string toSystemName, CancellationToken cancellationToken = default);
    Task RemoveLinkAsync(string fromSystemName, string toSystemName, CancellationToken cancellationToken = default);
}
