using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Shortcuts;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace OpenHab.Windows.Tray.Shortcuts;

public sealed class RadialCommandMenuWindow : Window
{
    private const int WindowSize = 260;
    private const int MaxVisibleRadialSlots = 8;
    private const int MaxActionSlotsPerPage = 7;
    private const double ActionButtonSize = 52d;
    private const double ActionRadius = 88d;

    private readonly Grid root;
    private readonly Canvas actionCanvas;
    private readonly Border menuSurface;
    private readonly TextBlock emptyStateText;
    private readonly Button closeButton;
    private readonly List<RadialDisplayEntry> displayedEntries = [];
    private readonly List<ShortcutAction> validActions = [];

    private Func<ShortcutAction, Task>? executeActionAsync;
    private int selectedActionIndex;
    private int currentPageIndex;
    private int totalPages;

    public bool IsMenuVisible => AppWindow.IsVisible;

    public RadialCommandMenuWindow()
    {
        var appWindow = AppWindow;
        appWindow.Resize(new SizeInt32(WindowSize, WindowSize));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        appWindow.IsShownInSwitchers = false;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        StripNonClientFrame(hwnd);
        ExtendTransparentClientArea(hwnd);
        ApplyCircularWindowRegion(hwnd);
        Activated += RadialCommandMenuWindow_Activated;

        root = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            IsTabStop = true
        };
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), handledEventsToo: true);
        root.PointerPressed += Root_PointerPressed;

        menuSurface = new Border
        {
            Width = WindowSize,
            Height = WindowSize,
            CornerRadius = new CornerRadius(WindowSize / 2d),
            Background = GetBrush("CardBackgroundFillColorDefaultBrush", "LayerFillColorDefaultBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush", "SurfaceStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };

        actionCanvas = new Canvas
        {
            Width = WindowSize - 16,
            Height = WindowSize - 16
        };
        actionCanvas.SizeChanged += (_, _) => ArrangeRadialActions();

        closeButton = new Button
        {
            Width = 60,
            Height = 60,
            CornerRadius = new CornerRadius(30),
            Background = GetBrush("AccentFillColorSecondaryBrush", "AccentFillColorDefaultBrush"),
            BorderBrush = GetBrush("AccentFillColorTertiaryBrush", "CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Content = BuildIconContent("\uE711", 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransform = new ScaleTransform(),
            RenderTransformOrigin = new Point(0.5, 0.5),
            UseSystemFocusVisuals = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(closeButton, "Close command menu");
        ToolTipService.SetToolTip(closeButton, "Close");
        WireHoverVisuals(closeButton, GetBrush("AccentFillColorSecondaryBrush", "AccentFillColorDefaultBrush"));
        closeButton.Click += (_, _) => CloseMenu();

        emptyStateText = new TextBlock
        {
            Text = "No command menu actions available",
            Style = GetTextStyle("BodyTextBlockStyle"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.82,
            Visibility = Visibility.Collapsed
        };

        var layer = new Grid();
        layer.Children.Add(actionCanvas);
        layer.Children.Add(emptyStateText);
        layer.Children.Add(closeButton);
        menuSurface.Child = layer;
        root.Children.Add(menuSurface);
        Content = root;
    }

    public void ShowActions(IReadOnlyList<ShortcutAction> commandMenuActions, Func<ShortcutAction, Task> execute)
    {
        executeActionAsync = execute;
        validActions.Clear();
        validActions.AddRange((commandMenuActions ?? [])
            .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid)
            .ToList());
        currentPageIndex = 0;
        totalPages = validActions.Count == 0
            ? 0
            : (int)Math.Ceiling(validActions.Count / (double)MaxActionSlotsPerPage);

        if (validActions.Count == 0)
        {
            displayedEntries.Clear();
            actionCanvas.Children.Clear();
            emptyStateText.Visibility = Visibility.Visible;
            selectedActionIndex = -1;
            PositionUnderCursor();
            ApplyCircularWindowRegion();
            Activate();
            closeButton.Focus(FocusState.Programmatic);
            return;
        }

        BuildDisplayedEntriesForCurrentPage();
        PositionUnderCursor();
        ApplyCircularWindowRegion();
        Activate();
        FocusSelectedEntry();
    }

    public void CloseMenu()
    {
        AppWindow.Hide();
    }

    private void RadialCommandMenuWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            CloseMenu();
        }
    }

    private async Task ExecuteAndCloseAsync(ShortcutAction action)
    {
        try
        {
            if (executeActionAsync is not null)
            {
                await executeActionAsync(action);
            }
        }
        catch
        {
            // Intentionally swallow in this isolated window.
        }
        finally
        {
            CloseMenu();
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                CloseMenu();
                return;
            case VirtualKey.Left:
            case VirtualKey.Up:
                e.Handled = true;
                MoveSelection(-1);
                return;
            case VirtualKey.Right:
            case VirtualKey.Down:
                e.Handled = true;
                MoveSelection(1);
                return;
            case VirtualKey.Enter:
                if (FocusManager.GetFocusedElement(root.XamlRoot) is Button focusedButton && ReferenceEquals(focusedButton, closeButton))
                {
                    e.Handled = true;
                    CloseMenu();
                    return;
                }

                if (FocusManager.GetFocusedElement(root.XamlRoot) is Button displayedButton
                    && displayedEntries.Any(entry => ReferenceEquals(entry.Button, displayedButton)))
                {
                    // Avoid duplicate execution: let the Button class Enter activation path own Click.
                    return;
                }

                if (selectedActionIndex < 0)
                {
                    e.Handled = true;
                    CloseMenu();
                    return;
                }

                e.Handled = true;
                ExecuteSelectedAction();
                return;
        }
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsWithin(source, menuSurface))
        {
            return;
        }

        e.Handled = true;
        CloseMenu();
    }

    private void MoveSelection(int direction)
    {
        if (displayedEntries.Count == 0)
        {
            return;
        }

        selectedActionIndex = (selectedActionIndex + direction + displayedEntries.Count) % displayedEntries.Count;
        FocusSelectedEntry();
    }

    private void ExecuteSelectedAction()
    {
        if (selectedActionIndex < 0 || selectedActionIndex >= displayedEntries.Count)
        {
            return;
        }

        var entry = displayedEntries[selectedActionIndex];
        if (entry.EntryType == RadialEntryType.PageAdvance)
        {
            AdvancePage();
            return;
        }

        if (entry.Action is null)
        {
            return;
        }

        _ = ExecuteAndCloseAsync(entry.Action);
    }

    private void ArrangeRadialActions()
    {
        if (displayedEntries.Count == 0)
        {
            return;
        }

        var centerX = actionCanvas.Width / 2d;
        var centerY = actionCanvas.Height / 2d;
        var count = displayedEntries.Count;
        for (var i = 0; i < count; i++)
        {
            var angle = ((Math.PI * 2d) * i / count) - (Math.PI / 2d);
            var x = centerX + (Math.Cos(angle) * ActionRadius) - (ActionButtonSize / 2d);
            var y = centerY + (Math.Sin(angle) * ActionRadius) - (ActionButtonSize / 2d);
            Canvas.SetLeft(displayedEntries[i].Button, x);
            Canvas.SetTop(displayedEntries[i].Button, y);
        }
    }

    private void FocusSelectedEntry()
    {
        if (selectedActionIndex < 0 || selectedActionIndex >= displayedEntries.Count)
        {
            return;
        }

        UpdateSelectedVisualState();
        _ = displayedEntries[selectedActionIndex].Button.Focus(FocusState.Programmatic);
    }

    private void AdvancePage()
    {
        if (totalPages <= 1)
        {
            return;
        }

        currentPageIndex = (currentPageIndex + 1) % totalPages;
        BuildDisplayedEntriesForCurrentPage();
        FocusSelectedEntry();
    }

    private void BuildDisplayedEntriesForCurrentPage()
    {
        displayedEntries.Clear();
        actionCanvas.Children.Clear();
        emptyStateText.Visibility = Visibility.Collapsed;

        var pageStart = currentPageIndex * MaxActionSlotsPerPage;
        var pageActions = validActions
            .Skip(pageStart)
            .Take(MaxActionSlotsPerPage)
            .ToList();

        foreach (var action in pageActions)
        {
            var button = CreateActionButton(action);
            displayedEntries.Add(new RadialDisplayEntry(action, button, RadialEntryType.Action));
            actionCanvas.Children.Add(button);
        }

        if (totalPages > 1)
        {
            var isLastPage = currentPageIndex == totalPages - 1;
            var hiddenCount = isLastPage
                ? 0
                : Math.Max(0, validActions.Count - ((currentPageIndex + 1) * MaxActionSlotsPerPage));

            var pageButton = CreatePageButton(hiddenCount, currentPageIndex + 1, totalPages);
            displayedEntries.Add(new RadialDisplayEntry(null, pageButton, RadialEntryType.PageAdvance));
            actionCanvas.Children.Add(pageButton);
        }

        if (displayedEntries.Count > MaxVisibleRadialSlots)
        {
            displayedEntries.RemoveRange(MaxVisibleRadialSlots, displayedEntries.Count - MaxVisibleRadialSlots);
            while (actionCanvas.Children.Count > MaxVisibleRadialSlots)
            {
                actionCanvas.Children.RemoveAt(actionCanvas.Children.Count - 1);
            }
        }

        selectedActionIndex = displayedEntries.FindIndex(static entry => entry.EntryType == RadialEntryType.Action);
        if (selectedActionIndex < 0)
        {
            selectedActionIndex = 0;
        }

        ArrangeRadialActions();
        UpdateSelectedVisualState();
    }

    private void UpdateSelectedVisualState()
    {
        for (var i = 0; i < displayedEntries.Count; i++)
        {
            var button = displayedEntries[i].Button;
            if (i == selectedActionIndex)
            {
                button.BorderThickness = new Thickness(2);
                button.BorderBrush = GetBrush("AccentTextFillColorPrimaryBrush", "AccentFillColorDefaultBrush");
            }
            else
            {
                button.BorderThickness = new Thickness(1);
                button.BorderBrush = GetBrush("CardStrokeColorDefaultBrush", "SurfaceStrokeColorDefaultBrush");
            }
        }
    }

    private Button CreateActionButton(ShortcutAction action)
    {
        var button = new Button
        {
            Width = ActionButtonSize,
            Height = ActionButtonSize,
            CornerRadius = new CornerRadius(ActionButtonSize / 2d),
            Background = GetBrush("SubtleFillColorSecondaryBrush", "LayerFillColorDefaultBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush", "SurfaceStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Content = BuildIconContent(ResolveShortcutGlyph(action.IconId), 18),
            IsTabStop = true,
            RenderTransform = new ScaleTransform(),
            RenderTransformOrigin = new Point(0.5, 0.5),
            UseSystemFocusVisuals = true
        };
        AutomationProperties.SetName(button, $"Run {action.Name}");
        ToolTipService.SetToolTip(button, $"{action.Name} - {action.TargetItem}");
        button.GotFocus += ActionButton_GotFocus;
        WireHoverVisuals(button, GetBrush("SubtleFillColorSecondaryBrush", "LayerFillColorDefaultBrush"));
        button.Click += (_, _) => _ = ExecuteAndCloseAsync(action);
        return button;
    }

    private Button CreatePageButton(int hiddenCount, int pageNumber, int pageCount)
    {
        var moreText = hiddenCount > 0
            ? $"More +{hiddenCount}"
            : "More";

        var button = new Button
        {
            Width = ActionButtonSize,
            Height = ActionButtonSize,
            CornerRadius = new CornerRadius(ActionButtonSize / 2d),
            Background = GetBrush("ControlFillColorSecondaryBrush", "LayerFillColorDefaultBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush", "SurfaceStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            RenderTransform = new ScaleTransform(),
            RenderTransformOrigin = new Point(0.5, 0.5),
            UseSystemFocusVisuals = true,
            Content = BuildIconContent("\uE712", 18)
        };
        AutomationProperties.SetName(button, $"Show command menu page {pageNumber} of {pageCount}");
        ToolTipService.SetToolTip(button, $"{moreText} ({pageNumber}/{pageCount})");
        button.GotFocus += ActionButton_GotFocus;
        WireHoverVisuals(button, GetBrush("ControlFillColorSecondaryBrush", "LayerFillColorDefaultBrush"));
        button.Click += (_, _) => AdvancePage();
        return button;
    }

    private void ActionButton_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var index = displayedEntries.FindIndex(entry => ReferenceEquals(entry.Button, button));
        if (index < 0 || index == selectedActionIndex)
        {
            return;
        }

        selectedActionIndex = index;
        UpdateSelectedVisualState();
    }

    private static UIElement BuildIconContent(string glyph, double size)
    {
        return new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static bool IsWithin(DependencyObject source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static void WireHoverVisuals(Button button, Brush defaultBackground)
    {
        var hoverBackground = GetBrush("ControlFillColorSecondaryBrush", "LayerFillColorDefaultBrush");
        button.PointerEntered += (_, _) => ApplyInteractiveVisual(button, hoverBackground, 1.08d);
        button.PointerExited += (_, _) => ApplyInteractiveVisual(button, defaultBackground, 1d);
        button.PointerCanceled += (_, _) => ApplyInteractiveVisual(button, defaultBackground, 1d);
    }

    private static void ApplyInteractiveVisual(Button button, Brush background, double scale)
    {
        button.Background = background;
        if (button.RenderTransform is ScaleTransform transform)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
    }

    private static Style GetTextStyle(string styleKey)
    {
        if (Application.Current.Resources.TryGetValue(styleKey, out var value) && value is Style style)
        {
            return style;
        }

        if (Application.Current.Resources.TryGetValue("DefaultTextBlockStyle", out var fallback) && fallback is Style defaultStyle)
        {
            return defaultStyle;
        }

        return new Style(typeof(TextBlock));
    }

    private static Brush GetBrush(string preferredKey, string fallbackKey)
    {
        if (Application.Current.Resources.TryGetValue(preferredKey, out var preferred) && preferred is Brush preferredBrush)
        {
            return preferredBrush;
        }

        if (Application.Current.Resources.TryGetValue(fallbackKey, out var fallback) && fallback is Brush fallbackBrush)
        {
            return fallbackBrush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public static string ResolveShortcutGlyph(string? iconId)
    {
        return (iconId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "light-bulb" => "\uE793",
            "ceiling-light" => "\uE9A8",
            "lamp" => "\uE706",
            "strip-light" => "\uE706",
            "brightness" => "\uE706",
            "color-wheel" => "\uE790",
            "power" => "\uE7E8",
            "blinds" => "\uE8B8",
            "curtains" => "\uE91C",
            "garage" => "\uE90F",
            "door" => "\uE8B7",
            "lock" => "\uE72E",
            "thermostat" => "\uE9CA",
            "fan" => "\uE9F3",
            "snowflake" => "\uEB46",
            "flame" => "\uE9A9",
            "humidity" => "\uE9CA",
            "play" => "\uE768",
            "pause" => "\uE769",
            "stop" => "\uE71A",
            "speaker" => "\uE15D",
            "tv" => "\uE7F4",
            "cast" => "\uE7F5",
            "music" => "\uE189",
            "volume" => "\uE767",
            "scene" => "\uE7C4",
            "movie" => "\uE8B2",
            "sleep" => "\uE708",
            "away" => "\uE81D",
            "sparkle" => "\uEA3A",
            "timer" => "\uE823",
            _ => "\uE10F"
        };
    }

    private enum RadialEntryType
    {
        Action,
        PageAdvance
    }

    private sealed record RadialDisplayEntry(ShortcutAction? Action, Button Button, RadialEntryType EntryType);

    private void PositionUnderCursor()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var x = cursor.X - (WindowSize / 2);
        var y = cursor.Y - (WindowSize / 2);
        var displayArea = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        x = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - WindowSize);
        y = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - WindowSize);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void ApplyCircularWindowRegion()
    {
        ApplyCircularWindowRegion(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    private static void StripNonClientFrame(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const int WS_BORDER = 0x00800000;
        const int WS_DLGFRAME = 0x00400000;
        const int WS_CAPTION = 0x00C00000;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_SYSMENU = 0x00080000;
        const int WS_EX_DLGMODALFRAME = 0x00000001;
        const int WS_EX_CLIENTEDGE = 0x00000200;
        const int WS_EX_STATICEDGE = 0x00020000;
        const int WS_EX_WINDOWEDGE = 0x00000100;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_FRAMECHANGED = 0x0020;

        var style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(WS_BORDER | WS_DLGFRAME | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
        SetWindowLong(hwnd, GWL_STYLE, style);

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle &= ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE | WS_EX_WINDOWEDGE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private static void ExtendTransparentClientArea(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var margins = new DwmMargins
        {
            CxLeftWidth = -1,
            CxRightWidth = -1,
            CyTopHeight = -1,
            CyBottomHeight = -1
        };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    private static void ApplyCircularWindowRegion(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var region = CreateEllipticRgn(0, 0, WindowSize + 1, WindowSize + 1);
        if (region != IntPtr.Zero)
        {
            _ = SetWindowRgn(hwnd, region, redraw: true);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins
    {
        public int CxLeftWidth;
        public int CxRightWidth;
        public int CyTopHeight;
        public int CyBottomHeight;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref DwmMargins margins);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
}
