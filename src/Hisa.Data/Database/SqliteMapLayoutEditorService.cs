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

        foreach (var sourceRegionId in sourceRegionIds.Distinct())
        {
            var systemsToAdd = await LoadSdeSystemsForRegionAsync(sourceRegionId, cancellationToken);
            var chunkToInsert = BuildImportedChunkCoordinates(existingNodes, systemsToAdd);
            foreach (var system in chunkToInsert)
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

                existingNodes[system.Id] = (system.Name, system.X, system.Y);
            }
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

    public async Task<IReadOnlyList<MapNode>> GetMissingConnectedSystemsAsync(
        IReadOnlyCollection<long> selectedSystemIds,
        IReadOnlyCollection<long> existingSystemIds,
        IReadOnlyDictionary<long, (double X, double Y)> existingNodeLayoutBySystemId,
        CancellationToken cancellationToken = default)
    {
        var selectedList = selectedSystemIds.Where(x => x > 0).Select(x => (int)x).Distinct().ToList();
        if (selectedList.Count == 0)
        {
            return [];
        }

        var selectedSet = selectedList.ToHashSet();
        var existing = existingSystemIds.Where(x => x > 0).Select(x => (int)x).ToHashSet();
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var selectedParams = new List<string>(selectedList.Count);
        var jumpsCommand = connection.CreateCommand();
        for (var i = 0; i < selectedList.Count; i++)
        {
            var name = $"$sel{i}";
            selectedParams.Add(name);
            jumpsCommand.Parameters.AddWithValue(name, selectedList[i]);
        }

        jumpsCommand.CommandText = $"""
            SELECT fromSolarSystemID, toSolarSystemID
            FROM mapSolarSystemJumps
            WHERE fromSolarSystemID IN ({string.Join(", ", selectedParams)})
               OR toSolarSystemID IN ({string.Join(", ", selectedParams)});
            """;

        var missingNeighborIds = new HashSet<int>();
        await using (var reader = await jumpsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var from = reader.GetInt32(0);
                var to = reader.GetInt32(1);
                if (selectedSet.Contains(from) && !existing.Contains(to))
                {
                    missingNeighborIds.Add(to);
                }

                if (selectedSet.Contains(to) && !existing.Contains(from))
                {
                    missingNeighborIds.Add(from);
                }
            }
        }

        if (missingNeighborIds.Count == 0)
        {
            return [];
        }

        var missingList = missingNeighborIds.ToList();
        var rawSystemIds = existingSystemIds
            .Where(x => x > 0)
            .Select(x => (int)x)
            .Concat(missingList)
            .Distinct()
            .ToList();
        var nodeParams = new List<string>(rawSystemIds.Count);
        var nodesCommand = connection.CreateCommand();
        for (var i = 0; i < rawSystemIds.Count; i++)
        {
            var name = $"$node{i}";
            nodeParams.Add(name);
            nodesCommand.Parameters.AddWithValue(name, rawSystemIds[i]);
        }

        nodesCommand.CommandText = $"""
            SELECT solarSystemID, solarSystemName, x2D, y2D
            FROM mapSolarSystems
            WHERE solarSystemID IN ({string.Join(", ", nodeParams)})
              AND x2D IS NOT NULL
              AND y2D IS NOT NULL;
            """;

        var rawBySystemId = new Dictionary<int, (string Name, double X, double Y)>(rawSystemIds.Count);
        await using var nodeReader = await nodesCommand.ExecuteReaderAsync(cancellationToken);
        while (await nodeReader.ReadAsync(cancellationToken))
        {
            var id = nodeReader.GetInt32(0);
            rawBySystemId[id] = (nodeReader.GetString(1), nodeReader.GetDouble(2), nodeReader.GetDouble(3));
        }

        var anchors = rawBySystemId
            .Where(kvp => existingNodeLayoutBySystemId.ContainsKey(kvp.Key))
            .Select(kvp => new
            {
                Id = (long)kvp.Key,
                RawX = kvp.Value.X,
                RawY = kvp.Value.Y,
                LayoutX = existingNodeLayoutBySystemId[kvp.Key].X,
                LayoutY = existingNodeLayoutBySystemId[kvp.Key].Y
            })
            .ToList();

        var canAffineMap = anchors.Count >= 2;
        var rawMinX = canAffineMap ? anchors.Min(a => a.RawX) : 0;
        var rawMaxX = canAffineMap ? anchors.Max(a => a.RawX) : 1;
        var rawMinY = canAffineMap ? anchors.Min(a => a.RawY) : 0;
        var rawMaxY = canAffineMap ? anchors.Max(a => a.RawY) : 1;
        var layoutMinX = canAffineMap ? anchors.Min(a => a.LayoutX) : 0;
        var layoutMaxX = canAffineMap ? anchors.Max(a => a.LayoutX) : 1;
        var layoutMinY = canAffineMap ? anchors.Min(a => a.LayoutY) : 0;
        var layoutMaxY = canAffineMap ? anchors.Max(a => a.LayoutY) : 1;
        var rawWidth = Math.Max(1e-9, rawMaxX - rawMinX);
        var rawHeight = Math.Max(1e-9, rawMaxY - rawMinY);
        var layoutWidth = Math.Max(1e-9, layoutMaxX - layoutMinX);
        var layoutHeight = Math.Max(1e-9, layoutMaxY - layoutMinY);
        var scaleX = layoutWidth / rawWidth;
        var scaleY = layoutHeight / rawHeight;

        var selectedAnchorPoints = selectedList
            .Where(rawBySystemId.ContainsKey)
            .Where(id => existingNodeLayoutBySystemId.ContainsKey(id))
            .Select(id => existingNodeLayoutBySystemId[id])
            .ToList();
        var selectedCenterX = selectedAnchorPoints.Count > 0 ? selectedAnchorPoints.Average(p => p.X) : 0.5;
        var selectedCenterY = selectedAnchorPoints.Count > 0 ? selectedAnchorPoints.Average(p => p.Y) : 0.5;
        const double fallbackRadius = 0.08;
        var fallbackIndex = 0;

        var result = new List<MapNode>(missingList.Count);
        foreach (var missingId in missingList)
        {
            if (!rawBySystemId.TryGetValue(missingId, out var rawNode))
            {
                continue;
            }

            double mappedX;
            double mappedY;
            if (canAffineMap)
            {
                mappedX = layoutMinX + ((rawNode.X - rawMinX) * scaleX);
                mappedY = layoutMaxY - ((rawNode.Y - rawMinY) * scaleY);
            }
            else
            {
                var angle = (Math.PI * 2.0 * fallbackIndex) / Math.Max(1, missingList.Count);
                mappedX = selectedCenterX + (Math.Cos(angle) * fallbackRadius);
                mappedY = selectedCenterY + (Math.Sin(angle) * fallbackRadius);
                fallbackIndex++;
            }

            result.Add(new MapNode
            {
                Id = missingId,
                Name = rawNode.Name,
                X = mappedX,
                Y = mappedY,
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
            });
        }

        return result;
    }

    private async Task<MapGraph?> LoadLayoutGraphAsync(SqliteConnection connection, long layoutRegionId, CancellationToken cancellationToken)
    {
        var nodes = new List<MapNode>();
        var nodeByRowId = new Dictionary<long, MapNode>();
        var loadedSystemIds = new HashSet<int>();
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
                if (solarSystemId is > 0)
                {
                    loadedSystemIds.Add((int)solarSystemId.Value);
                }

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

        if (loadedSystemIds.Count > 0)
        {
            var metadataBySystemId = await LoadSdeNodeMetadataByIdAsync(loadedSystemIds, cancellationToken);
            nodes = nodes
                .Select(node =>
                {
                    if (node.Id <= 0 || !metadataBySystemId.TryGetValue((int)node.Id, out var meta))
                    {
                        return node;
                    }

                    return new MapNode
                    {
                        Id = node.Id,
                        Name = node.Name,
                        X = node.X,
                        Y = node.Y,
                        Security = meta.Security,
                        SunTypeId = node.SunTypeId,
                        StarTypeName = node.StarTypeName,
                        SpectralClass = node.SpectralClass,
                        HasJoveObservatory = node.HasJoveObservatory,
                        IceFieldCount = node.IceFieldCount,
                        RegionId = meta.RegionId,
                        RegionName = meta.RegionName,
                        ConstellationId = meta.ConstellationId,
                        ConstellationName = meta.ConstellationName,
                        StormEffects = node.StormEffects,
                        HubWormholeConnections = node.HubWormholeConnections
                    };
                })
                .ToList();
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

        return new MapGraph { Nodes = nodes, Links = links };
    }

    private async Task<Dictionary<int, (int? RegionId, string? RegionName, int? ConstellationId, string? ConstellationName, double? Security)>> LoadSdeNodeMetadataByIdAsync(
        IReadOnlyCollection<int> systemIds,
        CancellationToken cancellationToken)
    {
        if (systemIds.Count == 0)
        {
            return [];
        }

        await using var sdeConnection = _sdeDatabase.CreateConnection();
        await sdeConnection.OpenAsync(cancellationToken);

        var idList = systemIds.Distinct().ToList();
        var parameters = new List<string>(idList.Count);
        var command = sdeConnection.CreateCommand();
        for (var i = 0; i < idList.Count; i++)
        {
            var param = $"$id{i}";
            parameters.Add(param);
            command.Parameters.AddWithValue(param, idList[i]);
        }

        command.CommandText = $"""
            SELECT s.solarSystemID, s.regionID, r.regionName, s.constellationID, c.constellationName, s.security
            FROM mapSolarSystems s
            LEFT JOIN mapRegions r ON r.regionID = s.regionID
            LEFT JOIN mapConstellations c ON c.constellationID = s.constellationID
            WHERE s.solarSystemID IN ({string.Join(", ", parameters)});
            """;

        var result = new Dictionary<int, (int? RegionId, string? RegionName, int? ConstellationId, string? ConstellationName, double? Security)>(idList.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var systemId = reader.GetInt32(0);
            var regionId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
            var regionName = reader.IsDBNull(2) ? null : reader.GetString(2);
            var constellationId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var constellationName = reader.IsDBNull(4) ? null : reader.GetString(4);
            var security = reader.IsDBNull(5) ? (double?)null : reader.GetDouble(5);
            result[systemId] = (regionId, regionName, constellationId, constellationName, security);
        }

        return result;
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

    private static List<(long Id, string Name, double X, double Y)> BuildImportedChunkCoordinates(
        IReadOnlyDictionary<long, (string Name, double X, double Y)> existingNodes,
        IReadOnlyList<(long Id, string Name, double X, double Y)> systemsToAdd)
    {
        if (systemsToAdd.Count == 0)
        {
            return [];
        }

        var minX = systemsToAdd.Min(s => s.X);
        var maxX = systemsToAdd.Max(s => s.X);
        var minY = systemsToAdd.Min(s => s.Y);
        var maxY = systemsToAdd.Max(s => s.Y);
        var width = Math.Max(1e-9, maxX - minX);
        var height = Math.Max(1e-9, maxY - minY);
        var maxDim = Math.Max(width, height);

        // Aspect-preserving normalization for this imported chunk.
        var normalized = systemsToAdd
            .Select(s => (
                s.Id,
                s.Name,
                X: (s.X - minX) / maxDim,
                Y: 1.0 - ((s.Y - minY) / maxDim)))
            .ToList();

        // First import into an empty layout uses normalized chunk coordinates directly.
        if (existingNodes.Count == 0)
        {
            return normalized;
        }

        var existingMinX = existingNodes.Min(n => n.Value.X);
        var existingMaxX = existingNodes.Max(n => n.Value.X);
        var existingMinY = existingNodes.Min(n => n.Value.Y);
        var existingMaxY = existingNodes.Max(n => n.Value.Y);
        var existingWidth = Math.Max(0.2, existingMaxX - existingMinX);
        var existingCenterY = (existingMinY + existingMaxY) * 0.5;

        // Keep chunk proportions and scale as normalized; only translate into open space.
        var chunkMinX = normalized.Min(n => n.X);
        var chunkMaxX = normalized.Max(n => n.X);
        var chunkMinY = normalized.Min(n => n.Y);
        var chunkMaxY = normalized.Max(n => n.Y);
        var chunkHeight = Math.Max(0.0001, chunkMaxY - chunkMinY);

        var margin = Math.Max(0.06, existingWidth * 0.08);
        var offsetX = existingMaxX + margin - chunkMinX;
        var offsetY = existingCenterY - ((chunkMinY + chunkHeight * 0.5));

        return normalized
            .Select(n => (
                n.Id,
                n.Name,
                X: offsetX + n.X,
                Y: offsetY + n.Y))
            .ToList();
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

    private async Task<List<(long Id, string Name, double X, double Y)>> LoadSdeSystemsForRegionAsync(int sourceRegionId, CancellationToken cancellationToken)
    {
        await using var connection = _sdeDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT solarSystemID, solarSystemName, x2D, y2D
            FROM mapSolarSystems
            WHERE regionID = $regionId
              AND x2D IS NOT NULL
              AND y2D IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$regionId", sourceRegionId);

        var result = new List<(long Id, string Name, double X, double Y)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((reader.GetInt32(0), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3)));
        }

        return result;
    }
}
