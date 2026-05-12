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
}
