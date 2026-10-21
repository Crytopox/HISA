using System.Numerics;
using Hisa.Core.Models;

namespace Hisa.Services.Probes;

internal static class EveFormationData
{
    internal const string FormationKey = "probescanning.customFormations";
    internal static MarshalValue? Entries(MarshalValue root)
    {
        if (root.Kind != MarshalKind.Dict) throw new InvalidDataException("Settings root is not a dictionary.");
        var ui = root.Get("ui");
        if (ui is null) return null;
        if (ui.Kind != MarshalKind.Dict) throw new InvalidDataException("Settings ui is not a dictionary.");
        var setting = ui.Get(FormationKey);
        if (setting is null) return null;
        RequireTuple(setting, 2);
        if (setting.Items[0].Kind != MarshalKind.Long || setting.Items[1].Kind != MarshalKind.Dict) throw new InvalidDataException("Unexpected formations timestamp or dictionary.");
        return setting.Items[1];
    }
    internal static (List<EveStoredFormation> Formations, List<string> Errors) Read(MarshalValue root)
    {
        var formations = new List<EveStoredFormation>(); var errors = new List<string>();
        try {
            var entries = Entries(root); if (entries is null) return (formations, errors);
            var ids = new HashSet<long>();
            foreach (var (key, value) in entries.Pairs)
            {
                try {
                    var id = key.Integer;
                    if (!ids.Add(id)) throw new InvalidDataException($"Duplicate formation ID {id}.");
                    if (id < 0) continue;
                    RequireTuple(value, 2);
                    var name = value.Items[0].Text;
                    var probes = value.Items[1];
                    if (probes.Kind != MarshalKind.List) throw new InvalidDataException("Probe collection is not a list.");
                    var parsed = probes.Items.Select(p => {
                        RequireTuple(p, 2); var xyz = p.Items[0]; RequireTuple(xyz, 3);
                        return new FormationProbe(new(xyz.Items[0].Number, xyz.Items[1].Number, xyz.Items[2].Number), p.Items[1].Number);
                    }).ToArray();
                    var formation = new ProbeFormation(Guid.NewGuid(), name, parsed); ProbeFormationMath.Validate(formation);
                    formations.Add(new(id, formation));
                } catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException) { errors.Add($"Formation entry: {ex.Message}"); }
            }
        } catch (InvalidDataException ex) { errors.Add(ex.Message); }
        return (formations, errors);
    }
    internal static MarshalValue Patch(MarshalValue root, IReadOnlyList<FormationMergeEntry> changes)
    {
        var entries = CopyDict(Entries(root) ?? new(MarshalKind.Dict));
        foreach (var change in changes)
        {
            var probes = new MarshalValue(MarshalKind.List);
            probes.Items.AddRange(change.Formation.Probes.Select(p => MarshalValue.Tuple(
                MarshalValue.Tuple(MarshalValue.Float(p.Position.X), MarshalValue.Float(p.Position.Y), MarshalValue.Float(p.Position.Z)), MarshalValue.Float(p.RadiusMeters))));
            var value = MarshalValue.Tuple(new(MarshalKind.Unicode, change.Formation.Name), probes);
            var ix = entries.Pairs.FindIndex(p => p.Key.Kind == MarshalKind.Int && p.Key.Integer == change.TargetId);
            if (ix >= 0) entries.Pairs[ix] = (entries.Pairs[ix].Key, value); else entries.Pairs.Add((MarshalValue.Int(change.TargetId), value));
        }
        var ui = CopyDict(root.Get("ui") ?? new(MarshalKind.Dict));
        ui.Set(FormationKey, MarshalValue.Tuple(new(MarshalKind.Long, new BigInteger(DateTime.UtcNow.ToFileTimeUtc())), entries));
        var result = CopyDict(root); result.Set("ui", ui); return result;
    }
    internal static MarshalValue WithoutFormations(MarshalValue root)
    {
        var result = CopyDict(root); var existingUi = root.Get("ui");
        if (existingUi is null) return result;
        var ui = CopyDict(existingUi); ui.Pairs.RemoveAll(p => p.Key.IsByteKey(FormationKey));
        // Creating ui solely to hold formations is the only allowed structural addition.
        if (ui.Pairs.Count == 0) result.Pairs.RemoveAll(p => p.Key.IsByteKey("ui")); else result.Set("ui", ui);
        return result;
    }
    private static MarshalValue CopyDict(MarshalValue value)
    {
        if (value.Kind != MarshalKind.Dict) throw new InvalidDataException("Expected settings dictionary.");
        var copy = new MarshalValue(MarshalKind.Dict); copy.Pairs.AddRange(value.Pairs); return copy;
    }
    private static void RequireTuple(MarshalValue v, int n) { if (v.Kind != MarshalKind.Tuple || v.Items.Count != n) throw new InvalidDataException($"Expected a {n}-element tuple."); }
}
