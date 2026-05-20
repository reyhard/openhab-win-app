using Microsoft.UI.Xaml;

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

internal readonly record struct SidebarLayoutMetrics(
    double SidePadding,
    Thickness NavButtonPadding,
    Thickness BrandMargin,
    double BrandMinHeight,
    double NavButtonHeight,
    double IconLaneWidth);

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

internal readonly record struct CenterContentTransitionPlan(
    bool Animates,
    int DurationMs,
    double StartTranslationY,
    double TargetTranslationY,
    double StartOpacity,
    double TargetOpacity);

internal static class MainWindowShellAnimationPlanner
{
    private const int SidebarChromeDurationMs = 240;
    private const double SitemapPaneDurationMultiplier = 1.5d;
    private const double CenterContentEntranceOffsetY = 24d;

    public static SidebarAnimationPlan CreateSidebarPlan(
        bool targetCollapsed,
        double currentWidth,
        double expandedWidth,
        double collapsedWidth)
    {
        var targetWidth = targetCollapsed ? collapsedWidth : expandedWidth;
        var endLayout = targetCollapsed ? SidebarLayoutState.Collapsed : SidebarLayoutState.Expanded;
        var startLayout = SidebarLayoutState.Collapsed;
        return new SidebarAnimationPlan(
            targetWidth,
            startLayout,
            endLayout,
            Math.Abs(currentWidth - targetWidth) >= 0.5d);
    }

    public static double ResolveSidebarCollapseIconAngle(bool isCollapsed) => isCollapsed ? 180d : 0d;

    public static double ResolveMainUiPagesChevronAngle(bool isExpanded) => isExpanded ? 180d : 0d;

    public static int ResolveSidebarChromeDurationMs() => SidebarChromeDurationMs;

    public static SidebarLayoutMetrics ResolveSidebarLayoutMetrics(SidebarLayoutState layoutState) =>
        new(
            SidePadding: 12d,
            NavButtonPadding: new Thickness(0),
            BrandMargin: new Thickness(0, 0, 0, 14),
            BrandMinHeight: 44d,
            NavButtonHeight: 34d,
            IconLaneWidth: 34d);

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

    public static CenterContentTransitionPlan CreateCenterContentTransitionPlan(int configuredFlyoutMs)
    {
        var durationMs = configuredFlyoutMs <= 0 ? 0 : Math.Max(100, (int)(configuredFlyoutMs * 0.6d));
        return new CenterContentTransitionPlan(
            Animates: durationMs > 0,
            DurationMs: durationMs,
            StartTranslationY: durationMs > 0 ? CenterContentEntranceOffsetY : 0d,
            TargetTranslationY: 0d,
            StartOpacity: durationMs > 0 ? 0d : 1d,
            TargetOpacity: 1d);
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
