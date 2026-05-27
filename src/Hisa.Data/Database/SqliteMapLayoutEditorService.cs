using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Microsoft.Data.Sqlite;

namespace Hisa.Data.Database;

public sealed class SqliteMapLayoutEditorService : IMapLayoutEditorService
{
    private readonly string _connectionString;
    private readonly ISdeDatabase _sdeDatabase;

    public SqliteMapLayoutEditorService(string connectionString, ISdeDatabase sdeDatabase)
    {
        _connectionString = connectionString;
        _sdeDatabase = sdeDatabase;
    }

    public async Task<MapLayoutRegionSummary> CreateCustomRegionAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Region name is required.");
        }

        var trimmedName = name.Trim();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = connection.BeginTransaction();

        var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = tx;
        existsCommand.CommandText = "SELECT COUNT(1) FROM MapLayoutRegion WHERE Name = $name COLLATE NOCASE;";
        existsCommand.Parameters.AddWithValue("$name", trimmedName);
        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            throw new InvalidOperationException($"A layout region named '{trimmedName}' already exists.");
        }

        var packId = await EnsureUserPackAsync(connection, tx, cancellationToken);

        var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = tx;
        insertCommand.CommandText = """
            INSERT INTO MapLayoutRegion(PackId, Name, SourceRegionId, IsGameRegion)
            VALUES ($packId, $name, NULL, 0);
            SELECT last_insert_rowid();
            """;
        insertCommand.Parameters.AddWithValue("$packId", packId);
        insertCommand.Parameters.AddWithValue("$name", trimmedName);
        var layoutRegionId = Convert.ToInt64(await insertCommand.ExecuteScalarAsync(cancellationToken));

        await tx.CommitAsync(cancellationToken);
        return new MapLayoutRegionSummary
        {
            Id = layoutRegionId,
            Name = trimmedName,
            SourceRegionId = null,
            IsGameRegion = false,
            IsReadOnly = false
        };
    }

    public async Task DeleteLayoutRegionAsync(long layoutRegionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = connection.BeginTransaction();

        await EnsureEditableRegionAsync(connection, tx, layoutRegionId, cancellationToken);

        var deleteRegion = connection.CreateCommand();
        deleteRegion.Transaction = tx;
        deleteRegion.CommandText = "DELETE FROM MapLayoutRegion WHERE Id = $layoutRegionId;";
        deleteRegion.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await deleteRegion.ExecuteNonQueryAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<MapGraph?> GetLayoutRegionGraphAsync(long layoutRegionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await LoadLayoutGraphAsync(connection, layoutRegionId, cancellationToken);
    }

    public async Task AddGameRegionsToLayoutAsync(long layoutRegionId, IReadOnlyList<int> sourceRegionIds, CancellationToken cancellationToken = default)
    {
        if (sourceRegionIds.Count == 0)
        {
            return;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = connection.BeginTransaction();

        await EnsureEditableRegionAsync(connection, tx, layoutRegionId, cancellationToken);

        var existingNodes = new Dictionary<long, (string Name, double X, double Y)>();
        var existingNodesCommand = connection.CreateCommand();
        existingNodesCommand.Transaction = tx;
        existingNodesCommand.CommandText = """
            SELECT SolarSystemId, Name, X, Y
            FROM MapLayoutNode
            WHERE RegionLayoutId = $layoutRegionId AND SolarSystemId IS NOT NULL;
            """;
        existingNodesCommand.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await using (var reader = await existingNodesCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var systemId = reader.GetInt64(0);
                existingNodes[systemId] = (reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3));
            }
        }

        var systemsToAdd = await LoadSdeSystemsForRegionsAsync(sourceRegionIds, cancellationToken);
        foreach (var system in systemsToAdd)
        {
            if (existingNodes.ContainsKey(system.Id))
            {
                continue;
            }

            var insertNode = connection.CreateCommand();
            insertNode.Transaction = tx;
            insertNode.CommandText = """
                INSERT INTO MapLayoutNode(RegionLayoutId, SolarSystemId, Name, X, Y)
                VALUES ($layoutRegionId, $solarSystemId, $name, $x, $y);
                """;
            insertNode.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
            insertNode.Parameters.AddWithValue("$solarSystemId", system.Id);
            insertNode.Parameters.AddWithValue("$name", system.Name);
            insertNode.Parameters.AddWithValue("$x", system.X);
            insertNode.Parameters.AddWithValue("$y", system.Y);
            await insertNode.ExecuteNonQueryAsync(cancellationToken);
        }

        await RebuildLinksForLayoutRegionAsync(connection, tx, layoutRegionId, cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task SaveLayoutRegionGraphAsync(long layoutRegionId, MapGraph graph, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = connection.BeginTransaction();

        await EnsureEditableRegionAsync(connection, tx, layoutRegionId, cancellationToken);

        var clearLinksCommand = connection.CreateCommand();
        clearLinksCommand.Transaction = tx;
        clearLinksCommand.CommandText = "DELETE FROM MapLayoutLink WHERE RegionLayoutId = $layoutRegionId;";
        clearLinksCommand.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await clearLinksCommand.ExecuteNonQueryAsync(cancellationToken);

        var clearNodesCommand = connection.CreateCommand();
        clearNodesCommand.Transaction = tx;
        clearNodesCommand.CommandText = "DELETE FROM MapLayoutNode WHERE RegionLayoutId = $layoutRegionId;";
        clearNodesCommand.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await clearNodesCommand.ExecuteNonQueryAsync(cancellationToken);

        var rowIdByMapId = new Dictionary<long, long>();
        foreach (var node in graph.Nodes)
        {
            var insertNode = connection.CreateCommand();
            insertNode.Transaction = tx;
            insertNode.CommandText = """
                INSERT INTO MapLayoutNode(RegionLayoutId, SolarSystemId, Name, X, Y)
                VALUES ($layoutRegionId, $solarSystemId, $name, $x, $y);
                SELECT last_insert_rowid();
                """;
            insertNode.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
            insertNode.Parameters.AddWithValue("$solarSystemId", node.Id > 0 ? node.Id : null);
            insertNode.Parameters.AddWithValue("$name", node.Name);
            insertNode.Parameters.AddWithValue("$x", node.X);
            insertNode.Parameters.AddWithValue("$y", node.Y);
            var rowId = Convert.ToInt64(await insertNode.ExecuteScalarAsync(cancellationToken));
            rowIdByMapId[node.Id] = rowId;
        }

        var autoLinks = await BuildAutoLinksForSystemsAsync(graph.Nodes.Where(n => n.Id > 0).Select(n => n.Id).ToHashSet(), cancellationToken);
        foreach (var link in autoLinks)
        {
            if (!rowIdByMapId.TryGetValue(link.FromId, out var fromNodeId) ||
                !rowIdByMapId.TryGetValue(link.ToId, out var toNodeId))
            {
                continue;
            }

            var insertLink = connection.CreateCommand();
            insertLink.Transaction = tx;
            insertLink.CommandText = """
                INSERT INTO MapLayoutLink(RegionLayoutId, FromNodeId, ToNodeId)
                VALUES ($layoutRegionId, $fromNodeId, $toNodeId);
                """;
            insertLink.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
            insertLink.Parameters.AddWithValue("$fromNodeId", fromNodeId);
            insertLink.Parameters.AddWithValue("$toNodeId", toNodeId);
            await insertLink.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MapLink>> BuildAutoLinksForSystemsAsync(IReadOnlyCollection<long> solarSystemIds, CancellationToken cancellationToken = default)
    {
        if (solarSystemIds.Count < 2)
        {
            return [];
        }

        var systemIdSet = solarSystemIds.Select(x => (int)x).ToHashSet();
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fromSolarSystemID, toSolarSystemID
            FROM mapSolarSystemJumps;
            """;

        var links = new List<MapLink>();
        var linkKeys = new HashSet<(long A, long B)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var from = reader.GetInt32(0);
            var to = reader.GetInt32(1);
            if (!systemIdSet.Contains(from) || !systemIdSet.Contains(to) || from == to)
            {
                continue;
            }

            var key = from < to ? ((long)from, (long)to) : ((long)to, (long)from);
            if (!linkKeys.Add(key))
            {
                continue;
            }

            links.Add(new MapLink { FromId = from, ToId = to });
        }

        return links;
    }

    private async Task<MapGraph?> LoadLayoutGraphAsync(SqliteConnection connection, long layoutRegionId, CancellationToken cancellationToken)
    {
        var nodes = new List<MapNode>();
        var nodeByRowId = new Dictionary<long, MapNode>();
        var nodeCommand = connection.CreateCommand();
        nodeCommand.CommandText = """
            SELECT Id, SolarSystemId, Name, X, Y
            FROM MapLayoutNode
            WHERE RegionLayoutId = $layoutRegionId;
            """;
        nodeCommand.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await using (var reader = await nodeCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var rowId = reader.GetInt64(0);
                var solarSystemId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                var mapId = solarSystemId is > 0 ? solarSystemId.Value : -rowId;
                var node = new MapNode
                {
                    Id = mapId,
                    Name = reader.GetString(2),
                    X = reader.GetDouble(3),
                    Y = reader.GetDouble(4),
                    Security = null,
                    SunTypeId = null,
                    StarTypeName = null,
                    SpectralClass = null,
                    HasJoveObservatory = false,
                    IceFieldCount = 0,
                    RegionId = null,
                    RegionName = null,
                    ConstellationId = null,
                    ConstellationName = null,
                    StormEffects = [],
                    HubWormholeConnections = []
                };
                nodeByRowId[rowId] = node;
                nodes.Add(node);
            }
        }

        if (nodes.Count == 0)
        {
            return new MapGraph { Nodes = [], Links = [] };
        }

        var links = new List<MapLink>();
        var linkCommand = connection.CreateCommand();
        linkCommand.CommandText = """
            SELECT FromNodeId, ToNodeId
            FROM MapLayoutLink
            WHERE RegionLayoutId = $layoutRegionId;
            """;
        linkCommand.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await using (var reader = await linkCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var fromRow = reader.GetInt64(0);
                var toRow = reader.GetInt64(1);
                if (!nodeByRowId.TryGetValue(fromRow, out var fromNode) || !nodeByRowId.TryGetValue(toRow, out var toNode))
                {
                    continue;
                }

                links.Add(new MapLink { FromId = fromNode.Id, ToId = toNode.Id });
            }
        }

        return NormalizeGraph(nodes, links);
    }

    private async Task RebuildLinksForLayoutRegionAsync(SqliteConnection connection, SqliteTransaction tx, long layoutRegionId, CancellationToken cancellationToken)
    {
        var nodeIdsBySystemId = new Dictionary<int, long>();
        var nodeCommand = connection.CreateCommand();
        nodeCommand.Transaction = tx;
        nodeCommand.CommandText = """
            SELECT Id, SolarSystemId
            FROM MapLayoutNode
            WHERE RegionLayoutId = $layoutRegionId
              AND SolarSystemId IS NOT NULL;
            """;
        nodeCommand.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await using (var reader = await nodeCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                nodeIdsBySystemId[reader.GetInt32(1)] = reader.GetInt64(0);
            }
        }

        var clearLinksCommand = connection.CreateCommand();
        clearLinksCommand.Transaction = tx;
        clearLinksCommand.CommandText = "DELETE FROM MapLayoutLink WHERE RegionLayoutId = $layoutRegionId;";
        clearLinksCommand.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        await clearLinksCommand.ExecuteNonQueryAsync(cancellationToken);

        if (nodeIdsBySystemId.Count < 2)
        {
            return;
        }

        var autoLinks = await BuildAutoLinksForSystemsAsync(nodeIdsBySystemId.Keys.Select(x => (long)x).ToHashSet(), cancellationToken);
        foreach (var autoLink in autoLinks)
        {
            if (!nodeIdsBySystemId.TryGetValue((int)autoLink.FromId, out var fromNodeRowId) ||
                !nodeIdsBySystemId.TryGetValue((int)autoLink.ToId, out var toNodeRowId))
            {
                continue;
            }

            var insertLink = connection.CreateCommand();
            insertLink.Transaction = tx;
            insertLink.CommandText = """
                INSERT INTO MapLayoutLink(RegionLayoutId, FromNodeId, ToNodeId)
                VALUES ($layoutRegionId, $fromNodeId, $toNodeId);
                """;
            insertLink.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
            insertLink.Parameters.AddWithValue("$fromNodeId", fromNodeRowId);
            insertLink.Parameters.AddWithValue("$toNodeId", toNodeRowId);
            await insertLink.ExecuteNonQueryAsync(cancellationToken);
        }
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

    private async Task<long> EnsureUserPackAsync(SqliteConnection connection, SqliteTransaction tx, CancellationToken cancellationToken)
    {
        var getPackCommand = connection.CreateCommand();
        getPackCommand.Transaction = tx;
        getPackCommand.CommandText = """
            SELECT Id
            FROM MapLayoutPack
            WHERE IsBase = 0
            ORDER BY Id
            LIMIT 1;
            """;
        var existing = await getPackCommand.ExecuteScalarAsync(cancellationToken);
        if (existing is not null and not DBNull)
        {
            return Convert.ToInt64(existing);
        }

        var createPackCommand = connection.CreateCommand();
        createPackCommand.Transaction = tx;
        createPackCommand.CommandText = """
            INSERT INTO MapLayoutPack(Name, IsBase, IsReadOnly)
            VALUES ('User Custom', 0, 0);
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt64(await createPackCommand.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task EnsureEditableRegionAsync(SqliteConnection connection, SqliteTransaction tx, long layoutRegionId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT lp.IsReadOnly
            FROM MapLayoutRegion lr
            INNER JOIN MapLayoutPack lp ON lp.Id = lr.PackId
            WHERE lr.Id = $layoutRegionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$layoutRegionId", layoutRegionId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            throw new InvalidOperationException("Layout region not found.");
        }

        var isReadOnly = Convert.ToInt32(result) == 1;
        if (isReadOnly)
        {
            throw new InvalidOperationException("This layout region is read-only.");
        }
    }

    private async Task<List<(long Id, string Name, double X, double Y)>> LoadSdeSystemsForRegionsAsync(IReadOnlyList<int> sourceRegionIds, CancellationToken cancellationToken)
    {
        var distinctIds = sourceRegionIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return [];
        }

        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var parameters = new List<string>(distinctIds.Count);
        var command = connection.CreateCommand();
        for (var i = 0; i < distinctIds.Count; i++)
        {
            var name = $"$region{i}";
            parameters.Add(name);
            command.Parameters.AddWithValue(name, distinctIds[i]);
        }

        command.CommandText = $"""
            SELECT solarSystemID, solarSystemName, x2D, y2D
            FROM mapSolarSystems
            WHERE regionID IN ({string.Join(", ", parameters)})
              AND x2D IS NOT NULL
              AND y2D IS NOT NULL;
            """;

        var result = new List<(long Id, string Name, double X, double Y)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((reader.GetInt32(0), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3)));
        }

        return result;
    }
}
