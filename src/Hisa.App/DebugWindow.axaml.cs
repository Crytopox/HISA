using Avalonia.Controls;
using Avalonia.Interactivity;

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
}
