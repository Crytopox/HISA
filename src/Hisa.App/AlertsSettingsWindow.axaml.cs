using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Hisa.App.Services;
using Hisa.Core.Models;

namespace Hisa.App;

public partial class AlertsSettingsWindow : Window
{
    private sealed record AlertChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly MainWindowViewModel? _vm;
    private readonly List<AlertRule> _rules = [];
    private readonly List<string> _allTrackedCharacterNames = [];
    private readonly HashSet<string> _selectedCharacterNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RegionOption> _availableRegions = [];
    private readonly HashSet<int> _selectedRegionIds = [];
    private int _selectedIndex = -1;

    public AlertsSettingsWindow()
    {
        InitializeComponent();
        EventTypeComboBox.ItemsSource = new[]
        {
            new AlertChoice<AlertEventType>(AlertEventType.IntelReport, "Intel Report"),
            new AlertChoice<AlertEventType>(AlertEventType.IntelTextMatch, "Intel text match"),
            new AlertChoice<AlertEventType>(AlertEventType.Killmail, "Killmail"),
            new AlertChoice<AlertEventType>(AlertEventType.HubWormholeSpawn, "Thera/Turnur Spawn"),
            new AlertChoice<AlertEventType>(AlertEventType.IncursionSpawn, "Incursion Spawn"),
            new AlertChoice<AlertEventType>(AlertEventType.StormSpawn, "Storm Moved")
        };
        ScopeModeComboBox.ItemsSource = new[]
        {
            new AlertChoice<AlertLocationScopeMode>(AlertLocationScopeMode.Global, "Everywhere"),
            new AlertChoice<AlertLocationScopeMode>(AlertLocationScopeMode.AnyTrackedCharacter, "Near any tracked character"),
            new AlertChoice<AlertLocationScopeMode>(AlertLocationScopeMode.SpecificCharacters, "Near specific characters"),
            new AlertChoice<AlertLocationScopeMode>(AlertLocationScopeMode.SelectedRegions, "In selected regions")
        };
        DistanceModeComboBox.ItemsSource = new[]
        {
            new AlertChoice<AlertDistanceMode>(AlertDistanceMode.Any, "Any distance"),
            new AlertChoice<AlertDistanceMode>(AlertDistanceMode.MaxJumps, "Within max jumps"),
            new AlertChoice<AlertDistanceMode>(AlertDistanceMode.CurrentRegion, "In the same region")
        };
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
            _allTrackedCharacterNames.Clear();
            _allTrackedCharacterNames.AddRange(_vm.GetTrackedCharacterNamesSnapshot());
            _availableRegions.Clear();
            _availableRegions.AddRange(_vm.GetAlertRegionOptionsSnapshot());
        }
        RefreshCharacterCandidatesList();
        RefreshSelectedCharactersList();
        RefreshRegionCandidatesList();
        RefreshSelectedRegionsList();
        SoundFileComboBox.ItemsSource = AlertSoundPlayer.GetAvailableSoundFiles();

        RefreshRulesList();
        RulesListBox.SelectedIndex = 0;

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
            ShowClearIntelReports = false,
            CooldownSeconds = 30,
            SoundVolume = 1.0,
            Actions = []
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
        SelectChoice(EventTypeComboBox, rule.EventType);
        SelectChoice(ScopeModeComboBox, rule.ScopeMode);
        var characterNames = rule.CharacterNames?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        if (characterNames.Count == 0 && _vm is not null)
        {
            characterNames = (rule.CharacterIds ?? [])
                .Select(_vm.GetTrackedCharacterNameById)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
        _selectedCharacterNames.Clear();
        foreach (var name in characterNames)
        {
            _selectedCharacterNames.Add(name.Trim());
        }
        CharacterSearchTextBox.Text = string.Empty;
        RefreshCharacterCandidatesList();
        RefreshSelectedCharactersList();
        SelectChoice(DistanceModeComboBox, rule.DistanceMode);
        _selectedRegionIds.Clear();
        foreach (var regionId in rule.RegionIds ?? [])
        {
            _selectedRegionIds.Add(regionId);
        }
        RegionSearchTextBox.Text = string.Empty;
        RefreshRegionCandidatesList();
        RefreshSelectedRegionsList();
        MaxJumpsTextBox.Text = rule.MaxJumps.ToString();
        IncludeAnsiblexCheckBox.IsChecked = rule.IncludeAnsiblexLinks;
        ShowClearIntelReportsCheckBox.IsChecked = rule.ShowClearIntelReports;
        TextPatternTextBox.Text = rule.TextPattern;
        UseRegexCheckBox.IsChecked = rule.UseRegex;
        CooldownSecondsTextBox.Text = rule.CooldownSeconds.ToString();
        SoundFileComboBox.Text = string.IsNullOrWhiteSpace(rule.SoundFile) ? "default-alert.wav" : rule.SoundFile;
        var volumePercent = Math.Clamp((int)Math.Round(rule.SoundVolume * 100.0), 0, 100);
        SoundVolumeSlider.Value = volumePercent;
        SoundVolumeTextBox.Text = volumePercent.ToString();
        ActionSoundCheckBox.IsChecked = rule.Actions.Contains(AlertActionType.PlaySound);
        UpdateEditorVisibility();
    }

    private void OnEditorValueChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateEditorVisibility();
    }

    private void OnActionSoundToggleChanged(object? sender, RoutedEventArgs e)
    {
        UpdateEditorVisibility();
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

    private async void OnMoveRuleUpClicked(object? sender, RoutedEventArgs e)
    {
        await MoveSelectedRuleAsync(-1);
    }

    private async void OnMoveRuleDownClicked(object? sender, RoutedEventArgs e)
    {
        await MoveSelectedRuleAsync(1);
    }

    private async Task MoveSelectedRuleAsync(int offset)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _rules.Count)
        {
            return;
        }

        var destination = _selectedIndex + offset;
        if (destination < 0 || destination >= _rules.Count)
        {
            return;
        }

        // Keep edits in the currently visible editor before moving its rule.
        await ApplyRuleToSelectionAsync();
        (_rules[_selectedIndex], _rules[destination]) = (_rules[destination], _rules[_selectedIndex]);
        RefreshRulesList();
        RulesListBox.SelectedIndex = destination;
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

        var eventType = SelectedValue(EventTypeComboBox, AlertEventType.IntelReport);
        var scopeMode = SelectedValue(ScopeModeComboBox, AlertLocationScopeMode.Global);
        var distanceMode = SelectedValue(DistanceModeComboBox, AlertDistanceMode.Any);

        var characterNames = _selectedCharacterNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var characterIds = _vm is null
            ? []
            : (await _vm.ResolveCharacterIdsByNamesAsync(characterNames)).Distinct().ToList();
        var regionIds = _selectedRegionIds.Where(id => id > 0).OrderBy(id => id).ToList();

        var maxJumps = int.TryParse(MaxJumpsTextBox.Text, out var j) ? Math.Max(0, j) : 0;
        var cooldown = int.TryParse(CooldownSecondsTextBox.Text, out var c) ? Math.Max(0, c) : 0;
        var volumePercent = int.TryParse(SoundVolumeTextBox.Text, out var rawVolume)
            ? Math.Clamp(rawVolume, 0, 100)
            : Math.Clamp((int)Math.Round(SoundVolumeSlider.Value), 0, 100);
        SoundVolumeSlider.Value = volumePercent;
        SoundVolumeTextBox.Text = volumePercent.ToString();

        var actions = new List<AlertActionType>();
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
            RegionIds = regionIds,
            DistanceMode = distanceMode,
            MaxJumps = maxJumps,
            IncludeAnsiblexLinks = IncludeAnsiblexCheckBox.IsChecked == true,
            ShowClearIntelReports = ShowClearIntelReportsCheckBox.IsChecked == true,
            TextPattern = TextPatternTextBox.Text?.Trim() ?? string.Empty,
            UseRegex = UseRegexCheckBox.IsChecked == true,
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

    private void OnCharacterSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshCharacterCandidatesList();
    }

    private void OnCharacterSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddCharacterFromPicker();
            e.Handled = true;
        }
    }

    private void OnCharacterCandidateDoubleTapped(object? sender, TappedEventArgs e)
    {
        AddCharacterFromPicker();
    }

    private void OnSelectedCharacterDoubleTapped(object? sender, TappedEventArgs e)
    {
        RemoveSelectedCharacterFromPicker();
    }

    private void OnAddCharacterClicked(object? sender, RoutedEventArgs e)
    {
        AddCharacterFromPicker();
    }

    private void AddCharacterFromPicker()
    {
        var picked = CharacterCandidatesListBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(picked))
        {
            var typed = CharacterSearchTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(typed))
            {
                picked = typed;
            }
        }

        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        _selectedCharacterNames.Add(picked.Trim());
        CharacterSearchTextBox.Text = string.Empty;
        RefreshCharacterCandidatesList();
        RefreshSelectedCharactersList();
    }

    private void OnRemoveCharacterClicked(object? sender, RoutedEventArgs e)
    {
        RemoveSelectedCharacterFromPicker();
    }

    private void RemoveSelectedCharacterFromPicker()
    {
        var picked = SelectedCharactersListBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        _selectedCharacterNames.Remove(picked.Trim());
        RefreshCharacterCandidatesList();
        RefreshSelectedCharactersList();
    }

    private void RefreshCharacterCandidatesList()
    {
        var query = CharacterSearchTextBox.Text?.Trim() ?? string.Empty;
        var candidates = _allTrackedCharacterNames
            .Where(x => !_selectedCharacterNames.Contains(x))
            .Where(x => query.Length == 0 || x.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
        CharacterCandidatesListBox.ItemsSource = candidates;
        if (candidates.Count > 0)
        {
            CharacterCandidatesListBox.SelectedIndex = 0;
        }
    }

    private void RefreshSelectedCharactersList()
    {
        SelectedCharactersListBox.ItemsSource = _selectedCharacterNames
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void OnRegionSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshRegionCandidatesList();
    }

    private void OnRegionSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddRegionFromPicker();
            e.Handled = true;
        }
    }

    private void OnRegionCandidateDoubleTapped(object? sender, TappedEventArgs e) => AddRegionFromPicker();

    private void OnSelectedRegionDoubleTapped(object? sender, TappedEventArgs e) => RemoveSelectedRegionFromPicker();

    private void OnAddRegionClicked(object? sender, RoutedEventArgs e) => AddRegionFromPicker();

    private void OnRemoveRegionClicked(object? sender, RoutedEventArgs e) => RemoveSelectedRegionFromPicker();

    private void AddRegionFromPicker()
    {
        if (RegionCandidatesListBox.SelectedItem is not RegionOption region)
        {
            return;
        }

        _selectedRegionIds.Add(region.RegionId);
        RegionSearchTextBox.Text = string.Empty;
        RefreshRegionCandidatesList();
        RefreshSelectedRegionsList();
    }

    private void RemoveSelectedRegionFromPicker()
    {
        if (SelectedRegionsListBox.SelectedItem is not RegionOption region)
        {
            return;
        }

        _selectedRegionIds.Remove(region.RegionId);
        RefreshRegionCandidatesList();
        RefreshSelectedRegionsList();
    }

    private void RefreshRegionCandidatesList()
    {
        var query = RegionSearchTextBox.Text?.Trim() ?? string.Empty;
        var candidates = _availableRegions
            .Where(region => !_selectedRegionIds.Contains(region.RegionId))
            .Where(region => query.Length == 0 || region.RegionName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(region => region.RegionName, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
        RegionCandidatesListBox.ItemsSource = candidates;
        if (candidates.Count > 0)
        {
            RegionCandidatesListBox.SelectedIndex = 0;
        }
    }

    private void RefreshSelectedRegionsList()
    {
        var selected = _availableRegions
            .Where(region => _selectedRegionIds.Contains(region.RegionId))
            .OrderBy(region => region.RegionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SelectedRegionsListBox.ItemsSource = selected;
        if (selected.Count > 0)
        {
            SelectedRegionsListBox.SelectedIndex = 0;
        }
    }

    private void UpdateEditorVisibility()
    {
        var scopeMode = SelectedValue(ScopeModeComboBox, AlertLocationScopeMode.Global);
        var distanceMode = SelectedValue(DistanceModeComboBox, AlertDistanceMode.Any);
        var soundEnabled = ActionSoundCheckBox.IsChecked == true;
        var eventType = SelectedValue(EventTypeComboBox, AlertEventType.IntelReport);

        CharacterSearchTextBox.IsEnabled = scopeMode == AlertLocationScopeMode.SpecificCharacters;
        CharacterCandidatesListBox.IsEnabled = scopeMode == AlertLocationScopeMode.SpecificCharacters;
        SelectedCharactersListBox.IsEnabled = scopeMode == AlertLocationScopeMode.SpecificCharacters;
        CharacterSelectorPanel.IsVisible = scopeMode == AlertLocationScopeMode.SpecificCharacters;
        RegionSelectorPanel.IsVisible = scopeMode == AlertLocationScopeMode.SelectedRegions;
        EventTypeComboBox.IsVisible = true;
        var usesCharacterDistance = scopeMode is AlertLocationScopeMode.AnyTrackedCharacter or AlertLocationScopeMode.SpecificCharacters;
        DistanceSettingsPanel.IsVisible = usesCharacterDistance;
        MaxJumpsPanel.IsVisible = usesCharacterDistance && distanceMode == AlertDistanceMode.MaxJumps;
        DistanceModeComboBox.IsEnabled = usesCharacterDistance;
        ShowClearIntelReportsCheckBox.IsVisible = eventType is AlertEventType.IntelReport or AlertEventType.IntelTextMatch;
        IntelTextMatchPanel.IsVisible = eventType == AlertEventType.IntelTextMatch;
        SoundFileComboBox.IsEnabled = soundEnabled;
        SoundVolumeTextBox.IsEnabled = soundEnabled;
        SoundVolumeSlider.IsEnabled = soundEnabled;
        ScopeHelpTextBlock.Text = scopeMode switch
        {
            AlertLocationScopeMode.Global => "Everywhere ignores distance and character location: any matching event can alert.",
            AlertLocationScopeMode.AnyTrackedCharacter => "Uses every tracked character as a reference. Choose the distance below.",
            AlertLocationScopeMode.SpecificCharacters => "Uses only the selected characters as references. Choose the distance below.",
            AlertLocationScopeMode.SelectedRegions => "Only events inside one of the selected regions can alert. Distance and characters are not used.",
            _ => string.Empty
        };
    }

    private static T SelectedValue<T>(ComboBox comboBox, T fallback) =>
        comboBox.SelectedItem is AlertChoice<T> choice ? choice.Value : fallback;

    private static void SelectChoice<T>(ComboBox comboBox, T value)
    {
        comboBox.SelectedItem = (comboBox.ItemsSource as IEnumerable<AlertChoice<T>>)
            ?.FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value));
    }
}
