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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
