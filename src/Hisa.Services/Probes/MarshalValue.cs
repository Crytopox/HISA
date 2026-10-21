using System.Numerics;
using System.Text;

namespace Hisa.Services.Probes;

// Data only: globals, instances and reduce payloads are never executed.
internal enum MarshalKind { None, Bool, Int, Long, Float, Bytes, Unicode, Tuple, List, Dict, Global, Instance, Reduce, NewObject, Callback }
internal sealed class MarshalValue(MarshalKind kind, object? scalar = null)
{
    public MarshalKind Kind { get; } = kind;
    public object? Scalar { get; } = scalar;
    public List<MarshalValue> Items { get; } = [];
    public List<(MarshalValue Key, MarshalValue Value)> Pairs { get; } = [];
    public static MarshalValue Bytes(string s) => new(MarshalKind.Bytes, Encoding.UTF8.GetBytes(s));
    public static MarshalValue Int(long n) => new(MarshalKind.Int, n);
    public static MarshalValue Float(double n) => new(MarshalKind.Float, n);
    public static MarshalValue Tuple(params MarshalValue[] values) { var v = new MarshalValue(MarshalKind.Tuple); v.Items.AddRange(values); return v; }
    public bool IsByteKey(string key) => Kind == MarshalKind.Bytes && ((byte[])Scalar!).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(key));
    public MarshalValue? Get(string key) => Pairs.FirstOrDefault(p => p.Key.IsByteKey(key)).Value;
    public void Set(string key, MarshalValue value)
    {
        var ix = Pairs.FindIndex(p => p.Key.IsByteKey(key));
        if (ix >= 0) Pairs[ix] = (Pairs[ix].Key, value); else Pairs.Add((Bytes(key), value));
    }
    public string Text => Kind switch { MarshalKind.Bytes => new UTF8Encoding(false, true).GetString((byte[])Scalar!), MarshalKind.Unicode or MarshalKind.Global => (string)Scalar!, _ => throw new InvalidDataException("Expected text.") };
    public long Integer => Kind == MarshalKind.Int ? (long)Scalar! : throw new InvalidDataException("Expected an integer.");
    public double Number => Kind switch { MarshalKind.Int => (long)Scalar!, MarshalKind.Float => (double)Scalar!, MarshalKind.Long => (double)(BigInteger)Scalar!, _ => throw new InvalidDataException("Expected a number.") };
    public bool Same(MarshalValue b)
    {
        if (Kind != b.Kind || Items.Count != b.Items.Count || Pairs.Count != b.Pairs.Count) return false;
        var equal = Kind == MarshalKind.Bytes ? ((byte[])Scalar!).AsSpan().SequenceEqual((byte[])b.Scalar!) :
            Kind == MarshalKind.Float ? BitConverter.DoubleToInt64Bits((double)Scalar!) == BitConverter.DoubleToInt64Bits((double)b.Scalar!) : Equals(Scalar, b.Scalar);
        return equal && Items.Zip(b.Items).All(p => p.First.Same(p.Second)) && Pairs.Zip(b.Pairs).All(p => p.First.Key.Same(p.Second.Key) && p.First.Value.Same(p.Second.Value));
    }
}
