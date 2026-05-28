using System.Text.Json;
using Hisa.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hisa.Data.Database;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface ISdeDatabase
{
    SqliteConnection CreateConnection();
}

public sealed class SqliteDatabaseInitializer : IDatabaseInitializer
{
    private readonly string _connectionString;
    private const string ImportedPackName = "Imported Base";

    public SqliteDatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MapLayoutPack (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                IsBase INTEGER NOT NULL DEFAULT 0,
                IsReadOnly INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_MapLayoutPack_Name ON MapLayoutPack(Name COLLATE NOCASE);

            CREATE TABLE IF NOT EXISTS MapLayoutRegion (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                PackId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                SourceRegionId INTEGER NULL,
                IsGameRegion INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(PackId) REFERENCES MapLayoutPack(Id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_MapLayoutRegion_Name ON MapLayoutRegion(Name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS IX_MapLayoutRegion_SourceRegionId ON MapLayoutRegion(SourceRegionId);

            CREATE TABLE IF NOT EXISTS MapLayoutNode (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                RegionLayoutId INTEGER NOT NULL,
                SolarSystemId INTEGER NULL,
                Name TEXT NOT NULL,
                X REAL NOT NULL,
                Y REAL NOT NULL,
                FOREIGN KEY(RegionLayoutId) REFERENCES MapLayoutRegion(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_MapLayoutNode_RegionLayoutId ON MapLayoutNode(RegionLayoutId);
            CREATE INDEX IF NOT EXISTS IX_MapLayoutNode_SolarSystemId ON MapLayoutNode(SolarSystemId);

            CREATE TABLE IF NOT EXISTS MapLayoutLink (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                RegionLayoutId INTEGER NOT NULL,
                FromNodeId INTEGER NOT NULL,
                ToNodeId INTEGER NOT NULL,
                FOREIGN KEY(RegionLayoutId) REFERENCES MapLayoutRegion(Id) ON DELETE CASCADE,
                FOREIGN KEY(FromNodeId) REFERENCES MapLayoutNode(Id) ON DELETE CASCADE,
                FOREIGN KEY(ToNodeId) REFERENCES MapLayoutNode(Id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_MapLayoutLink_Unique ON MapLayoutLink(RegionLayoutId, FromNodeId, ToNodeId);

            INSERT INTO MapLayoutPack(Name, IsBase, IsReadOnly)
            SELECT 'HISA Base', 1, 1
            WHERE NOT EXISTS (SELECT 1 FROM MapLayoutPack WHERE Name = 'HISA Base');
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await SyncImportedBaseLayoutsAsync(connection, cancellationToken);
    }

    private static string? ResolveImportedLayoutSourcePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "solarSystemsStaticData.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Hisa.App", "Data", "solarSystemsStaticData.db"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "solarSystemsStaticData.db")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task SyncImportedBaseLayoutsAsync(SqliteConnection targetConnection, CancellationToken cancellationToken)
    {
        var sourcePath = ResolveImportedLayoutSourcePath();
        if (sourcePath is null)
        {
            return;
        }

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        await using var sourceConnection = new SqliteConnection(sourceConnectionString);
        await sourceConnection.OpenAsync(cancellationToken);

        var sourcePackIdCommand = sourceConnection.CreateCommand();
        sourcePackIdCommand.CommandText = """
            SELECT p.Id
            FROM MapLayoutPack p
            LEFT JOIN (
                SELECT PackId, COUNT(1) AS RegionCount
                FROM MapLayoutRegion
                GROUP BY PackId
            ) r ON r.PackId = p.Id
            WHERE p.Name = 'Imported Base'
               OR (p.IsBase = 1 AND p.IsReadOnly = 1 AND IFNULL(r.RegionCount, 0) > 0)
            ORDER BY
                CASE
                    WHEN p.Name = 'Imported Base' THEN 0
                    ELSE 1
                END,
                IFNULL(r.RegionCount, 0) DESC,
                p.Id DESC
            LIMIT 1;
            """;
        var sourcePackIdRaw = await sourcePackIdCommand.ExecuteScalarAsync(cancellationToken);
        if (sourcePackIdRaw is null or DBNull)
        {
            return;
        }

        var sourcePackId = Convert.ToInt64(sourcePackIdRaw);
        await using var tx = (SqliteTransaction)await targetConnection.BeginTransactionAsync(cancellationToken);

        var existingPackIdCommand = targetConnection.CreateCommand();
        existingPackIdCommand.Transaction = tx;
        existingPackIdCommand.CommandText = """
            SELECT Id
            FROM MapLayoutPack
            WHERE Name IN ('Imported Base')
               OR (IsBase = 1 AND IsReadOnly = 1 AND Name <> 'HISA Base')
            ORDER BY Id;
            """;
        var existingPackIds = new List<long>();
        await using (var existingReader = await existingPackIdCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await existingReader.ReadAsync(cancellationToken))
            {
                existingPackIds.Add(existingReader.GetInt64(0));
            }
        }

        foreach (var existingPackId in existingPackIds)
        {
            await DeletePackByIdAsync(targetConnection, tx, existingPackId, cancellationToken);
        }

        var insertPackCommand = targetConnection.CreateCommand();
        insertPackCommand.Transaction = tx;
        insertPackCommand.CommandText = """
            INSERT INTO MapLayoutPack(Name, IsBase, IsReadOnly)
            VALUES($name, 1, 1);
            SELECT last_insert_rowid();
            """;
        insertPackCommand.Parameters.AddWithValue("$name", ImportedPackName);
        var newPackId = Convert.ToInt64(await insertPackCommand.ExecuteScalarAsync(cancellationToken));

        var regionIdMap = new Dictionary<long, long>();
        var sourceRegionCommand = sourceConnection.CreateCommand();
        sourceRegionCommand.CommandText = """
            SELECT Id, Name, SourceRegionId, IsGameRegion
            FROM MapLayoutRegion
            WHERE PackId = $packId;
            """;
        sourceRegionCommand.Parameters.AddWithValue("$packId", sourcePackId);
        await using (var regionReader = await sourceRegionCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await regionReader.ReadAsync(cancellationToken))
            {
                var sourceRegionLayoutId = regionReader.GetInt64(0);
                var insertRegionCommand = targetConnection.CreateCommand();
                insertRegionCommand.Transaction = tx;
                insertRegionCommand.CommandText = """
                    INSERT INTO MapLayoutRegion(PackId, Name, SourceRegionId, IsGameRegion)
                    VALUES($packId, $name, $sourceRegionId, $isGameRegion);
                    SELECT last_insert_rowid();
                    """;
                insertRegionCommand.Parameters.AddWithValue("$packId", newPackId);
                insertRegionCommand.Parameters.AddWithValue("$name", regionReader.GetString(1));
                insertRegionCommand.Parameters.AddWithValue("$sourceRegionId", regionReader.IsDBNull(2) ? DBNull.Value : regionReader.GetInt32(2));
                insertRegionCommand.Parameters.AddWithValue("$isGameRegion", regionReader.GetInt32(3));
                var targetRegionLayoutId = Convert.ToInt64(await insertRegionCommand.ExecuteScalarAsync(cancellationToken));
                regionIdMap[sourceRegionLayoutId] = targetRegionLayoutId;
            }
        }

        foreach (var (sourceRegionLayoutId, targetRegionLayoutId) in regionIdMap)
        {
            var nodeIdMap = new Dictionary<long, long>();
            var sourceNodeCommand = sourceConnection.CreateCommand();
            sourceNodeCommand.CommandText = """
                SELECT Id, SolarSystemId, Name, X, Y
                FROM MapLayoutNode
                WHERE RegionLayoutId = $layoutId;
                """;
            sourceNodeCommand.Parameters.AddWithValue("$layoutId", sourceRegionLayoutId);
            await using (var nodeReader = await sourceNodeCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await nodeReader.ReadAsync(cancellationToken))
                {
                    var sourceNodeId = nodeReader.GetInt64(0);
                    var insertNodeCommand = targetConnection.CreateCommand();
                    insertNodeCommand.Transaction = tx;
                    insertNodeCommand.CommandText = """
                        INSERT INTO MapLayoutNode(RegionLayoutId, SolarSystemId, Name, X, Y)
                        VALUES($layoutId, $solarSystemId, $name, $x, $y);
                        SELECT last_insert_rowid();
                        """;
                    insertNodeCommand.Parameters.AddWithValue("$layoutId", targetRegionLayoutId);
                    insertNodeCommand.Parameters.AddWithValue("$solarSystemId", nodeReader.IsDBNull(1) ? DBNull.Value : nodeReader.GetInt64(1));
                    insertNodeCommand.Parameters.AddWithValue("$name", nodeReader.GetString(2));
                    insertNodeCommand.Parameters.AddWithValue("$x", nodeReader.GetDouble(3));
                    insertNodeCommand.Parameters.AddWithValue("$y", nodeReader.GetDouble(4));
                    var targetNodeId = Convert.ToInt64(await insertNodeCommand.ExecuteScalarAsync(cancellationToken));
                    nodeIdMap[sourceNodeId] = targetNodeId;
                }
            }

            var sourceLinkCommand = sourceConnection.CreateCommand();
            sourceLinkCommand.CommandText = """
                SELECT FromNodeId, ToNodeId
                FROM MapLayoutLink
                WHERE RegionLayoutId = $layoutId;
                """;
            sourceLinkCommand.Parameters.AddWithValue("$layoutId", sourceRegionLayoutId);
            await using var linkReader = await sourceLinkCommand.ExecuteReaderAsync(cancellationToken);
            while (await linkReader.ReadAsync(cancellationToken))
            {
                var fromSourceNodeId = linkReader.GetInt64(0);
                var toSourceNodeId = linkReader.GetInt64(1);
                if (!nodeIdMap.TryGetValue(fromSourceNodeId, out var fromTargetNodeId) ||
                    !nodeIdMap.TryGetValue(toSourceNodeId, out var toTargetNodeId))
                {
                    continue;
                }

                var insertLinkCommand = targetConnection.CreateCommand();
                insertLinkCommand.Transaction = tx;
                insertLinkCommand.CommandText = """
                    INSERT OR IGNORE INTO MapLayoutLink(RegionLayoutId, FromNodeId, ToNodeId)
                    VALUES($layoutId, $fromNodeId, $toNodeId);
                    """;
                insertLinkCommand.Parameters.AddWithValue("$layoutId", targetRegionLayoutId);
                insertLinkCommand.Parameters.AddWithValue("$fromNodeId", Math.Min(fromTargetNodeId, toTargetNodeId));
                insertLinkCommand.Parameters.AddWithValue("$toNodeId", Math.Max(fromTargetNodeId, toTargetNodeId));
                await insertLinkCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task DeletePackByIdAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        long packId,
        CancellationToken cancellationToken)
    {
        var clearLinks = connection.CreateCommand();
        clearLinks.Transaction = tx;
        clearLinks.CommandText = """
            DELETE FROM MapLayoutLink
            WHERE RegionLayoutId IN (SELECT Id FROM MapLayoutRegion WHERE PackId = $packId);
            """;
        clearLinks.Parameters.AddWithValue("$packId", packId);
        await clearLinks.ExecuteNonQueryAsync(cancellationToken);

        var clearNodes = connection.CreateCommand();
        clearNodes.Transaction = tx;
        clearNodes.CommandText = """
            DELETE FROM MapLayoutNode
            WHERE RegionLayoutId IN (SELECT Id FROM MapLayoutRegion WHERE PackId = $packId);
            """;
        clearNodes.Parameters.AddWithValue("$packId", packId);
        await clearNodes.ExecuteNonQueryAsync(cancellationToken);

        var clearRegions = connection.CreateCommand();
        clearRegions.Transaction = tx;
        clearRegions.CommandText = "DELETE FROM MapLayoutRegion WHERE PackId = $packId;";
        clearRegions.Parameters.AddWithValue("$packId", packId);
        await clearRegions.ExecuteNonQueryAsync(cancellationToken);

        var clearPack = connection.CreateCommand();
        clearPack.Transaction = tx;
        clearPack.CommandText = "DELETE FROM MapLayoutPack WHERE Id = $packId;";
        clearPack.Parameters.AddWithValue("$packId", packId);
        await clearPack.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SqliteSettingsService : ISettingsService
{
    private readonly string _connectionString;

    public SqliteSettingsService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not string json)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json);
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Settings(Key, Value)
            VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", json);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SdeSqliteDatabase : ISdeDatabase
{
    private readonly string _connectionString;

    public SdeSqliteDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqliteConnection CreateConnection() => new(_connectionString);
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHisaData(this IServiceCollection services, IConfiguration configuration)
    {
        var fileName = configuration["Hisa:DatabaseFileName"] ?? "hisa.db";
        var sdeFileName = configuration["Hisa:SdeDatabaseFileName"] ?? "eve-hk-sde.db";
        var explicitSdePath = configuration["Hisa:SdeDatabasePath"];
        var dbDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HISA");
        Directory.CreateDirectory(dbDirectory);
        var dbPath = Path.Combine(dbDirectory, fileName);
        var sdeDbPath = ResolveSdeDatabasePath(dbDirectory, sdeFileName, explicitSdePath);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        var sdeConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sdeDbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        services.AddSingleton<IDatabaseInitializer>(_ => new SqliteDatabaseInitializer(connectionString));
        services.AddSingleton<ISettingsService>(_ => new SqliteSettingsService(connectionString));
        services.AddSingleton<IMapLayoutDataService>(_ => new SqliteMapLayoutDataService(connectionString));
        services.AddSingleton<IMapLayoutEditorService>(sp =>
            new SqliteMapLayoutEditorService(connectionString, sp.GetRequiredService<ISdeDatabase>()));
        services.AddSingleton<ISdeDatabase>(_ => new SdeSqliteDatabase(sdeConnectionString));

        return services;
    }

    private static string ResolveSdeDatabasePath(string localDataDirectory, string sdeFileName, string? explicitSdePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitSdePath) && File.Exists(explicitSdePath))
        {
            return explicitSdePath;
        }

        var localDataPath = Path.Combine(localDataDirectory, sdeFileName);
        if (File.Exists(localDataPath))
        {
            return localDataPath;
        }

        var appBasePath = Path.Combine(AppContext.BaseDirectory, "Data", sdeFileName);
        if (File.Exists(appBasePath))
        {
            return appBasePath;
        }

        return localDataPath;
    }
}
