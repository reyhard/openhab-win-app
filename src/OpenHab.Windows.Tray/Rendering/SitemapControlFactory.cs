using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering;

public static class SitemapControlFactory
{
    public static FrameworkElement Create(SitemapRowDescriptor row, Func<Task>? activateRow)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row, activateRow),
            RenderControlKind.Slider => CreateSlider(row, activateRow),
            RenderControlKind.Selection => CreateSelection(row, activateRow),
            RenderControlKind.Fallback => CreateFallback(row),
            _ => CreateText(row)
        };
    }

    private static FrameworkElement CreateText(SitemapRowDescriptor row)
    {
        return CreateRow(row.Label, row.State ?? string.Empty);
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

    private static FrameworkElement CreateSlider(SitemapRowDescriptor row, Func<Task>? activateRow)
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
                new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = value,
                    IsEnabled = row.Action == RenderActionKind.SendCommand && activateRow is not null
                }
            }
        };

        if (control.Children[1] is Slider slider && row.Action == RenderActionKind.SendCommand && activateRow is not null)
        {
            slider.PointerCaptureLost += async (_, _) => await activateRow();
        }

        return control;
     }

    private static FrameworkElement CreateSelection(SitemapRowDescriptor row, Func<Task>? activateRow)
    {
        var button = new Button
        {
            Content = CreateButtonTextBlock(string.IsNullOrWhiteSpace(row.State) ? row.Label : $"{row.Label}: {row.State}"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = row.Action == RenderActionKind.SendCommand && activateRow is not null
        };

        if (row.Action == RenderActionKind.SendCommand && activateRow is not null)
        {
            button.Click += async (_, _) => await activateRow();
        }

        return button;
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
