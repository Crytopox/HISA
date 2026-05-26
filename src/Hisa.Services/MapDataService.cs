using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Data.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Hisa.Services;

public sealed class MapDataService : IMapDataService
{
    private readonly ISdeDatabase _sdeDatabase;

    public MapDataService(ISdeDatabase sdeDatabase)
    {
        _sdeDatabase = sdeDatabase;
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
                RegionName = reader.GetString(1)
            });
        }

        return result;
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
        var systems = await QuerySystemsAsync(regionId, coordinateMode, cancellationToken);
        var links = await QuerySystemLinksAsync(regionId, cancellationToken);
        return BuildNormalizedGraph(systems, links);
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
              SELECT s.solarSystemID, s.solarSystemName, {xColumn}, {yColumn}, s.regionID, r.regionName
              FROM mapSolarSystems s
              LEFT JOIN mapRegions r ON r.regionID = s.regionID;
              """
            : $"""
              SELECT s.solarSystemID, s.solarSystemName, {xColumn}, {yColumn}, s.regionID, r.regionName
              FROM mapSolarSystems s
              LEFT JOIN mapRegions r ON r.regionID = s.regionID
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

            nodes.Add(new MapNode
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                X = reader.GetDouble(2),
                Y = reader.GetDouble(3),
                RegionId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                RegionName = reader.IsDBNull(5) ? null : reader.GetString(5)
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
                RegionId = n.RegionId,
                RegionName = n.RegionName
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
}

public static class MapServiceCollectionExtensions
{
    public static IServiceCollection AddHisaMapServices(this IServiceCollection services)
    {
        services.AddSingleton<IMapDataService, MapDataService>();
        return services;
    }
}
