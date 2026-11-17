using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hisa.Core.Models;

namespace Hisa.App;

internal partial class XmppMessageFilterWindow : Window
{
    private readonly XmppMessageFilter? _existing;
    private readonly XmppConversation? _defaultScope;
    private bool _busy;

    public bool Saved { get; private set; }
    public XmppMessageFilter? Result { get; private set; }

    public XmppMessageFilterWindow(XmppMessageFilter? filter, XmppConversation? defaultScope,
        string? prefillSender = null)
    {
        _existing = filter;
        _defaultScope = filter?.Scope ?? defaultScope;
        InitializeComponent();
        TitleText.Text = filter is null ? "Add message filter" : "Edit message filter";
        LoadFields(prefillSender);
    }

    private void LoadFields(string? prefillSender)
    {
        SenderBox.Text = _existing?.Sender ?? prefillSender;
        ActionBox.ItemsSource = new[] { "Collapse", "Hide" };
        ActionBox.SelectedIndex = _existing?.Action == XmppMessageFilterAction.Hide ? 1 : 0;
        ScopeBox.ItemsSource = new[] { "All conversations", "Specific conversation" };
        ScopeBox.SelectedIndex = _existing?.Scope is not null ? 1 : 0;
        KindBox.ItemsSource = new[] { "Direct message", "Room" };
        var scope = _existing?.Scope ?? _defaultScope;
        KindBox.SelectedIndex = (int)(scope?.Kind ?? XmppConversationKind.Direct);
        AddressBox.Text = scope?.Jid;
        EnabledBox.IsChecked = _existing?.Enabled ?? true;
        ScopeChanged(null, null!);
    }

    private void ScopeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var specific = ScopeBox.SelectedIndex == 1;
        AddressBox.IsVisible = AddressLabel.IsVisible =
            KindBox.IsVisible = KindLabel.IsVisible = specific;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        StatusText.Text = "";
        try
        {
            if (string.IsNullOrWhiteSpace(SenderBox.Text))
                throw new ArgumentException("Enter a sender nick or JID.");

            XmppConversation? scope = null;
            if (ScopeBox.SelectedIndex == 1)
            {
                var jid = XmppAddresses.Bare(AddressBox.Text?.Trim());
                if (jid is null || AddressBox.Text?.Contains('/') == true)
                    throw new ArgumentException("Enter a valid conversation address.");
                scope = new((XmppConversationKind)Math.Clamp(KindBox.SelectedIndex, 0, 1), jid);
            }

            Result = new XmppMessageFilter(
                _existing?.Id ?? Guid.NewGuid(),
                SenderBox.Text.Trim(),
                ActionBox.SelectedIndex == 1 ? XmppMessageFilterAction.Hide : XmppMessageFilterAction.Collapse,
                scope,
                EnabledBox.IsChecked == true);
            Saved = true;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            StatusText.Text = ex.Message;
        }
        catch
        {
            StatusText.Text = "The message filter could not be saved.";
        }
        finally { _busy = false; }
    }
}
