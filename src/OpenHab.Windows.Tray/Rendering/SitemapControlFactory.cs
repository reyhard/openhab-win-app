using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering;

public static class SitemapControlFactory
{
    private const double ValueLaneWidth = 96;
    private const double ControlLaneWidth = 48;

    private static readonly Dictionary<string, string> Win11IconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["light"] = "\uE706", ["lights"] = "\uE706",
        ["lighton"] = "\uE706", ["lightoff"] = "\uE706",
        ["lightson"] = "\uE706", ["lightsoff"] = "\uE706",
        ["switch"] = "\uE7E8",
        ["switchon"] = "\uE7E8", ["switchoff"] = "\uE7E8",
        ["poweron"] = "\uE7E8", ["poweroff"] = "\uE7E8",
        ["rollershutter"] = "\uE7A0", ["blinds"] = "\uE7A0",
        ["heating"] = "\uE7B2", ["temperature"] = "\uE7B2", ["temp"] = "\uE7B2",
        ["humidity"] = "\uE7A6", ["moisture"] = "\uE7A6",
        ["contact"] = "\uE8E1", ["door"] = "\uE8E1", ["window"] = "\uE8E1", ["garagedoor"] = "\uE8E1",
        ["motion"] = "\uE7A6", ["presence"] = "\uE716", ["occupancy"] = "\uE716",
        ["alarm"] = "\uE7BA", ["smoke"] = "\uE7BA", ["siren"] = "\uE995",
        ["battery"] = "\uEBA0", ["batterylevel"] = "\uEBA0",
        ["energy"] = "\uE994", ["power"] = "\uE994",
        ["lock"] = "\uE72E",
        ["dimmer"] = "\uE706",
        ["colorpicker"] = "\uE790", ["color"] = "\uE790",
        ["speaker"] = "\uE7F5", ["audio"] = "\uE7F5", ["receiver"] = "\uE7F5",
        ["tv"] = "\uE7F4", ["screen"] = "\uE7F4",
        ["network"] = "\uE701", ["wifi"] = "\uE701",
        ["camera"] = "\uE722",
        ["fan"] = "\uE785", ["pump"] = "\uE785",
        ["water"] = "\uE7A6", ["gas"] = "\uE7A6",
        ["quality"] = "\uE769", ["co2"] = "\uE769", ["airquality"] = "\uE769",
        ["chart"] = "\uE9D2", ["number"] = "\uE9D2",
        ["text"] = "\uE8A5", ["string"] = "\uE8A5", ["group"] = "\uE902",
        ["none"] = "\uE776",
        ["settings"] = "\uE713", ["setup"] = "\uE713",
        ["sun"] = "\uE706", ["sunrise"] = "\uE706", ["sunset"] = "\uE706",
        ["moon"] = "\uE708",
        ["cloud"] = "\uE753", ["weather"] = "\uE753",
        ["rain"] = "\uE7A6", ["wind"] = "\uE7A6", ["snow"] = "\uE7A6",
        ["pressure"] = "\uE976",
        ["groundfloor"] = "\uE831", ["ground_floor"] = "\uE831",
        ["firstfloor"] = "\uE831", ["first_floor"] = "\uE831",
        ["floorplan"] = "\uE831",
        ["kitchen"] = "\uE7A7", ["bath"] = "\uE7A8", ["bathroom"] = "\uE7A8",
        ["bedroom"] = "\uE7A9", ["living"] = "\uE7AA", ["office"] = "\uE7AB",
        ["garage"] = "\uE83D", ["garden"] = "\uE7A5", ["terrace"] = "\uE7A5",
        ["attic"] = "\uE831", ["cellar"] = "\uE831", ["basement"] = "\uE831",
        ["time"] = "\uE787", ["datetime"] = "\uE787", ["date"] = "\uE787",
        ["location"] = "\uE707",
        ["player"] = "\uE768", ["music"] = "\uE768",
        ["image"] = "\uE722", ["video"] = "\uE722",
        ["outlet"] = "\uE994", ["plug"] = "\uE994",
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

    public static FrameworkElement Create(SitemapRowDescriptor row, Func<Task>? activateRow, Func<string, Task>? sendCommand = null, Uri? baseUri = null, bool useWindowsIcons = false)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row, activateRow, baseUri, useWindowsIcons),
            RenderControlKind.Slider => CreateSlider(row, sendCommand, baseUri, useWindowsIcons),
            RenderControlKind.Selection => CreateSelection(row, sendCommand, baseUri, useWindowsIcons),
            RenderControlKind.Fallback => CreateFallback(row),
            _ => CreateText(row, activateRow, baseUri, useWindowsIcons)
        };
    }

    private static bool TryAddIcon(Grid grid, int column, string? iconName, Uri? baseUri, bool useWindowsIcons)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return false;

        if (useWindowsIcons)
        {
            var winIcon = ResolveWin11Icon(iconName);
            if (winIcon is not null)
            {
                winIcon.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(winIcon, column);
                grid.Children.Add(winIcon);
                return true;
            }
        }

        if (baseUri is not null)
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(baseUri, $"icon/{Uri.EscapeDataString(iconName)}")),
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(image, column);
            grid.Children.Add(image);
            return true;
        }

        return false;
    }

    private static bool CanDisplayIcon(string? iconName, Uri? baseUri, bool useWindowsIcons)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return false;
        return baseUri is not null || (useWindowsIcons && ResolveGlyphForIcon(iconName) is not null);
    }

    private static RowLayout CreateRowLayout(string label, Uri? baseUri, string? iconName, bool useWindowsIcons)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        var hasIcon = CanDisplayIcon(iconName, baseUri, useWindowsIcons);

        if (hasIcon)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            TryAddIcon(grid, 0, iconName, baseUri, useWindowsIcons);
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
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        };
        Grid.SetColumn(labelBlock, labelColumn);
        grid.Children.Add(labelBlock);

        return new RowLayout(grid, labelColumn, valueColumn, controlColumn);
    }

    private static TextBlock CreateStateTextBlock(string state)
    {
        return new TextBlock
        {
            Text = state,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    private static FrameworkElement CreateText(SitemapRowDescriptor row, Func<Task>? activateRow = null, Uri? baseUri = null, bool useWindowsIcons = false)
    {
        if (IsSectionHeader(row))
        {
            return CreateSectionHeader(row.Label);
        }

        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, useWindowsIcons);
        var grid = layout.Grid;
        var navigateAction = row.Action == RenderActionKind.Navigate ? activateRow : null;
        var isNavigate = navigateAction is not null;

        var stateText = CreateStateTextBlock(row.State ?? string.Empty);
        Grid.SetColumn(stateText, layout.ValueColumn);
        Grid.SetColumnSpan(stateText, isNavigate ? 1 : 2);
        grid.Children.Add(stateText);

        if (navigateAction is not null)
        {
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
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0)
            };
            button.Click += async (_, _) => await navigate();
            return WrapWithBorder(button);
        }

        return WrapWithBorder(grid);
    }

    private static bool IsSectionHeader(SitemapRowDescriptor row)
    {
        return row.Control == RenderControlKind.Text
            && row.Action == RenderActionKind.None
            && string.IsNullOrWhiteSpace(row.State)
            && string.IsNullOrWhiteSpace(row.IconName);
    }

    private static FrameworkElement CreateSectionHeader(string label)
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

    private static FrameworkElement CreateToggle(SitemapRowDescriptor row, Func<Task>? activateRow, Uri? baseUri = null, bool useWindowsIcons = false)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, useWindowsIcons);
        var grid = layout.Grid;
        var rawState = row.RawState ?? row.State;

        var stateBlock = CreateStateTextBlock(
            string.Equals(rawState, "ON", StringComparison.OrdinalIgnoreCase) ? "ON" : "OFF");
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
            toggle.Toggled += async (_, _) => await activateRow();
        }

        return WrapWithBorder(grid);
    }

    private static FrameworkElement CreateSlider(SitemapRowDescriptor row, Func<string, Task>? sendCommand, Uri? baseUri = null, bool useWindowsIcons = false)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, useWindowsIcons);
        var grid = layout.Grid;

        var value = double.TryParse(row.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 100)
            : 0;

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (sendCommand is not null)
        {
            slider.ValueChanged += async (_, args) =>
            {
                var newValue = args.NewValue.ToString("F0", CultureInfo.InvariantCulture);
                await sendCommand(newValue);
            };
        }

        Grid.SetColumn(slider, layout.ValueColumn);
        Grid.SetColumnSpan(slider, 2);
        grid.Children.Add(slider);

        return WrapWithBorder(grid);
    }

    private static FrameworkElement CreateSelection(SitemapRowDescriptor row, Func<string, Task>? sendCommand, Uri? baseUri = null, bool useWindowsIcons = false)
    {
        var layout = CreateRowLayout(row.Label, baseUri, row.IconName, useWindowsIcons);
        var grid = layout.Grid;

        var comboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var option in row.SelectionOptions)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Command });
            if (string.Equals(option.Command, row.RawState, StringComparison.OrdinalIgnoreCase))
                comboBox.SelectedIndex = comboBox.Items.Count - 1;
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

    private static FrameworkElement CreateFallback(SitemapRowDescriptor row)
    {
        return WrapWithBorder(new Button
        {
            Content = CreateButtonTextBlock(row.Label),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false,
            BorderThickness = new Thickness(0)
        });
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
