using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IMapLayoutEditorService
{
    Task<MapLayoutRegionSummary> CreateCustomRegionAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteLayoutRegionAsync(long layoutRegionId, CancellationToken cancellationToken = default);
    Task<MapGraph?> GetLayoutRegionGraphAsync(long layoutRegionId, CancellationToken cancellationToken = default);
    Task AddGameRegionsToLayoutAsync(long layoutRegionId, IReadOnlyList<int> sourceRegionIds, CancellationToken cancellationToken = default);
    Task SaveLayoutRegionGraphAsync(long layoutRegionId, MapGraph graph, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MapLink>> BuildAutoLinksForSystemsAsync(IReadOnlyCollection<long> solarSystemIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MapNode>> GetMissingConnectedSystemsAsync(
        IReadOnlyCollection<long> selectedSystemIds,
        IReadOnlyCollection<long> existingSystemIds,
        CancellationToken cancellationToken = default);
}
