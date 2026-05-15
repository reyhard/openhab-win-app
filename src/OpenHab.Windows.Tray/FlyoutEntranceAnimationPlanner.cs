using Windows.Graphics;

namespace OpenHab.Windows.Tray;

internal readonly record struct FlyoutEntranceAnimationPlan(
    PointInt32 TargetPosition,
    PointInt32 PreActivationPosition,
    float InitialOpacity,
    float InitialScale);

internal static class FlyoutEntranceAnimationPlanner
{
    public static FlyoutEntranceAnimationPlan Create(PointInt32 targetPosition, int offscreenStartY) =>
        new(
            targetPosition,
            new PointInt32(targetPosition.X, offscreenStartY),
            InitialOpacity: 0f,
            InitialScale: 0.97f);
}
