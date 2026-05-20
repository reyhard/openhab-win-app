using OpenHab.App.Shortcuts;
using OpenHab.Windows.Tray.Shortcuts;
using System.Drawing;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class RadialCommandMenuLogicTests
{
    [Fact]
    public void ResolveHoverCaptionUsesActionName()
    {
        var action = new ShortcutAction(
            "a1",
            "Kitchen dimmer",
            "brightness",
            true,
            null,
            "Kitchen_Dimmer",
            ShortcutCommandType.OpenSlider,
            null);

        var caption = RadialCommandMenuLogic.ResolveHoverCaption(RadialCommandMenuHoverTarget.Action(action));

        Assert.Equal("Kitchen dimmer", caption);
    }

    [Fact]
    public void ResolveHoverCaptionUsesBuiltInMenuLabels()
    {
        Assert.Equal("Close", RadialCommandMenuLogic.ResolveHoverCaption(RadialCommandMenuHoverTarget.Close));
        Assert.Equal("More", RadialCommandMenuLogic.ResolveHoverCaption(RadialCommandMenuHoverTarget.PageAdvance));
    }

    [Theory]
    [InlineData(220, 138, RadialCommandMenuCaptionPlacement.Above)]
    [InlineData(220, 302, RadialCommandMenuCaptionPlacement.Below)]
    [InlineData(302, 220, RadialCommandMenuCaptionPlacement.Right)]
    [InlineData(138, 220, RadialCommandMenuCaptionPlacement.Left)]
    [InlineData(220, 220, RadialCommandMenuCaptionPlacement.Above)]
    public void ResolveHoverCaptionPlacementMovesTextOutward(
        float centerX,
        float centerY,
        RadialCommandMenuCaptionPlacement expected)
    {
        var button = new RectangleF(centerX - 26, centerY - 26, 52, 52);

        var placement = RadialCommandMenuLogic.ResolveHoverCaptionPlacement(button, new SizeF(440, 440));

        Assert.Equal(expected, placement);
    }

    [Theory]
    [InlineData(220, 138, RadialCommandMenuCaptionPlacement.Above)]
    [InlineData(220, 302, RadialCommandMenuCaptionPlacement.Below)]
    [InlineData(302, 220, RadialCommandMenuCaptionPlacement.Right)]
    [InlineData(138, 220, RadialCommandMenuCaptionPlacement.Left)]
    public void ResolveHoverCaptionBoundsKeepsCaptionOutsideButton(
        float centerX,
        float centerY,
        RadialCommandMenuCaptionPlacement expected)
    {
        var button = new RectangleF(centerX - 26, centerY - 26, 52, 52);

        var bounds = RadialCommandMenuLogic.ResolveHoverCaptionBounds(
            button,
            new SizeF(90, 24),
            new SizeF(440, 440),
            gap: 7,
            edgePadding: 6);

        switch (expected)
        {
            case RadialCommandMenuCaptionPlacement.Above:
                Assert.True(bounds.Bottom <= button.Top);
                break;
            case RadialCommandMenuCaptionPlacement.Below:
                Assert.True(bounds.Top >= button.Bottom);
                break;
            case RadialCommandMenuCaptionPlacement.Left:
                Assert.True(bounds.Right <= button.Left);
                break;
            case RadialCommandMenuCaptionPlacement.Right:
                Assert.True(bounds.Left >= button.Right);
                break;
        }
    }

    [Fact]
    public void ResolveAnimatedButtonBoundsOpensFromMenuCenterAtFivePercentScale()
    {
        var finalBounds = new RectangleF(194, 112, 52, 52);

        var state = RadialCommandMenuLogic.ResolveAnimatedButtonState(
            finalBounds,
            new SizeF(440, 440),
            RadialCommandMenuAnimationKind.Opening,
            elapsed: TimeSpan.Zero,
            delay: TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMilliseconds(450), RadialCommandMenuLogic.OpeningAnimationDuration);
        Assert.Equal(new PointF(220, 220), state.Center);
        Assert.Equal(0.05f, state.Scale, precision: 3);
        Assert.Equal(0, state.Alpha);
    }

    [Fact]
    public void ResolveAnimatedButtonBoundsOpensToFinalBoundsAfterDuration()
    {
        var finalBounds = new RectangleF(194, 112, 52, 52);

        var state = RadialCommandMenuLogic.ResolveAnimatedButtonState(
            finalBounds,
            new SizeF(440, 440),
            RadialCommandMenuAnimationKind.Opening,
            RadialCommandMenuLogic.OpeningAnimationDuration,
            delay: TimeSpan.Zero);

        Assert.Equal(new PointF(220, 138), state.Center);
        Assert.Equal(1f, state.Scale, precision: 3);
        Assert.Equal(255, state.Alpha);
    }

    [Fact]
    public void ResolveAnimatedButtonBoundsOpensByScalingAtCenterBeforeMovingOutward()
    {
        var finalBounds = new RectangleF(276, 194, 52, 52);
        var elapsed = TimeSpan.FromMilliseconds(
            RadialCommandMenuLogic.OpeningAnimationDuration.TotalMilliseconds
            * RadialCommandMenuLogic.OpenCenterScaleProgress);

        var state = RadialCommandMenuLogic.ResolveAnimatedButtonState(
            finalBounds,
            new SizeF(440, 440),
            RadialCommandMenuAnimationKind.Opening,
            elapsed,
            delay: TimeSpan.Zero);

        Assert.Equal(new PointF(220, 220), state.Center);
        Assert.Equal(0.85f, state.Scale, precision: 3);
        Assert.Equal(255, state.Alpha);
    }

    [Fact]
    public void ResolveAnimatedButtonBoundsHonorsOpeningDelay()
    {
        var finalBounds = new RectangleF(276, 194, 52, 52);

        var state = RadialCommandMenuLogic.ResolveAnimatedButtonState(
            finalBounds,
            new SizeF(440, 440),
            RadialCommandMenuAnimationKind.Opening,
            elapsed: TimeSpan.FromMilliseconds(40),
            delay: TimeSpan.FromMilliseconds(80));

        Assert.Equal(new PointF(220, 220), state.Center);
        Assert.Equal(0.05f, state.Scale, precision: 3);
        Assert.Equal(0, state.Alpha);
    }

    [Fact]
    public void ResolveAnimatedButtonBoundsCollapsesPositionBeforeFinalShrink()
    {
        var finalBounds = new RectangleF(276, 194, 52, 52);
        var elapsed = TimeSpan.FromMilliseconds(
            RadialCommandMenuLogic.ClosingAnimationDuration.TotalMilliseconds
            * RadialCommandMenuLogic.CollapseConvergedProgress);

        var state = RadialCommandMenuLogic.ResolveAnimatedButtonState(
            finalBounds,
            new SizeF(440, 440),
            RadialCommandMenuAnimationKind.Closing,
            elapsed,
            delay: TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMilliseconds(350), RadialCommandMenuLogic.ClosingAnimationDuration);
        Assert.Equal(new PointF(220, 220), state.Center);
        Assert.Equal(0.85f, state.Scale, precision: 3);
        Assert.Equal(255, state.Alpha);
    }

    [Fact]
    public void ResolveAnimatedButtonBoundsClosesAtCenterWithFivePercentScale()
    {
        var finalBounds = new RectangleF(276, 194, 52, 52);

        var state = RadialCommandMenuLogic.ResolveAnimatedButtonState(
            finalBounds,
            new SizeF(440, 440),
            RadialCommandMenuAnimationKind.Closing,
            RadialCommandMenuLogic.ClosingAnimationDuration,
            delay: TimeSpan.Zero);

        Assert.Equal(new PointF(220, 220), state.Center);
        Assert.Equal(0.05f, state.Scale, precision: 3);
        Assert.Equal(0, state.Alpha);
    }
}
