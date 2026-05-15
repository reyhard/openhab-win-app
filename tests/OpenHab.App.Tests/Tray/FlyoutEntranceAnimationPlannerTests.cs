using OpenHab.Windows.Tray;
using Windows.Graphics;

namespace OpenHab.App.Tests.Tray;

public class FlyoutEntranceAnimationPlannerTests
{
    [Fact]
    public void Create_ReturnsHiddenOffscreenPreActivationStateAndPreservesTarget()
    {
        var target = new PointInt32(1500, 820);

        var plan = FlyoutEntranceAnimationPlanner.Create(target, offscreenStartY: 1160);

        Assert.Equal(target, plan.TargetPosition);
        Assert.Equal(new PointInt32(1500, 1160), plan.PreActivationPosition);
        Assert.Equal(0f, plan.InitialOpacity);
        Assert.Equal(0.97f, plan.InitialScale);
    }
}
