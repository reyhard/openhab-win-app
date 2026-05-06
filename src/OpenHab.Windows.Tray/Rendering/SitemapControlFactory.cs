using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering;

public static class SitemapControlFactory
{
    public static FrameworkElement Create(SitemapRowDescriptor row, Func<Task>? activateRow, Func<string, Task>? sendCommand = null, Uri? baseUri = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row, activateRow),
            RenderControlKind.Slider => CreateSlider(row, activateRow, sendCommand),
            RenderControlKind.Selection => CreateSelection(row, activateRow, sendCommand),
            RenderControlKind.Fallback => CreateFallback(row),
            _ => CreateText(row, activateRow, baseUri)
        };
    }

    private static FrameworkElement CreateText(SitemapRowDescriptor row, Func<Task>? activateRow = null, Uri? baseUri = null)
    {
        var grid = CreateRow(row.Label, row.State ?? string.Empty, baseUri, row.IconName);

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
        var toggle = new ToggleSwitch
        {
            Header = row.Label,
            IsOn = string.Equals(row.State, "ON", StringComparison.OrdinalIgnoreCase)
        };

        if (row.Action == RenderActionKind.SendCommand && activateRow is not null)
        {
            toggle.Toggled += async (_, _) => await activateRow();
        }

        return toggle;
    }

    private static FrameworkElement CreateSlider(SitemapRowDescriptor row, Func<Task>? activateRow, Func<string, Task>? sendCommand)
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

    private static FrameworkElement CreateSelection(SitemapRowDescriptor row, Func<Task>? activateRow, Func<string, Task>? sendCommand)
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
            comboBox.SelectionChanged += (_, _) =>
            {
                if (comboBox.SelectedItem is ComboBoxItem { Tag: string cmd })
                    _ = sendCommand(cmd);
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

    private static Grid CreateRow(string label, string state, Uri? baseUri = null, string? iconName = null)
    {
        var hasIcon = iconName is not null && baseUri is not null;
        var labelCol = hasIcon ? 1 : 0;
        var stateCol = hasIcon ? 2 : 1;

        var grid = new Grid();

        if (hasIcon)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (hasIcon)
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(baseUri!, $"icon/{Uri.EscapeDataString(iconName!)}.png")),
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(image, 0);
            grid.Children.Add(image);
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
