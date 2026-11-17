using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hisa.App.Services;
using Hisa.Core.Models;
using Hisa.Services.Xmpp;

namespace Hisa.App;

internal partial class XmppAlertRuleWindow : Window
{
    private static readonly string[] ScopeLabels =
        ["Specific conversation", "Anywhere", "Any direct message", "Any room"];
    private static readonly string[] MatchLabels =
        ["Any message", "Contains text", "Equals text", "Regular expression", "Mentions my room nickname"];

    private readonly XmppClientService _client;
    private readonly XmppAlertRule? _existing;
    private readonly XmppConversation? _defaultSource;
    private readonly bool _fleet;
    private bool _busy;

    public bool Saved { get; private set; }

    public XmppAlertRuleWindow(XmppClientService client, XmppAlertRule? rule, XmppConversation? defaultSource)
    {
        _client = client;
        _existing = rule;
        _defaultSource = defaultSource;
        _fleet = rule?.FleetPings == true;
        InitializeComponent();
        TitleText.Text = _fleet ? "Directorbot fleet pings" : rule is null ? "Add alert rule" : "Edit alert rule";
        LoadFields();
    }

    private void LoadFields()
    {
        var rule = _existing;
        EnabledBox.IsChecked = rule?.Enabled ?? true;
        EnabledBox.IsVisible = _fleet;
        MatchPanel.IsVisible = !_fleet;

        ScopeBox.ItemsSource = ScopeLabels;
        ScopeBox.SelectedIndex = rule is null
            ? (int)XmppAlertSourceScope.Conversation
            : (int)rule.SourceScope;
        KindBox.ItemsSource = new[] { "Direct message", "Room" };
        var source = rule?.Source ?? _defaultSource;
        KindBox.SelectedIndex = (int)(source?.Kind ?? XmppConversationKind.Direct);
        AddressBox.Text = source?.Jid;
        SenderBox.Text = rule?.Sender;
        MatchBox.ItemsSource = MatchLabels;
        MatchBox.SelectedIndex = MatchToIndex(rule?.Match ?? XmppAlertMatch.AnyMessage);
        TextBox.Text = rule?.Text;
        PopupBox.IsChecked = rule?.Popup ?? true;
        HighlightBox.IsChecked = rule?.Highlight == true;
        FleetFeedBox.IsChecked = rule?.ShowInFleetFeed == true || _fleet;
        FleetFeedBox.IsEnabled = !_fleet;
        FeedTitleBox.Text = rule?.FeedTitle ?? (_fleet ? "Fleet ping" : null);
        FeedSubtitleBox.Text = rule?.FeedSubtitle;
        CooldownBox.Text = (rule?.CooldownSeconds ?? 0).ToString();
        CooldownBox.IsEnabled = !_fleet;

        var files = AlertSoundPlayer.GetAvailableSoundFiles().Prepend("No sound").ToList();
        if (rule?.Sound is { } sound &&
            !files.Contains(sound, StringComparer.OrdinalIgnoreCase))
            files.Add(sound);
        SoundBox.ItemsSource = files;
        SoundBox.SelectedIndex = rule?.Sound is { } current
            ? Math.Max(0, files.FindIndex(file => file.Equals(current, StringComparison.OrdinalIgnoreCase)))
            : 0;
        VolumeSlider.Value = (rule?.Volume ?? 0.4) * 100;
        ScopeChanged(null, null!);
        MatchChanged(null, null!);
        FeedChanged(null, null!);
    }

    private static int MatchToIndex(XmppAlertMatch match) => match switch
    {
        XmppAlertMatch.ContainsText => 1,
        XmppAlertMatch.EqualsText => 2,
        XmppAlertMatch.Regex => 3,
        XmppAlertMatch.NicknameMention => 4,
        _ => 0
    };

    private static XmppAlertMatch IndexToMatch(int index) => index switch
    {
        1 => XmppAlertMatch.ContainsText,
        2 => XmppAlertMatch.EqualsText,
        3 => XmppAlertMatch.Regex,
        4 => XmppAlertMatch.NicknameMention,
        _ => XmppAlertMatch.AnyMessage
    };

    private void ScopeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var specific = ScopeBox.SelectedIndex <= 0;
        AddressBox.IsVisible = AddressLabel.IsVisible =
            KindBox.IsVisible = KindLabel.IsVisible = specific && !_fleet;
    }

    private void MatchChanged(object? sender, SelectionChangedEventArgs e)
    {
        var needsText = MatchBox.SelectedIndex is 1 or 2 or 3;
        TextBox.IsVisible = TextLabel.IsVisible = needsText && !_fleet;
    }

    private void FeedChanged(object? sender, RoutedEventArgs e) =>
        FeedFields.IsVisible = FleetFeedBox.IsChecked == true;

    private void PreviewSound(object? sender, RoutedEventArgs e)
    {
        if (SoundBox.SelectedIndex > 0 && SoundBox.SelectedItem is string file)
            AlertSoundPlayer.Play(file, VolumeSlider.Value / 100);
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        StatusText.Text = "";
        try
        {
            var files = (SoundBox.ItemsSource as IList<string>) ?? Array.Empty<string>();
            var soundFile = SoundBox.SelectedIndex <= 0 ? null : files[SoundBox.SelectedIndex];
            var feedTitle = string.IsNullOrWhiteSpace(FeedTitleBox.Text) ? null : FeedTitleBox.Text.Trim();
            var feedSubtitle = string.IsNullOrWhiteSpace(FeedSubtitleBox.Text) ? null : FeedSubtitleBox.Text.Trim();
            if (_fleet)
            {
                await _client.SaveRuleAsync(_existing! with
                {
                    Enabled = EnabledBox.IsChecked == true,
                    Popup = PopupBox.IsChecked == true,
                    Sound = soundFile,
                    Volume = VolumeSlider.Value / 100,
                    ShowInFleetFeed = true,
                    Highlight = HighlightBox.IsChecked == true,
                    FeedTitle = feedTitle ?? "Fleet ping",
                    FeedSubtitle = feedSubtitle
                });
            }
            else
            {
                if (!int.TryParse(CooldownBox.Text?.Trim(), out var cooldown) || cooldown < 0)
                    throw new ArgumentException("Enter a cooldown of 0 or more seconds.");
                var scope = (XmppAlertSourceScope)Math.Clamp(ScopeBox.SelectedIndex, 0, 3);
                XmppConversation source;
                if (scope == XmppAlertSourceScope.Conversation)
                {
                    var jid = XmppAddresses.Bare(AddressBox.Text?.Trim());
                    if (jid is null || AddressBox.Text?.Contains('/') == true)
                        throw new ArgumentException("Enter a valid conversation address.");
                    source = new((XmppConversationKind)Math.Clamp(KindBox.SelectedIndex, 0, 1), jid);
                }
                else
                    source = _defaultSource ?? new(XmppConversationKind.Direct, _client.Account?.Jid ?? "local@local");

                await _client.SaveRuleAsync(new(
                    _existing?.Id ?? Guid.NewGuid(),
                    source,
                    IndexToMatch(MatchBox.SelectedIndex),
                    TextBox.Text?.Trim(),
                    PopupBox.IsChecked == true,
                    soundFile,
                    VolumeSlider.Value / 100,
                    Enabled: true,
                    FleetPings: false,
                    SourceScope: scope,
                    Sender: string.IsNullOrWhiteSpace(SenderBox.Text) ? null : SenderBox.Text.Trim(),
                    CooldownSeconds: cooldown,
                    ShowInFleetFeed: FleetFeedBox.IsChecked == true,
                    Highlight: HighlightBox.IsChecked == true,
                    FeedTitle: FleetFeedBox.IsChecked == true ? feedTitle : null,
                    FeedSubtitle: FleetFeedBox.IsChecked == true ? feedSubtitle : null));
            }

            Saved = true;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            StatusText.Text = ex.Message;
        }
        catch
        {
            StatusText.Text = "The alert rule could not be saved.";
        }
        finally { _busy = false; }
    }
}
