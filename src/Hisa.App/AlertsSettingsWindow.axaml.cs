using Avalonia.Controls;
using Avalonia.Interactivity;
using Hisa.App.Services;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class AlertsSettingsWindow : Window
{
    private readonly MainWindowViewModel? _vm;
    private readonly List<AlertRule> _rules = [];
    private int _selectedIndex = -1;

    public AlertsSettingsWindow()
    {
        InitializeComponent();
        EventTypeComboBox.ItemsSource = Enum.GetValues<AlertEventType>();
        ScopeModeComboBox.ItemsSource = Enum.GetValues<AlertLocationScopeMode>();
        DistanceModeComboBox.ItemsSource = Enum.GetValues<AlertDistanceMode>();
        PopupAnchorComboBox.ItemsSource = Enum.GetValues<AlertPopupAnchor>();
        SoundVolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                SoundVolumeTextBox.Text = Math.Round(SoundVolumeSlider.Value).ToString();
            }
        };
    }

    public AlertsSettingsWindow(MainWindowViewModel vm) : this()
    {
        _vm = vm;
        LoadFromViewModel();
    }

    private void LoadFromViewModel()
    {
        _rules.Clear();
        if (_vm is not null)
        {
            _rules.AddRange(_vm.GetAlertRulesSnapshot());
        }

        if (_rules.Count == 0)
        {
            _rules.Add(CreateDefaultRule());
        }

        if (_vm is not null)
        {
            CharacterNamesComboBox.ItemsSource = _vm.GetTrackedCharacterNamesSnapshot();
        }
        SoundFileComboBox.ItemsSource = AlertSoundPlayer.GetAvailableSoundFiles();

        RefreshRulesList();
        RulesListBox.SelectedIndex = 0;

        if (_vm is not null)
        {
            var popup = _vm.GetAlertPopupSettingsSnapshot();
            PopupEnabledCheckBox.IsChecked = popup.Enabled;
            PopupClickThroughCheckBox.IsChecked = popup.ClickThrough;
            PopupMaxCardsTextBox.Text = popup.MaxCards.ToString();
            PopupAutoDismissTextBox.Text = popup.AutoDismissSeconds.ToString();
            PopupOpacityTextBox.Text = popup.Opacity.ToString("0.##");
            PopupAnchorComboBox.SelectedItem = popup.Anchor;
            PopupOffsetXTextBox.Text = popup.OffsetX.ToString();
            PopupOffsetYTextBox.Text = popup.OffsetY.ToString();
        }
    }

    private static AlertRule CreateDefaultRule()
    {
        return new AlertRule
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Alert Rule",
            Enabled = true,
            EventType = AlertEventType.IntelReport,
            ScopeMode = AlertLocationScopeMode.Global,
            CharacterIds = [],
            DistanceMode = AlertDistanceMode.Any,
            MaxJumps = 3,
            IncludeAnsiblexLinks = false,
            CooldownSeconds = 30,
            SoundVolume = 1.0,
            Actions = [AlertActionType.ShowPopup]
        };
    }

    private void RefreshRulesList()
    {
        RulesListBox.ItemsSource = _rules.Select((r, i) => $"{i + 1}. {(r.Enabled ? "[ON]" : "[OFF]")} {r.Name}").ToList();
    }

    private void OnRuleSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedIndex = RulesListBox.SelectedIndex;
        if (_selectedIndex < 0 || _selectedIndex >= _rules.Count)
        {
            return;
        }

        var rule = _rules[_selectedIndex];
        RuleNameTextBox.Text = rule.Name;
        RuleEnabledCheckBox.IsChecked = rule.Enabled;
        EventTypeComboBox.SelectedItem = rule.EventType;
        ScopeModeComboBox.SelectedItem = rule.ScopeMode;
        var characterNames = rule.CharacterNames?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        if (characterNames.Count == 0 && _vm is not null)
        {
            characterNames = (rule.CharacterIds ?? [])
                .Select(_vm.GetTrackedCharacterNameById)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
        CharacterNamesComboBox.Text = string.Join(", ", characterNames);
        DistanceModeComboBox.SelectedItem = rule.DistanceMode;
        MaxJumpsTextBox.Text = rule.MaxJumps.ToString();
        IncludeAnsiblexCheckBox.IsChecked = rule.IncludeAnsiblexLinks;
        CooldownSecondsTextBox.Text = rule.CooldownSeconds.ToString();
        SoundFileComboBox.Text = string.IsNullOrWhiteSpace(rule.SoundFile) ? "default-alert.wav" : rule.SoundFile;
        var volumePercent = Math.Clamp((int)Math.Round(rule.SoundVolume * 100.0), 0, 100);
        SoundVolumeSlider.Value = volumePercent;
        SoundVolumeTextBox.Text = volumePercent.ToString();
        ActionPopupCheckBox.IsChecked = rule.Actions.Contains(AlertActionType.ShowPopup);
        ActionSoundCheckBox.IsChecked = rule.Actions.Contains(AlertActionType.PlaySound);
    }

    private void OnEditorValueChanged(object? sender, SelectionChangedEventArgs e)
    {
        // no-op: explicit Apply button commits changes
    }

    private void OnAddRuleClicked(object? sender, RoutedEventArgs e)
    {
        _rules.Add(CreateDefaultRule());
        RefreshRulesList();
        RulesListBox.SelectedIndex = _rules.Count - 1;
    }

    private void OnDeleteRuleClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _rules.Count)
        {
            return;
        }

        _rules.RemoveAt(_selectedIndex);
        if (_rules.Count == 0)
        {
            _rules.Add(CreateDefaultRule());
        }

        RefreshRulesList();
        RulesListBox.SelectedIndex = Math.Clamp(_selectedIndex, 0, _rules.Count - 1);
    }

    private async void OnApplyRuleClicked(object? sender, RoutedEventArgs e)
    {
        await ApplyRuleToSelectionAsync();
    }

    private async Task ApplyRuleToSelectionAsync()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _rules.Count)
        {
            return;
        }

        var eventType = EventTypeComboBox.SelectedItem is AlertEventType et ? et : AlertEventType.IntelReport;
        var scopeMode = ScopeModeComboBox.SelectedItem is AlertLocationScopeMode sm ? sm : AlertLocationScopeMode.Global;
        var distanceMode = DistanceModeComboBox.SelectedItem is AlertDistanceMode dm ? dm : AlertDistanceMode.Any;

        var characterNames = (CharacterNamesComboBox.Text ?? string.Empty)
            .Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var characterIds = _vm is null
            ? []
            : (await _vm.ResolveCharacterIdsByNamesAsync(characterNames)).Distinct().ToList();

        var maxJumps = int.TryParse(MaxJumpsTextBox.Text, out var j) ? Math.Max(0, j) : 0;
        var cooldown = int.TryParse(CooldownSecondsTextBox.Text, out var c) ? Math.Max(0, c) : 0;
        var volumePercent = int.TryParse(SoundVolumeTextBox.Text, out var rawVolume)
            ? Math.Clamp(rawVolume, 0, 100)
            : Math.Clamp((int)Math.Round(SoundVolumeSlider.Value), 0, 100);
        SoundVolumeSlider.Value = volumePercent;
        SoundVolumeTextBox.Text = volumePercent.ToString();

        var actions = new List<AlertActionType>();
        if (ActionPopupCheckBox.IsChecked == true)
        {
            actions.Add(AlertActionType.ShowPopup);
        }

        if (ActionSoundCheckBox.IsChecked == true)
        {
            actions.Add(AlertActionType.PlaySound);
        }

        _rules[_selectedIndex] = new AlertRule
        {
            Id = _rules[_selectedIndex].Id,
            Name = string.IsNullOrWhiteSpace(RuleNameTextBox.Text) ? "Unnamed Alert Rule" : RuleNameTextBox.Text.Trim(),
            Enabled = RuleEnabledCheckBox.IsChecked == true,
            EventType = eventType,
            ScopeMode = scopeMode,
            CharacterIds = characterIds,
            CharacterNames = characterNames,
            DistanceMode = distanceMode,
            MaxJumps = maxJumps,
            IncludeAnsiblexLinks = IncludeAnsiblexCheckBox.IsChecked == true,
            CooldownSeconds = cooldown,
            SoundFile = string.IsNullOrWhiteSpace(SoundFileComboBox.Text) ? "default-alert.wav" : SoundFileComboBox.Text.Trim(),
            SoundVolume = volumePercent / 100.0,
            Actions = actions
        };

        RefreshRulesList();
        RulesListBox.SelectedIndex = _selectedIndex;
    }

    private async void OnSaveAllClicked(object? sender, RoutedEventArgs e)
    {
        await ApplyRuleToSelectionAsync();
        if (_vm is null)
        {
            return;
        }

        await _vm.SaveAlertRulesAsync(_rules);
        await SavePopupSettingsInternalAsync();
    }

    private async void OnSavePopupSettingsClicked(object? sender, RoutedEventArgs e)
    {
        await SavePopupSettingsInternalAsync();
    }

    private async Task SavePopupSettingsInternalAsync()
    {
        if (_vm is null)
        {
            return;
        }

        var maxCards = int.TryParse(PopupMaxCardsTextBox.Text, out var mc) ? mc : 8;
        var dismiss = int.TryParse(PopupAutoDismissTextBox.Text, out var ds) ? ds : 18;
        var opacity = double.TryParse(PopupOpacityTextBox.Text, out var op) ? op : 0.95;
        var offsetX = int.TryParse(PopupOffsetXTextBox.Text, out var ox) ? ox : 12;
        var offsetY = int.TryParse(PopupOffsetYTextBox.Text, out var oy) ? oy : 56;
        var anchor = PopupAnchorComboBox.SelectedItem is AlertPopupAnchor a ? a : AlertPopupAnchor.TopRight;

        await _vm.SaveAlertPopupSettingsAsync(new AlertPopupSettings
        {
            Enabled = PopupEnabledCheckBox.IsChecked == true,
            ClickThrough = PopupClickThroughCheckBox.IsChecked == true,
            MaxCards = maxCards,
            AutoDismissSeconds = dismiss,
            Opacity = opacity,
            Anchor = anchor,
            OffsetX = offsetX,
            OffsetY = offsetY
        });
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTestSoundClicked(object? sender, RoutedEventArgs e)
    {
        var sound = string.IsNullOrWhiteSpace(SoundFileComboBox.Text) ? "default-alert.wav" : SoundFileComboBox.Text.Trim();
        var volumePercent = int.TryParse(SoundVolumeTextBox.Text, out var rawVolume)
            ? Math.Clamp(rawVolume, 0, 100)
            : Math.Clamp((int)Math.Round(SoundVolumeSlider.Value), 0, 100);
        SoundVolumeSlider.Value = volumePercent;
        SoundVolumeTextBox.Text = volumePercent.ToString();
        AlertSoundPlayer.Play(sound, volumePercent / 100.0);
    }
}
