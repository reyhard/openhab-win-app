using OpenHab.App.Runtime;

namespace OpenHab.App.Tray;

public readonly record struct TrayFlyoutShowPlan(bool PreloadBeforeShow);

public static class TrayFlyoutShowPlanner
{
    public static TrayFlyoutShowPlan PlanShow(bool isFlyoutVisible, SitemapRuntimeSnapshot runtimeSnapshot)
    {
        ArgumentNullException.ThrowIfNull(runtimeSnapshot);

        return new TrayFlyoutShowPlan(
            PreloadBeforeShow: !isFlyoutVisible && runtimeSnapshot.Descriptor is null);
    }
}
