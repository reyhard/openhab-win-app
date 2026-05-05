using Microsoft.UI.Xaml;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Windows.Tray.Tray;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? window;
    private TrayIconService? trayIcon;
    private bool isShuttingDown;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        var settingsController = new AppSettingsController();
        var renderController = new SitemapRenderController(settingsController);

        window = new MainWindow(settingsController, renderController);
        trayIcon = new TrayIconService(
            showWindow: () =>
            {
                window.Activate();
                window.Refresh();
            },
            exitApplication: () =>
            {
                ShutdownTrayResources();
                Exit();
            });

        window.Activate();
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        ShutdownTrayResources();
    }

    private void ShutdownTrayResources()
    {
        if (isShuttingDown)
        {
            return;
        }

        isShuttingDown = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        trayIcon?.Dispose();
        trayIcon = null;
    }
}
