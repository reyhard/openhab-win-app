using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenHab.App.Shortcuts;
using WinRT.Interop;

namespace OpenHab.Windows.Tray.Shortcuts;

[ExcludeFromCodeCoverage(Justification = "Win32 global hotkey registration result glue.")]
internal sealed record HotkeyRegistrationFailure(string Owner, string Message);

[ExcludeFromCodeCoverage(Justification = "Win32 global hotkey registration result glue.")]
internal sealed record HotkeyRefreshResult(ImmutableArray<HotkeyRegistrationFailure> Failures)
{
    public bool Succeeded => Failures.IsDefaultOrEmpty;
}

[ExcludeFromCodeCoverage(Justification = "Win32 global hotkey registration wrapper.")]
internal sealed partial class GlobalHotkeyService : IDisposable
{
    private const int FirstHotkeyId = 0x4F00;
    private const string InvalidBindingMessage = "This shortcut is invalid and could not be registered.";
    private const string UnmappableBindingMessage = "This shortcut cannot be mapped to a Windows hotkey.";
    private const string DuplicateBindingMessage = "This shortcut is already used by another shortcut in settings.";
    private const string OsRegistrationFailedMessage = "This shortcut could not be registered. It may already be used by Windows or another app.";
    private const uint WmHotkey = 0x0312;

    private readonly DispatcherQueue dispatcherQueue;
    private readonly IntPtr hwnd;
    private readonly SubclassProc subclassProc;
    private readonly Dictionary<int, RegisteredHotkey> registered = [];
    private int nextHotkeyId = FirstHotkeyId;
    private bool subclassRemoved;
    private bool suspended;
    private bool disposed;

    public GlobalHotkeyService(Window window, DispatcherQueue dispatcherQueue)
        : this(
            WindowNative.GetWindowHandle(window ?? throw new ArgumentNullException(nameof(window))),
            dispatcherQueue)
    {
    }

    public GlobalHotkeyService(IntPtr hwnd, DispatcherQueue dispatcherQueue)
    {
        this.dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot register global hotkeys because the window handle is not available.");
        }

        this.hwnd = hwnd;
        subclassProc = WindowProc;
        if (!SetWindowSubclass(this.hwnd, subclassProc, 1, IntPtr.Zero))
        {
            throw new InvalidOperationException("Cannot listen for global hotkeys because the window message hook could not be installed.");
        }
    }

    public event EventHandler? CommandMenuRequested;
    public event EventHandler<ShortcutAction>? ActionRequested;

    public HotkeyRefreshResult Refresh(ShortcutSettings settings)
    {
        if (disposed)
        {
            return new HotkeyRefreshResult([]);
        }

        var failures = ImmutableArray.CreateBuilder<HotkeyRegistrationFailure>();
        var seenBindings = new HashSet<string>(StringComparer.Ordinal);

        UnregisterAll();
        if (suspended)
        {
            return new HotkeyRefreshResult([]);
        }

        var normalized = (settings ?? ShortcutSettings.Default).Normalized();

        if (normalized.CommandMenu.Enabled)
        {
            RegisterBinding(
                owner: "openHAB Command Menu",
                binding: normalized.CommandMenu.Binding,
                action: null,
                seenBindings,
                failures);
        }

        foreach (var action in normalized.Actions)
        {
            if (action.CommandType == ShortcutCommandType.Voice && !normalized.VoiceMode.Enabled)
            {
                continue;
            }

            if (action.GlobalShortcut is null)
            {
                continue;
            }

            if (!ShortcutValidation.ValidateAction(action).IsValid)
            {
                continue;
            }

            RegisterBinding(
                owner: $"Action: {action.Name}",
                binding: action.GlobalShortcut,
                action,
                seenBindings,
                failures);
        }

        return new HotkeyRefreshResult(failures.ToImmutable());
    }

    public void Suspend()
    {
        if (disposed || suspended)
        {
            return;
        }

        suspended = true;
        UnregisterAll();
    }

    public HotkeyRefreshResult Resume(ShortcutSettings settings)
    {
        if (disposed)
        {
            return new HotkeyRefreshResult([]);
        }

        suspended = false;
        return Refresh(settings);
    }

    public bool HandleHotkeyMessage(int id)
    {
        if (disposed || suspended)
        {
            return false;
        }

        if (!registered.TryGetValue(id, out var hotkey))
        {
            return false;
        }

        return dispatcherQueue.TryEnqueue(() =>
        {
            if (hotkey.Action is null)
            {
                CommandMenuRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ActionRequested?.Invoke(this, hotkey.Action);
            }
        });
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        RemoveSubclass();
        UnregisterAll();
    }

    private IntPtr WindowProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr refData)
    {
        if (message == WmHotkey && HandleHotkeyMessage((int)wParam))
        {
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void RegisterBinding(
        string owner,
        ShortcutBinding? binding,
        ShortcutAction? action,
        HashSet<string> seenBindings,
        ImmutableArray<HotkeyRegistrationFailure>.Builder failures)
    {
        if (!ShortcutBindingFormatter.TryNormalize(binding, out var normalizedBinding))
        {
            failures.Add(new HotkeyRegistrationFailure(owner, InvalidBindingMessage));
            return;
        }

        var normalizedText = ShortcutBindingFormatter.Format(normalizedBinding);
        if (!seenBindings.Add(normalizedText))
        {
            failures.Add(new HotkeyRegistrationFailure(owner, DuplicateBindingMessage));
            return;
        }

        if (!ShortcutWindowsMapper.TryMap(normalizedBinding, out var modifiers, out var virtualKey))
        {
            failures.Add(new HotkeyRegistrationFailure(owner, UnmappableBindingMessage));
            return;
        }

        var hotkeyId = nextHotkeyId++;
        if (!RegisterHotKey(hwnd, hotkeyId, modifiers, virtualKey))
        {
            failures.Add(new HotkeyRegistrationFailure(owner, OsRegistrationFailedMessage));
            return;
        }

        registered[hotkeyId] = new RegisteredHotkey(action);
    }

    private void UnregisterAll()
    {
        foreach (var id in registered.Keys)
        {
            _ = UnregisterHotKey(hwnd, id);
        }

        registered.Clear();
        nextHotkeyId = FirstHotkeyId;
    }

    private void RemoveSubclass()
    {
        if (subclassRemoved)
        {
            return;
        }

        subclassRemoved = true;
        _ = RemoveWindowSubclass(hwnd, subclassProc, 1);
    }

    private delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr refData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam);

    private sealed record RegisteredHotkey(ShortcutAction? Action);
}
