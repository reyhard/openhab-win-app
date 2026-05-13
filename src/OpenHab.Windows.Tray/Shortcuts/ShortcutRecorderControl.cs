using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Shortcuts;
using Windows.System;
using Windows.UI.Core;

namespace OpenHab.Windows.Tray.Shortcuts;

internal sealed class ShortcutRecorderControl : UserControl
{
    private readonly StackPanel chipsPanel;
    private readonly Button editButton;
    private readonly Button clearButton;
    private readonly TextBlock statusText;
    private readonly TextBlock errorText;
    private ShortcutBinding? binding;
    private bool allowClear;
    private bool isRecording;
    private string? error;

    public event EventHandler<ShortcutBinding?>? BindingChanged;

    public ShortcutBinding? Binding
    {
        get => binding;
        set
        {
            binding = value is null ? null : ShortcutBindingFormatter.Normalize(value);
            RefreshVisualState();
        }
    }

    public bool AllowClear
    {
        get => allowClear;
        set
        {
            allowClear = value;
            RefreshVisualState();
        }
    }

    public string? Error
    {
        get => error;
        set
        {
            error = value;
            RefreshVisualState();
        }
    }

    public ShortcutRecorderControl()
    {
        chipsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        editButton = new Button
        {
            Content = "Edit",
            MinWidth = 76
        };
        editButton.Click += EditButton_Click;

        clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 76
        };
        clearButton.Click += ClearButton_Click;

        statusText = new TextBlock
        {
            Opacity = 0.68,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Text = "Press Edit to record"
        };

        errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Visibility = Visibility.Collapsed
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(editButton);
        actions.Children.Add(clearButton);

        var row = new Grid
        {
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(chipsPanel);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        var root = new StackPanel
        {
            Spacing = 6
        };
        root.Children.Add(row);
        root.Children.Add(statusText);
        root.Children.Add(errorText);
        root.KeyDown += Root_KeyDown;
        root.IsTabStop = true;

        Content = root;
        RefreshVisualState();
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        isRecording = true;
        Error = null;
        _ = ((FrameworkElement)Content).Focus(FocusState.Programmatic);
        RefreshVisualState();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowClear || isRecording)
        {
            return;
        }

        binding = null;
        Error = null;
        BindingChanged?.Invoke(this, binding);
        RefreshVisualState();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!isRecording)
        {
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            isRecording = false;
            Error = null;
            RefreshVisualState();
            e.Handled = true;
            return;
        }

        if (IsModifierKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        var modifiers = GetActiveModifiers();
        if (modifiers.Count == 0)
        {
            Error = "Use at least one modifier key.";
            e.Handled = true;
            return;
        }

        var keyText = FormatKey(e.Key);
        if (string.IsNullOrWhiteSpace(keyText))
        {
            Error = "Unsupported key.";
            e.Handled = true;
            return;
        }

        binding = ShortcutBindingFormatter.Normalize(new ShortcutBinding(modifiers.ToImmutableArray(), keyText));
        isRecording = false;
        Error = null;
        BindingChanged?.Invoke(this, binding);
        RefreshVisualState();
        e.Handled = true;
    }

    private void RefreshVisualState()
    {
        chipsPanel.Children.Clear();

        if (ShortcutBindingFormatter.TryNormalize(binding, out var normalized))
        {
            foreach (var part in ShortcutBindingFormatter.Format(normalized).Split(" + "))
            {
                chipsPanel.Children.Add(CreateChip(part));
            }
        }
        else
        {
            chipsPanel.Children.Add(CreateChip("Unassigned"));
        }

        editButton.Content = isRecording ? "Press shortcut..." : "Edit";
        editButton.IsEnabled = true;
        clearButton.Visibility = allowClear ? Visibility.Visible : Visibility.Collapsed;
        clearButton.IsEnabled = !isRecording && binding is not null;
        statusText.Text = isRecording ? "Press modifiers + key. Press Esc to cancel." : "Press Edit to record";
        errorText.Text = error ?? string.Empty;
        errorText.Visibility = string.IsNullOrWhiteSpace(error) ? Visibility.Collapsed : Visibility.Visible;
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

    private static List<ShortcutModifier> GetActiveModifiers()
    {
        var result = new List<ShortcutModifier>(4);

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            result.Add(ShortcutModifier.Win);
        }
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.LeftControl) || IsDown(VirtualKey.RightControl))
        {
            result.Add(ShortcutModifier.Ctrl);
        }
        if (IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftMenu) || IsDown(VirtualKey.RightMenu))
        {
            result.Add(ShortcutModifier.Alt);
        }
        if (IsDown(VirtualKey.Shift) || IsDown(VirtualKey.LeftShift) || IsDown(VirtualKey.RightShift))
        {
            result.Add(ShortcutModifier.Shift);
        }

        return result;
    }

    private static bool IsDown(VirtualKey key)
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }

    private static bool IsModifierKey(VirtualKey key)
    {
        return key is VirtualKey.LeftWindows
            or VirtualKey.RightWindows
            or VirtualKey.Control
            or VirtualKey.LeftControl
            or VirtualKey.RightControl
            or VirtualKey.Menu
            or VirtualKey.LeftMenu
            or VirtualKey.RightMenu
            or VirtualKey.Shift
            or VirtualKey.LeftShift
            or VirtualKey.RightShift;
    }

    private static string FormatKey(VirtualKey key)
    {
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            return key.ToString();
        }

        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            return ((int)key - (int)VirtualKey.Number0).ToString();
        }

        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Tab => "Tab",
            VirtualKey.Enter => "Enter",
            VirtualKey.Back => "Backspace",
            VirtualKey.Delete => "Delete",
            VirtualKey.Insert => "Insert",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            _ => key.ToString()
        };
    }
}
