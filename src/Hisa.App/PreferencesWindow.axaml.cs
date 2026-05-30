using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
namespace Hisa.App;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    public PreferencesWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private async void OnBrowseLogsRootPathClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || StorageProvider is null)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select EVE Logs Root Folder",
            AllowMultiple = false
        });

        var selected = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        vm.LogsRootPath = selected;
        vm.ValidateLogsRootPath();
    }

    private void OnValidateLogsRootPathClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.ValidateLogsRootPath();
    }

    private async void OnSaveLogsRootPathClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await vm.SaveLogsRootPathAsync();
        vm.ValidateLogsRootPath();
    }
}
