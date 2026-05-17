using OpenHab.App.Runtime;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapSearchKeyboardPlannerTests
{
    [Fact]
    public void Plan_EscapeClosesVisibleSearch()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.Escape,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: true,
                IsRefreshing: false));

        Assert.Equal(SitemapSearchKeyboardAction.CloseSearch, action);
    }

    [Fact]
    public void Plan_EscapeDoesNothingWhenSearchIsClosed()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.Escape,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: false,
                IsRefreshing: false));

        Assert.Equal(SitemapSearchKeyboardAction.None, action);
    }

    [Fact]
    public void Plan_GoBackClosesVisibleSearchWhenNotRefreshing()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.GoBack,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: true,
                IsRefreshing: false));

        Assert.Equal(SitemapSearchKeyboardAction.CloseSearch, action);
    }

    [Fact]
    public void Plan_GoBackDoesNotCloseSearchWhileRefreshing()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.GoBack,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: true,
                IsRefreshing: true));

        Assert.Equal(SitemapSearchKeyboardAction.None, action);
    }
}
