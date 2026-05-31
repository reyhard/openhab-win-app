using OpenHab.App.Runtime;
using OpenHab.App.Tray;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Tests.Tray;

public sealed class TrayFlyoutShowPlannerTests
{
    [Fact]
    public void PlanShowPreloadsBeforeFirstVisibleFlyoutWhenRuntimeHasNoDescriptor()
    {
        var plan = TrayFlyoutShowPlanner.PlanShow(
            isFlyoutVisible: false,
            runtimeSnapshot: SitemapRuntimeSnapshot.Initial);

        Assert.True(plan.PreloadBeforeShow);
    }

    [Fact]
    public void PlanShowDoesNotPreloadWhenFlyoutAlreadyHasRuntimeContent()
    {
        var snapshot = SitemapRuntimeSnapshot.Initial with
        {
            Descriptor = new SitemapRenderDescriptor(
                SitemapSkinKind.Windows11,
                PageId: "home",
                Title: "Home",
                Rows: [])
        };

        var plan = TrayFlyoutShowPlanner.PlanShow(
            isFlyoutVisible: false,
            runtimeSnapshot: snapshot);

        Assert.False(plan.PreloadBeforeShow);
    }

    [Fact]
    public void PlanShowDoesNotPreloadWhenFlyoutIsAlreadyVisible()
    {
        var plan = TrayFlyoutShowPlanner.PlanShow(
            isFlyoutVisible: true,
            runtimeSnapshot: SitemapRuntimeSnapshot.Initial);

        Assert.False(plan.PreloadBeforeShow);
    }

    [Fact]
    public void FlyoutShellAppliesPreloadPlanBeforeActivation()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "OpenHab.Windows.Tray",
            "App.xaml.cs"));

        var plannerIndex = source.IndexOf("TrayFlyoutShowPlanner.PlanShow", StringComparison.Ordinal);
        var preloadIndex = source.IndexOf("await flyout.LoadRuntimeBeforeShowAsync()", StringComparison.Ordinal);
        var activateIndex = source.IndexOf("flyout.Activate();", StringComparison.Ordinal);

        Assert.True(plannerIndex >= 0);
        Assert.True(preloadIndex > plannerIndex);
        Assert.True(activateIndex > preloadIndex);
    }
}
