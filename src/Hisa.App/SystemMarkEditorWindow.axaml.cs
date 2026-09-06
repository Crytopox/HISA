using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hisa.Core.Models;
using Hisa.Rendering;

namespace Hisa.App;

public sealed class SystemMarkIconOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required SystemMarkIconKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required StreamGeometry Geometry { get; init; }

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
}

public partial class SystemMarkEditorWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindowViewModel? _viewModel;
    private readonly long _systemId;
    private readonly string _systemName;
    private readonly string? _regionName;
    private Color _pickerColor = Color.Parse("#7AA5D6");
    private string _labelText = string.Empty;
    private bool _showIcon = true;
    private bool _showLabel;
    private string _validationText = string.Empty;
    private SystemMarkIconKind _selectedIcon = SystemMarkIconKind.Pin;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public SystemMarkEditorWindow()
    {
        _systemId = 0;
        _systemName = string.Empty;
        IconOptions = [];
        DataContext = this;
        InitializeComponent();
    }

    public SystemMarkEditorWindow(
        MainWindowViewModel viewModel,
        long systemId,
        string systemName,
        string? regionName,
        UserSystemMark? existing) : this()
    {
        _viewModel = viewModel;
        _systemId = systemId;
        _systemName = systemName;
        _regionName = regionName;
        CanRemove = existing is not null;
        HeaderText = existing is null ? $"Mark {systemName}" : $"Edit mark · {systemName}";

        foreach (var kind in SystemMarkIcons.All)
        {
            IconOptions.Add(new SystemMarkIconOption
            {
                Kind = kind,
                DisplayName = SystemMarkIcons.GetDisplayName(kind),
                Geometry = SystemMarkIconGeometry.Get(kind)
            });
        }

        if (existing is not null)
        {
            _selectedIcon = existing.IconKind ?? SystemMarkIconKind.Pin;
            _showIcon = existing.ShowIcon && existing.IconKind is not null;
            _showLabel = existing.ShowLabel && !string.IsNullOrWhiteSpace(existing.Label);
            _labelText = existing.Label ?? string.Empty;
            if (TryParseColor(existing.ColorHex, out var color))
            {
                _pickerColor = color;
            }
        }

        SelectIcon(_selectedIcon, enableIcon: false);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeaderText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRemove)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowIcon)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PickerColor)));
    }

    public ObservableCollection<SystemMarkIconOption> IconOptions { get; }
    public string HeaderText { get; private set; } = "Mark System";
    public bool CanRemove { get; }

    public Color PickerColor
    {
        get => _pickerColor;
        set => SetProperty(ref _pickerColor, value);
    }

    public string LabelText
    {
        get => _labelText;
        set
        {
            if (!SetProperty(ref _labelText, value))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value) && !_showLabel)
            {
                ShowLabel = true;
            }
        }
    }

    public bool ShowIcon
    {
        get => _showIcon;
        set => SetProperty(ref _showIcon, value);
    }

    public bool ShowLabel
    {
        get => _showLabel;
        set => SetProperty(ref _showLabel, value);
    }

    public string ValidationText
    {
        get => _validationText;
        private set => SetProperty(ref _validationText, value);
    }

    public void SelectIcon(SystemMarkIconKind kind, bool enableIcon = true)
    {
        _selectedIcon = kind;
        foreach (var option in IconOptions)
        {
            option.IsSelected = option.Kind == kind;
        }

        if (enableIcon && !_showIcon)
        {
            ShowIcon = true;
        }
    }

    private void OnIconClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: SystemMarkIconKind kind })
        {
            SelectIcon(kind);
        }
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(LabelText) ? null : LabelText.Trim();
        if (!ShowIcon && (string.IsNullOrWhiteSpace(label) || !ShowLabel))
        {
            ValidationText = "Turn on an icon, a label, or both.";
            return;
        }

        var saved = _viewModel.UpsertUserSystemMark(new UserSystemMark
        {
            SolarSystemId = _systemId,
            SolarSystemName = _systemName,
            RegionName = _regionName,
            IconKind = _selectedIcon,
            Label = label,
            ColorHex = $"#{PickerColor.R:X2}{PickerColor.G:X2}{PickerColor.B:X2}",
            ShowIcon = ShowIcon,
            ShowLabel = ShowLabel && !string.IsNullOrWhiteSpace(label)
        });

        if (!saved)
        {
            ValidationText = "Could not save this mark. Choose an icon and/or a label.";
            return;
        }

        Close();
    }

    private void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel?.RemoveUserSystemMark(_systemId);
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = Color.Parse(hex.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
