using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Hisa.Services.Xmpp;

namespace Hisa.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = Program.Host!.Services;
            var vm = services.GetRequiredService<MainWindowViewModel>();
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1.2));
            }
            catch
            {
                // Ignore splash delay cancellation/failures.
            }

            _ = vm.InitialLoadTask.ContinueWith(_ =>
            {
                // Let MainWindow surface load errors in its status bar.
            }, TaskScheduler.Default);

            var mainWindow = services.GetRequiredService<MainWindow>();
            var xmpp = services.GetRequiredService<XmppClientService>();
            mainWindow.AttachXmpp(xmpp, services.GetRequiredService<XmppFeedPortraitLoader>());
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.InitializeTray(this, desktop);
            mainWindow.Show();
            splash.Close();
            xmpp.DesktopReady();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
