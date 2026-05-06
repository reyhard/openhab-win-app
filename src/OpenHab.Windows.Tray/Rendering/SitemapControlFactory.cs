using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering;

public static class SitemapControlFactory
{
    private static readonly Dictionary<string, string> Win11IconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["light"] = "\uE706", ["lights"] = "\uE706",
        ["switch"] = "\uE8A3",
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

    private static FontIcon? ResolveWin11Icon(string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName)) return null;
        if (Win11IconMap.TryGetValue(iconName, out var glyph))
            return new FontIcon { Glyph = glyph, FontSize = 16, Opacity = 0.8 };
        return null;
    }

    public static FrameworkElement Create(SitemapRowDescriptor row, Func<Task>? activateRow, Func<string, Task>? sendCommand = null, Uri? baseUri = null, bool useWindowsIcons = false)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row, activateRow),
            RenderControlKind.Slider => CreateSlider(row, sendCommand),
            RenderControlKind.Selection => CreateSelection(row, sendCommand),
            RenderControlKind.Fallback => CreateFallback(row),
            _ => CreateText(row, activateRow, baseUri, useWindowsIcons)
        };
    }

    private static FrameworkElement CreateText(SitemapRowDescriptor row, Func<Task>? activateRow = null, Uri? baseUri = null, bool useWindowsIcons = false)
    {
        var grid = CreateRow(row.Label, row.State ?? string.Empty, baseUri, row.IconName, useWindowsIcons);

        if (activateRow is not null && row.Action == RenderActionKind.Navigate)
        {
            var hasIcon = row.IconName is not null && baseUri is not null;
            var chevronCol = hasIcon ? 3 : 2;
            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6
            };
            Grid.SetColumn(chevron, chevronCol);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(chevron);

            var button = new Button
            {
                Content = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0)
            };
            button.Click += async (_, _) => await activateRow();
            return button;
        }

        return grid;
    }

    private static FrameworkElement CreateToggle(SitemapRowDescriptor row, Func<Task>? activateRow)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = row.Label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        };
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var stateBlock = new TextBlock
        {
            Text = row.State ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Opacity = 0.7,
            FontSize = 13
        };
        Grid.SetColumn(stateBlock, 1);
        grid.Children.Add(stateBlock);

        var toggle = new ToggleSwitch
        {
            IsOn = string.Equals(row.State, "ON", StringComparison.OrdinalIgnoreCase)
        };
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);

        if (row.Action == RenderActionKind.SendCommand && activateRow is not null)
        {
            toggle.Toggled += async (_, _) => await activateRow();
        }

        return grid;
    }

    private static FrameworkElement CreateSlider(SitemapRowDescriptor row, Func<string, Task>? sendCommand)
    {
        var value = double.TryParse(row.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 100)
            : 0;

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = value
        };

        if (sendCommand is not null)
        {
            slider.ValueChanged += async (_, args) =>
            {
                var newValue = args.NewValue.ToString("F0", CultureInfo.InvariantCulture);
                await sendCommand(newValue);
            };
        }

        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = row.Label,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 2
                },
                slider
            }
        };
    }

    private static FrameworkElement CreateSelection(SitemapRowDescriptor row, Func<string, Task>? sendCommand)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = row.Label,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        });

        var comboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
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

        panel.Children.Add(comboBox);
        return panel;
    }

    private static FrameworkElement CreateFallback(SitemapRowDescriptor row)
    {
        return new Button
        {
            Content = CreateButtonTextBlock(row.Label),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false
        };
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

    private static Grid CreateRow(string label, string state, Uri? baseUri = null, string? iconName = null, bool useWindowsIcons = false)
    {
        var hasIcon = iconName is not null && (baseUri is not null || useWindowsIcons);
        var labelCol = hasIcon ? 1 : 0;
        var stateCol = hasIcon ? 2 : 1;

        var grid = new Grid();

        if (hasIcon)
        {
            grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(24) });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (hasIcon)
        {
            if (useWindowsIcons)
            {
                var winIcon = ResolveWin11Icon(iconName);
                if (winIcon is not null)
                {
                    winIcon.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(winIcon, 0);
                    grid.Children.Add(winIcon);
                }
                else if (baseUri is not null)
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(baseUri, $"icon/{Uri.EscapeDataString(iconName!)}")),
                        Width = 20,
                        Height = 20,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(image, 0);
                    grid.Children.Add(image);
                }
            }
            else if (baseUri is not null)
            {
                var image = new Image
                {
                    Source = new BitmapImage(new Uri(baseUri, $"icon/{Uri.EscapeDataString(iconName!)}")),
                    Width = 20,
                    Height = 20,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(image, 0);
                grid.Children.Add(image);
            }
        }

        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        };
        Grid.SetColumn(labelBlock, labelCol);
        grid.Children.Add(labelBlock);

        var stateText = new TextBlock
        {
            Text = state,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };
        Grid.SetColumn(stateText, stateCol);
        grid.Children.Add(stateText);

        return grid;
    }
}
