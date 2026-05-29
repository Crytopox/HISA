using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Hisa.App;

public partial class JumpRouteOptimizerWindow : Window
{
    private sealed class SessionState
    {
        public string MaxLyText { get; set; } = "7.0";
        public string TopResultsText { get; set; } = "5";
        public string StartSystemText { get; set; } = string.Empty;
        public string EndSystemText { get; set; } = string.Empty;
        public bool ReturnToStart { get; set; }
        public bool FollowInputOrder { get; set; }
        public string InputSystemsText { get; set; } = string.Empty;
        public IReadOnlyList<JumpRouteCandidateRow> Candidates { get; set; } = [];
        public int SelectedIndex { get; set; } = -1;
        public string SummaryText { get; set; } = "Run analysis to see route candidates.";
        public string StatusText { get; set; } = string.Empty;
        public string StatusColorHex { get; set; } = "#8FA5C4";
        public string LegsText { get; set; } = string.Empty;
    }

    private static SessionState? s_sessionState;
    private readonly MainWindowViewModel _vm;
    private bool _suppressSuggestionRefresh;
    public JumpRouteOptimizerWindow()
    {
        InitializeComponent();
        _vm = null!;
    }

    public JumpRouteOptimizerWindow(MainWindowViewModel vm) : this()
    {
        _vm = vm;
        CandidatesListBox.SelectionChanged += OnSelectionChanged;
        RestoreSessionState();
        Closed += (_, _) => CaptureSessionState();
    }

    private async void OnAnalyzeClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var rawLy = MaxLyTextBox.Text?.Trim() ?? string.Empty;
        if (!double.TryParse(rawLy, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLy) &&
            !double.TryParse(rawLy, NumberStyles.Float, CultureInfo.CurrentCulture, out maxLy))
        {
            StatusTextBlock.Text = "Invalid max jump LY value.";
            return;
        }
        if (maxLy <= 0)
        {
            StatusTextBlock.Text = "Max jump LY must be greater than 0.";
            return;
        }

        var topRaw = TopResultsTextBox.Text?.Trim() ?? "5";
        if (!int.TryParse(topRaw, out var topN) || topN <= 0)
        {
            topN = 5;
        }

        var result = await _vm.AnalyzeJumpRoutesAsync(
            InputSystemsTextBox.Text ?? string.Empty,
            FollowInputOrderCheckBox.IsChecked == true,
            maxLy,
            StartSystemTextBox.Text,
            EndSystemTextBox.Text,
            ReturnToStartCheckBox.IsChecked == true,
            topN);

        CandidatesListBox.ItemsSource = result.Candidates;
        var invalidSuffix = result.InvalidTokens.Count > 0
            ? $" | Invalid: {result.InvalidTokens.Count} ({string.Join(", ", result.InvalidTokens.Take(8))}{(result.InvalidTokens.Count > 8 ? ", ..." : string.Empty)})"
            : string.Empty;
        SummaryTextBlock.Text = $"Targets: {result.TargetCount} | Results shown: {result.Candidates.Count} | Max jump: {maxLy:0.00} LY{invalidSuffix}";
        if (!string.IsNullOrWhiteSpace(result.OrderingMessage))
        {
            StatusTextBlock.Text = result.OrderingMessage;
            if (result.OrderingFailed)
            {
                StatusTextBlock.Foreground = result.Candidates.Count > 0
                    ? new SolidColorBrush(Color.Parse("#F0B35A"))
                    : new SolidColorBrush(Color.Parse("#FF6A6A"));
            }
            else
            {
                StatusTextBlock.Foreground = new SolidColorBrush(Color.Parse("#8FA5C4"));
            }
        }
        else
        {
            StatusTextBlock.Text = result.Candidates.Count == 0
                ? "No valid route candidates found under current constraints."
                : "Select a route and click 'Apply Selected To Map'.";
            StatusTextBlock.Foreground = new SolidColorBrush(Color.Parse("#8FA5C4"));
        }
        StartSuggestionsPopup.IsOpen = false;
        EndSuggestionsPopup.IsOpen = false;
    }

    private void OnApplySelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (CandidatesListBox.SelectedItem is not JumpRouteCandidateRow row)
        {
            StatusTextBlock.Text = "Select a route row first.";
            return;
        }

        _vm.ApplyJumpRouteCandidate(row);
        LegsBreakdownTextBox.Text = row.LegsText;
        StatusTextBlock.Text = $"Applied route with {row.VisitedCount}/{row.TargetCount} systems.";
    }

    private void OnClearMapRouteClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.ClearJumpRouteHighlights();
        LegsBreakdownTextBox.Text = string.Empty;
        StatusTextBlock.Text = "Cleared route highlights from map.";
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CandidatesListBox.SelectedItem is JumpRouteCandidateRow row)
        {
            LegsBreakdownTextBox.Text = row.LegsText;
        }
        else
        {
            LegsBreakdownTextBox.Text = string.Empty;
        }
    }

    private async void OnCopySelectedRouteClicked(object? sender, RoutedEventArgs e)
    {
        if (CandidatesListBox.SelectedItem is not JumpRouteCandidateRow row)
        {
            StatusTextBlock.Text = "Select a route row first.";
            return;
        }

        var text = $"{row.RouteText}{Environment.NewLine}{Environment.NewLine}{row.LegsText}";
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null)
        {
            StatusTextBlock.Text = "Clipboard is unavailable.";
            return;
        }

        await top.Clipboard.SetTextAsync(text);
        StatusTextBlock.Text = "Copied selected route and leg breakdown to clipboard.";
    }

    private async void OnStartSystemTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_vm is null || _suppressSuggestionRefresh)
        {
            return;
        }

        var text = StartSystemTextBox.Text?.Trim() ?? string.Empty;
        if (text.Length < 2)
        {
            StartSuggestionsListBox.ItemsSource = null;
            StartSuggestionsPopup.IsOpen = false;
            return;
        }

        var suggestions = await _vm.GetSystemNameSuggestionsAsync(text, 8);
        StartSuggestionsListBox.ItemsSource = suggestions;
        StartSuggestionsPopup.IsOpen = suggestions.Count > 0;
    }

    private async void OnEndSystemTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_vm is null || _suppressSuggestionRefresh)
        {
            return;
        }

        var text = EndSystemTextBox.Text?.Trim() ?? string.Empty;
        if (text.Length < 2)
        {
            EndSuggestionsListBox.ItemsSource = null;
            EndSuggestionsPopup.IsOpen = false;
            return;
        }

        var suggestions = await _vm.GetSystemNameSuggestionsAsync(text, 8);
        EndSuggestionsListBox.ItemsSource = suggestions;
        EndSuggestionsPopup.IsOpen = suggestions.Count > 0;
    }

    private void OnStartSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (StartSuggestionsListBox.SelectedItem is not string value)
        {
            return;
        }

        _suppressSuggestionRefresh = true;
        StartSystemTextBox.Text = value;
        _suppressSuggestionRefresh = false;
        StartSuggestionsPopup.IsOpen = false;
        StartSuggestionsListBox.SelectedItem = null;
    }

    private void OnEndSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (EndSuggestionsListBox.SelectedItem is not string value)
        {
            return;
        }

        _suppressSuggestionRefresh = true;
        EndSystemTextBox.Text = value;
        _suppressSuggestionRefresh = false;
        EndSuggestionsPopup.IsOpen = false;
        EndSuggestionsListBox.SelectedItem = null;
    }

    private void RestoreSessionState()
    {
        if (s_sessionState is null)
        {
            return;
        }

        _suppressSuggestionRefresh = true;
        MaxLyTextBox.Text = s_sessionState.MaxLyText;
        TopResultsTextBox.Text = s_sessionState.TopResultsText;
        StartSystemTextBox.Text = s_sessionState.StartSystemText;
        EndSystemTextBox.Text = s_sessionState.EndSystemText;
        _suppressSuggestionRefresh = false;

        ReturnToStartCheckBox.IsChecked = s_sessionState.ReturnToStart;
        FollowInputOrderCheckBox.IsChecked = s_sessionState.FollowInputOrder;
        InputSystemsTextBox.Text = s_sessionState.InputSystemsText;
        CandidatesListBox.ItemsSource = s_sessionState.Candidates;
        SummaryTextBlock.Text = s_sessionState.SummaryText;
        StatusTextBlock.Text = s_sessionState.StatusText;
        StatusTextBlock.Foreground = new SolidColorBrush(Color.Parse(s_sessionState.StatusColorHex));
        LegsBreakdownTextBox.Text = s_sessionState.LegsText;
        if (s_sessionState.SelectedIndex >= 0 && s_sessionState.SelectedIndex < s_sessionState.Candidates.Count)
        {
            CandidatesListBox.SelectedIndex = s_sessionState.SelectedIndex;
        }
    }

    private void CaptureSessionState()
    {
        var rows = (CandidatesListBox.ItemsSource as IEnumerable<JumpRouteCandidateRow>)?.ToList() ?? [];
        var statusBrush = StatusTextBlock.Foreground as ISolidColorBrush;
        s_sessionState = new SessionState
        {
            MaxLyText = MaxLyTextBox.Text ?? "7.0",
            TopResultsText = TopResultsTextBox.Text ?? "5",
            StartSystemText = StartSystemTextBox.Text ?? string.Empty,
            EndSystemText = EndSystemTextBox.Text ?? string.Empty,
            ReturnToStart = ReturnToStartCheckBox.IsChecked == true,
            FollowInputOrder = FollowInputOrderCheckBox.IsChecked == true,
            InputSystemsText = InputSystemsTextBox.Text ?? string.Empty,
            Candidates = rows,
            SelectedIndex = CandidatesListBox.SelectedIndex,
            SummaryText = SummaryTextBlock.Text ?? "Run analysis to see route candidates.",
            StatusText = StatusTextBlock.Text ?? string.Empty,
            StatusColorHex = statusBrush?.Color.ToString() ?? "#8FA5C4",
            LegsText = LegsBreakdownTextBox.Text ?? string.Empty
        };
    }
}
