# openHAB Windows Tray Shell Behavior Status

Date: 2026-05-06

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Auth & notifications status: `docs/superpowers/status/2026-05-05-openhab-windows-auth-notifications-status.md`
- Tray shell behavior plan: `docs/superpowers/plans/2026-05-05-openhab-windows-tray-shell-behavior.md`
- Visual reference: `.docs/mockup.png`

## Completed

- Added a testable tray shell state controller in `OpenHab.App` so tray/background lifecycle rules are not buried in WinUI code-behind.
- Split the app shell into a compact `FlyoutWindow` and a larger `MainWindow`.
- Changed tray behavior to single left-click flyout toggle plus explicit context-menu actions for `Open flyout`, `Open main window`, and `Exit`.
- Changed close-button behavior so closing the main window hides the app back to tray instead of exiting the process.
- Routed toast activation into the main-window path rather than the compact flyout.
- Added pragmatic tray-area placement for the flyout window.
- Updated the settings surface to match the intended grouping of local and cloud auth fields.

## Verification

- `dotnet build OpenHab.Windows.sln --configuration Release`: passed in `main`, `0` warnings, `0` errors.
- `dotnet test OpenHab.Windows.sln --configuration Release`: passed in `main`.
- `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release`: passed after the settings-order XAML fix.

### Test breakdown from the full solution run

| Project | Tests | Status |
|---------|-------|--------|
| OpenHab.Core.Tests | 29 | All pass |
| OpenHab.App.Tests | 45 | All pass |
| OpenHab.Sitemaps.Tests | 30 | All pass |
| OpenHab.Rendering.Tests | 10 | All pass |
| **Total** | **114** | **0 failed** |

### Focused tray-shell coverage

| Area | Tests |
|------|-------|
| Tray shell controller launch, flyout toggle, close-to-background, notification activation, exit | 6 |

## Manual Verification Notes

- Manual verification is partial, not complete.
- Confirmed from implementation and follow-up UI correction that the settings surface now groups cloud username and cloud password together after the local token field.
- A full Windows 11 behavior pass for launch, tray click, close-to-background, toast activation, and exit should still be done before calling the shell polish finished.

## Still Out Of Scope

- Dashboard cards/widgets matching the full mockup.
- Search/filter behavior inside the flyout.
- Live event stream updates.
- Subpage navigation.
- Offline cache persistence.
- WebView/Main UI fallback.
- Startup-with-Windows and packaging polish.
