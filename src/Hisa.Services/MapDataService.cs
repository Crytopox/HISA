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
                    Y = reader.GetDouble(3)
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

        var xColumn = coordinateMode == MapCoordinateMode.SdePlanarXY ? "x2D" : "x";
        var yColumn = coordinateMode == MapCoordinateMode.SdePlanarXY ? "y2D" : "z";
        var command = connection.CreateCommand();
        command.CommandText = regionId is null
            ? $"""
              SELECT solarSystemID, solarSystemName, {xColumn}, {yColumn}
              FROM mapSolarSystems;
              """
            : $"""
              SELECT solarSystemID, solarSystemName, {xColumn}, {yColumn}
              FROM mapSolarSystems
              WHERE regionID = $regionId;
              """;

        if (regionId is not null)
        {
            command.Parameters.AddWithValue("$regionId", regionId.Value);
        }

        var nodes = new List<MapNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(2) || reader.IsDBNull(3))
            {
                continue;
            }

            nodes.Add(new MapNode
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                X = reader.GetDouble(2),
                Y = reader.GetDouble(3)
            });
        }

        return nodes;
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
                Y = 1.0 - ((n.Y - minY) / height)
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
