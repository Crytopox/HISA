using Avalonia.Controls;

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
}
