using System.Numerics;
using System.Text.Json;
using Hisa.Services.Probes;

namespace Hisa.Services.Tests;

public sealed class BlueMarshalCodecTests
{
    [Theory]
    [InlineData(int.MinValue)] [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    [InlineData(16918921)] [InlineData(int.MaxValue)]
    public void PythonIntsUseInt32TagRatherThanLongProducingInt64(int value)
    {
        var bytes = BlueMarshalCodec.Encode(new(MarshalValue.Int(value), false));
        // Independent wire-level assertion: reverence marshal.c loads tag 4
        // with PyInt_FromLong, but tag 3 with PyLong_FromLongLong:
        // https://github.com/ntt/reverence/blob/master/src/blue/marshal.c
        Assert.Equal((byte)4, bytes[2]);
        Assert.Equal(7, bytes.Length);
        Assert.Equal(value, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3)));
        Assert.Equal(MarshalKind.Int, BlueMarshalCodec.Decode(bytes).Root.Kind);
    }
    [Theory]
    [InlineData(-1L)] [InlineData(0L)] [InlineData(1L)]
    [InlineData(long.MinValue)] [InlineData(long.MaxValue)]
    public void Int64WireTagIsAPythonLongEvenWhenTheValueFitsAnInt(long value)
    {
        var bytes = new byte[11]; bytes[0] = 125; bytes[1] = 1; bytes[2] = 3;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(3), value);
        var doc = BlueMarshalCodec.Decode(bytes);
        Assert.Equal(MarshalKind.Long, doc.Root.Kind);
        Assert.Equal(new BigInteger(value), doc.Root.Scalar);
        var output = BlueMarshalCodec.Encode(doc);
        Assert.Equal((byte)47, output[2]);
        Assert.True(doc.Root.Same(BlueMarshalCodec.Decode(output).Root));
    }
    [Fact]
    public void IntOutsidePythonIntRangeFailsInsteadOfSilentlyChangingItsType()
    {
        Assert.Throws<InvalidDataException>(() => BlueMarshalCodec.Encode(new(MarshalValue.Int((long)int.MaxValue + 1), false)));
        Assert.Throws<InvalidDataException>(() => BlueMarshalCodec.Encode(new(MarshalValue.Int((long)int.MinValue - 1), false)));
    }
    [Fact]
    public void SettingsWithInt64TimestampAndInt32FormationKeysRetainTheirDistinctTypes()
    {
        // Hand-assembled wire data, independent of our encoder. This tuple
        // contains the two distinct Python integer representations seen in
        // client settings: long timestamp and int formation ID.
        var doc = BlueMarshalCodec.Decode(Convert.FromHexString("7D012C03D2040000000000000407000000"));
        Assert.Equal(MarshalKind.Long, doc.Root.Items[0].Kind);
        Assert.Equal(new BigInteger(1234), doc.Root.Items[0].Scalar);
        Assert.Equal(MarshalKind.Int, doc.Root.Items[1].Kind);
        Assert.Equal(7, doc.Root.Items[1].Integer);
        var encoded = BlueMarshalCodec.Encode(doc);
        Assert.Equal((byte)47, encoded[4]); // tuple header, then typed long
        Assert.True(doc.Root.Same(BlueMarshalCodec.Decode(encoded).Root));
    }
    [Fact]
    public void IndependentRustWasmSettingsFixtureDecodesWithAllTypesAndExactProbeData()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blue-marshal-v1.json")));
        var document = BlueMarshalCodec.Decode(Convert.FromBase64String(fixture.RootElement.GetProperty("base64").GetString()!));
        Assert.True(document.HadChecksum);
        var data = EveFormationData.Read(document.Root); Assert.Empty(data.Errors);
        var f = Assert.Single(data.Formations); Assert.Equal(7, f.EveId); Assert.Equal("Independent 雪", f.Formation.Name);
        var p = Assert.Single(f.Formation.Probes); Assert.Equal(123456.789123, p.Position.X); Assert.Equal(-500000.25, p.Position.Y); Assert.Equal(.125, p.Position.Z); Assert.Equal(37399467675, p.RadiusMeters);
        var unrelated = document.Root.Get("ui")!.Get("unrelated")!;
        Assert.Equal(BigInteger.Parse("-123456789012345678901234567890"), unrelated.Items[3].Scalar);
        Assert.Equal(new byte[] { 255, 0, 128 }, unrelated.Items[4].Scalar);
        Assert.Equal("Snow ☃ 😀", unrelated.Items[5].Text);
        Assert.Equal(MarshalKind.Instance, unrelated.Items[7].Kind); Assert.Equal(MarshalKind.Callback, unrelated.Items[8].Kind);
        Assert.Equal(MarshalKind.Reduce, unrelated.Items[9].Kind); Assert.Equal(MarshalKind.NewObject, unrelated.Items[10].Kind);
        Assert.Contains(document.Root.Pairs, e => e.Key.Kind == MarshalKind.Tuple);
        var reencoded = BlueMarshalCodec.Encode(document);
        Assert.True(document.Root.Same(BlueMarshalCodec.Decode(reencoded).Root));
        // Optional external-oracle check consumes this ignored test-output artifact.
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blue-marshal-csharp.bin"), reencoded);
    }
    [Fact]
    public void IndependentWireFixturesDecodeV0V1ReferencesTableAndUtf16()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "blue-marshal-wire.json")));
        foreach (var entry in fixture.RootElement.EnumerateArray()) {
            var doc = BlueMarshalCodec.Decode(Convert.FromBase64String(entry.GetProperty("base64").GetString()!));
            switch (entry.GetProperty("name").GetString()) {
                case "table": Assert.Equal("*corpid", doc.Root.Text); break;
                case "unicode16": Assert.Equal("hé", doc.Root.Text); break;
                case "v0crc": Assert.True(doc.HadChecksum); Assert.Equal(1, doc.Root.Integer); break;
                default: Assert.Equal(2, doc.Root.Items.Count); Assert.All(doc.Root.Items, v => Assert.Equal("hi", v.Text)); break;
            }
            Assert.True(doc.Root.Same(BlueMarshalCodec.Decode(BlueMarshalCodec.Encode(doc)).Root));
        }
    }
    [Theory]
    [InlineData("")] [InlineData("00000000")] [InlineData("7d")] [InlineData("7d0216")]
    [InlineData("7d0114ffffffffff")] [InlineData("7d0116ffffffff7f")]
    [InlineData("7d011b2a")] [InlineData("7d0155011b00")]
    [InlineData("7d012a")] [InlineData("7d011a")] [InlineData("7d012b")]
    [InlineData("7d0121")] [InlineData("7d010100")]
    public void MalformedUnsupportedCyclicAndTrailingDataIsRejected(string hex) => Assert.ThrowsAny<Exception>(() => BlueMarshalCodec.Decode(Convert.FromHexString(hex)));
    [Fact]
    public void ChecksumCorruptionIsRejected()
    {
        var bytes = BlueMarshalCodec.Encode(new(MarshalValue.Int(5), true)); bytes[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => BlueMarshalCodec.Decode(bytes));
    }
    [Fact]
    public void DeepInputsAndReferenceExpansionAreBounded()
    {
        var bytes = new List<byte> {125, 1}; for (var i = 0; i < 200; i++) bytes.Add(37); bytes.Add(1);
        Assert.Throws<InvalidDataException>(() => BlueMarshalCodec.Decode(bytes.ToArray()));
        var root = new MarshalValue(MarshalKind.List); var current = root;
        for (var i = 0; i < 200; i++) { var next = new MarshalValue(MarshalKind.List); current.Items.Add(next); current = next; }
        Assert.Throws<InvalidDataException>(() => BlueMarshalCodec.Encode(new(root, false)));
        // A shallow wire stream with exponentially expanding shared references.
        var bomb = new List<byte> {125,1,21,30,103,1};
        for (byte i = 1; i < 30; i++) bomb.AddRange(new byte[] {85,2,27,(byte)(i-1),27,(byte)(i-1)});
        Assert.Throws<InvalidDataException>(() => BlueMarshalCodec.Decode(bomb.ToArray()));
    }
    [Fact]
    public void NegativeZeroAndNonfiniteUnrelatedFloatsArePreserved()
    {
        foreach (var value in new[] { -0d, double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
            var root = MarshalValue.Float(value); Assert.True(root.Same(BlueMarshalCodec.Decode(BlueMarshalCodec.Encode(new(root, false))).Root));
        }
    }
}
