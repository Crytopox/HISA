using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Hisa.Services;

public sealed class MapDataService : IMapDataService
{
    private const int LayoutRegionSyntheticBase = 2_000_000_000;
    private static readonly Lazy<IReadOnlyDictionary<int, StaticSolarSystemData>> StaticSolarSystemDataById = new(LoadStaticSolarSystemDataById);
    private readonly ISdeDatabase _sdeDatabase;
    private readonly IMapLayoutDataService _mapLayoutDataService;
    private readonly IStormStateService _stormStateService;
    private readonly IHubWormholeStateService _hubWormholeStateService;
    private readonly ISovUpgradeStateService _sovUpgradeStateService;
    private readonly IIncursionStateService _incursionStateService;

    private sealed record StaticSolarSystemData(bool HasJoveObservatory, int IceFieldCount);

    public MapDataService(
        ISdeDatabase sdeDatabase,
        IMapLayoutDataService mapLayoutDataService,
        IStormStateService stormStateService,
        IHubWormholeStateService hubWormholeStateService,
        ISovUpgradeStateService sovUpgradeStateService,
        IIncursionStateService incursionStateService)
    {
        _sdeDatabase = sdeDatabase;
        _mapLayoutDataService = mapLayoutDataService;
        _stormStateService = stormStateService;
        _hubWormholeStateService = hubWormholeStateService;
        _sovUpgradeStateService = sovUpgradeStateService;
        _incursionStateService = incursionStateService;
    }

    public async Task<IReadOnlyList<RegionOption>> GetRegionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT regionID, regionName
            FROM mapRegions
            WHERE regionID IN (
                SELECT fromRegionID FROM mapRegionJumps
                UNION
                SELECT toRegionID FROM mapRegionJumps
            )
            ORDER BY regionName;
            """;

        var result = new List<RegionOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RegionOption
            {
                RegionId = reader.GetInt32(0),
                RegionName = reader.GetString(1),
                Kind = RegionOptionKind.Regular
            });
        }

        var layoutRegions = await _mapLayoutDataService.GetLayoutRegionsAsync(cancellationToken);
        foreach (var layoutRegion in layoutRegions.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (layoutRegion.IsGameRegion && layoutRegion.SourceRegionId is int sourceRegionId &&
                result.Any(x => x.RegionId == sourceRegionId))
            {
                continue;
            }

            if (layoutRegion.Id >= LayoutRegionSyntheticBase)
            {
                continue;
            }

            result.Add(new RegionOption
            {
                RegionId = -LayoutRegionSyntheticBase + (int)layoutRegion.Id,
                RegionName = layoutRegion.Name,
                Kind = layoutRegion.IsReadOnly ? RegionOptionKind.Combined : RegionOptionKind.Custom
            });
        }

        return result.OrderBy(x => x.RegionName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<MapGraph> GetUniverseGraphAsync(MapCoordinateMode coordinateMode, CancellationToken cancellationToken = default)
    {
        var systems = await QuerySystemsAsync(null, coordinateMode, cancellationToken);
        var links = await QuerySystemLinksAsync(null, cancellationToken);
        return BuildNormalizedGraph(systems, links);
    }

    public async Task<MapGraph> GetUniverseRegionsGraphAsync(MapCoordinateMode coordinateMode, CancellationToken cancellationToken = default)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var xColumn = coordinateMode == MapCoordinateMode.SdePlanarXY ? "s.x2D" : "s.x";
        var yColumn = coordinateMode == MapCoordinateMode.SdePlanarXY ? "s.y2D" : "s.z";
        var nodeCommand = connection.CreateCommand();
        nodeCommand.CommandText = $"""
            SELECT
                r.regionID,
                r.regionName,
                AVG({xColumn}) AS avgX,
                AVG({yColumn}) AS avgY
            FROM mapRegions r
            INNER JOIN mapSolarSystems s ON s.regionID = r.regionID
            WHERE {xColumn} IS NOT NULL AND {yColumn} IS NOT NULL
              AND r.regionID IN (
                    SELECT fromRegionID FROM mapRegionJumps
                    UNION
                    SELECT toRegionID FROM mapRegionJumps
              )
            GROUP BY r.regionID, r.regionName;
            """;

        var nodes = new List<MapNode>();
        await using (var reader = await nodeCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                nodes.Add(new MapNode
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    X = reader.GetDouble(2),
                    Y = reader.GetDouble(3),
                    RegionId = reader.GetInt32(0),
                    RegionName = reader.GetString(1)
                });
            }
        }

        var linkCommand = connection.CreateCommand();
        linkCommand.CommandText = """
            SELECT fromRegionID, toRegionID
            FROM mapRegionJumps;
            """;

        var links = new List<MapLink>();
        await using (var reader = await linkCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                links.Add(new MapLink
                {
                    FromId = reader.GetInt32(0),
                    ToId = reader.GetInt32(1)
                });
            }
        }

        return BuildNormalizedGraph(nodes, links);
    }

    public async Task<MapGraph> GetRegionGraphAsync(int regionId, MapCoordinateMode coordinateMode, CancellationToken cancellationToken = default)
    {
        if (TryGetLayoutRegionId(regionId, out var layoutRegionId))
        {
            var layoutGraph = await _mapLayoutDataService.TryGetLayoutRegionGraphAsync(layoutRegionId, cancellationToken);
            if (layoutGraph is not null)
            {
                return await EnrichLayoutGraphFromSdeAsync(layoutGraph, cancellationToken);
            }
        }

        if (coordinateMode == MapCoordinateMode.SdePlanarXY)
        {
            var customLayoutGraph = await _mapLayoutDataService.TryGetRegionLayoutGraphAsync(regionId, cancellationToken);
            if (customLayoutGraph is not null)
            {
                return await EnrichLayoutGraphFromSdeAsync(customLayoutGraph, cancellationToken);
            }
        }

        var systems = await QuerySystemsAsync(regionId, coordinateMode, cancellationToken);
        var links = await QuerySystemLinksAsync(regionId, cancellationToken);
        return BuildNormalizedGraph(systems, links);
    }

    private async Task<MapGraph> EnrichLayoutGraphFromSdeAsync(MapGraph layoutGraph, CancellationToken cancellationToken)
    {
        var systemIds = layoutGraph.Nodes
            .Where(n => n.Id > 0)
            .Select(n => (int)n.Id)
            .Distinct()
            .ToList();
        if (systemIds.Count == 0)
        {
            return layoutGraph;
        }

        var sdeBySystemId = await LoadSdeNodeMetadataBySystemIdAsync(systemIds, cancellationToken);
        var nodes = layoutGraph.Nodes.Select(n =>
        {
            if (n.Id <= 0 || !sdeBySystemId.TryGetValue((int)n.Id, out var m))
            {
                return n;
            }

            var staticData = StaticSolarSystemDataById.Value.TryGetValue((int)n.Id, out var loadedStaticData)
                ? loadedStaticData
                : null;

            return new MapNode
            {
                Id = n.Id,
                Name = n.Name,
                X = n.X,
                Y = n.Y,
                PositionX = m.PositionX,
                PositionY = m.PositionY,
                PositionZ = m.PositionZ,
                Security = m.Security,
                SunTypeId = m.SunTypeId,
                StarTypeName = m.StarTypeName,
                SpectralClass = m.SpectralClass,
                HasJoveObservatory = staticData?.HasJoveObservatory ?? false,
                IceFieldCount = staticData?.IceFieldCount ?? 0,
                RegionId = m.RegionId,
                RegionName = m.RegionName,
                ConstellationId = m.ConstellationId,
                ConstellationName = m.ConstellationName,
                StormEffects = _stormStateService.Current.EffectsBySystemId.TryGetValue((int)n.Id, out var effects)
                    ? effects
                    : [],
                HubWormholeConnections = _hubWormholeStateService.Current.ConnectionsBySystemId.TryGetValue((int)n.Id, out var wormholes)
                    ? wormholes
                    : [],
                SovUpgrades = _sovUpgradeStateService.CurrentBySystemId.TryGetValue((int)n.Id, out var upgrades)
                    ? upgrades
                    : [],
                HasActiveIncursion = _incursionStateService.Current.ActiveSystemIds.Contains((int)n.Id)
            };
        }).ToList();

        return new MapGraph { Nodes = nodes, Links = layoutGraph.Links };
    }

    private async Task<Dictionary<int, (double? PositionX, double? PositionY, double? PositionZ, double? Security, int? SunTypeId, string? StarTypeName, string? SpectralClass, int? RegionId, string? RegionName, int? ConstellationId, string? ConstellationName)>> LoadSdeNodeMetadataBySystemIdAsync(
        IReadOnlyList<int> systemIds,
        CancellationToken cancellationToken)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var result = new Dictionary<int, (double? PositionX, double? PositionY, double? PositionZ, double? Security, int? SunTypeId, string? StarTypeName, string? SpectralClass, int? RegionId, string? RegionName, int? ConstellationId, string? ConstellationName)>(systemIds.Count);
        const int chunkSize = 500;
        for (var i = 0; i < systemIds.Count; i += chunkSize)
        {
            var chunk = systemIds.Skip(i).Take(chunkSize).ToList();
            if (chunk.Count == 0)
            {
                continue;
            }

            var parameters = new List<string>(chunk.Count);
            var command = connection.CreateCommand();
            for (var p = 0; p < chunk.Count; p++)
            {
                var param = $"$id{p}";
                parameters.Add(param);
                command.Parameters.AddWithValue(param, chunk[p]);
            }

            command.CommandText = $"""
                SELECT s.solarSystemID, s.x, s.y, s.z, s.security, star.typeID, st.typeName, cs.spectralClass, s.regionID, r.regionName, s.constellationID, c.constellationName
                FROM mapSolarSystems s
                LEFT JOIN mapDenormalize star ON star.solarSystemID = s.solarSystemID AND star.groupID = 6
                LEFT JOIN mapCelestialStatistics cs ON cs.celestialID = star.itemID
                LEFT JOIN invTypes st ON st.typeID = star.typeID
                LEFT JOIN mapRegions r ON r.regionID = s.regionID
                LEFT JOIN mapConstellations c ON c.constellationID = s.constellationID
                WHERE s.solarSystemID IN ({string.Join(", ", parameters)});
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var systemId = reader.GetInt32(0);
                result[systemId] = (
                    reader.IsDBNull(1) ? null : reader.GetDouble(1),
                    reader.IsDBNull(2) ? null : reader.GetDouble(2),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11));
            }
        }

        return result;
    }

    private static bool TryGetLayoutRegionId(int regionId, out long layoutRegionId)
    {
        layoutRegionId = 0;
        if (regionId <= -LayoutRegionSyntheticBase)
        {
            return false;
        }

        if (regionId >= 0)
        {
            return false;
        }

        layoutRegionId = regionId + LayoutRegionSyntheticBase;
        return layoutRegionId > 0;
    }

    public async Task<IReadOnlyList<MapSearchCandidate>> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var like = $"%{term.Trim()}%";
        var results = new List<MapSearchCandidate>();

        async Task RunAsync(string sql, MapSearchKind kind)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$term", like);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new MapSearchCandidate
                {
                    Kind = kind,
                    Name = reader.GetString(1),
                    RegionId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    ConstellationId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    SolarSystemId = reader.IsDBNull(4) ? null : reader.GetInt32(4)
                });
            }
        }

        await RunAsync("""
            SELECT r.regionID AS id, r.regionName, r.regionID, NULL AS constellationID, NULL AS solarSystemID
            FROM mapRegions r
            WHERE r.regionName LIKE $term
              AND r.regionID IN (
                    SELECT fromRegionID FROM mapRegionJumps
                    UNION
                    SELECT toRegionID FROM mapRegionJumps
              )
            ORDER BY r.regionName
            LIMIT 20;
            """, MapSearchKind.Region);

        await RunAsync("""
            SELECT c.constellationID AS id, c.constellationName, c.regionID, c.constellationID, NULL AS solarSystemID
            FROM mapConstellations c
            WHERE c.constellationName LIKE $term
            ORDER BY c.constellationName
            LIMIT 30;
            """, MapSearchKind.Constellation);

        await RunAsync("""
            SELECT s.solarSystemID AS id, s.solarSystemName, s.regionID, s.constellationID, s.solarSystemID
            FROM mapSolarSystems s
            WHERE s.solarSystemName LIKE $term
            ORDER BY s.solarSystemName
            LIMIT 60;
            """, MapSearchKind.SolarSystem);

        return results;
    }

    public async Task<IReadOnlyDictionary<long, int>> GetSystemNeighborCountsAsync(IReadOnlyCollection<long> systemIds, CancellationToken cancellationToken = default)
    {
        var result = systemIds.ToDictionary(id => id, _ => 0);
        var filtered = systemIds.Where(id => id > 0).Distinct().ToList();
        if (filtered.Count == 0)
        {
            return result;
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const int chunkSize = 500;
        for (var offset = 0; offset < filtered.Count; offset += chunkSize)
        {
            var chunk = filtered.Skip(offset).Take(chunkSize).ToList();
            var command = connection.CreateCommand();
            var names = new List<string>(chunk.Count);
            for (var i = 0; i < chunk.Count; i++)
            {
                var name = $"$id{i}";
                names.Add(name);
                command.Parameters.AddWithValue(name, chunk[i]);
            }

            command.CommandText = $"""
                SELECT solarSystemId, COUNT(DISTINCT neighborSystemId) AS cnt
                FROM (
                    SELECT fromSolarSystemID AS solarSystemId, toSolarSystemID AS neighborSystemId
                    FROM mapSolarSystemJumps
                    WHERE fromSolarSystemID IN ({string.Join(", ", names)})
                    UNION ALL
                    SELECT toSolarSystemID AS solarSystemId, fromSolarSystemID AS neighborSystemId
                    FROM mapSolarSystemJumps
                    WHERE toSolarSystemID IN ({string.Join(", ", names)})
                )
                GROUP BY solarSystemId;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                var count = reader.GetInt32(1);
                result[id] = count;
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<long, MapSystemMetadata>> GetSystemMetadataByIdsAsync(IReadOnlyCollection<long> systemIds, CancellationToken cancellationToken = default)
    {
        var filtered = systemIds.Where(id => id > 0).Distinct().ToList();
        if (filtered.Count == 0)
        {
            return new Dictionary<long, MapSystemMetadata>();
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var result = new Dictionary<long, MapSystemMetadata>(filtered.Count);
        const int chunkSize = 500;
        for (var offset = 0; offset < filtered.Count; offset += chunkSize)
        {
            var chunk = filtered.Skip(offset).Take(chunkSize).ToList();
            var command = connection.CreateCommand();
            var names = new List<string>(chunk.Count);
            for (var i = 0; i < chunk.Count; i++)
            {
                var name = $"$id{i}";
                names.Add(name);
                command.Parameters.AddWithValue(name, chunk[i]);
            }

            command.CommandText = $"""
                SELECT s.solarSystemID, s.solarSystemName, s.constellationID, c.constellationName, s.regionID, r.regionName
                FROM mapSolarSystems s
                LEFT JOIN mapConstellations c ON c.constellationID = s.constellationID
                LEFT JOIN mapRegions r ON r.regionID = s.regionID
                WHERE s.solarSystemID IN ({string.Join(", ", names)});
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                result[id] = new MapSystemMetadata
                {
                    SolarSystemId = id,
                    SolarSystemName = reader.GetString(1),
                    ConstellationId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    ConstellationName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    RegionId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    RegionName = reader.IsDBNull(5) ? null : reader.GetString(5)
                };
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<MapSystemPosition>> GetSystemsWithSdeCoordinatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.solarSystemID, s.solarSystemName, c.constellationName, r.regionName, s.x, s.y, s.z
            FROM mapSolarSystems s
            LEFT JOIN mapConstellations c ON c.constellationID = s.constellationID
            LEFT JOIN mapRegions r ON r.regionID = s.regionID
            WHERE s.x IS NOT NULL AND s.y IS NOT NULL AND s.z IS NOT NULL;
            """;

        var result = new List<MapSystemPosition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MapSystemPosition
            {
                SolarSystemId = reader.GetInt64(0),
                SolarSystemName = reader.GetString(1),
                ConstellationName = reader.IsDBNull(2) ? null : reader.GetString(2),
                RegionName = reader.IsDBNull(3) ? null : reader.GetString(3),
                PositionX = reader.GetDouble(4),
                PositionY = reader.GetDouble(5),
                PositionZ = reader.GetDouble(6)
            });
        }

        return result;
    }

    private async Task<List<MapNode>> QuerySystemsAsync(int? regionId, MapCoordinateMode coordinateMode, CancellationToken cancellationToken)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var xColumn = coordinateMode == MapCoordinateMode.SdePlanarXY ? "s.x2D" : "s.x";
        var yColumn = coordinateMode == MapCoordinateMode.SdePlanarXY ? "s.y2D" : "s.z";
        var command = connection.CreateCommand();
        command.CommandText = regionId is null
            ? $"""
              SELECT s.solarSystemID, s.solarSystemName, {xColumn}, {yColumn}, s.security, star.typeID, st.typeName, cs.spectralClass, s.regionID, r.regionName
                     , s.constellationID
                     , c.constellationName
                     , s.x, s.y, s.z
              FROM mapSolarSystems s
              LEFT JOIN mapDenormalize star ON star.solarSystemID = s.solarSystemID AND star.groupID = 6
              LEFT JOIN mapCelestialStatistics cs ON cs.celestialID = star.itemID
              LEFT JOIN invTypes st ON st.typeID = star.typeID
              LEFT JOIN mapRegions r ON r.regionID = s.regionID
              LEFT JOIN mapConstellations c ON c.constellationID = s.constellationID;
              """
            : $"""
              SELECT s.solarSystemID, s.solarSystemName, {xColumn}, {yColumn}, s.security, star.typeID, st.typeName, cs.spectralClass, s.regionID, r.regionName
                     , s.constellationID
                     , c.constellationName
                     , s.x, s.y, s.z
              FROM mapSolarSystems s
              LEFT JOIN mapDenormalize star ON star.solarSystemID = s.solarSystemID AND star.groupID = 6
              LEFT JOIN mapCelestialStatistics cs ON cs.celestialID = star.itemID
              LEFT JOIN invTypes st ON st.typeID = star.typeID
              LEFT JOIN mapRegions r ON r.regionID = s.regionID
              LEFT JOIN mapConstellations c ON c.constellationID = s.constellationID
              WHERE s.regionID = $regionId;
              """;

        if (regionId is not null)
        {
            command.Parameters.AddWithValue("$regionId", regionId.Value);
        }

        var nodes = new List<MapNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var connectedSystemIds = regionId is null
            ? await LoadConnectedSystemIdsAsync(cancellationToken)
            : null;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(2) || reader.IsDBNull(3))
            {
                continue;
            }

            if (connectedSystemIds is not null && !connectedSystemIds.Contains(reader.GetInt32(0)))
            {
                continue;
            }

            var solarSystemId = reader.GetInt32(0);
            var staticData = StaticSolarSystemDataById.Value.TryGetValue(solarSystemId, out var loadedStaticData)
                ? loadedStaticData
                : null;

            nodes.Add(new MapNode
            {
                Id = solarSystemId,
                Name = reader.GetString(1),
                X = reader.GetDouble(2),
                Y = reader.GetDouble(3),
                PositionX = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                PositionY = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                PositionZ = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                Security = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                SunTypeId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                StarTypeName = reader.IsDBNull(6) ? null : reader.GetString(6),
                SpectralClass = reader.IsDBNull(7) ? null : reader.GetString(7),
                HasJoveObservatory = staticData?.HasJoveObservatory ?? false,
                IceFieldCount = staticData?.IceFieldCount ?? 0,
                RegionId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                RegionName = reader.IsDBNull(9) ? null : reader.GetString(9),
                ConstellationId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                ConstellationName = reader.IsDBNull(11) ? null : reader.GetString(11),
                StormEffects = _stormStateService.Current.EffectsBySystemId.TryGetValue(solarSystemId, out var effects)
                    ? effects
                    : [],
                HubWormholeConnections = _hubWormholeStateService.Current.ConnectionsBySystemId.TryGetValue(solarSystemId, out var wormholes)
                    ? wormholes
                    : [],
                SovUpgrades = _sovUpgradeStateService.CurrentBySystemId.TryGetValue(solarSystemId, out var upgrades)
                    ? upgrades
                    : [],
                HasActiveIncursion = _incursionStateService.Current.ActiveSystemIds.Contains(solarSystemId)
            });
        }

        return nodes;
    }

    private async Task<HashSet<int>> LoadConnectedSystemIdsAsync(CancellationToken cancellationToken)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fromSolarSystemID AS solarSystemID FROM mapSolarSystemJumps
            UNION
            SELECT toSolarSystemID AS solarSystemID FROM mapSolarSystemJumps;
            """;

        var ids = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    private async Task<List<MapLink>> QuerySystemLinksAsync(int? regionId, CancellationToken cancellationToken)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = regionId is null
            ? """
              SELECT fromSolarSystemID, toSolarSystemID
              FROM mapSolarSystemJumps;
              """
            : """
              SELECT j.fromSolarSystemID, j.toSolarSystemID
              FROM mapSolarSystemJumps j
              INNER JOIN mapSolarSystems s1 ON s1.solarSystemID = j.fromSolarSystemID
              INNER JOIN mapSolarSystems s2 ON s2.solarSystemID = j.toSolarSystemID
              WHERE s1.regionID = $regionId AND s2.regionID = $regionId;
              """;

        if (regionId is not null)
        {
            command.Parameters.AddWithValue("$regionId", regionId.Value);
        }

        var links = new List<MapLink>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            links.Add(new MapLink
            {
                FromId = reader.GetInt32(0),
                ToId = reader.GetInt32(1)
            });
        }

        return links;
    }

    private static MapGraph BuildNormalizedGraph(IReadOnlyList<MapNode> rawNodes, IReadOnlyList<MapLink> rawLinks)
    {
        if (rawNodes.Count == 0)
        {
            return new MapGraph { Nodes = [], Links = [] };
        }

        var minX = rawNodes.Min(n => n.X);
        var maxX = rawNodes.Max(n => n.X);
        var minY = rawNodes.Min(n => n.Y);
        var maxY = rawNodes.Max(n => n.Y);

        var width = Math.Max(1e-9, maxX - minX);
        var height = Math.Max(1e-9, maxY - minY);

        var nodes = rawNodes
            .Select(n => new MapNode
            {
                Id = n.Id,
                Name = n.Name,
                X = (n.X - minX) / width,
                Y = 1.0 - ((n.Y - minY) / height),
                PositionX = n.PositionX,
                PositionY = n.PositionY,
                PositionZ = n.PositionZ,
                Security = n.Security,
                SunTypeId = n.SunTypeId,
                StarTypeName = n.StarTypeName,
                SpectralClass = n.SpectralClass,
                HasJoveObservatory = n.HasJoveObservatory,
                IceFieldCount = n.IceFieldCount,
                RegionId = n.RegionId,
                RegionName = n.RegionName,
                ConstellationId = n.ConstellationId,
                ConstellationName = n.ConstellationName,
                StormEffects = n.StormEffects,
                HubWormholeConnections = n.HubWormholeConnections
                ,
                SovUpgrades = n.SovUpgrades,
                HasActiveIncursion = n.HasActiveIncursion
            })
            .ToList();

        var idSet = nodes.Select(n => n.Id).ToHashSet();
        var links = rawLinks
            .Where(l => idSet.Contains(l.FromId) && idSet.Contains(l.ToId))
            .ToList();

        return new MapGraph
        {
            Nodes = nodes,
            Links = links
        };
    }

    private static IReadOnlyDictionary<int, StaticSolarSystemData> LoadStaticSolarSystemDataById()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "solarsystemsstaticdata.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Hisa.App", "Data", "solarsystemsstaticdata.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "solarsystemsstaticdata.db")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            return new Dictionary<int, StaticSolarSystemData>();
        }

        var result = new Dictionary<int, StaticSolarSystemData>();
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        };

        using var connection = new SqliteConnection(connectionStringBuilder.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT solarSystemID, hasJoveObservatory, iceFieldCount
            FROM SolarSystems;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var hasJoveObservatory = !reader.IsDBNull(1) && reader.GetBoolean(1);
            var iceFieldCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            result[id] = new StaticSolarSystemData(hasJoveObservatory, iceFieldCount);
        }

        return result;
    }
}

public static class MapServiceCollectionExtensions
{
    public static IServiceCollection AddHisaMapServices(this IServiceCollection services)
    {
        services.AddSingleton<IMapDataService, MapDataService>();
        return services;
    }
}
