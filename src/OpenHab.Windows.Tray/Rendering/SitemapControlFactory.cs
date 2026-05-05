using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering;

public static class SitemapControlFactory
{
    public static FrameworkElement Create(SitemapRowDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row),
            RenderControlKind.Slider => CreateSlider(row),
            RenderControlKind.Selection => CreateSelection(row),
            RenderControlKind.Fallback => CreateFallback(row),
            _ => CreateText(row)
        };
    }

    private static FrameworkElement CreateText(SitemapRowDescriptor row)
    {
        return CreateRow(row.Label, row.State ?? string.Empty);
    }

    private static FrameworkElement CreateToggle(SitemapRowDescriptor row)
    {
        return new ToggleSwitch
        {
            Header = row.Label,
            IsOn = string.Equals(row.State, "ON", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static FrameworkElement CreateSlider(SitemapRowDescriptor row)
    {
        var value = double.TryParse(row.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 100)
            : 0;

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
                new Slider { Minimum = 0, Maximum = 100, Value = value }
            }
        };
    }

    private static FrameworkElement CreateSelection(SitemapRowDescriptor row)
    {
        return new Button
        {
            Content = string.IsNullOrWhiteSpace(row.State) ? row.Label : $"{row.Label}: {row.State}",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static FrameworkElement CreateFallback(SitemapRowDescriptor row)
    {
        return new Button
        {
            Content = row.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false
        };
    }

    private static FrameworkElement CreateRow(string label, string state)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        });

        var stateText = new TextBlock
        {
            Text = state,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };
        Grid.SetColumn(stateText, 1);
        grid.Children.Add(stateText);

        return grid;
    }
}
