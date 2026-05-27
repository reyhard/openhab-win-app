using OpenHab.App.Shell;

namespace OpenHab.App.Tests.Shell;

public sealed class MainWindowShellControllerTests
{
    [Fact]
    public void StartsOnMainUiWithSitemapHidden()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: false);

        Assert.Equal(MainWindowCenterPage.MainUi, controller.Current.CenterPage);
        Assert.False(controller.Current.IsSitemapVisible);
    }

    [Fact]
    public void SelectingCurrentCenterPageDoesNotRaiseChanged()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: false);
        var changedCount = 0;
        controller.Changed += (_, _) => changedCount++;

        controller.SelectCenterPage(MainWindowCenterPage.MainUi);

        Assert.Equal(0, changedCount);
        Assert.Equal(MainWindowCenterPage.MainUi, controller.Current.CenterPage);
    }

    [Fact]
    public void SitemapVisibilitySurvivesSettingsNavigation()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: false);

        controller.SetSitemapVisible(true);
        controller.SelectCenterPage(MainWindowCenterPage.Settings);

        Assert.Equal(MainWindowCenterPage.Settings, controller.Current.CenterPage);
        Assert.True(controller.Current.IsSitemapVisible);
    }

    [Fact]
    public void PromotedMainUiPageSelectionKeepsSitemapVisibility()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: true);

        controller.SelectPromotedMainUiPage("/page/energy");

        Assert.Equal(MainWindowCenterPage.MainUi, controller.Current.CenterPage);
        Assert.True(controller.Current.IsSitemapVisible);
        Assert.Equal("/page/energy", controller.Current.PendingMainUiRoute);
    }

    [Fact]
    public void SelectingCurrentPromotedMainUiRouteDoesNotRaiseChanged()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: true);
        var changedCount = 0;
        controller.Changed += (_, _) => changedCount++;

        controller.SelectPromotedMainUiPage("/page/energy");
        controller.SelectPromotedMainUiPage("/page/energy");

        Assert.Equal(1, changedCount);
        Assert.Equal("/page/energy", controller.Current.PendingMainUiRoute);
    }

    [Fact]
    public void SyncCurrentMainUiRouteUpdatesRouteWithoutChangingShellSections()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: true);
        controller.SelectCenterPage(MainWindowCenterPage.Settings);

        controller.SyncCurrentMainUiRoute("page/lights");

        Assert.Equal(MainWindowCenterPage.Settings, controller.Current.CenterPage);
        Assert.True(controller.Current.IsSitemapVisible);
        Assert.Equal("/page/lights", controller.Current.PendingMainUiRoute);
    }

    [Fact]
    public void SelectingMainUiCenterPageReturnsToMainUiRoot()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: true);

        controller.SelectPromotedMainUiPage("/page/energy");
        controller.SelectCenterPage(MainWindowCenterPage.Settings);
        controller.SelectCenterPage(MainWindowCenterPage.MainUi);

        Assert.Equal(MainWindowCenterPage.MainUi, controller.Current.CenterPage);
        Assert.Equal("/", controller.Current.PendingMainUiRoute);
    }

    [Fact]
    public void SyncCurrentMainUiRouteDoesNotRaiseChangedWhenRouteUnchanged()
    {
        var controller = new MainWindowShellController(initialSitemapVisible: true);
        controller.SelectPromotedMainUiPage("/page/energy");

        var changedCount = 0;
        controller.Changed += (_, _) => changedCount++;

        controller.SyncCurrentMainUiRoute("/page/energy");

        Assert.Equal(0, changedCount);
        Assert.Equal("/page/energy", controller.Current.PendingMainUiRoute);
    }
}
