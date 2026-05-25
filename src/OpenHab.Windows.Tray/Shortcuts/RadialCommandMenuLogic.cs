using OpenHab.App.Shortcuts;
using System.Drawing;

namespace OpenHab.Windows.Tray.Shortcuts;

internal enum RadialCommandMenuHoverKind
{
    None,
    Close,
    PageAdvance,
    Action
}

public enum RadialCommandMenuCaptionPlacement
{
    Above,
    Below,
    Left,
    Right
}

internal enum RadialCommandMenuAnimationKind
{
    Opening,
    Closing
}

internal readonly record struct RadialCommandMenuAnimationState(
    PointF Center,
    float Scale,
    byte Alpha);

internal readonly record struct RadialCommandMenuHoverTarget(
    RadialCommandMenuHoverKind Kind,
    ShortcutAction? ShortcutAction)
{
    public static RadialCommandMenuHoverTarget None { get; } = new(RadialCommandMenuHoverKind.None, null);
    public static RadialCommandMenuHoverTarget Close { get; } = new(RadialCommandMenuHoverKind.Close, null);
    public static RadialCommandMenuHoverTarget PageAdvance { get; } = new(RadialCommandMenuHoverKind.PageAdvance, null);

    public static RadialCommandMenuHoverTarget Action(ShortcutAction action)
    {
        return new RadialCommandMenuHoverTarget(RadialCommandMenuHoverKind.Action, action);
    }
}

internal static class RadialCommandMenuLogic
{
    public static readonly TimeSpan OpeningAnimationDuration = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan ClosingAnimationDuration = TimeSpan.FromMilliseconds(350);
    public const float OpenCenterScaleProgress = 0.32f;
    public const float CollapseConvergedProgress = 0.68f;

    public static string ResolveHoverCaption(RadialCommandMenuHoverTarget target)
    {
        return target.Kind switch
        {
            RadialCommandMenuHoverKind.Close => "Close",
            RadialCommandMenuHoverKind.PageAdvance => "More",
            RadialCommandMenuHoverKind.Action => ResolveActionCaption(target.ShortcutAction),
            _ => string.Empty
        };
    }

    public static RadialCommandMenuAnimationState ResolveAnimatedButtonState(
        RectangleF finalBounds,
        SizeF menuSize,
        RadialCommandMenuAnimationKind kind,
        TimeSpan elapsed,
        TimeSpan delay)
    {
        var adjustedElapsed = elapsed - delay;
        var finalCenter = new PointF(
            finalBounds.Left + (finalBounds.Width / 2f),
            finalBounds.Top + (finalBounds.Height / 2f));
        var menuCenter = new PointF(menuSize.Width / 2f, menuSize.Height / 2f);

        if (kind == RadialCommandMenuAnimationKind.Opening)
        {
            var progress = ResolveProgress(adjustedElapsed, OpeningAnimationDuration);
            if (progress <= OpenCenterScaleProgress)
            {
                var scaleProgress = EaseIn(progress / OpenCenterScaleProgress);
                return new RadialCommandMenuAnimationState(
                    menuCenter,
                    Lerp(0.05f, 0.85f, scaleProgress),
                    ToAlpha(scaleProgress));
            }

            var expandProgress = EaseInOut((progress - OpenCenterScaleProgress) / (1f - OpenCenterScaleProgress));
            return new RadialCommandMenuAnimationState(
                Lerp(menuCenter, finalCenter, expandProgress),
                Lerp(0.85f, 1f, expandProgress),
                255);
        }

        var closingProgress = ResolveProgress(adjustedElapsed, ClosingAnimationDuration);
        if (closingProgress <= CollapseConvergedProgress)
        {
            var convergeProgress = closingProgress / CollapseConvergedProgress;
            var eased = EaseInOut(convergeProgress);
            return new RadialCommandMenuAnimationState(
                Lerp(finalCenter, menuCenter, eased),
                Lerp(1f, 0.85f, eased),
                255);
        }

        var shrinkProgress = (closingProgress - CollapseConvergedProgress) / (1f - CollapseConvergedProgress);
        var shrinkEased = EaseIn(shrinkProgress);
        return new RadialCommandMenuAnimationState(
            menuCenter,
            Lerp(0.85f, 0.05f, shrinkEased),
            ToAlpha(1f - shrinkEased));
    }

    public static RadialCommandMenuCaptionPlacement ResolveHoverCaptionPlacement(RectangleF buttonBounds, SizeF menuSize)
    {
        var buttonCenterX = buttonBounds.Left + (buttonBounds.Width / 2f);
        var buttonCenterY = buttonBounds.Top + (buttonBounds.Height / 2f);
        var menuCenterX = menuSize.Width / 2f;
        var menuCenterY = menuSize.Height / 2f;
        var dx = buttonCenterX - menuCenterX;
        var dy = buttonCenterY - menuCenterY;

        if (Math.Abs(dx) <= buttonBounds.Width * 0.35f && Math.Abs(dy) <= buttonBounds.Height * 0.35f)
        {
            return RadialCommandMenuCaptionPlacement.Above;
        }

        if (Math.Abs(dy) > Math.Abs(dx))
        {
            return dy < 0
                ? RadialCommandMenuCaptionPlacement.Above
                : RadialCommandMenuCaptionPlacement.Below;
        }

        return dx < 0
            ? RadialCommandMenuCaptionPlacement.Left
            : RadialCommandMenuCaptionPlacement.Right;
    }

    public static RectangleF ResolveHoverCaptionBounds(
        RectangleF buttonBounds,
        SizeF captionSize,
        SizeF menuSize,
        float gap,
        float edgePadding)
    {
        var placement = ResolveHoverCaptionPlacement(buttonBounds, menuSize);
        var buttonCenterX = buttonBounds.Left + (buttonBounds.Width / 2f);
        var buttonCenterY = buttonBounds.Top + (buttonBounds.Height / 2f);

        var x = placement switch
        {
            RadialCommandMenuCaptionPlacement.Left => buttonBounds.Left - captionSize.Width - gap,
            RadialCommandMenuCaptionPlacement.Right => buttonBounds.Right + gap,
            _ => buttonCenterX - (captionSize.Width / 2f)
        };
        var y = placement switch
        {
            RadialCommandMenuCaptionPlacement.Above => buttonBounds.Top - captionSize.Height - gap,
            RadialCommandMenuCaptionPlacement.Below => buttonBounds.Bottom + gap,
            _ => buttonCenterY - (captionSize.Height / 2f)
        };

        x = Clamp(x, edgePadding, menuSize.Width - captionSize.Width - edgePadding);
        y = Clamp(y, edgePadding, menuSize.Height - captionSize.Height - edgePadding);
        return new RectangleF(x, y, captionSize.Width, captionSize.Height);
    }

    private static string ResolveActionCaption(ShortcutAction? action)
    {
        var caption = action?.Name?.Trim();
        return string.IsNullOrWhiteSpace(caption) ? "Command" : caption;
    }

    private static float ResolveProgress(TimeSpan elapsed, TimeSpan duration)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return 0f;
        }

        if (elapsed >= duration)
        {
            return 1f;
        }

        return (float)(elapsed.TotalMilliseconds / duration.TotalMilliseconds);
    }

    private static PointF Lerp(PointF from, PointF to, float progress)
    {
        return new PointF(
            Lerp(from.X, to.X, progress),
            Lerp(from.Y, to.Y, progress));
    }

    private static float Lerp(float from, float to, float progress)
    {
        return from + ((to - from) * Clamp(progress, 0f, 1f));
    }

    private static float EaseIn(float progress)
    {
        var t = Clamp(progress, 0f, 1f);
        return t * t * t;
    }

    private static float EaseInOut(float progress)
    {
        var t = Clamp(progress, 0f, 1f);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - (float)Math.Pow(-2f * t + 2f, 3d) / 2f;
    }

    private static byte ToAlpha(float progress)
    {
        return (byte)Math.Round(255f * Clamp(progress, 0f, 1f));
    }

    private static float Clamp(float value, float min, float max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Clamp(value, min, max);
    }
}
