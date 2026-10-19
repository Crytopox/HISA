using Microsoft.Data.Sqlite;

namespace Hisa.Data.Database;

/// <summary>
/// Creates and upgrades the local intelligence history database.
/// Timestamps are stored as INTEGER UTC ticks (<see cref="DateTimeOffset.UtcTicks"/>), which sorts
/// chronologically and round-trips without locale formatting or sub-tick truncation.
/// ISK values are stored as TEXT using the invariant general decimal format so SQLite REAL cannot
/// change the value. NULL stays NULL for every optional column.
/// </summary>
public sealed class SqliteIntelligenceDatabaseInitializer
{
    public const int CurrentSchemaVersion = 3;

    public async Task InitializeAsync(
        SqliteConnection connection,
        int busyTimeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        if (busyTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(busyTimeoutMilliseconds));
        }

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL;", cancellationToken);
        await ExecuteAsync(connection, $"PRAGMA busy_timeout = {busyTimeoutMilliseconds};", cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                Version INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Killmail (
                KillmailId INTEGER NOT NULL PRIMARY KEY,
                Source TEXT NOT NULL,
                SourceSequence INTEGER NULL,
                SolarSystemId INTEGER NOT NULL,
                OccurredAtUtcTicks INTEGER NOT NULL,
                ReceivedAtUtcTicks INTEGER NOT NULL,
                Hash TEXT NULL,
                VictimCharacterId INTEGER NULL,
                VictimCorporationId INTEGER NULL,
                VictimAllianceId INTEGER NULL,
                VictimShipTypeId INTEGER NOT NULL,
                VictimDamageTaken INTEGER NULL,
                TotalValue TEXT NULL,
                AttackerCount INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS KillmailParticipant (
                KillmailId INTEGER NOT NULL,
                ParticipantIndex INTEGER NOT NULL,
                CharacterId INTEGER NULL,
                CorporationId INTEGER NULL,
                AllianceId INTEGER NULL,
                ShipTypeId INTEGER NULL,
                WeaponTypeId INTEGER NULL,
                FinalBlow INTEGER NULL,
                DamageDone INTEGER NULL,
                PRIMARY KEY (KillmailId, ParticipantIndex),
                FOREIGN KEY (KillmailId) REFERENCES Killmail (KillmailId) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CollectionGap (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                RecordedAtUtcTicks INTEGER NOT NULL,
                Kind TEXT NOT NULL,
                Source TEXT NULL,
                SourceSequence INTEGER NULL,
                KillmailId INTEGER NULL,
                Count INTEGER NOT NULL,
                Detail TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Killmail_OccurredAtUtcTicks
                ON Killmail (OccurredAtUtcTicks);

            CREATE INDEX IF NOT EXISTS IX_Killmail_SolarSystemId_OccurredAtUtcTicks
                ON Killmail (SolarSystemId, OccurredAtUtcTicks);

            CREATE INDEX IF NOT EXISTS IX_CollectionGap_RecordedAtUtcTicks
                ON CollectionGap (RecordedAtUtcTicks);

            CREATE TABLE IF NOT EXISTS CoverageCheckpoint (
                Name TEXT NOT NULL PRIMARY KEY,
                Value INTEGER NOT NULL,
                SessionId TEXT NOT NULL DEFAULT ''
            );
            """, cancellationToken);

        var version = await ReadVersionAsync(connection, cancellationToken);
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Intelligence database schema version {version} is newer than this application ({CurrentSchemaVersion}).");
        }

        if (version < CurrentSchemaVersion)
        {
            await ApplyUpgradesAsync(connection, version, cancellationToken);
            await ExecuteAsync(
                connection,
                """
                INSERT INTO SchemaVersion (Id, Version)
                VALUES (1, $version)
                ON CONFLICT (Id) DO UPDATE SET Version = excluded.Version;
                """,
                cancellationToken,
                ("$version", CurrentSchemaVersion));
        }
    }

    public static async Task<int> ReadVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaVersion WHERE Id = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static async Task ApplyUpgradesAsync(
        SqliteConnection connection,
        int fromVersion,
        CancellationToken cancellationToken)
    {
        if (fromVersion < 2)
        {
            await EnsureCoverageCheckpointSessionAsync(connection, cancellationToken);
        }
        if (fromVersion < 3)
        {
            // One transactional DDL step: a crash cannot leave a column without its schema version.
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                ALTER TABLE Killmail ADD COLUMN ActivityAggregated INTEGER NOT NULL DEFAULT 0;
                CREATE INDEX IX_Killmail_ActivityPending ON Killmail(KillmailId) WHERE ActivityAggregated = 0;
                CREATE TABLE SystemActivity (
                    SolarSystemId INTEGER NOT NULL, BucketStartUtcTicks INTEGER NOT NULL,
                    KillmailCount INTEGER NOT NULL, ConfirmedPvpKills INTEGER NOT NULL,
                    KnownIsk TEXT NOT NULL, KnownValues INTEGER NOT NULL, UnknownValues INTEGER NOT NULL,
                    AttackerEntries INTEGER NOT NULL, FirstUtcTicks INTEGER NOT NULL, LastUtcTicks INTEGER NOT NULL,
                    PRIMARY KEY(SolarSystemId, BucketStartUtcTicks));
                CREATE INDEX IX_SystemActivity_Time ON SystemActivity(BucketStartUtcTicks);
                CREATE TABLE ShipLossActivity (
                    SolarSystemId INTEGER NOT NULL, BucketStartUtcTicks INTEGER NOT NULL,
                    VictimShipTypeId INTEGER NOT NULL, LossCount INTEGER NOT NULL,
                    PRIMARY KEY(SolarSystemId, BucketStartUtcTicks, VictimShipTypeId),
                    FOREIGN KEY(SolarSystemId, BucketStartUtcTicks)
                        REFERENCES SystemActivity(SolarSystemId, BucketStartUtcTicks) ON DELETE CASCADE);
                CREATE TABLE CollectionSession (
                    SessionId TEXT NOT NULL PRIMARY KEY, StartUtcTicks INTEGER NOT NULL,
                    LastHeartbeatUtcTicks INTEGER NOT NULL, EndUtcTicks INTEGER NULL, Interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE INDEX IX_CollectionSession_Time ON CollectionSession(LastHeartbeatUtcTicks);
                CREATE TABLE ActivityDedup (
                    KillmailId INTEGER NOT NULL PRIMARY KEY, BucketStartUtcTicks INTEGER NOT NULL);
                CREATE INDEX IX_ActivityDedup_Time ON ActivityDedup(BucketStartUtcTicks);
                INSERT INTO SchemaVersion(Id, Version) VALUES(1, 3)
                    ON CONFLICT(Id) DO UPDATE SET Version = 3;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task EnsureCoverageCheckpointSessionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CoverageCheckpoint') WHERE name = 'SessionId';";
        var existing = Convert.ToInt32(await probe.ExecuteScalarAsync(cancellationToken));
        if (existing > 0)
        {
            return;
        }

        var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE CoverageCheckpoint ADD COLUMN SessionId TEXT NOT NULL DEFAULT '';";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
