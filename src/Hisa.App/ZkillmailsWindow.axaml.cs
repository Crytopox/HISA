using Avalonia.Controls;

namespace Hisa.App;

public partial class ZkillmailsWindow : Window
{
    public ZkillmailsWindow()
    {
        InitializeComponent();
    }

    public ZkillmailsWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }
}
