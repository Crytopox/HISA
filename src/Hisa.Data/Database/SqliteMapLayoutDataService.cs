using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Microsoft.Data.Sqlite;

namespace Hisa.Data.Database;

public sealed class SqliteMapLayoutDataService : IMapLayoutDataService
{
    private readonly string _connectionString;

    public SqliteMapLayoutDataService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<MapGraph?> TryGetRegionLayoutGraphAsync(int regionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var regionCommand = connection.CreateCommand();
        regionCommand.CommandText = """
            SELECT lr.Id
            FROM MapLayoutRegion lr
            INNER JOIN MapLayoutPack lp ON lp.Id = lr.PackId
            WHERE lr.SourceRegionId = $regionId
              AND lr.IsGameRegion = 1
            ORDER BY lp.IsBase DESC, lp.CreatedUtc DESC, lr.Id DESC
            LIMIT 1;
            """;
        regionCommand.Parameters.AddWithValue("$regionId", regionId);
        var regionLayoutIdRaw = await regionCommand.ExecuteScalarAsync(cancellationToken);
        if (regionLayoutIdRaw is null or DBNull)
        {
            return null;
        }

        var regionLayoutId = Convert.ToInt64(regionLayoutIdRaw);

        var nodes = new List<MapNode>();
        var nodeById = new Dictionary<long, MapNode>();
        var nodeCommand = connection.CreateCommand();
        nodeCommand.CommandText = """
            SELECT n.Id, n.SolarSystemId, n.Name, n.X, n.Y
            FROM MapLayoutNode n
            WHERE n.RegionLayoutId = $layoutId;
            """;
        nodeCommand.Parameters.AddWithValue("$layoutId", regionLayoutId);
        await using (var reader = await nodeCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var nodeRowId = reader.GetInt64(0);
                var solarSystemId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                var node = new MapNode
                {
                    Id = solarSystemId is > 0 ? solarSystemId.Value : -nodeRowId,
                    Name = reader.GetString(2),
                    X = reader.GetDouble(3),
                    Y = reader.GetDouble(4),
                    Security = null,
                    SunTypeId = null,
                    StarTypeName = null,
                    SpectralClass = null,
                    HasJoveObservatory = false,
                    IceFieldCount = 0,
                    RegionId = regionId,
                    RegionName = null,
                    ConstellationId = null,
                    ConstellationName = null,
                    StormEffects = [],
                    HubWormholeConnections = []
                };

                nodeById[nodeRowId] = node;
                nodes.Add(node);
            }
        }

        var links = new List<MapLink>();
        var linkCommand = connection.CreateCommand();
        linkCommand.CommandText = """
            SELECT l.FromNodeId, l.ToNodeId
            FROM MapLayoutLink l
            WHERE l.RegionLayoutId = $layoutId;
            """;
        linkCommand.Parameters.AddWithValue("$layoutId", regionLayoutId);
        await using (var reader = await linkCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var fromNodeId = reader.GetInt64(0);
                var toNodeId = reader.GetInt64(1);
                if (!nodeById.TryGetValue(fromNodeId, out var fromNode) || !nodeById.TryGetValue(toNodeId, out var toNode))
                {
                    continue;
                }

                links.Add(new MapLink
                {
                    FromId = fromNode.Id,
                    ToId = toNode.Id
                });
            }
        }

        if (nodes.Count == 0)
        {
            return null;
        }

        return NormalizeGraph(nodes, links);
    }

    public async Task<IReadOnlyList<MapLayoutRegionSummary>> GetLayoutRegionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lr.Id, lr.Name, lr.SourceRegionId, lr.IsGameRegion, lp.IsReadOnly
            FROM MapLayoutRegion lr
            INNER JOIN MapLayoutPack lp ON lp.Id = lr.PackId
            ORDER BY lr.Name;
            """;

        var result = new List<MapLayoutRegionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MapLayoutRegionSummary
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                SourceRegionId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                IsGameRegion = !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
                IsReadOnly = !reader.IsDBNull(4) && reader.GetInt32(4) == 1
            });
        }

        return result;
    }

    private static MapGraph NormalizeGraph(IReadOnlyList<MapNode> rawNodes, IReadOnlyList<MapLink> rawLinks)
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
            })
            .ToList();

        var idSet = nodes.Select(n => n.Id).ToHashSet();
        var links = rawLinks.Where(l => idSet.Contains(l.FromId) && idSet.Contains(l.ToId)).ToList();
        return new MapGraph { Nodes = nodes, Links = links };
    }
}
