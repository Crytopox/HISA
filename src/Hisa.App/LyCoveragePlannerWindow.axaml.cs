using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hisa.App;

public partial class LyCoveragePlannerWindow : Window
{
    private sealed class SessionState
    {
        public string RangeLyText { get; set; } = "7.0";
        public string TopResultsText { get; set; } = "50";
        public bool InputOnlyCenters { get; set; }
        public string InputSystemsText { get; set; } = string.Empty;
        public IReadOnlyList<LyCoverageCandidateRow> Candidates { get; set; } = [];
        public int SelectedIndex { get; set; } = -1;
        public string SummaryText { get; set; } = "Run analysis to see candidate center systems.";
        public string StatusText { get; set; } = string.Empty;
        public double LastRangeLy { get; set; } = 7.0;
    }

    private static SessionState? s_sessionState;
    private readonly MainWindowViewModel _vm;
    private double _lastRangeLy = 7.0;

    public LyCoveragePlannerWindow()
    {
        InitializeComponent();
        _vm = null!;
    }

    public LyCoveragePlannerWindow(MainWindowViewModel vm) : this()
    {
        _vm = vm;
        RestoreSessionState();
        Closed += (_, _) => CaptureSessionState();
    }

    private async void OnAnalyzeClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var rawLy = RangeLyTextBox.Text?.Trim() ?? string.Empty;
        if (!double.TryParse(rawLy, NumberStyles.Float, CultureInfo.InvariantCulture, out var lyRange) &&
            !double.TryParse(rawLy, NumberStyles.Float, CultureInfo.CurrentCulture, out lyRange))
        {
            StatusTextBlock.Text = "Invalid LY range.";
            return;
        }

        if (lyRange <= 0)
        {
            StatusTextBlock.Text = "LY range must be greater than 0.";
            return;
        }

        _lastRangeLy = lyRange;
        var topRaw = TopResultsTextBox.Text?.Trim() ?? "250";
        if (!int.TryParse(topRaw, out var topN) || topN <= 0)
        {
            topN = 250;
        }

        var result = await _vm.AnalyzeLyCoverageAsync(
            InputSystemsTextBox.Text ?? string.Empty,
            lyRange,
            InputOnlyCentersCheckBox.IsChecked == true,
            topN);

        CandidatesListBox.ItemsSource = result.Candidates;

        var invalidSuffix = result.InvalidTokens.Count > 0
            ? $" | Invalid: {result.InvalidTokens.Count} ({string.Join(", ", result.InvalidTokens.Take(8))}{(result.InvalidTokens.Count > 8 ? ", ..." : string.Empty)})"
            : string.Empty;
        SummaryTextBlock.Text = $"Targets: {result.TargetCount} | Candidates tested: {result.CandidateCountTested} | Results shown: {result.Candidates.Count} | Range: {lyRange:0.00} LY{invalidSuffix}";
        StatusTextBlock.Text = result.Candidates.Count == 0
            ? "No candidate systems found for this input/range."
            : "Select a row and click 'Apply Selected To Map' to push it to jump range overlay.";
    }

    private void OnApplySelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (CandidatesListBox.SelectedItem is not LyCoverageCandidateRow row)
        {
            StatusTextBlock.Text = "Select a candidate row first.";
            return;
        }

        if (!_vm.ApplyLyCoverageCandidate(row, _lastRangeLy, clearExisting: true))
        {
            StatusTextBlock.Text = "Failed to apply selected center to map jump range.";
            return;
        }

        _vm.SelectedNodeId = row.CenterSystemId;
        StatusTextBlock.Text = $"Applied center '{row.CenterSystemName}' at {_lastRangeLy:0.00} LY to map jump range.";
    }

    private void OnClearMapClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.ClearJumpRangeOrigins();
        StatusTextBlock.Text = "Cleared LY coverage highlights from map.";
    }

    private void RestoreSessionState()
    {
        if (s_sessionState is null)
        {
            return;
        }

        RangeLyTextBox.Text = s_sessionState.RangeLyText;
        TopResultsTextBox.Text = s_sessionState.TopResultsText;
        InputOnlyCentersCheckBox.IsChecked = s_sessionState.InputOnlyCenters;
        InputSystemsTextBox.Text = s_sessionState.InputSystemsText;
        CandidatesListBox.ItemsSource = s_sessionState.Candidates;
        SummaryTextBlock.Text = s_sessionState.SummaryText;
        StatusTextBlock.Text = s_sessionState.StatusText;
        _lastRangeLy = s_sessionState.LastRangeLy;
        if (s_sessionState.SelectedIndex >= 0 && s_sessionState.SelectedIndex < s_sessionState.Candidates.Count)
        {
            CandidatesListBox.SelectedIndex = s_sessionState.SelectedIndex;
        }
    }

    private void CaptureSessionState()
    {
        var rows = (CandidatesListBox.ItemsSource as IEnumerable<LyCoverageCandidateRow>)?.ToList() ?? [];
        s_sessionState = new SessionState
        {
            RangeLyText = RangeLyTextBox.Text ?? "7.0",
            TopResultsText = TopResultsTextBox.Text ?? "50",
            InputOnlyCenters = InputOnlyCentersCheckBox.IsChecked == true,
            InputSystemsText = InputSystemsTextBox.Text ?? string.Empty,
            Candidates = rows,
            SelectedIndex = CandidatesListBox.SelectedIndex,
            SummaryText = SummaryTextBlock.Text ?? "Run analysis to see candidate center systems.",
            StatusText = StatusTextBlock.Text ?? string.Empty,
            LastRangeLy = _lastRangeLy
        };
    }
}
