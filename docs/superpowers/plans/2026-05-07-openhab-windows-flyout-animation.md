# openHAB Windows Flyout Animation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers-extended-cc:subagent-driven-development (if subagents available) or superpowers-extended-cc:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the linear, entrance-only flyout animation with smooth eased entrance + exit animations matching Windows 11 Fluent design feel (inspired by FluentFlyout's approach).

**Architecture:** Extend the existing WinUI Composition API animation code in `FlyoutWindow.xaml.cs` with easing curves, a dismiss animation, and composition batch completion callbacks. Add user-facing `FlyoutAnimationSpeed` to `AppSettings`. The shell state machine in `App.xaml.cs` gains awareness of exit animation completion to avoid the current instant-hide deactivation. No changes to Core, Sitemaps, or Rendering layers — animation is purely a Windows.Tray concern.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), WinUI Composition API (`Microsoft.UI.Composition`)

**Reference:** FluentFlyout animation patterns (WPF Storyboard + DoubleAnimation) adapted to WinUI Composition API equivalents.

---

## File Structure Map

| File | Current Role | Changes in This Plan |
|------|-------------|---------------------|
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` | Flyout code-behind, contains `AnimateFlyoutEntranceAsync()` | Add easing curves, scale animation, exit animation, `ScopedBatch` completion |
| `src/OpenHab.Windows.Tray/App.xaml.cs` | Shell state machine — `ApplyShellStateAsync()` | Wire exit animation into hide flow (`AnimateFlyoutExitAndHideAsync`) |
| `src/OpenHab.App/Settings/AppSettings.cs` | Settings model | Add `FlyoutAnimationSpeed` enum setting |
| `src/OpenHab.App/Settings/AppSettingsController.cs` | Settings controller | Add getter for animation speed |
| `src/OpenHab.Windows.Tray/Rendering/CompositionAnimationHelper.cs` | **New file** | Extracted easing factory, reusable animation builders |
| `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs` | Flyout placement | Minor: pre-position with visual hidden to prevent flicker |
| `tests/OpenHab.App.Tests/Settings/AppSettingsTests.cs` | Settings tests | Test new `FlyoutAnimationSpeed` persistence |
| `docs/superpowers/status/2026-05-07-openhab-windows-flyout-animation-status.md` | **New file** | Status doc for verification record |

---

### Task 1: Add FlyoutAnimationSpeed Setting

**Files:**
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Modify: `tests/OpenHab.App.Tests/Settings/AppSettingsTests.cs`

- [ ] **Step 1: Define `FlyoutAnimationSpeed` enum and add to settings model**

In `AppSettings.cs`, add the enum and property near the existing `FlyoutWidth` field:

```csharp
// AppSettings.cs — add to the record:
public enum FlyoutAnimationSpeed
{
    Off = 0,     // Instant (no animation)
    Fast = 1,    // 150ms
    Default = 2, // 300ms
    Slow = 3,    // 450ms
}

// Add property to AppSettings record:
public FlyoutAnimationSpeed AnimationSpeed { get; init; } = FlyoutAnimationSpeed.Default;
```

Also update `AppSettings.Default` to include `AnimationSpeed = FlyoutAnimationSpeed.Default`.

- [ ] **Step 2: Add getter to AppSettingsController**

In `AppSettingsController.cs`, add a computed property or method that returns the animation duration in milliseconds:

```csharp
// AppSettingsController.cs:
public int GetFlyoutAnimationDurationMs()
{
    return Current.AnimationSpeed switch
    {
        FlyoutAnimationSpeed.Off => 0,
        FlyoutAnimationSpeed.Fast => 150,
        FlyoutAnimationSpeed.Default => 300,
        FlyoutAnimationSpeed.Slow => 450,
        _ => 300
    };
}
```

Note: The `FlyoutAnimationSpeed getter` is always read from `Current.AnimationSpeed` — no separate mutation method needed since `AppSettings` is an immutable record and the user sets this via the normal settings write flow. If the settings write flow doesn't handle the property automatically, add a `SetAnimationSpeed(FlyoutAnimationSpeed speed)` method.

- [ ] **Step 3: Write failing test for animation speed persistence**

Read an existing settings test to match patterns, then add:

```csharp
// AppSettingsTests.cs:
[Fact]
public void AnimationSpeed_DefaultsToDefault()
{
    var settings = AppSettings.Default;
    Assert.Equal(AppSettings.FlyoutAnimationSpeed.Default, settings.AnimationSpeed);
}

[Fact]
public void AnimationSpeed_RoundTripsThroughJson()
{
    var original = AppSettings.Default with { AnimationSpeed = AppSettings.FlyoutAnimationSpeed.Slow };
    var json = JsonSerializer.Serialize(original);
    var deserialized = JsonSerializer.Deserialize<AppSettings>(json);
    Assert.Equal(AppSettings.FlyoutAnimationSpeed.Slow, deserialized!.AnimationSpeed);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenHab.Windows.sln --filter "FullyQualifiedName~AnimationSpeed" --configuration Release`
Expected: 2 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.App/Settings/AppSettings.cs src/OpenHab.App/Settings/AppSettingsController.cs tests/OpenHab.App.Tests/Settings/AppSettingsTests.cs
git commit -m "feat: add FlyoutAnimationSpeed setting with persistence"
```

---

### Task 2: Extract Composition Animation Helpers

**Files:**
- Create: `src/OpenHab.Windows.Tray/Rendering/CompositionAnimationHelper.cs`

- [ ] **Step 1: Create the helper file with easing factory and animation builders**

```csharp
// CompositionAnimationHelper.cs — new file in src/OpenHab.Windows.Tray/Rendering/
using Microsoft.UI.Composition;
using System.Numerics;

namespace OpenHab.Windows.Tray.Rendering;

internal static class CompositionAnimationHelper
{
    /// <summary>
    /// Creates a CubicBezier easing that approximates WPF's CubicEase EaseOut.
    /// Control points: (0, 0) → (0.215, 0.61) → (0.355, 1) → (1, 1)
    /// </summary>
    public static CubicBezierEasingFunction CreateEaseOut(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.215f, 0.61f),
            new Vector2(0.355f, 1.0f));
    }

    /// <summary>
    /// Creates a CubicBezier easing that approximates WPF's CubicEase EaseIn.
    /// Control points: (0, 0) → (0.645, 0) → (0.785, 0.39) → (1, 1)
    /// </summary>
    public static CubicBezierEasingFunction CreateEaseIn(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.645f, 0.0f),
            new Vector2(0.785f, 0.39f));
    }

    /// <summary>
    /// Builds a scalar keyframe animation with easing at the final keyframe.
    /// </summary>
    public static ScalarKeyFrameAnimation BuildScalarEntrance(
        Compositor compositor,
        float from,
        float to,
        TimeSpan duration)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseOut(compositor));
        return animation;
    }

    /// <summary>
    /// Builds a scalar keyframe animation with EaseIn for exit transitions.
    /// </summary>
    public static ScalarKeyFrameAnimation BuildScalarExit(
        Compositor compositor,
        float from,
        float to,
        TimeSpan duration)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseIn(compositor));
        return animation;
    }

    /// <summary>
    /// Builds a Vector3 keyframe animation for offset transitions with EaseOut.
    /// </summary>
    public static Vector3KeyFrameAnimation BuildOffsetEntrance(
        Compositor compositor,
        Vector3 from,
        Vector3 to,
        TimeSpan duration)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseOut(compositor));
        return animation;
    }

    /// <summary>
    /// Builds a Vector3 keyframe animation for offset transitions with EaseIn.
    /// </summary>
    public static Vector3KeyFrameAnimation BuildOffsetExit(
        Compositor compositor,
        Vector3 from,
        Vector3 to,
        TimeSpan duration)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, CreateEaseIn(compositor));
        return animation;
    }

    /// <summary>
    /// Returns a zero duration when animation is disabled, or the configured duration.
    /// </summary>
    public static TimeSpan ResolveDuration(int configuredMs) =>
        configuredMs <= 0 ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(configuredMs);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Commit**

```bash
git add src/OpenHab.Windows.Tray/Rendering/CompositionAnimationHelper.cs
git commit -m "feat: add extracted composition animation easing helpers"
```

---

### Task 3: Upgrade Entrance Animation (Easing + Scale + Flicker Fix)

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` (lines 421-461 `AnimateFlyoutEntranceAsync`)
- Modify: `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs` (pre-position fix)

- [ ] **Step 1: Rewrite `AnimateFlyoutEntranceAsync()` to use easing, scale, and ScopedBatch**

Replace the current `AnimateFlyoutEntranceAsync()` method (lines 421-461) with:

```csharp
private async Task AnimateFlyoutEntranceAsync()
{
    if (isEntranceAnimationRunning) return;
    if (Content is not UIElement contentRoot) return;

    isEntranceAnimationRunning = true;
    var visual = ElementCompositionPreview.GetElementVisual(contentRoot);
    var compositor = visual.Compositor;
    var duration = CompositionAnimationHelper.ResolveDuration(
        settingsController.GetFlyoutAnimationDurationMs());

    try
    {
        // Pre-position: hidden, slightly below final position, slightly scaled down
        visual.Opacity = 0f;
        visual.Offset = new Vector3(0f, 12f, 0f);
        visual.Scale = new Vector3(0.97f, 0.97f, 1f);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        // Opacity: 0 → 1 with EaseOut (slightly faster than offset for responsive feel)
        var opacityAnim = CompositionAnimationHelper.BuildScalarEntrance(
            compositor, 0f, 1f, duration);
        visual.StartAnimation(nameof(visual.Opacity), opacityAnim);

        // Offset: Y=12 → Y=0 with EaseOut
        var offsetAnim = CompositionAnimationHelper.BuildOffsetEntrance(
            compositor,
            new Vector3(0f, 12f, 0f),
            Vector3.Zero,
            duration);
        visual.StartAnimation(nameof(visual.Offset), offsetAnim);

        // Scale: 0.97 → 1.0 with EaseOut (the subtle "Fluent pop")
        var scaleAnim = CompositionAnimationHelper.BuildScalarEntrance(
            compositor, 0.97f, 1f, duration);
        visual.StartAnimation("Scale.X", scaleAnim);
        visual.StartAnimation("Scale.Y", scaleAnim);

        batch.End();

        // Wait for batch completion instead of Task.Delay
        var tcs = new TaskCompletionSource<bool>();
        batch.Completed += (_, _) => tcs.TrySetResult(true);
        await tcs.Task;
    }
    finally
    {
        // Snap to final values to prevent sub-pixel drift
        visual.Opacity = 1f;
        visual.Offset = Vector3.Zero;
        visual.Scale = new Vector3(1f, 1f, 1f);
        isEntranceAnimationRunning = false;
    }
}
```

- [ ] **Step 2: Add the `using OpenHab.Windows.Tray.Rendering;` import** at the top of `FlyoutWindow.xaml.cs`

- [ ] **Step 3: Pre-hide visual before positioning (flicker fix)**

In `TrayFlyoutPositioner.PlaceNearTrayArea()`, the flyout currently gets positioned via `AppWindow.MoveAndResize()` and then activated. The flyout's internals may be briefly visible before the animation runs. To fix this:

In `App.xaml.cs`, in the `TrayShellSurface.Flyout` case of `ApplyShellStateAsync()`, ensure the flyout content visual is set to zero opacity BEFORE calling `MoveAndResize()`. Add a `PrepareForHideVisual()` method:

```csharp
// FlyoutWindow.xaml.cs — new method:
public void PrepareForHideVisual()
{
    if (Content is UIElement contentRoot)
    {
        var visual = ElementCompositionPreview.GetElementVisual(contentRoot);
        visual.Opacity = 0f;
        visual.Offset = Vector3.Zero;
        visual.Scale = new Vector3(1f, 1f, 1f);
    }
}
```

In `App.xaml.cs`, update the flyout show case:

```csharp
// App.xaml.cs — TrayShellSurface.Flyout case, BEFORE positioning:
flyout.PrepareForHideVisual();  // hide visual before repositioning
TrayFlyoutPositioner.PlaceNearTrayArea(flyout, width);
flyout.PrepareForShowAnimation();
flyout.Activate();
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.Windows.Tray/App.xaml.cs src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs
git commit -m "feat: upgrade flyout entrance animation with easing, scale, and flicker fix"
```

---

### Task 4: Add Exit Animation

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` (new `AnimateFlyoutExitAsync` method + refactor deactivation handler)
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs` (wire exit flow)

- [ ] **Step 1: Add `AnimateFlyoutExitAndHideAsync()` to FlyoutWindow**

```csharp
// FlyoutWindow.xaml.cs — new method:
private bool isExitAnimationRunning;

public async Task AnimateFlyoutExitAndHideAsync()
{
    if (isExitAnimationRunning) return;
    if (Content is not UIElement contentRoot) return;

    isExitAnimationRunning = true;
    var visual = ElementCompositionPreview.GetElementVisual(contentRoot);
    var compositor = visual.Compositor;
    var duration = CompositionAnimationHelper.ResolveDuration(
        settingsController.GetFlyoutAnimationDurationMs());

    // If animation disabled, hide instantly
    if (duration.TotalMilliseconds <= 1)
    {
        AppWindow.Hide();
        isExitAnimationRunning = false;
        return;
    }

    try
    {
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        // Opacity: current → 0 with EaseIn
        var opacityAnim = CompositionAnimationHelper.BuildScalarExit(
            compositor, 1f, 0f, duration);
        visual.StartAnimation(nameof(visual.Opacity), opacityAnim);

        // Offset: slide down 12px with EaseIn
        var offsetAnim = CompositionAnimationHelper.BuildOffsetExit(
            compositor,
            visual.Offset,
            new Vector3(0f, 12f, 0f),
            duration);
        visual.StartAnimation(nameof(visual.Offset), offsetAnim);

        // Scale: slight shrink with EaseIn
        var scaleAnim = CompositionAnimationHelper.BuildScalarExit(
            compositor, 1f, 0.97f, duration);
        visual.StartAnimation("Scale.X", scaleAnim);
        visual.StartAnimation("Scale.Y", scaleAnim);

        batch.End();

        var tcs = new TaskCompletionSource<bool>();
        batch.Completed += (_, _) => tcs.TrySetResult(true);
        await tcs.Task;

        // Only hide after animation completes
        AppWindow.Hide();
    }
    finally
    {
        // Reset visual state for next show
        visual.Opacity = 0f;
        visual.Offset = Vector3.Zero;
        visual.Scale = new Vector3(1f, 1f, 1f);
        isExitAnimationRunning = false;
    }
}
```

- [ ] **Step 2: Refactor deactivation handler to use exit animation**

Change `OnWindowActivated` to call the animated exit instead of immediate hide:

```csharp
// FlyoutWindow.xaml.cs — modify OnWindowActivated:
private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
{
    if (args.WindowActivationState == WindowActivationState.Deactivated)
    {
        if (suppressNextDeactivationHide)
        {
            suppressNextDeactivationHide = false;
            return;
        }

        // Run exit animation before hiding
        await AnimateFlyoutExitAndHideAsync();
        requestHideFlyout();
        return;
    }

    // ... rest stays the same
}
```

**Important:** The `requestHideFlyout()` call is now AFTER the exit animation completes. This means `shellController.HandleWindowCloseRequested(Flyout)` runs after the animation. The `App.xaml.cs` `ApplyShellStateAsync()` will then call `flyout.AppWindow.Hide()`, but the window is already hidden from `AnimateFlyoutExitAndHideAsync()`. Add a guard in `ApplyShellStateAsync()`:

```csharp
// App.xaml.cs — default case and flyout-hide, add null/visibility check:
default:
    main.AppWindow.Hide();
    if (flyout.isExitAnimationRunning) break;  // already hiding
    flyout.AppWindow.Hide();
    break;
```

Alternatively, make `ApplyShellStateAsync()` in the `None` case skip `AppWindow.Hide()` when the exit animation has already hidden the window. Simpler approach: check if flyout's `AppWindow.IsVisible` is already false before calling `Hide()`.

```csharp
default:
    main.AppWindow.Hide();
    if (flyout.AppWindow.IsVisible)
        flyout.AppWindow.Hide();
    break;
```

This is simpler and doesn't require exposing internal state.

- [ ] **Step 3: Prevent double-hide on deactivation + close request race**

The flyout `AppWindow.Closing` handler also calls `requestHideFlyout()`. Since the exit animation now hides the window, `Closing` will fire AFTER the hide. Add a guard:

```csharp
// In App.xaml.cs, the flyoutWindow.AppWindow.Closing handler:
flyoutWindow.AppWindow.Closing += (sender, args) =>
{
    // If the window is already hidden (by exit animation), just cancel
    if (!flyoutWindow.AppWindow.IsVisible)
    {
        args.Cancel = true;
        return;
    }
    args.Cancel = true;
    shellController.HandleWindowCloseRequested(TrayShellSurface.Flyout);
    _ = ApplyShellStateAsync();
};
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "feat: add eased flyout exit animation with fade+slide+scale"
```

---

### Task 5: LSP Diagnostics and Solution Build

**Files:**
- All modified files

- [ ] **Step 1: Run LSP diagnostics on all changed files**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Run full test suite**

Run: `dotnet test OpenHab.Windows.sln --configuration Release`
Expected: All tests pass (119+ tests, 0 failures)

- [ ] **Step 3: Write status doc**

Create `docs/superpowers/status/2026-05-07-openhab-windows-flyout-animation-status.md` with:

```markdown
# openHAB Windows Flyout Animation Status

Date: 2026-05-07

## Source Documents
- Implementation plan: docs/superpowers/plans/2026-05-07-openhab-windows-flyout-animation.md
- Reference: FluentFlyout (https://github.com/unchihugo/FluentFlyout)
- Prior status: docs/superpowers/status/2026-05-06-openhab-windows-ui-polish-status.md

## Completed

### Animation Engine
- Extracted `CompositionAnimationHelper` with CubicBezier easing (EaseOut/EaseIn), reusable scalar and Vector3 animation builders.
- Entrance animation upgraded: Opacity (0→1), Offset Y (12→0), Scale (0.97→1.0) — all with EaseOut cubic bezier.
- Exit animation added: Opacity (1→0), Offset Y (0→12), Scale (1.0→0.97) — all with EaseIn cubic bezier.
- `CompositionScopedBatch` used for animation completion instead of `Task.Delay`.
- Pre-hide visual before positioning to eliminate flicker.

### Settings
- `FlyoutAnimationSpeed` enum added: Off (0ms), Fast (150ms), Default (300ms), Slow (450ms).
- Persisted to settings.json; defaults to Default (300ms).

### Architecture
- Animation logic stays in `OpenHab.Windows.Tray` layer only.
- `App.xaml.cs` shell state machine handles exit animation via `AnimateFlyoutExitAndHideAsync()`.
- `TrayFlyoutPositioner` unchanged (positioning is static, animation is visual-only).

## Verification
- `dotnet build OpenHab.Windows.sln --configuration Release`: passed. 0 warnings, 0 errors.
- `dotnet test OpenHab.Windows.sln --configuration Release`: passed. [N] tests run, 0 failed.

## Branch
All changes on `feature/flyout-animation`.

## Still Out Of Scope
- Per-element staggered entrance animations (individual sitemap rows).
- Acrylic/Mica backdrop via composition API (still using DWM TransientWindow).
- Multi-monitor aware positioning (still primary monitor only).
- Re-show animation (only first activation animates; re-show after hide is instant).
```

- [ ] **Step 4: Commit status doc**

```bash
git add docs/superpowers/status/2026-05-07-openhab-windows-flyout-animation-status.md
git commit -m "docs: add flyout animation status doc"
```

---

### Task 6: Full Solution Verification

**Files:**
- All

- [ ] **Step 1: Clean build**

```bash
dotnet clean OpenHab.Windows.sln --configuration Release && dotnet build OpenHab.Windows.sln --configuration Release
```
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Run all tests**

```bash
dotnet test OpenHab.Windows.sln --configuration Release
```
Expected: All tests pass, 0 failures

- [ ] **Step 3: Verify no regressions in adjacent features**

Check that:
- Tray icon toggle (click to show/hide flyout) still works
- "Open main window" from flyout still works (flyout dismisses, main opens)
- Flyout deactivation (click outside) triggers exit animation then hide
- Flyout refresh button still works
- Settings persist across app restarts

- [ ] **Step 4: Commit**

```bash
git commit -m "chore: full solution verification after flyout animation changes"
```
