# openHAB Windows Flyout Corner Smoothness Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers-extended-cc:subagent-driven-development (if subagents available) or superpowers-extended-cc:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the flyout window corners smooth and Fluent by switching DWM corner preference to `DWMWCP_ROUND`, upgrading the DWM backdrop to Mica, and adding dual-layer acrylic blur — matching the FluentFlyout approach.

**Architecture:** Pure `DwmWindowDecorations` and new `AcrylicBlurHelper` in `OpenHab.Windows.Tray`. No changes to Core, Sitemaps, Rendering, or App layers. The DWM preference change is a one-line enum swap. Acrylic blur is added via Win32 `SetWindowCompositionAttribute` (the same legacy API FluentFlyout uses), layered on top of DWM Mica backdrop. Theme-aware — re-applies on system theme change.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), P/Invoke to `user32.dll` (`SetWindowCompositionAttribute`), `dwmapi.dll` (`DwmSetWindowAttribute`)

**Reference:** FluentFlyout's four-layer corner stack: DWM `DWMWCP_ROUND` + Mica backdrop + `ACCENT_ENABLE_ACRYLICBLURBEHIND` + `WindowStyle=None`.

---

## File Structure Map

| File | Current Role | Changes in This Plan |
|------|-------------|---------------------|
| `src/OpenHab.Windows.Tray/DwmWindowDecorations.cs` | DWM attribute application (rounding, border, dark mode, backdrop) | Swap `RoundSmall`→`Round`, `TransientWindow`→`MainWindow` |
| `src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs` | **New file** | Win32 `SetWindowCompositionAttribute` wrapper for acrylic blur |
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` | Flyout code-behind | Call `AcrylicBlurHelper.Apply()` in `ConfigureFlyoutWindow()` and `OnColorValuesChanged` |

---

### Task 1: Upgrade DWM Corner Preference and Backdrop

**Files:**
- Modify: `src/OpenHab.Windows.Tray/DwmWindowDecorations.cs` (lines 27, 40)

- [ ] **Step 1: Swap corner preference from RoundSmall to Round**

In `DwmWindowDecorations.cs`, change the corner preference:

```csharp
// Line 27 — before:
(DwmWindowCornerPreference.RoundSmall));

// After:
(DwmWindowCornerPreference.Round));
```

The `Round` value (=2) already exists in the `DwmWindowCornerPreference` enum at line 125. No new enum value needed.

- [ ] **Step 2: Swap backdrop from TransientWindow to MainWindow**

In `DwmWindowDecorations.cs`, change the backdrop type:

```csharp
// Line 40 — before:
(DwmSystemBackdropType.TransientWindow));

// After:
(DwmSystemBackdropType.MainWindow));
```

The `MainWindow` value (=2) already exists in the `DwmSystemBackdropType` enum at line 133. This tells DWM to composite the window with the Mica material — wallpaper-adaptive tinted background. Combined with the acrylic blur in Task 2, this creates the dual-layer translucency that makes corners look smooth.

- [ ] **Step 3: Build and verify**

Run: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.Windows.Tray/DwmWindowDecorations.cs
git commit -m "fix: upgrade flyout corner preference to DWMWCP_ROUND and backdrop to Mica"
```

---

### Task 2: Add Acrylic Blur via SetWindowCompositionAttribute

**Files:**
- Create: `src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` (consume the helper)

- [ ] **Step 1: Create AcrylicBlurHelper with Win32 P/Invoke**

Create `src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs`:

```csharp
using System.Runtime.InteropServices;

namespace OpenHab.Windows.Tray;

internal static class AcrylicBlurHelper
{
    // Accent state enum from Undocumented Windows Internals
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_ENABLE_HOSTBACKDROP = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern void SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttributeData data);

    /// <summary>
    /// Applies acrylic blur to the window. The gradient color's alpha
    /// controls blur opacity (0 = clear, 255 = fully opaque tint).
    /// Uses 0x00000000 (fully transparent black) for a pure blur effect.
    /// </summary>
    public static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = 0x00000000 // fully transparent — pure blur, no tint
            };

            var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = Marshal.SizeOf<AccentPolicy>(),
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch (DllNotFoundException)
        {
            // SetWindowCompositionAttribute not available on this system.
        }
        catch (EntryPointNotFoundException)
        {
            // Older platform/runtime combination.
        }
    }

    /// <summary>
    /// Disables acrylic blur, restoring the default window surface.
    /// </summary>
    public static void Remove(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_DISABLED
            };

            var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = Marshal.SizeOf<AccentPolicy>(),
                    Data = accentPtr
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
```

- [ ] **Step 2: Wire AcrylicBlurHelper into FlyoutWindow**

In `FlyoutWindow.xaml.cs`, apply acrylic blur in `ConfigureFlyoutWindow()`:

```csharp
// Add call at end of ConfigureFlyoutWindow():
private void ConfigureFlyoutWindow()
{
    // ... existing code unchanged ...

    appWindow.IsShownInSwitchers = false;

    // Apply acrylic blur after DWM backdrop is configured
    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    AcrylicBlurHelper.Apply(hwnd);
}
```

Also re-apply on theme change in `OnColorValuesChanged`:

```csharp
private void OnColorValuesChanged(UISettings sender, object args)
{
    _ = DispatcherQueue.TryEnqueue(() =>
    {
        ApplyFlyoutTheme();
        ScheduleNativeDecorationApply();

        // Re-apply acrylic blur after DWM attributes are refreshed
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AcrylicBlurHelper.Apply(hwnd);
    });
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs
git commit -m "feat: add acrylic blur to flyout via SetWindowCompositionAttribute"
```

---

### Task 3: Full Solution Verification

**Files:**
- All modified files

- [ ] **Step 1: Clean build**

```bash
dotnet clean OpenHab.Windows.sln --configuration Release && dotnet build OpenHab.Windows.sln --configuration Release
```
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Run full test suite**

```bash
dotnet test OpenHab.Windows.sln --configuration Release
```
Expected: All tests pass (119+ tests, 0 failures)

- [ ] **Step 3: Run LSP diagnostics on changed files**

```bash
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```
Expected: 0 diagnostics

- [ ] **Step 4: Commit**

```bash
git commit -m "chore: full solution verification after corner smoothness changes"
```
