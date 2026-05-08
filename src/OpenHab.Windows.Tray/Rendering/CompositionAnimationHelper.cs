using Microsoft.UI.Composition;
using System.Numerics;

namespace OpenHab.Windows.Tray.Rendering;

internal static class CompositionAnimationHelper
{
    /// <summary>
    /// Creates a CubicBezier easing that approximates WPF's CubicEase EaseOut.
    /// Control points: (0, 0) → (0.215, 0.61) → (0.355, 1) → (1, 1)
    /// </summary>
    public static CubicBezierEasingFunction CreateEaseOut(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.215f, 0.61f),
            new Vector2(0.355f, 1.0f));
    }

    /// <summary>
    /// Creates a CubicBezier easing that approximates WPF's CubicEase EaseIn.
    /// Control points: (0, 0) → (0.645, 0) → (0.785, 0.39) → (1, 1)
    /// </summary>
    public static CubicBezierEasingFunction CreateEaseIn(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.645f, 0.0f),
            new Vector2(0.785f, 0.39f));
    }

    /// <summary>
    /// Builds a scalar keyframe animation with easing at the final keyframe.
    /// </summary>
    public static ScalarKeyFrameAnimation BuildScalarEntrance(
        Compositor compositor,
        float from,
        float to,
        TimeSpan duration)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseOut(compositor));
        return animation;
    }

    /// <summary>
    /// Builds a scalar keyframe animation with EaseIn for exit transitions.
    /// </summary>
    public static ScalarKeyFrameAnimation BuildScalarExit(
        Compositor compositor,
        float from,
        float to,
        TimeSpan duration)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseIn(compositor));
        return animation;
    }

    /// <summary>
    /// Builds a Vector3 keyframe animation for offset transitions with EaseOut.
    /// </summary>
    public static Vector3KeyFrameAnimation BuildOffsetEntrance(
        Compositor compositor,
        Vector3 from,
        Vector3 to,
        TimeSpan duration)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseOut(compositor));
        return animation;
    }

    /// <summary>
    /// Builds a Vector3 keyframe animation for offset transitions with EaseIn.
    /// </summary>
    public static Vector3KeyFrameAnimation BuildOffsetExit(
        Compositor compositor,
        Vector3 from,
        Vector3 to,
        TimeSpan duration)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseIn(compositor));
        return animation;
    }

    /// <summary>
    /// Returns a zero duration when animation is disabled, or the configured duration.
    /// </summary>
    public static TimeSpan ResolveDuration(int configuredMs) =>
        configuredMs <= 0 ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(configuredMs);
}
