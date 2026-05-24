using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using OpenHab.App.Localization;
using OpenHab.Core;
using OpenHab.Core.Diagnostics;
using OpenHab.Core.Profiles;
using OpenHab.Rendering;
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Icons;
using OpenHab.Rendering.SitemapSurface;
using OpenHab.Sitemaps.Models;
using Windows.Storage.Streams;

namespace OpenHab.Windows.Tray.Rendering;

public static partial class SitemapControlFactory
{
    public static ITextLocalizer TextLocalizer { get; set; } = DefaultEnglishTextLocalizer.Instance;

    private const double ValueLaneWidth = 96;
    private const double ControlLaneWidth = 56;
    private const double NavigateChevronLaneWidth = 20;
    private const double OpenHabServerIconSize = 26;
    private const double IconColumnWidth = 32;
    private const int SliderMoveDebounceMs = 200;
    private const int ColorPickerMoveDebounceMs = 200;
    private const double WidgetVisibilityAnimationDurationMs = 320d;
    private const string MissingIconStateText = "(none)";
    private const string UnknownDiagnosticText = "unknown";
    private static readonly string[] IconFormatsByPreference = ["svg", "png"];
    private static readonly HttpClient IconHttpClient = new();
    private static readonly Regex FirstNumberRegex = FirstNumberRegexFactory();
    private static readonly System.Threading.Lock IconProbeSyncRoot = new();
    private static readonly HashSet<string> ProbedIconEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private sealed record IconImageTag(Uri BaseUri, string IconName, string? IconState, string? IconColor, IconAuthContext? AuthContext);

    internal static void ClearSitemapMediaCaches()
    {
        OpenHabIconImageSourceLoader.ClearPayloadCache();
        lock (IconProbeSyncRoot)
        {
            ProbedIconEndpoints.Clear();
        }
    }

    internal static string? ResolveGlyphForIcon(string? iconName)
    {
        return SitemapUiLogic.ResolveWin11Glyph(iconName);
    }

    private static FontIcon? ResolveWin11Icon(string? iconName)
    {
        var glyph = ResolveGlyphForIcon(iconName);
        if (glyph is null)
            return null;

        return new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            Opacity = 0.8,
            FontFamily = new FontFamily("Segoe MDL2 Assets")
        };
    }

    private readonly record struct RowLayout(Grid Grid, int LabelColumn, int ValueColumn, int ControlColumn);
    private readonly record struct WidgetVisibilityAnimationProfile(
        double DurationMs,
        double SlideDistanceHeightRatio,
        double FadeStartProgress,
        double FadeCompleteProgress,
        double FadeControlPoint1X,
        double FadeControlPoint1Y,
        double FadeControlPoint2X,
        double FadeControlPoint2Y);

    public readonly record struct IconAuthContext(
        string? ApiToken,
        string? BasicUserName,
        string? BasicPassword,
        TransportKind? TransportKind = null);

    private sealed class SliderCommandState
    {
        public bool SuppressValueChanged { get; set; }
        public bool IsDragging { get; set; }
        public CancellationTokenSource? DebounceCts { get; set; }
        public string? LastSentCommand { get; set; }
    }

    private sealed class ColorCommandState
    {
        public CancellationTokenSource? DebounceCts { get; set; }
        public string? LastSentCommand { get; set; }
    }

    [GeneratedRegex(@"[-+]?\d+([.,]\d+)?", RegexOptions.Compiled)]
    private static partial Regex FirstNumberRegexFactory();

    /// <summary>
    /// Collapses separators and digits so common openHAB icon-name variants
    /// (e.g. "roller_shutter", "ground-floor", "chart-1") still resolve.
    /// </summary>
    internal static string NormalizeIconName(string? iconName)
    {
        return SitemapUiLogic.NormalizeIconName(iconName);
    }

    /// <summary>Pure-logic query: does the normalized icon name resolve
    /// to a known Win11 glyph?  Safe to call in tests without WinUI runtime.</summary>
    internal static bool CanResolveNormalizedIcon(string? iconName)
    {
        return SitemapUiLogic.CanResolveWin11Glyph(iconName);
    }

    internal static double ResolveWebviewHeight(SitemapRowDescriptor row)
    {
        return SitemapRowVisualPolicy.ResolveWebviewHeight(row);
    }

    internal static string BuildRowIdentityKey(SitemapRowDescriptor row)
    {
        return SitemapRowVisualPolicy.BuildRowIdentityKey(row);
    }

    internal static string BuildRowVisualStateKey(SitemapRowDescriptor row, int rowIndex)
    {
        return SitemapRowVisualPolicy.BuildRowVisualStateKey(row, rowIndex);
    }

    public static FrameworkElement Create(
        SitemapRowDescriptor row,
        Func<Task>? activateRow,
        Func<string, Task>? sendCommand = null,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null,
        int chartDpi = 192,
        Func<SitemapMapOption, bool, Task>? sendButtonGridCommand = null)
    {
        using var scope = OpenHabProfiling.StartScope("SitemapControlFactory.Create");
        ArgumentNullException.ThrowIfNull(row);
        scope?.SetTag("row.control", row.Control.ToString());
        scope?.SetTag("row.action", row.Action.ToString());
        scope?.SetTag("row.is_visible", row.IsVisible);

        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row, activateRow, baseUri, useWindowsIcons, iconAuth),
            RenderControlKind.Slider => CreateSlider(row, sendCommand, baseUri, useWindowsIcons, iconAuth),
            RenderControlKind.Selection => CreateSelection(row, sendCommand, baseUri, useWindowsIcons, iconAuth),
            RenderControlKind.Input => CreateInput(row, sendCommand, baseUri, useWindowsIcons, iconAuth),
            RenderControlKind.Button => CreateButton(row, sendCommand, baseUri, useWindowsIcons, iconAuth),
            RenderControlKind.ButtonGrid => CreateButtonGrid(row, sendCommand, sendButtonGridCommand, baseUri, useWindowsIcons, iconAuth),
            RenderControlKind.Image => CreateImage(row, baseUri, useWindowsIcons, iconAuth),
            RenderControlKind.Webview => CreateWebview(row, baseUri),
            RenderControlKind.Mapview => CreateMapview(row, baseUri),
            RenderControlKind.Video => CreateVideo(row, baseUri),
            RenderControlKind.Chart => CreateChart(row, baseUri, chartDpi, iconAuth),
            RenderControlKind.Fallback => CreateFallback(row),
            _ => CreateText(row, activateRow, baseUri, useWindowsIcons, iconAuth)
        };
    }

    public static void UpdateState(FrameworkElement control, SitemapRowDescriptor updated)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(updated);

        var inner = control;
        if (control is Border border && border.Child is FrameworkElement child)
        {
            inner = child;
        }

        // Update visibility first (with animated transitions for smoother unhide/hide).
        ApplyAnimatedVisibility(control, updated.IsVisible);

        var rawState = updated.RawState ?? updated.State;

        switch (updated.Control)
        {
            case RenderControlKind.Toggle:
                var toggle = FindVisualChild<ToggleSwitch>(inner);
                if (toggle is not null)
                {
                    var isOn = string.Equals(rawState, "ON", StringComparison.OrdinalIgnoreCase);
                    if (toggle.IsOn != isOn)
                    {
                        // Suppress Toggled event to prevent feedback loop.
                        toggle.Tag = "suppress";
                        toggle.IsOn = isOn;
                        toggle.Tag = null;
                    }
                }
                // Also update the state text next to the toggle
                UpdateStateTextBlock(inner, string.Equals(rawState, "ON", StringComparison.OrdinalIgnoreCase) ? "ON" : "OFF");
                break;

            case RenderControlKind.Slider:
                var slider = FindVisualChild<Slider>(inner);
                if (slider is not null && double.TryParse(rawState, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    var sliderState = slider.Tag as SliderCommandState;
                    sliderState?.SuppressValueChanged = true;

                    slider.Value = val;

                    sliderState?.SuppressValueChanged = false;

                    if (updated.InputHint == SitemapInputHint.ColorTemperature)
                    {
                        UpdateColorPreview(inner, ResolveColorTemperaturePreview(val, slider.Minimum, slider.Maximum));
                    }
                    else
                    {
                        var currentStateText = FindStateTextBlockText(inner);
                        UpdateStateTextBlock(inner, FormatSliderStateText(currentStateText, val));
                    }
                }
                else
                {
                    UpdateStateTextBlock(inner, updated.State ?? rawState ?? string.Empty);
                }
                break;

            case RenderControlKind.Selection:
            case RenderControlKind.Input:
            case RenderControlKind.Text:
            case RenderControlKind.Webview:
            case RenderControlKind.Mapview:
            case RenderControlKind.Video:
            case RenderControlKind.Chart:
            case RenderControlKind.Fallback:
                UpdateStateTextBlock(inner, updated.State ?? string.Empty);
                if (updated.Control == RenderControlKind.Input
                    && updated.InputHint == SitemapInputHint.Color
                    && TryResolveOpenHabColor(updated.RawItemState ?? updated.RawState ?? updated.State, out var inputColor))
                {
                    UpdateColorPreview(inner, inputColor);
                }
                break;
        }

        ApplyRowColors(inner, updated);
        UpdateIconImage(inner, updated);
    }

    public static void SetVisibility(FrameworkElement control, bool visible)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        control.Opacity = 1d;
        control.Height = double.NaN;
        if (control.RenderTransform is TranslateTransform transform)
        {
            transform.Y = 0d;
        }
    }

    public static void CollapseAndRemove(Panel parent, FrameworkElement control)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(control);

        if (!parent.Children.Contains(control))
        {
            return;
        }

        if (control.Visibility == Visibility.Collapsed)
        {
            parent.Children.Remove(control);
            return;
        }

        ApplyAnimatedVisibility(control, visible: false, onCompleted: () =>
        {
            if (parent.Children.Contains(control))
            {
                parent.Children.Remove(control);
            }
        });
    }

    private static void ApplyAnimatedVisibility(FrameworkElement control, bool visible, Action? onCompleted = null)
    {
        var profile = ResolveWidgetVisibilityAnimationProfile();
        var collapseEase = new CubicEase { EasingMode = EasingMode.EaseIn };

        if (visible)
        {
            if (control.Visibility != Visibility.Visible)
            {
                EnsureSlideTransform(control);
                var showOriginalMinHeight = control.MinHeight;
                control.MinHeight = 0d;
                var restoreShowClip = BeginHeightAnimationClip(control);
                control.Opacity = 0d;
                control.Visibility = Visibility.Visible;
                control.Height = double.NaN;
                control.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                var targetHeight = Math.Max(1d, control.DesiredSize.Height);
                control.Height = 0d;

                // Match Windows 11's feel: height, offset, and opacity move together.
                var heightIn = new DoubleAnimation
                {
                    From = 0d,
                    To = targetHeight,
                    Duration = new Duration(TimeSpan.FromMilliseconds(profile.DurationMs)),
                    EnableDependentAnimation = true
                };

                var slideIn = new DoubleAnimation
                {
                    From = -targetHeight * profile.SlideDistanceHeightRatio,
                    To = 0d,
                    Duration = new Duration(TimeSpan.FromMilliseconds(profile.DurationMs)),
                    EnableDependentAnimation = true
                };

                var fadeIn = new DoubleAnimationUsingKeyFrames
                {
                    Duration = new Duration(TimeSpan.FromMilliseconds(profile.DurationMs)),
                    EnableDependentAnimation = true
                };
                fadeIn.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0d });
                fadeIn.KeyFrames.Add(new LinearDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(profile.DurationMs * profile.FadeStartProgress)),
                    Value = 0d
                });
                fadeIn.KeyFrames.Add(new SplineDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(profile.DurationMs * profile.FadeCompleteProgress)),
                    Value = 1d,
                    KeySpline = new KeySpline
                    {
                        ControlPoint1 = new global::Windows.Foundation.Point(profile.FadeControlPoint1X, profile.FadeControlPoint1Y),
                        ControlPoint2 = new global::Windows.Foundation.Point(profile.FadeControlPoint2X, profile.FadeControlPoint2Y)
                    }
                });

                var sb = new Storyboard();
                Storyboard.SetTarget(heightIn, control);
                Storyboard.SetTargetProperty(heightIn, nameof(FrameworkElement.Height));
                Storyboard.SetTarget(slideIn, control);
                Storyboard.SetTargetProperty(slideIn, "(UIElement.RenderTransform).(TranslateTransform.Y)");
                Storyboard.SetTarget(fadeIn, control);
                Storyboard.SetTargetProperty(fadeIn, nameof(UIElement.Opacity));
                sb.Children.Add(heightIn);
                sb.Children.Add(slideIn);
                sb.Children.Add(fadeIn);
                sb.Completed += (_, _) =>
                {
                    control.Height = double.NaN;
                    control.MinHeight = showOriginalMinHeight;
                    restoreShowClip();
                    control.Opacity = 1d;
                    if (control.RenderTransform is TranslateTransform transform)
                    {
                        transform.Y = 0d;
                    }

                    onCompleted?.Invoke();
                };
                sb.Begin();
            }
            else
            {
                control.Opacity = 1d;
                onCompleted?.Invoke();
            }

            return;
        }

        if (control.Visibility == Visibility.Collapsed)
        {
            control.Opacity = 1d;
            onCompleted?.Invoke();
            return;
        }

        EnsureSlideTransform(control);
        var measuredHeight = control.ActualHeight;
        if (measuredHeight <= 0d)
        {
            control.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            measuredHeight = control.DesiredSize.Height;
        }

        var currentHeight = Math.Max(1d, measuredHeight);
        var hideOriginalMinHeight = control.MinHeight;
        control.MinHeight = 0d;
        var restoreHideClip = BeginHeightAnimationClip(control);
        control.Height = currentHeight;

        var fadeOut = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(profile.DurationMs)),
            EnableDependentAnimation = true
        };
        fadeOut.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1d });
        fadeOut.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(profile.DurationMs * 0.62d)),
            Value = 0d,
            KeySpline = new KeySpline { ControlPoint1 = new global::Windows.Foundation.Point(0.7d, 0d), ControlPoint2 = new global::Windows.Foundation.Point(0.84d, 0d) }
        });

        var slideOut = new DoubleAnimation
        {
            To = -currentHeight * profile.SlideDistanceHeightRatio,
            Duration = new Duration(TimeSpan.FromMilliseconds(profile.DurationMs)),
            EasingFunction = collapseEase,
            EnableDependentAnimation = true
        };

        var heightOut = new DoubleAnimation
        {
            From = currentHeight,
            To = 0d,
            Duration = new Duration(TimeSpan.FromMilliseconds(profile.DurationMs)),
            EasingFunction = collapseEase,
            EnableDependentAnimation = true
        };

        var sbHide = new Storyboard();
        Storyboard.SetTarget(heightOut, control);
        Storyboard.SetTargetProperty(heightOut, nameof(FrameworkElement.Height));
        Storyboard.SetTarget(slideOut, control);
        Storyboard.SetTargetProperty(slideOut, "(UIElement.RenderTransform).(TranslateTransform.Y)");
        Storyboard.SetTarget(fadeOut, control);
        Storyboard.SetTargetProperty(fadeOut, nameof(UIElement.Opacity));
        sbHide.Children.Add(heightOut);
        sbHide.Children.Add(slideOut);
        sbHide.Children.Add(fadeOut);
        sbHide.Completed += (_, _) =>
        {
            control.Visibility = Visibility.Collapsed;
            control.Opacity = 1d;
            control.Height = double.NaN;
            control.MinHeight = hideOriginalMinHeight;
            restoreHideClip();
            if (control.RenderTransform is TranslateTransform transform)
            {
                transform.Y = 0d;
            }

            onCompleted?.Invoke();
        };
        sbHide.Begin();
    }

    private static Action BeginHeightAnimationClip(FrameworkElement control)
    {
        var originalClip = control.Clip;

        void UpdateClip()
        {
            var width = Math.Max(control.ActualWidth, control.DesiredSize.Width);
            var height = double.IsNaN(control.Height) ? control.ActualHeight : Math.Max(0d, control.Height);
            control.Clip = new RectangleGeometry
            {
                Rect = new global::Windows.Foundation.Rect(0d, 0d, width, height)
            };
        }

        SizeChangedEventHandler handler = (_, _) => UpdateClip();
        control.SizeChanged += handler;
        UpdateClip();

        return () =>
        {
            control.SizeChanged -= handler;
            control.Clip = originalClip;
        };
    }

    private static WidgetVisibilityAnimationProfile ResolveWidgetVisibilityAnimationProfile()
    {
        return new WidgetVisibilityAnimationProfile(
            DurationMs: WidgetVisibilityAnimationDurationMs,
            SlideDistanceHeightRatio: 1d,
            FadeStartProgress: 0.5d,
            FadeCompleteProgress: 1d,
            FadeControlPoint1X: 0.32d,
            FadeControlPoint1Y: 0.28d,
            FadeControlPoint2X: 0.68d,
            FadeControlPoint2Y: 0.72d);
    }

    private static void EnsureSlideTransform(FrameworkElement control)
    {
        if (control.RenderTransform is not TranslateTransform)
        {
            control.RenderTransform = new TranslateTransform();
        }
    }

    private static void UpdateStateTextBlock(DependencyObject parent, string newState)
    {
        _ = TryUpdateStateTextBlock(parent, newState);
    }

    private static string? FindStateTextBlockText(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb && tb.FontSize <= 14 && IsStateTextBlock(tb))
            {
                return tb.Text;
            }

            var nested = FindStateTextBlockText(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool TryUpdateStateTextBlock(DependencyObject parent, string newState)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb && tb.FontSize <= 14 && !string.IsNullOrEmpty(tb.Text) && IsStateTextBlock(tb))
            {
                tb.Text = newState;
                return true;
            }

            if (TryUpdateStateTextBlock(child, newState))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStateTextBlock(TextBlock textBlock)
    {
        return textBlock.HorizontalAlignment == HorizontalAlignment.Right ||
               textBlock.TextAlignment == TextAlignment.Right;
    }

    private static void ApplyRowColors(FrameworkElement root, SitemapRowDescriptor row)
    {
        if (FindTaggedElement<TextBlock>(root, "sitemap-label") is { } labelBlock)
        {
            ApplyBrush(labelBlock, row.LabelColor);
        }

        if (FindTaggedElement<TextBlock>(root, "sitemap-value") is { } valueBlock)
        {
            ApplyBrush(valueBlock, row.ValueColor);
        }

        if (FindTaggedElement<FontIcon>(root, "sitemap-icon") is { } icon)
        {
            ApplyBrush(icon, row.IconColor);
        }
    }

    private static void UpdateColorPreview(FrameworkElement root, global::Windows.UI.Color color)
    {
        if (FindTaggedElement<Border>(root, "sitemap-color-preview") is { } preview)
        {
            preview.Background = new SolidColorBrush(color);
        }
    }

    private static void UpdateIconImage(FrameworkElement root, SitemapRowDescriptor row)
    {
        if (FindIconImage(root) is not { } image || image.Tag is not IconImageTag tag)
        {
            return;
        }

        var iconName = row.IconName;
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return;
        }

        var iconState = OpenHabIconUriBuilder.NormalizeStateForRequest(row.RawState ?? row.State);
        if (string.Equals(tag.IconName, iconName, StringComparison.Ordinal) &&
            string.Equals(tag.IconState, iconState, StringComparison.Ordinal) &&
            string.Equals(tag.IconColor, row.IconColor, StringComparison.Ordinal))
        {
            return;
        }

        var updated = tag with
        {
            IconName = iconName,
            IconState = iconState,
            IconColor = row.IconColor
        };
        image.Tag = updated;
        _ = LoadIconAsync(image, updated.BaseUri, updated.IconName, updated.IconState, updated.IconColor, updated.AuthContext);
    }

    private static Image? FindIconImage(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Image { Tag: IconImageTag } image)
            {
                return image;
            }

            var found = FindIconImage(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindTaggedElement<T>(DependencyObject parent, string tag) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && string.Equals(typed.Tag as string, tag, StringComparison.Ordinal))
            {
                return typed;
            }

            var found = FindTaggedElement<T>(child, tag);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void ApplyBrush(IconElement icon, string? color)
    {
        if (TryCreateBrush(color, out var brush))
        {
            icon.Foreground = brush;
        }
        else
        {
            icon.ClearValue(IconElement.ForegroundProperty);
        }
    }

    private static void ApplyBrush(TextBlock textBlock, string? color)
    {
        if (TryCreateBrush(color, out var brush))
        {
            textBlock.Foreground = brush;
        }
        else
        {
            textBlock.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    private static bool TryCreateBrush(string? color, out SolidColorBrush brush)
    {
        brush = default!;
        if (!TryParseColor(color, out var parsedColor))
        {
            return false;
        }

        brush = new SolidColorBrush(parsedColor);
        return true;
    }

    private static bool TryParseColor(string? color, out global::Windows.UI.Color parsed)
    {
        if (SitemapUiLogic.TryResolveOpenHabColor(color, out var sitemapColor))
        {
            parsed = ToWindowsColor(sitemapColor);
            return true;
        }

        parsed = default;
        return false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                return typed;
            }

            var found = FindVisualChild<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void TryAddIcon(
        Grid grid,
        int column,
        string? iconName,
        string? iconState,
        string? iconColor,
        Uri? baseUri,
        bool useWindowsIcons,
        IconAuthContext? iconAuth)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return;
        }

        var requestIconState = OpenHabIconUriBuilder.NormalizeStateForRequest(iconState);

        if (useWindowsIcons)
        {
            var winIcon = ResolveWin11Icon(iconName);
            if (winIcon is not null)
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                {
                    DiagnosticLogger.Info($"Icon render via Win11 glyph: icon='{iconName}', normalized='{NormalizeIconName(iconName)}'");
                }

                winIcon.VerticalAlignment = VerticalAlignment.Center;
                winIcon.Tag = "sitemap-icon";
                if (TryCreateBrush(iconColor, out var iconBrush))
                {
                    winIcon.Foreground = iconBrush;
                }
                Grid.SetColumn(winIcon, column);
                grid.Children.Add(winIcon);
                return;
            }

            if (!DiagnosticLogger.SuppressIconLogging)
            {
                DiagnosticLogger.Warn($"Win11 glyph mapping missing: icon='{iconName}', normalized='{NormalizeIconName(iconName)}'; falling back to server icon endpoint");
            }
        }

        if (baseUri is not null)
        {
            var image = new Image
            {
                Width = OpenHabServerIconSize,
                Height = OpenHabServerIconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = new IconImageTag(baseUri, iconName, requestIconState, iconColor, iconAuth)
            };
            Grid.SetColumn(image, column);
            grid.Children.Add(image);

            if (iconAuth is { } authContext)
            {
                StartIconProbeIfNeeded(baseUri, authContext);
                _ = LoadIconAsync(image, baseUri, iconName, requestIconState, iconColor, authContext);
                return;
            }

            _ = LoadIconAsync(image, baseUri, iconName, requestIconState, iconColor, null);
            return;
        }

        if (!DiagnosticLogger.SuppressIconLogging)
        {
            DiagnosticLogger.Warn($"Icon skipped: icon='{iconName}', state='{requestIconState ?? MissingIconStateText}', reason='no glyph mapping and no base URI'");
        }
    }

    private static void StartIconProbeIfNeeded(Uri baseUri, IconAuthContext authContext)
    {
        var probeKey = $"{baseUri.Scheme}://{baseUri.Authority}|{IconAuthHeaderHelper.GetAuthMode(authContext)}|{authContext.TransportKind?.ToString() ?? UnknownDiagnosticText}";
        lock (IconProbeSyncRoot)
        {
            if (!ProbedIconEndpoints.Add(probeKey))
            {
                return;
            }
        }

        _ = ProbeIconEndpointAsync(baseUri, authContext);
    }

    private static async Task ProbeIconEndpointAsync(Uri baseUri, IconAuthContext authContext)
    {
        var probeUri = BuildOpenHabIconUri(baseUri, "switch", "ON");

        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, probeUri);
            IconAuthHeaderHelper.ApplyAuthHeaders(headRequest, authContext);
            using var headResponse = await IconHttpClient.SendAsync(headRequest);

            if (headResponse.IsSuccessStatusCode)
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                {
                    DiagnosticLogger.Info($"Icon probe OK (HEAD): endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? UnknownDiagnosticText}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}', status={(int)headResponse.StatusCode}");
                }

                return;
            }

            if (!DiagnosticLogger.SuppressIconLogging)
            {
                DiagnosticLogger.Warn($"Icon probe HEAD non-success: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? UnknownDiagnosticText}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}', status={(int)headResponse.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            if (!DiagnosticLogger.SuppressIconLogging)
            {
                DiagnosticLogger.Warn($"Icon probe HEAD failed: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? UnknownDiagnosticText}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}', error='{ex.GetType().Name}: {ex.Message}'");
            }
        }

        try
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, probeUri);
            IconAuthHeaderHelper.ApplyAuthHeaders(getRequest, authContext);
            using var getResponse = await IconHttpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);

            if (getResponse.IsSuccessStatusCode)
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                {
                    DiagnosticLogger.Info($"Icon probe OK (GET): endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? UnknownDiagnosticText}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}', status={(int)getResponse.StatusCode}");
                }
            }
            else
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                {
                    DiagnosticLogger.Warn($"Icon probe GET non-success: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? UnknownDiagnosticText}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}', status={(int)getResponse.StatusCode}");
                }
            }
        }
        catch (Exception ex)
        {
            if (!DiagnosticLogger.SuppressIconLogging)
            {
                DiagnosticLogger.Warn($"Icon probe GET failed: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? UnknownDiagnosticText}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}', error='{ex.GetType().Name}: {ex.Message}'");
            }
        }
    }

    private static async Task LoadIconAsync(
        Image image,
        Uri baseUri,
        string iconName,
        string? iconState,
        string? iconColor,
        IconAuthContext? authContext)
    {
        iconState = OpenHabIconUriBuilder.NormalizeStateForRequest(iconState);
        var attempts = new List<string>(IconFormatsByPreference.Length);

        foreach (var format in IconFormatsByPreference)
        {
            var iconUri = BuildOpenHabIconUri(baseUri, iconName, iconState, format);
            if (!DiagnosticLogger.SuppressIconLogging)
            {
                DiagnosticLogger.Info($"Icon request: icon='{iconName}', state='{iconState ?? MissingIconStateText}', format='{format}', url='{iconUri.PathAndQuery}'");
            }

            var attemptResult = await TryLoadIconForFormatAsync(image, iconUri, iconName, iconState, iconColor, format, authContext);
            if (attemptResult is null)
            {
                return;
            }

            attempts.Add(attemptResult);
        }

        if (!DiagnosticLogger.SuppressIconLogging)
        {
            DiagnosticLogger.Warn($"Icon failed: icon='{iconName}', state='{iconState ?? MissingIconStateText}', formats='{string.Join(",", IconFormatsByPreference)}', attempts='{string.Join("; ", attempts)}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}'");
        }
    }

    private static async Task<string?> TryLoadIconForFormatAsync(
        Image image,
        Uri iconUri,
        string iconName,
        string? iconState,
        string? iconColor,
        string format,
        IconAuthContext? authContext)
    {
        try
        {
            var result = await OpenHabIconImageSourceLoader.TryLoadAsync(image, iconUri, iconColor, authContext);
            if (!result.Success)
            {
                if (!DiagnosticLogger.SuppressIconLogging && result.Error?.StartsWith("status=", StringComparison.Ordinal) == true)
                {
                    DiagnosticLogger.Warn($"Icon request failed: icon='{iconName}', state='{iconState ?? MissingIconStateText}', url='{iconUri.PathAndQuery}', requestedFormat='{format}', {result.Error}, media='{result.MediaType ?? UnknownDiagnosticText}', auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}'");
                }
                return $"format={format}:{result.Error}";
            }

            if (!DiagnosticLogger.SuppressIconLogging)
            {
                var action = result.FromCache ? "Icon cache hit" : "Icon loaded";
                var media = result.FromCache ? "cache" : result.MediaType ?? UnknownDiagnosticText;
                var bytes = result.FromCache ? string.Empty : $", bytes={result.BytesLength}";
                DiagnosticLogger.Info($"{action}: icon='{iconName}', state='{iconState ?? MissingIconStateText}', url='{iconUri.PathAndQuery}', requestedFormat='{format}', decodedAs='{result.DecodedAs}', media='{media}'{bytes}, auth='{IconAuthHeaderHelper.GetAuthMode(authContext)}'");
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"format={format}:error={ex.GetType().Name}";
        }
    }

    internal static Uri BuildOpenHabIconUri(Uri baseUri, string iconName, string? iconState, string format = "png")
    {
        return OpenHabIconUriBuilder.Build(baseUri, iconName, iconState, format);
    }

    private static bool CanDisplayIcon(string? iconName, Uri? baseUri, bool useWindowsIcons)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return false;
        return baseUri is not null || (useWindowsIcons && ResolveGlyphForIcon(iconName) is not null);
    }

    private static RowLayout CreateRowLayout(
        string label,
        Uri? baseUri,
        string? iconName,
        string? iconState,
        string? labelColor,
        string? iconColor,
        bool useWindowsIcons,
        IconAuthContext? iconAuth)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        var hasIcon = CanDisplayIcon(iconName, baseUri, useWindowsIcons);

        if (hasIcon)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconColumnWidth) });
            TryAddIcon(grid, 0, iconName, iconState, iconColor, baseUri, useWindowsIcons, iconAuth);
        }

        var labelColumn = hasIcon ? 1 : 0;
        var valueColumn = hasIcon ? 2 : 1;
        var controlColumn = hasIcon ? 3 : 2;

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ValueLaneWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ControlLaneWidth) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Tag = "sitemap-label",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        };
        if (TryCreateBrush(labelColor, out var labelBrush))
        {
            labelBlock.Foreground = labelBrush;
        }
        Grid.SetColumn(labelBlock, labelColumn);
        grid.Children.Add(labelBlock);

        return new RowLayout(grid, labelColumn, valueColumn, controlColumn);
    }

    private static TextBlock CreateStateTextBlock(string state, string? valueColor)
    {
        var textBlock = new TextBlock
        {
            Text = state,
            Tag = "sitemap-value",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        if (TryCreateBrush(valueColor, out var valueBrush))
        {
            textBlock.Foreground = valueBrush;
        }

        return textBlock;
    }

    private static Border CreateStateColorPreview(global::Windows.UI.Color color)
    {
        return new Border
        {
            Tag = "sitemap-color-preview",
            Width = 30,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(3),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(color)
        };
    }

    private static FrameworkElement CreateText(
        SitemapRowDescriptor row,
        Func<Task>? activateRow = null,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        if (IsSectionHeader(row))
        {
            return CreateSectionHeader(row.Label);
        }

        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        var grid = layout.Grid;
        var navigateAction = row.Action == RenderActionKind.Navigate ? activateRow : null;
        var isNavigate = navigateAction is not null;

        var stateText = CreateStateTextBlock(row.State ?? string.Empty, row.ValueColor);
        Grid.SetColumn(stateText, layout.ValueColumn);
        Grid.SetColumnSpan(stateText, isNavigate ? 1 : 2);
        grid.Children.Add(stateText);

        if (navigateAction is not null)
        {
            grid.ColumnDefinitions[layout.ControlColumn].Width = new GridLength(NavigateChevronLaneWidth);
            Func<Task> navigate = navigateAction;
            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6
            };
            Grid.SetColumn(chevron, layout.ControlColumn);
            grid.Children.Add(chevron);

            var button = new Button
            {
                Content = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(0, 4, 0, 4),
                MinHeight = 36,
                BorderThickness = new Thickness(0)
            };
            button.Click += async (_, _) => await navigate();
            return WrapWithBorder(button);
        }

        return WrapWithBorder(grid);
    }

    private static bool IsSectionHeader(SitemapRowDescriptor row)
    {
        if (row.IsSectionHeader)
        {
            return true;
        }

        // Some installations surface section-like rows as plain text/group rows
        // with icon "none". Treat those as headers too, so they don't look like buttons.
        var iconIsAbsent = string.IsNullOrWhiteSpace(row.IconName)
            || string.Equals(row.IconName, "none", StringComparison.OrdinalIgnoreCase);

        return row.Control == RenderControlKind.Text
            && row.Action == RenderActionKind.None
            && string.IsNullOrWhiteSpace(row.State)
            && iconIsAbsent;
    }

    private static TextBlock CreateSectionHeader(string label)
    {
        return new TextBlock
        {
            Text = label,
            Margin = new Thickness(2, 12, 2, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.92
        };
    }

    private static Border CreateToggle(
        SitemapRowDescriptor row,
        Func<Task>? activateRow,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        var grid = layout.Grid;
        var rawState = row.RawState ?? row.State;

        var stateBlock = CreateStateTextBlock(
            string.Equals(rawState, "ON", StringComparison.OrdinalIgnoreCase) ? "ON" : "OFF",
            row.ValueColor);
        stateBlock.Margin = new Thickness(0, 0, 8, 0);
        stateBlock.Opacity = 0.7;
        stateBlock.FontSize = 13;
        stateBlock.MinWidth = 32;
        Grid.SetColumn(stateBlock, layout.ValueColumn);
        grid.Children.Add(stateBlock);

        var toggle = new ToggleSwitch
        {
            IsOn = string.Equals(rawState, "ON", StringComparison.OrdinalIgnoreCase),
            OnContent = string.Empty,
            OffContent = string.Empty,
            Width = 48,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(toggle, layout.ControlColumn);
        grid.Children.Add(toggle);

        if (row.Action == RenderActionKind.SendCommand && activateRow is not null)
        {
            toggle.Toggled += async (s, _) =>
            {
                if (s is ToggleSwitch ts && ts.Tag as string == "suppress") return;
                await activateRow();
            };
        }

        return WrapWithBorder(grid);
    }

    private static Border CreateSlider(
        SitemapRowDescriptor row,
        Func<string, Task>? sendCommand,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        var grid = layout.Grid;
        grid.ColumnDefinitions[layout.ControlColumn].Width = new GridLength(120);

        var min = row.MinValue ?? 0;
        var max = row.MaxValue ?? 100;
        if (max < min)
        {
            (min, max) = (max, min);
        }

        var value = TryParseNumericState(row.RawState ?? row.State, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : min;

        var isColorTemperature = row.InputHint == SitemapInputHint.ColorTemperature;
        TextBlock? stateBlock = null;
        if (isColorTemperature)
        {
            var previewColor = ResolveColorTemperaturePreview(value, min, max);
            var preview = CreateStateColorPreview(previewColor);
            preview.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(preview, layout.ValueColumn);
            grid.Children.Add(preview);
        }
        else
        {
            stateBlock = CreateStateTextBlock(row.State ?? string.Empty, row.ValueColor);
            stateBlock.Margin = new Thickness(0, 0, 8, 0);
            stateBlock.Opacity = 0.85;
            Grid.SetColumn(stateBlock, layout.ValueColumn);
            grid.Children.Add(stateBlock);
        }

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            SmallChange = row.Step ?? 1,
            StepFrequency = row.Step ?? 1,
            MinWidth = 110,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (sendCommand is not null)
        {
            var commandState = new SliderCommandState();
            slider.Tag = commandState;

            async Task SendIfChangedAsync(string value)
            {
                if (!string.Equals(commandState.LastSentCommand, value, StringComparison.Ordinal))
                {
                    await sendCommand(value);
                    commandState.LastSentCommand = value;
                }
            }

            async Task FlushReleaseValueAsync()
            {
                if (row.SliderUpdateOnMove)
                {
                    return;
                }

                var releaseValue = slider.Value.ToString("F0", CultureInfo.InvariantCulture);
                await SendIfChangedAsync(releaseValue);
            }

            slider.ValueChanged += async (_, args) =>
            {
                if (commandState.SuppressValueChanged)
                {
                    return;
                }

                var newValue = args.NewValue.ToString("F0", CultureInfo.InvariantCulture);
                if (isColorTemperature)
                {
                    UpdateColorPreview(grid, ResolveColorTemperaturePreview(args.NewValue, min, max));
                }
                else if (stateBlock is not null)
                {
                    stateBlock.Text = FormatSliderStateText(stateBlock.Text, args.NewValue);
                }

                if (row.SliderUpdateOnMove)
                {
                    commandState.DebounceCts?.Cancel();
                    commandState.DebounceCts?.Dispose();

                    var cts = new CancellationTokenSource();
                    commandState.DebounceCts = cts;
                    try
                    {
                        await Task.Delay(SliderMoveDebounceMs, cts.Token);
                        await sendCommand(newValue);
                        commandState.LastSentCommand = newValue;
                    }
                    catch (OperationCanceledException)
                    {
                        // New move event superseded this one.
                    }

                    return;
                }

                if (!commandState.IsDragging)
                {
                    await SendIfChangedAsync(newValue);
                }
            };

            slider.PointerPressed += (_, _) =>
            {
                commandState.IsDragging = true;
            };

            slider.PointerReleased += async (_, _) =>
            {
                commandState.IsDragging = false;
                await FlushReleaseValueAsync();
            };

            slider.PointerCaptureLost += async (_, _) =>
            {
                commandState.IsDragging = false;
                await FlushReleaseValueAsync();
            };

            slider.KeyUp += async (_, _) =>
            {
                // Keyboard slider interactions don't produce pointer release events.
                commandState.IsDragging = false;
                await FlushReleaseValueAsync();
            };

            slider.LostFocus += async (_, _) =>
            {
                // Ensure final value is sent when interaction ends through focus changes.
                commandState.IsDragging = false;
                await FlushReleaseValueAsync();
            };
        }

        Grid.SetColumn(slider, layout.ControlColumn);
        grid.Children.Add(slider);

        return WrapWithBorder(grid);
    }

    private static bool TryParseNumericState(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        var match = FirstNumberRegex.Match(raw);
        if (!match.Success)
        {
            return false;
        }

        var normalized = match.Value.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static Border CreateSelection(
        SitemapRowDescriptor row,
        Func<string, Task>? sendCommand,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        var grid = layout.Grid;

        var comboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        var selectedIndex = -1;
        foreach (var option in row.SelectionOptions)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Command });
            var commandSource = row.RawItemState ?? row.RawState;
            var matchesCommand = SitemapUiLogic.SelectionValueMatches(option.Command, commandSource);
            var matchesLabel = SitemapUiLogic.SelectionValueMatches(option.Label, row.State);
            if (selectedIndex < 0 && (matchesCommand || matchesLabel))
            {
                selectedIndex = comboBox.Items.Count - 1;
            }
        }
        comboBox.SelectedIndex = selectedIndex;

        // Fallback: always show current state text in collapsed view even if it does not
        // match any mapping label/command exactly.
        if (comboBox.SelectedIndex < 0 && !string.IsNullOrWhiteSpace(row.State))
        {
            var displayItem = new ComboBoxItem
            {
                Content = row.State!.Trim(),
                Tag = row.RawItemState ?? row.RawState ?? row.State
            };
            comboBox.Items.Insert(0, displayItem);
            comboBox.SelectedIndex = 0;
        }

        if (sendCommand is not null)
        {
            comboBox.SelectionChanged += async (_, _) =>
            {
                if (comboBox.SelectedItem is ComboBoxItem { Tag: string cmd })
                    await sendCommand(cmd);
            };
        }

        Grid.SetColumn(comboBox, layout.ValueColumn);
        Grid.SetColumnSpan(comboBox, 2);
        grid.Children.Add(comboBox);

        return WrapWithBorder(grid);
    }

    private static Border CreateInput(
        SitemapRowDescriptor row,
        Func<string, Task>? sendCommand,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        var grid = layout.Grid;
        grid.ColumnDefinitions[layout.ValueColumn].Width = new GridLength(0);
        grid.ColumnDefinitions[layout.ControlColumn].Width = new GridLength(220);

        var inferredHint = ResolveInputHint(row);
        var rawValue = NormalizeInputStateValue(row.RawItemState ?? row.RawState ?? row.State);

        async Task SubmitAsync(string value)
        {
            if (sendCommand is not null && !string.IsNullOrWhiteSpace(value))
            {
                await sendCommand(value.Trim());
            }
        }

        if (inferredHint == SitemapInputHint.Color)
        {
            grid.ColumnDefinitions[layout.ControlColumn].Width = new GridLength(104);
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                MinWidth = 280,
                MaxWidth = 340
            };

            var selectedColor = TryResolveOpenHabColor(rawValue, out var initialColor)
                ? initialColor
                : Microsoft.UI.Colors.White;
            var currentCommand = BuildOpenHabColorCommand(selectedColor);
            var button = CreateCompactColorTrigger(selectedColor, "\uE790", 96);

            var preview = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(selectedColor),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1)
            };

            var stateText = new TextBlock
            {
                Text = currentCommand,
                Opacity = 0.7
            };

            var picker = new ColorPicker
            {
                IsAlphaEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Color = selectedColor
            };

            if (sendCommand is not null)
            {
                var colorState = new ColorCommandState();
                picker.ColorChanged += async (_, args) =>
                {
                    var command = BuildOpenHabColorCommand(args.NewColor);
                    stateText.Text = command;
                    preview.Background = new SolidColorBrush(args.NewColor);
                    UpdateColorPreview(button, args.NewColor);

                    colorState.DebounceCts?.Cancel();
                    colorState.DebounceCts?.Dispose();
                    var cts = new CancellationTokenSource();
                    colorState.DebounceCts = cts;
                    try
                    {
                        await Task.Delay(ColorPickerMoveDebounceMs, cts.Token);
                        if (!string.Equals(colorState.LastSentCommand, command, StringComparison.Ordinal))
                        {
                            await SubmitAsync(command);
                            colorState.LastSentCommand = command;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // New color change superseded this event.
                    }
                };
            }
            else
            {
                picker.ColorChanged += (_, args) =>
                {
                    var command = BuildOpenHabColorCommand(args.NewColor);
                    stateText.Text = command;
                    preview.Background = new SolidColorBrush(args.NewColor);
                    UpdateColorPreview(button, args.NewColor);
                };
            }

            panel.Children.Add(preview);
            panel.Children.Add(stateText);
            panel.Children.Add(picker);
            button.Flyout = new Flyout { Content = panel };
            Grid.SetColumn(button, layout.ControlColumn);
            grid.Children.Add(button);
            return WrapWithBorder(grid);
        }

        if (inferredHint == SitemapInputHint.Date)
        {
            var button = CreateCompactInputTrigger(FormatInputDisplayValue(rawValue, SitemapInputHint.Date), "\uE787", 160);
            var picker = new DatePicker
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (TryParseDateOnly(rawValue, out var date))
            {
                picker.Date = date;
            }

            if (sendCommand is not null)
            {
                picker.DateChanged += async (_, _) => await SubmitAsync($"{picker.Date:yyyy-MM-dd}");
            }

            button.Flyout = new Flyout { Content = picker };
            Grid.SetColumn(button, layout.ControlColumn);
            grid.Children.Add(button);
            return WrapWithBorder(grid);
        }

        if (inferredHint == SitemapInputHint.Time)
        {
            var button = CreateCompactInputTrigger(FormatInputDisplayValue(rawValue, SitemapInputHint.Time), "\uE121", 120);
            var picker = new TimePicker
            {
                ClockIdentifier = "24HourClock",
                MinuteIncrement = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (TryParseTimeOnly(rawValue, out var time))
            {
                picker.Time = time;
            }

            if (sendCommand is not null)
            {
                picker.TimeChanged += async (_, _) => await SubmitAsync($"{picker.Time:hh\\:mm\\:ss}");
            }

            button.Flyout = new Flyout { Content = picker };
            Grid.SetColumn(button, layout.ControlColumn);
            grid.Children.Add(button);
            return WrapWithBorder(grid);
        }

        if (inferredHint == SitemapInputHint.DateTime)
        {
            var button = CreateCompactInputTrigger(FormatInputDisplayValue(rawValue, SitemapInputHint.DateTime), "\uE787", 190);
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                MinWidth = 260,
                MaxWidth = 320
            };
            var datePicker = new DatePicker
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0
            };
            var timePicker = new TimePicker
            {
                ClockIdentifier = "24HourClock",
                MinuteIncrement = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0
            };

            if (TryParseDateTime(rawValue, out var dateTime))
            {
                datePicker.Date = dateTime;
                timePicker.Time = dateTime.TimeOfDay;
            }

            if (sendCommand is not null)
            {
                async Task SendComposedAsync()
                {
                    var composed = datePicker.Date.Date + timePicker.Time;
                    await SubmitAsync(composed.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
                }

                datePicker.DateChanged += async (_, _) => await SendComposedAsync();
                timePicker.TimeChanged += async (_, _) => await SendComposedAsync();
            }

            panel.Children.Add(datePicker);
            panel.Children.Add(timePicker);
            button.Flyout = new Flyout { Content = panel };
            Grid.SetColumn(button, layout.ControlColumn);
            grid.Children.Add(button);
            return WrapWithBorder(grid);
        }

        var input = new TextBox
        {
            Text = rawValue,
            PlaceholderText = inferredHint switch
            {
                SitemapInputHint.Number => "number",
                _ => "value"
            },
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            MinWidth = 120,
            MaxWidth = 180
        };
        if (inferredHint == SitemapInputHint.Number)
        {
            input.InputScope = new Microsoft.UI.Xaml.Input.InputScope
            {
                Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) }
            };
        }

        var textPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        input.KeyDown += async (_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Enter)
            {
                var normalized = NormalizeInputByHint(input.Text, inferredHint);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    input.Text = normalized;
                    await SubmitAsync(normalized);
                }
            }
        };

        textPanel.Children.Add(input);
        Grid.SetColumn(textPanel, layout.ControlColumn);
        grid.Children.Add(textPanel);
        return WrapWithBorder(grid);
    }

    private static Button CreateCompactInputTrigger(string displayValue, string glyph, double width)
    {
        var content = new Grid { ColumnSpacing = 6 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = displayValue,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 0);
        content.Children.Add(text);

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            Opacity = 0.65,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 1);
        content.Children.Add(icon);

        return new Button
        {
            Content = content,
            Width = width,
            MinWidth = 0,
            MinHeight = 30,
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Button CreateCompactColorTrigger(global::Windows.UI.Color color, string glyph, double width)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var swatch = new Border
        {
            Tag = "sitemap-color-preview",
            Width = 40,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(color)
        };
        content.Children.Add(swatch);

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            Opacity = 0.65,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        content.Children.Add(icon);

        return new Button
        {
            Content = content,
            Width = width,
            MinWidth = 0,
            MinHeight = 30,
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static SitemapInputHint ResolveInputHint(SitemapRowDescriptor row)
    {
        if (row.InputHint != SitemapInputHint.Auto)
        {
            return row.InputHint;
        }

        var raw = row.RawItemState ?? row.RawState ?? row.State;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SitemapInputHint.Text;
        }

        if (TryParseDateTime(raw, out _))
        {
            return raw.Contains('T', StringComparison.OrdinalIgnoreCase)
                ? SitemapInputHint.DateTime
                : SitemapInputHint.Date;
        }

        if (TryParseDateOnly(raw, out _))
        {
            return SitemapInputHint.Date;
        }

        if (TryParseTimeOnly(raw, out _))
        {
            return SitemapInputHint.Time;
        }

        return TryParseNumericState(raw, out _) ? SitemapInputHint.Number : SitemapInputHint.Text;
    }

    private static bool TryParseDateOnly(string? raw, out DateTimeOffset date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            date = parsed;
            return true;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date);
    }

    private static bool TryParseTimeOnly(string? raw, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            time = parsed;
            return true;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
        {
            time = dateTime.TimeOfDay;
            return true;
        }

        return false;
    }

    private static bool TryParseDateTime(string? raw, out DateTimeOffset dateTime)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dateTime))
        {
            return true;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dateTime);
    }

    internal static string? NormalizeInputByHint(string? raw, SitemapInputHint hint)
    {
        return SitemapUiLogic.NormalizeInputByHint(raw, hint);
    }

    private static string FormatInputDisplayValue(string? raw, SitemapInputHint hint)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return hint switch
        {
            SitemapInputHint.Date when TryParseDateOnly(raw, out var date) => date.ToString("d", CultureInfo.CurrentCulture),
            SitemapInputHint.Time when TryParseTimeOnly(raw, out var time) => time.ToString(@"hh\:mm", CultureInfo.CurrentCulture),
            SitemapInputHint.DateTime when TryParseDateTime(raw, out var dateTime) => dateTime.ToString("g", CultureInfo.CurrentCulture),
            SitemapInputHint.Color when TryResolveOpenHabColor(raw, out var color) => BuildOpenHabColorHex(color),
            _ => raw.Trim()
        };
    }

    internal static bool TryParseOpenHabColorState(string? raw, out double hue, out double saturation, out double brightness)
    {
        return SitemapUiLogic.TryParseOpenHabColorState(raw, out hue, out saturation, out brightness);
    }

    internal static bool TryResolveOpenHabColor(string? raw, out global::Windows.UI.Color color)
    {
        if (SitemapUiLogic.TryResolveOpenHabColor(raw, out var sitemapColor))
        {
            color = ToWindowsColor(sitemapColor);
            return true;
        }

        color = default;
        return false;
    }

    internal static string BuildOpenHabColorCommand(global::Windows.UI.Color color)
    {
        return SitemapUiLogic.BuildOpenHabColorCommand(ToSitemapColor(color));
    }

    internal static global::Windows.UI.Color ResolveColorTemperaturePreview(double value, double min, double max)
    {
        return ToWindowsColor(SitemapUiLogic.ResolveColorTemperaturePreview(value, min, max));
    }

    private static string BuildOpenHabColorHex(global::Windows.UI.Color color)
    {
        return SitemapUiLogic.BuildOpenHabColorHex(ToSitemapColor(color));
    }

    internal static void ColorToHsv(global::Windows.UI.Color color, out double hue, out double saturation, out double brightness)
    {
        SitemapUiLogic.ColorToHsv(ToSitemapColor(color), out hue, out saturation, out brightness);
    }

    private static global::Windows.UI.Color ToWindowsColor(SitemapColor color)
    {
        return global::Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static global::Windows.UI.Color CreateColor(byte a, byte r, byte g, byte b)
    {
        return global::Windows.UI.Color.FromArgb(a, r, g, b);
    }

    private static SitemapColor ToSitemapColor(global::Windows.UI.Color color)
    {
        return new SitemapColor(color.A, color.R, color.G, color.B);
    }

    private static string NormalizeInputStateValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();
        return value.Equals("NULL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("UNDEF", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
    }

    private static Border CreateFallback(SitemapRowDescriptor row)
    {
        return WrapWithBorder(new Button
        {
            Content = CreateButtonTextBlock(row.Label),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false,
            BorderThickness = new Thickness(0)
        });
    }

    private static string FormatSliderStateText(string? template, double value)
    {
        return SitemapRowVisualPolicy.FormatSliderStateText(template, value);
    }

    private static Border CreateButton(
        SitemapRowDescriptor row,
        Func<string, Task>? sendCommand,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        var grid = layout.Grid;
        var command = row.Command
            ?? (row.SelectionOptions.Count > 0 ? row.SelectionOptions[0].Command : null)
            ?? row.RawItemState
            ?? row.RawState
            ?? row.State;
        var button = new Button
        {
            Content = TextLocalizer.Get("Sitemap.Action.Run"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = !string.IsNullOrWhiteSpace(command) && sendCommand is not null
        };
        button.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(command) && sendCommand is not null)
            {
                await sendCommand(command);
            }
        };
        Grid.SetColumn(button, layout.ControlColumn);
        grid.Children.Add(button);
        return WrapWithBorder(grid);
    }

    private static Border CreateButtonGrid(
        SitemapRowDescriptor row,
        Func<string, Task>? sendCommand,
        Func<SitemapMapOption, bool, Task>? sendButtonGridCommand,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        container.Children.Add(layout.Grid);
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        var hasExplicitCoordinates = row.SelectionOptions.Any(o => o.Row.HasValue || o.Column.HasValue);
        var maxColumn = hasExplicitCoordinates
            ? Math.Max(1, row.SelectionOptions.Where(o => o.Column.HasValue).Select(o => o.Column!.Value).DefaultIfEmpty(1).Max())
            : Math.Max(1, row.SelectionOptions.Count);
        var maxRow = hasExplicitCoordinates
            ? Math.Max(1, row.SelectionOptions.Where(o => o.Row.HasValue).Select(o => o.Row!.Value).DefaultIfEmpty(1).Max())
            : 1;
        for (var c = 0; c < maxColumn; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < maxRow; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var fallbackIndex = 0;
        foreach (var option in row.SelectionOptions)
        {
            var rowIndex = option.Row.HasValue && option.Row.Value > 0 ? option.Row.Value - 1 : fallbackIndex / maxColumn;
            var colIndex = option.Column.HasValue && option.Column.Value > 0 ? option.Column.Value - 1 : fallbackIndex % maxColumn;
            fallbackIndex++;
            var button = new Button
            {
                Content = option.Label,
                IsEnabled = sendCommand is not null || sendButtonGridCommand is not null,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ApplyButtonGridColors(button, option.IsActive, isHovered: false, isPressed: false);

            button.PointerEntered += (_, _) => ApplyButtonGridColors(button, option.IsActive, isHovered: true, isPressed: false);
            button.PointerExited += (_, _) => ApplyButtonGridColors(button, option.IsActive, isHovered: false, isPressed: false);

            button.PointerPressed += (_, _) =>
            {
                ApplyButtonGridColors(button, option.IsActive, isHovered: true, isPressed: true);
            };

            button.PointerReleased += (_, _) =>
            {
                ApplyButtonGridColors(button, option.IsActive, isHovered: true, isPressed: false);
            };
            button.Click += async (_, _) =>
            {
                var releaseCommand = option.ReleaseCommand;
                var clickCommand = option.ClickCommand ?? option.Command;
                var hasReleaseCommand = !string.IsNullOrWhiteSpace(releaseCommand)
                                        && !string.Equals(releaseCommand, "NULL", StringComparison.OrdinalIgnoreCase);
                var hasClickCommand = !string.IsNullOrWhiteSpace(clickCommand)
                                      && !string.Equals(clickCommand, "NULL", StringComparison.OrdinalIgnoreCase);

                if (sendButtonGridCommand is not null)
                {
                    if (hasReleaseCommand)
                    {
                        await sendButtonGridCommand(option, true);
                    }
                    else if (hasClickCommand)
                    {
                        await sendButtonGridCommand(option, false);
                    }

                    return;
                }

                if (sendCommand is not null)
                {
                    if (hasReleaseCommand)
                    {
                        await sendCommand(releaseCommand!);
                    }
                    else if (hasClickCommand)
                    {
                        await sendCommand(clickCommand!);
                    }
                }
            };
            Grid.SetRow(button, rowIndex);
            Grid.SetColumn(button, colIndex);
            grid.Children.Add(button);
        }
        container.Children.Add(grid);
        return WrapWithBorder(container);
    }

    private static void ApplyButtonGridColors(Button button, bool isActive, bool isHovered, bool isPressed)
    {
        var inactiveBackground = CreateColor(255, 245, 245, 245);
        var inactiveHoverBackground = CreateColor(255, 237, 237, 237);
        var inactivePressedBackground = CreateColor(255, 226, 226, 226);
        var activeBackground = CreateColor(255, 255, 62, 133);
        var activeHoverBackground = CreateColor(255, 250, 45, 120);
        var activePressedBackground = CreateColor(255, 230, 30, 108);

        var background = ResolveButtonGridBackground(
            isActive,
            isHovered,
            isPressed,
            inactiveBackground,
            inactiveHoverBackground,
            inactivePressedBackground,
            activeBackground,
            activeHoverBackground,
            activePressedBackground);
        var foreground = isActive ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;

        button.Background = new SolidColorBrush(background);
        button.Foreground = new SolidColorBrush(foreground);
        button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(foreground);
        button.Resources["ButtonForegroundPressed"] = new SolidColorBrush(foreground);
        button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(isActive ? activeHoverBackground : inactiveHoverBackground);
        button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(isActive ? activePressedBackground : inactivePressedBackground);
    }

    private static global::Windows.UI.Color ResolveButtonGridBackground(
        bool isActive,
        bool isHovered,
        bool isPressed,
        global::Windows.UI.Color inactiveBackground,
        global::Windows.UI.Color inactiveHoverBackground,
        global::Windows.UI.Color inactivePressedBackground,
        global::Windows.UI.Color activeBackground,
        global::Windows.UI.Color activeHoverBackground,
        global::Windows.UI.Color activePressedBackground)
    {
        if (isActive)
        {
            if (isPressed)
            {
                return activePressedBackground;
            }

            return isHovered ? activeHoverBackground : activeBackground;
        }

        if (isPressed)
        {
            return inactivePressedBackground;
        }

        return isHovered ? inactiveHoverBackground : inactiveBackground;
    }

    private static Border CreateImage(
        SitemapRowDescriptor row,
        Uri? baseUri = null,
        bool useWindowsIcons = false,
        IconAuthContext? iconAuth = null)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons, iconAuth);
        container.Children.Add(layout.Grid);
        var value = row.RawItemState ?? row.RawState ?? row.State;
        if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch };
            container.SizeChanged += (_, args) =>
            {
                var targetWidth = Math.Max(120, args.NewSize.Width * 0.98);
                image.Width = targetWidth;
                image.MaxWidth = targetWidth;
            };
            var comma = value.IndexOf(',');
            if (comma > 0 && value.Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _ = LoadRawImageBytesAsync(image, Convert.FromBase64String(value[(comma + 1)..]));
                }
                catch (FormatException)
                {
                    // Invalid inline image payloads are ignored; the row still renders its label and icon.
                }
            }
            container.Children.Add(image);
        }
        return WrapWithBorder(container);
    }

    private static async Task LoadRawImageBytesAsync(Image image, byte[] bytes)
    {
        var source = await OpenHabIconImageSourceLoader.CreateImageSourceFromBytesAsync(bytes, null);
        if (source is not null) image.Source = source;
    }

    private static Border CreateWebview(SitemapRowDescriptor row, Uri? baseUri)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons: false, iconAuth: null);

        var resolvedUri = SitemapUiLogic.ResolveEmbeddedUrl(row, baseUri);
        if (resolvedUri is null)
        {
            container.Children.Add(layout.Grid);
            container.Children.Add(new TextBlock
            {
                Text = TextLocalizer.Get("Sitemap.WebView.NoUrlConfigured"),
                Opacity = 0.4,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic
            });
            return WrapWithBorder(container);
        }

        // Reliable fallback: always show an "Open in browser" button.
        AddOpenInBrowserHeaderButton(layout, resolvedUri);
        container.Children.Add(layout.Grid);

        // Try WebView2 for inline display; WebView2 runtime may be missing
        var webview = new WebView2
        {
            Height = ResolveWebviewHeight(row),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        _ = InitializeWebViewAsync(webview, resolvedUri!);
        container.Children.Add(webview);

        return WrapWithBorder(container);
    }

    private static void OpenUrlInBrowser(Uri uri)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Best-effort browser launch.
        }
    }

    private static async Task InitializeWebViewAsync(WebView2 webview, Uri uri)
    {
        try
        {
            await webview.EnsureCoreWebView2Async();
            webview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webview.CoreWebView2.Settings.IsScriptEnabled = true;
            webview.Source = uri;
        }
        catch
        {
            // WebView2 runtime not available — hide the WebView2 control,
            // the "Open in browser" button remains as fallback.
            webview.Visibility = Visibility.Collapsed;
            webview.Height = 0;
        }
    }

    private static async Task InitializeWebViewHtmlAsync(WebView2 webview, string html)
    {
        try
        {
            await webview.EnsureCoreWebView2Async();
            webview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webview.CoreWebView2.Settings.IsScriptEnabled = true;
            webview.NavigateToString(html);
        }
        catch
        {
            webview.Visibility = Visibility.Collapsed;
            webview.Height = 0;
        }
    }

    private static Border CreateMapview(SitemapRowDescriptor row, Uri? baseUri)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName ?? "location", row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons: false, iconAuth: null);

        var mapUri = BuildMapviewUrl(row);
        if (mapUri is null)
        {
            container.Children.Add(layout.Grid);
            container.Children.Add(new TextBlock
            {
                Text = TextLocalizer.Get("Sitemap.MapView.NoLocationConfigured"),
                Opacity = 0.4,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic
            });
            return WrapWithBorder(container);
        }

        AddOpenInBrowserHeaderButton(layout, mapUri);
        container.Children.Add(layout.Grid);
        var webview = new WebView2
        {
            Height = ResolveWebviewHeight(row),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        _ = InitializeWebViewAsync(webview, mapUri);
        container.Children.Add(webview);
        return WrapWithBorder(container);
    }

    private static Border CreateVideo(SitemapRowDescriptor row, Uri? baseUri)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName ?? "video", row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons: false, iconAuth: null);

        var videoUri = SitemapUiLogic.ResolveEmbeddedUrl(row, baseUri);
        if (videoUri is null)
        {
            container.Children.Add(layout.Grid);
            container.Children.Add(new TextBlock
            {
                Text = TextLocalizer.Get("Sitemap.Video.NoUrlConfigured"),
                Opacity = 0.4,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic
            });
            return WrapWithBorder(container);
        }

        AddOpenInBrowserHeaderButton(layout, videoUri);
        container.Children.Add(layout.Grid);
        var webview = new WebView2
        {
            Height = ResolveWebviewHeight(row),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        _ = InitializeWebViewHtmlAsync(webview, BuildVideoHtml(videoUri, row.Encoding));
        container.Children.Add(webview);
        return WrapWithBorder(container);
    }

    private static void AddOpenInBrowserHeaderButton(RowLayout layout, Uri uri)
    {
        var openButton = new Button
        {
            Tag = "sitemap-open-browser",
            Content = new FontIcon
            {
                Glyph = "\uE8A7",
                FontSize = 13,
                FontFamily = new FontFamily("Segoe MDL2 Assets")
            },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 34,
            MinWidth = 0,
            MinHeight = 30,
            Padding = new Thickness(4),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(openButton, TextLocalizer.Get("Sitemap.WebView.OpenInBrowser"));
        openButton.Click += (_, _) => OpenUrlInBrowser(uri);
        layout.Grid.ColumnDefinitions[layout.ControlColumn].Width = new GridLength(40);
        Grid.SetColumn(openButton, layout.ControlColumn);
        layout.Grid.Children.Add(openButton);
    }

    private static string BuildVideoHtml(Uri uri, string? encoding)
    {
        var source = System.Net.WebUtility.HtmlEncode(uri.AbsoluteUri);
        var isMjpeg = string.Equals(encoding, "mjpeg", StringComparison.OrdinalIgnoreCase);
        var media = isMjpeg
            ? $"""<img src="{source}" alt="" style="max-width:100%;width:100%;height:100%;object-fit:contain;" />"""
            : $"""<video src="{source}" controls autoplay muted playsinline style="max-width:100%;width:100%;height:100%;object-fit:contain;background:#000;"></video>""";

        return $$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <style>
                html, body {
                  margin: 0;
                  width: 100%;
                  height: 100%;
                  background: #000;
                  overflow: hidden;
                }
                body {
                  display: flex;
                  align-items: center;
                  justify-content: center;
                }
              </style>
            </head>
            <body>{{media}}</body>
            </html>
            """;
    }

    internal static Uri? BuildMapviewUrl(SitemapRowDescriptor row)
    {
        return SitemapUiLogic.BuildMapviewUrl(row);
    }

    private static Border CreateChart(SitemapRowDescriptor row, Uri? baseUri, int chartDpi, IconAuthContext? iconAuth)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons: false, iconAuth: null);
        container.Children.Add(layout.Grid);

        var chartUrl = BuildChartUrl(row, baseUri, chartDpi);
        if (chartUrl is not null)
        {
            Image image = new()
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };

            // Store the chart URL as tag for potential refresh
            image.Tag = chartUrl.AbsoluteUri;

            // Set width relative to container on load
            container.SizeChanged += (_, args) =>
            {
                var targetWidth = Math.Max(120, args.NewSize.Width * 0.95);
                image.Width = targetWidth;
                image.MaxWidth = targetWidth;
            };

            _ = LoadChartImageWithAuthAsync(image, chartUrl, iconAuth);
            container.Children.Add(image);
        }
        else
        {
            container.Children.Add(new TextBlock
            {
                Text = TextLocalizer.Get("Sitemap.Chart.RequiresItem"),
                Opacity = 0.4,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic
            });
        }

        return WrapWithBorder(container);
    }

    /// <summary>Builds an openHAB chart image URL from the row descriptor and base URI.</summary>
    internal static Uri? BuildChartUrl(SitemapRowDescriptor row, Uri? baseUri, int chartDpi = 96, bool cacheBust = false)
    {
        return SitemapUiLogic.BuildChartUrl(row, baseUri, chartDpi, cacheBust);
    }

    private static async Task LoadChartImageWithAuthAsync(Image image, Uri chartUrl, IconAuthContext? iconAuth)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, chartUrl);
            if (iconAuth is { } context)
            {
                IconAuthHeaderHelper.ApplyAuthHeaders(request, context);
            }

            using var response = await IconHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                return;
            }

            var source = await OpenHabIconImageSourceLoader.CreateImageSourceFromBytesAsync(
                bytes,
                response.Content.Headers.ContentType?.MediaType);
            if (source is not null)
            {
                image.Source = source;
            }
        }
        catch
        {
            // Silently degrade if chart image can't be loaded from authenticated endpoint.
        }
    }

    private static TextBlock CreateButtonTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        };
    }

    private static Border WrapWithBorder(FrameworkElement child)
    {
        return new Border
        {
            Child = child,
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 40
        };
    }
}


