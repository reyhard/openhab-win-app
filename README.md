# openHAB Windows Companion App

This repository contains a Windows companion app for openHAB. The current product direction is a Windows 11 tray app with a compact flyout, a larger main window, embedded openHAB Main UI, and native sitemap rendering for Windows-specific workflows.

## Current Status

This app is under active development. It is not yet an official release-ready openHAB distribution.

Known release-readiness items still need maintainer decisions or follow-up implementation:

- Official package identity and signing ownership.
- Microsoft Store or other distribution ownership.
- Localization and broader accessibility review.
- Full dependency/license review beyond the initial `NOTICE` file.
- Manual smoke and memory/performance verification for the Windows tray, flyout, main window, notifications, and shortcut workflows.

## Features

- Windows tray app with compact flyout.
- Main window with embedded openHAB Main UI through WebView2.
- Optional native sitemap pane backed by the shared sitemap/runtime/rendering pipeline.
- Settings, notifications, startup integration, sitemap navigation, and shortcut command menu work.
- Local app state and diagnostics under `%LocalAppData%\OpenHab.WinApp`.

## Repository Layout

- `src/OpenHab.Core` - endpoint selection, server profiles, HTTP client, credentials, diagnostics, event streams, and device-state mapping.
- `src/OpenHab.Sitemaps` - sitemap models, parsing, normalization, and navigation intents.
- `src/OpenHab.Rendering` - skin-neutral render descriptors and sitemap skin mapping.
- `src/OpenHab.App` - UI-independent settings, runtime controllers, notifications, shell state, and shortcuts.
- `src/OpenHab.Windows.Tray` - WinUI/Windows App SDK shell, tray icon, flyout, main window, settings UI, and Windows-specific rendering.
- `src/OpenHab.Windows.Notifications` - Windows toast notification integration.
- `src/OpenHab.Windows.Package` - MSIX packaging project.
- `tests` - xUnit coverage for core, sitemap, rendering, and app/runtime behavior.
- `docs/superpowers` - design, plan, status, and verification notes used during development.

## Requirements

- Windows 11 for the intended desktop experience.
- .NET SDK version from `global.json`.
- Visual Studio with MSBuild and MSIX/DesktopBridge tooling for package builds.
- WebView2 Runtime for embedded Main UI.

## Build And Test

Everyday direct test gate:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

The direct test projects are the normal logic gate. The package project has DesktopBridge/MSIX prerequisites, so use the package build script for release/package validation.

Build the tray app:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Build the MSIX package when Visual Studio MSIX/DesktopBridge targets are installed:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

The package project imports DesktopBridge targets that are not always available through standalone .NET SDK MSBuild. Use `build-package.ps1` for package builds because it locates Visual Studio MSBuild and verifies the required DesktopBridge props file.

## CI And Static Analysis

GitHub Actions workflows live under `.github/workflows`:

- `ci.yml` runs the direct test projects and Release tray build.
- `codeql.yml` runs GitHub CodeQL analysis for C#.
- `sonarcloud.yml` runs SonarCloud analysis for the tray project dependency graph.
- `release-msix.yml` builds and publishes signed MSIX release artifacts.

To enable SonarCloud, create a SonarCloud project for this repository and configure these GitHub repository settings:

- Repository variables:
  - `SONAR_ORGANIZATION`
  - `SONAR_PROJECT_KEY`
- Repository secret:
  - `SONAR_TOKEN`

The SonarCloud workflow skips pull requests from forks because repository secrets are not available to untrusted fork workflows. It builds `src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj` under the SonarScanner for .NET instead of the full solution, because the MSIX packaging project requires DesktopBridge tooling that is validated separately by the package workflows.

## Runtime Data

Runtime logs and app state are written under:

```text
%LocalAppData%\OpenHab.WinApp
```

Useful files include:

- `diagnostics.log`
- `task-crash.log`
- `settings.json`
- `notifications.json`

Do not post full logs publicly if they can include endpoint URLs, item names, notification payloads, credentials, tokens, or other private information.

## Packaging And Signing

Release signing is not finalized. Official distribution must use signing certificates, package identity, and release infrastructure owned by the appropriate openHAB maintainers.

Local temporary signing files and package output must not be committed.

## Contributing

See `CONTRIBUTING.md`.

## Security

See `SECURITY.md`.

## License

This project is licensed under the Eclipse Public License 2.0. See `LICENSE`.
