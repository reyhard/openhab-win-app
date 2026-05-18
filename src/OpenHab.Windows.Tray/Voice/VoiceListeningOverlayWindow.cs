using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace OpenHab.Windows.Tray.Voice;

[ExcludeFromCodeCoverage(Justification = "Native layered-window visual host.")]
public sealed class VoiceListeningOverlayWindow
{
    private const int WindowWidth = 220;
    private const int WindowHeight = 148;
    private const int IconCenterX = WindowWidth / 2;
    private const int IconCenterY = 58;
    private const string WindowClassName = "OpenHabVoiceListeningOverlayLayeredWindow";

    private static readonly ConcurrentDictionary<IntPtr, VoiceListeningOverlayWindow> Instances = new();
    private static readonly object ClassRegistrationLock = new();
    private static bool windowClassRegistered;
    private static WndProcDelegate? sharedWndProc;

    private readonly DispatcherTimer animationTimer = new();
    private IntPtr hwnd;
    private double animationPhase;
    private double activityBoost;
    private bool isListening;
    private bool isClosed;
    private string statusText = "Listening...";

    public VoiceListeningOverlayWindow()
    {
        animationTimer.Interval = TimeSpan.FromMilliseconds(80);
        animationTimer.Tick += AnimationTimer_Tick;
    }

    public event EventHandler? Closed;

    public void ShowOverlay()
    {
        EnsureWindow();
        PositionNearTopCenter();
        RenderAndShow();
    }

    public void SetListening(bool listening)
    {
        isListening = listening;
        animationPhase = 0d;
        activityBoost = listening ? 1d : 0d;

        if (listening)
        {
            animationTimer.Start();
            RenderLayeredBitmap();
            return;
        }

        animationTimer.Stop();
        RenderLayeredBitmap();
    }

    public void PulseVoiceActivity()
    {
        activityBoost = 1d;
        RenderLayeredBitmap();
    }

    public void SetStatus(string text)
    {
        statusText = string.IsNullOrWhiteSpace(text) ? "Listening..." : text.Trim();
        RenderLayeredBitmap();
    }

    public void Close()
    {
        if (isClosed)
        {
            return;
        }

        isClosed = true;
        animationTimer.Stop();
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_HIDE);
            Instances.TryRemove(hwnd, out _);
            DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureWindow()
    {
        if (hwnd != IntPtr.Zero)
        {
            return;
        }

        RegisterWindowClass();
        hwnd = CreateWindowEx(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            WindowClassName,
            "openHAB Voice Listening",
            WS_POPUP,
            0,
            0,
            WindowWidth,
            WindowHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create voice listening overlay window.");
        }

        Instances[hwnd] = this;
        isClosed = false;
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
                throw new InvalidOperationException("Failed to register voice listening overlay window class.");
            }

            windowClassRegistered = true;
        }
    }

    private void PositionNearTopCenter()
    {
        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, out var workArea, 0))
        {
            workArea = new NativeRect
            {
                Left = 0,
                Top = 0,
                Right = GetSystemMetrics(SM_CXSCREEN),
                Bottom = GetSystemMetrics(SM_CYSCREEN)
            };
        }

        var width = workArea.Right - workArea.Left;
        var height = workArea.Bottom - workArea.Top;
        var x = workArea.Left + Math.Max(0, (width - WindowWidth) / 2);
        var y = workArea.Top + Math.Min(96, Math.Max(24, height / 10));
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, WindowWidth, WindowHeight, SWP_NOACTIVATE);
    }

    private void RenderAndShow()
    {
        RenderLayeredBitmap();
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void RenderLayeredBitmap()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        using var bitmap = new Bitmap(WindowWidth, WindowHeight, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            DrawOverlay(graphics);
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memoryDc, bitmapHandle);

        try
        {
            var position = new NativePoint(0, 0);
            ClientToScreen(hwnd, ref position);
            var size = new NativeSize(WindowWidth, WindowHeight);
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

    private void DrawOverlay(Graphics graphics)
    {
        using var rootPath = CreateRoundedRectanglePath(new RectangleF(0.5f, 0.5f, WindowWidth - 1f, WindowHeight - 1f), 16f);
        using var rootBrush = new SolidBrush(Color.FromArgb(238, 28, 31, 36));
        graphics.FillPath(rootBrush, rootPath);

        var wave = (Math.Sin(animationPhase) + 1d) / 2d;
        var intensity = Math.Clamp(0.35d + (wave * 0.35d) + (activityBoost * 0.3d), 0d, 1d);
        var ringSize = 70f + ((float)intensity * 22f);
        var ringBounds = new RectangleF(IconCenterX - (ringSize / 2f), IconCenterY - (ringSize / 2f), ringSize, ringSize);
        using var ringPen = new Pen(Color.FromArgb((int)(70 + (intensity * 140)), 0, 120, 212), 3f);
        graphics.DrawEllipse(ringPen, ringBounds);

        using var micBackground = new SolidBrush(Color.FromArgb(255, 0, 120, 212));
        graphics.FillEllipse(micBackground, IconCenterX - 29, IconCenterY - 29, 58, 58);

        using var glyphFont = new Font("Segoe Fluent Icons", 34f + ((float)activityBoost * 6f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var glyphBrush = new SolidBrush(Color.White);
        using var glyphFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString("\uE720", glyphFont, glyphBrush, new RectangleF(IconCenterX - 32, IconCenterY - 31, 64, 64), glyphFormat);

        using var textFont = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisWord
        };
        graphics.DrawString(statusText, textFont, textBrush, new RectangleF(16, 108, WindowWidth - 32, 26), textFormat);
    }

    private void AnimationTimer_Tick(object? sender, object e)
    {
        if (!isListening)
        {
            return;
        }

        animationPhase += 0.22d;
        activityBoost = Math.Max(0d, activityBoost - 0.09d);
        RenderLayeredBitmap();
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (Instances.TryGetValue(hwnd, out var instance))
        {
            return instance.WndProc(hwnd, msg, wParam, lParam);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr WndProc(IntPtr windowHandle, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DESTROY)
        {
            Instances.TryRemove(windowHandle, out _);
            return IntPtr.Zero;
        }

        return DefWindowProc(windowHandle, msg, wParam, lParam);
    }

    private const int CS_HREDRAW = 0x0002;
    private const int CS_VREDRAW = 0x0001;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const int SPI_GETWORKAREA = 0x0030;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint WM_DESTROY = 0x0002;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr IDC_ARROW = new(32512);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

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
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
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

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, out NativeRect pvParam, int fWinIni);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

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
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Cx;
        public int Cy;

        public NativeSize(int cx, int cy)
        {
            Cx = cx;
            Cy = cy;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
