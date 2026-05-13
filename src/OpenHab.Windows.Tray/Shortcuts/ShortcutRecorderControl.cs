using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Shortcuts;
using Windows.System;
using Windows.UI.Core;

namespace OpenHab.Windows.Tray.Shortcuts;

internal sealed class ShortcutRecorderControl : UserControl
{
    private static readonly VirtualKey[] PollableShortcutKeys =
    [
        VirtualKey.A,
        VirtualKey.B,
        VirtualKey.C,
        VirtualKey.D,
        VirtualKey.E,
        VirtualKey.F,
        VirtualKey.G,
        VirtualKey.H,
        VirtualKey.I,
        VirtualKey.J,
        VirtualKey.K,
        VirtualKey.L,
        VirtualKey.M,
        VirtualKey.N,
        VirtualKey.O,
        VirtualKey.P,
        VirtualKey.Q,
        VirtualKey.R,
        VirtualKey.S,
        VirtualKey.T,
        VirtualKey.U,
        VirtualKey.V,
        VirtualKey.W,
        VirtualKey.X,
        VirtualKey.Y,
        VirtualKey.Z,
        VirtualKey.Number0,
        VirtualKey.Number1,
        VirtualKey.Number2,
        VirtualKey.Number3,
        VirtualKey.Number4,
        VirtualKey.Number5,
        VirtualKey.Number6,
        VirtualKey.Number7,
        VirtualKey.Number8,
        VirtualKey.Number9,
        VirtualKey.F1,
        VirtualKey.F2,
        VirtualKey.F3,
        VirtualKey.F4,
        VirtualKey.F5,
        VirtualKey.F6,
        VirtualKey.F7,
        VirtualKey.F8,
        VirtualKey.F9,
        VirtualKey.F10,
        VirtualKey.F11,
        VirtualKey.F12,
        VirtualKey.F13,
        VirtualKey.F14,
        VirtualKey.F15,
        VirtualKey.F16,
        VirtualKey.F17,
        VirtualKey.F18,
        VirtualKey.F19,
        VirtualKey.F20,
        VirtualKey.F21,
        VirtualKey.F22,
        VirtualKey.F23,
        VirtualKey.F24,
        VirtualKey.Space,
        VirtualKey.Back,
        VirtualKey.Delete,
        VirtualKey.Insert,
        VirtualKey.Up,
        VirtualKey.Down,
        VirtualKey.Left,
        VirtualKey.Right,
        VirtualKey.PageUp,
        VirtualKey.PageDown,
        VirtualKey.Home,
        VirtualKey.End,
        VirtualKey.Enter,
        VirtualKey.Tab
    ];

    private readonly StackPanel chipsPanel;
    private readonly Button editButton;
    private readonly Button clearButton;
    private readonly TextBlock errorText;
    private ShortcutBinding? binding;
    private bool allowClear;
    private bool isRecording;
    private string? error;
    private static int activeRecorderCount;

    public event EventHandler<ShortcutBinding?>? BindingChanged;
    public static event EventHandler<bool>? AnyRecordingChanged;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

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
            MinWidth = 76,
            UseSystemFocusVisuals = true
        };
        editButton.Click += EditButton_Click;

        clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 76,
            UseSystemFocusVisuals = true
        };
        clearButton.Click += ClearButton_Click;

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
        root.Children.Add(errorText);

        Content = root;
        UseSystemFocusVisuals = true;
        Unloaded += ShortcutRecorderControl_Unloaded;
        RefreshVisualState();
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowRecorderDialogAsync();
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

    private async Task ShowRecorderDialogAsync()
    {
        StartRecording();
        Error = null;
        RefreshVisualState();

        var initialBinding = binding is null ? null : ShortcutBindingFormatter.Normalize(binding);
        var capturedBinding = initialBinding;
        var previewPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            MinHeight = 78
        };
        var dialogStatus = new TextBlock
        {
            Text = "Press a modifier and key.",
            TextAlignment = TextAlignment.Center,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        };
        var dialogError = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var captureSurface = new Border
        {
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(18),
            Child = previewPanel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var resetButton = new Button
        {
            Content = "Reset",
            MinWidth = 86
        };
        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 86
        };

        var helperButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Children =
            {
                resetButton,
                clearButton
            }
        };

        var content = new StackPanel
        {
            Spacing = 14,
            MinWidth = 420,
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            Children =
            {
                new TextBlock
                {
                    Text = "Shortcut must start with Windows, Ctrl, Alt, or Shift.",
                    TextWrapping = TextWrapping.Wrap
                },
                captureSurface,
                helperButtons,
                dialogStatus,
                dialogError
            }
        };
        content.Loaded += (_, _) => _ = content.Focus(FocusState.Programmatic);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Activation shortcut",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        var pollingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };

        void RenderPreview()
        {
            previewPanel.Children.Clear();
            if (ShortcutBindingFormatter.TryNormalize(capturedBinding, out var normalized))
            {
                foreach (var part in ShortcutBindingFormatter.Format(normalized).Split(" + "))
                {
                    previewPanel.Children.Add(CreatePreviewChip(part));
                }
            }
            else
            {
                previewPanel.Children.Add(CreatePreviewChip("Press shortcut"));
            }

            dialog.IsPrimaryButtonEnabled = capturedBinding is not null || AllowClear;
        }

        void SetDialogError(string? message)
        {
            dialogError.Text = message ?? string.Empty;
            dialogError.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        void CaptureBinding(IReadOnlyCollection<ShortcutModifier> modifiers, VirtualKey key)
        {
            var keyText = FormatKey(key);
            if (string.IsNullOrWhiteSpace(keyText))
            {
                SetDialogError("Unsupported key.");
                return;
            }

            capturedBinding = ShortcutBindingFormatter.Normalize(new ShortcutBinding(modifiers.ToImmutableArray(), keyText));
            dialogStatus.Text = ShortcutBindingFormatter.Format(capturedBinding);
            SetDialogError(null);
            RenderPreview();
        }

        content.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key == VirtualKey.Escape)
            {
                return;
            }

            if (IsModifierKey(args.Key))
            {
                SetDialogError(null);
                args.Handled = true;
                return;
            }

            var modifiers = GetActiveModifiers();
            if (modifiers.Count == 0)
            {
                SetDialogError("Use at least one modifier key.");
                args.Handled = true;
                return;
            }

            CaptureBinding(modifiers, args.Key);
            args.Handled = true;
        }), handledEventsToo: true);

        pollingTimer.Tick += (_, _) =>
        {
            var modifiers = GetActiveModifiers();
            if (modifiers.Count == 0)
            {
                return;
            }

            foreach (var key in PollableShortcutKeys)
            {
                if (IsDown(key))
                {
                    CaptureBinding(modifiers, key);
                    return;
                }
            }
        };

        resetButton.Click += (_, _) =>
        {
            capturedBinding = initialBinding;
            dialogStatus.Text = "Reset to current shortcut.";
            SetDialogError(null);
            RenderPreview();
        };
        clearButton.Click += (_, _) =>
        {
            capturedBinding = null;
            dialogStatus.Text = AllowClear ? "Shortcut cleared." : "Shortcut is required for this control.";
            SetDialogError(AllowClear ? null : "Shortcut is required.");
            RenderPreview();
        };

        try
        {
            RenderPreview();
            pollingTimer.Start();
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                binding = capturedBinding is null ? null : ShortcutBindingFormatter.Normalize(capturedBinding);
                Error = null;
                BindingChanged?.Invoke(this, binding);
            }
        }
        finally
        {
            pollingTimer.Stop();
            StopRecording();
            RefreshVisualState();
        }
    }

    private void ShortcutRecorderControl_Unloaded(object sender, RoutedEventArgs e)
    {
        StopRecording();
    }

    private void StartRecording()
    {
        if (isRecording)
        {
            return;
        }

        isRecording = true;
        if (Interlocked.Increment(ref activeRecorderCount) == 1)
        {
            AnyRecordingChanged?.Invoke(null, true);
        }
    }

    private void StopRecording()
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;
        var remaining = Interlocked.Decrement(ref activeRecorderCount);
        if (remaining <= 0)
        {
            if (remaining < 0)
            {
                Interlocked.Exchange(ref activeRecorderCount, 0);
            }

            AnyRecordingChanged?.Invoke(null, false);
        }
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
        AutomationProperties.SetName(editButton, "Edit shortcut");
        clearButton.Visibility = allowClear ? Visibility.Visible : Visibility.Collapsed;
        clearButton.IsEnabled = !isRecording && binding is not null;
        AutomationProperties.SetName(clearButton, "Clear shortcut");
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
        return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down)
            || (GetAsyncKeyState((int)key) & 0x8000) != 0;
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

        if (key >= VirtualKey.F1 && key <= VirtualKey.F24)
        {
            return key.ToString();
        }

        return key switch
        {
            VirtualKey.Space => "Space",
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
            VirtualKey.Enter => "Enter",
            VirtualKey.Tab => "Tab",
            _ => string.Empty
        };
    }

    private static Border CreatePreviewChip(string text)
    {
        var foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        return new Border
        {
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(6),
            MinWidth = 58,
            Height = 48,
            Padding = new Thickness(14, 0, 14, 0),
            Child = text.Equals("Win", StringComparison.Ordinal)
                ? new FontIcon
                {
                    Glyph = "\uE782",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 22,
                    Foreground = foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
                : new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };
    }
}
