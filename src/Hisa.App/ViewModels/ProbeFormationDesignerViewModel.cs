using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class ProbeFormationLibraryRow(ProbeFormation formation) : INotifyPropertyChanged
{
    private ProbeFormation formation = formation;
    public ProbeFormation Formation { get => formation; set { formation = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
    public string Name => Formation.Name;
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed partial class ProbeFormationDesignerViewModel : INotifyPropertyChanged
{
    private readonly IProbeFormationLibrary libraryService;
    private readonly IEveProbeSettingsService eve;
    private readonly ISettingsService settings;
    private readonly IEveSettingsIdentityService? identityService;
    private readonly IEveCharacterResolver? characterResolver;
    private sealed record EditorState(FormationLibrary Library, Guid? Selected, int[] Probes, int Primary);
    private readonly Stack<EditorState> undo = [], redo = [];
    private string saved = "", search = "", status = "Ready", units = "km";
    private ProbeFormationLibraryRow? selected;
    private int selectedProbe;
    private EditorState? dragStart;
    private bool busy;
    private bool changingSelection, updatingFilteredLibrary;
    internal bool IsUpdatingFilteredLibrary => updatingFilteredLibrary;
    public ProbeFormationDesignerViewModel(IProbeFormationLibrary libraryService, IEveProbeSettingsService eve, ISettingsService settings,IEveSettingsIdentityService? identityService=null,IEveCharacterResolver? characterResolver=null)
    { this.libraryService = libraryService; this.eve = eve; this.settings = settings;this.identityService=identityService;this.characterResolver=characterResolver; }
    public ProbeFormationAccountsViewModel CreateAccountsViewModel()=>new(this,eve,settings,identityService??new Hisa.Services.Probes.EveSettingsIdentityService(settings),characterResolver??new LocalOnlyResolver());
    private sealed class LocalOnlyResolver:IEveCharacterResolver
    {
        public Task<EveCharacterIdentity> ResolveAsync(string server,long characterId,CancellationToken cancellationToken=default)=>Task.FromResult(new EveCharacterIdentity(server,characterId,null,"Name service unavailable"));
        public Task<byte[]?> GetPortraitAsync(string server,long characterId,CancellationToken cancellationToken=default)=>Task.FromResult<byte[]?>(null);
    }
    public void AddTransferredFormation(ProbeFormation formation)
    {
        CommitPendingEdits();ProbeFormationMath.Validate(formation);Remember();var row=new ProbeFormationLibraryRow(formation with{Id=Guid.NewGuid(),Name=UniqueName(formation.Name),Probes=formation.Probes.ToArray()});Library.Add(row);selected=row;SelectAllCore();Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ProbeFormationLibraryRow> Library { get; } = [];
    public IReadOnlyList<ProbeFormationLibraryRow> FilteredLibrary { get; private set; } = [];
    public string Search { get => search; set { if (search == value) return; search = value; Notify(); RefreshFilteredLibrary(); } }
    private void RefreshFilteredLibrary()
    {
        var visible = Library.Where(r => r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (FilteredLibrary.SequenceEqual(visible)) return;
        updatingFilteredLibrary = true;
        try {
            FilteredLibrary = visible;
            Notify(nameof(FilteredLibrary));
        } finally { updatingFilteredLibrary = false; }
        Notify(nameof(LibrarySelection));
    }
    public string Status { get => status; set { status = value; Notify(); } }
    public bool IsBusy { get => busy; private set { busy = value; Notify(); Notify(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public bool IsDirty => HasPendingEdits || saved != libraryService.Export(Library.Select(r => r.Formation));
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public ProbeFormation? Current => SelectedRow?.Formation;
    // A filtered-out document remains open, while the list has no highlighted row.
    public ProbeFormationLibraryRow? LibrarySelection { get => selected is not null && FilteredLibrary.Contains(selected) ? selected : null; set => SelectedRow = value; }
    public ProbeFormationLibraryRow? SelectedRow
    {
        get => selected;
        set
        {
            // ListBox temporarily clears selection while a filtered source is refreshed.
            // Keep the current editor document until an actual replacement is selected.
            if (changingSelection || updatingFilteredLibrary || selected == value || value is null && Library.Count > 0 || value is not null && !Library.Contains(value)) return;
            changingSelection = true;
            try {
                CommitPendingEdits(); if (Current is { } f && FormationName != f.Name) Rename();
                selected = value; ClearProbeDrafts(); SelectAllCore(); Refresh();
            }
            catch (ArgumentException ex) { Status = ex.Message; Notify(); Notify(nameof(LibrarySelection)); }
            finally { changingSelection = false; }
        }
    }
    public int SelectedProbe { get => selectedProbe; set => SetSelection(value >= 0 ? [value] : [], value); }
    public string Units { get => units; set { if (value is not ("km" or "AU") || units == value) return; try { CommitPendingEdits(); } catch (ArgumentException ex) { Status = ex.Message; Notify(); return; } units = value; Notify(); SyncProbeRows(); } }
    public string[] UnitOptions { get; } = ["km", "AU"];
    public IReadOnlyList<double> StandardRadii => ProbeFormationMath.StandardRadiiAu;
    public string FormationName { get; set; } = "";
    public string CentroidText => Current is { } f ? $"Centroid ({Units}): {Format(f.Centroid.X / UnitFactor)}, {Format(f.Centroid.Y / UnitFactor)}, {Format(f.Centroid.Z / UnitFactor)}" : "";
    public double UnitFactor => Units == "AU" ? ProbeFormationMath.AstronomicalUnit : 1000;
    public async Task InitializeAsync()
    {
        var library = await libraryService.LoadAsync(); Load(library, library.Formations.FirstOrDefault()?.Id);
        saved = libraryService.Export(library.Formations);
        Notify(nameof(IsDirty));
    }
    public async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return; IsBusy = true;
        try { await action(); } catch (Exception ex) { Status = ex.Message; } finally { IsBusy = false; }
    }
    public void Run(Action action) { try { action(); } catch (Exception ex) { Status = ex.Message; } }
    private FormationLibrary Snapshot() => new(1, "meters", Library.Select(r => r.Formation with { Probes = r.Formation.Probes.ToArray() }).ToArray());
    private EditorState State() => new(Snapshot(), Current?.Id, SelectedIndices.ToArray(), selectedProbe);
    private void Remember() { undo.Push(State()); redo.Clear(); }
    private void Load(FormationLibrary library, Guid? id, int[]? probes = null, int primary = -1)
    {
        ClearProbeDrafts();
        Library.Clear(); foreach (var f in library.Formations) Library.Add(new(f));
        selected = Library.FirstOrDefault(r => r.Formation.Id == id) ?? Library.FirstOrDefault();
        if (probes is null) SelectAllCore(); else SetSelectionCore(probes, primary);
        Refresh();
    }
    private void Refresh()
    {
        FormationName = Current?.Name ?? "";
        RefreshFilteredLibrary();
        foreach (var name in new[] { nameof(Current), nameof(SelectedRow), nameof(LibrarySelection), nameof(SelectedProbe), nameof(FormationName), nameof(CentroidText), nameof(CanUndo), nameof(CanRedo), nameof(IsDirty) }) Notify(name);
        SyncProbeRows(); NotifySelection();
    }
    private static string Format(double n) => n.ToString("G17", CultureInfo.InvariantCulture);
    public static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new ArgumentException("Enter a finite number using a decimal point.");
    public void Create() { CommitPendingEdits(); var f = ProbeFormationMath.Create(UniqueName("New formation")); Remember(); var row = new ProbeFormationLibraryRow(f); Library.Add(row); selected = row; SelectAllCore(); Refresh(); }
    public void Duplicate() { CommitPendingEdits(); if (Current is not { } f) return; Remember(); var row = new ProbeFormationLibraryRow(f with { Id = Guid.NewGuid(), Name = UniqueName(f.Name + " copy"), Probes = f.Probes.ToArray() }); Library.Add(row); selected = row; SelectAllCore(); Refresh(); }
    private string UniqueName(string name) { var result = name; var i = 2; while (Library.Any(r => r.Name.Equals(result, StringComparison.OrdinalIgnoreCase))) result = $"{name} ({i++})"; return result; }
    public void Delete() { CommitPendingEdits(); if (selected is null) return; Remember(); Library.Remove(selected); selected = Library.FirstOrDefault(); SelectAllCore(); Refresh(); }
    public void Edit(Func<ProbeFormation, ProbeFormation> edit)
    {
        CommitPendingEdits(); if (selected is null) return; CommitChange(edit(selected.Formation));
    }
    public void Rename() => Edit(f => f with { Name = FormationName.Trim() });
    public void CenterFormationAtOrigin() => Edit(ProbeFormationMath.Center);
    public void SetAllProbesAtOrigin() => Edit(f => f with { Probes = f.Probes.Select(p => p with { Position = default }).ToArray() });
    public void AddProbe() { CommitPendingEdits(); if (Current is null) return; if (!CanAddProbe) throw new ArgumentException("Maximum eight probes."); CommitChange(Current with { Probes = Current.Probes.Append(new(default, 32 * ProbeFormationMath.AstronomicalUnit)).ToArray() }, [Current.Probes.Count]); }
    public void RemoveProbe() => DeleteSelected();
    public void Undo() { CommitPendingEdits(); if (undo.TryPop(out var state)) { redo.Push(State()); Load(state.Library, state.Selected, state.Probes, state.Primary); } }
    public void Redo() { CommitPendingEdits(); if (redo.TryPop(out var state)) { undo.Push(State()); Load(state.Library, state.Selected, state.Probes, state.Primary); } }
    public async Task SaveAsync() { CommitPendingEdits(); var library = Snapshot(); await libraryService.SaveAsync(library); saved = libraryService.Export(library.Formations); Notify(nameof(IsDirty)); Status = "Formation library saved."; }
    public string Export(bool all) { CommitPendingEdits(); return libraryService.Export(all ? Library.Select(r => r.Formation) : Current is { } f ? [f] : []); }
    public void Import(string json, FormationImportAction action) { CommitPendingEdits(); var imported = libraryService.Import(json, Snapshot(), action); Remember(); Load(imported, Current?.Id); Status = "Imported into the library. Save to keep these changes."; }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
