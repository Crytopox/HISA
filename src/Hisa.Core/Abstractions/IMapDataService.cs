using Hisa.Core.Models;

namespace Hisa.Core.Abstractions;

public interface IMapDataService
{
    Task<IReadOnlyList<RegionOption>> GetRegionsAsync(CancellationToken cancellationToken = default);
    Task<MapGraph> GetUniverseGraphAsync(MapCoordinateMode coordinateMode, CancellationToken cancellationToken = default);
    Task<MapGraph> GetUniverseRegionsGraphAsync(MapCoordinateMode coordinateMode, CancellationToken cancellationToken = default);
    Task<MapGraph> GetRegionGraphAsync(int regionId, MapCoordinateMode coordinateMode, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, int>> GetSystemNeighborCountsAsync(IReadOnlyCollection<long> systemIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MapSearchCandidate>> SearchAsync(string term, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<long, MapSystemMetadata>> GetSystemMetadataByIdsAsync(IReadOnlyCollection<long> systemIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MapSystemPosition>> GetSystemsWithSdeCoordinatesAsync(CancellationToken cancellationToken = default);
}
