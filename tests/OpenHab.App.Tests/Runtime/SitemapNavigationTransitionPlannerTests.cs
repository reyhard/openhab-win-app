using OpenHab.App.Runtime;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapNavigationTransitionPlannerTests
{
    [Fact]
    public void PlanNavigateBlocksWhenRefreshing()
    {
        var result = SitemapNavigationTransitionPlanner.PlanNavigate(
            isRefreshing: true,
            isTransitionRunning: true,
            canNavigate: true,
            direction: SitemapNavigationTransitionDirection.Forward);

        Assert.False(result.ShouldNavigate);
        Assert.False(result.ShouldAnimate);
        Assert.Equal(SitemapNavigationTransitionDirection.Forward, result.Direction);
        Assert.Equal(SitemapNavigationTransitionBlockReason.Refreshing, result.BlockReason);
    }

    [Fact]
    public void PlanNavigateBlocksWhenTransitionIsAlreadyRunning()
    {
        var result = SitemapNavigationTransitionPlanner.PlanNavigate(
            isRefreshing: false,
            isTransitionRunning: true,
            canNavigate: true,
            direction: SitemapNavigationTransitionDirection.Forward);

        Assert.False(result.ShouldNavigate);
        Assert.False(result.ShouldAnimate);
        Assert.Equal(SitemapNavigationTransitionDirection.Forward, result.Direction);
        Assert.Equal(SitemapNavigationTransitionBlockReason.TransitionRunning, result.BlockReason);
    }

    [Fact]
    public void PlanNavigateBlocksWhenNavigationIsUnavailable()
    {
        var result = SitemapNavigationTransitionPlanner.PlanNavigate(
            isRefreshing: false,
            isTransitionRunning: false,
            canNavigate: false,
            direction: SitemapNavigationTransitionDirection.Back);

        Assert.False(result.ShouldNavigate);
        Assert.False(result.ShouldAnimate);
        Assert.Equal(SitemapNavigationTransitionDirection.Back, result.Direction);
        Assert.Equal(SitemapNavigationTransitionBlockReason.NavigationUnavailable, result.BlockReason);
    }

    [Fact]
    public void PlanNavigateAllowsForwardWhenNavigationIsAvailable()
    {
        var result = SitemapNavigationTransitionPlanner.PlanNavigate(
            isRefreshing: false,
            isTransitionRunning: false,
            canNavigate: true,
            direction: SitemapNavigationTransitionDirection.Forward);

        Assert.True(result.ShouldNavigate);
        Assert.True(result.ShouldAnimate);
        Assert.Equal(SitemapNavigationTransitionDirection.Forward, result.Direction);
        Assert.Equal(SitemapNavigationTransitionBlockReason.None, result.BlockReason);
    }

    [Fact]
    public void PlanNavigateAllowsBackWhenNavigationIsAvailable()
    {
        var result = SitemapNavigationTransitionPlanner.PlanNavigate(
            isRefreshing: false,
            isTransitionRunning: false,
            canNavigate: true,
            direction: SitemapNavigationTransitionDirection.Back);

        Assert.True(result.ShouldNavigate);
        Assert.True(result.ShouldAnimate);
        Assert.Equal(SitemapNavigationTransitionDirection.Back, result.Direction);
        Assert.Equal(SitemapNavigationTransitionBlockReason.None, result.BlockReason);
    }
}
