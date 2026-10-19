using System.Globalization;
using System.Numerics;
using Hisa.Core.Models.Intelligence;
using Microsoft.Data.Sqlite;

namespace Hisa.Data.Database;

public sealed partial class SqliteIntelligenceHistoryRepository
{
    private static SqliteCommand ActivityCommand(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static async Task AggregateAsync(SqliteConnection connection, SqliteTransaction transaction,
        long id, long system, DateTimeOffset occurred, int ship, decimal? value, int attackers, bool pvp,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var bucket = ActivityRange.BucketStart(occurred).UtcTicks;
        using var claim = ActivityCommand(connection, transaction, """
            INSERT OR IGNORE INTO ActivityDedup(KillmailId, BucketStartUtcTicks)
            SELECT $id, $bucket WHERE $bucket >= COALESCE(
                (SELECT Value FROM CoverageCheckpoint WHERE Name = 'ActivityRetentionFloor'), 0);
            """, ("$id", id), ("$bucket", bucket));
        if (await claim.ExecuteNonQueryAsync(token) > 0)
        {
            decimal previous = 0;
            using (var read = ActivityCommand(connection, transaction,
                "SELECT KnownIsk FROM SystemActivity WHERE SolarSystemId=$s AND BucketStartUtcTicks=$b;",
                ("$s", system), ("$b", bucket)))
            {
                var stored = await read.ExecuteScalarAsync(token);
                if (stored is string text) previous = ParseIsk(text);
            }
            using var update = ActivityCommand(connection, transaction, """
                INSERT INTO SystemActivity VALUES($s,$b,1,$pvp,$isk,$known,$unknown,$attackers,$time,$time)
                ON CONFLICT(SolarSystemId,BucketStartUtcTicks) DO UPDATE SET
                    KillmailCount=KillmailCount+1, ConfirmedPvpKills=ConfirmedPvpKills+$pvp,
                    KnownIsk=$isk, KnownValues=KnownValues+$known, UnknownValues=UnknownValues+$unknown,
                    AttackerEntries=AttackerEntries+$attackers,
                    FirstUtcTicks=MIN(FirstUtcTicks,$time), LastUtcTicks=MAX(LastUtcTicks,$time);
                INSERT INTO ShipLossActivity VALUES($s,$b,$ship,1)
                ON CONFLICT(SolarSystemId,BucketStartUtcTicks,VictimShipTypeId) DO UPDATE SET LossCount=LossCount+1;
                """, ("$s", system), ("$b", bucket), ("$pvp", pvp ? 1 : 0),
                ("$isk", AddExact(previous, value ?? 0).ToString("G29", CultureInfo.InvariantCulture)),
                ("$known", value.HasValue ? 1 : 0), ("$unknown", value.HasValue ? 0 : 1),
                ("$attackers", attackers), ("$time", occurred.UtcTicks), ("$ship", ship));
            await update.ExecuteNonQueryAsync(token);
        }
        using var mark = ActivityCommand(connection, transaction,
            "UPDATE Killmail SET ActivityAggregated=1 WHERE KillmailId=$id;", ("$id", id));
        await mark.ExecuteNonQueryAsync(token);
    }

    public async Task<int> CatchUpActivityAsync(int maxRows, CancellationToken cancellationToken = default)
    {
        if (maxRows is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            using var transaction = connection.BeginTransaction();
            using var read = ActivityCommand(connection, transaction, """
                SELECT k.KillmailId,k.SolarSystemId,k.OccurredAtUtcTicks,k.VictimShipTypeId,k.TotalValue,k.AttackerCount,
                    (k.VictimCharacterId > 0 AND EXISTS(SELECT 1 FROM KillmailParticipant p
                     WHERE p.KillmailId=k.KillmailId AND p.CharacterId>0 AND p.CharacterId<>k.VictimCharacterId))
                FROM Killmail k WHERE ActivityAggregated=0 ORDER BY KillmailId LIMIT $limit;
                """, ("$limit", maxRows));
            var pending = new List<(long Id, long System, long Time, int Ship, decimal? Value, int Attackers, bool Pvp)>();
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken))
                    pending.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3),
                        ReadNullableDecimal(reader, 4), reader.GetInt32(5), !reader.IsDBNull(6) && reader.GetBoolean(6)));
            foreach (var row in pending)
                await AggregateAsync(connection, transaction, row.Id, row.System, FromTicks(row.Time), row.Ship,
                    row.Value, row.Attackers, row.Pvp, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return pending.Count;
        }
        finally { _gate.Release(); }
    }

    public async Task<int> DeleteActivityBeforeAsync(DateTimeOffset cutoffUtc, int maxRows, CancellationToken cancellationToken = default)
    {
        if (maxRows is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        var floor = ActivityRange.BucketStart(cutoffUtc).UtcTicks;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var connection = RequireConnection();
            using var transaction = connection.BeginTransaction();
            using var checkpoint = ActivityCommand(connection, transaction, """
                INSERT INTO CoverageCheckpoint(Name,Value,SessionId) VALUES('ActivityRetentionFloor',$floor,'')
                ON CONFLICT(Name) DO UPDATE SET Value=MAX(Value,excluded.Value);
                """, ("$floor", floor));
            await checkpoint.ExecuteNonQueryAsync(cancellationToken);
            using var delete = ActivityCommand(connection, transaction, """
                DELETE FROM SystemActivity WHERE rowid IN
                  (SELECT rowid FROM SystemActivity WHERE BucketStartUtcTicks<$floor ORDER BY BucketStartUtcTicks LIMIT $limit);
                """, ("$floor", floor), ("$limit", maxRows));
            var count = await delete.ExecuteNonQueryAsync(cancellationToken);
            using var prune = ActivityCommand(connection, transaction, """
                DELETE FROM ActivityDedup WHERE KillmailId IN
                  (SELECT KillmailId FROM ActivityDedup WHERE BucketStartUtcTicks<$floor ORDER BY BucketStartUtcTicks LIMIT $limit);
                DELETE FROM CollectionSession WHERE SessionId IN
                  (SELECT SessionId FROM CollectionSession WHERE LastHeartbeatUtcTicks<$floor AND
                   (EndUtcTicks IS NOT NULL OR Interrupted=1) ORDER BY LastHeartbeatUtcTicks LIMIT $limit);
                """, ("$floor", floor), ("$limit", maxRows));
            var pruned = await prune.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Math.Max(count, pruned);
        }
        finally { _gate.Release(); }
    }

    public Task StartCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => SessionWriteAsync(sessionId, now, """
            UPDATE CollectionSession SET Interrupted=1 WHERE EndUtcTicks IS NULL AND SessionId<>$id;
            INSERT OR IGNORE INTO CollectionSession(SessionId,StartUtcTicks,LastHeartbeatUtcTicks) VALUES($id,$now,$now);
            """, cancellationToken);

    public Task HeartbeatCollectionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => SessionWriteAsync(sessionId, now, """
            UPDATE CollectionSession SET LastHeartbeatUtcTicks=MAX(LastHeartbeatUtcTicks,$now)
            WHERE SessionId=$id AND EndUtcTicks IS NULL AND Interrupted=0;
            """, cancellationToken);

    public Task EndCollectionSessionAsync(string sessionId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => SessionWriteAsync(sessionId, now,
            "UPDATE CollectionSession SET EndUtcTicks=$now WHERE SessionId=$id AND EndUtcTicks IS NULL;", cancellationToken);

    private async Task SessionWriteAsync(string sessionId, DateTimeOffset now, string sql, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await _gate.WaitAsync(token);
        try
        {
            using var transaction = RequireConnection().BeginTransaction();
            using var command = ActivityCommand(RequireConnection(), transaction, sql, ("$id", sessionId), ("$now", now.UtcTicks));
            await command.ExecuteNonQueryAsync(token);
            await transaction.CommitAsync(token);
        }
        finally { _gate.Release(); }
    }

    private static ActivityMetrics Add(ActivityMetrics a, ActivityMetrics b) => new(
        checked(a.KillmailCount+b.KillmailCount), checked(a.ConfirmedPvpKills+b.ConfirmedPvpKills), checked(a.OtherKills+b.OtherKills),
        AddExact(a.KnownIskDestroyed,b.KnownIskDestroyed), a.KnownValueCount+b.KnownValueCount,
        a.UnknownValueCount+b.UnknownValueCount, a.AttackerEntries+b.AttackerEntries,
        a.FirstOccurrenceUtc is null ? b.FirstOccurrenceUtc : b.FirstOccurrenceUtc < a.FirstOccurrenceUtc ? b.FirstOccurrenceUtc : a.FirstOccurrenceUtc,
        a.LastOccurrenceUtc is null ? b.LastOccurrenceUtc : b.LastOccurrenceUtc > a.LastOccurrenceUtc ? b.LastOccurrenceUtc : a.LastOccurrenceUtc);

    // Decimal addition can silently discard low-order digits before reaching MaxValue.
    // Reject that case as well as overflow, so neither persisted nor returned money is rounded.
    private static decimal AddExact(decimal left, decimal right)
    {
        if (left == 0) return right;
        if (right == 0) return left;
        var sum = checked(left + right);
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(left, bits);
        var leftScale = (bits[3] >> 16) & 255;
        decimal.GetBits(right, bits);
        var rightScale = (bits[3] >> 16) & 255;
        decimal.GetBits(sum, bits);
        // Addition is exact if the result retained both operands' scale. Only the
        // rare scale-reduction case needs the arbitrary-precision comparison.
        if (((bits[3] >> 16) & 255) >= Math.Max(leftScale, rightScale)) return sum;
        static (BigInteger Units, int Scale) Parts(decimal value)
        {
            Span<int> bits = stackalloc int[4];
            decimal.GetBits(value, bits);
            var units = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
            return ((bits[3] & int.MinValue) != 0 ? -units : units, (bits[3] >> 16) & 255);
        }
        var a = Parts(left); var b = Parts(right);
        var scale = Math.Max(a.Scale, b.Scale);
        var exact = a.Units * BigInteger.Pow(10, scale-a.Scale) + b.Units * BigInteger.Pow(10, scale-b.Scale);
        var actual = Parts(sum);
        var compareScale = Math.Max(scale, actual.Scale);
        if (exact * BigInteger.Pow(10, compareScale-scale) != actual.Units * BigInteger.Pow(10, compareScale-actual.Scale))
            throw new OverflowException("Exact ISK total exceeds decimal precision.");
        return sum;
    }

    private static async Task<ActivityCoverage> ReadCoverageAsync(SqliteConnection connection, SqliteTransaction transaction,
        ActivityRange range, CancellationToken token)
    {
        using var command = ActivityCommand(connection, transaction, """
            SELECT StartUtcTicks,LastHeartbeatUtcTicks FROM CollectionSession
            WHERE StartUtcTicks<$to AND LastHeartbeatUtcTicks>$from ORDER BY StartUtcTicks;
            """, ("$from", range.FromUtc.UtcTicks), ("$to", range.ToUtc.UtcTicks));
        long observed = 0, end = range.FromUtc.UtcTicks;
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
            {
                var start = Math.Max(end, Math.Max(range.FromUtc.UtcTicks, reader.GetInt64(0)));
                var next = Math.Min(range.ToUtc.UtcTicks, reader.GetInt64(1));
                if (next > start) observed += next-start;
                end = Math.Max(end, next);
            }
        command.CommandText = "SELECT COALESCE(SUM(Count),0) FROM CollectionGap WHERE RecordedAtUtcTicks >= $from AND RecordedAtUtcTicks < $to;";
        var gaps = Convert.ToInt64(await command.ExecuteScalarAsync(token));
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Killmail WHERE ActivityAggregated=0);";
        var pending = Convert.ToInt64(await command.ExecuteScalarAsync(token)) != 0;
        command.CommandText = "SELECT COALESCE((SELECT Value FROM CoverageCheckpoint WHERE Name='ActivityRetentionFloor'),0);";
        var expired = Convert.ToInt64(await command.ExecuteScalarAsync(token)) > range.FromUtc.UtcTicks;
        command.CommandText = "SELECT COALESCE((SELECT Value FROM CoverageCheckpoint WHERE Name='GapRetentionFloor'),0);";
        var gapExpired = Convert.ToInt64(await command.ExecuteScalarAsync(token)) > range.FromUtc.UtcTicks;
        var state = observed == 0 ? ActivityCoverageState.Unknown :
            observed < range.ToUtc.UtcTicks-range.FromUtc.UtcTicks || gaps > 0 || pending || expired || gapExpired ? ActivityCoverageState.Partial : ActivityCoverageState.Observed;
        return new(state, TimeSpan.FromTicks(observed), gaps, pending, AggregateHistoryExpired: expired, GapHistoryExpired: gapExpired);
    }
}
