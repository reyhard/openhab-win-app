# Contributing

Thanks for contributing to the openHAB Windows companion app.

This repository follows openHAB contribution expectations where they apply, adapted for a .NET/WinUI Windows app.

## Before You Start

- Discuss large features, architectural changes, packaging changes, and security-sensitive work before implementing them.
- Prefer issue-backed work and focused branches.
- Keep changes small enough to review.
- Update tests and documentation with behavior changes.

## Developer Certificate Of Origin

Commits must be signed off using the Developer Certificate of Origin.

Use:

```powershell
git commit -s -m "short summary"
```

The sign-off must use your real name and a reachable email address.

## Coding Guidelines

- Preserve the project split:
  - `OpenHab.Core` for openHAB access, credentials, diagnostics, profiles, event streams, and device-state mapping.
  - `OpenHab.Sitemaps` for sitemap parsing and runtime-neutral sitemap behavior.
  - `OpenHab.Rendering` for render descriptors and skin mapping.
  - `OpenHab.App` for UI-independent app/runtime behavior.
  - `OpenHab.Windows.Tray` for WinUI, tray, flyout, main window, and Windows-specific rendering.
  - `OpenHab.Windows.Notifications` for Windows toast integration.
- Do not push WinUI-specific concerns into lower layers.
- Reuse the existing sitemap normalizer, runtime controller, and rendering pipeline.
- Keep logs and user-visible errors privacy-safe.
- Do not add broad refactors to unrelated feature work.

## Sensitive Data

Do not commit:

- Credentials or tokens.
- Private endpoint URLs.
- Full diagnostic logs from a real installation.
- `.pfx` signing keys.
- `.user` project files.
- Package output under `AppPackages` or `BundleArtifacts`.

When adding diagnostics, assume server responses, URLs, item names, notification payloads, and exception messages can contain private information.

## Verification

Run the direct test gate for normal logic changes:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --blame-hang --blame-hang-timeout 30s
```

Run the tray build for UI or Windows-shell changes:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Run the package build for packaging, manifest, startup-task, notification activation, or signing changes:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

The full package build requires Visual Studio MSBuild with DesktopBridge/MSIX targets.

## Pull Requests

Pull requests should include:

- Clear summary of behavior changes.
- Tests run and their result.
- Known limitations or follow-up work.
- Screenshots only for UI-visible changes.
- Documentation updates when setup, packaging, commands, or user-visible behavior changes.

Do not hide failing tests. If a known infrastructure issue applies, call it out explicitly.
