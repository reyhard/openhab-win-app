# Flyout Light-Dismiss via InputLightDismissAction

> **For agentic workers:** Use subagent-driven-development to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking. Delegate each Task to a separate subagent using the **`DeepSeek V4 Flash`** model — all tasks here are straightforward implementation changes (add/remove code in known locations), no complex logic or architecture decisions.

**Goal:** Replace the unreliable `Window.Activated(Deactivated)` flyout-close mechanism with Windows App SDK's `InputLightDismissAction`, and fix the foreground permission issue that prevents activation tracking from working.

**Architecture:** Two-layer change: (1) Grant foreground permission before showing the flyout so `Activate()` actually works, and (2) use `InputLightDismissAction.Dismissed` event — which wraps `WM_ACTIVATE`/`WA_INACTIVE` plus Escape/Alt/HotKey dismissal — instead of the managed `Window.Activated` event, removing brittle guard state.

**Tech Stack:** .NET 10, Windows App SDK 1.8, WinUI 3, P/Invoke user32.dll

---

## File Structure

| File | Role | Action |
|------|------|--------|
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` | Flyout window class | **Modify** — add `InputLightDismissAction`, add P/Invoke, remove deactivation guard state |
| `src/OpenHab.Windows.Tray/App.xaml.cs` | Application shell | **Modify** — grant foreground permission before `flyout.Activate()` |

No new files. No new dependencies beyond existing Windows App SDK.

---

## Current vs Target Architecture

### Current (broken)
```
tray click → explorer.exe processes click
  → ApplyShellStateAsync calls flyout.Activate()
  → SetForegroundWindow FAILS (thread lacks foreground permission)
  → Window.Activated(Deactivated) never fires
  → User must click inside flyout first to give it focus
  → THEN click outside triggers deactivation
```
Plus: `suppressNextDeactivationHide`, `deactivationCloseGeneration` counter, `isEntranceAnimationRunning`, `isExitAnimationRunning` all exist to prevent races from the brittle deactivation handler.

### Target
```
tray click → explorer.exe processes click
  → AllowSetForegroundWindow(-1) grants foreground permission
  → flyout.Activate() → SetForegroundWindow SUCCEEDS → flyout gets focus
  → InputLightDismissAction.Dismissed fires reliably on:
      - click outside
      - Escape key
      - Alt key
      - WM_HOTKEY / APPCOMMAND_BROWSER_*
  → Dismissed handler → CloseFlyoutWithAnimationAsync() → requestHideFlyout()
```
Entrance animation runs independently via `StartEntranceAnimationIfPending()`, not dependent on `Window.Activated`.

---

### Task 1: Add P/Invoke and InputLightDismissAction wiring

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`

**What this does:** Adds the `AllowSetForegroundWindow` P/Invoke and wires `InputLightDismissAction.Dismissed` to trigger the flyout close animation. Removes the `Window.Activated` subscription and the deactivation-based close logic.

- [ ] **Step 1: Add `using Microsoft.UI.Input;` to imports**

  Add to the using block at the top of the file (after the existing `using Windows.Graphics;` or similar):
  ```csharp
  using Microsoft.UI.Input;
  ```

- [ ] **Step 2: Add `AllowSetForegroundWindow` P/Invoke**

  Add to the P/Invoke region (~line 975-990, near existing `user32.dll` declarations):
  ```csharp
  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool AllowSetForegroundWindow(uint dwProcessId);

  public static void GrantForegroundPermission() => AllowSetForegroundWindow(0xFFFFFFFF);
  ```
  `0xFFFFFFFF` = `ASFW_ANY` — grants foreground permission to any process.

- [ ] **Step 3: Add `InputLightDismissAction` field**

  Add to the field declarations (~line 37, after `deactivationCloseGeneration`):
  ```csharp
  private InputLightDismissAction? _lightDismissAction;
  ```

- [ ] **Step 4: Initialize `InputLightDismissAction` in constructor**

  In the constructor, after `FlyoutChrome.PointerPressed += OnFlyoutChromePointerPressed;` (line 54), add:
  ```csharp
  _lightDismissAction = InputLightDismissAction.GetForWindowId(AppWindow.Id);
  _lightDismissAction.Dismissed += OnFlyoutLightDismissed;
  ```

- [ ] **Step 5: Add `OnFlyoutLightDismissed` handler**

  Add as a new private method (e.g., near `CloseFlyoutWithAnimationAsync` ~line 775):
  ```csharp
  private void OnFlyoutLightDismissed(InputLightDismissAction sender, InputLightDismissActionEventArgs args)
  {
      _ = CloseFlyoutWithAnimationAsync();
  }
  ```
  `CloseFlyoutWithAnimationAsync` already has `isExitAnimationRunning` guard (line 748), so re-entrancy is prevented.

- [ ] **Step 6: Remove `this.Activated += OnWindowActivated;`**

  Delete line 55: `this.Activated += OnWindowActivated;`

- [ ] **Step 7: Remove `OnWindowActivated` method**

  Delete the entire method (lines 402-470). Its responsibilities are now covered:
  - Deactivation → `OnFlyoutLightDismissed` (Step 5)
  - Activation / entrance animation → already handled by `StartEntranceAnimationIfPending()` called from `ApplyShellStateAsync`
  - Theme application on activation → already called in `PrepareForShowAnimation()` (line 83)

- [ ] **Step 8: Update `PrepareForShowAnimation` comment**

  Line 78 currently reads: `// Keep content visible if Activated does not fire on this show cycle.`
  Replace with: `// Content is set to visible; entrance animation runs via StartEntranceAnimationIfPending.`

- [ ] **Step 9: Verify build**

  ```bash
  dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release
  ```

---

### Task 2: Remove deactivation guard state

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`

**What this does:** Removes all fields and methods that existed solely to prevent races with the now-removed deactivation handler. Only `isEntranceAnimationRunning` and `isExitAnimationRunning` remain (they gate animations, not deactivation).

- [ ] **Step 1: Remove `suppressNextDeactivationHide` field**

  Delete line 33: `private bool suppressNextDeactivationHide;`

- [ ] **Step 2: Remove `deactivationCloseGeneration` field**

  Delete line 37: `private int deactivationCloseGeneration;`

- [ ] **Step 3: Remove `SuppressNextDeactivation()` method**

  Delete lines 597-602 (the method + its XML doc).

- [ ] **Step 4: Remove `deactivationCloseGeneration` from `CancelRunningAnimations()`**

  In `CancelRunningAnimations()` (line 590), delete line 594:
  ```csharp
  Interlocked.Increment(ref deactivationCloseGeneration);
  ```
  The method body should become:
  ```csharp
  public void CancelRunningAnimations()
  {
      isEntranceAnimationRunning = false;
      isExitAnimationRunning = false;
  }
  ```

- [ ] **Step 5: Verify build**

  ```bash
  dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release
  ```

---

### Task 3: Grant foreground permission in App.xaml.cs

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

**What this does:** Before calling `flyout.Activate()`, grants foreground permission so that `SetForegroundWindow` (called internally by WinUI's `Activate()`) succeeds. Without this, `InputLightDismissAction.Dismissed` won't fire.

- [ ] **Step 1: Add using directive**

  Add at the top of the file (if needed for the static call):
  The `FlyoutWindow` class is in the same namespace (`OpenHab.Windows.Tray`), so no new using is needed.

- [ ] **Step 2: Add foreground permission grant before `flyout.Activate()`**

  In `ApplyShellStateAsync()` ~line 322, before `flyout.Activate()`, add:
  ```csharp
  FlyoutWindow.GrantForegroundPermission();
  ```
  The block becomes:
  ```csharp
  case TrayShellSurface.Flyout:
      main.AppWindow.Hide();
      TrayFlyoutPositioner.PlaceNearTrayArea(
          flyout,
          settingsController?.Current.FlyoutWidth ?? AppSettings.Default.FlyoutWidth);
      flyout.PrepareForShowAnimation();
      FlyoutWindow.GrantForegroundPermission();
      flyout.Activate();
      flyout.StartEntranceAnimationIfPending();
      break;
  ```

- [ ] **Step 3: Verify build**

  ```bash
  dotnet build OpenHab.Windows.sln --configuration Release
  ```

---

### Task 4: Run full test suite and verify

**Files:** None (verification only)

- [ ] **Step 1: Run solution tests**

  ```bash
  dotnet test OpenHab.Windows.sln
  ```

- [ ] **Step 2: Expected results**

  All existing tests pass. No new tests needed — this is a behavior change to activation handling, not new business logic. The test suite covers runtime controllers, parsers, and renderers, which are unaffected.

- [ ] **Step 3: Manual verification checklist**

  1. Click tray icon → flyout appears with entrance animation ✅
  2. Click outside flyout (on desktop) → flyout closes with exit animation ✅
  3. Click tray icon → flyout appears → press Escape → flyout closes ✅
  4. Click tray icon → flyout appears → click tray icon again → flyout closes (toggle works) ✅
  5. Click tray icon → flyout appears → Alt+Tab to another window → flyout closes ✅
  6. Click tray icon → flyout appears → click inside flyout (e.g., sitemap button) → flyout stays open ✅
  7. Rapid toggle (open/close/open) → no blank white window (regression from `docs/superpowers/status/2026-05-08-openhab-windows-flyout-blank-on-retoggle.md`) ✅

---

## Behavior Changes

| Before | After |
|--------|-------|
| Flyout needs to be clicked first to gain focus before "click outside" works | Flyout gains focus automatically when shown (thanks to foreground permission grant) |
| Only click-outside dismisses | Also dismisses on Escape, Alt, and other light-dismiss triggers |
| Race-prone guard state (`suppressNextDeactivationHide`, `deactivationCloseGeneration`) | No deactivation-specific guard state — only `isExitAnimationRunning` for re-entrancy |
| `Window.Activated` event drives both show and hide animation | `InputLightDismissAction.Dismissed` for hide; `StartEntranceAnimationIfPending()` for show |

## Risk Assessment

- **Foreground permission grant**: Using `ASFW_ANY` is a broad grant. In practice, this only matters within the few milliseconds between the `AllowSetForegroundWindow` call and the `flyout.Activate()` call. The permission is consumed by the next `SetForegroundWindow` call. Low risk.
- **Window focus behavior**: The flyout will now reliably become the foreground window when shown. This means it steals keyboard focus from whatever app the user is currently in. This matches system flyout behavior (network/volume/clock flyouts also steal focus). If users find this undesirable, a future iteration could explore the `SetWinEventHook` approach instead.
- **Entrance animation timing**: Currently, the entrance animation was sometimes gated on `Window.Activated` firing. With this change, it runs unconditionally via `StartEntranceAnimationIfPending()`. Since `PrepareForShowAnimation()` already sets the visual to visible (opacity=1), a late-running entrance animation won't cause a blank screen. Low risk.
