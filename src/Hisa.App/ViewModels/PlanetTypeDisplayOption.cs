using System.ComponentModel;
using Avalonia.Media.Imaging;

namespace Hisa.App;

public sealed class PlanetTypeDisplayOption : INotifyPropertyChanged
{
    private bool _isSelected;
    private Bitmap? _icon;

    public required int TypeId { get; init; }
    public required string DisplayName { get; init; }

    public Bitmap? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }

            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

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
}
