namespace OpenHab.App.Tray;

public sealed class TrayShellController
{
    public TrayShellState Current { get; private set; } = TrayShellState.Initial;

    public void HandleLaunch()
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        Current = TrayShellState.Initial;
    }

    public void HandlePrimaryTrayClick()
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        if (Current.VisibleSurface == TrayShellSurface.Flyout)
        {
            Current = Current with
            {
                VisibleSurface = TrayShellSurface.None,
                IsRunningInBackground = true
            };
            return;
        }

        Current = Current with
        {
            VisibleSurface = TrayShellSurface.Flyout,
            IsRunningInBackground = true
        };
        RequestRefresh();
    }

    public void HandleOpenMainWindow()
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        Current = Current with
        {
            VisibleSurface = TrayShellSurface.MainWindow,
            IsRunningInBackground = false
        };
        RequestRefresh();
    }

    public void HandleNotificationActivated()
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        HandleOpenMainWindow();
    }

    public void HandleWindowCloseRequested(TrayShellSurface surface)
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        if (surface is TrayShellSurface.None)
        {
            return;
        }

        Current = Current with
        {
            VisibleSurface = TrayShellSurface.None,
            IsRunningInBackground = true
        };
    }

    public void HandleRefreshCompleted()
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        Current = Current with { PendingRefresh = false };
    }

    public void HandleRefreshCompleted(long refreshRequestVersion, TrayShellSurface visibleSurface)
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        if (!Current.PendingRefresh)
        {
            return;
        }

        if (Current.RefreshRequestVersion != refreshRequestVersion)
        {
            return;
        }

        if (Current.VisibleSurface != visibleSurface)
        {
            return;
        }

        Current = Current with { PendingRefresh = false };
    }

    public void HandleExitRequested()
    {
        Current = Current with
        {
            VisibleSurface = TrayShellSurface.None,
            IsRunningInBackground = false,
            ShouldExitProcess = true
        };
    }

    private void RequestRefresh()
    {
        Current = Current with
        {
            PendingRefresh = true,
            RefreshRequestVersion = Current.RefreshRequestVersion + 1
        };
    }
}
