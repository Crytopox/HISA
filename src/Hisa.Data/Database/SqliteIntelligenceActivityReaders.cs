using System.Globalization;
using Hisa.Core.Models.Intelligence;
using Microsoft.Data.Sqlite;

namespace Hisa.Data.Database;

public sealed partial class SqliteIntelligenceHistoryRepository
{
    public const int MaxActivityReaders = 2;
    public const int MaxTimeSeriesBuckets = 30 * 24 * 12;
    public const int MaxShipLossRows = 10000;

    private const string SummaryProjection = """
        SolarSystemId, SUM(KillmailCount), SUM(ConfirmedPvpKills), hisa_decimal_sum(KnownIsk),
        SUM(KnownValues), SUM(UnknownValues), SUM(AttackerEntries), MIN(FirstUtcTicks), MAX(LastUtcTicks)
        """;

    // Reference state avoids boxing the decimal accumulator for every SQLite input row.
    private sealed class IskSum { public decimal Value; }

    // Test seam after a real SQLite snapshot has been established; never holds the writer gate.
    internal Func<CancellationToken, Task>? ActivityReadSnapshotReady { get; set; }

    public Task<ActivityResult> GetSystemActivityAsync(long solarSystemId, ActivityRange range,
        CancellationToken cancellationToken = default)
        => GetActivityAsync(range, [solarSystemId], cancellationToken: cancellationToken);

    public Task<ActivityResult> GetMapActivityAsync(ActivityRange range, CancellationToken cancellationToken = default)
        => GetActivityAsync(range, cancellationToken: cancellationToken);

    public Task<ActivityResult> GetActivityAsync(ActivityRange range, IReadOnlyCollection<long>? systems = null,
        bool includeBuckets = false, CancellationToken cancellationToken = default)
    {
        var effective = range.Effective();
        var ids = SnapshotSystems(systems);
        if (includeBuckets)
        {
            if (ids?.Length != 1)
                throw new ArgumentException("Time-series queries require exactly one system. Use summaries for map queries.", nameof(systems));
            return GetTimeSeriesAsync(ids[0], range, cancellationToken);
        }
        return ReadActivityAsync(async (connection, transaction, token) =>
        {
            using var command = RangeCommand(connection, transaction, effective, ids);
            command.CommandText = $"SELECT {SummaryProjection} FROM {SummarySource(effective, ids)} {command.CommandText} GROUP BY SolarSystemId ORDER BY SolarSystemId;";
            var summaries = await ReadSummariesAsync(command, token);
            if (ids is not null)
            {
                var byId = summaries.ToDictionary(s => s.SolarSystemId);
                summaries = ids.Select(id => byId.GetValueOrDefault(id, new SystemActivity(id, ActivityMetrics.Zero))).ToList();
            }
            var total = summaries.Aggregate(ActivityMetrics.Zero, (sum, system) => Add(sum, system.Metrics));
            var coverage = await ReadCoverageAsync(connection, transaction, effective, token);
            return new ActivityResult(range, effective, summaries, [], [], total, coverage);
        }, cancellationToken);
    }

    public Task<ActivityResult> GetTimeSeriesAsync(long solarSystemId, ActivityRange range,
        CancellationToken cancellationToken = default)
    {
        var effective = range.Effective();
        var ids = SnapshotSystems([solarSystemId]);
        return ReadActivityAsync(async (connection, transaction, token) =>
        {
            using var command = RangeCommand(connection, transaction, effective, ids);
            command.CommandText = """
                SELECT SolarSystemId, BucketStartUtcTicks, KillmailCount, ConfirmedPvpKills, KnownIsk,
                    KnownValues, UnknownValues, AttackerEntries, FirstUtcTicks, LastUtcTicks
                FROM SystemActivity
                """ + "\n" + command.CommandText + " ORDER BY BucketStartUtcTicks LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", MaxTimeSeriesBuckets + 1);
            var buckets = new List<ActivityBucket>();
            var total = ActivityMetrics.Zero;
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (buckets.Count == MaxTimeSeriesBuckets) throw new InvalidOperationException("Time-series result exceeds its bucket bound.");
                    var metrics = ReadActivityMetrics(reader, 2);
                    buckets.Add(new(solarSystemId, FromTicks(reader.GetInt64(1)), metrics));
                    total = Add(total, metrics);
                }
            }
            var coverage = await ReadCoverageAsync(connection, transaction, effective, token);
            return new ActivityResult(range, effective, [new(solarSystemId, total)], buckets, [], total, coverage);
        }, cancellationToken);
    }

    public Task<ShipLossActivityResult> GetShipLossesAsync(ActivityRange range, IReadOnlyCollection<long>? systems = null,
        CancellationToken cancellationToken = default)
    {
        var effective = range.Effective();
        var ids = SnapshotSystems(systems);
        return ReadActivityAsync(async (connection, transaction, token) =>
        {
            using var command = RangeCommand(connection, transaction, effective, ids);
            command.CommandText = """
                SELECT SolarSystemId, VictimShipTypeId, SUM(LossCount) FROM SystemActivity
                JOIN ShipLossActivity USING(SolarSystemId, BucketStartUtcTicks)
                """ + "\n" + command.CommandText + " GROUP BY SolarSystemId, VictimShipTypeId ORDER BY SolarSystemId, VictimShipTypeId LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", MaxShipLossRows + 1);
            var ships = new List<ShipLossActivity>();
            await using (var reader = await command.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    if (ships.Count == MaxShipLossRows) throw new InvalidOperationException("Ship-loss breakdown exceeds 10,000 rows; narrow the system or time selection.");
                    ships.Add(new(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2)));
                }
            }
            return new ShipLossActivityResult(range, effective, ships, await ReadCoverageAsync(connection, transaction, effective, token));
        }, cancellationToken);
    }

    public Task<RankedActivityResult> GetTopSystemsAsync(ActivityRange range, ActivityRanking ranking,
        int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!Enum.IsDefined(ranking)) throw new ArgumentOutOfRangeException(nameof(ranking));
        var effective = range.Effective();
        return ReadActivityAsync(async (connection, transaction, token) =>
        {
            var rankExpression = ranking == ActivityRanking.KillmailCount ? "SUM(KillmailCount)" : "hisa_decimal_sum(KnownIsk)";
            var collation = ranking == ActivityRanking.KnownIskDestroyed ? " COLLATE HISA_DECIMAL" : "";
            using var command = RangeCommand(connection, transaction, effective, null);
            // Only the leaders need the remaining metrics. Neither ranking reads ship losses.
            command.CommandText = $"""
                WITH leaders AS MATERIALIZED (
                    SELECT SolarSystemId, {rankExpression} AS RankValue FROM {SummarySource(effective, null)}
                    {command.CommandText} GROUP BY SolarSystemId
                    ORDER BY RankValue{collation} DESC, SolarSystemId ASC LIMIT $limit
                )
                SELECT {SummaryProjection} FROM leaders CROSS JOIN SystemActivity USING(SolarSystemId)
                WHERE BucketStartUtcTicks >= $from AND BucketStartUtcTicks < $to
                GROUP BY SolarSystemId
                ORDER BY leaders.RankValue{collation} DESC, SolarSystemId ASC;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            var systems = await ReadSummariesAsync(command, token);
            return new RankedActivityResult(range, effective, systems, await ReadCoverageAsync(connection, transaction, effective, token));
        }, cancellationToken);
    }

    private static long[]? SnapshotSystems(IReadOnlyCollection<long>? systems)
    {
        if (systems is null) return null;
        if (systems.Count > 10000) throw new ArgumentOutOfRangeException(nameof(systems));
        var ids = systems.Distinct().Order().ToArray();
        if (ids.Any(id => id <= 0)) throw new ArgumentOutOfRangeException(nameof(systems));
        return ids;
    }

    private static string SummarySource(ActivityRange range, long[]? systems)
        // For wide all-system windows, traverse the existing system/time primary key
        // in GROUP BY order instead of sorting hundreds of thousands of full bucket rows.
        // Short map windows keep SQLite's time-index range plan; filtered systems use PK seeks.
        => systems is null && range.ToUtc - range.FromUtc >= TimeSpan.FromDays(7)
            ? "SystemActivity INDEXED BY sqlite_autoindex_SystemActivity_1" : "SystemActivity";

    private static SqliteCommand RangeCommand(SqliteConnection connection, SqliteTransaction transaction,
        ActivityRange range, long[]? systems)
    {
        var command = ActivityCommand(connection, transaction,
            "WHERE BucketStartUtcTicks >= $from AND BucketStartUtcTicks < $to",
            ("$from", range.FromUtc.UtcTicks), ("$to", range.ToUtc.UtcTicks));
        if (systems is not null)
        {
            if (systems.Length == 0) command.CommandText += " AND 0";
            else if (systems.Length == 1)
            {
                command.CommandText += " AND SolarSystemId=$system";
                command.Parameters.AddWithValue("$system", systems[0]);
            }
            else
            {
                var names = new string[systems.Length];
                for (var i = 0; i < systems.Length; i++)
                {
                    names[i] = "$s" + i;
                    command.Parameters.AddWithValue(names[i], systems[i]);
                }
                command.CommandText += $" AND SolarSystemId IN ({string.Join(',', names)})";
            }
        }
        return command;
    }

    private static async Task<List<SystemActivity>> ReadSummariesAsync(SqliteCommand command, CancellationToken token)
    {
        var results = new List<SystemActivity>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            token.ThrowIfCancellationRequested();
            results.Add(new(reader.GetInt64(0), ReadActivityMetrics(reader, 1)));
        }
        return results;
    }

    private static ActivityMetrics ReadActivityMetrics(SqliteDataReader reader, int offset) => new(
        reader.GetInt64(offset), reader.GetInt64(offset + 1), reader.GetInt64(offset) - reader.GetInt64(offset + 1),
        ParseIsk(reader.GetString(offset + 2)), reader.GetInt64(offset + 3),
        reader.GetInt64(offset + 4), reader.GetInt64(offset + 5), FromTicks(reader.GetInt64(offset + 6)), FromTicks(reader.GetInt64(offset + 7)));

    private async Task<T> ReadActivityAsync<T>(Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> query,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activityReaderShutdown.Token);
        deadline.CancelAfter(_activityQueryTimeout);
        var token = deadline.Token;
        await _activityReaderSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Microsoft.Data.Sqlite executes SQL synchronously, including native aggregate callbacks.
            return await Task.Run(async () =>
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
                if (!Volatile.Read(ref _activityReadsReady))
                {
                    // Admission only: wait for in-progress schema initialization, or fail if never initialized.
                    await _gate.WaitAsync(token).ConfigureAwait(false);
                    try { _ = RequireConnection(); }
                    finally { _gate.Release(); }
                }
                token.ThrowIfCancellationRequested();
                await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private,
                    Pooling = false, DefaultTimeout = 1
                }.ToString());
                await connection.OpenAsync(token).ConfigureAwait(false);
                var handle = connection.Handle;
                // Cancel() on SqliteCommand alone does not interrupt a running native VM.
                // Dispose the registration before closing the handle, including exception paths.
                using var interrupt = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(handle));
                using (var configure = ActivityCommand(connection, null, "PRAGMA query_only=ON; PRAGMA cache_size=-2048; PRAGMA mmap_size=0; PRAGMA temp_store=FILE; PRAGMA busy_timeout=1000;"))
                    await configure.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                connection.CreateAggregate("hisa_decimal_sum", (IskSum?)null,
                    (IskSum? sum, string value) =>
                    {
                        token.ThrowIfCancellationRequested();
                        sum ??= new IskSum();
                        sum.Value = AddExact(sum.Value, ParseIsk(value));
                        return sum;
                    }, sum => (sum?.Value ?? 0m).ToString("G29", CultureInfo.InvariantCulture));
                connection.CreateCollation("HISA_DECIMAL", (left, right) =>
                    ParseIsk(left).CompareTo(ParseIsk(right)));
                using var transaction = connection.BeginTransaction(deferred: true);
                using (var snapshot = ActivityCommand(connection, transaction, "SELECT Version FROM SchemaVersion WHERE Id=1;"))
                    await snapshot.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (ActivityReadSnapshotReady is { } hook) await hook(token).ConfigureAwait(false);
                var result = await query(connection, transaction, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return result;
            }, token).ConfigureAwait(false);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("Historical activity query was cancelled or exceeded its time budget.", token);
        }
        finally { _activityReaderSlots.Release(); }
    }

    private static decimal ParseIsk(string value) => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
