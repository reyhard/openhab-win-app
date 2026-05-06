# openHAB Windows UI Polish & Feature Completion Status

Date: 2026-05-06

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Design notes: `.docs/notes.md`
- UI polish plan: `docs/superpowers/plans/2026-05-06-openhab-windows-ui-polish.md`
- Bugfix plan: `docs/superpowers/plans/2026-05-06-openhab-windows-ui-bugfixes.md`
- Prior status: `docs/superpowers/status/2026-05-05-openhab-windows-tray-shell-behavior-status.md`

## Completed

### Sitemap Discovery & Selection

- Added `GET /rest/sitemaps` API endpoint (`IOpenHabClient.GetSitemapsAsync()`).
- Replaced manual TextBox sitemap entry with a server-populated ComboBox in both FlyoutWindow and MainWindow.
- Replaced visible ComboBox in flyout with a clean `MenuFlyout` triggered by tapping the title — matches compact tray-flyout UX.
- Auto-discovers the server's default sitemap on first launch when the configured name doesn't match any available sitemap.
- Extracted shared `SitemapComboHelper` to eliminate DRY duplication between FlyoutWindow and MainWindow.
- Robust JSON parsing: `TryGetProperty` fallback for missing `name`/`label` fields; programmatic `SelectedItem` during population no longer triggers redundant reloads.

### Widget Rendering

- **State transforms**: `SitemapRowMapper.ToRow` now applies mapping-based state transformation (e.g., raw state `"OPEN"` → display label `"Unlocked"`). 3 unit tests cover match, no-match, and no-mappings scenarios.
- **Toggle layout**: Restructured to horizontal row with label on left, state text (`ON`/`OFF`) in center, and `ToggleSwitch` on the right — matches the `[icon] [label] [widget]` design from reference mockups.
- **Selection widget**: Replaced disabled `Button` with interactive `ComboBox` populated from sitemap mappings. Sends selected command via `SendCommandForRowAsync`. Selection compares against `RawState` (untransformed) to avoid mismatch with mapped display states.
- **Slider/Setpoint**: Enabled interactive `Slider` control with `ValueChanged` wired to command sending. Removed `IsEnabled = false`.
- **Submenu navigation**: Navigable widgets (`CanNavigate → Navigate` action) are rendered with a chevron (`>`) indicator and wrapped in a clickable `Button`. Navigation pushes the current page onto a back-stack; a back button (`←`) enables returning to the previous level.

### Icons

- Parsed `icon` field from sitemap widget JSON through the full pipeline: parser → model → normalizer → render descriptor → UI.
- Icons rendered as 20×20 `Image` elements (server-fetched PNG) in a 24px grid column.
- `UseWindows11Icons` setting wired end-to-end — when enabled, maps 60+ openHAB icon/category names to Segoe Fluent glyphs (`FontIcon`), falling back to server icons when no mapping exists.
- Icon URL uses `/icon/{name}` format (no `.png` extension) for openHAB server compatibility.

### Flyout Polish

- Flyout width increased from 420px to 500px.
- Flyout anchored to bottom-right of primary working area (taskbar-adjacent), replacing cursor-based positioning.
- Slide-up animation on first activation (ease-out cubic, ~96ms).
- Footer buttons replaced with Segoe Fluent `FontIcon` glyphs: Refresh (`↻`), Open main window (`⬈`), Settings (`⚙`).
- Back button (`←`) added for sitemap page navigation.

### Settings

- `FollowSystemTheme` toggle added to settings (WinUI already follows system theme natively; toggle provides explicit control for future use).
- `UseWindows11Icons` toggle added — controls whether sitemap icons use Segoe Fluent glyphs or server-fetched images.
- Both settings persisted to `settings.json`.

### Code Quality

- Extracted `SitemapComboHelper.Populate()` to eliminate duplicated ComboBox/MenuFlyout population logic.
- Extracted `SitemapComboHelper` shared helper for sitemap dropdown population.
- Removed dead `activateRow` parameters from `CreateSlider` and `CreateSelection`.
- `NavigateToChildAsync` reverts to embedded child page data (openHAB returns linked pages inline in sitemap JSON).
- `SitemapRowDescriptor` gained `RawState` (untransformed) to keep selection matching correct after state transforms.
- `.dotnet-main/` and `.sisyphus/` added to `.gitignore`.

## Verification

- `dotnet build OpenHab.Windows.sln --configuration Release`: passed. `0` warnings, `0` errors.
- `dotnet test OpenHab.Windows.sln --configuration Release`: passed. `119` tests run, `119` passed, `0` failed, `0` skipped.

### Test breakdown

| Project | Tests | Status |
|---------|-------|--------|
| OpenHab.Core.Tests | 29 | All pass |
| OpenHab.App.Tests | 45 | All pass |
| OpenHab.Sitemaps.Tests | 32 | All pass |
| OpenHab.Rendering.Tests | 13 | All pass |
| **Total** | **119** | **0 failed** |

### Tests added in this slice

| Area | Tests |
|------|-------|
| Sitemap state transforms (match, no-match, no-mappings) | 3 |
| Sitemap icon parsing (present, absent) | 2 |
| **Total added** | **5** |

## Branch

All changes on `feature/ui-polish` (14 commits ahead of `main`).

## Still Out Of Scope

- Live event stream updates for real-time state changes.
- Offline cache persistence for sitemap pages and icons.
- WebView/Main UI fallback for unsupported widgets.
- Cross-sitemap navigation (child pages in different sitemaps).
- Startup-with-Windows and MSIX packaging.
- Device state reporting (battery, lock, session).
