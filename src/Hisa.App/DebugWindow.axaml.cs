using Avalonia.Controls;

namespace Hisa.App;

public partial class DebugWindow : Window
{
    public DebugWindow()
    {
        InitializeComponent();
    }

    public DebugWindow(DebugWindowViewModel vm) : this()
    {
        DataContext = vm;
    }
}
