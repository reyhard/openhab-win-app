namespace OpenHab.Windows.Tray;

internal enum SidebarLayoutState
{
    Expanded,
    Collapsed
}

internal readonly record struct SidebarAnimationPlan(
    double TargetWidth,
    SidebarLayoutState LayoutAtAnimationStart,
    SidebarLayoutState LayoutAtAnimationEnd,
    bool AnimatesWidth);

internal readonly record struct PageListAnimationPlan(
    bool VisibleAtAnimationStart,
    bool VisibleAtAnimationEnd,
    double StartHeight,
    double TargetHeight,
    double StartOpacity,
    double TargetOpacity);

internal readonly record struct SitemapPaneAnimationPlan(
    bool VisibleAtAnimationStart,
    bool VisibleAtAnimationEnd,
    double StartWidth,
    double TargetWidth,
    double StartClipWidth,
    double TargetClipWidth,
    double StartTranslationX,
    double TargetTranslationX,
    double StartOpacity,
    double TargetOpacity,
    bool Animates);

internal static class MainWindowShellAnimationPlanner
{
    private const double SitemapPaneDurationMultiplier = 1.5d;

    public static SidebarAnimationPlan CreateSidebarPlan(
        bool targetCollapsed,
        double currentWidth,
        double expandedWidth,
        double collapsedWidth)
    {
        var targetWidth = targetCollapsed ? collapsedWidth : expandedWidth;
        var startLayout = targetCollapsed ? SidebarLayoutState.Expanded : SidebarLayoutState.Collapsed;
        var endLayout = targetCollapsed ? SidebarLayoutState.Collapsed : SidebarLayoutState.Expanded;
        return new SidebarAnimationPlan(
            targetWidth,
            startLayout,
            endLayout,
            Math.Abs(currentWidth - targetWidth) >= 0.5d);
    }

    public static PageListAnimationPlan CreatePageListPlan(
        bool targetVisible,
        double currentHeight,
        double desiredHeight)
    {
        return CreatePageListPlan(
            targetVisible,
            wasVisibleBeforeMeasure: currentHeight > 0.5d,
            currentHeightAfterMeasure: currentHeight,
            desiredHeight);
    }

    public static PageListAnimationPlan CreatePageListPlan(
        bool targetVisible,
        bool wasVisibleBeforeMeasure,
        double currentHeightAfterMeasure,
        double desiredHeight)
    {
        var measuredHeight = Math.Max(0d, desiredHeight);
        if (targetVisible)
        {
            var startHeight = wasVisibleBeforeMeasure ? Math.Max(0d, currentHeightAfterMeasure) : 0d;
            return new PageListAnimationPlan(
                VisibleAtAnimationStart: true,
                VisibleAtAnimationEnd: true,
                StartHeight: startHeight,
                TargetHeight: measuredHeight,
                StartOpacity: wasVisibleBeforeMeasure ? 1d : 0d,
                TargetOpacity: 1d);
        }

        return new PageListAnimationPlan(
            VisibleAtAnimationStart: wasVisibleBeforeMeasure && Math.Max(currentHeightAfterMeasure, measuredHeight) > 0d,
            VisibleAtAnimationEnd: false,
            StartHeight: wasVisibleBeforeMeasure ? Math.Max(0d, Math.Max(currentHeightAfterMeasure, measuredHeight)) : 0d,
            TargetHeight: 0d,
            StartOpacity: 1d,
            TargetOpacity: 0d);
    }

    public static SitemapPaneAnimationPlan CreateSitemapPanePlan(
        bool targetVisible,
        double currentWidth,
        double expandedWidth) =>
        CreateSitemapPanePlan(targetVisible, currentWidth, expandedWidth, isCurrentlyVisible: currentWidth > 0.5d);

    public static SitemapPaneAnimationPlan CreateSitemapPanePlan(
        bool targetVisible,
        double currentWidth,
        double expandedWidth,
        bool isCurrentlyVisible)
    {
        var fullWidth = Math.Max(0d, expandedWidth);
        var startWidth = !targetVisible && isCurrentlyVisible
            ? fullWidth
            : Math.Max(0d, currentWidth);
        var targetWidth = targetVisible ? fullWidth : 0d;
        var opacityFromWidth = Math.Clamp(startWidth / Math.Max(fullWidth, 1d), 0d, 1d);
        var startTranslationX = Math.Max(0d, fullWidth - startWidth);
        var targetTranslationX = targetVisible ? 0d : fullWidth;
        var startOpacity = ResolveSitemapPaneStartOpacity(startWidth, opacityFromWidth, targetVisible);
        return new SitemapPaneAnimationPlan(
            VisibleAtAnimationStart: targetVisible || startWidth > 0.5d,
            VisibleAtAnimationEnd: targetVisible,
            StartWidth: startWidth,
            TargetWidth: targetWidth,
            StartClipWidth: startWidth,
            TargetClipWidth: targetWidth,
            StartTranslationX: startTranslationX,
            TargetTranslationX: targetTranslationX,
            StartOpacity: startOpacity,
            TargetOpacity: targetVisible ? 1d : 0d,
            Animates: Math.Abs(startWidth - targetWidth) >= 0.5d);
    }

    public static int ResolveSitemapPaneDurationMs(int configuredFlyoutMs)
    {
        return configuredFlyoutMs <= 0 ? 0 : (int)Math.Round(configuredFlyoutMs * SitemapPaneDurationMultiplier);
    }

    private static double ResolveSitemapPaneStartOpacity(double startWidth, double opacityFromWidth, bool targetVisible)
    {
        if (startWidth > 0.5d)
        {
            return opacityFromWidth;
        }

        return targetVisible ? 0d : 1d;
    }
}
