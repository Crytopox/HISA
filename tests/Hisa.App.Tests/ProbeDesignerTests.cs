using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hisa.App;
using Hisa.Core.Abstractions;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;
using Hisa.Services.Probes;

[assembly: AvaloniaTestApplication(typeof(Hisa.App.Tests.TestApp))]
namespace Hisa.App.Tests;

public sealed class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class ProbeDesignerTests
{
    [AvaloniaFact]
    public async Task FormationListSwitchesReliablyAfterEditsRenamesAndFiltering()
    {
        var vm=Create(); var window=new ProbeFormationDesignerWindow(vm); window.Show();
        try {
            foreach(var name in new[]{"Alpha","Beta","Gamma"}) { vm.Create(); vm.FormationName=name; vm.Rename(); }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var list=window.FindControl<ListBox>("FormationList")!;
            var stableItems=list.ItemsSource;
            async Task Click(int index) {
                Point point=default; Control? item=null; ListBoxItem? hitRow=null;
                // The compositor updates hit-test geometry asynchronously after filtering.
                for(var attempt=0;attempt<30;attempt++) {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    using var frame=window.CaptureRenderedFrame();
                    item=(Control)list.ContainerFromIndex(index)!;
                    Assert.Same(vm.FilteredLibrary[index],item.DataContext);
                    var label=item.GetVisualDescendants().OfType<TextBlock>().First(t=>t.Text==vm.FilteredLibrary[index].Name);
                    point=label.TranslatePoint(new Point(label.Bounds.Width/2,label.Bounds.Height/2),window)!.Value;
                    hitRow=(window.InputHitTest(point) as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
                    if(ReferenceEquals(item,hitRow))break;
                    await Task.Delay(10);
                }
                Assert.Same(item,hitRow);
                window.MouseDown(point,MouseButton.Left); window.MouseUp(point,MouseButton.Left);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            for(var i=0;i<12;i++) {
                var index=i%3; var target=vm.Library[index];
                vm.ProbeRows[0].X.Text=(100+i).ToString();
                await Click(index);
                Assert.Same(target,vm.SelectedRow); Assert.Same(target,list.SelectedItem);
                Assert.Equal(target.Formation.Id,window.FindControl<ProbeFormationControl>("Viewport")!.Formation!.Id);
                Assert.Equal(target.Name,window.FindControl<TextBox>("NameEditor")!.Text);
                Assert.Same(stableItems,list.ItemsSource);
            }
            vm.Search="Beta"; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); await Click(0);
            Assert.Equal("Beta",vm.Current!.Name); vm.FormationName="Beta renamed";
            vm.Search=""; await Click(0); Assert.Equal("Beta renamed",vm.Library[1].Name); Assert.Same(vm.Library[0],list.SelectedItem);
            vm.ProbeRows[0].X.Text="invalid"; await Click(2);
            Assert.Same(vm.Library[0],vm.SelectedRow); Assert.Same(vm.SelectedRow,list.SelectedItem);
            vm.ProbeRows[0].X.Cancel(); await Click(2); Assert.Same(vm.Library[2],vm.SelectedRow);
        } finally { foreach(var r in vm.ProbeRows)foreach(var c in r.Cells)c.Cancel(); await vm.SaveAsync(); window.Close(); }
    }
    [Theory]
    [InlineData("km")] [InlineData("AU")]
    public async Task MouseSnappingUsesDisplayedUnitsPreservesSpacingAndSupportsAxisCancelAndUndo(string units)
    {
        var vm=Create(); await vm.InitializeAsync(); vm.Create(); vm.Units=units;
        var factor=vm.UnitFactor;
        vm.Edit(f=>f with { Probes=f.Probes.Select((p,i)=>p with { Position=new ProbePosition(i+1.25,-1.5,3.125)*factor }).ToArray() });
        vm.SetSelection([0,1],1); vm.SnapToNearestInteger=true; var original=vm.Current!;
        vm.BeginGroupDrag(); vm.UpdateGroupDrag(new ProbePosition(.12,.36,-.21)*factor); vm.EndGroupDrag(false);
        Assert.Equal(new ProbePosition(2,-1,3)*factor,vm.Current!.Probes[1].Position);
        Assert.Equal(original.Probes[1].Position-original.Probes[0].Position,vm.Current.Probes[1].Position-vm.Current.Probes[0].Position);
        Assert.Equal(original.Probes[2],vm.Current.Probes[2]);
        vm.Undo(); Assert.Equal(original.Probes,vm.Current!.Probes); vm.Redo();
        vm.Undo(); vm.BeginGroupDrag(); vm.UpdateGroupDrag(new ProbePosition(.4,0,0)*factor,ProbeAxis.X); vm.EndGroupDrag(false);
        Assert.Equal(3*factor,vm.Current!.Probes[1].Position.X);
        Assert.Equal(original.Probes[1].Position.Y,vm.Current.Probes[1].Position.Y);
        Assert.Equal(original.Probes[1].Position.Z,vm.Current.Probes[1].Position.Z);
        var moved=vm.Current; vm.BeginGroupDrag(); vm.UpdateGroupDrag(new ProbePosition(4,4,4)*factor); vm.EndGroupDrag(true);
        Assert.Equal(moved.Probes,vm.Current!.Probes); vm.Undo(); Assert.Equal(original.Probes,vm.Current!.Probes);
        vm.SnapToNearestInteger=false; vm.BeginGroupDrag(); vm.UpdateGroupDrag(new ProbePosition(.12,.36,-.21)*factor); vm.EndGroupDrag(false);
        Assert.Equal(original.Probes[1].Position+new ProbePosition(.12,.36,-.21)*factor,vm.Current!.Probes[1].Position);
    }
    [Fact]
    public async Task FormationCenterAndZeroAffectEveryProbePreserveRangesAndUndoInSingleSteps()
    {
        var vm=Create(); await vm.InitializeAsync(); vm.Create();
        vm.Edit(f=>ProbeFormationMath.Translate(f,new(12345,-67890,23456)));
        vm.SetSelection([3],3); var original=vm.Current!;
        vm.CenterFormationAtOrigin(); var centered=vm.Current!;
        Assert.InRange(centered.Centroid.Length,0,1e-8);
        Assert.Equal(original.Probes[1].Position-original.Probes[0].Position,centered.Probes[1].Position-centered.Probes[0].Position);
        Assert.Equal(new[]{3},vm.SelectedIndices);
        vm.SetAllProbesAtOrigin(); Assert.All(vm.Current!.Probes,p=>Assert.Equal(default,p.Position));
        Assert.Equal(original.Probes.Select(p=>p.RadiusMeters),vm.Current.Probes.Select(p=>p.RadiusMeters));
        vm.SetAllProbesAtOrigin(); vm.Undo(); Assert.Equal(centered.Probes,vm.Current!.Probes);
        vm.Undo(); Assert.Equal(original.Probes,vm.Current!.Probes);
        vm.ProbeRows[0].X.Text="invalid";
        Assert.Throws<ArgumentException>(vm.CenterFormationAtOrigin); Assert.Throws<ArgumentException>(vm.SetAllProbesAtOrigin);
    }
    [AvaloniaTheory]
    [InlineData(1550,880)]
    [InlineData(1050,720)]
    public async Task DefaultAndMinimumLayoutsKeepTableAndPrimaryActionsWithinWindow(int width,int height)
    {
        var vm=Create();var window=new ProbeFormationDesignerWindow(vm) {Width=width,Height=height};window.Show();vm.Create();
        try {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var cells=window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Tag is ProbeNumericCell).ToArray();Assert.Equal(32,cells.Length);
            foreach (var c in cells) {var point=c.TranslatePoint(default,window)!.Value;Assert.True(point.X>=0);Assert.True(point.X+c.Bounds.Width<=window.Bounds.Width+1);Assert.True(c.Bounds.Width>=30);}
            var send=window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString()=="Send to EVE…");
            Assert.True(send.TranslatePoint(default,window)!.Value.X+send.Bounds.Width<=window.Bounds.Width+1);
            using var image=new RenderTargetBitmap(new((int)window.Bounds.Width,(int)window.Bounds.Height));image.Render(window);image.Save(Path.Combine(AppContext.BaseDirectory,$"probe-designer-{width}.png"));
            await vm.SaveAsync();
        } finally {await vm.SaveAsync();window.Close();}
    }
    [AvaloniaFact]
    public async Task CellEnterFocusLossAndEscapeCommitOrCancelExactlyOneField()
    {
        var vm=Create();var window=new ProbeFormationDesignerWindow(vm);window.Show();vm.Create();
        try {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var cell=window.GetVisualDescendants().OfType<TextBox>().First(t => t.Tag is ProbeNumericCell c && c.Index==0 && c.Column==0);
            cell.Focus();cell.Text="123.125";window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);
            Assert.Equal(123125,vm.Current!.Probes[0].Position.X);Assert.False(vm.HasPendingEdits);
            cell.Text="invalid";window.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);Assert.False(vm.HasInvalidEdits);Assert.Equal(123125,vm.Current.Probes[0].Position.X);
            cell.Text="234.5";window.FindControl<ProbeFormationControl>("Viewport")!.Focus();Assert.Equal(234500,vm.Current.Probes[0].Position.X);
            vm.Undo();Assert.Equal(123125,vm.Current!.Probes[0].Position.X);await vm.SaveAsync();
        } finally {foreach(var r in vm.ProbeRows)foreach(var c in r.Cells)c.Cancel();await vm.SaveAsync();window.Close();}
    }
    [Fact]
    public async Task SelectionTransformsMixedRadiusAndStructuralEditsPreserveUndoSelection()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.Create();
        Assert.Equal(8,vm.SelectedIndices.Count);
        vm.SetSelection([1,3],3); var original = vm.Current!;
        vm.TransformSelected(s => ProbeFormationMath.Translate(s,new(100,200,300)));
        Assert.Equal(original.Probes[0],vm.Current!.Probes[0]);
        Assert.Equal(original.Probes[1].Position + new ProbePosition(100,200,300),vm.Current.Probes[1].Position);
        vm.Undo(); Assert.Equal(new[] {1,3},vm.SelectedIndices); Assert.Equal(3,vm.SelectedProbe); Assert.Equal(original.Probes,vm.Current!.Probes);
        vm.ProbeRows[1].Radius.Text = ".125"; vm.CommitPendingEdits(); Assert.Equal("Mixed",vm.SelectionRadius);
        vm.SetSelectedRadius(.5); Assert.Equal("0.5",vm.SelectionRadius); Assert.Equal(original.Probes[0],vm.Current.Probes[0]);
        Assert.False(vm.CanDuplicateProbes); vm.DeleteSelected(); Assert.Equal(6,vm.Current.Probes.Count); Assert.Single(vm.SelectedIndices);
        vm.Undo(); Assert.Equal(new[] {1,3},vm.SelectedIndices);
        vm.DeleteSelected(); vm.SetSelection([0,1],1); vm.DuplicateSelected(); Assert.Equal(new[] {6,7},vm.SelectedIndices); Assert.Equal(8,vm.Current.Probes.Count);
        Assert.False(vm.CanAddProbe); Assert.False(vm.CanDuplicateProbes);
        vm.SelectAll(); Assert.False(vm.CanDeleteProbes); Assert.Throws<ArgumentException>(vm.DeleteSelected);
    }
    [Fact]
    public async Task InvalidDraftBlocksDependentActionsAndCanBeCanceledWithoutLosingPrecision()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.Create(); vm.Duplicate(); var first = vm.Library[0];
        var original = vm.Current!; vm.SetSelection([0]); var cell = vm.ProbeRows[0].X;
        cell.Text = "-"; Assert.True(cell.HasError); Assert.True(vm.IsDirty);
        Assert.Throws<ArgumentException>(() => vm.SetSelection([1]));
        vm.Units = "AU"; Assert.Equal("km",vm.Units);
        vm.SelectedRow = first; Assert.Equal(original.Id,vm.Current!.Id);
        await Assert.ThrowsAsync<ArgumentException>(vm.SaveAsync);
        Assert.Throws<ArgumentException>(() => vm.Export(false));
        using (var transfer = vm.CreateAccountsViewModel()) await Assert.ThrowsAsync<ArgumentException>(transfer.BuildReviewAsync);
        cell.Cancel(); Assert.False(cell.HasError); Assert.False(vm.HasPendingEdits); Assert.Equal(original.Probes,vm.Current.Probes);
        vm.ProbeRows[0].X.Text = "250.0"; vm.CommitPendingEdits(); Assert.False(vm.HasPendingEdits);
        vm.ProbeRows[0].Y.Text = "123.456789"; vm.SetSelection([1]); Assert.Equal(123456.789,vm.Current.Probes[0].Position.Y,7);
        var meters = vm.Current.Probes[0]; vm.Units = "AU"; vm.Units = "km"; Assert.Equal(meters,vm.Current.Probes[0]);
    }
    [Fact]
    public async Task GroupDragUsesOriginalPositionsAndCancelOrNoOpDoesNotCreateUndoStep()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.Create(); vm.SetSelection([0,2],2);
        var original = vm.Current!; vm.BeginGroupDrag(); vm.UpdateGroupDrag(new(1,2,3)); vm.UpdateGroupDrag(new(4,5,6)); vm.EndGroupDrag(false);
        Assert.Equal(original.Probes[0].Position + new ProbePosition(4,5,6),vm.Current!.Probes[0].Position);
        Assert.Equal(original.Probes[2].Position - original.Probes[0].Position,vm.Current.Probes[2].Position-vm.Current.Probes[0].Position);
        Assert.Equal(original.Probes[1],vm.Current.Probes[1]);
        vm.BeginGroupDrag(); vm.UpdateGroupDrag(new(100,0,0)); vm.EndGroupDrag(true);
        vm.BeginGroupDrag(); vm.EndGroupDrag(false);
        vm.Undo(); Assert.Equal(original.Probes,vm.Current!.Probes); Assert.Equal(new[] {0,2},vm.SelectedIndices); Assert.Equal(2,vm.SelectedProbe);
        vm.Redo(); Assert.Equal(original.Probes[0].Position + new ProbePosition(4,5,6),vm.Current!.Probes[0].Position);
    }
    [AvaloniaFact]
    public async Task TableAndViewportStaySynchronizedAndAccountWindowIsOwnedAndReused()
    {
        var vm = Create(); var window = new ProbeFormationDesignerWindow(vm); window.Show(); vm.Create();
        try {
            vm.SetSelection([1,4],4); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var view = window.FindControl<ProbeFormationControl>("Viewport")!;
            Assert.Equal(new[] {1,4},view.SelectedIndices); Assert.True(vm.ProbeRows[1].IsSelected); Assert.False(vm.ProbeRows[0].IsSelected);
            vm.SelectTableRow(6,false,true); Assert.Equal(new[] {4,5,6},view.SelectedIndices);
            vm.SelectTableRow(5,true,false); Assert.Equal(new[] {4,6},view.SelectedIndices);
            window.OpenEveAccounts(); Assert.NotNull(window.AccountsWindow); Assert.Equal(window,window.AccountsWindow.Owner);
            var account = window.AccountsWindow; window.OpenEveAccounts(); Assert.Same(account,window.AccountsWindow);
            account.Close(); Assert.Null(window.AccountsWindow);
            await vm.SaveAsync();
        } finally { await vm.SaveAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task NumericFieldsCanBeRefocusedAfterInvalidInputAndKeyboardShortcutsRespectTextFocus()
    {
        var vm = Create(); var window = new ProbeFormationDesignerWindow(vm); window.Show(); vm.Create();
        try {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var cellBox = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Tag is ProbeNumericCell c && c.Index == 0 && c.Column == 0);
            vm.SetSelection([2]); cellBox.Focus(); vm.ProbeRows[0].X.Text = "invalid";
            window.FindControl<TextBox>("NameEditor")!.Focus();
            var point = cellBox.TranslatePoint(new Point(10,10),window)!.Value;
            window.MouseDown(point,MouseButton.Left); window.MouseUp(point,MouseButton.Left); Assert.True(cellBox.IsFocused);
            window.KeyPressQwerty(PhysicalKey.A,RawInputModifiers.Control); Assert.Equal(new[] {2},vm.SelectedIndices);
            vm.ProbeRows[0].X.Cancel(); vm.SetSelection([0]);
            viewFocus(); window.KeyPressQwerty(PhysicalKey.A,RawInputModifiers.Control); Assert.Equal(8,vm.SelectedIndices.Count);
            await vm.SaveAsync();
            void viewFocus() => window.FindControl<ProbeFormationControl>("Viewport")!.Focus();
        } finally { vm.ProbeRows[0].X.Cancel(); await vm.SaveAsync(); window.Close(); }
    }
    private static ProbeFormationDesignerViewModel Create(MemorySettings? settings = null)
    {
        settings ??= new(); return new(new ProbeFormationLibrary(settings), new EmptyEveSettings(), settings);
    }
    [Fact]
    public async Task UnitsDoNotMutatePreciseMeterValuesAndUndoRedoCoalesceDrags()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.Create();
        vm.Edit(f => f with { Probes = [new(new(123456.78912345678, -1, .125), 123.456)] });
        var precise = vm.Current!;
        vm.Units = "AU"; vm.Units = "km"; Assert.Equal(precise.Probes, vm.Current!.Probes);
        vm.SetSelection([0]); vm.BeginGroupDrag(); vm.UpdateGroupDrag(new ProbePosition(5,6,7)-precise.Probes[0].Position); vm.UpdateGroupDrag(new ProbePosition(8,9,10)-precise.Probes[0].Position); vm.EndGroupDrag(false);
        vm.Undo(); Assert.Equal(precise.Probes, vm.Current!.Probes);
        vm.Redo(); Assert.Equal(new ProbePosition(8, 9, 10), vm.Current!.Probes[0].Position);
        await vm.SaveAsync(); Assert.False(vm.IsDirty);
        vm.Duplicate(); Assert.True(vm.IsDirty); vm.Undo(); Assert.False(vm.IsDirty);
    }
    [Fact]
    public async Task TableAndLibraryImportMaintainSelectionAndValidation()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.Create();
        vm.SelectedProbe = 2; vm.ProbeRows[2].X.Text = "123.456789"; vm.ProbeRows[2].Radius.Text = "0.125"; vm.CommitPendingEdits();
        Assert.Equal(123456.789, vm.Current!.Probes[2].Position.X, 7);
        Assert.Equal(.125 * ProbeFormationMath.AstronomicalUnit, vm.Current.Probes[2].RadiusMeters);
        var before = vm.Export(true); vm.ProbeRows[2].X.Text = "NaN"; Assert.Throws<ArgumentException>(vm.CommitPendingEdits); vm.ProbeRows[2].X.Cancel(); Assert.Equal(before, vm.Export(true));
        vm.Import(before, FormationImportAction.RenamedCopy); Assert.Equal(2, vm.Library.Count);
    }
    [AvaloniaFact]
    public async Task WindowBindsCoordinatesSelectionAndLifecycle()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.Create(); await vm.SaveAsync();
        var window = new ProbeFormationDesignerWindow(vm);
        try {
            window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var viewport = window.FindControl<ProbeFormationControl>("Viewport")!;
            Assert.NotNull(viewport.Formation); Assert.Equal(vm.Current!.Id, viewport.Formation.Id);
            viewport.FitToView();
            using (var preview = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height))) { preview.Render(window); preview.Save(Path.Combine(AppContext.BaseDirectory, "probe-designer-preview.png")); }
            vm.SelectedProbe = 3; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Equal(3, viewport.SelectedProbe);
            vm.ProbeRows[3].X.Text = "222"; vm.CommitPendingEdits(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.Current); Assert.NotNull(viewport.Formation);
            Assert.Equal(222000, viewport.Formation.Probes[3].Position.X);
            Assert.True(vm.IsDirty);
            vm.Undo(); await vm.SaveAsync();
        } finally { window.Close(); }
        Assert.False(window.IsVisible);
    }
    [AvaloniaFact]
    public async Task ReopeningUsesSavedLibraryAndFreshViewModel()
    {
        var settings = new MemorySettings(); var vm = Create(settings); await vm.InitializeAsync(); vm.Create(); await vm.SaveAsync();
        var first = new ProbeFormationDesignerWindow(vm); first.Show(); first.Close();
        var next = Create(settings); await next.InitializeAsync(); var window = new ProbeFormationDesignerWindow(next);
        try { window.Show(); Assert.Single(next.Library); Assert.Equal(vm.Current!.Id, next.Current!.Id); } finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task UnsavedCloseCanBeCanceledOrDiscarded()
    {
        var vm = Create(); var window = new ProbeFormationDesignerWindow(vm); window.Show(); vm.Create();
        window.Close(); Assert.True(window.IsVisible);
        var dialog = Assert.Single(window.OwnedWindows); dialog.Close("Cancel");
        await Task.Yield(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(window.IsVisible);
        window.Close(); dialog = Assert.Single(window.OwnedWindows); dialog.Close("Discard");
        await Task.Yield(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.False(window.IsVisible);
    }
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(values.TryGetValue(key, out var json) ? System.Text.Json.JsonSerializer.Deserialize<T>(json) : default);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) { values[key] = System.Text.Json.JsonSerializer.Serialize(value); return Task.CompletedTask; }
    }
    private sealed class EmptyEveSettings : IEveProbeSettingsService
    {
        public Task<IReadOnlyList<EveSettingsAccount>> DiscoverAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EveSettingsAccount>>([]);
        public EveClientStatus GetClientStatus() => EveClientStatus.Closed;
        public Task<EveFormationSnapshot> ReadAsync(EveSettingsAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetFingerprintAsync(EveSettingsAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public FormationMergePreview Preview(EveFormationSnapshot snapshot, IReadOnlyList<ProbeFormation> formations, IReadOnlyList<FormationMergeChoice> choices) => throw new NotSupportedException();
        public Task<EveSettingsApplyResult> ApplyAsync(FormationMergePreview preview, bool clientClosedAcknowledged, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EveSettingsBackup>> ListBackupsAsync(EveSettingsAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EveSettingsApplyResult> RestoreAsync(EveSettingsAccount account, EveSettingsBackup backup, string expectedFingerprint, bool clientClosedAcknowledged, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
