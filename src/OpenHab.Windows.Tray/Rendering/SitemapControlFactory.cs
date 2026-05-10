using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Reflection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenHab.Core;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using Windows.Storage.Streams;

namespace OpenHab.Windows.Tray.Rendering;

public static partial class SitemapControlFactory
{
    private const double ValueLaneWidth = 96;
    private const double ControlLaneWidth = 56;
    private const double NavigateChevronLaneWidth = 20;
    private const int SliderMoveDebounceMs = 200;
    private static readonly string[] IconFormatsByPreference = ["svg", "png"];
    private static readonly HttpClient IconHttpClient = new();
    private static readonly Regex FirstNumberRegex = FirstNumberRegexFactory();
    private static readonly ConcurrentDictionary<string, ImageSource> IconSourceCache = new(StringComparer.Ordinal);
    private static readonly System.Threading.Lock IconProbeSyncRoot = new();
    private static readonly HashSet<string> ProbedIconEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Win11IconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- LIGHTING ---
        ["light"] = "\uE706", ["lights"] = "\uE706",
        ["lighton"] = "\uE706", ["lightoff"] = "\uE706",
        ["lightson"] = "\uE706", ["lightsoff"] = "\uE706",
        ["dimmer"] = "\uE706",                                // Brightness (Sun shape works well for dimmer)
        ["colorpicker"] = "\uE790", ["color"] = "\uE790",

        // --- SWITCHES & POWER ---
        ["switch"] = "\uE7E8",                                // PowerButton
        ["switchon"] = "\uE7E8", ["switchoff"] = "\uE7E8",
        ["poweron"] = "\uE7E8", ["poweroff"] = "\uE7E8",
        ["energy"] = "\uE946", ["power"] = "\uE946",          // LightningBolt
        ["outlet"] = "\uE994", ["plug"] = "\uE994",           // Plug/Connector
        ["poweroutlet"] = "\uE994",["power_outlet"] = "\uE994",
        ["battery"] = "\uEBA0",["batterylevel"] = "\uEBA0",  // Battery0

        // --- DOORS, WINDOWS & BLINDS ---
        ["rollershutter"] = "\uE728", ["blinds"] = "\uE728",  // Hamburger menu mimics horizontal blind slats perfectly
        ["window"] = "\uE7C4",                                // Windowpane (Distinct from Door)
        ["door"] = "\uE8E1", ["garagedoor"] = "\uE8E1",
        ["contact"] = "\uE8E1",
        ["lock"] = "\uE72E",

        // --- CLIMATE & HVAC ---
        ["heating"] = "\uE9CA", ["temp"] = "\uE9CA",          // Thermometer
        ["temperature"] = "\uE9CA", ["climate"] = "\uE9CA",
        ["radiator"] = "\uE9CA",
        ["humidity"] = "\uEB42", ["moisture"] = "\uEB42",     // Drop (Water droplet)
        ["water"] = "\uEB42",
        ["gas"] = "\uE825",                                   // Gas pump / generic fuel
        ["fan"] = "\uE785", ["fan_ceiling"] = "\uE785",       // Sync / Rotating arrows
        ["pump"] = "\uE785",

        // --- SENSORS & SECURITY ---
        ["motion"] = "\uE916",                                // Activity (Zig-zag pulse line)
        ["presence"] = "\uE716", ["occupancy"] = "\uE716",    // Person / Account
        ["alarm"] = "\uEA8F", ["siren"] = "\uEA8F",           // Ringer / Bell
        ["smoke"] = "\uE7BA",                                 // Alert Warning Triangle
        ["camera"] = "\uE722",

        // --- MULTIMEDIA ---
        ["speaker"] = "\uE7F5",["audio"] = "\uE7F5", ["receiver"] = "\uE7F5",
        ["tv"] = "\uE7F4", ["screen"] = "\uE7F4",             // TVMonitor
        ["player"] = "\uE768", ["music"] = "\uE768",
        ["image"] = "\uE722", ["video"] = "\uE714",           // \uE714 is Video

        // --- WEATHER ---
        ["sun"] = "\uE706", ["sunrise"] = "\uE706", ["sunset"] = "\uE706",
        ["moon"] = "\uE708",                                  // QuietHours (Moon shape)
        ["cloud"] = "\uE753", ["weather"] = "\uE753",
        ["sunclouds"] = "\uE753", ["sun_clouds"] = "\uE753",
        ["rain"] = "\uEB42",                                  // Drop
        ["wind"] = "\uE743",                                  // Wind/Cloud
        ["snow"] = "\uE9C8",                                  // Snowflake
        ["pressure"] = "\uE976",

        // --- ROOMS & LOCATIONS ---
        ["groundfloor"] = "\uE831", ["ground_floor"] = "\uE831",["firstfloor"] = "\uE831", ["first_floor"] = "\uE831",
        ["floorplan"] = "\uE831",
        ["kitchen"] = "\uE7A7", ["bath"] = "\uE7A8", ["bathroom"] = "\uE7A8",
        ["bedroom"] = "\uE7A9", ["living"] = "\uE7F4",        // Mapped Living Room to TV Monitor
        ["office"] = "\uE7AB",
        ["garage"] = "\uE83D", ["garden"] = "\uE7A5", ["terrace"] = "\uE7A5",
        ["attic"] = "\uE831", ["cellar"] = "\uE831", ["basement"] = "\uE831",
        ["location"] = "\uE707",

        // --- MISC & UI ---
        ["network"] = "\uE701", ["wifi"] = "\uE701",
        ["quality"] = "\uE769", ["co2"] = "\uE769", ["airquality"] = "\uE769",
        ["chart"] = "\uE9D2", ["number"] = "\uE9D2",
        ["pie"] = "\uE9D2", ["line"] = "\uE9D2",
        ["text"] = "\uE8A5", ["string"] = "\uE8A5", ["group"] = "\uE902",
        ["settings"] = "\uE713", ["setup"] = "\uE713",
        ["time"] = "\uE787", ["datetime"] = "\uE787", ["date"] = "\uE787",
        ["none"] = "\uE776"
    };
    // Built once: normalized-key → glyph, for fuzzy icon-name matching.
    // GroupBy handles alias collisions (groundfloor + ground_floor, firstfloor + first_floor)
    // that normalize to the same key but share an identical glyph.
    private static readonly Dictionary<string, string> NormalizedWin11IconMap = Win11IconMap
        .GroupBy(kvp => NormalizeIconName(kvp.Key))
        .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

    internal static string? ResolveGlyphForIcon(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return null;

        // Exact match first (case-insensitive, preserves the original map behaviour).
        if (Win11IconMap.TryGetValue(iconName, out var glyph))
            return glyph;

        // Fallback: normalize and try again so common variants still match.
        var normalized = NormalizeIconName(iconName);
        if (NormalizedWin11IconMap.TryGetValue(normalized, out glyph))
            return glyph;

        return null;
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

    [GeneratedRegex(@"[-+]?\d+([.,]\d+)?", RegexOptions.Compiled)]
    private static partial Regex FirstNumberRegexFactory();

    [GeneratedRegex("<svg\\b", RegexOptions.IgnoreCase)]
    private static partial Regex SvgOpenTagRegex();

    /// <summary>
    /// Collapses separators and digits so common openHAB icon-name variants
    /// (e.g. "roller_shutter", "ground-floor", "chart-1") still resolve.
    /// </summary>
    internal static string NormalizeIconName(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return string.Empty;
        ReadOnlySpan<char> span = iconName.Trim();

        // Estimate worst-case capacity (trim + removed separators/digits).
        var sb = new System.Text.StringBuilder(span.Length);
        foreach (var ch in span)
        {
            if (ch is '_' or '-') continue;   // collapse separators
            if (char.IsDigit(ch)) continue;   // strip numeric suffixes
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    /// <summary>Pure-logic query: does the normalized icon name resolve
    /// to a known Win11 glyph?  Safe to call in tests without WinUI runtime.</summary>
    internal static bool CanResolveNormalizedIcon(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return false;
        // Exact match first.
        if (Win11IconMap.ContainsKey(iconName)) return true;
        // Normalized fallback.
        var normalized = NormalizeIconName(iconName);
        return NormalizedWin11IconMap.ContainsKey(normalized);
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
        ArgumentNullException.ThrowIfNull(row);

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

        // Update visibility first
        control.Visibility = updated.IsVisible ? Visibility.Visible : Visibility.Collapsed;

        var rawState = updated.RawState ?? updated.State;

        switch (updated.Control)
        {
            case RenderControlKind.Toggle:
                var toggle = FindVisualChild<ToggleSwitch>(inner);
                if (toggle is not null)
                {
                    var isOn = string.Equals(rawState, "ON", StringComparison.OrdinalIgnoreCase);
                    // Suppress Toggled event to prevent feedback loop
                    toggle.Tag = "suppress";
                    toggle.IsOn = isOn;
                    toggle.Tag = null;
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

                    var currentStateText = FindStateTextBlockText(inner);
                    UpdateStateTextBlock(inner, FormatSliderStateText(currentStateText, val));
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
            case RenderControlKind.Chart:
            case RenderControlKind.Fallback:
                UpdateStateTextBlock(inner, updated.State ?? string.Empty);
                break;
        }

        ApplyRowColors(inner, updated);
    }

    public static void SetVisibility(FrameworkElement control, bool visible)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
        parsed = default;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        var input = color.Trim();
        if (input.StartsWith('#'))
        {
            var hex = input[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => $"{c}{c}"));
            }

            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                parsed = Microsoft.UI.ColorHelper.FromArgb(
                    255,
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
                return true;
            }

            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                parsed = Microsoft.UI.ColorHelper.FromArgb(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));
                return true;
            }
        }

        var property = typeof(Microsoft.UI.Colors).GetProperty(input, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (property?.PropertyType == typeof(global::Windows.UI.Color))
        {
            parsed = (global::Windows.UI.Color)property.GetValue(null)!;
            return true;
        }

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

    private static bool TryAddIcon(
        Grid grid,
        int column,
        string? iconName,
        string? iconState,
        string? iconColor,
        Uri? baseUri,
        bool useWindowsIcons,
        IconAuthContext? iconAuth)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return false;

        if (useWindowsIcons)
        {
            var winIcon = ResolveWin11Icon(iconName);
            if (winIcon is not null)
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                    DiagnosticLogger.Info($"Icon render via Win11 glyph: icon='{iconName}', normalized='{NormalizeIconName(iconName)}'");
                winIcon.VerticalAlignment = VerticalAlignment.Center;
                winIcon.Tag = "sitemap-icon";
                if (TryCreateBrush(iconColor, out var iconBrush))
                {
                    winIcon.Foreground = iconBrush;
                }
                Grid.SetColumn(winIcon, column);
                grid.Children.Add(winIcon);
                return true;
            }

                if (!DiagnosticLogger.SuppressIconLogging)
                    DiagnosticLogger.Warn($"Win11 glyph mapping missing: icon='{iconName}', normalized='{NormalizeIconName(iconName)}'; falling back to server icon endpoint");
        }

        if (baseUri is not null)
        {
            var image = new Image
            {
                Width = 26,
                Height = 26,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(image, column);
            grid.Children.Add(image);

            if (iconAuth is { } authContext)
            {
                StartIconProbeIfNeeded(baseUri, authContext);
                _ = LoadIconAsync(image, baseUri, iconName, iconState, iconColor, authContext);
                return true;
            }

            _ = LoadIconAsync(image, baseUri, iconName, iconState, iconColor, null);
            return true;
        }

            if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Warn($"Icon skipped: icon='{iconName}', state='{iconState ?? "(none)"}', reason='no glyph mapping and no base URI'");
        return false;
    }

    private static void StartIconProbeIfNeeded(Uri baseUri, IconAuthContext authContext)
    {
        var probeKey = $"{baseUri.Scheme}://{baseUri.Authority}|{GetAuthMode(authContext)}|{authContext.TransportKind?.ToString() ?? "unknown"}";
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
            ApplyAuthHeaders(headRequest, authContext);
            using var headResponse = await IconHttpClient.SendAsync(headRequest);

            if (headResponse.IsSuccessStatusCode)
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Info($"Icon probe OK (HEAD): endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? "unknown"}', auth='{GetAuthMode(authContext)}', status={(int)headResponse.StatusCode}");
                return;
            }

            if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Warn($"Icon probe HEAD non-success: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? "unknown"}', auth='{GetAuthMode(authContext)}', status={(int)headResponse.StatusCode}");
        }
        catch (Exception ex)
        {
            if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Warn($"Icon probe HEAD failed: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? "unknown"}', auth='{GetAuthMode(authContext)}', error='{ex.GetType().Name}: {ex.Message}'");
        }

        try
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, probeUri);
            ApplyAuthHeaders(getRequest, authContext);
            using var getResponse = await IconHttpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);

            if (getResponse.IsSuccessStatusCode)
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Info($"Icon probe OK (GET): endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? "unknown"}', auth='{GetAuthMode(authContext)}', status={(int)getResponse.StatusCode}");
            }
            else
            {
                if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Warn($"Icon probe GET non-success: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? "unknown"}', auth='{GetAuthMode(authContext)}', status={(int)getResponse.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Warn($"Icon probe GET failed: endpoint='{baseUri.Host}', transport='{authContext.TransportKind?.ToString() ?? "unknown"}', auth='{GetAuthMode(authContext)}', error='{ex.GetType().Name}: {ex.Message}'");
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
        var attempts = new List<string>(IconFormatsByPreference.Length);

        foreach (var format in IconFormatsByPreference)
        {
            var iconUri = BuildOpenHabIconUri(baseUri, iconName, iconState, format);
            if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Info($"Icon request: icon='{iconName}', state='{iconState ?? "(none)"}', format='{format}', url='{iconUri.PathAndQuery}'");

            var attemptResult = await TryLoadIconForFormatAsync(image, iconUri, iconName, iconState, iconColor, format, authContext);
            if (attemptResult is null)
            {
                return;
            }

            attempts.Add(attemptResult);
        }

            if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Warn($"Icon failed: icon='{iconName}', state='{iconState ?? "(none)"}', formats='{string.Join(",", IconFormatsByPreference)}', attempts='{string.Join("; ", attempts)}', auth='{GetAuthMode(authContext)}'");
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
            var cacheKey = $"{iconUri.AbsoluteUri}|{iconColor ?? string.Empty}|{GetAuthMode(authContext)}";
            if (IconSourceCache.TryGetValue(cacheKey, out var cachedSource))
            {
                image.Source = cachedSource;
                var cachedFormat = cachedSource is SvgImageSource ? "svg" : "bitmap";
                if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Info($"Icon cache hit: icon='{iconName}', state='{iconState ?? "(none)"}', url='{iconUri.PathAndQuery}', requestedFormat='{format}', decodedAs='{cachedFormat}', media='cache', auth='{GetAuthMode(authContext)}'");
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, iconUri);
            if (authContext is { } context)
            {
                ApplyAuthHeaders(request, context);
            }

            using var response = await IconHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            if (!response.IsSuccessStatusCode)
            {
                var failedMediaType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Warn($"Icon request failed: icon='{iconName}', state='{iconState ?? "(none)"}', url='{iconUri.PathAndQuery}', requestedFormat='{format}', status={(int)response.StatusCode}, media='{failedMediaType}', auth='{GetAuthMode(authContext)}'");
                return $"format={format}:status={(int)response.StatusCode}";
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                return $"format={format}:empty";
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var source = await CreateImageSourceFromBytesAsync(bytes, mediaType, iconColor);
            if (source is null)
            {
                return $"format={format}:decode-failed(media={mediaType ?? "unknown"})";
            }

            image.Source = source;
            IconSourceCache.TryAdd(cacheKey, source);
            var effectiveFormat = source is SvgImageSource ? "svg" : "bitmap";
            if (!DiagnosticLogger.SuppressIconLogging)
                DiagnosticLogger.Info($"Icon loaded: icon='{iconName}', state='{iconState ?? "(none)"}', url='{iconUri.PathAndQuery}', requestedFormat='{format}', decodedAs='{effectiveFormat}', media='{mediaType ?? "unknown"}', bytes={bytes.Length}, auth='{GetAuthMode(authContext)}'");
            return null;
        }
        catch (Exception ex)
        {
            return $"format={format}:error={ex.GetType().Name}";
        }
    }

    private static async Task<ImageSource?> CreateImageSourceFromBytesAsync(byte[] bytes, string? mediaType, string? iconColor = null)
    {
        if (LooksLikeSvg(mediaType, bytes))
        {
            var tintedBytes = TryApplySvgColorTint(bytes, iconColor) ?? bytes;
            var svg = await CreateSvgFromBytesAsync(tintedBytes);
            if (svg is not null)
            {
                return svg;
            }
        }

        try
        {
            return await CreateBitmapFromBytesAsync(bytes);
        }
        catch
        {
            var svgFallback = await CreateSvgFromBytesAsync(bytes);
            return svgFallback;
        }
    }

    private static byte[]? TryApplySvgColorTint(byte[] svgBytes, string? iconColor)
    {
        if (string.IsNullOrWhiteSpace(iconColor))
        {
            return null;
        }

        if (!TryNormalizeColorToHex(iconColor, out var hexColor))
        {
            return null;
        }

        try
        {
            var svgText = Encoding.UTF8.GetString(svgBytes);
            if (string.IsNullOrWhiteSpace(svgText) || svgText.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            // Many modern icon packs (including f7/iconify) use currentColor.
            // Adding a root style color enables tinting without raster conversion.
            var match = SvgOpenTagRegex().Match(svgText);
            if (!match.Success)
            {
                return null;
            }

            var replacement = $"<svg style=\"color:{hexColor};\"";
            var tinted = svgText[..match.Index] + replacement + svgText[(match.Index + match.Length)..];
            return Encoding.UTF8.GetBytes(tinted);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryNormalizeColorToHex(string color, out string hex)
    {
        hex = string.Empty;
        if (!TryParseColor(color, out var parsed))
        {
            return false;
        }

        hex = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
        return true;
    }

    private static async Task<SvgImageSource?> CreateSvgFromBytesAsync(byte[] bytes)
    {
        var svg = new SvgImageSource
        {
            RasterizePixelWidth = 96,
            RasterizePixelHeight = 96
        };
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var status = await svg.SetSourceAsync(stream);
        return status == SvgImageSourceLoadStatus.Success ? svg : null;
    }

    private static bool LooksLikeSvg(string? mediaType, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sampleLength = Math.Min(bytes.Length, 256);
        if (sampleLength == 0)
        {
            return false;
        }

        var sample = Encoding.UTF8.GetString(bytes, 0, sampleLength).TrimStart('\uFEFF', '\t', '\r', '\n', ' ');
        return sample.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<BitmapImage> CreateBitmapFromBytesAsync(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static void ApplyAuthHeaders(HttpRequestMessage request, IconAuthContext authContext)
    {
        if (!string.IsNullOrWhiteSpace(authContext.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authContext.ApiToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(authContext.BasicUserName))
        {
            var raw = $"{authContext.BasicUserName}:{authContext.BasicPassword ?? string.Empty}";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        }
    }

    private static string GetAuthMode(IconAuthContext? authContext)
    {
        if (authContext is null) return "none";

        var context = authContext.Value;
        if (!string.IsNullOrWhiteSpace(context.ApiToken)) return "bearer";
        if (!string.IsNullOrWhiteSpace(context.BasicUserName)) return "basic";
        return "none";
    }

    internal static Uri BuildOpenHabIconUri(Uri baseUri, string iconName, string? iconState, string format = "png")
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconName);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var sourceSplit = iconName.Split(':', 2, StringSplitOptions.TrimEntries);
        var hasExplicitSource = sourceSplit.Length == 2;
        var iconSource = hasExplicitSource ? sourceSplit[0] : "oh";
        if (hasExplicitSource &&
            (iconSource.Equals("f7", StringComparison.OrdinalIgnoreCase) ||
             iconSource.Equals("material", StringComparison.OrdinalIgnoreCase) ||
             iconSource.Equals("if", StringComparison.OrdinalIgnoreCase) ||
             iconSource.Equals("iconify", StringComparison.OrdinalIgnoreCase)))
        {
            var source = iconSource.ToLowerInvariant();
            var rawName = sourceSplit[1];
            if (source == "f7")
            {
                return new Uri($"https://api.iconify.design/f7/{Uri.EscapeDataString(rawName.Replace('_', '-'))}.svg");
            }

            if (source == "material")
            {
                var materialName = $"baseline-{rawName.Replace('_', '-')}";
                return new Uri($"https://api.iconify.design/ic/{Uri.EscapeDataString(materialName)}.svg");
            }

            var iconifyName = rawName.Replace(':', '/');
            return new Uri($"https://api.iconify.design/{Uri.EscapeDataString(iconifyName)}.svg");
        }

        var escapedIcon = hasExplicitSource
            ? $"{Uri.EscapeDataString(iconSource)}:{Uri.EscapeDataString(sourceSplit[1])}"
            : Uri.EscapeDataString(iconName);
        var query = string.IsNullOrWhiteSpace(iconState)
            ? $"format={Uri.EscapeDataString(format)}"
            : $"format={Uri.EscapeDataString(format)}&state={Uri.EscapeDataString(iconState)}";

        return new Uri(baseUri, $"icon/{escapedIcon}?{query}");
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
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

        var stateBlock = CreateStateTextBlock(row.State ?? string.Empty, row.ValueColor);
        stateBlock.Margin = new Thickness(0, 0, 8, 0);
        stateBlock.Opacity = 0.85;
        Grid.SetColumn(stateBlock, layout.ValueColumn);
        grid.Children.Add(stateBlock);

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
                stateBlock.Text = FormatSliderStateText(stateBlock.Text, args.NewValue);

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
            var matchesCommand = SelectionValueMatches(option.Command, commandSource);
            var matchesLabel = SelectionValueMatches(option.Label, row.State);
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
        var rawValue = row.RawItemState ?? row.RawState ?? row.State ?? string.Empty;

        async Task SubmitAsync(string value)
        {
            if (sendCommand is not null && !string.IsNullOrWhiteSpace(value))
            {
                await sendCommand(value.Trim());
            }
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

    private static string? NormalizeInputByHint(string? raw, SitemapInputHint hint)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        return hint switch
        {
            SitemapInputHint.Date when TryParseDateOnly(value, out var date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SitemapInputHint.Time when TryParseTimeOnly(value, out var time) => time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            SitemapInputHint.DateTime when TryParseDateTime(value, out var dateTime) => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            SitemapInputHint.Number when TryParseNumericState(value, out var number) => number.ToString(CultureInfo.InvariantCulture),
            _ => value
        };
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
            _ => raw.Trim()
        };
    }

    private static bool SelectionValueMatches(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var l = left.Trim();
        var r = right.Trim();
        if (string.Equals(l, r, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Handle numeric command/state variants such as "3" vs "3.0".
        var leftIsNumber = double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumber = double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber);
        return leftIsNumber && rightIsNumber && Math.Abs(leftNumber - rightNumber) < 0.0001;
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
        var numeric = value.ToString("F0", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(template))
        {
            return numeric;
        }

        var match = FirstNumberRegex.Match(template);
        if (!match.Success)
        {
            return numeric;
        }

        return string.Concat(template.AsSpan(0, match.Index), numeric, template.AsSpan(match.Index + match.Length));
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
            Content = "Run",
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
        var inactiveBackground = Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 245);
        var inactiveHoverBackground = Microsoft.UI.ColorHelper.FromArgb(255, 237, 237, 237);
        var inactivePressedBackground = Microsoft.UI.ColorHelper.FromArgb(255, 226, 226, 226);
        var activeBackground = Microsoft.UI.ColorHelper.FromArgb(255, 255, 62, 133);
        var activeHoverBackground = Microsoft.UI.ColorHelper.FromArgb(255, 250, 45, 120);
        var activePressedBackground = Microsoft.UI.ColorHelper.FromArgb(255, 230, 30, 108);

        var background = isActive
            ? (isPressed ? activePressedBackground : isHovered ? activeHoverBackground : activeBackground)
            : (isPressed ? inactivePressedBackground : isHovered ? inactiveHoverBackground : inactiveBackground);
        var foreground = isActive ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;

        button.Background = new SolidColorBrush(background);
        button.Foreground = new SolidColorBrush(foreground);
        button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(foreground);
        button.Resources["ButtonForegroundPressed"] = new SolidColorBrush(foreground);
        button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(isActive ? activeHoverBackground : inactiveHoverBackground);
        button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(isActive ? activePressedBackground : inactivePressedBackground);
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
                var targetWidth = Math.Max(120, args.NewSize.Width * 0.8);
                image.Width = targetWidth;
                image.MaxWidth = targetWidth;
            };
            var comma = value.IndexOf(',');
            if (comma > 0 && value.Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                try { _ = LoadRawImageBytesAsync(image, Convert.FromBase64String(value[(comma + 1)..])); } catch { }
            }
            container.Children.Add(image);
        }
        return WrapWithBorder(container);
    }

    private static async Task LoadRawImageBytesAsync(Image image, byte[] bytes)
    {
        var source = await CreateImageSourceFromBytesAsync(bytes, null);
        if (source is not null) image.Source = source;
    }

    private static Border CreateWebview(SitemapRowDescriptor row, Uri? baseUri)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, row.RawState ?? row.State, row.LabelColor, row.IconColor, useWindowsIcons: false, iconAuth: null);
        container.Children.Add(layout.Grid);

        var url = row.Url ?? row.RawItemState ?? row.RawState ?? row.State;
        if (string.IsNullOrWhiteSpace(url))
        {
            container.Children.Add(new TextBlock
            {
                Text = "No URL configured",
                Opacity = 0.4,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic
            });
            return WrapWithBorder(container);
        }

        if (!TryResolveWebviewUrl(url, baseUri, out var resolvedUri))
        {
            container.Children.Add(new TextBlock
            {
                Text = $"Invalid URL: {url}",
                Opacity = 0.6,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic
            });
            return WrapWithBorder(container);
        }

        // Reliable fallback: always show an "Open in browser" button
        var fallbackPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var openButton = new Button
        {
            Content = "Open in browser",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 36
        };
        openButton.Click += (_, _) => OpenUrlInBrowser(resolvedUri!);
        fallbackPanel.Children.Add(openButton);
        container.Children.Add(fallbackPanel);

        // Try WebView2 for inline display; WebView2 runtime may be missing
        var webview = new WebView2
        {
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        _ = InitializeWebViewAsync(webview, resolvedUri!);
        container.Children.Add(webview);

        return WrapWithBorder(container);
    }

    private static bool TryResolveWebviewUrl(string url, Uri? baseUri, out Uri? resolvedUri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            resolvedUri = absoluteUri;
            return true;
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, url, out var relativeUri))
        {
            resolvedUri = relativeUri;
            return true;
        }

        resolvedUri = null;
        return false;
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
                Text = "Chart requires an item",
                Opacity = 0.4,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic
            });
        }

        return WrapWithBorder(container);
    }

    /// <summary>Builds an openHAB chart image URL from the row descriptor and base URI.</summary>
    internal static Uri? BuildChartUrl(SitemapRowDescriptor row, Uri? baseUri, int chartDpi)
    {
        var itemName = row.ItemName ?? row.RawItemState ?? row.RawState ?? row.State;
        if (string.IsNullOrWhiteSpace(itemName) && string.IsNullOrWhiteSpace(row.Command))
        {
            return null;
        }

        if (baseUri is null)
        {
            return null;
        }

        var items = Uri.EscapeDataString(itemName ?? row.Command ?? string.Empty);
        var period = row.Period ?? "D";
        var random = Random.Shared.Next();

        var query = $"items={items}&period={Uri.EscapeDataString(period)}&dpi={chartDpi}&random={random}";
        return new Uri(baseUri, $"chart?{query}");
    }

    private static async Task LoadChartImageWithAuthAsync(Image image, Uri chartUrl, IconAuthContext? iconAuth)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, chartUrl);
            if (iconAuth is { } context)
            {
                ApplyAuthHeaders(request, context);
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

            var source = await CreateImageSourceFromBytesAsync(bytes, response.Content.Headers.ContentType?.MediaType);
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


