using Microsoft.UI.Windowing;
using OpenHab.App.Shortcuts;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;

namespace OpenHab.Windows.Tray.Shortcuts;

public sealed class RadialCommandMenuWindow
{
    private const int WindowSize = 260;
    private const int MaxVisibleRadialSlots = 8;
    private const int MaxActionSlotsPerPage = 7;
    private const float ActionButtonSize = 52f;
    private const float CloseButtonSize = 58f;
    private const float ActionRadius = 82f;
    private const string WindowClassName = "OpenHabRadialCommandMenuLayeredWindow";

    private static readonly ConcurrentDictionary<IntPtr, RadialCommandMenuWindow> Instances = new();
    private static readonly object ClassRegistrationLock = new();
    private static bool windowClassRegistered;
    private static WndProcDelegate? sharedWndProc;

    private readonly List<ShortcutAction> validActions = [];
    private readonly List<RadialDisplayEntry> displayedEntries = [];
    private readonly List<RadialButtonLayout> buttonLayouts = [];

    private Func<ShortcutAction, Task>? executeActionAsync;
    private IntPtr hwnd;
    private int selectedActionIndex;
    private int hoveredButtonIndex = int.MinValue;
    private int currentPageIndex;
    private int totalPages;
    private bool isVisible;
    private int windowX;
    private int windowY;

    public bool IsMenuVisible => isVisible;

    public void ShowActions(IReadOnlyList<ShortcutAction> commandMenuActions, Func<ShortcutAction, Task> execute)
    {
        executeActionAsync = execute;
        validActions.Clear();
        validActions.AddRange((commandMenuActions ?? [])
            .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid));

        currentPageIndex = 0;
        totalPages = validActions.Count == 0
            ? 0
            : (int)Math.Ceiling(validActions.Count / (double)MaxActionSlotsPerPage);

        EnsureWindow();
        BuildDisplayedEntriesForCurrentPage();
        PositionUnderCursor();
        RenderAndShow();
    }

    public void CloseMenu()
    {
        if (!isVisible)
        {
            return;
        }

        isVisible = false;
        ReleaseCapture();
        ShowWindow(hwnd, SW_HIDE);
        hoveredButtonIndex = int.MinValue;
    }

    private void EnsureWindow()
    {
        if (hwnd != IntPtr.Zero)
        {
            return;
        }

        RegisterWindowClass();
        hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            WindowClassName,
            "openHAB Command Menu",
            WS_POPUP,
            0,
            0,
            WindowSize,
            WindowSize,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create command menu window.");
        }

        Instances[hwnd] = this;
    }

    private static void RegisterWindowClass()
    {
        lock (ClassRegistrationLock)
        {
            if (windowClassRegistered)
            {
                return;
            }

            sharedWndProc = StaticWndProc;
            var wc = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(sharedWndProc),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
                lpszClassName = WindowClassName
            };

            if (RegisterClassEx(ref wc) == 0)
            {
                throw new InvalidOperationException("Failed to register command menu window class.");
            }

            windowClassRegistered = true;
        }
    }

    private void BuildDisplayedEntriesForCurrentPage()
    {
        displayedEntries.Clear();
        buttonLayouts.Clear();

        var pageStart = currentPageIndex * MaxActionSlotsPerPage;
        foreach (var action in validActions.Skip(pageStart).Take(MaxActionSlotsPerPage))
        {
            displayedEntries.Add(new RadialDisplayEntry(action, RadialEntryType.Action));
        }

        if (totalPages > 1)
        {
            displayedEntries.Add(new RadialDisplayEntry(null, RadialEntryType.PageAdvance));
        }

        if (displayedEntries.Count > MaxVisibleRadialSlots)
        {
            displayedEntries.RemoveRange(MaxVisibleRadialSlots, displayedEntries.Count - MaxVisibleRadialSlots);
        }

        selectedActionIndex = displayedEntries.FindIndex(static entry => entry.EntryType == RadialEntryType.Action);
        if (selectedActionIndex < 0 && displayedEntries.Count > 0)
        {
            selectedActionIndex = 0;
        }
    }

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
        windowX = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - WindowSize);
        windowY = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - WindowSize);
    }

    private void RenderAndShow()
    {
        RenderLayeredBitmap();
        ShowWindow(hwnd, SW_SHOWNORMAL);
        SetWindowPos(hwnd, HWND_TOPMOST, windowX, windowY, WindowSize, WindowSize, SWP_SHOWWINDOW);
        SetForegroundWindow(hwnd);
        SetFocus(hwnd);
        SetCapture(hwnd);
        isVisible = true;
    }

    private void RenderLayeredBitmap()
    {
        using var bitmap = new Bitmap(WindowSize, WindowSize, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            DrawButtons(graphics);
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);

        try
        {
            var position = new NativePoint(windowX, windowY);
            var size = new NativeSize(WindowSize, WindowSize);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            UpdateLayeredWindow(hwnd, screenDc, ref position, ref size, memoryDc, ref source, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void DrawButtons(Graphics graphics)
    {
        buttonLayouts.Clear();

        var center = new PointF(WindowSize / 2f, WindowSize / 2f);
        AddButtonLayout(ButtonIndexClose, center, CloseButtonSize);

        var count = displayedEntries.Count;
        for (var i = 0; i < count; i++)
        {
            var angle = ((Math.PI * 2d) * i / count) - (Math.PI / 2d);
            var point = new PointF(
                center.X + (float)(Math.Cos(angle) * ActionRadius),
                center.Y + (float)(Math.Sin(angle) * ActionRadius));
            AddButtonLayout(i, point, ActionButtonSize);
        }

        foreach (var layout in buttonLayouts.OrderBy(static layout => layout.ButtonIndex == ButtonIndexClose ? 1 : 0))
        {
            DrawButton(graphics, layout);
        }
    }

    private void AddButtonLayout(int buttonIndex, PointF center, float size)
    {
        var scale = hoveredButtonIndex == buttonIndex ? 1.08f : 1f;
        if (buttonIndex >= 0 && buttonIndex == selectedActionIndex)
        {
            scale = Math.Max(scale, 1.04f);
        }

        var scaledSize = size * scale;
        buttonLayouts.Add(new RadialButtonLayout(
            buttonIndex,
            new RectangleF(center.X - (scaledSize / 2f), center.Y - (scaledSize / 2f), scaledSize, scaledSize)));
    }

    private void DrawButton(Graphics graphics, RadialButtonLayout layout)
    {
        var isClose = layout.ButtonIndex == ButtonIndexClose;
        var isHovered = hoveredButtonIndex == layout.ButtonIndex;
        var isSelected = !isClose && layout.ButtonIndex == selectedActionIndex;

        var fill = isClose
            ? (isHovered ? Color.FromArgb(255, 67, 76, 90) : Color.FromArgb(255, 88, 97, 112))
            : (isHovered ? Color.FromArgb(255, 232, 242, 255) : Color.FromArgb(255, 248, 250, 252));
        var stroke = isSelected || isHovered
            ? Color.FromArgb(255, 15, 23, 42)
            : Color.FromArgb(255, 40, 49, 64);
        var strokeWidth = isSelected || isHovered ? 2.2f : 1.6f;

        using var path = new GraphicsPath();
        path.AddEllipse(layout.Bounds);
        using var fillBrush = new SolidBrush(fill);
        graphics.FillPath(fillBrush, path);
        using var strokePen = new Pen(stroke, strokeWidth);
        graphics.DrawPath(strokePen, path);

        var glyph = ResolveButtonGlyph(layout.ButtonIndex);
        using var glyphBrush = new SolidBrush(isClose ? Color.FromArgb(255, 12, 18, 26) : Color.FromArgb(255, 15, 23, 42));
        DrawCenteredGlyph(graphics, glyph, glyphBrush, layout.Bounds, isClose ? 19f : 17f);
    }

    private static void DrawCenteredGlyph(Graphics graphics, string glyph, Brush brush, RectangleF bounds, float size)
    {
        using var fontFamily = new FontFamily("Segoe MDL2 Assets");
        using var format = StringFormat.GenericTypographic;
        using var path = new GraphicsPath();
        path.AddString(glyph, fontFamily, (int)FontStyle.Regular, size, PointF.Empty, format);

        var glyphBounds = path.GetBounds();
        using var transform = new Matrix();
        transform.Translate(
            bounds.Left + ((bounds.Width - glyphBounds.Width) / 2f) - glyphBounds.Left,
            bounds.Top + ((bounds.Height - glyphBounds.Height) / 2f) - glyphBounds.Top);
        path.Transform(transform);
        graphics.FillPath(brush, path);
    }

    private string ResolveButtonGlyph(int buttonIndex)
    {
        if (buttonIndex == ButtonIndexClose)
        {
            return "\uE711";
        }

        if (buttonIndex < 0 || buttonIndex >= displayedEntries.Count)
        {
            return "\uE10F";
        }

        var entry = displayedEntries[buttonIndex];
        return entry.EntryType == RadialEntryType.PageAdvance
            ? "\uE712"
            : ResolveShortcutGlyph(entry.Action?.IconId);
    }

    private int HitTestButton(int x, int y)
    {
        foreach (var layout in buttonLayouts)
        {
            var radius = layout.Bounds.Width / 2f;
            var centerX = layout.Bounds.Left + radius;
            var centerY = layout.Bounds.Top + radius;
            var dx = x - centerX;
            var dy = y - centerY;
            if ((dx * dx) + (dy * dy) <= radius * radius)
            {
                return layout.ButtonIndex;
            }
        }

        return int.MinValue;
    }

    private void HandleButtonClick(int buttonIndex)
    {
        if (buttonIndex == ButtonIndexClose || buttonIndex == int.MinValue)
        {
            CloseMenu();
            return;
        }

        if (buttonIndex < 0 || buttonIndex >= displayedEntries.Count)
        {
            CloseMenu();
            return;
        }

        var entry = displayedEntries[buttonIndex];
        if (entry.EntryType == RadialEntryType.PageAdvance)
        {
            AdvancePage();
            return;
        }

        if (entry.Action is null)
        {
            CloseMenu();
            return;
        }

        _ = ExecuteAndCloseAsync(entry.Action);
    }

    private void AdvancePage()
    {
        if (totalPages <= 1)
        {
            return;
        }

        currentPageIndex = (currentPageIndex + 1) % totalPages;
        BuildDisplayedEntriesForCurrentPage();
        hoveredButtonIndex = int.MinValue;
        RenderLayeredBitmap();
    }

    private void MoveSelection(int offset)
    {
        if (displayedEntries.Count == 0)
        {
            return;
        }

        selectedActionIndex = (selectedActionIndex + offset + displayedEntries.Count) % displayedEntries.Count;
        RenderLayeredBitmap();
    }

    private void ExecuteSelectedAction()
    {
        if (selectedActionIndex < 0 || selectedActionIndex >= displayedEntries.Count)
        {
            CloseMenu();
            return;
        }

        HandleButtonClick(selectedActionIndex);
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
            // The shortcut surface should close even if command execution fails.
        }
        finally
        {
            CloseMenu();
        }
    }

    private IntPtr WndProc(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_MOUSEMOVE:
            {
                var x = GetSignedLowWord(lParam);
                var y = GetSignedHighWord(lParam);
                var hover = HitTestButton(x, y);
                if (hover != hoveredButtonIndex)
                {
                    hoveredButtonIndex = hover;
                    RenderLayeredBitmap();
                }
                return IntPtr.Zero;
            }
            case WM_LBUTTONDOWN:
            {
                var x = GetSignedLowWord(lParam);
                var y = GetSignedHighWord(lParam);
                HandleButtonClick(HitTestButton(x, y));
                return IntPtr.Zero;
            }
            case WM_KEYDOWN:
            {
                var key = (VirtualKey)wParam.ToInt32();
                switch (key)
                {
                    case VirtualKey.Escape:
                        CloseMenu();
                        return IntPtr.Zero;
                    case VirtualKey.Left:
                    case VirtualKey.Up:
                        MoveSelection(-1);
                        return IntPtr.Zero;
                    case VirtualKey.Right:
                    case VirtualKey.Down:
                        MoveSelection(1);
                        return IntPtr.Zero;
                    case VirtualKey.Enter:
                        ExecuteSelectedAction();
                        return IntPtr.Zero;
                }

                break;
            }
            case WM_CAPTURECHANGED:
                if (isVisible)
                {
                    CloseMenu();
                }
                return IntPtr.Zero;
            case WM_KILLFOCUS:
                if (isVisible)
                {
                    CloseMenu();
                }
                return IntPtr.Zero;
            case WM_DESTROY:
                Instances.TryRemove(hwnd, out _);
                hwnd = IntPtr.Zero;
                isVisible = false;
                break;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static IntPtr StaticWndProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        return Instances.TryGetValue(windowHandle, out var window)
            ? window.WndProc(message, wParam, lParam)
            : DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private static short GetSignedLowWord(IntPtr value)
    {
        return unchecked((short)((long)value & 0xffff));
    }

    private static short GetSignedHighWord(IntPtr value)
    {
        return unchecked((short)(((long)value >> 16) & 0xffff));
    }

    public static string ResolveShortcutGlyph(string? iconId)
    {
        return (iconId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "light-bulb" => "\uE793",
            "ceiling-light" => "\uE9A8",
            "lamp" => "\uE706",
            "strip-light" => "\uE95A",
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

    private const int ButtonIndexClose = -1;
    private const uint CS_HREDRAW = 0x0002;
    private const uint CS_VREDRAW = 0x0001;
    private const int IDC_ARROW = 32512;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNORMAL = 1;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const int ULW_ALPHA = 0x00000002;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KILLFOCUS = 0x0008;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const uint WM_DESTROY = 0x0002;

    private enum RadialEntryType
    {
        Action,
        PageAdvance
    }

    private sealed record RadialDisplayEntry(ShortcutAction? Action, RadialEntryType EntryType);

    private sealed record RadialButtonLayout(int ButtonIndex, RectangleF Bounds);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height)
    {
        public int Cx = width;
        public int Cy = height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx wndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref NativePoint pptDst,
        ref NativeSize psize,
        IntPtr hdcSrc,
        ref NativePoint pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out PointInt32 point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursorName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
