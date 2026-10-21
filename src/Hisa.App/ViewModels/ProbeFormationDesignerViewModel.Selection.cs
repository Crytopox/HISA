using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class ProbeNumericCell : INotifyPropertyChanged
{
    private string text = "", originalText = "", error = "";
    private readonly Action changed;
    public int Index { get; }
    public int Column { get; }
    public double Meters { get; private set; }
    public double Factor { get; private set; }
    public string Text { get => text; set { if (text == value) return; text = value; Validate(); Notify(nameof(Text)); changed(); } }
    public string Error => error;
    public bool HasError => error.Length > 0;
    public bool IsPending => text != originalText;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProbeNumericCell(int index, int column, Action changed) { Index = index; Column = column; this.changed = changed; }
    internal void Reset(double meters, double factor)
    {
        Meters = meters; Factor = factor; originalText = (meters / factor).ToString("G17", CultureInfo.InvariantCulture);
        if (text != originalText) { text = originalText; Notify(nameof(Text)); }
        error = ""; Notify(nameof(Error)); Notify(nameof(HasError));
    }
    public void Cancel() { text = originalText; error = ""; Notify(nameof(Text)); Notify(nameof(Error)); Notify(nameof(HasError)); changed(); }
    public double Value()
    {
        if (!IsPending) return Meters;
        Validate(); if (HasError) throw new ArgumentException($"Probe {Index + 1}: {Error}");
        return ProbeFormationDesignerViewModel.Number(text) * Factor;
    }
    private void Validate()
    {
        try {
            var n = ProbeFormationDesignerViewModel.Number(text) * Factor;
            error = !double.IsFinite(n) ? "Value is too large" : Column == 3 && n <= 0 ? "Radius must be positive" : "";
        } catch (ArgumentException) { error = "Enter a finite number"; }
        Notify(nameof(Error)); Notify(nameof(HasError));
    }
    private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class ProbeTableRow : INotifyPropertyChanged
{
    public int Index { get; }
    public int Number => Index + 1;
    public ProbeNumericCell X { get; }
    public ProbeNumericCell Y { get; }
    public ProbeNumericCell Z { get; }
    public ProbeNumericCell Radius { get; }
    public IEnumerable<ProbeNumericCell> Cells => [X, Y, Z, Radius];
    public bool IsSelected { get; private set; }
    public Avalonia.Media.IBrush Background => IsSelected ? SelectedBrush : Avalonia.Media.Brushes.Transparent;
    private static readonly Avalonia.Media.IBrush SelectedBrush = Avalonia.Media.Brush.Parse("#263F58");
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProbeTableRow(int index, Action changed) { Index = index; X = new(index,0,changed); Y = new(index,1,changed); Z = new(index,2,changed); Radius = new(index,3,changed); }
    internal void Select(bool value) { if (value == IsSelected) return; IsSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); PropertyChanged?.Invoke(this, new(nameof(Background))); }
}

public sealed partial class ProbeFormationDesignerViewModel
{
    private int[] selectedIndices = [];
    private bool committing;
    private bool snapToNearestInteger, dragSnap;
    private double dragSnapFactor;
    public bool SnapToNearestInteger { get => snapToNearestInteger; set { if (snapToNearestInteger == value) return; snapToNearestInteger = value; Notify(); } }
    public ObservableCollection<ProbeTableRow> ProbeRows { get; } = [];
    public IReadOnlyList<int> SelectedIndices => selectedIndices;
    public string SelectionSummary => $"{selectedIndices.Length} of {Current?.Probes.Count ?? 0} probes selected";
    public bool HasSelection => selectedIndices.Length > 0;
    public bool CanAddProbe => Current is { Probes.Count: < 8 };
    public bool CanDuplicateProbes => HasSelection && Current!.Probes.Count + selectedIndices.Length <= 8;
    public bool CanDeleteProbes => HasSelection && selectedIndices.Length < Current!.Probes.Count;
    public bool HasPendingEdits => ProbeRows.Any(r => r.Cells.Any(c => c.IsPending));
    public bool HasInvalidEdits => ProbeRows.Any(r => r.Cells.Any(c => c.HasError));
    public string SelectionRadius => Current is not { } f || !HasSelection ? "" : selectedIndices.Select(i => f.Probes[i].RadiusMeters).Distinct().Count() > 1 ? "Mixed" : (f.Probes[selectedIndices[0]].RadiusMeters / ProbeFormationMath.AstronomicalUnit).ToString("G17", CultureInfo.InvariantCulture);
    public string SelectionCenter => Current is { } f && HasSelection ? $"Selection center ({Units}): {ProbeFormationMath.SelectionCentroid(f, selectedIndices).X / UnitFactor:G6}, {ProbeFormationMath.SelectionCentroid(f, selectedIndices).Y / UnitFactor:G6}, {ProbeFormationMath.SelectionCentroid(f, selectedIndices).Z / UnitFactor:G6}" : "Select probes in the table or 3D view.";
    private void SelectAllCore() => SetSelectionCore(Current is { } f ? Enumerable.Range(0,f.Probes.Count) : [], 0);
    private void SetSelectionCore(IEnumerable<int> indices, int primary)
    {
        selectedIndices = indices.Where(i => i >= 0 && i < (Current?.Probes.Count ?? 0)).Distinct().Order().ToArray();
        selectedProbe = selectedIndices.Contains(primary) ? primary : selectedIndices.FirstOrDefault(-1);
    }
    public void SetSelection(IEnumerable<int> indices, int primary = -1)
    {
        CommitPendingEdits(); SetSelectionCore(indices, primary); NotifySelection();
    }
    public void SelectAll() => SetSelection(Current is { } f ? Enumerable.Range(0,f.Probes.Count) : []);
    public void ClearSelection() => SetSelection([]);
    public void SelectTableRow(int index, bool toggle, bool range)
    {
        if (range && selectedProbe >= 0) SetSelection(Enumerable.Range(Math.Min(index,selectedProbe), Math.Abs(index-selectedProbe)+1), selectedProbe);
        else if (toggle) SetSelection(selectedIndices.Contains(index) ? selectedIndices.Where(i => i != index) : selectedIndices.Append(index), index);
        else SetSelection([index], index);
    }
    private void NotifySelection()
    {
        foreach (var row in ProbeRows) row.Select(selectedIndices.Contains(row.Index));
        foreach (var name in new[] { nameof(SelectedIndices), nameof(SelectedProbe), nameof(SelectionSummary), nameof(SelectionRadius), nameof(SelectionCenter), nameof(HasSelection), nameof(CanAddProbe), nameof(CanDuplicateProbes), nameof(CanDeleteProbes) }) Notify(name);
    }
    private void DraftChanged() { Notify(nameof(HasPendingEdits)); Notify(nameof(HasInvalidEdits)); Notify(nameof(IsDirty)); }
    private void ClearProbeDrafts() { ProbeRows.Clear(); }
    private void SyncProbeRows()
    {
        var count = Current?.Probes.Count ?? 0;
        while (ProbeRows.Count > count) ProbeRows.RemoveAt(ProbeRows.Count-1);
        while (ProbeRows.Count < count) ProbeRows.Add(new(ProbeRows.Count, DraftChanged));
        for (var i = 0; i < count; i++) {
            var p = Current!.Probes[i]; var row = ProbeRows[i];
            var values = new[] {p.Position.X,p.Position.Y,p.Position.Z,p.RadiusMeters};
            foreach (var c in row.Cells) if (!c.IsPending || committing) c.Reset(values[c.Column], c.Column == 3 ? ProbeFormationMath.AstronomicalUnit : UnitFactor);
        }
        DraftChanged(); NotifySelection();
    }
    public void CommitPendingEdits()
    {
        if (committing || !HasPendingEdits || Current is not { } f) return;
        if (dragStart is not null) throw new ArgumentException("Finish moving probes before editing values.");
        var probes = f.Probes.ToArray();
        foreach (var row in ProbeRows) if (row.Cells.Any(c => c.IsPending)) probes[row.Index] = new(new(row.X.Value(),row.Y.Value(),row.Z.Value()),row.Radius.Value());
        committing = true;
        try { CommitChange(f with { Probes = probes }); SyncProbeRows(); }
        finally { committing = false; }
    }
    private void CommitChange(ProbeFormation changed, int[]? newSelection = null)
    {
        ProbeFormationMath.Validate(changed);
        if (selected is null) return;
        if (Current is { } before && before.Name == changed.Name && before.Probes.SequenceEqual(changed.Probes)) { if (newSelection is not null) SetSelectionCore(newSelection, newSelection.FirstOrDefault(-1)); return; }
        Remember(); selected.Formation = changed;
        SetSelectionCore(newSelection ?? selectedIndices, newSelection?.FirstOrDefault(-1) ?? selectedProbe); Refresh();
    }
    public void TransformSelected(Func<ProbeFormation, ProbeFormation> transform)
    {
        CommitPendingEdits(); if (Current is not { } f) return;
        CommitChange(ProbeFormationMath.TransformSelection(f, selectedIndices, transform));
    }
    public void SetSelectedRadius(double au)
    {
        if (!double.IsFinite(au) || au <= 0) throw new ArgumentException("Enter a positive scan radius in AU.");
        TransformSelected(f => f with { Probes = f.Probes.Select(p => p with { RadiusMeters = au * ProbeFormationMath.AstronomicalUnit }).ToArray() });
    }
    public void DuplicateSelected()
    {
        CommitPendingEdits(); if (!CanDuplicateProbes) throw new ArgumentException("Select probes that fit within the eight-probe limit.");
        var f = Current!; CommitChange(f with { Probes = f.Probes.Concat(selectedIndices.Select(i => f.Probes[i])).ToArray() }, Enumerable.Range(f.Probes.Count,selectedIndices.Length).ToArray());
    }
    public void DeleteSelected()
    {
        CommitPendingEdits(); if (!CanDeleteProbes) throw new ArgumentException("Select probes to delete and keep at least one probe.");
        var f = Current!; var next = Math.Min(selectedIndices[0], f.Probes.Count-selectedIndices.Length-1);
        CommitChange(f with { Probes = f.Probes.Where((_,i) => !selectedIndices.Contains(i)).ToArray() }, [next]);
    }
    public void BeginGroupDrag()
    {
        CommitPendingEdits(); if (!HasSelection) throw new ArgumentException("Select probes to move.");
        dragStart = State();
        dragSnap = SnapToNearestInteger; dragSnapFactor = UnitFactor;
    }
    public void UpdateGroupDrag(ProbePosition delta, ProbeAxis? axis = null)
    {
        if (dragStart is not { } start || selected is null) return;
        var original = start.Library.Formations.First(f => f.Id == start.Selected);
        if (dragSnap && delta != default) {
            var anchor = original.Probes[start.Probes.Contains(start.Primary) ? start.Primary : start.Probes[0]].Position;
            var target = anchor + delta;
            double Snap(double value) => Math.Round(value / dragSnapFactor, MidpointRounding.AwayFromZero) * dragSnapFactor;
            delta = new(
                axis is null or ProbeAxis.X ? Snap(target.X) - anchor.X : delta.X,
                axis is null or ProbeAxis.Y ? Snap(target.Y) - anchor.Y : delta.Y,
                axis is null or ProbeAxis.Z ? Snap(target.Z) - anchor.Z : delta.Z);
        }
        var changed = ProbeFormationMath.TransformSelection(original, start.Probes, f => ProbeFormationMath.Translate(f,delta));
        selected.Formation = changed;
        Notify(nameof(Current)); Notify(nameof(IsDirty)); SyncProbeRows();
    }
    public void EndGroupDrag(bool cancel)
    {
        if (dragStart is not { } start) return; dragStart = null;
        var original = start.Library.Formations.First(f => f.Id == start.Selected);
        if (selected is not null) {
            if (cancel) selected.Formation = original;
            else if (!original.Probes.SequenceEqual(selected.Formation.Probes)) { undo.Push(start); redo.Clear(); }
        }
        SetSelectionCore(start.Probes,start.Primary); Refresh();
    }
}
