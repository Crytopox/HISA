using System.Text.Json.Serialization;

namespace Hisa.Core.Models;

public readonly record struct ProbePosition(double X, double Y, double Z)
{
    public static ProbePosition operator +(ProbePosition a, ProbePosition b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static ProbePosition operator -(ProbePosition a, ProbePosition b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static ProbePosition operator *(ProbePosition a, double f) => new(a.X * f, a.Y * f, a.Z * f);
    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double Dot(ProbePosition b) => X * b.X + Y * b.Y + Z * b.Z;
    public ProbePosition Cross(ProbePosition b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
}

public sealed record FormationProbe(ProbePosition Position, double RadiusMeters);
public sealed record ProbeFormation(Guid Id, string Name, IReadOnlyList<FormationProbe> Probes)
{
    [JsonIgnore] public ProbePosition Centroid => Probes.Count == 0 ? default : Probes.Aggregate(default(ProbePosition), (a, p) => a + p.Position * (1d / Probes.Count));
}

public enum ProbeAxis { X, Y, Z }
public enum FormationConflictAction { Unresolved, KeepExisting, Replace, RenamedCopy }
public enum FormationImportAction { Skip, Replace, RenamedCopy }

public static class ProbeFormationMath
{
    public const double AstronomicalUnit = 149_597_870_700d;
    public static IReadOnlyList<double> StandardRadiiAu { get; } = Array.AsReadOnly(new[] { .25, .5, 1, 2, 4, 8, 16, 32 });
    public static void Validate(ProbeFormation f)
    {
        if (string.IsNullOrWhiteSpace(f.Name) || f.Name.Length > 256) throw new ArgumentException("Formation names must contain 1–256 characters.");
        if (f.Probes is null || f.Probes.Count is < 1 or > 8) throw new ArgumentException("A formation needs 1–8 probes.");
        foreach (var p in f.Probes)
            if (p is null || !double.IsFinite(p.Position.X) || !double.IsFinite(p.Position.Y) || !double.IsFinite(p.Position.Z) || !double.IsFinite(p.RadiusMeters) || p.RadiusMeters <= 0)
                throw new ArgumentException("Coordinates must be finite and scan radii must be positive and finite.");
    }
    public static ProbeFormation Translate(ProbeFormation f, ProbePosition delta) => Checked(f with { Probes = f.Probes.Select(p => p with { Position = p.Position + delta }).ToArray() });
    public static ProbeFormation Center(ProbeFormation f) => Translate(f, f.Centroid * -1);
    public static ProbePosition SelectionCentroid(ProbeFormation f, IReadOnlyCollection<int> indices)
    {
        ValidateSelection(f, indices);
        return indices.Aggregate(default(ProbePosition), (sum, i) => sum + f.Probes[i].Position * (1d / indices.Count));
    }
    public static ProbeFormation TransformSelection(ProbeFormation f, IReadOnlyCollection<int> indices, Func<ProbeFormation, ProbeFormation> transform)
    {
        ValidateSelection(f, indices);
        var ordered = indices.Order().ToArray();
        var subset = f with { Probes = ordered.Select(i => f.Probes[i]).ToArray() };
        var changed = transform(subset); Validate(changed);
        if (changed.Probes.Count != ordered.Length) throw new ArgumentException("A transform cannot change the selection size.");
        var probes = f.Probes.ToArray();
        for (var i = 0; i < ordered.Length; i++) probes[ordered[i]] = changed.Probes[i];
        return Checked(f with { Probes = probes });
    }
    private static void ValidateSelection(ProbeFormation f, IReadOnlyCollection<int> indices)
    {
        if (indices.Count == 0 || indices.Distinct().Count() != indices.Count || indices.Any(i => i < 0 || i >= f.Probes.Count))
            throw new ArgumentException("Select one or more valid probes.");
    }
    public static ProbeFormation Scale(ProbeFormation f, double factor, bool aboutCentroid)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentException("Scale must be positive and finite.");
        var pivot = aboutCentroid ? f.Centroid : default;
        return Checked(f with { Probes = f.Probes.Select(p => p with { Position = pivot + (p.Position - pivot) * factor }).ToArray() });
    }
    public static ProbeFormation Rotate(ProbeFormation f, ProbeAxis axis, double degrees, bool aboutCentroid)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentException("Angle must be finite.");
        var pivot = aboutCentroid ? f.Centroid : default;
        var a = degrees % 360 * Math.PI / 180; var c = Math.Cos(a); var s = Math.Sin(a);
        return Checked(f with { Probes = f.Probes.Select(p => {
            var v = p.Position - pivot;
            var r = axis switch {
                ProbeAxis.X => new ProbePosition(v.X, c * v.Y - s * v.Z, s * v.Y + c * v.Z),
                ProbeAxis.Y => new ProbePosition(c * v.X + s * v.Z, v.Y, -s * v.X + c * v.Z),
                _ => new ProbePosition(c * v.X - s * v.Y, s * v.X + c * v.Y, v.Z) };
            return p with { Position = pivot + r };
        }).ToArray() });
    }
    private static ProbeFormation Checked(ProbeFormation f) { Validate(f); return f; }
    public static ProbeFormation Create(string name = "New formation") => new(Guid.NewGuid(), name,
        new[] { new ProbePosition(1,1,1), new(1,1,-1), new(1,-1,1), new(1,-1,-1), new(-1,1,1), new(-1,1,-1), new(-1,-1,1), new(-1,-1,-1) }
            .Select(p => new FormationProbe(p * 250_000, 32 * AstronomicalUnit)).ToArray());
}

public sealed record FormationLibrary(int Version, string Units, IReadOnlyList<ProbeFormation> Formations);
public sealed record EveSettingsAccount(string Path, string Installation, string Profile, long AccountId, string? Alias = null)
{
    public override string ToString() => $"{Alias ?? AccountId.ToString()} · {Profile} · {Installation}";
}
public sealed record EveStoredFormation(long EveId, ProbeFormation Formation);
public sealed record EveFormationSnapshot(EveSettingsAccount Account, string Fingerprint, IReadOnlyList<EveStoredFormation> Formations, IReadOnlyList<string> Errors);
public sealed record FormationMergeChoice(Guid LibraryId, FormationConflictAction Action, long? TargetId = null, string? NewName = null);
public sealed record FormationMergeEntry(long TargetId, ProbeFormation Formation, bool ReplacesExisting);
public sealed record FormationMergePreview(EveFormationSnapshot Snapshot, IReadOnlyList<FormationMergeEntry> Entries);
public sealed record EveSettingsApplyResult(string Path, bool Success, string Message, string? BackupPath = null);
public sealed record EveSettingsBackup(string Path, string SourcePath, DateTimeOffset CreatedUtc, string Sha256);
public sealed record EveDiscoveryPreferences(IReadOnlyList<string> Roots, IReadOnlyDictionary<string, string> Aliases);
public enum EveClientStatus { Closed, Running, Unknown }
