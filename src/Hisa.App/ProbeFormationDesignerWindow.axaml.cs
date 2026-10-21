using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Hisa.Core.Models;
using Hisa.Rendering.Controls;

namespace Hisa.App;

public partial class ProbeFormationDesignerWindow : Window
{
    private bool allowClose, closingDialog, selectingFormation;
    private Guid? viewportFormationId;
    private ProbeFormationDesignerViewModel? Vm => DataContext as ProbeFormationDesignerViewModel;
    public ProbeFormationAccountsWindow? AccountsWindow { get; private set; }
    public ProbeFormationDesignerWindow()
    {
        InitializeComponent();
        ProbeTable.AddHandler(PointerPressedEvent, OnTablePointerPressed, RoutingStrategies.Tunnel);
        FormationList.AddHandler(PointerPressedEvent, OnFormationPointerPressed, RoutingStrategies.Tunnel);
    }
    public ProbeFormationDesignerWindow(ProbeFormationDesignerViewModel vm) : this()
    {
        DataContext = vm; Opened += OnOpened; Closing += OnClosing; KeyDown += OnKey;
        vm.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelChanged;
        Viewport.SelectionChanged += (_, change) => {
            try { CommitEditor(); vm.SetSelection(change.Indices,change.Primary); }
            catch (ArgumentException ex) { change.Cancel = true; vm.Status = ex.Message; }
        };
        Viewport.GroupDrag += (_, change) => {
            try {
                switch (change.Phase) {
                    case ProbeDragPhase.Started: CommitEditor(); vm.BeginGroupDrag(); break;
                    case ProbeDragPhase.Updated: vm.UpdateGroupDrag(change.Delta,change.Axis); break;
                    case ProbeDragPhase.Completed: vm.EndGroupDrag(false); break;
                    case ProbeDragPhase.Canceled: vm.EndGroupDrag(true); break;
                }
            } catch (ArgumentException ex) { change.Cancel = true; vm.Status = ex.Message; }
        };
    }
    private void CommitEditor()
    {
        if (Vm is not { } vm) return;
        vm.CommitPendingEdits();
        if (vm.Current is { } f && vm.FormationName != f.Name) vm.Rename();
    }
    private void Edit(Action<ProbeFormationDesignerViewModel> action) { if (Vm is { } vm) vm.Run(() => { Viewport.CancelGesture(); CommitEditor(); action(vm); }); }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProbeFormationDesignerViewModel.Current) && Vm?.Current?.Id != viewportFormationId) {
            viewportFormationId = Vm?.Current?.Id;
            Avalonia.Threading.Dispatcher.UIThread.Post(Viewport.FitToView);
        }
    }
    private void OnFormationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (selectingFormation || Vm is not { } vm || vm.IsUpdatingFilteredLibrary || sender is not ListBox list) return;
        selectingFormation = true;
        try {
            if (list.SelectedItem is ProbeFormationLibraryRow row) vm.SelectedRow = row;
            // A rejected switch (invalid draft) must also restore the highlight.
            list.SetCurrentValue(ListBox.SelectedItemProperty, vm.LibrarySelection);
        } finally { selectingFormation = false; }
    }
    private void OnFormationPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || !e.GetCurrentPoint(FormationList).Properties.IsLeftButtonPressed || e.Source is not Visual source) return;
        var row = source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().Select(c => c.DataContext).OfType<ProbeFormationLibraryRow>().FirstOrDefault();
        if (row is null) return;
        Viewport.CancelGesture();
        vm.SelectedRow = row;
        if (vm.SelectedRow != row) e.Handled = true;
    }
    private async void OnOpened(object? sender, EventArgs e) { if (Vm is { } vm) { await vm.RunAsync(vm.InitializeAsync); Viewport.FitToView(); } }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose || Vm is not { } vm) return;
        if(AccountsWindow?.DataContext is ProbeFormationAccountsViewModel{IsWriting:true}){e.Cancel=true;vm.Status="Wait for the EVE settings write to finish before closing.";return;}
        if (vm.IsBusy) { e.Cancel = true; vm.Status = "Wait for the current operation to finish before closing."; return; }
        Viewport.CancelGesture();
        try { CommitEditor(); } catch (ArgumentException ex) { vm.Status = ex.Message; }
        if (!vm.IsDirty && vm.FormationName == (vm.Current?.Name ?? "")) return;
        e.Cancel = true; if (closingDialog) return; closingDialog = true;
        try {
            var result = await Ask("Unsaved library", "Save formation library changes before closing?", "Save", "Discard", "Cancel");
            if (result == "Save") { await vm.RunAsync(async () => { CommitEditor(); await vm.SaveAsync(); }); if (vm.IsDirty || vm.FormationName != vm.Current?.Name) return; }
            if (result is "Save" or "Discard") { allowClose = true; AccountsWindow?.Close(); Close(); }
        } finally { closingDialog = false; }
    }
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Vm is not { IsBusy: false }) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { OnSave(sender,new()); e.Handled = true; return; }
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
            switch (e.Key) {
                case Key.A: Edit(vm => vm.SelectAll()); e.Handled = true; break;
                case Key.D: Edit(vm => vm.DuplicateSelected()); e.Handled = true; break;
                case Key.Z: Edit(vm => vm.Undo()); e.Handled = true; break;
                case Key.Y: Edit(vm => vm.Redo()); e.Handled = true; break;
            }
        } else if (e.Key == Key.Delete) { Edit(vm => vm.DeleteSelected()); e.Handled = true; }
    }
    private void OnTablePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || !e.GetCurrentPoint(ProbeTable).Properties.IsLeftButtonPressed || e.Source is not Visual source) return;
        var row = source.GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<ProbeTableRow>().FirstOrDefault();
        if (row is null) return;
        try { CommitEditor(); vm.SelectTableRow(row.Index,e.KeyModifiers.HasFlag(KeyModifiers.Control),e.KeyModifiers.HasFlag(KeyModifiers.Shift)); }
        catch (ArgumentException ex) {
            vm.Status = ex.Message;
            // Invalid drafts block selection changes, but the fields must remain focusable for correction.
            e.Handled = !source.GetSelfAndVisualAncestors().OfType<TextBox>().Any(t => t.Tag is ProbeNumericCell);
        }
    }
    private void OnCellKey(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: ProbeNumericCell cell }) return;
        if (e.Key == Key.Escape) { cell.Cancel(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Vm?.Run(() => Vm.CommitPendingEdits()); e.Handled = true; }
    }
    private void OnCellLostFocus(object? sender, RoutedEventArgs e) => Vm?.Run(() => Vm.CommitPendingEdits());
    private void OnNameKey(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Escape) { NameEditor.Text = vm.Current?.Name ?? ""; vm.FormationName = NameEditor.Text; e.Handled = true; }
        else if (e.Key == Key.Enter) { vm.Run(CommitEditor); e.Handled = true; }
    }
    private void OnNameLostFocus(object? sender, RoutedEventArgs e) => Vm?.Run(CommitEditor);
    private async void OnSave(object? s, RoutedEventArgs e) { if (Vm is { } vm) await vm.RunAsync(async () => { Viewport.CancelGesture(); CommitEditor(); await vm.SaveAsync(); }); }
    private void OnNew(object? s, RoutedEventArgs e) => Edit(vm => vm.Create());
    private void OnDuplicate(object? s, RoutedEventArgs e) => Edit(vm => vm.Duplicate());
    private async void OnDelete(object? s, RoutedEventArgs e) { if (Vm is { Current: { } f } && await Ask("Delete formation", $"Delete '{f.Name}' from the library?", "Delete", "Cancel") == "Delete") Edit(vm => vm.Delete()); }
    private void OnUndo(object? s, RoutedEventArgs e) => Edit(vm => vm.Undo());
    private void OnRedo(object? s, RoutedEventArgs e) => Edit(vm => vm.Redo());
    private void OnAddProbe(object? s, RoutedEventArgs e) => Edit(vm => vm.AddProbe());
    private void OnDuplicateProbes(object? s, RoutedEventArgs e) => Edit(vm => vm.DuplicateSelected());
    private void OnDeleteProbes(object? s, RoutedEventArgs e) => Edit(vm => vm.DeleteSelected());
    private void OnSelectAll(object? s, RoutedEventArgs e) => Edit(vm => vm.SelectAll());
    private void OnClear(object? s, RoutedEventArgs e) => Edit(vm => vm.ClearSelection());
    private void OnCenter(object? s, RoutedEventArgs e) => Edit(vm => vm.TransformSelected(ProbeFormationMath.Center));
    private void OnCenterFormation(object? s, RoutedEventArgs e) => Edit(vm => vm.CenterFormationAtOrigin());
    private void OnZeroAllProbes(object? s, RoutedEventArgs e) => Edit(vm => vm.SetAllProbesAtOrigin());
    private void OnTranslate(object? s, RoutedEventArgs e) => Edit(vm => vm.TransformSelected(f => ProbeFormationMath.Translate(f, new ProbePosition(ProbeFormationDesignerViewModel.Number(DeltaX.Text ?? ""),ProbeFormationDesignerViewModel.Number(DeltaY.Text ?? ""),ProbeFormationDesignerViewModel.Number(DeltaZ.Text ?? ""))*vm.UnitFactor)));
    private void OnRotate(object? s, RoutedEventArgs e) => Edit(vm => vm.TransformSelected(f => ProbeFormationMath.Rotate(f,(ProbeAxis)RotationAxis.SelectedIndex,ProbeFormationDesignerViewModel.Number(Angle.Text ?? ""),Pivot.SelectedIndex == 0)));
    private void OnScale(object? s, RoutedEventArgs e) => Edit(vm => vm.TransformSelected(f => ProbeFormationMath.Scale(f,ProbeFormationDesignerViewModel.Number(ScaleFactor.Text ?? ""),ScalePivot.SelectedIndex == 0)));
    private void OnBulkRadius(object? s, RoutedEventArgs e) => Edit(vm => { vm.SetSelectedRadius(ProbeFormationDesignerViewModel.Number(BulkRadius.Text ?? "")); BulkRadius.Text = ""; });
    private void OnRadiusChoice(object? s, SelectionChangedEventArgs e) { if (RadiusChoices.SelectedItem is double r) { Edit(vm => vm.SetSelectedRadius(r)); RadiusChoices.SelectedIndex = -1; } }
    private void OnFit(object? s, RoutedEventArgs e) => Viewport.FitToView();
    private void OnFitSelection(object? s, RoutedEventArgs e) => Viewport.FitSelection();
    private void OnFitRange(object? s, RoutedEventArgs e) { Viewport.ShowRanges = true; Viewport.FitRange(); }
    private void OnView(object? s, RoutedEventArgs e) { if (s is MenuItem b) Viewport.SetView(b.Header?.ToString() ?? "Perspective"); }
    public void OpenEveAccounts()
    {
        if (Vm is not { } vm) return;
        vm.Run(() => {
            CommitEditor();
            if (AccountsWindow is null) { AccountsWindow = new(vm); AccountsWindow.Closed += (_, _) => AccountsWindow = null; AccountsWindow.Show(this); }
            AccountsWindow.Activate();
        });
    }
    private void OnSendToEve(object? s, RoutedEventArgs e) => OpenEveAccounts();
    private async void OnImport(object? s, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        await vm.RunAsync(async () => {
            CommitEditor();
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Import HISA formations", AllowMultiple = false, FileTypeFilter = [new("Formation JSON") { Patterns = ["*.json"] }] });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync(); using var reader = new StreamReader(stream);
            var buffer = new char[8*1024*1024+1]; var length = 0;
            while (length < buffer.Length) { var read = await reader.ReadAsync(buffer.AsMemory(length)); if (read == 0) break; length += read; }
            if (length == buffer.Length) throw new IOException("Formation import is too large.");
            var policy = await Ask("Import formations", "If an imported name already exists in the library:", "Renamed copies", "Skip existing", "Replace existing", "Cancel");
            if (policy is null or "Cancel") return;
            vm.Import(new(buffer,0,length), policy switch { "Skip existing" => FormationImportAction.Skip, "Replace existing" => FormationImportAction.Replace, _ => FormationImportAction.RenamedCopy });
        });
    }
    private async void OnExport(object? s, RoutedEventArgs e) => await ExportAsync(false);
    private async void OnExportAll(object? s, RoutedEventArgs e) => await ExportAsync(true);
    private async Task ExportAsync(bool all)
    {
        if (Vm is not { } vm) return;
        await vm.RunAsync(async () => {
            CommitEditor(); var json = vm.Export(all);
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export formations", SuggestedFileName = "hisa-formations.json", DefaultExtension = "json", FileTypeChoices = [new("Formation JSON") { Patterns = ["*.json"] }] });
            if (file is null) return; await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(json); vm.Status = "Formations exported.";
        });
    }
    private async Task<string?> Ask(string title, string text, params string[] buttons)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) }; panel.Children.Add(new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window { Title = title, Width = 570, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel, Background = Avalonia.Media.Brush.Parse("#141D2B") };
        foreach (var label in buttons) { var button = new Button { Content = label, Margin = new Thickness(3), Padding = new Thickness(10,6) }; button.Click += (_, _) => dialog.Close(label); actions.Children.Add(button); }
        panel.Children.Add(actions); return await dialog.ShowDialog<string?>(this);
    }
}
