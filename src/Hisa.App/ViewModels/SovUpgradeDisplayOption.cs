using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace Hisa.App;

public sealed class SovUpgradeDisplayOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required Bitmap? Icon { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaiseAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
