using System.Globalization;
using System.Text.Json;
using System.Collections.Frozen;
using Hisa.Core;
using Hisa.Core.Models.Intelligence;

namespace Hisa.Data.Database;

public sealed partial class SqliteIntelligenceHistoryRepository
{
    private readonly SemaphoreSlim _combatProcessing = new(1, 1);
    public async Task<int> ProcessCombatAsync(DateTimeOffset now, TimeSpan retention, CombatDetectionOptions options, CancellationToken cancellationToken = default)
    {
        await _combatProcessing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ProcessCombatPartitionAsync(now, retention, options, cancellationToken).ConfigureAwait(false); }
        finally { _combatProcessing.Release(); }
    }
    // Rebuild one affected system / UTC six-hour partition, never a full-system history scan.
    // The read/compute phase uses the same bounded WAL reader admission as historical activity.
    private async Task<int> ProcessCombatPartitionAsync(DateTimeOffset now, TimeSpan retention, CombatDetectionOptions options, CancellationToken cancellationToken)
    {
        if (retention <= TimeSpan.Zero || retention > TimeSpan.FromDays(30)) throw new ArgumentOutOfRangeException(nameof(retention));
        var work = await ReadActivityAsync(async (connection, transaction, token) =>
        {
            using var dirty = ActivityCommand(connection, transaction, "SELECT SystemId,PartitionTicks,Revision FROM CombatDirty ORDER BY PartitionTicks,SystemId LIMIT 1;");
            long system, ticks, revision;
            using (var reader = await dirty.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token)) return null;
                system = reader.GetInt64(0); ticks = reader.GetInt64(1); revision = reader.GetInt64(2);
            }
            var partition = FromTicks(ticks);
            using var floor = ActivityCommand(connection, transaction, "SELECT COALESCE((SELECT Value FROM CoverageCheckpoint WHERE Name='CombatRawFloor'),0);");
            var rawFloor = Convert.ToInt64(await floor.ExecuteScalarAsync(token));
            if (ticks < rawFloor || partition < now.AddHours(-48) || partition.AddHours(6) <= now - retention)
                return new CombatWork(system, ticks, revision, [], "Detailed source partition expired; retained findings were not reconstructed from incomplete records.");
            using var rows = ActivityCommand(connection, transaction, """
                SELECT k.KillmailId,k.OccurredAtUtcTicks,k.VictimShipTypeId,k.TotalValue,
                    CASE WHEN k.VictimCharacterId > 0 AND EXISTS(SELECT 1 FROM KillmailParticipant p
                        WHERE p.KillmailId=k.KillmailId AND p.CharacterId>0 AND p.CharacterId<>k.VictimCharacterId) THEN 1 ELSE 0 END
                FROM Killmail k WHERE k.SolarSystemId=$system AND k.OccurredAtUtcTicks >= $from AND k.OccurredAtUtcTicks < $to
                ORDER BY k.OccurredAtUtcTicks,k.KillmailId LIMIT 4097;
                """, ("$system", system), ("$from", ticks), ("$to", partition.AddHours(6).UtcTicks));
            var input = new List<CombatInput>();
            using (var reader = await rows.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token))
                {
                    token.ThrowIfCancellationRequested();
                    input.Add(new(reader.GetInt64(0), system, FromTicks(reader.GetInt64(1)), reader.GetInt32(2), reader.IsDBNull(3) ? null : ParseIsk(reader.GetString(3)), reader.GetInt32(4) != 0));
                }
            // Never publish a seemingly complete classification from a truncated high-volume partition.
            if (input.Count > 4096) return new CombatWork(system, ticks, revision, [], "Analysis capacity exceeded (4096 losses / system / six hours); classification unavailable.");
            var coverage = await ReadCoverageAsync(connection, transaction, new(partition, partition.AddHours(6)), token);
            try { return new CombatWork(system, ticks, revision, CombatDetector.Detect(input, system, partition, coverage, now, options, addExact: AddExact, cancellationToken: token), null); }
            catch (OverflowException) { return new CombatWork(system, ticks, revision, [], "Exact monetary aggregation exceeded decimal capacity; classification unavailable."); }
        }, cancellationToken).ConfigureAwait(false);
        var summaries = work?.Events.Select(item => (Item: item, Json: JsonSerializer.Serialize(item))).ToArray();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = RequireConnection();
            using var transaction = connection.BeginTransaction();
            // Retention is bounded on each background iteration, including when there is no dirty work.
            using (var prune = ActivityCommand(connection, transaction, """
                DELETE FROM CombatEvent WHERE Id IN (SELECT Id FROM CombatEvent WHERE LastTicks < $floor LIMIT 512);
                DELETE FROM CombatAnalysisIssue WHERE rowid IN (SELECT rowid FROM CombatAnalysisIssue WHERE PartitionTicks < $issueFloor LIMIT 512);
                """, ("$floor", (now - retention).UtcTicks), ("$issueFloor", Math.Max(0, (now - retention).UtcTicks - TimeSpan.FromHours(6).Ticks)))) await prune.ExecuteNonQueryAsync(cancellationToken);
            if (work is null) { transaction.Commit(); return 0; }
            using var check = ActivityCommand(connection, transaction, "SELECT Revision FROM CombatDirty WHERE SystemId=$system AND PartitionTicks=$partition;", ("$system", work.System), ("$partition", work.Ticks));
            if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) != work.Revision) { transaction.Commit(); return 1; }
            using var currentFloor = ActivityCommand(connection, transaction, "SELECT COALESCE((SELECT Value FROM CoverageCheckpoint WHERE Name='CombatRawFloor'),0);");
            var expired = work.Issue?.StartsWith("Detailed source", StringComparison.Ordinal) == true || work.Ticks < Convert.ToInt64(await currentFloor.ExecuteScalarAsync(cancellationToken));
            if (!expired)
            {
                using var remove = ActivityCommand(connection, transaction, "DELETE FROM CombatEvent WHERE SystemId=$system AND PartitionTicks=$partition;", ("$system", work.System), ("$partition", work.Ticks));
                await remove.ExecuteNonQueryAsync(cancellationToken);
                foreach (var prepared in summaries!)
                {
                    var item = prepared.Item;
                    using var insert = ActivityCommand(connection, transaction, """
                        INSERT INTO CombatEvent VALUES($id,$system,$partition,$first,$last,$class,$kills,$isk,$summary);
                        """, ("$id", item.Id), ("$system", item.SolarSystemId), ("$partition", work.Ticks), ("$first", item.FirstUtc.UtcTicks),
                        ("$last", item.LastUtc.UtcTicks), ("$class", (int)item.Classification), ("$kills", item.KillmailCount),
                        ("$isk", item.KnownIskDestroyed.ToString("G29", CultureInfo.InvariantCulture)), ("$summary", prepared.Json));
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            using var finish = ActivityCommand(connection, transaction, """
                DELETE FROM CombatAnalysisIssue WHERE SystemId=$system AND PartitionTicks=$partition;
                DELETE FROM CombatDirty WHERE SystemId=$system AND PartitionTicks=$partition;
                """, ("$system", work.System), ("$partition", work.Ticks));
            await finish.ExecuteNonQueryAsync(cancellationToken);
            if (work.Issue is not null)
            {
                using var issue = ActivityCommand(connection, transaction, "INSERT INTO CombatAnalysisIssue VALUES($system,$partition,$reason);", ("$system", work.System), ("$partition", work.Ticks), ("$reason", work.Issue));
                await issue.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return 1;
        }
        finally { _gate.Release(); }
    }

    private sealed record CombatWork(long System, long Ticks, long Revision, IReadOnlyList<CombatEngagement> Events, string? Issue);

    public Task<CombatMapSnapshot> GetCombatMapAsync(ActivityRange range, CancellationToken cancellationToken = default)
    {
        range = range.Effective();
        return ReadActivityAsync(async (connection, transaction, token) =>
        {
            using var command = ActivityCommand(connection, transaction, """
                SELECT SystemId, MAX(CASE WHEN Classification=0 THEN 1 WHEN Classification IN(1,2) THEN 2 ELSE 0 END)
                    + MAX(CASE WHEN Classification=3 THEN 4 ELSE 0 END)
                FROM CombatEvent WHERE FirstTicks >= $earliest AND FirstTicks < $to AND LastTicks >= $from GROUP BY SystemId ORDER BY SystemId LIMIT 10001;
                """, ("$earliest", Math.Max(0, range.FromUtc.UtcTicks - TimeSpan.FromHours(6).Ticks)), ("$from", range.FromUtc.UtcTicks), ("$to", range.ToUtc.UtcTicks));
            var markers = new Dictionary<long, byte>();
            using (var reader = await command.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token)) markers.Add(reader.GetInt64(0), (byte)reader.GetInt32(1));
            var truncated = markers.Count > 10000;
            if (truncated) markers.Remove(markers.Keys.Last());
            using var pending = ActivityCommand(connection, transaction, """
                SELECT EXISTS(SELECT 1 FROM CombatDirty WHERE PartitionTicks < $to AND PartitionTicks+216000000000 > $from)
                    OR EXISTS(SELECT 1 FROM CombatAnalysisIssue WHERE PartitionTicks < $to AND PartitionTicks+216000000000 > $from);
                """, ("$from", range.FromUtc.UtcTicks), ("$to", range.ToUtc.UtcTicks));
            return new CombatMapSnapshot(range, markers.ToFrozenDictionary(), truncated || Convert.ToInt32(await pending.ExecuteScalarAsync(token)) != 0);
        }, cancellationToken);
    }

    public Task<CombatPage> GetCombatEventsAsync(CombatQuery query, CancellationToken cancellationToken = default)
    {
        var range = query.Range.Effective();
        if (query.Limit is < 1 or > 200 || query.MinimumKills < 2 || query.MinimumKnownIsk < 0 || query.SystemId <= 0 ||
            query.Classification is { } kind && !Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(query));
        return ReadActivityAsync(async (connection, transaction, token) =>
        {
            using var command = ActivityCommand(connection, transaction, """
                SELECT Summary FROM CombatEvent WHERE FirstTicks >= $earliest AND FirstTicks < $to AND LastTicks >= $from
                    AND ($class IS NULL OR Classification=$class)
                    AND ($battles=0 OR Classification IN (1,2))
                    AND Kills >= $kills AND ($isk IS NULL OR KnownIsk COLLATE HISA_DECIMAL >= $isk COLLATE HISA_DECIMAL)
                    AND ($cursor IS NULL OR FirstTicks > $cursor OR (FirstTicks=$cursor AND Id > $id))
                ORDER BY FirstTicks,Id LIMIT $limit;
                """, ("$earliest", Math.Max(0, range.FromUtc.UtcTicks - TimeSpan.FromHours(6).Ticks)), ("$from", range.FromUtc.UtcTicks), ("$to", range.ToUtc.UtcTicks),
                ("$class", query.Classification is { } c ? (object)(int)c : DBNull.Value), ("$battles", query.BattlesOnly ? 1 : 0), ("$kills", query.MinimumKills),
                ("$isk", (object?)query.MinimumKnownIsk?.ToString("G29", CultureInfo.InvariantCulture) ?? DBNull.Value),
                ("$cursor", (object?)query.After?.FirstUtc.UtcTicks ?? DBNull.Value), ("$id", query.After?.Id ?? ""), ("$limit", query.Limit + 1));
            if (query.SystemId is { } systemId)
            {
                command.CommandText = command.CommandText.Replace("WHERE FirstTicks", "WHERE SystemId=$system AND FirstTicks", StringComparison.Ordinal);
                command.Parameters.AddWithValue("$system", systemId);
            }
            var events = new List<CombatEngagement>();
            using (var reader = await command.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token)) events.Add(JsonSerializer.Deserialize<CombatEngagement>(reader.GetString(0))!);
            var next = events.Count > query.Limit ? new CombatCursor(events[query.Limit - 1].FirstUtc, events[query.Limit - 1].Id) : null;
            if (next is not null) events.RemoveAt(events.Count - 1);
            using var pending = ActivityCommand(connection, transaction, """
                SELECT EXISTS(SELECT 1 FROM CombatDirty WHERE PartitionTicks < $to AND PartitionTicks+216000000000 > $from AND ($system IS NULL OR SystemId=$system))
                    OR EXISTS(SELECT 1 FROM CombatAnalysisIssue WHERE PartitionTicks < $to AND PartitionTicks+216000000000 > $from AND ($system IS NULL OR SystemId=$system));
                """, ("$from", range.FromUtc.UtcTicks), ("$to", range.ToUtc.UtcTicks), ("$system", (object?)query.SystemId ?? DBNull.Value));
            var coverage = await ReadCoverageAsync(connection, transaction, range, token);
            // Attach current collection evidence, so a later recorded interruption cannot leave a stale Observed claim.
            return new CombatPage(events.Select(e => e with { Coverage = ConservativeCoverage(e.Coverage, coverage) }).ToArray(), next, Convert.ToInt32(await pending.ExecuteScalarAsync(token)) != 0, coverage);
        }, cancellationToken);
    }

    private static ActivityCoverage ConservativeCoverage(ActivityCoverage eventEvidence, ActivityCoverage selectedWindow) => new(
        (ActivityCoverageState)Math.Min((int)eventEvidence.State, (int)selectedWindow.State),
        eventEvidence.ObservedDuration < selectedWindow.ObservedDuration ? eventEvidence.ObservedDuration : selectedWindow.ObservedDuration,
        Math.Max(eventEvidence.KnownGapCount, selectedWindow.KnownGapCount), eventEvidence.CatchUpPending || selectedWindow.CatchUpPending,
        false, eventEvidence.AggregateHistoryExpired || selectedWindow.AggregateHistoryExpired, eventEvidence.GapHistoryExpired || selectedWindow.GapHistoryExpired);

    public Task<CombatEngagement?> GetCombatEventAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 150) throw new ArgumentException("Invalid event identifier.", nameof(id));
        return ReadActivityAsync(async (connection, transaction, token) =>
        {
            using var command = ActivityCommand(connection, transaction, "SELECT Summary FROM CombatEvent WHERE Id=$id;", ("$id", id));
            var json = await command.ExecuteScalarAsync(token) as string;
            if (json is null) return null;
            var item = JsonSerializer.Deserialize<CombatEngagement>(json)!;
            var from = ActivityRange.BucketStart(item.FirstUtc);
            var to = ActivityRange.BucketStart(item.LastUtc).AddMinutes(5);
            return item with { Coverage = await ReadCoverageAsync(connection, transaction, new(from, to), token) };
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CombatSystemSummary>> GetCombatSystemsAsync(ActivityRange range, int limit = 10000, CancellationToken cancellationToken = default)
    {
        range = range.Effective();
        if (limit is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(limit));
        return ReadActivityAsync<IReadOnlyList<CombatSystemSummary>>(async (connection, transaction, token) =>
        {
            using var command = ActivityCommand(connection, transaction, """
                SELECT SystemId,COUNT(*),SUM(Kills) FROM CombatEvent WHERE FirstTicks >= $earliest AND FirstTicks < $to AND LastTicks >= $from AND Classification<>3
                GROUP BY SystemId ORDER BY SUM(Kills) DESC,SystemId LIMIT $limit;
                """, ("$earliest", Math.Max(0, range.FromUtc.UtcTicks - TimeSpan.FromHours(6).Ticks)), ("$from", range.FromUtc.UtcTicks), ("$to", range.ToUtc.UtcTicks), ("$limit", limit));
            var result = new List<CombatSystemSummary>();
            using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) result.Add(new(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2)));
            return result;
        }, cancellationToken);
    }
}
