using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hisa.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void OnFitCenterClicked(object? sender, RoutedEventArgs e)
    {
        MainMapControl.FitToView();
    }
}
