using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Hisa.App.Services;
using Hisa.Core.Models;
using Hisa.Services.Xmpp;

namespace Hisa.App;

internal enum XmppPreferencesSection { Account, Display, MessageFilters, AlertSounds, AlertRules, Reset }

internal partial class XmppPreferencesWindow : Window
{
    private readonly XmppClientService _client;
    private readonly XmppWindow _owner;
    private readonly List<string> _fonts = ["Inter", "Segoe UI", "Consolas", "Cascadia Mono"];
    private readonly List<XmppMessageFilter> _filters = [];
    private readonly string? _prefillFilterSender;
    private byte[]? _pendingAvatarBytes;
    private string? _pendingAvatarType;
    private bool _clearAvatar;
    private bool _avatarDirty;
    private bool _busy;
    private bool _openFilterEditor;

    public bool AccountChanged { get; private set; }
    public bool DisplaySaved { get; private set; }

    public XmppPreferencesWindow(XmppClientService client, XmppWindow owner, XmppPreferencesSection section,
        string? prefillFilterSender = null)
    {
        _client = client;
        _owner = owner;
        _prefillFilterSender = prefillFilterSender;
        InitializeComponent();
        LoadFields();
        ShowSection(section);
        RulesList.SelectionChanged += (_, _) =>
            RemoveRuleButton.IsEnabled = RulesList.SelectedItem is RuleRow { Rule.FleetPings: false };
        RemoveRuleButton.IsEnabled = false;
        UpdateAccountConnectionButtons();
        if (section == XmppPreferencesSection.MessageFilters && !string.IsNullOrWhiteSpace(prefillFilterSender))
            _openFilterEditor = true;
        Opened += async (_, _) =>
        {
            if (!_openFilterEditor) return;
            _openFilterEditor = false;
            if (await EditFilterAsync(null, _prefillFilterSender))
                await PersistFiltersAsync();
        };
    }

    private void LoadFields()
    {
        var account = _client.Account;
        JidBox.Text = account?.Jid;
        RememberBox.IsChecked = account?.RememberPassword == true;
        AutoConnectBox.IsChecked = account?.AutoConnect == true;
        AutoConnectBox.IsEnabled = RememberBox.IsChecked == true;
        HostBox.Text = account?.Host;
        PortBox.Text = account is { Port: > 0 } ? account.Port.ToString() : null;
        TlsBox.ItemsSource = new[] { "Automatic", "STARTTLS", "Direct TLS" };
        TlsBox.SelectedIndex = (int)(account?.TlsMode ?? XmppTlsMode.Automatic);

        var display = _client.DisplayPreferences;
        if (!string.IsNullOrWhiteSpace(display.FontFamily) &&
            !_fonts.Contains(display.FontFamily, StringComparer.OrdinalIgnoreCase))
            _fonts.Insert(0, display.FontFamily);
        FontBox.ItemsSource = _fonts;
        var fontIndex = _fonts.FindIndex(font =>
            font.Equals(display.FontFamily, StringComparison.OrdinalIgnoreCase));
        FontBox.SelectedIndex = fontIndex < 0 ? 0 : fontIndex;
        FontSizeBox.Text = Math.Clamp(display.MessageFontSize, 11, 18).ToString("0.#");
        MessagePaddingBox.Text = XmppDisplayPreferences.NormalizeMessageBlockPadding(display.MessageBlockPadding).ToString();
        ClockBox.IsChecked = display.Use12HourClock;
        DividersBox.IsChecked = display.ShowMessageDividers;
        AlternatingBox.IsChecked = display.AlternatingMessageColors;
        OccupantsBox.IsChecked = display.ShowOccupantSidebar;
        HeadersBox.IsChecked = display.AlwaysShowMessageHeaders;
        ScrollLatestBox.IsChecked = XmppDisplayPreferences.NormalizeScrollToLatestOnTabSwitch(
            display.ScrollToLatestOnTabSwitch);

        var soft = _client.AlertSoundPreferences;
        DirectPingBox.IsChecked = soft.SoftPingDirectMessages;
        DirectVolume.Value = soft.SoftPingDirectVolume * 100;
        MentionPingBox.IsChecked = soft.SoftPingMentions;
        MentionVolume.Value = soft.SoftPingMentionVolume * 100;
        var sounds = AlertSoundPlayer.GetAvailableSoundFiles().ToList();
        EnsureSound(sounds, soft.SoftPingDirectSound);
        EnsureSound(sounds, soft.SoftPingMentionSound);
        DirectSoundBox.ItemsSource = sounds;
        MentionSoundBox.ItemsSource = sounds;
        DirectSoundBox.SelectedItem = PickSound(sounds, soft.SoftPingDirectSound, "soft-message.wav");
        MentionSoundBox.SelectedItem = PickSound(sounds, soft.SoftPingMentionSound, "soft-mention.wav");
        _filters.Clear();
        _filters.AddRange(_client.MessageFilters);
        RefreshFilters();
        RefreshRules();
        _ = LoadAvatarPreviewAsync();
    }

    private static void EnsureSound(List<string> sounds, string? sound)
    {
        if (!string.IsNullOrWhiteSpace(sound) &&
            !sounds.Contains(sound, StringComparer.OrdinalIgnoreCase))
            sounds.Add(sound);
    }

    private static string PickSound(IReadOnlyList<string> sounds, string? preferred, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = sounds.FirstOrDefault(sound =>
                sound.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return sounds.FirstOrDefault(sound => sound.Equals(fallback, StringComparison.OrdinalIgnoreCase))
            ?? sounds.FirstOrDefault()
            ?? fallback;
    }

    private async Task LoadAvatarPreviewAsync()
    {
        if (_client.Account is null || !_client.Online) return;
        try
        {
            var profile = await _client.EnsureProfileAsync(_client.Account.Jid);
            if (profile.AvatarBytes is { Length: > 0 } bytes)
                SetAvatarPreview(bytes);
        }
        catch
        {
            // Preview is optional while editing.
        }
    }

    private void SetAvatarPreview(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            AvatarPreview.Source = new Bitmap(stream);
        }
        catch
        {
            AvatarPreview.Source = null;
        }
    }

    private void ShowAccountSection(object? sender, RoutedEventArgs e) => ShowSection(XmppPreferencesSection.Account);
    private void ShowDisplaySection(object? sender, RoutedEventArgs e) => ShowSection(XmppPreferencesSection.Display);
    private void ShowFiltersSection(object? sender, RoutedEventArgs e) => ShowSection(XmppPreferencesSection.MessageFilters);
    private void ShowSoundsSection(object? sender, RoutedEventArgs e) => ShowSection(XmppPreferencesSection.AlertSounds);
    private void ShowRulesSection(object? sender, RoutedEventArgs e) => ShowSection(XmppPreferencesSection.AlertRules);
    private void ShowResetSection(object? sender, RoutedEventArgs e) => ShowSection(XmppPreferencesSection.Reset);

    private void ShowSection(XmppPreferencesSection section)
    {
        AccountPanel.IsVisible = section == XmppPreferencesSection.Account;
        DisplayPanel.IsVisible = section == XmppPreferencesSection.Display;
        FiltersPanel.IsVisible = section == XmppPreferencesSection.MessageFilters;
        SoundsPanel.IsVisible = section == XmppPreferencesSection.AlertSounds;
        RulesPanel.IsVisible = section == XmppPreferencesSection.AlertRules;
        ResetPanel.IsVisible = section == XmppPreferencesSection.Reset;
        SetNav(AccountNav, section == XmppPreferencesSection.Account);
        SetNav(DisplayNav, section == XmppPreferencesSection.Display);
        SetNav(FiltersNav, section == XmppPreferencesSection.MessageFilters);
        SetNav(SoundsNav, section == XmppPreferencesSection.AlertSounds);
        SetNav(RulesNav, section == XmppPreferencesSection.AlertRules);
        SetNav(ResetNav, section == XmppPreferencesSection.Reset);
        SaveButton.Content = section == XmppPreferencesSection.Account ? "Save and connect" : "Save";
        SaveButton.IsVisible = section != XmppPreferencesSection.Reset;
        ClearHistoryButton.IsEnabled = _client.Account is not null;
        UpdateAccountConnectionButtons();
    }

    private void UpdateAccountConnectionButtons()
    {
        var connecting = _client.Status is "Connecting…" or "Reconnecting…" ||
            _client.Status.StartsWith("Connection lost", StringComparison.Ordinal);
        AccountConnectButton.IsVisible = !_client.Online;
        AccountDisconnectButton.IsVisible = _client.Online || connecting;
    }

    private static void SetNav(Button button, bool active)
    {
        if (active) button.Classes.Add("active");
        else button.Classes.Remove("active");
    }

    private void RememberChanged(object? sender, RoutedEventArgs e)
    {
        AutoConnectBox.IsEnabled = RememberBox.IsChecked == true;
        if (!AutoConnectBox.IsEnabled) AutoConnectBox.IsChecked = false;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void PreviewDirectPing(object? sender, RoutedEventArgs e) =>
        AlertSoundPlayer.Play(DirectSoundBox.SelectedItem as string ?? "soft-message.wav", DirectVolume.Value / 100);

    private void PreviewMentionPing(object? sender, RoutedEventArgs e) =>
        AlertSoundPlayer.Play(MentionSoundBox.SelectedItem as string ?? "soft-mention.wav", MentionVolume.Value / 100);

    private void RefreshRules() =>
        RulesList.ItemsSource = _client.Rules.Select(rule => new RuleRow(rule)).ToArray();

    private void RefreshFilters() =>
        FiltersList.ItemsSource = _filters.Select(filter => new FilterRow(filter)).ToArray();

    private async void AddRule(object? sender, RoutedEventArgs e) =>
        await RunAsync(async () => { await EditRuleAsync(null); RefreshRules(); });

    private async void EditRule(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (RulesList.SelectedItem is not RuleRow row) throw new ArgumentException("Select an alert rule.");
        await EditRuleAsync(row.Rule);
        RefreshRules();
    });

    private async void RemoveRule(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (RulesList.SelectedItem is not RuleRow row) throw new ArgumentException("Select an alert rule.");
        await _client.RemoveRuleAsync(row.Rule.Id);
        RefreshRules();
    });

    private async void AddFilter(object? sender, RoutedEventArgs e) =>
        await RunAsync(async () =>
        {
            if (!await EditFilterAsync(null, null)) return;
            await PersistFiltersAsync();
        });

    private async void EditFilter(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (FiltersList.SelectedItem is not FilterRow row) throw new ArgumentException("Select a message filter.");
        if (!await EditFilterAsync(row.Filter, null)) return;
        await PersistFiltersAsync();
    });

    private async void RemoveFilter(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (FiltersList.SelectedItem is not FilterRow row) throw new ArgumentException("Select a message filter.");
        _filters.RemoveAll(filter => filter.Id == row.Filter.Id);
        await PersistFiltersAsync();
    });

    private async Task PersistFiltersAsync()
    {
        await _client.SaveMessageFiltersAsync(_filters);
        DisplaySaved = true;
        RefreshFilters();
    }

    private async void AccountConnect(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!await SaveAccountAsync()) return;
        UpdateAccountConnectionButtons();
    });

    private async void AccountDisconnect(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await _client.DisconnectAsync();
        UpdateAccountConnectionButtons();
        StatusText.Text = "Disconnected.";
    });

    private async void ChooseAvatar(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose avatar",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.gif"] }
            ]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        var prepared = XmppAvatarImage.Prepare(path);
        _pendingAvatarBytes = prepared.Bytes;
        _pendingAvatarType = prepared.ContentType;
        _clearAvatar = false;
        _avatarDirty = true;
        SetAvatarPreview(prepared.Bytes);
        StatusText.Text = "Avatar will upload when you save.";
    });

    private void ClearAvatar(object? sender, RoutedEventArgs e)
    {
        _pendingAvatarBytes = null;
        _pendingAvatarType = null;
        _clearAvatar = true;
        _avatarDirty = true;
        AvatarPreview.Source = null;
        StatusText.Text = "Avatar will clear when you save.";
    }

    private async void Save(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await SaveDisplayAndAlertsAsync();
        await _client.SaveMessageFiltersAsync(_filters);
        DisplaySaved = true;
        if (AccountPanel.IsVisible && !await SaveAccountAsync()) return;
        await SaveAvatarAsync();
        Close();
    });

    private async Task SaveDisplayAndAlertsAsync()
    {
        if (!double.TryParse(FontSizeBox.Text?.Trim(), out var fontSize) || fontSize is < 11 or > 18)
            throw new ArgumentException("Enter a font size from 11 to 18.");
        if (!double.TryParse(MessagePaddingBox.Text?.Trim(), out var messagePadding) || !double.IsFinite(messagePadding) ||
            messagePadding is < 0 or > XmppDisplayPreferences.MaxMessageBlockPadding)
            throw new ArgumentException("Enter message row padding from 0 to 12 per side.");
        var family = _fonts[Math.Clamp(FontBox.SelectedIndex, 0, _fonts.Count - 1)];
        await _client.SaveDisplayPreferencesAsync(new(
            ClockBox.IsChecked == true,
            DividersBox.IsChecked == true,
            AlternatingBox.IsChecked == true,
            OccupantsBox.IsChecked == true,
            family,
            fontSize,
            HeadersBox.IsChecked == true,
            messagePadding,
            ScrollLatestBox.IsChecked == true));
        await _client.SaveAlertSoundPreferencesAsync(new(
            DirectPingBox.IsChecked == true,
            DirectVolume.Value / 100,
            DirectSoundBox.SelectedItem as string ?? "soft-message.wav",
            MentionPingBox.IsChecked == true,
            MentionVolume.Value / 100,
            MentionSoundBox.SelectedItem as string ?? "soft-mention.wav"));
    }

    private async Task<bool> SaveAccountAsync()
    {
        ushort portNumber = 0;
        if (!string.IsNullOrWhiteSpace(PortBox.Text) &&
            (!ushort.TryParse(PortBox.Text, out portNumber) || portNumber == 0))
            throw new ArgumentException("Enter a port from 1 to 65535.");

        var account = _client.Account;
        var next = XmppAddresses.Bare(JidBox.Text?.Trim());
        if (account is not null && next is not null && next != account.Jid &&
            !await XmppDialog.ConfirmAsync(this, "Replace Jabber account",
                "Connect with the new account? Its rooms and alert rules will be set up separately."))
            return false;

        await _owner.PrepareAccountSaveAsync();
        var prior = account?.Jid;
        var feedback = await _client.ConfigureAsync(JidBox.Text ?? "", HostBox.Text, portNumber,
            (XmppTlsMode)Math.Max(0, TlsBox.SelectedIndex), PasswordBox.Text, RememberBox.IsChecked == true,
            AutoConnectBox.IsChecked == true, connect: true);
        PasswordBox.Text = "";
        _owner.SetPreferencesFeedback(feedback);
        if (prior != _client.Account?.Jid)
        {
            AccountChanged = true;
            await _owner.ReloadWorkspaceAfterAccountChangeAsync();
        }
        UpdateAccountConnectionButtons();
        return true;
    }

    private async Task SaveAvatarAsync()
    {
        if (!_avatarDirty) return;
        if (!_client.Online)
            throw new InvalidOperationException("Connect to Jabber before updating your avatar.");
        if (_clearAvatar)
            await _client.ClearAvatarAsync();
        else if (_pendingAvatarBytes is { Length: > 0 } bytes && _pendingAvatarType is { } type)
            await _client.SetAvatarAsync(bytes, type);
        _avatarDirty = false;
    }

    private async Task EditRuleAsync(XmppAlertRule? rule)
    {
        var editor = new XmppAlertRuleWindow(_client, rule, _owner.SelectedConversation)
        {
            Topmost = _owner.Topmost
        };
        await editor.ShowDialog(_owner);
        if (!editor.Saved) return;
        RefreshRules();
    }

    private async Task<bool> EditFilterAsync(XmppMessageFilter? filter, string? prefillSender)
    {
        var editor = new XmppMessageFilterWindow(filter, _owner.SelectedConversation, prefillSender)
        {
            Topmost = _owner.Topmost
        };
        await editor.ShowDialog(_owner);
        if (!editor.Saved || editor.Result is not { } next) return false;
        var index = _filters.FindIndex(item => item.Id == next.Id);
        if (index < 0) _filters.Add(next);
        else _filters[index] = next;
        return true;
    }

    private async void RestorePreferences(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!await XmppDialog.ConfirmAsync(this, "Restore Jabber preferences",
                "Reset display, soft pings, message filters, and custom alert rules to defaults? Account, rooms, presence, avatar, and local history stay as they are."))
            return;
        await _client.ResetPreferencesAsync();
        LoadFields();
        DisplaySaved = true;
        StatusText.Text = "Preferences restored to defaults.";
    });

    private async void ClearLocalHistory(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_client.Account is null) throw new InvalidOperationException("Set up a Jabber account first.");
        if (!await XmppDialog.ConfirmAsync(this, "Clear local history",
                "Remove saved messages and unread counts from this device?"))
            return;
        await _owner.ClearLocalHistoryAsync(confirm: false);
        StatusText.Text = "Local history cleared.";
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        StatusText.Text = "";
        SaveButton.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            StatusText.Text = ex.Message;
        }
        catch
        {
            StatusText.Text = "The preferences action could not be completed.";
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
        }
    }

    private sealed record RuleRow(XmppAlertRule Rule)
    {
        public override string ToString()
        {
            if (Rule.FleetPings)
                return Rule.Enabled ? "Directorbot fleet pings" : "Directorbot fleet pings · Disabled";
            var source = Rule.SourceScope switch
            {
                XmppAlertSourceScope.Anywhere => "Anywhere",
                XmppAlertSourceScope.AnyDirect => "Any direct",
                XmppAlertSourceScope.AnyRoom => "Any room",
                _ => Rule.Source.Jid
            };
            var match = Rule.Match switch
            {
                XmppAlertMatch.ContainsText => $"Contains “{Rule.Text}”",
                XmppAlertMatch.EqualsText => $"Equals “{Rule.Text}”",
                XmppAlertMatch.Regex => $"Regex “{Rule.Text}”",
                XmppAlertMatch.NicknameMention => "Nickname mention",
                _ => "Any message"
            };
            var actions = new[]
            {
                Rule.Popup ? "Popup" : null,
                Rule.Sound,
                Rule.Highlight ? "Highlight" : null,
                Rule.ShowInFleetFeed ? "Notification feed" : null
            }.OfType<string>();
            return $"{source} · {match} · {string.Join(" + ", actions)}";
        }
    }

    private sealed record FilterRow(XmppMessageFilter Filter)
    {
        public override string ToString()
        {
            var action = Filter.Action == XmppMessageFilterAction.Hide ? "Hide" : "Collapse";
            var scope = Filter.Scope is { } conversation ? conversation.Jid : "All conversations";
            var state = Filter.Enabled ? "" : " · Disabled";
            return $"{Filter.Sender} · {action} · {scope}{state}";
        }
    }
}

internal static class XmppAvatarImage
{
    public static (byte[] Bytes, string ContentType) Prepare(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var contentType = extension switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new ArgumentException("Use a JPEG, PNG, or GIF avatar.")
        };
        var raw = File.ReadAllBytes(path);
        if (raw.Length is 0) throw new ArgumentException("The image file is empty.");
        if (raw.Length <= XmppProfileParsing.MaxAvatarBytes) return (raw, contentType);

        using var source = new Bitmap(path);
        for (var edge = 256; edge >= 48; edge -= 32)
        {
            var width = source.PixelSize.Width;
            var height = source.PixelSize.Height;
            if (width <= 0 || height <= 0) break;
            var scale = Math.Min(edge / (double)Math.Max(width, height), 1.0);
            var target = new PixelSize(
                Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)));
            using var scaled = source.CreateScaledBitmap(target);
            using var stream = new MemoryStream();
            scaled.Save(stream);
            if (stream.Length <= XmppProfileParsing.MaxAvatarBytes)
                return (stream.ToArray(), "image/png");
        }

        throw new ArgumentException("Could not shrink the image below 256 KB.");
    }
}
