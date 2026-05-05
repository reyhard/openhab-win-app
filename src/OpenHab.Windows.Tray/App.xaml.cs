using Microsoft.UI.Xaml;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Windows.Tray.Tray;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? window;
    private TrayIconService? trayIcon;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
                trayIcon?.Dispose();
                Exit();
            });

        window.Activate();
    }
}
