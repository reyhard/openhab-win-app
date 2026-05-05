using Microsoft.UI.Xaml;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Windows.Tray.Tray;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? window;
    private TrayIconService? trayIcon;
    private DispatcherQueue? uiDispatcherQueue;
    private int isShuttingDown;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

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
        // Shared shutdown path for both tray-initiated exit and process-exit cleanup.
        if (Interlocked.Exchange(ref isShuttingDown, 1) != 0)
        {
            return;
        }

        var dispatcher = uiDispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess && dispatcher.TryEnqueue(ShutdownTrayResourcesCore))
        {
            return;
        }

        ShutdownTrayResourcesCore();
    }

    private void ShutdownTrayResourcesCore()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        trayIcon?.Dispose();
        trayIcon = null;
    }
}
