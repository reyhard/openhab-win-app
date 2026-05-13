using OpenHab.App.Settings;

namespace OpenHab.App.Tests.Settings;

public sealed class SettingsPageTransitionPlannerTests
{
    [Fact]
    public void RootToCategoryUsesForwardDirection()
    {
        var shouldAnimate = SettingsPageTransitionPlanner.TryResolveDirection(
            SettingsPageKind.Root,
            SettingsPageKind.Connection,
            out var direction);

        Assert.True(shouldAnimate);
        Assert.Equal(SettingsPageTransitionDirection.Forward, direction);
    }

    [Fact]
    public void CategoryToRootUsesBackDirection()
    {
        var shouldAnimate = SettingsPageTransitionPlanner.TryResolveDirection(
            SettingsPageKind.Appearance,
            SettingsPageKind.Root,
            out var direction);

        Assert.True(shouldAnimate);
        Assert.Equal(SettingsPageTransitionDirection.Back, direction);
    }

    [Fact]
    public void SamePageDoesNotAnimate()
    {
        var shouldAnimate = SettingsPageTransitionPlanner.TryResolveDirection(
            SettingsPageKind.Shortcuts,
            SettingsPageKind.Shortcuts,
            out _);

        Assert.False(shouldAnimate);
    }
}
