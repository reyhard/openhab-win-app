# openHAB Windows App Test Host Shutdown Root-Cause Patch

Date: 2026-05-15

## Root Cause

`tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj` directly references `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj` so App tests can cover tray helper types.

`OpenHab.Windows.Tray.csproj` leaves `WindowsAppSdkBootstrapInitialize` enabled when `WindowsPackageType` is `None`, which is the unpackaged/debug default. When the tray assembly is built and loaded by the xUnit testhost, the generated Windows App SDK bootstrap initializer keeps the process alive even for pure helper tests that do not create WinUI objects.

## Reproducer

The smallest concrete reproducer is a single pure helper test:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-build --filter FullyQualifiedName~DwmWindowDecorationsTests.BuildRequestsForLightThemeDisablesImmersiveDarkMode --logger "console;verbosity=normal"
```

Observed before the fix: the testhost starts the test run and does not exit within 60 seconds.

Control validation:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DwmWindowDecorationsTests.BuildRequestsForLightThemeDisablesImmersiveDarkMode --logger "console;verbosity=normal" -p:WindowsAppSdkBootstrapInitialize=false
```

Observed: the test passes and the process exits cleanly.

## Patch

Modify the `OpenHab.Windows.Tray` `ProjectReference` in `tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj` to pass:

```xml
AdditionalProperties="WindowsAppSdkBootstrapInitialize=false"
```

This disables the generated bootstrap initializer only for the tray assembly built as an App test dependency. The normal tray app project remains unchanged for direct Debug, Release, and package builds.

Two test-only follow-up failures were exposed by that narrower test dependency build:

- `SitemapControlFactory` and `OpenHabIconImageSourceLoader` used `Microsoft.UI.ColorHelper.FromArgb` in pure parsing/color helpers. With WinUI bootstrap disabled for App tests, these helpers must construct `Windows.UI.Color` directly through `Windows.UI.Color.FromArgb`.
- `SitemapRuntimeController.StartSitemapEventStreamAsync` could return as a same-page duplicate no-op before event handlers were attached, because `LoadAsync` may have already marked the stream as started through a background start. Attach sitemap event handlers before duplicate-start detection so widget events are not dropped.

## Verification

Run the smallest reproducer without the command-line property:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DwmWindowDecorationsTests.BuildRequestsForLightThemeDisablesImmersiveDarkMode --logger "console;verbosity=normal"
```

Expected: the test passes and the process exits cleanly.

Run the six known suspect filters without blame-hang:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapControlFactoryTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DwmWindowDecorationsTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowPlannerTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapSurfaceRendererTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapInputNormalizationTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DispatcherRefreshGateTests
```

Expected: each filtered run passes and exits cleanly.

Run full App tests:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: the test run passes and the process exits cleanly.

Observed after the patch on 2026-05-15:

- Passed and exited cleanly: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DwmWindowDecorationsTests.BuildRequestsForLightThemeDisablesImmersiveDarkMode --logger "console;verbosity=normal"` (`1/1`).
- Passed and exited cleanly: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapControlFactoryTests` (`76/76`).
- Passed and exited cleanly: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DwmWindowDecorationsTests` (`4/4`).
- Passed and exited cleanly: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowPlannerTests` (`9/9`).
- Passed and exited cleanly: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapSurfaceRendererTests` (`2/2`).
- Passed and exited cleanly after replacing WinUI `ColorHelper.FromArgb` in pure color helpers: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapInputNormalizationTests` (`7/7`).
- Passed and exited cleanly: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DispatcherRefreshGateTests` (`3/3`).
- Passed and exited cleanly after attaching sitemap event handlers before duplicate-start detection: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal"` (`457/457`).
