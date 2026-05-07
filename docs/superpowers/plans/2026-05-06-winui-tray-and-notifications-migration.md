# WinUIEx Tray Icon + CommunityToolkit Notifications Migration Plan

> **For agentic workers:** REQUIRED: Use superpowers-extended-cc:subagent-driven-development or superpowers-extended-cc:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `System.Windows.Forms.NotifyIcon` (WinForms) with `WinUIEx.TrayIcon` and replace `Microsoft.Windows.AppNotifications.AppNotificationManager` with `CommunityToolkit.WinUI.Notifications.ToastNotificationManagerCompat`, eliminating the WinForms FrameworkReference and fixing `REGDB_E_CLASSNOTREG` in one migration.

**Architecture:** Two independent subsystem replacements that converge in `App.xaml.cs`. The tray icon subsystem (4 files) swaps WinForms for WinUIEx, keeping the same callback-based shell state machine. The notification subsystem (2 files) swaps Windows App SDK push notifications for CommunityToolkit's desktop-compatible toast API, keeping the same public API surface (`IsAvailable`, `Show()`, `NotificationActivated` event). Both subsystems stay fully isolated to their respective projects; no changes cascade into `OpenHab.Core`, `OpenHab.App`, `OpenHab.Sitemaps`, or `OpenHab.Rendering`.

**Tech Stack:** WinUI 3, .NET 10, Microsoft.WindowsAppSDK 1.8.260317003, WinUIEx, CommunityToolkit.WinUI.Notifications

---

## Impact Summary

| Subsystem | Files Changed | Dependencies Added | Dependencies Removed |
|---|---|---|---|
| Tray icon (WinForms → WinUIEx) | 4 files | `WinUIEx` | `Microsoft.WindowsDesktop.App.WindowsForms` (entire FrameworkReference) |
| Notifications (AppNotificationManager → CommunityToolkit) | 2 files | `CommunityToolkit.WinUI.Notifications` | None (uses existing Microsoft.WindowsAppSDK) |
| Tests | 0 files | — | — |
| Cross-cutting | None | — | — |

---

### Task 1: Add NuGet packages and remove WinForms FrameworkReference

**Files:**
- Modify: `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`
- Modify: `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj`

- [ ] **Step 1: Add WinUIEx package to Tray csproj**

Add to `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj` inside the `<ItemGroup>` containing the existing `PackageReference` for `Microsoft.WindowsAppSDK`:

```xml
<PackageReference Include="WinUIEx" Version="2.9.0" />
```

- [ ] **Step 2: Add CommunityToolkit package to Notifications csproj**

Add to `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj` inside the `<ItemGroup>` containing the existing `PackageReference` for `Microsoft.WindowsAppSDK`:

```xml
<PackageReference Include="CommunityToolkit.WinUI.Notifications" Version="7.1.2" />
```

- [ ] **Step 3: Remove WinForms FrameworkReference from Tray csproj**

In `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`, remove this `<ItemGroup>` entirely:

```xml
<ItemGroup>
    <FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />
</ItemGroup>
```

- [ ] **Step 4: Restore packages**

```powershell
dotnet restore OpenHab.Windows.sln
```

Expected: No errors. All packages resolve.

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj
git commit -m "build: add WinUIEx and CommunityToolkit packages, remove WinForms FrameworkReference"
```

---

### Task 2: Rewrite ToastService with CommunityToolkit.WinUI.Notifications

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/ToastService.cs`

**Public API contract (MUST preserve — callers in App.xaml.cs depend on this exact surface):**
```csharp
public static bool IsAvailable { get; }
public static void EnsureRegistered();
public static void Show(string title, string body);
public static event EventHandler? NotificationActivated;
```

- [ ] **Step 1: Write the new ToastService.cs**

Replace the entire contents of `src/OpenHab.Windows.Notifications/ToastService.cs` with:

```csharp
using Microsoft.Toolkit.Uwp.Notifications;
using OpenHab.Core;

namespace OpenHab.Windows.Notifications;

public static class ToastService
{
    private static bool isAvailable;

    /// <summary>
    /// True when toast notifications are available.
    /// In packed mode (MSIX), always true. In unpackaged mode, depends on
    /// the Start menu shortcut with AppUserModelId being present.
    /// </summary>
    public static bool IsAvailable => isAvailable;

    public static void EnsureRegistered()
    {
        if (isAvailable) return;

        try
        {
            // ToastNotificationManagerCompat auto-registers on first use.
            // This call forces early registration so we can detect failures.
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            _ = notifier.Setting; // validates the notifier is functional
            ToastNotificationManagerCompat.OnActivated += OnActivated;
            isAvailable = true;
            DiagnosticLogger.Info("Toast notification system registered via CommunityToolkit");
        }
        catch (Exception ex)
        {
            isAvailable = false;
            DiagnosticLogger.Warn($"Toast notifications unavailable — {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void Show(string title, string body)
    {
        if (!isAvailable) return;

        EnsureRegistered();
        if (!isAvailable) return;

        DiagnosticLogger.Info($"Showing toast: \"{title}\"");

        var builder = new ToastContentBuilder()
            .AddText(title)
            .AddText(body);

        var toast = new ToastNotification(builder.GetXml());
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
    }

    public static event EventHandler? NotificationActivated;

    private static void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        DiagnosticLogger.Info("User activated a toast notification");
        NotificationActivated?.Invoke(null, EventArgs.Empty);
    }
}
```

- [ ] **Step 2: Verify the public API is unchanged**

The caller in `App.xaml.cs` (lines 154-193) uses:
- `ToastService.EnsureRegistered()` ✅ same signature
- `ToastService.IsAvailable` ✅ same signature
- `ToastService.NotificationActivated += ...` ✅ same signature
- `ToastService.Show(title, body)` ✅ same signature

No changes needed in `App.xaml.cs` for the notification subsystem.

- [ ] **Step 3: Verify ShortcutRegistrar is still needed**

`ShortcutRegistrar.cs` creates a Start menu shortcut with `AppUserModelId=OpenHab.OpenHabWinApp`. CommunityToolkit's `ToastNotificationManagerCompat` uses this exact same AUMID mechanism for unpackaged apps — the shortcut is essential. **No changes to ShortcutRegistrar.cs.**

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.Windows.Notifications/ToastService.cs
git commit -m "refactor: replace AppNotificationManager with CommunityToolkit.WinUI.Notifications"
```

---

### Task 3: Rewrite TrayIconService with WinUIEx TrayIcon

**Files:**
- Rewrite: `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`

**Public API contract (MUST preserve — callers in App.xaml.cs depend on this exact surface):**
```csharp
public TrayIconService(Action toggleFlyout, Action openMainWindow, Action exitApplication);
public void ShowBalloon(string title, string text); // currently unused but part of public API
public void Dispose();
```

**Important note about WinUIEx.TrayIcon:** WinUIEx v2.9.0+ TrayIcon uses GDI `LoadImage` internally, which requires `.ico` format. The project only has `Assets\openhab-icon.svg`. You must convert it to `openhab-icon.ico` (32×32 or 48×48, any icon editor or `magick convert openhab-icon.svg openhab-icon.ico`). The SVG remains for FlyoutWindow/MainWindow window icons.

- [ ] **Step 1: Generate ICO from SVG**

Convert the existing SVG to an ICO file. Using ImageMagick:
```powershell
magick convert src/OpenHab.Windows.Tray/Assets/openhab-icon.svg -resize 32x32 src/OpenHab.Windows.Tray/Assets/openhab-icon.ico
```

Or use any icon editor. The resulting `openhab-icon.ico` must be in `Assets/` alongside the existing SVG.

- [ ] **Step 2: Add ICO to csproj as content**

Add to `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj` inside the existing `<ItemGroup>` with the SVG content item:
```xml
<Content Include="Assets\openhab-icon.ico">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

- [ ] **Step 3: Rewrite TrayIconService.cs**

Replace the entire contents of `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs` with:

```csharp
using Microsoft.UI.Xaml.Controls;
using OpenHab.Windows.Notifications;
using System.Threading;
using WinUIEx;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly TrayIcon trayIcon;
    private int isDisposed;

    public TrayIconService(Action toggleFlyout, Action openMainWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(toggleFlyout);
        ArgumentNullException.ThrowIfNull(openMainWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        // WinUIEx TrayIcon requires an .ico file (GDI LoadImage) and a unique uint ID.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "openhab-icon.ico");
        trayIcon = new TrayIcon(trayiconId: 1, iconPath, tooltip: "openHAB");

        trayIcon.Selected += (_, _) => toggleFlyout();
        trayIcon.ContextMenu += (_, e) =>
        {
            e.Flyout = new MenuFlyout
            {
                Items =
                {
                    new MenuFlyoutItem { Text = "Open flyout", Command = new RelayCommand(toggleFlyout) },
                    new MenuFlyoutItem { Text = "Open main window", Command = new RelayCommand(openMainWindow) },
                    new MenuFlyoutSeparator(),
                    new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(exitApplication) }
                }
            };
        };

        trayIcon.IsVisible = true;
    }

    /// <summary>
    /// Shows a toast notification. WinUIEx does not provide balloon tips natively;
    /// this delegates to the CommunityToolkit toast service for proper Action Center toasts.
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        if (isDisposed != 0) return;
        ToastService.Show(title, text);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        trayIcon.Dispose();
    }
}

/// <summary>
/// Minimal ICommand implementation for MenuFlyout items.
/// </summary>
internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action execute;
    public RelayCommand(Action execute) => this.execute = execute;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
```

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.Windows.Tray/Tray/TrayIconService.cs src/OpenHab.Windows.Tray/Assets/openhab-icon.ico
git commit -m "refactor: replace WinForms NotifyIcon with WinUIEx TrayIcon"
```

---

### Task 4: Rewrite TrayFlyoutPositioner without WinForms Screen dependency

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs`

The current implementation uses `System.Windows.Forms.Screen.PrimaryScreen.WorkingArea` to get screen bounds. After removing the WinForms FrameworkReference, we use `Microsoft.UI.Windowing.DisplayArea.Primary` — the WinUI 3 native equivalent.

- [ ] **Step 1: Rewrite TrayFlyoutPositioner.cs**

Replace the entire contents of `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs` with:

```csharp
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace OpenHab.Windows.Tray.Tray;

public static class TrayFlyoutPositioner
{
    private const int DefaultFlyoutWidth = 460;
    private const int DefaultFlyoutHeight = 560;
    private const int ScreenPadding = 8;

    public static void PlaceNearTrayArea(FlyoutWindow flyoutWindow, int preferredWidth)
    {
        ArgumentNullException.ThrowIfNull(flyoutWindow);

        var appWindow = flyoutWindow.AppWindow;
        var width = preferredWidth > 0 ? preferredWidth : DefaultFlyoutWidth;
        var height = appWindow.Size.Height > 0 ? appWindow.Size.Height : DefaultFlyoutHeight;
        var placement = CalculatePlacement(width, height);

        appWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height));
    }

    public static TrayFlyoutPlacement CalculatePlacement(int flyoutWidth, int flyoutHeight)
    {
        // Use DisplayArea.Primary — the WinUI 3 equivalent of Screen.PrimaryScreen.WorkingArea
        var workArea = DisplayArea.Primary.WorkArea;

        var maxWidth = Math.Max(1, workArea.Width - (ScreenPadding * 2));
        var maxHeight = Math.Max(1, workArea.Height - (ScreenPadding * 2));
        var width = Math.Clamp(flyoutWidth, 1, maxWidth);
        var height = Math.Clamp(flyoutHeight, 1, maxHeight);

        // Position at bottom-right of working area, near the taskbar tray area
        var x = workArea.X + workArea.Width - width - ScreenPadding;
        var y = workArea.Y + workArea.Height - height - ScreenPadding;

        return new TrayFlyoutPlacement(x, y, width, height);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs
git commit -m "refactor: replace WinForms Screen API with DisplayArea.Primary in flyout positioner"
```

---

### Task 5: Update App.xaml.cs tray icon wiring

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

The `TrayIconService` constructor signature is unchanged (`Action toggleFlyout, Action openMainWindow, Action exitApplication`). The wiring in App.xaml.cs (lines 100-115) remains structurally identical. Only the WinForms-specific comment and `using` need attention.

- [ ] **Step 1: Verify no breaking changes to App.xaml.cs**

The `TrayIconService` constructor call at lines 100-115:

```csharp
trayIcon = new TrayIconService(
    toggleFlyout: () => { shellController.HandlePrimaryTrayClick(); _ = ApplyShellStateAsync(); },
    openMainWindow: () => { shellController.HandleOpenMainWindow(); _ = ApplyShellStateAsync(); },
    exitApplication: () => { shellController.HandleExitRequested(); _ = ApplyShellStateAsync(); });
```

This signature is preserved exactly. **No changes needed to the constructor call.**

- [ ] **Step 2: Remove WinForms threading comment**

At line 349, replace:
```csharp
// Late process shutdown can prevent marshaled cleanup; avoid direct WinForms disposal off the UI thread.
```
With:
```csharp
// Late process shutdown can prevent marshaled cleanup; force UI-thread disposal.
```

- [ ] **Step 3: Remove unused using (optional cleanup)**

If `System.Windows.Forms`, `System.Drawing`, or any WinForms-related usings exist in `App.xaml.cs`, remove them. Based on the current file, there are **none** — no cleanup needed.

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "chore: remove WinForms thread-safety comment from shutdown path"
```

---

### Task 6: Build, test, and verify

**Files:** None (verification only)

- [ ] **Step 1: Clean build artifacts**

```powershell
Remove-Item -Recurse -Force src\OpenHab.Windows.Tray\bin, src\OpenHab.Windows.Tray\obj, src\OpenHab.Windows.Notifications\bin, src\OpenHab.Windows.Notifications\obj -ErrorAction SilentlyContinue
```

- [ ] **Step 2: Build the solution**

```powershell
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: Build succeeds with zero errors. If there are errors related to `WinUIEx` types, verify the package restored correctly with `dotnet list package`.

- [ ] **Step 3: Run all tests**

```powershell
dotnet test OpenHab.Windows.sln --configuration Release
```

Expected: All existing tests pass. No new test failures. The `TrayShellControllerTests` should pass unchanged.

- [ ] **Step 4: Verify no remaining WinForms references**

```powershell
# Should return ZERO matches in src/ directory (docs/ matches are fine)
rg "System\.Windows\.Forms" src/
rg "Microsoft\.WindowsDesktop\.App\.WindowsForms" src/
rg "System\.Drawing" src/
```

Expected: Zero matches in `src/` directory. The only match should be the `using` directives in the rewritten `TrayIconService.cs`... wait — we removed those too. Zero matches everywhere.

- [ ] **Step 5: Verify no remaining AppNotificationManager references in src/**

```powershell
rg "AppNotificationManager|Microsoft\.Windows\.AppNotifications|AppNotificationBuilder" src/
```

Expected: Zero matches in `src/`. Status/plan docs in `docs/` may still reference these — that's fine.

- [ ] **Step 6: Commit**

```bash
git commit -m "chore: final verification — clean build and test pass after migration"
```

---

## Verification Checklist

Before claiming completion:

- [ ] `dotnet build OpenHab.Windows.sln --configuration Release` exits 0
- [ ] `dotnet test OpenHab.Windows.sln --configuration Release` passes all tests
- [ ] No `System.Windows.Forms`, `System.Drawing`, or `Microsoft.WindowsDesktop.App.WindowsForms` in any `src/**/*.csproj` or `src/**/*.cs`
- [ ] No `AppNotificationManager`, `AppNotificationBuilder`, or `Microsoft.Windows.AppNotifications` in any `src/**/*.cs`
- [ ] `ToastService.EnsureRegistered()` is callable from `App.xaml.cs` (signature unchanged)
- [ ] `TrayIconService` constructor takes `(Action toggleFlyout, Action openMainWindow, Action exitApplication)` (signature unchanged)
- [ ] `TrayFlyoutPositioner.PlaceNearTrayArea(FlyoutWindow, int)` is callable (signature unchanged)

---

## Files Changed (Final Inventory)

| File | Change Type |
|---|---|
| `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj` | Add WinUIEx package, remove WinForms FrameworkReference |
| `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj` | Add CommunityToolkit.WinUI.Notifications package |
| `src/OpenHab.Windows.Notifications/ToastService.cs` | Rewrite: AppNotificationManager → CommunityToolkit |
| `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs` | Rewrite: NotifyIcon → WinUIEx TrayIcon |
| `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs` | Rewrite: Screen.PrimaryScreen → WinUIEx MonitorInfo |
| `src/OpenHab.Windows.Tray/App.xaml.cs` | Remove WinForms thread-safety comment (1 line) |

## Files NOT Changed

| File | Reason |
|---|---|
| `src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs` | Still needed for AUMID with CommunityToolkit |
| `src/OpenHab.Windows.Notifications/NotificationPoller.cs` | No dependency on old API |
| `src/OpenHab.Windows.Notifications/CloudNotification.cs` | No dependency on old API |
| `src/OpenHab.App/Tray/TrayShellController.cs` | No dependency on old API |
| `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPlacement.cs` | Pure record struct, no WinForms deps |
| `tests/**/*.cs` | No tray icon or notification-specific tests exist |
