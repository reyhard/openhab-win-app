# App Test Host Shutdown Investigation (Task 6)

## Reproduction

Command:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --blame-hang --blame-hang-timeout 30s
```

Result:

- Build/test execution succeeded far enough to run the suite.
- `341` tests executed; `336` passed; `5` failed (all in `SitemapRuntimeControllerTests`).
- After failures, blame-hang timed out at `30s` and aborted test host.
- Hang dump + sequence files were produced under:
  - `tests/OpenHab.App.Tests/TestResults/70dc1acc-c988-4cf5-9818-c2cb9fa57c63/`
- Broader active-test set from the blame-hang sequence file at host abort:
  - `OpenHabIconImageSourceLoaderTests.BuildPayloadCacheKey_DoesNotIncludeVisualDimensions`
  - `DwmWindowDecorationsTests.BuildRequestsForLightThemeDisablesImmersiveDarkMode`
  - `SitemapRowPlannerTests.VisualRowsPreserveHiddenButtonGridChildrenWhenNoVisibleChildExists`
  - `DispatcherRefreshGateTests.DrainRunsOnePendingRefresh`
  - `SitemapInputNormalizationTests.NormalizeInputByHint_NumberWithLeadingZeros_IsPreserved`
  - `NotificationsPageControlTests.NotificationServerIcons_KeepCompactInterfaceSize`
  - `SitemapControlFactoryTests.CanResolveNormalizedIcon_ReturnsFalse_ForUnknown`
  - `SitemapSurfaceRendererTests.PartialRowUpdate_DoesNotSnapVisibilityAfterUpdateState`
  - `NotificationPollerTests.ShouldReconfigureNotificationPolling_ReturnsTrueWhenActiveConfigMissing`

## Filter Results for the Six Groups

All runs used:

```powershell
--blame-hang --blame-hang-timeout 30s
```

### 1) SitemapControlFactoryTests

Command:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapControlFactoryTests --blame-hang --blame-hang-timeout 30s
```

Result: blame-hang abort; active test at crash:

- `SitemapControlFactoryTests.CanResolveNormalizedIcon_ReturnsFalse_ForUnknown`

Artifacts:

- `tests/OpenHab.App.Tests/TestResults/5de8ebdb-ae57-41a6-87a0-15946a7d13ef/...`

### 2) DwmWindowDecorationsTests

First attempt with `--no-restore` hit transient WinUI build lock (`obj\...\input.json` in `OpenHab.Windows.Tray`).
Per task instruction, retried once pattern and then used `--no-build` based on fresh prior successful builds.

Command used for result:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-build --filter FullyQualifiedName~DwmWindowDecorationsTests --blame-hang --blame-hang-timeout 30s
```

Result: blame-hang abort; active test at crash:

- `DwmWindowDecorationsTests.BuildRequestsForLightThemeDisablesImmersiveDarkMode`

Artifacts:

- `tests/OpenHab.App.Tests/TestResults/223f8b9a-ebc8-48ea-999d-a02bf34407d4/...`

### 3) SitemapRowPlannerTests

Command:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowPlannerTests --blame-hang --blame-hang-timeout 30s
```

Result: blame-hang abort; active test at crash:

- `SitemapRowPlannerTests.VisualRowsPreserveHiddenButtonGridChildrenWhenNoVisibleChildExists`

Artifacts:

- `tests/OpenHab.App.Tests/TestResults/46c4c1ab-3d5f-4806-b66d-f35e86a478c6/...`

### 4) SitemapSurfaceRendererTests

Command:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapSurfaceRendererTests --blame-hang --blame-hang-timeout 30s
```

Result: blame-hang abort; active test at crash:

- `SitemapSurfaceRendererTests.PartialRowUpdate_DoesNotSnapVisibilityAfterUpdateState`

Artifacts:

- `tests/OpenHab.App.Tests/TestResults/c8f212bb-e638-47cf-8366-6a420f4e5478/...`

### 5) SitemapInputNormalizationTests

First attempt with `--no-restore` hit transient WinUI build lock (`obj\...\input.json` in `OpenHab.Windows.Tray`).
Used `--no-build` after retries/other successful builds had refreshed binaries.

Command used for result:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-build --filter FullyQualifiedName~SitemapInputNormalizationTests --blame-hang --blame-hang-timeout 30s
```

Result: blame-hang abort; active test at crash:

- `SitemapInputNormalizationTests.NormalizeInputByHint_NumberWithLeadingZeros_IsPreserved`

Artifacts:

- `tests/OpenHab.App.Tests/TestResults/b8f4e98c-9633-4c7a-ad88-085ac3d8d3b2/...`

### 6) DispatcherRefreshGateTests

First attempt with `--no-restore` hit transient lock/denied write in WinUI intermediate outputs.
Second retry with same command succeeded in build and reproduced host shutdown.

Command (successful repro):

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~DispatcherRefreshGateTests --blame-hang --blame-hang-timeout 30s
```

Result: blame-hang abort; active test at crash:

- `DispatcherRefreshGateTests.DrainRunsOnePendingRefresh`

Artifacts:

- `tests/OpenHab.App.Tests/TestResults/f3a9a1db-b65c-4439-8111-953e75d5458b/...`

## Candidate Lifetime Sources

Searched with:

```powershell
rg -n "static readonly HttpClient|DispatcherQueue|Task.Run|Timer|PeriodicTimer|SystemEvents|\\.\\+=|WebView2|NotifyIcon|ToastNotificationManager|AppNotificationManager|CancellationTokenSource|Dispose\\(" src tests\OpenHab.App.Tests
```

Most relevant cross-cutting candidates for post-testhost lifetime leakage/shutdown instability:

- Static/shared HTTP clients in tray rendering path:
  - `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
  - `src/OpenHab.Windows.Tray/Rendering/OpenHabIconImageSourceLoader.cs`
  - `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Unawaited/background tasks:
  - `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs` (`Task.Run` read loop)
  - `src/OpenHab.App/Runtime/SitemapRuntimeController.cs` (multiple `Task.Run`)
- Notification poller CTS lifetime:
  - `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- UI dispatcher/timer/event lifetimes in tray shell:
  - `DispatcherQueue` and `DispatcherTimer` usage in `App.xaml.cs`, `FlyoutWindow.xaml.cs`, `MainWindow.xaml.cs`
  - System event subscription/unsubscription in `App.xaml.cs` (`SystemEvents.SessionSwitch`, `PowerModeChanged`)
- WebView2 event-hooked controls:
  - `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs`
  - `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

## Conclusion

- **Smallest reproducing group:** each of the six requested filtered groups independently reproduces the same `30s` blame-hang host abort, including single-test-active aborts. No unique single group was isolated as exclusive root cause.
- **Strongest suspicion:** a shared process-lifetime issue rather than per-test logic failure.

Ordered targets for Task 7 validation:

1. `src/OpenHab.Windows.Notifications/ToastService.cs`: static `ToastNotificationManagerCompat.OnActivated += HandleToastActivated` subscription has no corresponding unsubscribe/reset path. First validation: add a test-only or production reset/unregister path that unsubscribes the static handler, then rerun one single-test reproducer such as `FullyQualifiedName~DispatcherRefreshGateTests` without blame-hang.
2. `src/OpenHab.Windows.Notifications/NotificationPoller.cs`: background `pollingTask` and `CancellationTokenSource` lifetime can outlive filtered tests if disposal/stop does not await all paths. First validation: rerun `FullyQualifiedName~NotificationPollerTests` after forcing stop/dispose paths to await `StopAsync`.
3. UI-adjacent static/shared resources (`SitemapControlFactory`, `OpenHabIconImageSourceLoader`, `MainWindow` static `HttpClient`, and WebView2 paths): these are less likely as a first patch because each unrelated filtered group reproduces the same host abort. First validation: run a single no-UI helper group after disabling notification static activation registration.

- Prior-context evidence, not produced by the six-filter matrix itself: runtime event-stream tests also fail assertions independently. Treat those as a separate correctness issue unless a Task 7 validation links them directly to the host shutdown.
