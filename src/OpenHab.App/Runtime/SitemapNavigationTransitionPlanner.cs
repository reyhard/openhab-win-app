namespace OpenHab.App.Runtime;

public enum SitemapNavigationTransitionDirection
{
    Forward = 0,
    Back = 1
}

public enum SitemapNavigationTransitionBlockReason
{
    None = 0,
    Refreshing = 1,
    TransitionRunning = 2,
    NavigationUnavailable = 3
}

public readonly record struct SitemapNavigationTransitionPlan(
    bool ShouldNavigate,
    bool ShouldAnimate,
    SitemapNavigationTransitionDirection Direction,
    SitemapNavigationTransitionBlockReason BlockReason);

public static class SitemapNavigationTransitionPlanner
{
    public static SitemapNavigationTransitionPlan PlanNavigate(
        bool isRefreshing,
        bool isTransitionRunning,
        bool canNavigate,
        SitemapNavigationTransitionDirection direction)
    {
        if (isRefreshing)
        {
            return new SitemapNavigationTransitionPlan(
                ShouldNavigate: false,
                ShouldAnimate: false,
                Direction: direction,
                BlockReason: SitemapNavigationTransitionBlockReason.Refreshing);
        }

        if (isTransitionRunning)
        {
            return new SitemapNavigationTransitionPlan(
                ShouldNavigate: false,
                ShouldAnimate: false,
                Direction: direction,
                BlockReason: SitemapNavigationTransitionBlockReason.TransitionRunning);
        }

        if (!canNavigate)
        {
            return new SitemapNavigationTransitionPlan(
                ShouldNavigate: false,
                ShouldAnimate: false,
                Direction: direction,
                BlockReason: SitemapNavigationTransitionBlockReason.NavigationUnavailable);
        }

        return new SitemapNavigationTransitionPlan(
            ShouldNavigate: true,
            ShouldAnimate: true,
            Direction: direction,
            BlockReason: SitemapNavigationTransitionBlockReason.None);
    }
}
