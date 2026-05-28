using Avalonia.Controls;
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
}
