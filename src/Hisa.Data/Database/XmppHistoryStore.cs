using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Hisa.Core.Models;
using Microsoft.Data.Sqlite;

namespace Hisa.Data.Database;

public sealed class XmppHistoryStore(string connectionString) : IDisposable
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private const string FeedClearedCheckpoint = "$feed-cleared";
    private const string FeedCutoffSql = """
        COALESCE((SELECT Stamp FROM XmppCheckpoint WHERE Account = $account AND Archive = $feedCleared), $noCutoff)
        """;
    private const string FeedAlertSql = "json_extract(Value, '$.ShowInFleetFeed') = 1";
    private const string MainFeedSql = $"""
        ((Kind = $dirKind AND Address = $dirJid AND json_extract(Value, '$.Outgoing') = 0) OR {FeedAlertSql})
        """;
    private readonly SemaphoreSlim _databaseGate = new(1);
    private SqliteConnection? _database;
    private int _disposed;

    public async Task InitializeAsync(CancellationToken token)
    {
        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var command = db.CreateCommand();
        command.CommandText = SqliteDatabaseInitializer.SettingsSchema + "\n" +
            MessageSchema("main") + """
            CREATE TABLE IF NOT EXISTS XmppCheckpoint (
                Account TEXT NOT NULL, Archive TEXT NOT NULL, Stamp INTEGER NOT NULL,
                PRIMARY KEY (Account, Archive)
            );
            """;
        await command.ExecuteNonQueryAsync(token);
        await MergeSavedBroadcastCopiesAsync(db, token);
    }

    private static async Task MergeSavedBroadcastCopiesAsync(SqliteConnection db, CancellationToken token)
    {
        const string migration = "xmpp.broadcast-identity.v2";
        using var transaction = db.BeginTransaction();
        using var marker = Create(db, transaction,
            "INSERT OR IGNORE INTO Settings (Key, Value) VALUES ($key, 'true')", ("$key", migration));
        if (await marker.ExecuteNonQueryAsync(token) == 0) return;

        var broadcasts = new List<(string Account, string Key, XmppMessage Message)>();
        using (var select = Create(db, transaction, """
            SELECT Account, Id, Unread, Value FROM XmppMessage
            WHERE Kind = $direct AND Address LIKE 'directorbot@%' ORDER BY Id
            """, ("$direct", (int)XmppConversationKind.Direct)))
        using (var reader = await select.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var account = reader.GetString(0);
                var message = JsonSerializer.Deserialize<XmppMessage>(reader.GetString(3));
                if (message is not null && BroadcastKey(account, message) is { } key)
                    broadcasts.Add((account, key, message with { LocalId = reader.GetInt64(1), Unread = reader.GetBoolean(2) }));
            }
        }

        foreach (var copies in broadcasts.GroupBy(row => (row.Account, row.Key)))
        {
            var first = copies.First();
            var kept = first.Message;
            using var addKey = Create(db, transaction, """
                INSERT OR IGNORE INTO XmppMessageKey (Account, Key, MessageId) VALUES ($account, $key, $id)
                """, ("$account", first.Account), ("$key", first.Key), ("$id", kept.LocalId));
            await addKey.ExecuteNonQueryAsync(token);

            foreach (var (_, _, duplicate) in copies.Skip(1))
            {
                kept = kept with
                {
                    Unread = kept.Unread && duplicate.Unread,
                    ShowInFleetFeed = kept.ShowInFleetFeed || duplicate.ShowInFleetFeed,
                    IsFleetPing = kept.IsFleetPing || duplicate.IsFleetPing,
                    IsDelveBping = kept.IsDelveBping || duplicate.IsDelveBping,
                    Highlight = kept.Highlight || duplicate.Highlight,
                    Mention = kept.Mention || duplicate.Mention,
                    FeedTitle = kept.FeedTitle ?? duplicate.FeedTitle,
                    FeedSubtitle = kept.FeedSubtitle ?? duplicate.FeedSubtitle,
                    Html = kept.Html ?? duplicate.Html,
                    IdentityKeys = kept.IdentityKeys.Concat(duplicate.IdentityKeys).Distinct().ToArray()
                };
                using var merge = Create(db, transaction, """
                    UPDATE XmppMessageKey SET MessageId = $kept WHERE Account = $account AND MessageId = $duplicate;
                    UPDATE XmppMessage SET Unread = $unread, Value = $value WHERE Id = $kept;
                    DELETE FROM XmppMessage WHERE Id = $duplicate;
                    """, ("$account", first.Account), ("$kept", kept.LocalId), ("$duplicate", duplicate.LocalId),
                    ("$unread", kept.Unread ? 1 : 0), ("$value", JsonSerializer.Serialize(kept)));
                await merge.ExecuteNonQueryAsync(token);
            }
        }
        await transaction.CommitAsync(token);
    }

    private static string? BroadcastKey(string account, XmppMessage message)
    {
        var directorbot = XmppAddresses.Directorbot(account);
        if (message.Outgoing || message.Conversation != directorbot ||
            XmppAddresses.Bare(message.Sender) != directorbot.Jid ||
            XmppFleetPingParsing.TimestampedBroadcastBody(message.Body) is not { } body)
            return null;
        return "directorbot-broadcast:v2:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }

    private static string MessageSchema(string database) => $"""
        CREATE TABLE IF NOT EXISTS {database}.XmppMessage (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, Account TEXT NOT NULL, Kind INTEGER NOT NULL,
            Address TEXT NOT NULL, Stamp INTEGER NOT NULL, Unread INTEGER NOT NULL, Value TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS {database}.IX_XmppMessage_Account_Stamp ON XmppMessage(Account, Stamp);
        CREATE INDEX IF NOT EXISTS {database}.IX_XmppMessage_Conversation ON XmppMessage(Account, Kind, Address, Stamp);
        CREATE TABLE IF NOT EXISTS {database}.XmppMessageKey (
            Account TEXT NOT NULL, Key TEXT NOT NULL,
            MessageId INTEGER NOT NULL REFERENCES XmppMessage(Id) ON DELETE CASCADE,
            PRIMARY KEY (Account, Key)
        );
        CREATE INDEX IF NOT EXISTS {database}.IX_XmppMessageKey_MessageId ON XmppMessageKey(MessageId);
        CREATE TABLE IF NOT EXISTS {database}.XmppHistoryCopy (
            MessageId INTEGER PRIMARY KEY REFERENCES XmppMessage(Id) ON DELETE CASCADE,
            Account TEXT NOT NULL, Address TEXT NOT NULL, Fingerprint TEXT NOT NULL,
            Sources INTEGER NOT NULL, HasIds INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS {database}.IX_XmppHistoryCopy_Fingerprint ON XmppHistoryCopy(Account, Address, Fingerprint);
        """;

    public async Task<bool> InsertAsync(string account, XmppMessage message, CancellationToken token,
        XmppHistoryDelivery delivery = XmppHistoryDelivery.None, DateTimeOffset? readAtJoin = null)
    {
        var older = message.Timestamp < DateTimeOffset.UtcNow - Retention;
        if (older && delivery == XmppHistoryDelivery.None) return false;

        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var transaction = db.BeginTransaction();
        var stamp = message.Timestamp.ToUnixTimeMilliseconds();
        if (message.Recovered)
        {
            using var cleared = Create(db, transaction, """
                SELECT Stamp FROM XmppCheckpoint WHERE Account = $account AND Archive = '$cleared'
                """, ("$account", account));
            if (await cleared.ExecuteScalarAsync(token) is long cutoff && stamp <= cutoff) return false;
        }

        var database = older ? "roomSession" : "main";
        var fingerprint = message.Conversation.Kind != XmppConversationKind.Room ? null : Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message.Sender, message.Body, Stamp = stamp }))));
        var keys = fingerprint is not null && message.JoinHistoryOrdinal is { } ordinal
            ? message.IdentityKeys.Append($"muc-join:{message.Conversation.Jid}:{fingerprint}:{ordinal}")
            : message.IdentityKeys;
        if (BroadcastKey(account, message) is { } broadcastKey)
            keys = keys.Append(broadcastKey);
        long? existingId = null;
        foreach (var key in keys)
        {
            using var find = Create(db, transaction, """
                SELECT MessageId FROM main.XmppMessageKey WHERE Account = $account AND Key = $key
                UNION ALL SELECT -MessageId FROM roomSession.XmppMessageKey WHERE Account = $account AND Key = $key
                LIMIT 1
                """, ("$account", account), ("$key", key));
            if (await find.ExecuteScalarAsync(token) is not long id) continue;
            database = id < 0 ? "roomSession" : "main";
            existingId = Math.Abs(id);
            break;
        }

        var source = delivery == XmppHistoryDelivery.None ? 4 : (int)delivery;
        var hasIds = message.IdentityKeys.Count > 0;
        if (fingerprint is not null && existingId is null)
        {
            // Match one copy from the other source. Repeated identical messages
            // within either source keep their individual transcript rows.
            using var match = Create(db, transaction, $"""
                SELECT MessageId FROM {database}.XmppHistoryCopy
                WHERE Account = $account AND Address = $address AND Fingerprint = $fingerprint
                  AND (Sources & $source) = 0 AND (HasIds = 0 OR $ids = 0)
                ORDER BY MessageId LIMIT 1
                """, ("$account", account), ("$address", message.Conversation.Jid),
                ("$fingerprint", fingerprint), ("$source", source), ("$ids", hasIds ? 1 : 0));
            existingId = await match.ExecuteScalarAsync(token) as long?;
        }

        var inserted = existingId is null;
        if (existingId is null)
        {
            var unread = message.Unread;
            if (delivery != XmppHistoryDelivery.None && !message.Outgoing)
            {
                if (readAtJoin is { } position)
                    unread = position != DateTimeOffset.MinValue && stamp >= position.ToUnixTimeMilliseconds();
                else
                {
                    using var seen = Create(db, transaction, """
                        SELECT Stamp FROM XmppCheckpoint WHERE Account = $account AND Archive = $roomRead
                        """, ("$account", account), ("$roomRead", ReadKey(message.Conversation)));
                    unread = await seen.ExecuteScalarAsync(token) is long lastRead && stamp >= lastRead;
                }
            }

            using var insert = Create(db, transaction, $"""
                INSERT INTO {database}.XmppMessage (Account, Kind, Address, Stamp, Unread, Value)
                VALUES ($account, $kind, $address, $stamp, $unread, $value) RETURNING Id
                """, ("$account", account), ("$kind", (int)message.Conversation.Kind),
                ("$address", message.Conversation.Jid), ("$stamp", stamp),
                ("$unread", unread ? 1 : 0), ("$value", JsonSerializer.Serialize(message)));
            existingId = (long)(await insert.ExecuteScalarAsync(token))!;
        }

        foreach (var key in keys)
        {
            using var insertKey = Create(db, transaction,
                $"INSERT OR IGNORE INTO {database}.XmppMessageKey (Account, Key, MessageId) VALUES ($account, $key, $id)",
                ("$account", account), ("$key", key), ("$id", existingId.Value));
            await insertKey.ExecuteNonQueryAsync(token);
        }

        if (fingerprint is not null)
        {
            using var copy = Create(db, transaction, $"""
                INSERT INTO {database}.XmppHistoryCopy (MessageId, Account, Address, Fingerprint, Sources, HasIds)
                VALUES ($id, $account, $address, $fingerprint, $source, $ids)
                ON CONFLICT (MessageId) DO UPDATE SET Sources = Sources | excluded.Sources,
                    HasIds = MAX(HasIds, excluded.HasIds)
                """, ("$id", existingId.Value), ("$account", account), ("$address", message.Conversation.Jid),
                ("$fingerprint", fingerprint), ("$source", source), ("$ids", hasIds ? 1 : 0));
            await copy.ExecuteNonQueryAsync(token);
        }

        await transaction.CommitAsync(token);
        return inserted;
    }

    public async Task<IReadOnlyList<XmppMessage>> ReadAsync(string account, XmppConversation? conversation,
        bool pingsOnly, DateTimeOffset? before, CancellationToken token = default, long beforeId = 0,
        XmppMessage? from = null)
    {
        var directorbot = XmppAddresses.Directorbot(account);
        if (pingsOnly) conversation = null;
        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var command = Create(db, null, $$"""
            SELECT Value, Unread, LocalId FROM (
                SELECT Value, Unread, Id AS LocalId, Stamp FROM main.XmppMessage
                WHERE Account = $account
                  AND (
                    ($pings = 0 AND ($kind IS NULL OR (Kind = $kind AND Address = $jid)))
                    OR ($pings = 1 AND {{MainFeedSql}})
                  )
                UNION ALL
                SELECT Value, Unread, -Id AS LocalId, Stamp FROM roomSession.XmppMessage
                WHERE Account = $account
                  AND (
                    ($pings = 0 AND ($kind IS NULL OR (Kind = $kind AND Address = $jid)))
                    OR ($pings = 1 AND {{FeedAlertSql}})
                  )
            )
            WHERE (Stamp < $before OR (Stamp = $before AND LocalId < $beforeId))
              AND (Stamp > $from OR (Stamp = $from AND LocalId >= $fromId))
              AND ($pings = 0 OR Stamp > {{FeedCutoffSql}})
            ORDER BY Stamp DESC, LocalId DESC LIMIT $limit
            """, ("$account", account), ("$kind", conversation is null ? DBNull.Value : (int)conversation.Kind),
            ("$jid", (object?)conversation?.Jid ?? DBNull.Value), ("$pings", pingsOnly ? 1 : 0),
            ("$dirKind", (int)XmppConversationKind.Direct), ("$dirJid", directorbot.Jid),
            ("$before", before?.ToUnixTimeMilliseconds() ?? long.MaxValue), ("$beforeId", beforeId),
            ("$from", from?.Timestamp.ToUnixTimeMilliseconds() ?? long.MinValue), ("$fromId", from?.LocalId ?? 0),
            ("$feedCleared", FeedClearedCheckpoint), ("$noCutoff", long.MinValue),
            ("$limit", from is null ? 250 : -1));
        using var reader = await command.ExecuteReaderAsync(token);
        var messages = new List<XmppMessage>();
        while (await reader.ReadAsync(token))
        {
            var message = JsonSerializer.Deserialize<XmppMessage>(reader.GetString(0))!;
            var loaded = message with { Unread = reader.GetBoolean(1), LocalId = reader.GetInt64(2) };
            // Re-detect structured fleet pings for older rows stored before parser-based IsFleetPing.
            if (pingsOnly && loaded.Conversation == directorbot && !loaded.Outgoing &&
                !loaded.IsFleetPing && XmppFleetPingParsing.TryParse(loaded.Body) is not null)
                loaded = loaded with { IsFleetPing = true, FeedTitle = string.IsNullOrWhiteSpace(loaded.FeedTitle) ? "Fleet ping" : loaded.FeedTitle };
            if (pingsOnly && loaded.Conversation == XmppAddresses.Delve911 && !loaded.Outgoing &&
                !loaded.IsDelveBping && XmppDelveBpingParsing.TryParse(loaded.Body) is { } delve)
                loaded = loaded with
                {
                    IsDelveBping = true,
                    FeedTitle = string.IsNullOrWhiteSpace(loaded.FeedTitle)
                        ? (delve.Title ?? "Delve911")
                        : loaded.FeedTitle
                };
            messages.Add(loaded);
        }
        messages.Reverse();
        return messages;
    }

    public async Task<IReadOnlyDictionary<XmppConversation, int>> CountsAsync(string account, CancellationToken token = default)
    {
        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var command = Create(db, null, """
            SELECT Kind, Address, SUM(Unread) FROM (
                SELECT Kind, Address, SUM(Unread) AS Unread FROM main.XmppMessage WHERE Account = $account GROUP BY Kind, Address
                UNION ALL
                SELECT Kind, Address, SUM(Unread) AS Unread FROM roomSession.XmppMessage WHERE Account = $account GROUP BY Kind, Address
            ) GROUP BY Kind, Address
            """, ("$account", account));
        using var reader = await command.ExecuteReaderAsync(token);
        var counts = new Dictionary<XmppConversation, int>();
        while (await reader.ReadAsync(token))
            counts[new((XmppConversationKind)reader.GetInt32(0), reader.GetString(1))] = reader.GetInt32(2);
        return counts;
    }

    public async Task<bool> HasEarlierAsync(string account, XmppConversation conversation, bool pingsOnly, XmppMessage before)
    {
        var directorbot = XmppAddresses.Directorbot(account);
        using var lease = await OpenAsync(CancellationToken.None);
        var db = lease.Connection;
        using var command = Create(db, null, $$"""
            SELECT 1 FROM main.XmppMessage
            WHERE Account = $account
              AND (Stamp < $stamp OR (Stamp = $stamp AND Id < $id))
              AND ($pings = 0 OR Stamp > {{FeedCutoffSql}})
              AND (
                ($pings = 0 AND Kind = $kind AND Address = $jid)
                OR ($pings = 1 AND {{MainFeedSql}})
              )
            UNION ALL SELECT 1 FROM roomSession.XmppMessage
            WHERE Account = $account
              AND (Stamp < $stamp OR (Stamp = $stamp AND -Id < $id))
              AND ($pings = 0 OR Stamp > {{FeedCutoffSql}})
              AND (
                ($pings = 0 AND Kind = $kind AND Address = $jid)
                OR ($pings = 1 AND {{FeedAlertSql}})
              )
            LIMIT 1
            """, ("$account", account), ("$kind", (int)conversation.Kind), ("$jid", conversation.Jid),
            ("$stamp", before.Timestamp.ToUnixTimeMilliseconds()), ("$id", before.LocalId), ("$pings", pingsOnly ? 1 : 0),
            ("$feedCleared", FeedClearedCheckpoint), ("$noCutoff", long.MinValue),
            ("$dirKind", (int)XmppConversationKind.Direct), ("$dirJid", directorbot.Jid));
        return await command.ExecuteScalarAsync() is not null;
    }

    public async Task<int> FeedUnreadCountAsync(string account, CancellationToken token = default)
    {
        var directorbot = XmppAddresses.Directorbot(account);
        using var lease = await OpenAsync(token);
        using var command = Create(lease.Connection, null, $$"""
            SELECT SUM(Total) FROM (
                SELECT COUNT(*) AS Total FROM main.XmppMessage
                WHERE Account = $account AND Unread = 1 AND Stamp > {{FeedCutoffSql}} AND {{MainFeedSql}}
                UNION ALL
                SELECT COUNT(*) AS Total FROM roomSession.XmppMessage
                WHERE Account = $account AND Unread = 1 AND Stamp > {{FeedCutoffSql}} AND {{FeedAlertSql}}
            )
            """, ("$account", account), ("$dirKind", (int)XmppConversationKind.Direct), ("$dirJid", directorbot.Jid),
            ("$feedCleared", FeedClearedCheckpoint), ("$noCutoff", long.MinValue));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token));
    }

    public async Task<int> MarkVisibleAsync(string account, XmppConversation conversation,
        IReadOnlyList<XmppMessage> visible, CancellationToken token, bool advanceRoomReadPosition = true)
    {
        if (visible.Count == 0) return 0;
        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var transaction = db.BeginTransaction();
        var changed = 0;
        foreach (var row in visible.Where(row => row.Unread))
        {
            var database = row.LocalId < 0 ? "roomSession" : "main";
            using var read = Create(db, transaction, $"""
                UPDATE {database}.XmppMessage SET Unread = 0
                WHERE Account = $account AND Kind = $kind AND Address = $address AND Id = $id AND Unread = 1
                """, ("$account", account), ("$kind", (int)conversation.Kind), ("$address", conversation.Jid),
                ("$id", Math.Abs(row.LocalId)));
            changed += await read.ExecuteNonQueryAsync(token);
        }
        if (advanceRoomReadPosition && conversation.Kind == XmppConversationKind.Room)
        {
            using var position = Create(db, transaction, SaveCheckpointSql,
                ("$account", account), ("$archive", ReadKey(conversation)),
                ("$stamp", visible.Max(row => row.Timestamp).ToUnixTimeMilliseconds() + 1));
            await position.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return changed;
    }

    private static string ReadKey(XmppConversation conversation) => "$read/" + conversation.Jid;

    public Task<DateTimeOffset> GetRoomReadPositionAsync(string account, string room, CancellationToken token) =>
        GetCheckpointAsync(account, ReadKey(new(XmppConversationKind.Room, room)),
            DateTimeOffset.MinValue, token, clampToRetention: false);

    public Task<int> MarkReadAsync(string account, XmppConversation conversation, CancellationToken token = default, long throughId = long.MaxValue) =>
        ExecuteAsync("""
            UPDATE XmppMessage SET Unread = 0
            WHERE Account = $account AND Kind = $kind AND Address = $address AND Unread = 1 AND Id <= $through
            """, token, ("$account", account), ("$kind", (int)conversation.Kind), ("$address", conversation.Jid), ("$through", throughId));

    public Task PruneAsync(CancellationToken token) =>
        ExecuteAsync("DELETE FROM XmppMessage WHERE Stamp < $cutoff", token,
            ("$cutoff", (DateTimeOffset.UtcNow - Retention).ToUnixTimeMilliseconds()));

    public async Task ClearAsync(string account, CancellationToken token = default)
    {
        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var transaction = db.BeginTransaction();
        using var command = Create(db, transaction, """
            INSERT INTO XmppCheckpoint (Account, Archive, Stamp) VALUES ($account, '$cleared', $stamp)
            ON CONFLICT (Account, Archive) DO UPDATE SET Stamp = MAX(Stamp, excluded.Stamp);
            DELETE FROM main.XmppMessage WHERE Account = $account;
            DELETE FROM roomSession.XmppMessage WHERE Account = $account;
            DELETE FROM XmppCheckpoint WHERE Account = $account AND Archive LIKE '$read/%';
            """, ("$account", account), ("$stamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<DateTimeOffset> GetCheckpointAsync(string account, string archive, DateTimeOffset fallback,
        CancellationToken token, bool clampToRetention = true)
    {
        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var command = Create(db, null, "SELECT Stamp FROM XmppCheckpoint WHERE Account = $account AND Archive = $archive",
            ("$account", account), ("$archive", archive));
        var stored = await command.ExecuteScalarAsync(token);
        var cutoff = DateTimeOffset.UtcNow - Retention;
        var position = stored is long time ? DateTimeOffset.FromUnixTimeMilliseconds(time) : fallback;
        return clampToRetention && position < cutoff ? cutoff : position;
    }

    private const string SaveCheckpointSql = """
        INSERT INTO XmppCheckpoint (Account, Archive, Stamp) VALUES ($account, $archive, $stamp)
        ON CONFLICT (Account, Archive) DO UPDATE SET Stamp = MAX(Stamp, excluded.Stamp)
        """;

    public async Task ClearFeedAsync(string account, CancellationToken token = default)
    {
        using var lease = await OpenAsync(token);
        using var command = Create(lease.Connection, null, SaveCheckpointSql,
            ("$account", account), ("$archive", FeedClearedCheckpoint), ("$stamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        await command.ExecuteNonQueryAsync(token);
    }

    public Task SaveCheckpointAsync(string account, string archive, DateTimeOffset stamp, CancellationToken token) =>
        ExecuteAsync(SaveCheckpointSql, token, ("$account", account), ("$archive", archive), ("$stamp", stamp.ToUnixTimeMilliseconds()));

    public Task<int> EnsureCheckpointAsync(string account, string archive, DateTimeOffset stamp, CancellationToken token) =>
        ExecuteAsync("""
            INSERT OR IGNORE INTO XmppCheckpoint (Account, Archive, Stamp) VALUES ($account, $archive, $stamp)
            """, token, ("$account", account), ("$archive", archive), ("$stamp", stamp.ToUnixTimeMilliseconds()));

    private async Task<int> ExecuteAsync(string sql, CancellationToken token, params (string Key, object Value)[] parameters)
    {
        using var lease = await OpenAsync(token);
        var db = lease.Connection;
        using var command = Create(db, null, sql, parameters);
        return await command.ExecuteNonQueryAsync(token);
    }

    private async Task<DatabaseLease> OpenAsync(CancellationToken token)
    {
        await _databaseGate.WaitAsync(token);
        try
        {
            if (_database is null)
            {
                var db = new SqliteConnection(new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ToString());
                try
                {
                    await db.OpenAsync(token);
                    using var command = db.CreateCommand();
                    command.CommandText = "PRAGMA foreign_keys = ON; ATTACH DATABASE ':memory:' AS roomSession;"
                        + MessageSchema("roomSession");
                    await command.ExecuteNonQueryAsync(token);
                    _database = db;
                }
                catch
                {
                    await db.DisposeAsync();
                    throw;
                }
            }
            return new DatabaseLease(_database, _databaseGate);
        }
        catch
        {
            _databaseGate.Release();
            throw;
        }
    }

    private sealed class DatabaseLease(SqliteConnection connection, SemaphoreSlim gate) : IDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public void Dispose() => gate.Release();
    }

    private static SqliteCommand Create(SqliteConnection db, SqliteTransaction? transaction, string sql,
        params (string Key, object Value)[] parameters)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return command;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _database?.Dispose();
        _databaseGate.Dispose();
    }
}

