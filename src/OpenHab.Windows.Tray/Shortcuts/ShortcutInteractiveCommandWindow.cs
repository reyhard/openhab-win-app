using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Shortcuts;
using OpenHab.Core.Api;
using OpenHab.Rendering;
using Windows.Graphics;
using Windows.UI;

namespace OpenHab.Windows.Tray.Shortcuts;

[ExcludeFromCodeCoverage(Justification = "WinUI command popup host glue.")]
[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
    Justification = "LibraryImport source generation currently fails the Windows App SDK XAML compile path and does not support the PointInt32 out parameter here.")]
public sealed partial class ShortcutInteractiveCommandWindow : Window
{
    private const int SliderWidth = 300;
    private const int SliderHeight = 156;
    private const int ColorWidth = 360;
    private const int ColorHeight = 500;
    private const int SendDebounceMs = 200;

    private readonly ShortcutAction action;
    private readonly IOpenHabClient client;
    private readonly TextBlock statusText = new();
    private CancellationTokenSource? sendDebounceCts;
    private string? lastSentCommand;
    private bool isClosing;

    public ShortcutInteractiveCommandWindow(ShortcutAction action, IOpenHabClient client)
    {
        this.action = action ?? throw new ArgumentNullException(nameof(action));
        this.client = client ?? throw new ArgumentNullException(nameof(client));

        Title = action.Name;
        Content = CreateLoadingContent();
        ConfigureWindow();
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            isClosing = true;
            sendDebounceCts?.Cancel();
            sendDebounceCts?.Dispose();
            sendDebounceCts = null;
        };
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var rawState = await client.GetItemStateAsync(action.TargetItem, cancellationToken).ConfigureAwait(true);
        Content = action.CommandType switch
        {
            ShortcutCommandType.OpenSlider => CreateSliderContent(rawState),
            ShortcutCommandType.OpenColorPicker => CreateColorContent(rawState),
            _ => CreateMessageContent("Unsupported command surface.")
        };
    }

    public void ShowNearCursor()
    {
        Activate();
        PositionNearCursor();
    }

    private void ConfigureWindow()
    {
        var size = action.CommandType == ShortcutCommandType.OpenColorPicker
            ? new SizeInt32(ColorWidth, ColorHeight)
            : new SizeInt32(SliderWidth, SliderHeight);
        AppWindow.Resize(size);
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
    }

    private Border CreateLoadingContent()
    {
        return CreateRoot(new ProgressRing
        {
            IsActive = true,
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center
        });
    }

    private Border CreateMessageContent(string message)
    {
        return CreateRoot(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private Border CreateSliderContent(string? rawState)
    {
        var state = ShortcutInteractiveCommandLogic.CreateSliderState(rawState);
        statusText.Text = ShortcutInteractiveCommandLogic.FormatSliderCommand(state.InitialValue);

        var slider = new Slider
        {
            Minimum = state.Minimum,
            Maximum = state.Maximum,
            StepFrequency = 1,
            SmallChange = 1,
            Value = state.InitialValue,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        slider.ValueChanged += (_, args) =>
        {
            var command = ShortcutInteractiveCommandLogic.FormatSliderCommand(args.NewValue);
            statusText.Text = command;
            QueueSend(command);
        };
        slider.PointerReleased += (_, _) => FlushCurrentSliderValue(slider);
        slider.PointerCaptureLost += (_, _) => FlushCurrentSliderValue(slider);
        slider.KeyUp += (_, _) => FlushCurrentSliderValue(slider);
        slider.LostFocus += (_, _) => FlushCurrentSliderValue(slider);

        var panel = CreatePanel();
        panel.Children.Add(CreateHeader());
        panel.Children.Add(statusText);
        panel.Children.Add(slider);
        return CreateRoot(panel);
    }

    private Border CreateColorContent(string? rawState)
    {
        var state = ShortcutInteractiveCommandLogic.CreateColorState(rawState);
        var selectedColor = ToWindowsColor(state.Color);
        statusText.Text = state.Command;

        var preview = new Border
        {
            Height = 28,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(selectedColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 210, 216, 226)),
            BorderThickness = new Thickness(1)
        };

        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            Color = selectedColor,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        picker.ColorChanged += (_, args) =>
        {
            var command = ShortcutInteractiveCommandLogic.FormatColorCommand(ToSitemapColor(args.NewColor));
            statusText.Text = command;
            preview.Background = new SolidColorBrush(args.NewColor);
            QueueSend(command);
        };

        var panel = CreatePanel();
        panel.Children.Add(CreateHeader());
        panel.Children.Add(preview);
        panel.Children.Add(statusText);
        panel.Children.Add(picker);
        return CreateRoot(panel);
    }

    private static StackPanel CreatePanel()
    {
        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private Grid CreateHeader()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var title = new TextBlock
        {
            Text = action.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        grid.Children.Add(title);

        var closeButton = new Button
        {
            Content = "\uE711",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        closeButton.Click += (_, _) => Close();
        Grid.SetColumn(closeButton, 1);
        grid.Children.Add(closeButton);

        return grid;
    }

    private static Border CreateRoot(UIElement content)
    {
        return new Border
        {
            Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(255, 252, 252, 253)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 210, 216, 226)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    private void QueueSend(string command)
    {
        if (isClosing || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        sendDebounceCts?.Cancel();
        sendDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        sendDebounceCts = cts;
        _ = SendAfterDelayAsync(command, cts.Token);
    }

    private async Task SendAfterDelayAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SendDebounceMs, cancellationToken);
            await SendIfChangedAsync(command, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // New picker movement superseded this command.
        }
    }

    private void FlushCurrentSliderValue(Slider slider)
    {
        _ = SendIfChangedAsync(ShortcutInteractiveCommandLogic.FormatSliderCommand(slider.Value), CancellationToken.None);
    }

    private async Task SendIfChangedAsync(string command, CancellationToken cancellationToken)
    {
        if (isClosing || string.Equals(lastSentCommand, command, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await client.SendCommandAsync(action.TargetItem, command, cancellationToken);
            lastSentCommand = command;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            statusText.Text = "Command could not be sent.";
        }
    }

    private void PositionNearCursor()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var size = AppWindow.Size;
        var displayArea = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var x = Math.Clamp(cursor.X - (size.Width / 2), workArea.X, workArea.X + workArea.Width - size.Width);
        var y = Math.Clamp(cursor.Y + 18, workArea.Y, workArea.Y + workArea.Height - size.Height);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && !isClosing)
        {
            Close();
        }
    }

    private static Color ToWindowsColor(SitemapColor color)
    {
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static SitemapColor ToSitemapColor(Color color)
    {
        return new SitemapColor(color.A, color.R, color.G, color.B);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);
}
