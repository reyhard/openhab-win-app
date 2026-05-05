# openHAB Windows Connected Homepage Status

Date: 2026-05-05

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- UI slice status: `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`
- Connected homepage plan: `docs/superpowers/plans/2026-05-05-openhab-windows-connected-homepage.md`

## Completed

- Added sitemap REST JSON parsing from the openHAB homepage payload into existing sitemap models.
- Added a connected homepage runtime that selects the configured endpoint mode, loads the configured sitemap, and surfaces connection status.
- Added a configurable sitemap name in app settings.
- Reused the existing skin renderer to display a live homepage instead of the sample page.
- Wired the WinUI tray surface to load, refresh, and display runtime errors and status.
- Added a first live command path for switch rows by sending the command and reloading the homepage.

## Verification

- `dotnet test OpenHab.Windows.sln`: passed. `79` tests run, `79` passed, `0` failed, `0` skipped.
- `dotnet build OpenHab.Windows.sln --configuration Release`: passed with `0` warnings and `0` errors.

## Still Out Of Scope

- Subpage navigation.
- Event stream live item updates.
- Persisted settings and credentials.
- Offline cache persistence.
- WebView/Main UI fallback routing.
- Notifications, telemetry sending, and packaging.
