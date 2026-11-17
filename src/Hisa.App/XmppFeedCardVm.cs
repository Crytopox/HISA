using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hisa.Core.Models;

namespace Hisa.App;

public sealed class XmppFeedCardVm
{
    public XmppFeedCardVm(
        XmppMessage message,
        bool isFleet,
        string title,
        string time,
        string? subtitle,
        string? body,
        string? papType,
        bool emphasizePap,
        string? formup,
        string? doctrine,
        string? fcName,
        string? fcLookupName,
        string? commsLabel,
        string? commsUrl,
        IBrush cardBackground,
        IBrush accentBar,
        IBrush cardBorder,
        IBrush papChipBackground,
        IBrush papChipForeground,
        bool isCollapseSummary = false,
        string? collapseKey = null,
        string? notes = null,
        IReadOnlyList<XmppFeedCommanderVm>? lineup = null)
    {
        Message = message;
        LocalId = message.LocalId;
        IsFleet = isFleet;
        Title = title;
        Time = time;
        Subtitle = subtitle ?? "";
        Body = body ?? "";
        PapType = papType;
        EmphasizePap = emphasizePap;
        Formup = formup;
        Doctrine = doctrine;
        Notes = notes;
        Commander = new(fcName, fcLookupName, commsLabel, commsUrl);
        Lineup = lineup ?? [];
        CardBackground = cardBackground;
        AccentBar = accentBar;
        CardBorder = cardBorder;
        PapChipBackground = papChipBackground;
        PapChipForeground = papChipForeground;
        IsCollapseSummary = isCollapseSummary;
        CollapseKey = collapseKey ?? "";
    }

    public XmppMessage Message { get; }
    public long LocalId { get; }
    public bool IsFleet { get; }
    public bool IsSimple => !IsFleet && !IsCollapseSummary;
    public bool IsCollapseSummary { get; }
    public string CollapseKey { get; }
    public string Title { get; }
    public string Time { get; }
    public bool HasTime => !string.IsNullOrEmpty(Time);
    public string Subtitle { get; }
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public string Body { get; }
    public double BodySize => IsFleet ? 13 : 12;
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    public bool IsUnreadStart { get; internal set; }
    public string? PapType { get; }
    public bool HasPap => !string.IsNullOrWhiteSpace(PapType);
    public bool EmphasizePap { get; }
    public string? Formup { get; }
    public bool HasFormup => !string.IsNullOrWhiteSpace(Formup);
    public string? Doctrine { get; }
    public bool HasDoctrine => !string.IsNullOrWhiteSpace(Doctrine);
    public string? Notes { get; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public XmppFeedCommanderVm Commander { get; }
    public IReadOnlyList<XmppFeedCommanderVm> Lineup { get; }
    public bool HasLineup => Lineup.Count > 0;
    public string? FcName => Commander.Name;
    public bool HasFc => !string.IsNullOrWhiteSpace(FcName);
    public string CommsLabel => Commander.CommsLabel;
    public Uri? CommsUrl => Commander.CommsUrl;
    public IReadOnlyList<XmppTranscriptRun>? CommsDocument => Commander.CommsDocument;
    public bool HasComms => CommsUrl is not null;
    public bool ShowCommsField => !string.IsNullOrWhiteSpace(CommsLabel);
    public IBrush CardBackground { get; }
    public IBrush AccentBar { get; }
    public IBrush CardBorder { get; }
    public IBrush PapChipBackground { get; }
    public IBrush PapChipForeground { get; }
    public bool ShowAccent => !ReferenceEquals(AccentBar, Brushes.Transparent);
    public bool ShowActions => !IsCollapseSummary;
    public double AccentWidth => IsFleet ? 4 : 3;
    public double TitleSize => IsFleet ? 16 : 14;
    public Bitmap? Portrait => Commander.Portrait;
    public bool ShowPortraitPlaceholder => Commander.ShowPortraitPlaceholder;
}

public sealed class XmppFeedCommanderVm : INotifyPropertyChanged
{
    private Bitmap? _portrait;

    public XmppFeedCommanderVm(string? name, string? lookupName, string? commsLabel, string? commsUrl)
    {
        Name = name;
        LookupName = lookupName;
        CommsLabel = string.IsNullOrWhiteSpace(commsLabel) ? commsUrl ?? "" : commsLabel;
        CommsUrl = Uri.TryCreate(commsUrl, UriKind.Absolute, out var link) && link.Scheme is "http" or "https"
            ? link : null;
        if (CommsUrl is not null)
            CommsDocument = [new() { Text = CommsLabel, Link = CommsUrl }];
    }

    public string? Name { get; }
    public string? LookupName { get; }
    public string CommsLabel { get; }
    public Uri? CommsUrl { get; }
    public IReadOnlyList<XmppTranscriptRun>? CommsDocument { get; }
    public bool HasCommsLabel => CommsLabel.Length > 0;
    public bool HasDivider { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Bitmap? Portrait
    {
        get => _portrait;
        set
        {
            if (ReferenceEquals(_portrait, value)) return;
            _portrait = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPortrait));
            OnPropertyChanged(nameof(ShowPortraitPlaceholder));
        }
    }

    public bool ShowPortrait => Portrait is not null;
    public bool ShowPortraitPlaceholder => Portrait is null;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
