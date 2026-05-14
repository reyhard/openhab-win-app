using OpenHab.App.Tray;

namespace OpenHab.App.Tests.Tray;

public sealed class TrayShellControllerTests
{
    [Fact]
    public void Launch_StartsInBackgroundWithoutVisibleSurface()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
        Assert.False(controller.Current.PendingRefresh);
    }

    [Fact]
    public void HandleLaunchSetsInitialTrayState()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
        Assert.False(controller.Current.PendingRefresh);
        Assert.False(controller.Current.ShouldExitProcess);
    }

    [Fact]
    public void HandlePrimaryTrayClickOpensFlyoutAndRequestsRefresh()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandlePrimaryTrayClick();

        Assert.Equal(TrayShellSurface.Flyout, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
        Assert.True(controller.Current.PendingRefresh);
        Assert.False(controller.Current.ShouldExitProcess);
    }

    [Fact]
    public void HandlePrimaryTrayClickHidesOpenFlyout()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandlePrimaryTrayClick();
        controller.HandlePrimaryTrayClick();

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
    }

    [Fact]
    public void HandleOpenMainWindowShowsMainWindowAndRequestsRefresh()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandleOpenMainWindow();

        Assert.Equal(TrayShellSurface.MainWindow, controller.Current.VisibleSurface);
        Assert.False(controller.Current.IsRunningInBackground);
        Assert.True(controller.Current.PendingRefresh);
        Assert.False(controller.Current.ShouldExitProcess);
    }

    [Fact]
    public void HandleNotificationActivatedOpensMainWindow()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandleNotificationActivated();

        Assert.Equal(TrayShellSurface.MainWindow, controller.Current.VisibleSurface);
        Assert.False(controller.Current.IsRunningInBackground);
        Assert.True(controller.Current.PendingRefresh);
    }

    [Fact]
    public void HandleWindowCloseRequestedHidesToTray()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandlePrimaryTrayClick();
        controller.HandleOpenMainWindow();
        controller.HandleWindowCloseRequested(TrayShellSurface.MainWindow);

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
        Assert.True(controller.Current.PendingRefresh);
        Assert.False(controller.Current.ShouldExitProcess);
    }

    [Fact]
    public void HandleRefreshCompletedClearsPendingRefresh()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandleOpenMainWindow();
        controller.HandleRefreshCompleted();

        Assert.False(controller.Current.PendingRefresh);
        Assert.Equal(TrayShellSurface.MainWindow, controller.Current.VisibleSurface);
    }

    [Fact]
    public void HandleRefreshCompletedWithStaleVersionDoesNotClearNewerPendingRefresh()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandleOpenMainWindow();

        var firstVersion = controller.Current.RefreshRequestVersion;

        controller.HandlePrimaryTrayClick();
        controller.HandlePrimaryTrayClick();
        controller.HandleOpenMainWindow();

        var current = controller.Current;
        Assert.True(current.PendingRefresh);
        Assert.True(current.RefreshRequestVersion > firstVersion);

        controller.HandleRefreshCompleted(firstVersion, TrayShellSurface.MainWindow);

        Assert.True(controller.Current.PendingRefresh);
        Assert.Equal(current.RefreshRequestVersion, controller.Current.RefreshRequestVersion);
    }

    [Fact]
    public void HandleRefreshCompletedWithMatchingVersionAndSurfaceClearsPendingRefresh()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandleOpenMainWindow();

        var state = controller.Current;

        controller.HandleRefreshCompleted(state.RefreshRequestVersion, state.VisibleSurface);

        Assert.False(controller.Current.PendingRefresh);
    }

    [Fact]
    public void HandleExitRequestedSetsTerminalExitState()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandleOpenMainWindow();
        controller.HandleExitRequested();

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.False(controller.Current.IsRunningInBackground);
        Assert.True(controller.Current.PendingRefresh);
        Assert.True(controller.Current.ShouldExitProcess);
    }

    [Fact]
    public void HandlersAfterExitDoNotMutateTerminalState()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();
        controller.HandlePrimaryTrayClick();
        controller.HandleOpenMainWindow();
        controller.HandleExitRequested();

        var exitState = controller.Current;

        controller.HandleLaunch();
        controller.HandlePrimaryTrayClick();
        controller.HandleOpenMainWindow();
        controller.HandleNotificationActivated();
        controller.HandleWindowCloseRequested(TrayShellSurface.MainWindow);
        controller.HandleRefreshCompleted();

        Assert.Equal(exitState, controller.Current);
    }
}
