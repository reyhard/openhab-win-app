using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace OpenHab.Windows.Tray.Rendering;

internal static class SitemapPageTransitionAnimator
{
    public static int ResolveDurationMs(int configuredFlyoutMs)
    {
        var duration = CompositionAnimationHelper.ResolvePageTransitionDuration(configuredFlyoutMs);
        return duration <= TimeSpan.Zero ? 0 : (int)duration.TotalMilliseconds;
    }

    public static async Task AnimateOverlapAsync(
        FrameworkElement contentRoot,
        Grid activeSlot,
        Grid inactiveSlot,
        NavigationDirection direction,
        int durationMs)
    {
        if (durationMs <= 0)
        {
            return;
        }

        contentRoot.UpdateLayout();
        activeSlot.UpdateLayout();
        inactiveSlot.UpdateLayout();

        float slotWidth = (float)Math.Max(activeSlot.ActualWidth, inactiveSlot.ActualWidth);
        if (slotWidth <= 0) slotWidth = (float)contentRoot.ActualWidth;
        if (slotWidth <= 0) slotWidth = 360f;

        // Forward: current slides left, new page enters from right.
        // Back: current slides right, new page enters from left.
        var activeEndX = direction == NavigationDirection.Forward ? -slotWidth : slotWidth;
        var inactiveStartX = -activeEndX;

        var duration = TimeSpan.FromMilliseconds(durationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var activeTransform = EnsureTranslateTransform(activeSlot);
        var inactiveTransform = EnsureTranslateTransform(inactiveSlot);

        try
        {
            Canvas.SetZIndex(inactiveSlot, 1);
            Canvas.SetZIndex(activeSlot, 0);

            activeSlot.Opacity = 1d;
            inactiveSlot.Opacity = 1d;
            activeTransform.X = 0d;
            inactiveTransform.X = inactiveStartX;

            var activeAnim = new DoubleAnimation
            {
                To = activeEndX,
                Duration = duration,
                EasingFunction = easing
            };
            var inactiveAnim = new DoubleAnimation
            {
                To = 0d,
                Duration = duration,
                EasingFunction = easing
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(activeAnim, activeTransform);
            Storyboard.SetTargetProperty(activeAnim, "X");
            Storyboard.SetTarget(inactiveAnim, inactiveTransform);
            Storyboard.SetTargetProperty(inactiveAnim, "X");
            storyboard.Children.Add(activeAnim);
            storyboard.Children.Add(inactiveAnim);

            var tcs = new TaskCompletionSource<bool>();
            void OnCompleted(object? _, object __)
            {
                storyboard.Completed -= OnCompleted;
                tcs.TrySetResult(true);
            }

            storyboard.Completed += OnCompleted;
            storyboard.Begin();
            await tcs.Task;
        }
        finally
        {
            activeTransform.X = 0d;
            inactiveTransform.X = 0d;
            activeSlot.Opacity = 1d;
            inactiveSlot.Opacity = 1d;
            Canvas.SetZIndex(activeSlot, 0);
            Canvas.SetZIndex(inactiveSlot, 0);
        }
    }

    private static TranslateTransform EnsureTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform translate)
        {
            return translate;
        }

        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }
}
