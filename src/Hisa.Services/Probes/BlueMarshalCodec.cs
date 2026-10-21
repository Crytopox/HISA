// Managed port of blue-marshal-rs (fa99764271f5a70a9565ab6e6f092f57617382a8).
// Wire format and string table originate in CCP blue.Marshal.
// See BLUE-MARSHAL-LICENSE.txt. Never invokes serialized callbacks or types.
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Hisa.Services.Probes;

internal sealed record MarshalDocument(MarshalValue Root, bool HadChecksum);
internal static class BlueMarshalCodec
{
    internal const int MaxBytes = 64 * 1024 * 1024;
    internal const int MaxNodes = 1_000_000;
    internal const int MaxDepth = 128;
    internal static MarshalDocument Decode(byte[] bytes) => new Reader(bytes).Read();
    internal static byte[] Encode(MarshalDocument document)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)125); writer.Write((byte)1);
        if (document.HadChecksum) { writer.Write((byte)28); writer.Write(0u); }
        var nodes = 0;
        Write(writer, document.Root, 0, ref nodes);
        var bytes = stream.ToArray();
        if (document.HadChecksum) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(3, 4), Adler(bytes.AsSpan(7)));
        return bytes;
    }
    private static uint Adler(ReadOnlySpan<byte> bytes)
    {
        uint a = 1, b = 0;
        foreach (var v in bytes) { a = (a + v) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }
    private static void Length(BinaryWriter w, int n) { if (n < 255) w.Write((byte)n); else { w.Write((byte)255); w.Write(n); } }
    private static void Buffer(BinaryWriter w, byte[] b) { Length(w, b.Length); w.Write(b); }
    private static void Write(BinaryWriter w, MarshalValue v, int depth, ref int nodes)
    {
        if (depth > MaxDepth || ++nodes > MaxNodes || w.BaseStream.Length > MaxBytes) throw new InvalidDataException("Marshal resource limit exceeded.");
        switch (v.Kind)
        {
            case MarshalKind.None: w.Write((byte)1); break;
            case MarshalKind.Bool: w.Write((byte)((bool)v.Scalar! ? 31 : 32)); break;
            case MarshalKind.Int:
                // EVE uses Python 2 types: TY_INT64 constructs a PyLong, even
                // for small values. Ordinary ints must use the 32-bit tag.
                var integer = (long)v.Scalar!;
                if (integer < int.MinValue || integer > int.MaxValue)
                    throw new InvalidDataException("Python int exceeds the signed 32-bit range; use a typed long.");
                w.Write((byte)4); w.Write((int)integer); break;
            case MarshalKind.Long: w.Write((byte)47); Buffer(w, ((BigInteger)v.Scalar!).ToByteArray()); break;
            case MarshalKind.Float: w.Write((byte)10); w.Write((double)v.Scalar!); break;
            case MarshalKind.Bytes: w.Write((byte)19); Buffer(w, (byte[])v.Scalar!); break;
            case MarshalKind.Unicode: w.Write((byte)46); Buffer(w, new UTF8Encoding(false, true).GetBytes((string)v.Scalar!)); break;
            case MarshalKind.Global: w.Write((byte)2); Buffer(w, new UTF8Encoding(false, true).GetBytes((string)v.Scalar!)); break;
            case MarshalKind.Tuple:
            case MarshalKind.List:
                w.Write((byte)(v.Kind == MarshalKind.Tuple ? 20 : 21)); Length(w, v.Items.Count);
                foreach (var item in v.Items) Write(w, item, depth + 1, ref nodes); break;
            case MarshalKind.Dict:
                w.Write((byte)22); Length(w, v.Pairs.Count);
                foreach (var (k, value) in v.Pairs) { Write(w, value, depth + 1, ref nodes); Write(w, k, depth + 1, ref nodes); } break;
            case MarshalKind.Instance:
            case MarshalKind.Callback:
                w.Write((byte)(v.Kind == MarshalKind.Instance ? 23 : 25));
                foreach (var item in v.Items) Write(w, item, depth + 1, ref nodes); break;
            case MarshalKind.Reduce:
            case MarshalKind.NewObject:
                w.Write((byte)(v.Kind == MarshalKind.Reduce ? 34 : 35));
                Write(w, v.Items[0], depth + 1, ref nodes);
                foreach (var item in v.Items.Skip(1)) Write(w, item, depth + 1, ref nodes);
                w.Write((byte)45);
                foreach (var (k, value) in v.Pairs) { Write(w, k, depth + 1, ref nodes); Write(w, value, depth + 1, ref nodes); }
                w.Write((byte)45); break;
            default: throw new InvalidDataException("Unsupported marshal value.");
        }
        if (w.BaseStream.Length > MaxBytes) throw new InvalidDataException("Marshal output is too large.");
    }

    private sealed class Reader(byte[] data)
    {
        private int pos, end = data.Length, version, nodes, sharedSeen;
        private int crcStart = -1; private uint crc;
        private readonly List<MarshalValue?> shared = [];
        private int[] map = [];
        private readonly Dictionary<MarshalValue, int> costs = new(ReferenceEqualityComparer.Instance);
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
        private byte Byte() { Need(1); return data[pos++]; }
        private void Need(int count) { if (count < 0 || count > end - pos) throw new InvalidDataException("Truncated marshal stream."); }
        private byte[] Bytes(int n) { Need(n); var b = data.AsSpan(pos, n).ToArray(); pos += n; return b; }
        private int I32() { Need(4); var n = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4)); pos += 4; return n; }
        private int Length() { var b = Byte(); var n = b == 255 ? I32() : b; if (n < 0) throw new InvalidDataException("Negative marshal length."); return n; }
        private byte[] Buffer() => Bytes(Length());
        internal MarshalDocument Read()
        {
            if (data.Length > MaxBytes) throw new InvalidDataException("Settings file is too large.");
            var sig = Byte(); version = sig == 126 ? 0 : sig == 125 ? Byte() : throw new InvalidDataException("Invalid marshal signature.");
            if (version is not (0 or 1)) throw new InvalidDataException("Unsupported marshal version.");
            if (version == 0)
            {
                var count = I32();
                if (count < 0 || count > MaxNodes || count > (data.Length - pos) / 4) throw new InvalidDataException("Invalid shared map length.");
                end = data.Length - count * 4;
                map = new int[count];
                for (var i = 0; i < count; i++) {
                    var n = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(end + i * 4, 4));
                    if (n < 1 || n > count) throw new InvalidDataException("Invalid shared map entry.");
                    map[i] = n - 1; shared.Add(null);
                }
                if (map.Distinct().Count() != count) throw new InvalidDataException("Duplicate shared map entry.");
            }
            var root = Object(0);
            if (pos != end) throw new InvalidDataException("Unexpected trailing marshal data.");
            if (crcStart >= 0 && Adler(data.AsSpan(crcStart, (version == 0 ? data.Length : end) - crcStart)) != crc)
                throw new InvalidDataException("Settings checksum mismatch.");
            return new(root, crcStart >= 0);
        }
        private MarshalValue Object(int depth)
        {
            if (depth > MaxDepth || ++nodes > MaxNodes) throw new InvalidDataException("Marshal resource limit exceeded.");
            var raw = Byte();
            if ((raw & 128) != 0) throw new InvalidDataException("Invalid marshal type flag.");
            var tag = raw & 63; var isShared = (raw & 64) != 0;
            if (tag == 28) {
                if (crcStart >= 0 || depth != 0 || isShared) throw new InvalidDataException("Unexpected checksum wrapper.");
                crc = unchecked((uint)I32()); crcStart = pos; return Object(depth + 1);
            }
            if (tag == 27) {
                if (isShared) throw new InvalidDataException("Shared reference flag is invalid.");
                var id = Length() - (version == 0 ? 1 : 0);
                if (id < 0 || id >= shared.Count || shared[id] is not { } target) throw new InvalidDataException("Dangling or cyclic marshal reference.");
                nodes = checked(nodes + costs[target]);
                if (nodes > MaxNodes) throw new InvalidDataException("Expanded reference limit exceeded.");
                return target;
            }
            int? slot = null;
            if (isShared) {
                if (version == 0) {
                    if (sharedSeen >= map.Length) throw new InvalidDataException("Shared map overflow.");
                    slot = map[sharedSeen++];
                } else { slot = shared.Count; shared.Add(null); }
            }
            var before = nodes;
            MarshalValue v;
            switch (tag)
            {
                case 1: v = new(MarshalKind.None); break;
                case 31: case 32: v = new(MarshalKind.Bool, tag == 31); break;
                case 7: case 8: case 9: v = MarshalValue.Int(tag - 8); break;
                case 6: v = MarshalValue.Int(unchecked((sbyte)Byte())); break;
                case 5: v = MarshalValue.Int(BinaryPrimitives.ReadInt16LittleEndian(Bytes(2))); break;
                case 4: v = MarshalValue.Int(I32()); break;
                // Both TY_INT64 and TY_LONG produce a Python long in EVE.
                // blue-marshal-rs conflates TY_INT64 with ordinary ints.
                case 3: v = new(MarshalKind.Long, new BigInteger(BinaryPrimitives.ReadInt64LittleEndian(Bytes(8)))); break;
                case 11: v = MarshalValue.Float(0); break;
                case 10: v = MarshalValue.Float(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Bytes(8)))); break;
                case 47: v = new(MarshalKind.Long, new BigInteger(Buffer())); break;
                case 14: v = new(MarshalKind.Bytes, Array.Empty<byte>()); break;
                case 15: v = new(MarshalKind.Bytes, new[] { Byte() }); break;
                case 16: v = new(MarshalKind.Bytes, Bytes(Byte())); break;
                case 17:
                    var index = Byte();
                    if (index < 1 || index > MarshalStringTable.Strings.Length) throw new InvalidDataException("Invalid string table index.");
                    v = MarshalValue.Bytes(MarshalStringTable.Strings[index - 1]); break;
                case 13: case 19: v = new(MarshalKind.Bytes, Buffer()); break;
                case 40: v = new(MarshalKind.Unicode, ""); break;
                case 41: v = new(MarshalKind.Unicode, Utf16.GetString(Bytes(2))); break;
                case 18: v = new(MarshalKind.Unicode, Utf16.GetString(Bytes(checked(Length() * 2)))); break;
                case 46: v = new(MarshalKind.Unicode, Utf8.GetString(Buffer())); break;
                case 2: v = new(MarshalKind.Global, Utf8.GetString(Buffer())); break;
                case 20: case 21: case 36: case 37: case 38: case 39: case 44:
                    var count = tag switch { 36 or 38 => 0, 37 or 39 => 1, 44 => 2, _ => Length() };
                    Need(count);
                    if (count > MaxNodes - nodes) throw new InvalidDataException("Container limit exceeded.");
                    v = new(tag is 21 or 38 or 39 ? MarshalKind.List : MarshalKind.Tuple);
                    for (var i = 0; i < count; i++) v.Items.Add(Object(depth + 1)); break;
                case 22:
                    count = Length(); Need(checked(count * 2));
                    if (count > (MaxNodes - nodes) / 2) throw new InvalidDataException("Dictionary limit exceeded.");
                    v = new(MarshalKind.Dict);
                    for (var i = 0; i < count; i++) { var value = Object(depth + 1); var key = Object(depth + 1); v.Pairs.Add((key, value)); } break;
                case 23:
                    v = new(MarshalKind.Instance); v.Items.Add(Object(depth + 1));
                    if (v.Items[0].Kind is not (MarshalKind.Bytes or MarshalKind.Unicode)) throw new InvalidDataException("Instance type is not text.");
                    v.Items.Add(Object(depth + 1)); break;
                case 25: v = new(MarshalKind.Callback); v.Items.Add(Object(depth + 1)); break;
                case 34: case 35:
                    v = new(tag == 34 ? MarshalKind.Reduce : MarshalKind.NewObject);
                    var payload = Object(depth + 1);
                    if (payload.Kind != MarshalKind.Tuple || payload.Items.Count < (tag == 34 ? 2 : 1) || payload.Items.Count > (tag == 34 ? 3 : 2)) throw new InvalidDataException("Invalid reduce payload.");
                    v.Items.Add(payload);
                    while (Peek() != 45) v.Items.Add(Object(depth + 1)); Byte();
                    while (Peek() != 45) { var k = Object(depth + 1); var value = Object(depth + 1); v.Pairs.Add((k, value)); } Byte(); break;
                default: throw new InvalidDataException($"Unsupported marshal type {tag}; file has not been modified.");
            }
            costs[v] = nodes - before + 1;
            if (slot is { } ix) shared[ix] = v;
            return v;
        }
        private byte Peek() { Need(1); return data[pos]; }
    }
}
