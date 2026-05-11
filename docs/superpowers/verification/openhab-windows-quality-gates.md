# openHAB Windows Quality Gates

Date: 2026-05-11

## Direct Test Gate

Use this for everyday logic changes when the packaging project cannot load because DesktopBridge targets are unavailable.

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all direct test projects pass.

## Full Solution Gate

Use this before claiming release readiness or after package, manifest, Windows shell, or project-file changes.

```powershell
dotnet test OpenHab.Windows.sln
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: both commands pass when `Microsoft.DesktopBridge.props` is available under the installed .NET SDK or Visual Studio build targets.

## Known Packaging Prerequisite

`src\OpenHab.Windows.Package\OpenHab.Windows.Package.wapproj` imports DesktopBridge targets. If those targets are missing, direct test projects still provide useful logic coverage, but the full gate remains blocked by environment setup.
