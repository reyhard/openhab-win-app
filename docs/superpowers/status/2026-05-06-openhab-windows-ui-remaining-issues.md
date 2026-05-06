# openHAB Windows UI Remaining Issues

Date: 2026-05-06

## Context

This file captures the remaining UI status after the latest `ui-icon-polish` work in:

- Worktree: `D:\Source\Openhab\openhab-win-app\.worktrees\ui-icon-polish`
- Branch: `feature/ui-icon-polish`

The toggle alignment issue was resolved during the follow-up session. Dynamic icon behavior remains a later-phase follow-up.

## What Was Completed

- Toggle rows no longer show the extra localized state text on the right (`Włączone/Wyłączone`).
- Toggle rows keep `ON/OFF` text on the left side of the toggle.
- Toggle rows now use the same right-edge alignment model as other sitemap rows:
  - shared row layout with label, value, and control lanes;
  - `ON/OFF` in the value lane;
  - `ToggleSwitch` alone in a compact control lane;
  - no combined fixed-width toggle cluster.
- Toggle `OFF/ON` text no longer clips in grouped/nested sections.
- Toggle spacing was tightened after visual review by matching the control lane width to the explicit toggle width.
- Win11 icon mapping was improved for common switch/light categories.
- Win11 icon mapping now recognizes additional stateful/common aliases:
  - `lighton`, `lightoff`, `lightson`, `lightsoff`
  - `switchon`, `switchoff`
  - `poweron`, `poweroff`
- Added regression tests around the toggle layout contract:
  - `ToggleRows_DoNotUseCombinedFixedClusterWidth`
  - `ToggleRows_UseCompactControlLaneNextToStateText`
- Full solution build/test passed after the latest code changes.

## Current Verification State

Latest successful verification in this worktree:

- `dotnet test OpenHab.Windows.sln --configuration Release` ✅
- `dotnet build OpenHab.Windows.sln --configuration Release` ✅

Latest counts:

- Tests: 184 passed, 0 failed, 0 skipped.
- Build: 0 warnings, 0 errors.

Note: during verification, the running tray app had to be stopped because it locked `OpenHab.Windows.Tray.exe` in `bin\Release`.

## Remaining Issues

### 1. Dynamic openHAB icon behavior is still a later-phase problem

The current work only improved Win11 mappings.

Still unresolved by design:

- when `Use Windows 11 icons` is **disabled**, default/dynamic openHAB icon loading still needs deeper investigation;
- when `Use Windows 11 icons` is **enabled**, true state-sensitive Win11 icon behavior is not fully implemented yet — only better static/state-name mappings were added.

## What Was Tried For Toggle Alignment

Status: resolved by Attempt D.

### Attempt A: auto-sized trailing cluster

Approach:

- `label` in star column
- `OFF/ON` in auto column
- `ToggleSwitch` in auto column

Result:

- looked better than the broken fixed-width version,
- but still visually offset compared with neighboring control rows.

### Attempt B: fixed-width trailing control zone (first try)

Approach:

- force toggle rows into the same kind of fixed trailing control area as sliders/selections,
- right-align the `OFF/ON + toggle` cluster inside it.

Result:

- looked worse,
- toggle became clipped,
- user explicitly reported regression.

### Attempt C: wider fixed-width trailing control zone with nested grid

Approach:

- use a wider fixed trailing zone,
- use nested grid for `OFF/ON` + toggle instead of simple stack.

Result:

- still incorrect in grouped layouts,
- screenshot shows clipped `OFF` text (`FF`) and persistent alignment problems.

### Attempt D: shared value/control row lanes

Approach:

- replace the combined toggle cluster with a shared row layout helper;
- use stable columns for optional icon, label, value text, and control;
- put `ON/OFF` in the value lane;
- put the toggle alone in the compact control lane;
- set the control lane to the same width as the explicit toggle width.

Result:

- fixed the clipped `OFF` text in grouped/nested layouts;
- aligned toggle right edges consistently;
- user confirmed the visual result looked good.

## Resolved Root Cause

The toggle row layout was too ad-hoc relative to the rest of the row system.

Confirmed causes:

- Toggle rows did not share the same right-edge alignment model as text/value rows.
- Combined fixed-width assumptions were not stable across nested group/frame/scroll contexts.
- The `ToggleSwitch` template width plus custom `ON/OFF` text interacted badly with the chosen container width.

## Recommended Next Session Plan

Continue the icon work:

1. investigate default/dynamic openHAB icon loading when Win11 icons are disabled;
2. later add true state-sensitive Win11 icon behavior, not just extra aliases.

## Key Files

- `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- `src/OpenHab.Windows.Tray/MainWindow.xaml`

## Important User Intent To Preserve

- `ON/OFF` should stay **to the left of the toggle**.
- There should be **no extra localized text on the right side** of the toggle.
- Win11 icons should look better when enabled.
- Fallback / dynamic openHAB icon behavior still needs separate follow-up work.
