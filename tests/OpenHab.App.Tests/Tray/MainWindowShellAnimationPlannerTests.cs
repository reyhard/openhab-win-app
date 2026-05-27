using OpenHab.Windows.Tray;

namespace OpenHab.App.Tests.Tray;

public sealed class MainWindowShellAnimationPlannerTests
{
    [Fact]
    public void CreateSidebarPlan_CollapseHidesTextBeforeWidthAnimation()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSidebarPlan(
            targetCollapsed: true,
            currentWidth: 220d,
            expandedWidth: 220d,
            collapsedWidth: 56d);

        Assert.Equal(56d, plan.TargetWidth);
        Assert.Equal(SidebarLayoutState.Collapsed, plan.LayoutAtAnimationStart);
        Assert.Equal(SidebarLayoutState.Collapsed, plan.LayoutAtAnimationEnd);
        Assert.True(plan.AnimatesWidth);
    }

    [Theory]
    [InlineData(false, 0d)]
    [InlineData(true, 180d)]
    public void ResolveSidebarCollapseIconAngle_ReturnsTargetRotation(bool isCollapsed, double expectedAngle)
    {
        Assert.Equal(expectedAngle, MainWindowShellAnimationPlanner.ResolveSidebarCollapseIconAngle(isCollapsed));
    }

    [Theory]
    [InlineData(false, 0d)]
    [InlineData(true, 180d)]
    public void ResolveMainUiPagesChevronAngle_ReturnsTargetRotation(bool isExpanded, double expectedAngle)
    {
        Assert.Equal(expectedAngle, MainWindowShellAnimationPlanner.ResolveMainUiPagesChevronAngle(isExpanded));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldRenderMainUiPagesListItems_FollowsExpandedStateInsteadOfSidebarVisibility(
        bool mainUiPagesExpanded,
        bool sidebarCollapsed,
        bool expected)
    {
        var sidebarLayoutState = sidebarCollapsed ? SidebarLayoutState.Collapsed : SidebarLayoutState.Expanded;

        Assert.Equal(
            expected,
            MainWindowShellAnimationPlanner.ShouldRenderMainUiPagesListItems(mainUiPagesExpanded, sidebarLayoutState));
    }

    [Fact]
    public void CreateSidebarPlan_ExpandKeepsCollapsedLayoutUntilWidthAnimationCompletes()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSidebarPlan(
            targetCollapsed: false,
            currentWidth: 56d,
            expandedWidth: 220d,
            collapsedWidth: 56d);

        Assert.Equal(220d, plan.TargetWidth);
        Assert.Equal(SidebarLayoutState.Collapsed, plan.LayoutAtAnimationStart);
        Assert.Equal(SidebarLayoutState.Expanded, plan.LayoutAtAnimationEnd);
        Assert.True(plan.AnimatesWidth);
    }

    [Fact]
    public void ResolveSidebarChromeDuration_UsesSlightlySlowerMotion()
    {
        Assert.Equal(240, MainWindowShellAnimationPlanner.ResolveSidebarChromeDurationMs());
    }

    [Fact]
    public void ResolveSidebarLayoutMetrics_UsesSameIconLaneInCollapsedAndExpandedLayouts()
    {
        var collapsed = MainWindowShellAnimationPlanner.ResolveSidebarLayoutMetrics(SidebarLayoutState.Collapsed);
        var expanded = MainWindowShellAnimationPlanner.ResolveSidebarLayoutMetrics(SidebarLayoutState.Expanded);

        Assert.Equal(expanded.SidePadding, collapsed.SidePadding);
        Assert.Equal(expanded.NavButtonPadding, collapsed.NavButtonPadding);
        Assert.Equal(expanded.BrandMargin.Left, collapsed.BrandMargin.Left);
        Assert.Equal(expanded.BrandMinHeight, collapsed.BrandMinHeight);
        Assert.Equal(34d, collapsed.NavButtonHeight);
        Assert.Equal(expanded.NavButtonHeight, collapsed.NavButtonHeight);
        Assert.Equal(34d, collapsed.IconLaneWidth);
        Assert.Equal(expanded.IconLaneWidth, collapsed.IconLaneWidth);
    }

    [Fact]
    public void CreatePageListPlan_ExpandStartsVisibleAtZeroHeight()
    {
        var plan = MainWindowShellAnimationPlanner.CreatePageListPlan(
            targetVisible: true,
            currentHeight: 0d,
            desiredHeight: 96d);

        Assert.True(plan.VisibleAtAnimationStart);
        Assert.True(plan.VisibleAtAnimationEnd);
        Assert.Equal(0d, plan.StartHeight);
        Assert.Equal(96d, plan.TargetHeight);
        Assert.Equal(0d, plan.StartOpacity);
        Assert.Equal(1d, plan.TargetOpacity);
    }

    [Fact]
    public void CreatePageListPlan_RefreshVisibleListKeepsCurrentOpacity()
    {
        var plan = MainWindowShellAnimationPlanner.CreatePageListPlan(
            targetVisible: true,
            currentHeight: 64d,
            desiredHeight: 96d);

        Assert.True(plan.VisibleAtAnimationStart);
        Assert.Equal(64d, plan.StartHeight);
        Assert.Equal(96d, plan.TargetHeight);
        Assert.Equal(1d, plan.StartOpacity);
        Assert.Equal(1d, plan.TargetOpacity);
    }

    [Fact]
    public void CreatePageListPlan_HiddenListMeasuredForExpansionStillStartsAtZeroHeight()
    {
        var plan = MainWindowShellAnimationPlanner.CreatePageListPlan(
            targetVisible: true,
            wasVisibleBeforeMeasure: false,
            currentHeightAfterMeasure: 96d,
            desiredHeight: 96d);

        Assert.True(plan.VisibleAtAnimationStart);
        Assert.True(plan.VisibleAtAnimationEnd);
        Assert.Equal(0d, plan.StartHeight);
        Assert.Equal(96d, plan.TargetHeight);
        Assert.Equal(0d, plan.StartOpacity);
        Assert.Equal(1d, plan.TargetOpacity);
    }

    [Fact]
    public void CreatePageListPlan_CollapseKeepsVisibleUntilAnimationCompletes()
    {
        var plan = MainWindowShellAnimationPlanner.CreatePageListPlan(
            targetVisible: false,
            currentHeight: 96d,
            desiredHeight: 96d);

        Assert.True(plan.VisibleAtAnimationStart);
        Assert.False(plan.VisibleAtAnimationEnd);
        Assert.Equal(96d, plan.StartHeight);
        Assert.Equal(0d, plan.TargetHeight);
        Assert.Equal(1d, plan.StartOpacity);
        Assert.Equal(0d, plan.TargetOpacity);
    }

    [Fact]
    public void CreateSitemapPanePlan_ShowStartsVisibleAtZeroWidthAndOpacity()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSitemapPanePlan(
            targetVisible: true,
            currentWidth: 0d,
            expandedWidth: 380d);

        Assert.True(plan.VisibleAtAnimationStart);
        Assert.True(plan.VisibleAtAnimationEnd);
        Assert.Equal(0d, plan.StartWidth);
        Assert.Equal(380d, plan.TargetWidth);
        Assert.Equal(0d, plan.StartOpacity);
        Assert.Equal(1d, plan.TargetOpacity);
        Assert.True(plan.Animates);
    }

    [Fact]
    public void CreateSitemapPanePlan_ShowAnimatesClipWidthFromZeroToExpandedWidth()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSitemapPanePlan(
            targetVisible: true,
            currentWidth: 0d,
            expandedWidth: 380d);

        Assert.Equal(0d, plan.StartClipWidth);
        Assert.Equal(380d, plan.TargetClipWidth);
    }

    [Fact]
    public void CreateSitemapPanePlan_ShowSlidesPaneAcrossFullWidth()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSitemapPanePlan(
            targetVisible: true,
            currentWidth: 0d,
            expandedWidth: 380d);

        Assert.Equal(380d, plan.StartTranslationX);
        Assert.Equal(0d, plan.TargetTranslationX);
    }

    [Fact]
    public void CreateSitemapPanePlan_HideKeepsVisibleUntilAnimationCompletes()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSitemapPanePlan(
            targetVisible: false,
            currentWidth: 380d,
            expandedWidth: 380d);

        Assert.True(plan.VisibleAtAnimationStart);
        Assert.False(plan.VisibleAtAnimationEnd);
        Assert.Equal(380d, plan.StartWidth);
        Assert.Equal(0d, plan.TargetWidth);
        Assert.Equal(1d, plan.StartOpacity);
        Assert.Equal(0d, plan.TargetOpacity);
        Assert.True(plan.Animates);
    }

    [Fact]
    public void CreateSitemapPanePlan_HideSlidesPaneAcrossFullWidth()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSitemapPanePlan(
            targetVisible: false,
            currentWidth: 380d,
            expandedWidth: 380d);

        Assert.Equal(0d, plan.StartTranslationX);
        Assert.Equal(380d, plan.TargetTranslationX);
    }

    [Fact]
    public void CreateSitemapPanePlan_HideVisiblePaneUsesExpandedWidthWhenLayoutReportsNarrowWidth()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSitemapPanePlan(
            targetVisible: false,
            currentWidth: 5d,
            expandedWidth: 380d,
            isCurrentlyVisible: true);

        Assert.Equal(380d, plan.StartWidth);
        Assert.Equal(380d, plan.StartClipWidth);
        Assert.Equal(0d, plan.StartTranslationX);
        Assert.True(plan.Animates);
    }

    [Fact]
    public void CreateSitemapPanePlan_PartialWidthStartsWithMatchingOpacity()
    {
        var plan = MainWindowShellAnimationPlanner.CreateSitemapPanePlan(
            targetVisible: true,
            currentWidth: 190d,
            expandedWidth: 380d);

        Assert.Equal(190d, plan.StartWidth);
        Assert.Equal(0.5d, plan.StartOpacity);
        Assert.Equal(1d, plan.TargetOpacity);
    }

    [Fact]
    public void ResolveSitemapPaneDuration_UsesSlowerChromeMotion()
    {
        Assert.Equal(450, MainWindowShellAnimationPlanner.ResolveSitemapPaneDurationMs(300));
        Assert.Equal(0, MainWindowShellAnimationPlanner.ResolveSitemapPaneDurationMs(0));
    }

    [Fact]
    public void CreateCenterContentTransitionPlan_UsesBottomToTopEntrance()
    {
        var plan = MainWindowShellAnimationPlanner.CreateCenterContentTransitionPlan(configuredFlyoutMs: 300);

        Assert.True(plan.Animates);
        Assert.Equal(24d, plan.StartTranslationY);
        Assert.Equal(0d, plan.TargetTranslationY);
        Assert.Equal(0d, plan.StartOpacity);
        Assert.Equal(1d, plan.TargetOpacity);
        Assert.Equal(180, plan.DurationMs);
    }

    [Fact]
    public void CreateCenterContentTransitionPlan_DisablesAnimationWhenConfiguredOff()
    {
        var plan = MainWindowShellAnimationPlanner.CreateCenterContentTransitionPlan(configuredFlyoutMs: 0);

        Assert.False(plan.Animates);
        Assert.Equal(0, plan.DurationMs);
    }
}
