using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using OpenHab.Windows.Notifications;
using WinUIEx;

namespace OpenHab.Windows.Tray.Tray;

[ExcludeFromCodeCoverage(Justification = "NotifyIcon shell integration.")]
public sealed partial class TrayIconService : IDisposable
{
    private readonly TrayIcon trayIcon;
    private int isDisposed;

    public TrayIconService(Action toggleFlyout, Action openMainWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(toggleFlyout);
        ArgumentNullException.ThrowIfNull(openMainWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        // WinUIEx TrayIcon requires an .ico file (GDI LoadImage) and a unique uint ID.
        // The .ico is generated from Assets/openhab-icon.svg and copied to output.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "openhab-icon.ico");
        trayIcon = new TrayIcon(trayiconId: 1, iconPath, tooltip: "openHAB");

        trayIcon.Selected += (_, _) => toggleFlyout();
        trayIcon.ContextMenu += (_, e) =>
        {
            e.Flyout = new MenuFlyout
            {
                Items =
                {
                    new MenuFlyoutItem { Text = "Open flyout", Command = new RelayCommand(toggleFlyout) },
                    new MenuFlyoutItem { Text = "Open main window", Command = new RelayCommand(openMainWindow) },
                    new MenuFlyoutSeparator(),
                    new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(exitApplication) }
                }
            };
        };

        trayIcon.IsVisible = true;
    }

    /// <summary>
    /// Shows a toast notification. WinUIEx does not provide balloon tips natively;
    /// this delegates to the CommunityToolkit toast service for proper Action Center toasts.
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        if (isDisposed != 0) return;
        ToastService.Show(title, text);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        trayIcon.Dispose();
    }
}

/// <summary>
/// Minimal ICommand implementation for MenuFlyout items.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "NotifyIcon shell menu command glue.")]
internal sealed partial class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action execute;
    public RelayCommand(Action execute) => this.execute = execute;
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
