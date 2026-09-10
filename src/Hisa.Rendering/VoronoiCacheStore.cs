using System.Buffers.Binary;
using System.Security.Cryptography;
using Hisa.Core.Models;

namespace Hisa.Rendering;

public sealed class VoronoiCacheStore
{
    public const int MaxMemoryEntries = 8;

    private readonly Dictionary<string, Dictionary<long, IReadOnlyList<(double X, double Y)>>> _entries = [];
    private readonly LinkedList<string> _lru = [];
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = [];
    private readonly object _gate = new();

    public bool TryGet(string key, out Dictionary<long, IReadOnlyList<(double X, double Y)>> polygons)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out polygons!))
            {
                Touch(key);
                return true;
            }
        }

        polygons = [];
        return false;
    }

    public void Set(string key, Dictionary<long, IReadOnlyList<(double X, double Y)>> polygons)
    {
        lock (_gate)
        {
            _entries[key] = polygons;
            Touch(key);
            while (_entries.Count > MaxMemoryEntries && _lru.Last is { } oldest)
            {
                _entries.Remove(oldest.Value);
                _lruNodes.Remove(oldest.Value);
                _lru.RemoveLast();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public static string ComputeKey(MapGraph graph)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header, graph.Nodes.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], graph.Links.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], 2);
        hasher.AppendData(header[..12]);

        Span<byte> nodeBuffer = stackalloc byte[24];
        foreach (var node in graph.Nodes)
        {
            BinaryPrimitives.WriteInt64LittleEndian(nodeBuffer, node.Id);
            BinaryPrimitives.WriteDoubleLittleEndian(nodeBuffer[8..], node.X);
            BinaryPrimitives.WriteDoubleLittleEndian(nodeBuffer[16..], node.Y);
            hasher.AppendData(nodeBuffer);
        }

        Span<byte> linkBuffer = stackalloc byte[16];
        foreach (var link in graph.Links)
        {
            var a = Math.Min(link.FromId, link.ToId);
            var b = Math.Max(link.FromId, link.ToId);
            BinaryPrimitives.WriteInt64LittleEndian(linkBuffer, a);
            BinaryPrimitives.WriteInt64LittleEndian(linkBuffer[8..], b);
            hasher.AppendData(linkBuffer);
        }

        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private void Touch(string key)
    {
        if (_lruNodes.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return;
        }

        _lruNodes[key] = _lru.AddFirst(key);
    }
}
