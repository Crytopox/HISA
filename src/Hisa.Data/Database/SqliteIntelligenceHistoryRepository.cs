using System.Globalization;
using Hisa.Core.Abstractions;
using Hisa.Core.Models.Intelligence;
using Microsoft.Data.Sqlite;

namespace Hisa.Data.Database;

public sealed class SqliteIntelligenceHistoryRepository : IIntelligenceHistoryRepository
{
    private const string KillmailColumns = """
        KillmailId, Source, SourceSequence, SolarSystemId,
        OccurredAtUtcTicks, ReceivedAtUtcTicks, Hash,
        VictimCharacterId, VictimCorporationId, VictimAllianceId,
        VictimShipTypeId, VictimDamageTaken, TotalValue, AttackerCount
        """;

    private readonly int _busyTimeoutMilliseconds;
    private readonly int _maxQueryLimit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SqliteIntelligenceDatabaseInitializer _initializer = new();
    private SqliteConnection? _connection;
    private SqliteCommand? _insertKillmail;
    private SqliteCommand? _insertParticipant;
    private SqliteCommand? _insertGap;
    private bool _disposed;

    public SqliteIntelligenceHistoryRepository(string databasePath, TimeSpan busyTimeout, int maxQueryLimit = 1000)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        if (maxQueryLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueryLimit));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        _busyTimeoutMilliseconds = (int)Math.Clamp(busyTimeout.TotalMilliseconds, 0, int.MaxValue);
        _maxQueryLimit = maxQueryLimit;
    }

    public string DatabasePath { get; }

    internal Action<long>? AfterKillmailInsert { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString();

            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            try
            {
                await _initializer.InitializeAsync(connection, _busyTimeoutMilliseconds, cancellationToken);
                _connection = connection;
                PrepareWrites(connection);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KillmailWriteResult> InsertBatchAsync(
        IReadOnlyList<KillmailObservation> observations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var insertKillmail = RequireCommand(_insertKillmail);
            var insertParticipant = RequireCommand(_insertParticipant);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            insertKillmail.Transaction = transaction;
            insertParticipant.Transaction = transaction;
            var inserted = 0;
            var duplicates = 0;
            foreach (var observation in observations)
            {
                var wrote = await InsertKillmailAsync(insertKillmail, observation, cancellationToken);
                if (!wrote)
                {
                    duplicates++;
                    continue;
                }

                AfterKillmailInsert?.Invoke(observation.KillmailId);
                await InsertParticipantsAsync(insertParticipant, observation, cancellationToken);
                inserted++;
            }

            await transaction.CommitAsync(cancellationToken);
            return new KillmailWriteResult(inserted, duplicates, 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordGapsAsync(
        IReadOnlyList<IntelligenceCollectionGap> gaps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        if (gaps.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var insertGap = RequireCommand(_insertGap);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            insertGap.Transaction = transaction;
            foreach (var gap in gaps)
            {
                BindGap(insertGap, gap);
                await insertGap.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteOccurredBeforeAsync(
        DateTimeOffset cutoffUtc,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        if (maxRows < 1)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var deleteKillmails = connection.CreateCommand();
            deleteKillmails.Transaction = transaction;
            deleteKillmails.CommandText = """
                DELETE FROM Killmail
                WHERE KillmailId IN (
                    SELECT KillmailId FROM Killmail
                    WHERE OccurredAtUtcTicks < $cutoff
                    ORDER BY OccurredAtUtcTicks ASC, KillmailId ASC
                    LIMIT $limit
                );
                """;
            deleteKillmails.Parameters.AddWithValue("$cutoff", ToTicks(cutoffUtc));
            deleteKillmails.Parameters.AddWithValue("$limit", maxRows);
            var deleted = await deleteKillmails.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteGapsRecordedBeforeAsync(
        DateTimeOffset cutoffUtc,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        if (maxRows < 1)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var deleteGaps = connection.CreateCommand();
            deleteGaps.CommandText = """
                DELETE FROM CollectionGap
                WHERE Id IN (
                    SELECT Id FROM CollectionGap
                    WHERE RecordedAtUtcTicks < $cutoff
                    ORDER BY RecordedAtUtcTicks ASC, Id ASC
                    LIMIT $limit
                );
                """;
            deleteGaps.Parameters.AddWithValue("$cutoff", ToTicks(cutoffUtc));
            deleteGaps.Parameters.AddWithValue("$limit", maxRows);
            return await deleteGaps.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KillmailHistoryPage> GetTimeRangePageAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcInclusive,
        int limit,
        KillmailHistoryCursor? after,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            return new KillmailHistoryPage([], null);
        }

        var boundedLimit = Math.Min(limit, _maxQueryLimit);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {KillmailColumns}
                FROM Killmail
                WHERE OccurredAtUtcTicks >= $from
                  AND OccurredAtUtcTicks <= $to
                  AND (
                    $afterTicks IS NULL
                    OR OccurredAtUtcTicks > $afterTicks
                    OR (OccurredAtUtcTicks = $afterTicks AND KillmailId > $afterId)
                  )
                ORDER BY OccurredAtUtcTicks ASC, KillmailId ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$from", ToTicks(fromUtcInclusive));
            command.Parameters.AddWithValue("$to", ToTicks(toUtcInclusive));
            command.Parameters.AddWithValue("$afterTicks", after is KillmailHistoryCursor cursor ? ToTicks(cursor.OccurredAtUtc) : DBNull.Value);
            command.Parameters.AddWithValue("$afterId", after?.KillmailId ?? 0L);
            command.Parameters.AddWithValue("$limit", boundedLimit);
            var results = new List<KillmailHistoryRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadKillmail(reader));
            }

            KillmailHistoryCursor? next = results.Count == boundedLimit
                ? new KillmailHistoryCursor(results[^1].OccurredAtUtc, results[^1].KillmailId)
                : null;
            return new KillmailHistoryPage(results, next);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<IntelligenceCollectionGap>> GetCollectionGapsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            return [];
        }

        var boundedLimit = Math.Min(limit, _maxQueryLimit);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT RecordedAtUtcTicks, Kind, Source, SourceSequence, KillmailId, Count, Detail
                FROM CollectionGap
                ORDER BY Id ASC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", boundedLimit);
            var gaps = new List<IntelligenceCollectionGap>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                gaps.Add(new IntelligenceCollectionGap
                {
                    RecordedAtUtc = FromTicks(reader.GetInt64(0)),
                    Kind = reader.GetString(1),
                    Source = ReadNullableString(reader, 2),
                    SourceSequence = ReadNullableInt64(reader, 3),
                    KillmailId = ReadNullableInt64(reader, 4),
                    Count = Convert.ToInt32(reader.GetValue(5), System.Globalization.CultureInfo.InvariantCulture),
                    Detail = ReadNullableString(reader, 6)
                });
            }

            return gaps;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> RecordCoverageDeltaAsync(
        string checkpointName,
        long observedTotal,
        IntelligenceCollectionGap gap,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointName);
        ArgumentNullException.ThrowIfNull(gap);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var insertGap = RequireCommand(_insertGap);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            insertGap.Transaction = transaction;
            var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT Value FROM CoverageCheckpoint WHERE Name = $name;";
            read.Parameters.AddWithValue("$name", checkpointName);
            var storedValue = await read.ExecuteScalarAsync(cancellationToken);
            var stored = storedValue is null or DBNull ? 0L : Convert.ToInt64(storedValue);
            if (observedTotal <= stored)
            {
                await transaction.CommitAsync(cancellationToken);
                return 0;
            }

            var delta = observedTotal - stored;
            var recorded = delta > int.MaxValue ? int.MaxValue : (int)delta;
            BindGap(insertGap, gap with { Count = recorded, RecordedAtUtc = gap.RecordedAtUtc == default ? DateTimeOffset.UtcNow : gap.RecordedAtUtc });
            await insertGap.ExecuteNonQueryAsync(cancellationToken);

            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                INSERT INTO CoverageCheckpoint (Name, Value)
                VALUES ($name, $value)
                ON CONFLICT (Name) DO UPDATE SET Value = excluded.Value;
                """;
            update.Parameters.AddWithValue("$name", checkpointName);
            update.Parameters.AddWithValue("$value", stored + recorded);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return recorded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<KillmailHistoryRecord>> GetByTimeRangeAsync(
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcInclusive,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return QueryKillmailsAsync(
            """
            SELECT {columns}
            FROM Killmail
            WHERE OccurredAtUtcTicks >= $from AND OccurredAtUtcTicks <= $to
            ORDER BY OccurredAtUtcTicks ASC, KillmailId ASC
            LIMIT $limit;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$from", ToTicks(fromUtcInclusive));
                command.Parameters.AddWithValue("$to", ToTicks(toUtcInclusive));
            },
            limit,
            cancellationToken);
    }

    public Task<IReadOnlyList<KillmailHistoryRecord>> GetBySolarSystemAsync(
        long solarSystemId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcInclusive,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return QueryKillmailsAsync(
            """
            SELECT {columns}
            FROM Killmail
            WHERE SolarSystemId = $solarSystemId
              AND OccurredAtUtcTicks >= $from
              AND OccurredAtUtcTicks <= $to
            ORDER BY OccurredAtUtcTicks ASC, KillmailId ASC
            LIMIT $limit;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$solarSystemId", solarSystemId);
                command.Parameters.AddWithValue("$from", ToTicks(fromUtcInclusive));
                command.Parameters.AddWithValue("$to", ToTicks(toUtcInclusive));
            },
            limit,
            cancellationToken);
    }

    public async Task<KillmailHistoryDetail?> GetKillmailAsync(long killmailId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var killmailCommand = connection.CreateCommand();
            killmailCommand.CommandText = $"SELECT {KillmailColumns} FROM Killmail WHERE KillmailId = $killmailId;";
            killmailCommand.Parameters.AddWithValue("$killmailId", killmailId);
            KillmailHistoryRecord killmail;
            await using (var reader = await killmailCommand.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                killmail = ReadKillmail(reader);
            }

            var participants = new List<KillmailParticipantRecord>();
            var participantCommand = connection.CreateCommand();
            participantCommand.CommandText = """
                SELECT ParticipantIndex, CharacterId, CorporationId, AllianceId,
                       ShipTypeId, WeaponTypeId, FinalBlow, DamageDone
                FROM KillmailParticipant
                WHERE KillmailId = $killmailId
                ORDER BY ParticipantIndex ASC;
                """;
            participantCommand.Parameters.AddWithValue("$killmailId", killmailId);
            await using var participantReader = await participantCommand.ExecuteReaderAsync(cancellationToken);
            while (await participantReader.ReadAsync(cancellationToken))
            {
                participants.Add(new KillmailParticipantRecord
                {
                    ParticipantIndex = participantReader.GetInt32(0),
                    CharacterId = ReadNullableInt32(participantReader, 1),
                    CorporationId = ReadNullableInt32(participantReader, 2),
                    AllianceId = ReadNullableInt32(participantReader, 3),
                    ShipTypeId = ReadNullableInt32(participantReader, 4),
                    WeaponTypeId = ReadNullableInt32(participantReader, 5),
                    FinalBlow = ReadNullableBoolean(participantReader, 6),
                    DamageDone = ReadNullableInt64(participantReader, 7)
                });
            }

            return new KillmailHistoryDetail
            {
                Killmail = killmail,
                Participants = participants
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<KillmailHistoryRecord>> GetRecentAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var boundedOffset = Math.Max(0, offset);
        return QueryKillmailsAsync(
            """
            SELECT {columns}
            FROM Killmail
            ORDER BY OccurredAtUtcTicks DESC, KillmailId DESC
            LIMIT $limit OFFSET $offset;
            """,
            command => command.Parameters.AddWithValue("$offset", boundedOffset),
            limit,
            cancellationToken);
    }

    public async Task<IntelligenceHistoryHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var killmails = await ScalarInt64Async(connection, "SELECT COUNT(*) FROM Killmail;", cancellationToken);
            var participants = await ScalarInt64Async(connection, "SELECT COUNT(*) FROM KillmailParticipant;", cancellationToken);
            var gaps = await ScalarInt64Async(connection, "SELECT COUNT(*) FROM CollectionGap;", cancellationToken);
            var version = await SqliteIntelligenceDatabaseInitializer.ReadVersionAsync(connection, cancellationToken);
            var journal = await ScalarStringAsync(connection, "PRAGMA journal_mode;", cancellationToken);
            return new IntelligenceHistoryHealth(
                killmails,
                participants,
                gaps,
                version,
                string.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase),
                MeasureDatabaseBytes(DatabasePath),
                DatabasePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync();
        try
        {
            _insertKillmail?.Dispose();
            _insertParticipant?.Dispose();
            _insertGap?.Dispose();
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<IReadOnlyList<KillmailHistoryRecord>> QueryKillmailsAsync(
        string sqlTemplate,
        Action<SqliteCommand> bind,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit < 1)
        {
            return [];
        }

        var boundedLimit = Math.Min(limit, _maxQueryLimit);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            var command = connection.CreateCommand();
            command.CommandText = sqlTemplate.Replace("{columns}", KillmailColumns, StringComparison.Ordinal);
            bind(command);
            command.Parameters.AddWithValue("$limit", boundedLimit);
            var results = new List<KillmailHistoryRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadKillmail(reader));
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PrepareWrites(SqliteConnection connection)
    {
        _insertKillmail = connection.CreateCommand();
        _insertKillmail.CommandText = """
            INSERT OR IGNORE INTO Killmail (
                KillmailId, Source, SourceSequence, SolarSystemId,
                OccurredAtUtcTicks, ReceivedAtUtcTicks, Hash,
                VictimCharacterId, VictimCorporationId, VictimAllianceId,
                VictimShipTypeId, VictimDamageTaken, TotalValue, AttackerCount)
            VALUES (
                $killmailId, $source, $sourceSequence, $solarSystemId,
                $occurred, $received, $hash,
                $victimCharacterId, $victimCorporationId, $victimAllianceId,
                $victimShipTypeId, $victimDamageTaken, $totalValue, $attackerCount);
            """;
        AddParameters(
            _insertKillmail,
            "$killmailId", "$source", "$sourceSequence", "$solarSystemId",
            "$occurred", "$received", "$hash",
            "$victimCharacterId", "$victimCorporationId", "$victimAllianceId",
            "$victimShipTypeId", "$victimDamageTaken", "$totalValue", "$attackerCount");

        _insertParticipant = connection.CreateCommand();
        _insertParticipant.CommandText = """
            INSERT INTO KillmailParticipant (
                KillmailId, ParticipantIndex, CharacterId, CorporationId, AllianceId,
                ShipTypeId, WeaponTypeId, FinalBlow, DamageDone)
            VALUES (
                $killmailId, $participantIndex, $characterId, $corporationId, $allianceId,
                $shipTypeId, $weaponTypeId, $finalBlow, $damageDone);
            """;
        AddParameters(
            _insertParticipant,
            "$killmailId", "$participantIndex", "$characterId", "$corporationId", "$allianceId",
            "$shipTypeId", "$weaponTypeId", "$finalBlow", "$damageDone");

        _insertGap = connection.CreateCommand();
        _insertGap.CommandText = """
            INSERT INTO CollectionGap (
                RecordedAtUtcTicks, Kind, Source, SourceSequence, KillmailId, Count, Detail)
            VALUES (
                $recorded, $kind, $source, $sourceSequence, $killmailId, $count, $detail);
            """;
        AddParameters(
            _insertGap,
            "$recorded", "$kind", "$source", "$sourceSequence", "$killmailId", "$count", "$detail");
        _insertKillmail.Prepare();
        _insertParticipant.Prepare();
        _insertGap.Prepare();
    }

    private static async Task<bool> InsertKillmailAsync(
        SqliteCommand command,
        KillmailObservation observation,
        CancellationToken cancellationToken)
    {
        command.Parameters["$killmailId"].Value = observation.KillmailId;
        command.Parameters["$source"].Value = observation.Source;
        command.Parameters["$sourceSequence"].Value = DbValue(observation.SourceSequence);
        command.Parameters["$solarSystemId"].Value = observation.SolarSystemId;
        command.Parameters["$occurred"].Value = ToTicks(observation.OccurredAtUtc);
        command.Parameters["$received"].Value = ToTicks(observation.ReceivedAtUtc);
        command.Parameters["$hash"].Value = DbValue(observation.Hash);
        command.Parameters["$victimCharacterId"].Value = DbValue(observation.Victim.CharacterId);
        command.Parameters["$victimCorporationId"].Value = DbValue(observation.Victim.CorporationId);
        command.Parameters["$victimAllianceId"].Value = DbValue(observation.Victim.AllianceId);
        command.Parameters["$victimShipTypeId"].Value = observation.Victim.ShipTypeId;
        command.Parameters["$victimDamageTaken"].Value = DbValue(observation.Victim.DamageTaken);
        command.Parameters["$totalValue"].Value = observation.TotalValue is decimal totalValue
            ? totalValue.ToString("G29", CultureInfo.InvariantCulture)
            : DBNull.Value;
        command.Parameters["$attackerCount"].Value = observation.Attackers.Count;
        var written = await command.ExecuteNonQueryAsync(cancellationToken);
        return written > 0;
    }

    private static async Task InsertParticipantsAsync(
        SqliteCommand command,
        KillmailObservation observation,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < observation.Attackers.Count; index++)
        {
            var attacker = observation.Attackers[index];
            command.Parameters["$killmailId"].Value = observation.KillmailId;
            command.Parameters["$participantIndex"].Value = index;
            command.Parameters["$characterId"].Value = DbValue(attacker.CharacterId);
            command.Parameters["$corporationId"].Value = DbValue(attacker.CorporationId);
            command.Parameters["$allianceId"].Value = DbValue(attacker.AllianceId);
            command.Parameters["$shipTypeId"].Value = DbValue(attacker.ShipTypeId);
            command.Parameters["$weaponTypeId"].Value = DbValue(attacker.WeaponTypeId);
            command.Parameters["$finalBlow"].Value = attacker.FinalBlow switch
            {
                true => 1,
                false => 0,
                null => DBNull.Value
            };
            command.Parameters["$damageDone"].Value = DbValue(attacker.DamageDone);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void BindGap(SqliteCommand command, IntelligenceCollectionGap gap)
    {
        command.Parameters["$recorded"].Value = ToTicks(gap.RecordedAtUtc);
        command.Parameters["$kind"].Value = gap.Kind;
        command.Parameters["$source"].Value = DbValue(gap.Source);
        command.Parameters["$sourceSequence"].Value = DbValue(gap.SourceSequence);
        command.Parameters["$killmailId"].Value = DbValue(gap.KillmailId);
        command.Parameters["$count"].Value = gap.Count < 1 ? 1 : gap.Count;
        command.Parameters["$detail"].Value = DbValue(gap.Detail);
    }

    private static KillmailHistoryRecord ReadKillmail(SqliteDataReader reader)
    {
        return new KillmailHistoryRecord
        {
            KillmailId = reader.GetInt64(0),
            Source = reader.GetString(1),
            SourceSequence = ReadNullableInt64(reader, 2),
            SolarSystemId = reader.GetInt64(3),
            OccurredAtUtc = FromTicks(reader.GetInt64(4)),
            ReceivedAtUtc = FromTicks(reader.GetInt64(5)),
            Hash = ReadNullableString(reader, 6),
            VictimCharacterId = ReadNullableInt32(reader, 7),
            VictimCorporationId = ReadNullableInt32(reader, 8),
            VictimAllianceId = ReadNullableInt32(reader, 9),
            VictimShipTypeId = reader.GetInt32(10),
            VictimDamageTaken = ReadNullableInt64(reader, 11),
            TotalValue = ReadNullableDecimal(reader, 12),
            AttackerCount = reader.GetInt32(13)
        };
    }

    private SqliteConnection RequireConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _connection ?? throw new InvalidOperationException("Intelligence history database is not initialized.");
    }

    private static SqliteCommand RequireCommand(SqliteCommand? command)
    {
        return command ?? throw new InvalidOperationException("Intelligence history database is not initialized.");
    }

    private static void AddParameters(SqliteCommand command, params string[] names)
    {
        foreach (var name in names)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            command.Parameters.Add(parameter);
        }
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static async Task<string?> ScalarStringAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static long ToTicks(DateTimeOffset value) => value.ToUniversalTime().UtcTicks;

    private static DateTimeOffset FromTicks(long ticks) => new(ticks, TimeSpan.Zero);

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? ReadNullableInt32(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long? ReadNullableInt64(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static bool? ReadNullableBoolean(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture) != 0;
    }

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return decimal.Parse(reader.GetString(ordinal), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static long MeasureDatabaseBytes(string databasePath)
    {
        long size = 0;
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                size += new FileInfo(path).Length;
            }
        }

        return size;
    }
}
