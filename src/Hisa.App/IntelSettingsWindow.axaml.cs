using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hisa.App;

public partial class IntelSettingsWindow : Window
{
    public IntelSettingsWindow()
    {
        InitializeComponent();
    }

    public IntelSettingsWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private async void OnSaveIntelSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        await vm.SaveIntelSettingsAsync();
    }
}
