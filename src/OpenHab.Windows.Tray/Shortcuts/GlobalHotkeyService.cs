using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenHab.App.Shortcuts;
using WinRT.Interop;

namespace OpenHab.Windows.Tray.Shortcuts;

internal sealed record HotkeyRegistrationFailure(string Owner, string Message);

internal sealed record HotkeyRefreshResult(ImmutableArray<HotkeyRegistrationFailure> Failures)
{
    public bool Succeeded => Failures.IsDefaultOrEmpty;
}

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int FirstHotkeyId = 0x4F00;
    private const string InvalidBindingMessage = "This shortcut is invalid and could not be registered.";
    private const string InvalidActionMessage = "This action is invalid and its shortcut could not be registered.";
    private const string DuplicateBindingMessage = "This shortcut is already used by another shortcut in settings.";
    private const string OsRegistrationFailedMessage = "This shortcut could not be registered. It may already be used by Windows or another app.";

    private readonly DispatcherQueue dispatcherQueue;
    private readonly IntPtr hwnd;
    private readonly Dictionary<int, RegisteredHotkey> registered = [];
    private int nextHotkeyId = FirstHotkeyId;
    private bool disposed;

    public GlobalHotkeyService(Window window, DispatcherQueue dispatcherQueue)
    {
        this.dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        hwnd = WindowNative.GetWindowHandle(window ?? throw new ArgumentNullException(nameof(window)));
    }

    public event EventHandler? CommandMenuRequested;
    public event EventHandler<ShortcutAction>? ActionRequested;

    public HotkeyRefreshResult Refresh(ShortcutSettings settings)
    {
        ThrowIfDisposed();

        var failures = ImmutableArray.CreateBuilder<HotkeyRegistrationFailure>();
        var seenBindings = new HashSet<string>(StringComparer.Ordinal);

        UnregisterAll();

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
            if (action.GlobalShortcut is null)
            {
                continue;
            }

            if (!ShortcutValidation.ValidateAction(action).IsValid)
            {
                failures.Add(new HotkeyRegistrationFailure($"Action: {action.Name}", InvalidActionMessage));
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

    public bool HandleHotkeyMessage(int id)
    {
        ThrowIfDisposed();

        if (!registered.TryGetValue(id, out var hotkey))
        {
            return false;
        }

        _ = dispatcherQueue.TryEnqueue(() =>
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

        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        UnregisterAll();
    }

    private void RegisterBinding(
        string owner,
        ShortcutBinding? binding,
        ShortcutAction? action,
        ISet<string> seenBindings,
        ICollection<HotkeyRegistrationFailure> failures)
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
            failures.Add(new HotkeyRegistrationFailure(owner, InvalidBindingMessage));
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed record RegisteredHotkey(ShortcutAction? Action);
}
