using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Hisa.App.ViewModels;

public sealed class MiningStatsCard : INotifyPropertyChanged
{
    private static readonly HttpClient PortraitHttpClient = new();
    private Bitmap? _portraitBitmap;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required int CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public required string PrimaryOreName { get; init; }
    public required string EfficiencyPercentSummary { get; init; }
    public required string YieldPercentSummary { get; init; }
    public required string CritPercentSummary { get; init; }
    public required string WastePercentSummary { get; init; }
    public required string TotalMiningRateSummary { get; init; }
    public required string MiningRateSummary { get; init; }
    public required string WasteRateSummary { get; init; }
    public required string IskRateSummary { get; init; }
    public required string WasteIskRateSummary { get; init; }
    public required string TotalMinedSummary { get; init; }
    public required string TotalEstimatedIskSummary { get; init; }
    public required string TotalWasteIskSummary { get; init; }
    public required string SessionTotalSummary { get; init; }
    public required string WasteTotalSummary { get; init; }
    public required string EfficiencySummary { get; init; }
    public required string SessionAgeSummary { get; init; }
    public required string LastUpdatedSummary { get; init; }
    public required string OreMixSummary { get; init; }
    public double YieldRatio { get; init; }
    public double WasteRatio { get; init; }

    public Bitmap? PortraitBitmap
    {
        get => _portraitBitmap;
        private set
        {
            if (!ReferenceEquals(_portraitBitmap, value))
            {
                _portraitBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPortraitBitmap));
                OnPropertyChanged(nameof(HasNoPortraitBitmap));
            }
        }
    }

    public bool HasPortraitBitmap => PortraitBitmap is not null;

    public bool HasNoPortraitBitmap => PortraitBitmap is null;

    public string PortraitFallbackText
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(CharacterName) ? CharacterId.ToString() : CharacterName.Trim();
            return source.Length == 0
                ? "?"
                : source[..Math.Min(2, source.Length)].ToUpperInvariant();
        }
    }

    public async Task EnsurePortraitLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (CharacterId <= 0 || PortraitBitmap is not null)
        {
            return;
        }

        try
        {
            using var stream = await PortraitHttpClient.GetStreamAsync(
                $"https://images.evetech.net/characters/{CharacterId}/portrait?tenant=tranquility&size=128",
                cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            memory.Position = 0;
            var bitmap = new Bitmap(memory);
            await Dispatcher.UIThread.InvokeAsync(() => PortraitBitmap = bitmap);
        }
        catch
        {
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
