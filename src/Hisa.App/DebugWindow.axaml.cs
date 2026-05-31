using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Specialized;

namespace Hisa.App;

public partial class DebugWindow : Window
{
    private readonly DebugWindowViewModel? _boundVm;

    public DebugWindow()
    {
        InitializeComponent();
    }

    public DebugWindow(DebugWindowViewModel vm) : this()
    {
        _boundVm = vm;
        DataContext = vm;
        vm.Entries.CollectionChanged += OnEntriesCollectionChanged;
        vm.EsiEntries.CollectionChanged += OnEsiEntriesCollectionChanged;
        Closed += OnClosed;
    }

    private async void OnExportLogsClicked(object? sender, RoutedEventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        var path = await _boundVm.ExportLogsAsync();
        await MessageBox($"Logs exported to:\n{path}");
    }

    private void OnOpenLogsFolderClicked(object? sender, RoutedEventArgs e)
    {
        _boundVm?.OpenLogsFolder();
    }

    private async Task MessageBox(string text)
    {
        var dialog = new Window
        {
            Title = "Logs",
            Width = 520,
            Height = 160,
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(16)
            }
        };
        await dialog.ShowDialog(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_boundVm is null)
        {
            return;
        }

        _boundVm.Entries.CollectionChanged -= OnEntriesCollectionChanged;
        _boundVm.EsiEntries.CollectionChanged -= OnEsiEntriesCollectionChanged;
        Closed -= OnClosed;
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_boundVm is null || !_boundVm.AutoScroll || _boundVm.Entries.Count == 0 || e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        var last = _boundVm.Entries[^1];
        DebugLogGrid.ScrollIntoView(last, null);
    }

    private void OnEsiEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_boundVm is null || !_boundVm.AutoScroll || _boundVm.EsiEntries.Count == 0 || e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        var last = _boundVm.EsiEntries[^1];
        DebugEsiGrid.ScrollIntoView(last, null);
    }
}
