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

    private static float Clamp(float value, float min, float max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Clamp(value, min, max);
    }
}
