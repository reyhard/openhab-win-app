# openHAB Windows Performance Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce background RAM use, avoid unnecessary startup UI/WebView allocation, and improve visible-surface responsiveness without changing shipped behavior.

**Architecture:** Keep app/runtime logic in `OpenHab.App` and Windows-specific resource management in `OpenHab.Windows.Tray`. The key change is lifecycle separation: background services start immediately, heavyweight WinUI windows and WebView2 are created only when a user-visible surface needs them, and polling/event-stream work follows current settings and visibility.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK, xUnit, `System.Windows.Forms.NotifyIcon`, WebView2, openHAB REST/SSE clients.

---

## Current Baseline

Read these before changing code:

- `docs/superpowers/status/openhab-windows-current-state.md`
- `docs/superpowers/verification/openhab-windows-quality-gates.md`
- `src/OpenHab.Windows.Tray/App.xaml.cs`
- `src/OpenHab.Windows.Tray/MainWindow.xaml`
- `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml`
- `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs`
- `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`

Observed optimization targets:

- `App.OnLaunched` constructs `MainWindow`, `FlyoutWindow`, `RadialCommandMenuWindow`, and `GlobalHotkeyService` during startup.
- `GlobalHotkeyService` depends on `MainWindow`, forcing main-window allocation even while running in tray background.
- `CompleteStartupAsync` may load runtime into both flyout and main window after sitemap discovery.
- `MainWindow.xaml` declares `MainUiWebViewHost` in XAML, so the host is allocated with the main window.
- `NotificationPoller` has a default 30-second interval and is created without passing `AppSettings.NotificationPollIntervalSeconds`.
- Notification polling is started once and is not rebuilt when endpoint mode, credentials, or polling interval changes.
- SSE event stream currently logs high-volume raw/event lines unconditionally.

## File Structure

Modify these files during implementation:

- `src/OpenHab.Windows.Tray/App.xaml.cs`
  - Owns app startup, lazy window creation, notification poller lifecycle, and background/visible surface switching.
- `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs`
  - Add an HWND-based constructor or accept a lightweight host abstraction so hotkeys do not require `MainWindow`.
- `src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs`
  - New Windows-only hidden/message-window host for global hotkeys.
- `src/OpenHab.Windows.Tray/MainWindow.xaml`
  - Remove eager `MainUiWebViewHost` declaration from the center content host.
- `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
  - Lazily create `MainUiWebViewHost`, settings page, notifications page, and native sitemap pane content.
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
  - Ensure hidden flyout does not receive runtime refreshes and releases nonessential visual rows where appropriate.
- `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
  - Accept effective polling interval, support clean restart, and expose enough state for tests.
- `src/OpenHab.App/Settings/AppSettingsController.cs`
  - Add targeted no-op suppression or change-specific notifications only if poller restart would otherwise be noisy.
- `src/OpenHab.Core/DiagnosticLogger.cs`
  - Add a cheap diagnostic verbosity switch if no equivalent exists.
- `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
  - Gate raw SSE line logging behind verbose diagnostics.
- `tests/OpenHab.App.Tests/Tray/TrayShellControllerTests.cs`
  - Add or adjust shell lifecycle tests if `TrayShellController` needs a new visible-surface transition.
- `tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs`
  - Cover interval behavior and cancellation/restart behavior.
- `tests/OpenHab.Core.Tests/Events/OpenHabEventStreamClientTests.cs`
  - Cover that normal event handling still works with verbose logging disabled.
- `tests/OpenHab.App.Tests/Settings/AppSettingsControllerTests.cs`
  - Add no-op/change notification tests only if `AppSettingsController` is changed.

Do not include the Community Toolkit settings refactor in this plan. That belongs to `docs/superpowers/plans/2026-05-14-toolkit-settings-replacement.md`.

---

### Task 1: Add A Lightweight Hotkey Host

**Files:**
- Create: `src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs`
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Shortcuts/ShortcutBindingTests.cs` only if shortcut mapping behavior changes

- [ ] **Step 1: Inspect current hotkey service construction**

Run:

```powershell
rg -n "new GlobalHotkeyService|GlobalHotkeyService\\(" src tests
```

Expected: construction is in `src/OpenHab.Windows.Tray/App.xaml.cs`, currently requiring `mainWindow`.

- [ ] **Step 2: Create the hidden HWND host**

Add `HotkeyMessageWindow` with:

```csharp
namespace OpenHab.Windows.Tray.Shortcuts;

internal sealed class HotkeyMessageWindow : IDisposable
{
    public IntPtr Hwnd { get; }

    public HotkeyMessageWindow()
    {
        Hwnd = CreateHiddenWindow();
    }

    public void Dispose()
    {
        if (Hwnd != IntPtr.Zero)
        {
            DestroyWindow(Hwnd);
        }
    }

    private static IntPtr CreateHiddenWindow()
    {
        // Use a minimal native hidden window. Do not create a WinUI Window here.
        // Register class, create HWND_MESSAGE or hidden overlapped window,
        // and return the handle used by GlobalHotkeyService.
        throw new NotImplementedException("Implemented in this task.");
    }
}
```

Replace the `NotImplementedException` in the same step with the local P/Invoke pattern already used by `RadialCommandMenuWindow`; do not use PowerShell reflection or private-method invocation.

- [ ] **Step 3: Add an HWND constructor to `GlobalHotkeyService`**

Keep the existing `Window` constructor for tests and compatibility, and delegate both constructors to a shared private constructor:

```csharp
public GlobalHotkeyService(Window window, DispatcherQueue dispatcherQueue)
    : this(WindowNative.GetWindowHandle(window ?? throw new ArgumentNullException(nameof(window))), dispatcherQueue)
{
}

public GlobalHotkeyService(IntPtr hwnd, DispatcherQueue dispatcherQueue)
    : this(hwnd, dispatcherQueue, ownsWindowHandle: false)
{
}

private GlobalHotkeyService(IntPtr hwnd, DispatcherQueue dispatcherQueue, bool ownsWindowHandle)
{
    if (hwnd == IntPtr.Zero)
    {
        throw new ArgumentException("A valid window handle is required.", nameof(hwnd));
    }

    this.hwnd = hwnd;
    this.dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    subclassProc = WindowProc;
    if (!SetWindowSubclass(hwnd, subclassProc, 1, IntPtr.Zero))
    {
        throw new InvalidOperationException("Failed to subclass hotkey window.");
    }
}
```

- [ ] **Step 4: Construct hotkeys without constructing `MainWindow`**

In `App.xaml.cs`, add fields:

```csharp
private HotkeyMessageWindow? hotkeyMessageWindow;
```

Change startup from `new GlobalHotkeyService(mainWindow, ...)` to:

```csharp
hotkeyMessageWindow = new HotkeyMessageWindow();
globalHotkeyService = new GlobalHotkeyService(
    hotkeyMessageWindow.Hwnd,
    uiDispatcherQueue ?? DispatcherQueue.GetForCurrentThread());
```

- [ ] **Step 5: Dispose the host on application exit**

In the existing shutdown/disposal path, dispose in this order:

```csharp
globalHotkeyService?.Dispose();
globalHotkeyService = null;
hotkeyMessageWindow?.Dispose();
hotkeyMessageWindow = null;
```

- [ ] **Step 6: Build the tray project**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 7: Commit**

```powershell
git add src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "perf: decouple global hotkeys from main window"
```

---

### Task 2: Lazily Create Main And Flyout Windows

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.App/Tray/TrayShellController.cs` only if state needs an explicit created/visible distinction
- Test: `tests/OpenHab.App.Tests/Tray/TrayShellControllerTests.cs`

- [ ] **Step 1: Add tests for background launch behavior**

In `TrayShellControllerTests`, add:

```csharp
[Fact]
public void Launch_StartsInBackgroundWithoutVisibleSurface()
{
    var controller = new TrayShellController();

    controller.HandleLaunch();

    Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
    Assert.True(controller.Current.IsRunningInBackground);
    Assert.False(controller.Current.PendingRefresh);
}
```

If `TrayShellState.Initial` currently does not mark background mode, update the expected state and implementation together.

- [ ] **Step 2: Run the tray shell tests**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter FullyQualifiedName~TrayShellControllerTests
```

Expected before implementation: fail only if launch state needs correction.

- [ ] **Step 3: Add lazy factory methods in `App.xaml.cs`**

Add methods:

```csharp
private MainWindow EnsureMainWindow()
{
    if (mainWindow is not null)
    {
        return mainWindow;
    }

    mainWindow = CreateMainWindow();
    return mainWindow;
}

private FlyoutWindow EnsureFlyoutWindow()
{
    if (flyoutWindow is not null)
    {
        return flyoutWindow;
    }

    flyoutWindow = CreateFlyoutWindow();
    return flyoutWindow;
}
```

Move the current constructor blocks for `MainWindow` and `FlyoutWindow` into `CreateMainWindow` and `CreateFlyoutWindow`. Preserve all existing callbacks.

- [ ] **Step 4: Remove eager window creation from startup**

In `OnLaunched`, do not call `new MainWindow(...)` or `new FlyoutWindow(...)`. Keep creation of:

- `AppSettingsController`
- `NotificationStore`
- `HttpClient`
- `SitemapRuntimeController`
- `DeviceInfoSyncService`
- `ShortcutActionExecutor`
- `TrayIconService`
- `GlobalHotkeyService`

- [ ] **Step 5: Update shell application logic**

In `ApplyShellStateAsync`, call `EnsureMainWindow()` only when `VisibleSurface == TrayShellSurface.MainWindow`, and call `EnsureFlyoutWindow()` only when `VisibleSurface == TrayShellSurface.Flyout`.

Use nullable guards for hidden surfaces:

```csharp
if (mainWindow is not null && state.VisibleSurface != TrayShellSurface.MainWindow)
{
    mainWindow.AppWindow.Hide();
}

if (flyoutWindow is not null && state.VisibleSurface != TrayShellSurface.Flyout && flyoutWindow.AppWindow.IsVisible)
{
    await flyoutWindow.AnimateFlyoutExitAndHideAsync();
}
```

- [ ] **Step 6: Keep activation callbacks lazy-safe**

Replace direct `mainWindow?.ShowSettingsTab()` and `mainWindow?.ShowNotificationsTab()` callbacks with:

```csharp
EnsureMainWindow().ShowSettingsTab();
```

and:

```csharp
EnsureMainWindow().ShowNotificationsTab();
```

- [ ] **Step 7: Run tests and build**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter FullyQualifiedName~TrayShellControllerTests
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: tests and build pass.

- [ ] **Step 8: Commit**

```powershell
git add src/OpenHab.Windows.Tray/App.xaml.cs src/OpenHab.App/Tray/TrayShellController.cs tests/OpenHab.App.Tests/Tray/TrayShellControllerTests.cs
git commit -m "perf: create tray windows on demand"
```

---

### Task 3: Refresh Only The Visible Surface

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Tray/TrayShellControllerTests.cs`

- [ ] **Step 1: Search all dual-surface loads**

Run:

```powershell
rg -n "flyoutWindow\\?\\.LoadRuntimeAsync|mainWindow\\?\\.LoadRuntimeAsync|flyoutWindow\\?\\.RefreshRuntimeAsync|mainWindow\\?\\.RefreshRuntimeAsync" src/OpenHab.Windows.Tray/App.xaml.cs
```

Expected: find startup sitemap resolution paths and pending refresh handling.

- [ ] **Step 2: Add a helper for visible refresh**

In `App.xaml.cs`, add:

```csharp
private async Task RefreshVisibleRuntimeSurfaceAsync(TrayShellSurface visibleSurface)
{
    if (visibleSurface == TrayShellSurface.MainWindow)
    {
        await EnsureMainWindow().RefreshRuntimeAsync();
        return;
    }

    if (visibleSurface == TrayShellSurface.Flyout)
    {
        await EnsureFlyoutWindow().RefreshRuntimeAsync();
    }
}
```

Add a matching load helper:

```csharp
private async Task LoadVisibleRuntimeSurfaceAsync(TrayShellSurface visibleSurface)
{
    if (visibleSurface == TrayShellSurface.MainWindow)
    {
        await EnsureMainWindow().LoadRuntimeAsync();
        return;
    }

    if (visibleSurface == TrayShellSurface.Flyout)
    {
        await EnsureFlyoutWindow().LoadRuntimeAsync();
    }
}
```

- [ ] **Step 3: Replace dual startup load calls**

In sitemap resolution blocks, replace:

```csharp
flyoutWindow?.LoadRuntimeAsync();
mainWindow?.LoadRuntimeAsync();
```

with:

```csharp
_ = LoadVisibleRuntimeSurfaceAsync(shellController.Current.VisibleSurface);
```

If no surface is visible, do not load UI rows. The runtime controller can keep sitemap selection and status only.

- [ ] **Step 4: Replace pending refresh dual calls**

In `ApplyShellStateAsync`, replace per-window refresh branches with `RefreshVisibleRuntimeSurfaceAsync(state.VisibleSurface)`.

- [ ] **Step 5: Ensure hidden windows do not rebuild visual rows**

In `MainWindow.RefreshRuntimeBindings` and `FlyoutWindow.RefreshRuntimeBindings`, add an early return when the owning `AppWindow` is hidden and no explicit target panel is supplied:

```csharp
if (targetRows is null && !AppWindow.IsVisible)
{
    return;
}
```

Keep transition-target refreshes working because they pass an explicit target panel.

- [ ] **Step 6: Run build**

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```powershell
git add src/OpenHab.Windows.Tray/App.xaml.cs src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs
git commit -m "perf: refresh only visible runtime surface"
```

---

### Task 4: Lazily Create WebView2 Host

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Test: build verification

- [ ] **Step 1: Remove eager host from XAML**

In `MainWindow.xaml`, replace:

```xml
<Grid x:Name="CenterContentHost"
      Grid.Column="1"
      Margin="12,10,12,0">
    <mainui:MainUiWebViewHost x:Name="MainUiHost" />
</Grid>
```

with:

```xml
<Grid x:Name="CenterContentHost"
      Grid.Column="1"
      Margin="12,10,12,0" />
```

- [ ] **Step 2: Add nullable host field**

In `MainWindow.xaml.cs`, add:

```csharp
private MainUiWebViewHost? mainUiHost;

private MainUiWebViewHost MainUiHost => mainUiHost ??= CreateMainUiHost();

private MainUiWebViewHost CreateMainUiHost()
{
    var host = new MainUiWebViewHost();
    host.CurrentRouteChanged += MainUiHost_CurrentRouteChanged;
    return host;
}
```

Remove the constructor subscription to `MainUiHost.CurrentRouteChanged`.

- [ ] **Step 3: Guard route access**

Replace route reads that should not allocate WebView with:

```csharp
var currentRoute = mainUiHost?.CurrentRoute ?? "/";
```

Only use the `MainUiHost` property in paths that intentionally display or navigate Main UI.

- [ ] **Step 4: Update `ShowMainUi`**

Use:

```csharp
private void ShowMainUi()
{
    var host = MainUiHost;
    if (!CenterContentHost.Children.Contains(host))
    {
        CenterContentHost.Children.Clear();
        CenterContentHost.Children.Add(host);
    }
}
```

- [ ] **Step 5: Run build**

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```powershell
git add src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs
git commit -m "perf: defer main ui webview host creation"
```

---

### Task 5: Make Notification Polling Settings-Driven And Restartable

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs`
- Test: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs` only if settings notifications change

- [ ] **Step 1: Add poll interval test**

In `NotificationPollerTests`, add:

```csharp
[Fact]
public void Constructor_UsesProvidedPollInterval()
{
    using var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
    using var poller = new NotificationPoller(
        httpClient,
        new Uri("https://example.test/"),
        pollInterval: TimeSpan.FromSeconds(90));

    Assert.Equal(TimeSpan.FromSeconds(90), poller.PollInterval);
}
```

Add a public read-only property in implementation:

```csharp
public TimeSpan PollInterval => pollInterval;
```

- [ ] **Step 2: Pass settings interval**

In `App.xaml.cs`, when creating `NotificationPoller`, pass:

```csharp
pollInterval: TimeSpan.FromSeconds(settings.NotificationPollIntervalSeconds),
```

- [ ] **Step 3: Add poller restart method**

In `App.xaml.cs`, add:

```csharp
private void RestartNotificationPolling()
{
    notificationPoller?.Dispose();
    notificationPoller = null;
    if (settingsController is not null)
    {
        StartNotificationPolling(settingsController);
    }
}
```

- [ ] **Step 4: Restart only when relevant settings change**

Keep a private snapshot:

```csharp
private NotificationPollingConfig? activeNotificationPollingConfig;

private sealed record NotificationPollingConfig(
    EndpointMode EndpointMode,
    Uri CloudEndpoint,
    int PollIntervalSeconds,
    bool HasCloudCredentials,
    bool HasLocalToken);
```

In `StartNotificationPolling`, set `activeNotificationPollingConfig` from current settings. In the `SettingsChanged` handler, compare the new config and call `RestartNotificationPolling()` only when it changed.

- [ ] **Step 5: Respect `EndpointMode.LocalOnly` on restart**

If settings move to local-only mode:

```csharp
notificationPoller?.Dispose();
notificationPoller = null;
activeNotificationPollingConfig = BuildNotificationPollingConfig(settingsController.Current);
return;
```

- [ ] **Step 6: Run notification tests**

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter FullyQualifiedName~NotificationPollerTests
```

Expected: notification tests pass.

- [ ] **Step 7: Build tray project**

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 8: Commit**

```powershell
git add src/OpenHab.Windows.Notifications/NotificationPoller.cs src/OpenHab.Windows.Tray/App.xaml.cs tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs
git commit -m "perf: make notification polling settings driven"
```

---

### Task 6: Gate High-Volume SSE Diagnostics

**Files:**
- Modify: `src/OpenHab.Core/DiagnosticLogger.cs`
- Modify: `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
- Test: `tests/OpenHab.Core.Tests/Events/OpenHabEventStreamClientTests.cs`

- [ ] **Step 1: Inspect logger shape**

Run:

```powershell
Get-Content src/OpenHab.Core/DiagnosticLogger.cs
```

Expected: identify whether there is already a verbosity concept.

- [ ] **Step 2: Add a cheap verbose switch if needed**

If no switch exists, add:

```csharp
public static bool VerboseDiagnosticsEnabled { get; set; }

public static void Verbose(string message)
{
    if (VerboseDiagnosticsEnabled)
    {
        Info(message);
    }
}
```

Do not change existing log file paths.

- [ ] **Step 3: Replace raw SSE logs**

In `OpenHabEventStreamClient`, replace raw per-line `DiagnosticLogger.Info(...)` calls with:

```csharp
DiagnosticLogger.Verbose($"SSE raw: {line[..Math.Min(line.Length, 200)]}");
```

and:

```csharp
DiagnosticLogger.Verbose($"SSE widget event: id={widgetEvent.WidgetId} vis={widgetEvent.Visibility} item={widgetEvent.ItemName} state={widgetEvent.ItemState}");
```

Keep warnings for failures and unparsed data.

- [ ] **Step 4: Run event stream tests**

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj --filter FullyQualifiedName~OpenHabEventStreamClientTests
```

Expected: event stream tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/OpenHab.Core/DiagnosticLogger.cs src/OpenHab.Core/Events/OpenHabEventStreamClient.cs tests/OpenHab.Core.Tests/Events/OpenHabEventStreamClientTests.cs
git commit -m "perf: gate verbose sse diagnostics"
```

---

### Task 7: Pause Sitemap Event Stream When No Sitemap Surface Is Visible

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`

- [ ] **Step 1: Add runtime API for stopping events**

In `SitemapRuntimeController`, add:

```csharp
public void StopSitemapEventStream()
{
    sitemapEventStreamClient?.Dispose();
    _sitemapEventStreamStarted = false;
    _sitemapEventStreamSitemapName = null;
    _sitemapEventStreamPageId = null;
    _subscriptionId = null;
}
```

If the injected client is reused after dispose today, instead add `DisconnectAsync` to `IOpenHabEventStreamClient` and implement cancellation without making the client unusable.

- [ ] **Step 2: Add test for stop clearing stream state**

In `SitemapRuntimeControllerTests`, add a fake event stream client with a `DisposeCount` or `DisconnectCount`, then assert:

```csharp
controller.StopSitemapEventStream();

Assert.Equal(1, fakeStream.DisconnectCount);
```

Use the existing fake/test helpers in that test file rather than introducing a new test framework.

- [ ] **Step 3: Call stop when entering background**

In `App.xaml.cs`, when `ApplyShellStateAsync` applies `TrayShellSurface.None`, call:

```csharp
runtimeController?.StopSitemapEventStream();
```

Do not stop notification polling or device-info sync here.

- [ ] **Step 4: Restart stream on next visible load/refresh**

Confirm existing `LoadRuntimeAsync` and `RefreshRuntimeAsync` start the stream through runtime load paths. If not, call the existing runtime refresh path rather than direct event stream code from the Windows layer.

- [ ] **Step 5: Run runtime tests**

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter FullyQualifiedName~SitemapRuntimeControllerTests
```

Expected: runtime tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/OpenHab.App/Runtime/SitemapRuntimeController.cs src/OpenHab.Windows.Tray/App.xaml.cs tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs
git commit -m "perf: pause sitemap events in tray background"
```

---

### Task 8: Reduce Hidden Visual Tree Retention

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs` only if helper extraction is useful
- Test: build verification

- [ ] **Step 1: Add explicit visual row release methods**

In both `MainWindow` and `FlyoutWindow`, add:

```csharp
public void ReleaseSitemapVisualRows()
{
    SitemapRows.Children.Clear();
    SitemapRowsB.Children.Clear();
    sitemapSurfaceRenderer.ForceFullRebuild();
}
```

- [ ] **Step 2: Call release after hiding**

In `App.xaml.cs`, after a surface is hidden and the app is in `TrayShellSurface.None`, call:

```csharp
mainWindow?.ReleaseSitemapVisualRows();
flyoutWindow?.ReleaseSitemapVisualRows();
```

Do not clear notification or settings pages here.

- [ ] **Step 3: Keep active transition safe**

If a window has an active page transition flag, skip release until the hide animation finishes. Use existing `_isPageTransitionRunning` or public helper if available.

- [ ] **Step 4: Build tray project**

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "perf: release hidden sitemap visual rows"
```

---

### Task 9: Measure Startup And Background Footprint

**Files:**
- Modify: `docs/superpowers/status/openhab-windows-current-state.md`
- Create: `docs/superpowers/verification/2026-05-14-performance-optimization-results.md`

- [ ] **Step 1: Build Release**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 2: Capture baseline and optimized memory manually**

Use Task Manager or Process Explorer and record:

- private working set after app launch and tray idle for 60 seconds
- private working set after first flyout open
- private working set after flyout hide and 60 seconds idle
- private working set after first main window open
- private working set after main window hide and 60 seconds idle

- [ ] **Step 3: Save results**

Create `docs/superpowers/verification/2026-05-14-performance-optimization-results.md`:

```markdown
# Performance Optimization Verification

Date: 2026-05-14

## Build

- Command: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release`
- Result: passed

## Memory Observations

| Scenario | Before | After | Notes |
| --- | ---: | ---: | --- |
| Launch, tray idle 60s |  |  |  |
| First flyout open |  |  |  |
| Flyout hidden, idle 60s |  |  |  |
| First main window open |  |  |  |
| Main window hidden, idle 60s |  |  |  |

## Functional Smoke

- Tray icon click opens flyout.
- Flyout can refresh sitemap.
- Main window opens from flyout.
- Main UI tab loads when selected.
- Settings tab opens.
- Notifications tab opens.
- Configured global command menu shortcut still opens.
- Background notification polling still works when cloud mode is enabled.
```

- [ ] **Step 4: Update status doc**

Append a short evidence entry to `docs/superpowers/status/openhab-windows-current-state.md` with the verification file path and exact commands run.

- [ ] **Step 5: Commit**

```powershell
git add docs/superpowers/status/openhab-windows-current-state.md docs/superpowers/verification/2026-05-14-performance-optimization-results.md
git commit -m "docs: record performance optimization verification"
```

---

## Final Verification

Run:

```powershell
dotnet test tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj
dotnet test tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj
dotnet test tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release
```

Expected:

- Core tests pass.
- Sitemaps tests pass.
- Rendering tests pass.
- App tests pass.
- Tray Release build passes.

If DesktopBridge targets are installed, also run:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

Expected: package build succeeds. If it fails because the app is running and files cannot be overwritten, close the running app and rerun once before diagnosing code.

## Risk Notes

- Lazy window creation changes startup sequencing. Keep callbacks in `App.xaml.cs` centralized through `EnsureMainWindow` and `EnsureFlyoutWindow`.
- Hotkey behavior is user-visible. Test command menu activation before and after opening main window.
- WebView2 must remain deferred until Main UI is selected; avoid route checks that accidentally allocate `MainUiWebViewHost`.
- Do not stop notification polling in tray background. Only pause native sitemap SSE and visual row rendering.
- Keep settings persistence behavior intact unless a task explicitly changes it.

## Self-Review

- Spec coverage: covers background RAM, startup allocation, visible rendering, WebView deferral, notification polling, SSE logging, hidden visual retention, and measurement.
- Placeholder scan: no task uses unspecified implementation handoffs; each implementation task has exact files and commands.
- Type consistency: proposed helper names are consistent across app, window, notification, and runtime tasks.
