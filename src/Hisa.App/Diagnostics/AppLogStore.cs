using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hisa.App.Diagnostics;

public sealed class AppLogStore
{
    private readonly ConcurrentQueue<AppLogEntry> _entries = new();
    private readonly int _maxEntries;

    public AppLogStore(int maxEntries = 5000)
    {
        _maxEntries = Math.Max(100, maxEntries);
    }

    public event EventHandler<AppLogEntry>? EntryAdded;

    public IReadOnlyList<AppLogEntry> Snapshot()
    {
        return _entries.ToArray();
    }

    public void Add(AppLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > _maxEntries && _entries.TryDequeue(out _))
        {
        }

        EntryAdded?.Invoke(this, entry);
    }
}

public sealed record AppLogEntry(
    DateTimeOffset TimestampUtc,
    LogLevel Level,
    string SourceTag,
    string Category,
    string Message,
    string? Exception);
