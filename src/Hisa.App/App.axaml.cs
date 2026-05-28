using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

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
                await Task.WhenAll(Task.Delay(TimeSpan.FromSeconds(2)), vm.InitialLoadTask);
            }
            catch
            {
                // Let MainWindow surface load errors in its status bar.
            }

            var mainWindow = services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
