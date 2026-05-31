using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface ISystemActivityStateService
{
    SystemActivitySnapshot Current { get; }
    event EventHandler<SystemActivitySnapshot>? SystemActivitySnapshotUpdated;
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
