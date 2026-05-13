using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenHab.Windows.Tray.Shortcuts;

internal static class ShortcutSettingsControls
{
    public static Border CreateSettingsCard(params FrameworkElement[] rows)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        for (var index = 0; index < rows.Length; index++)
        {
            stack.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = index == rows.Length - 1 ? new Thickness(0) : new Thickness(0, 0, 0, 1),
                Child = rows[index]
            });
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    public static StackPanel CreateShortcutChips(IEnumerable<string> shortcuts)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var shortcut in shortcuts)
        {
            panel.Children.Add(CreateChip(shortcut));
        }

        return panel;
    }

    private static Border CreateChip(string text)
    {
        return new Border
        {
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = text,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
            }
        };
    }
}
