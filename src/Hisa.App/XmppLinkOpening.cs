using Avalonia.Controls;
using Avalonia.Media;
using Hisa.App.Services;
using Hisa.Services.Xmpp;

namespace Hisa.App;

internal static class XmppLinkOpening
{
    public static async Task<bool> OpenAsync(Window owner, XmppClientService client, Uri url)
    {
        if (!await ConfirmAsync(owner, client, url)) return false;
        ExternalUrlLauncher.Open(url.AbsoluteUri);
        return true;
    }

    internal static async Task<bool> ConfirmAsync(Window owner, XmppClientService client, Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https")) return false;
        var host = url.IdnHost;
        if (client.IsTrustedLinkHost(host)) return true;

        var dialog = new XmppDialog("Open link");
        dialog.Add(new TextBlock
        {
            Text = $"Open this link?\n{url.AbsoluteUri}",
            TextWrapping = TextWrapping.Wrap
        });
        var remember = dialog.Check($"Don't warn again for {host}", false);
        var confirmed = false;
        await dialog.OpenAsync(owner, "Open", () => Task.FromResult(confirmed = true));
        if (confirmed && remember.IsChecked == true)
            await client.TrustLinkHostAsync(host);
        return confirmed;
    }
}
