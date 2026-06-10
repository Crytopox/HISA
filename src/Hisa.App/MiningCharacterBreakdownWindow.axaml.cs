using Avalonia.Controls;

namespace Hisa.App;

public partial class MiningCharacterBreakdownWindow : Window
{
    public MiningCharacterBreakdownWindow()
    {
        InitializeComponent();
    }

    public MiningCharacterBreakdownWindow(MainWindowViewModel vm) : this()
    {
        DataContext = vm;
    }
}
