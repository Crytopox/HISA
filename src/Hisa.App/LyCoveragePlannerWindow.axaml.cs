using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hisa.App;

public partial class LyCoveragePlannerWindow : Window
{
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
}
