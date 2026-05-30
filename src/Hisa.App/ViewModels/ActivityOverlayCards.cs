namespace Hisa.App;

public sealed class IntelOverlayHostileCard : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = string.Empty;
    private Avalonia.Media.Imaging.Bitmap? _portraitBitmap;
    private Avalonia.Media.Imaging.Bitmap? _corporationBitmap;
    private Avalonia.Media.Imaging.Bitmap? _allianceBitmap;
    private Avalonia.Media.Imaging.Bitmap? _shipBitmap;
    private int? _corporationId;
    private int? _allianceId;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public required string Name
    {
        get => _name;
        set
        {
            if (!string.Equals(_name, value, StringComparison.Ordinal))
            {
                _name = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }
    public int? CharacterId { get; set; }
    public int? CorporationId
    {
        get => _corporationId;
        set
        {
            if (_corporationId != value)
            {
                _corporationId = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CorporationId)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasCorporation)));
            }
        }
    }
    public int? AllianceId
    {
        get => _allianceId;
        set
        {
            if (_allianceId != value)
            {
                _allianceId = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AllianceId)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasAlliance)));
            }
        }
    }
    public bool HasCorporation => CorporationId is not null && CorporationBitmap is not null;
    public bool HasAlliance => AllianceId is not null && AllianceBitmap is not null;
    public Avalonia.Media.Imaging.Bitmap? PortraitBitmap
    {
        get => _portraitBitmap;
        set
        {
            if (!ReferenceEquals(_portraitBitmap, value))
            {
                _portraitBitmap = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(PortraitBitmap)));
            }
        }
    }
    public Avalonia.Media.Imaging.Bitmap? CorporationBitmap
    {
        get => _corporationBitmap;
        set
        {
            if (!ReferenceEquals(_corporationBitmap, value))
            {
                _corporationBitmap = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CorporationBitmap)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasCorporation)));
            }
        }
    }
    public Avalonia.Media.Imaging.Bitmap? AllianceBitmap
    {
        get => _allianceBitmap;
        set
        {
            if (!ReferenceEquals(_allianceBitmap, value))
            {
                _allianceBitmap = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(AllianceBitmap)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasAlliance)));
            }
        }
    }
    public int? ShipTypeId { get; set; }
    public bool HasShipBitmap => ShipBitmap is not null;
    public bool HasNoShipBitmap => ShipBitmap is null;
    public Avalonia.Media.Imaging.Bitmap? ShipBitmap
    {
        get => _shipBitmap;
        set
        {
            if (!ReferenceEquals(_shipBitmap, value))
            {
                _shipBitmap = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ShipBitmap)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasShipBitmap)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasNoShipBitmap)));
            }
        }
    }
    public string ShipDisplayName { get; set; } = "Unknown";
    public string ShipIconKey { get; set; } = "crosshair";
}

public sealed class IntelOverlayShipSummaryCard : System.ComponentModel.INotifyPropertyChanged
{
    private Avalonia.Media.Imaging.Bitmap? _shipBitmap;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public required string ShipName { get; init; }
    public int Count { get; init; }
    public int? ShipTypeId { get; init; }
    public string ShipIconKey { get; init; } = "crosshair";
    public bool HasShipBitmap => ShipBitmap is not null;
    public bool HasNoShipBitmap => ShipBitmap is null;
    public Avalonia.Media.Imaging.Bitmap? ShipBitmap
    {
        get => _shipBitmap;
        set
        {
            if (!ReferenceEquals(_shipBitmap, value))
            {
                _shipBitmap = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ShipBitmap)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasShipBitmap)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasNoShipBitmap)));
            }
        }
    }
}

public sealed class WormholeOverlayCard
{
    public required string SystemName { get; init; }
    public required string RegionName { get; init; }
    public required string ConstellationName { get; init; }
    public required string HubSummary { get; init; }
    public required string HubLabelColorHex { get; init; }
    public required string ShipSizeSummary { get; init; }
    public required string SignatureSummary { get; init; }
    public required string ReportedUpdatedSummary { get; init; }
    public required string ExpirySummary { get; init; }
    public required string ExpiryColorHex { get; init; }
    public required int ConnectionCount { get; init; }
    public required string AccentHex { get; init; }
}

public sealed class IncursionOverlayCard
{
    public required string StagingSystemName { get; init; }
    public required string ConstellationName { get; init; }
    public required string RegionName { get; init; }
    public required string TypeLabel { get; init; }
    public required string StateLabel { get; init; }
    public required string StateColorHex { get; init; }
    public required string FactionLabel { get; init; }
    public required string BossLabel { get; init; }
    public required string InfluenceLabel { get; init; }
    public required string AffectedSystemsLabel { get; init; }
    public required string TypeColorHex { get; init; }
    public required string BossColorHex { get; init; }
    public required string AccentHex { get; init; }
}

public sealed class StormOverlayCard
{
    public required string CenterSystemName { get; init; }
    public required string ConstellationName { get; init; }
    public required string RegionName { get; init; }
    public required string StormTypeLabel { get; init; }
    public required string StormTypeColorHex { get; init; }
    public required string CoverageSummary { get; init; }
    public required string StrengthSummary { get; init; }
    public required string ReportedSummary { get; init; }
    public required string AccentHex { get; init; }
}

public sealed class IntelOverlayCard
{
    public required DateTime SortTimestampUtc { get; init; }
    public required DateTime LastUpdatedUtc { get; init; }
    public required long SolarSystemId { get; init; }
    public required string SystemName { get; init; }
    public required string ConstellationName { get; init; }
    public required string RegionName { get; init; }
    public int? ConstellationId { get; init; }
    public int? RegionId { get; init; }
    public required string ChannelName { get; init; }
    public required string ReporterName { get; init; }
    public required string AgeSummary { get; set; }
    public required string MessageText { get; init; }
    public required IReadOnlyList<IntelOverlayHostileCard> Hostiles { get; init; }
    public required IReadOnlyList<IntelOverlayShipSummaryCard> ShipsSummary { get; init; }
    public required string ShipClassSummary { get; init; }
    public required int HostileCount { get; init; }
    public required string ShipBadgeBackgroundHex { get; init; }
    public required string ShipBadgeBorderHex { get; init; }
    public required string HostileBadgeBackgroundHex { get; init; }
    public required string HostileBadgeBorderHex { get; init; }
    public required string AccentHex { get; init; }
}
