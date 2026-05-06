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
        ["light"] = "\uE706",
        ["switch"] = "\uE8A3",
        ["rollershutter"] = "\uE7A0",
        ["heating"] = "\uE7B2",
        ["temperature"] = "\uE7B2",
        ["contact"] = "\uE8E1",
        ["motion"] = "\uE7A6",
        ["alarm"] = "\uE7BA",
        ["battery"] = "\uEBA0",
        ["energy"] = "\uE994",
        ["power"] = "\uE994",
        ["lock"] = "\uE72E",
        ["door"] = "\uE8E1",
        ["window"] = "\uE8E1",
        ["garagedoor"] = "\uE8E1",
        ["blinds"] = "\uE7A0",
        ["dimmer"] = "\uE706",
        ["colorpicker"] = "\uE790",
        ["speaker"] = "\uE7F5",
        ["tv"] = "\uE7F4",
        ["network"] = "\uE701",
        ["presence"] = "\uE716",
        ["smoke"] = "\uE7BA",
        ["camera"] = "\uE722",
        ["fan"] = "\uE785",
        ["water"] = "\uE7A6",
        ["quality"] = "\uE769",
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

        var toggle = new ToggleSwitch
        {
            IsOn = string.Equals(row.State, "ON", StringComparison.OrdinalIgnoreCase)
        };
        Grid.SetColumn(toggle, 1);
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
                        Source = new BitmapImage(new Uri(baseUri, $"icon/{Uri.EscapeDataString(iconName!)}.png")),
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
                    Source = new BitmapImage(new Uri(baseUri, $"icon/{Uri.EscapeDataString(iconName!)}.png")),
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
