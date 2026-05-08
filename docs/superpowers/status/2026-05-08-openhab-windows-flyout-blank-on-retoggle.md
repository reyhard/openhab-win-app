# Issue: Flyout Shows Blank White Window on Rapid Tray Toggle

**Branch**: `main` (commits `0a6b3d0`, `e75aab6`, `cde2f01`)

## Repro

1. Click tray icon → flyout shows (entrance animation plays, content visible)
2. Click tray icon again → flyout hides (should play exit animation)
3. Click tray icon again → **flyout shows but content is blank white**

Happens when steps 2→3 occur faster than the exit animation duration (300ms by default). Not 100% reproducible — timing-dependent race.

## Root Cause

When `flyout.AppWindow.Hide()` is called from `ApplyShellStateAsync()` (None case, triggered by tray toggle):

1. Window deactivation fires `OnWindowActivated(Deactivated)` → spawns `AnimateFlyoutExitAndHideAsync()` (300ms exit animation)
2. If user re-shows before exit completes, entrance animation starts
3. Exit animation's `finally` block eventually fires → sets `visual.Opacity = 0f` **over** the running entrance animation → content invisible
4. Exit animation also calls `AppWindow.Hide()` after the window was already re-shown

## Fix Attempts (3 commits)

| Commit | Approach | Result |
|--------|----------|--------|
| `0a6b3d0` | `CancelRunningAnimations()` resets anim flags before hide | Partial — cleared stale `isEntranceAnimationRunning` but didn't address exit race |
| `e75aab6` | `SuppressNextDeactivation()` prevents deactivation handler from spawning exit anim | Broke user-visible dismiss animation on tray toggle |
| `cde2f01` | Entrance anim cancels exit by setting `isExitAnimationRunning=false`; exit checks flag before `Hide()`/visual reset | Should fix — exit plays visual effect but cancels silently if entrance starts |

## Verdict

**Status**: Pending user verification in fresh session.
