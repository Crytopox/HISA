using Avalonia.Controls;
using Avalonia.Interactivity;
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
            CooldownSeconds = 30,
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
        CharacterIdsTextBox.Text = string.Join(", ", rule.CharacterIds);
        DistanceModeComboBox.SelectedItem = rule.DistanceMode;
        MaxJumpsTextBox.Text = rule.MaxJumps.ToString();
        IncludeAnsiblexCheckBox.IsChecked = rule.IncludeAnsiblexLinks;
        CooldownSecondsTextBox.Text = rule.CooldownSeconds.ToString();
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

    private void OnApplyRuleClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _rules.Count)
        {
            return;
        }

        var eventType = EventTypeComboBox.SelectedItem is AlertEventType et ? et : AlertEventType.IntelReport;
        var scopeMode = ScopeModeComboBox.SelectedItem is AlertLocationScopeMode sm ? sm : AlertLocationScopeMode.Global;
        var distanceMode = DistanceModeComboBox.SelectedItem is AlertDistanceMode dm ? dm : AlertDistanceMode.Any;

        var characterIds = (CharacterIdsTextBox.Text ?? string.Empty)
            .Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var maxJumps = int.TryParse(MaxJumpsTextBox.Text, out var j) ? Math.Max(0, j) : 0;
        var cooldown = int.TryParse(CooldownSecondsTextBox.Text, out var c) ? Math.Max(0, c) : 0;

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
            DistanceMode = distanceMode,
            MaxJumps = maxJumps,
            IncludeAnsiblexLinks = IncludeAnsiblexCheckBox.IsChecked == true,
            CooldownSeconds = cooldown,
            Actions = actions
        };

        RefreshRulesList();
        RulesListBox.SelectedIndex = _selectedIndex;
    }

    private async void OnSaveAllClicked(object? sender, RoutedEventArgs e)
    {
        OnApplyRuleClicked(sender, e);
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
}
