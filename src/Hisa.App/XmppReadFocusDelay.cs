using System.Diagnostics;
using Hisa.Core.Models;
using Hisa.Services.Xmpp;

namespace Hisa.App;

internal sealed class XmppReadFocusDelay
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);
    private readonly Dictionary<(string Account, bool Feed, long Id), (long Started, XmppMessage Message)> _pending = [];
    private object? _target;
    private long _started;

    public bool HasDueRows => _pending.Values.Any(row => Stopwatch.GetElapsedTime(row.Started) >= Delay);

    public bool Update(object? target)
    {
        if (ReferenceEquals(_target, target)) return false;
        _target = target;
        _started = Stopwatch.GetTimestamp();
        return true;
    }

    public void Observe(string account, bool feed, IEnumerable<XmppMessage> visible)
    {
        if (_target is null) return;
        // Keep the first focus timestamp for rows seen again before their deadline.
        foreach (var message in visible)
            _pending.TryAdd((account, feed, message.LocalId), (_started, message));
    }

    public async Task MarkDueRowsAsync(XmppClientService client)
    {
        var account = client.Account?.Jid;
        foreach (var key in _pending.Keys.Where(key => key.Account != account).ToArray())
            _pending.Remove(key);
        var now = Stopwatch.GetTimestamp();
        var due = _pending.Where(row => Stopwatch.GetElapsedTime(row.Value.Started, now) >= Delay)
            .ToLookup(row => row.Key.Feed);
        foreach (var group in due)
        {
            if (client.Account?.Jid != account) return;
            var rows = group.Where(row => _pending.ContainsKey(row.Key)).ToArray();
            if (rows.Length == 0) continue;
            await client.MarkVisibleAsync(rows.Select(row => row.Value.Message).ToArray(), feed: group.Key);
            foreach (var row in rows)
                _pending.Remove(row.Key);
        }
    }

    public void ClearPending() => _pending.Clear();
}
