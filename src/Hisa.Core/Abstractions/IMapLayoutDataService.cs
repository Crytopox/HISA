using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IMapLayoutDataService
{
    Task<MapGraph?> TryGetRegionLayoutGraphAsync(int regionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MapLayoutRegionSummary>> GetLayoutRegionsAsync(CancellationToken cancellationToken = default);
}
