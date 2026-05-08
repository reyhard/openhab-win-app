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
- `SetAnimationSpeed()` method added to `AppSettingsController`.

### Architecture
- Animation logic stays in `OpenHab.Windows.Tray` layer only.
- `App.xaml.cs` shell state machine handles exit animation via `AnimateFlyoutExitAndHideAsync()`.
- `TrayFlyoutPositioner` unchanged (positioning is static, animation is visual-only).

## Verification
- `dotnet build OpenHab.Windows.sln --configuration Release`: passed. 0 warnings, 0 errors.
- `dotnet test OpenHab.Windows.sln --configuration Release`: passed. 246 tests run, 0 failed.

## Branch
All changes on `feature/flyout-animation`.

## Still Out Of Scope
- Per-element staggered entrance animations (individual sitemap rows).
- Acrylic/Mica backdrop via composition API (still using DWM TransientWindow).
- Multi-monitor aware positioning (still primary monitor only).
- Re-show animation (only first activation animates; re-show after hide is instant).
