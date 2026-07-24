using Hisa.Core.Models;

namespace Hisa.App.ViewModels;

public sealed class MiningSiteReportRow
{
    public required int SolarSystemId { get; init; }
    public required string SystemName { get; init; }
    public required string UpgradeName { get; init; }
    public required int Tier { get; init; }
    public required MiningSiteStatus Status { get; init; }
    public DateTime? AvailableAtUtc { get; init; }
    public string StatusText => Status == MiningSiteStatus.Available ? "Available" : Status == MiningSiteStatus.Cleared ? "Cleared / respawning" : "Missing / remind";
    public string StatusColor => Status switch
    {
        MiningSiteStatus.Available => "#51D88A",
        MiningSiteStatus.Cleared => "#F2C94C",
        _ => "#FF6B6B"
    };
    public string DueText => AvailableAtUtc is null ? "No timer" : $"{AvailableAtUtc:yyyy-MM-dd HH:mm} UTC";
}
