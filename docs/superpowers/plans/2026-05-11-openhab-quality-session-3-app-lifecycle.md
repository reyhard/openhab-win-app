# openHAB Quality Session 3 App Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make settings persistence and sitemap event-stream startup observable, retryable, and testable.

**Architecture:** Keep persistence and runtime lifecycle behavior in `OpenHab.App`; keep low-level SSE connection behavior in `OpenHab.Core.Events`. This session does not touch WinUI windows.

**Tech Stack:** .NET 10, xUnit, `Task`, `CancellationToken`, existing runtime/event-stream abstractions.

---

## Session Assignment

- **Codex instance:** Session 3, app lifecycle.
- **Recommended model:** default Codex 5.3 Medium.
- **Worktree:** `.worktrees\quality-app-lifecycle`
- **Branch:** `quality/app-lifecycle`
- **Must not edit:** `FlyoutWindow.xaml.cs`, `MainWindow.xaml.cs`, packaging files, docs except final lane notes if needed.

## Dependencies

- **Depends on:** No implementation dependency.
- **Can run in parallel with:** Sessions 1, 2, and 4.
- **Should merge:** After Sessions 1 and 2 if possible, before Session 4 final integration.
- **Expected conflicts:** Low. Potential conflicts only in app/core tests if another branch adds tests nearby.

## Files

- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Modify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
- Modify: `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs`
- Modify: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Modify: `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`
- Modify: `tests/OpenHab.Core.Tests/Events/OpenHabEventStreamClientTests.cs`

## Task 1: Prepare Worktree

- [ ] **Step 1: Create or enter worktree**

Run from `D:\Source\Openhab\openhab-win-app`:

```powershell
git worktree add .worktrees\quality-app-lifecycle -b quality/app-lifecycle
```

Expected: `.worktrees\quality-app-lifecycle` exists. If it already exists, run future commands from that directory.

- [ ] **Step 2: Inspect target files**

Run:

```powershell
rg -n "SaveAsync|_ = SaveAsync|StartSitemapEventStreamAsync|ConnectAsync|SubscribeToSitemapEventsAsync|_sitemapEventStreamStarted" src\OpenHab.App src\OpenHab.Core tests\OpenHab.App.Tests tests\OpenHab.Core.Tests
```

Expected: current fire-and-forget save and event stream paths are visible.

## Task 2: Observable Settings Persistence

- [ ] **Step 1: Write failing flush tests**

Add to `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`:

```csharp
[Fact]
public async Task FlushAsyncPersistsLatestQueuedSetting()
{
    var controller = new AppSettingsController();

    controller.SetSitemapName("first");
    controller.SetSitemapName("second");
    await controller.FlushAsync();

    var reloaded = new AppSettingsController();
    Assert.Equal("second", reloaded.Current.SitemapName);
}

[Fact]
public async Task FlushAsyncCompletesWhenNoSaveIsQueued()
{
    var controller = new AppSettingsController();

    await controller.FlushAsync();

    Assert.NotNull(controller.Current);
}
```

- [ ] **Step 2: Run focused tests and confirm failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter FlushAsync
```

Expected: compile failure because `FlushAsync` does not exist.

- [ ] **Step 3: Add serialized save queue**

In `src/OpenHab.App/Settings/AppSettingsController.cs`, add fields:

```csharp
private readonly object saveSyncRoot = new();
private Task queuedSaveTask = Task.CompletedTask;
```

Add public method:

```csharp
public Task FlushAsync()
{
    lock (saveSyncRoot)
    {
        return queuedSaveTask;
    }
}
```

Replace every `_ = SaveAsync();` with:

```csharp
QueueSave();
```

Add:

```csharp
private void QueueSave()
{
    AppSettings snapshot;
    lock (syncRoot)
    {
        snapshot = Current;
    }

    lock (saveSyncRoot)
    {
        queuedSaveTask = queuedSaveTask
            .ContinueWith(
                _ => SaveAsync(snapshot),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default)
            .Unwrap();
    }
}
```

Change `private async Task SaveAsync()` to:

```csharp
private async Task SaveAsync(AppSettings snapshot)
{
    try
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SettingsFilePath, json);
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Warn($"Settings save failed: {ex.GetType().Name}: {ex.Message}");
    }
}
```

- [ ] **Step 4: Run app settings tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter AppSettingsControllerTests
```

Expected: app settings tests pass.

- [ ] **Step 5: Remove retry-loop dependency where touched**

In constructors or setup code touched by this session, replace comments that say prior fire-and-forget saves may still be writing with plain cleanup comments. For tests that need durability before reload, call:

```csharp
await controller.FlushAsync();
```

Expected: tests no longer depend on timing sleeps for saves this session controls.

## Task 3: Runtime Event Stream Retry Semantics

- [ ] **Step 1: Extend fake event client**

In `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`, update `FakeEventStreamClient` with:

```csharp
public Exception? SubscribeFailure { get; set; }
public Exception? ConnectFailure { get; set; }
public int SubscribeCalls { get; private set; }
public int ConnectCalls { get; private set; }

public Task ConnectAsync(Uri baseUri, CancellationToken cancellationToken = default)
{
    ConnectCalls++;
    if (ConnectFailure is not null)
    {
        return Task.FromException(ConnectFailure);
    }

    IsConnected = true;
    return Task.CompletedTask;
}

public Task<string?> SubscribeToSitemapEventsAsync(Uri baseUri, CancellationToken cancellationToken = default)
{
    SubscribeCalls++;
    if (SubscribeFailure is not null)
    {
        return Task.FromException<string?>(SubscribeFailure);
    }

    return Task.FromResult<string?>("fake-subscription-id");
}
```

- [ ] **Step 2: Add retry tests**

Add to `SitemapRuntimeControllerTests`:

```csharp
[Fact]
public async Task StartSitemapEventStreamAllowsRetryAfterSubscribeFailure()
{
    var settings = new AppSettingsController();
    settings.SetSitemapName("default");
    var eventClient = new FakeEventStreamClient
    {
        SubscribeFailure = new InvalidOperationException("subscribe failed")
    };
    var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
    eventClient.SubscribeFailure = null;
    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

    Assert.Equal(2, eventClient.SubscribeCalls);
    Assert.Equal(1, eventClient.ConnectCalls);
}

[Fact]
public async Task StartSitemapEventStreamAllowsRetryAfterConnectFailure()
{
    var settings = new AppSettingsController();
    settings.SetSitemapName("default");
    var eventClient = new FakeEventStreamClient
    {
        ConnectFailure = new InvalidOperationException("connect failed")
    };
    var controller = CreateRuntimeController(settings, new FakeOpenHabClient(), new FakeOpenHabClient(), eventClient);

    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
    eventClient.ConnectFailure = null;
    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");

    Assert.Equal(2, eventClient.ConnectCalls);
    Assert.True(eventClient.IsConnected);
}
```

- [ ] **Step 3: Run focused tests and confirm failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter StartSitemapEventStreamAllowsRetry
```

Expected: failure because stream start state is not reset after connect failure.

- [ ] **Step 4: Reset stream-start state on failed start**

In `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`, replace `StartSitemapEventStreamAsync` body with:

```csharp
public async Task StartSitemapEventStreamAsync(Uri localBaseUri, string sitemapName, string pageId, CancellationToken ct = default)
{
    if (sitemapEventStreamClient is null) return;

    if (_sitemapEventStreamStarted && _sitemapEventStreamSitemapName == sitemapName && _sitemapEventStreamPageId == pageId)
    {
        return;
    }

    _sitemapEventStreamStarted = true;
    _sitemapEventStreamSitemapName = sitemapName;
    _sitemapEventStreamPageId = pageId;

    try
    {
        DiagnosticLogger.Info($"Starting sitemap event stream to {localBaseUri} for sitemap '{sitemapName}' page '{pageId}'");
        EnsureSitemapEventHandlersAttached();

        _subscriptionId = await sitemapEventStreamClient.SubscribeToSitemapEventsAsync(localBaseUri, ct);
        if (_subscriptionId is null)
        {
            DiagnosticLogger.Warn("Failed to subscribe to sitemap events - no subscription ID returned");
            ResetSitemapEventStreamStart();
            return;
        }

        DiagnosticLogger.Info($"Sitemap event subscription created: {_subscriptionId}");
        var sseUrl = new Uri(localBaseUri, $"rest/sitemaps/events/{_subscriptionId}?sitemap={Uri.EscapeDataString(sitemapName)}&pageid={Uri.EscapeDataString(pageId)}");
        await sitemapEventStreamClient.ConnectAsync(sseUrl, ct);
    }
    catch (OperationCanceledException)
    {
        ResetSitemapEventStreamStart();
        throw;
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Warn($"Sitemap event stream start failed: {ex.GetType().Name}: {ex.Message}");
        ResetSitemapEventStreamStart();
        Current = Current with
        {
            ConnectionState = Current.ConnectionState == ConnectionState.Online
                ? ConnectionState.Degraded
                : Current.ConnectionState,
            StatusText = Current.ConnectionState == ConnectionState.Online
                ? "Live updates unavailable. Refresh manually."
                : Current.StatusText,
            ChangedRowIndices = []
        };
    }
}

private void ResetSitemapEventStreamStart()
{
    _sitemapEventStreamStarted = false;
    _sitemapEventStreamSitemapName = null;
    _sitemapEventStreamPageId = null;
}
```

- [ ] **Step 5: Run runtime tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "StartSitemapEventStreamAllowsRetry|WidgetEvent"
```

Expected: selected runtime tests pass.

## Task 4: Make First SSE Connect Outcome Observable

- [ ] **Step 1: Add core connect outcome tests**

In `tests/OpenHab.Core.Tests/Events/OpenHabEventStreamClientTests.cs`, add a test that returns `HttpStatusCode.Unauthorized` on the first SSE request and asserts `ConnectAsync` throws:

```csharp
[Fact]
public async Task ConnectAsyncSurfacesFirstConnectionFailure()
{
    var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent("unauthorized")
    });
    var client = new OpenHabEventStreamClient(new HttpClient(handler));

    await Assert.ThrowsAsync<HttpRequestException>(
        () => client.ConnectAsync(new Uri("http://localhost:8080/rest/events")));
}
```

If `QueueHandler` does not exist, add a local test handler that dequeues supplied responses and returns them from `SendAsync`.

- [ ] **Step 2: Run focused core test and confirm failure**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter ConnectAsyncSurfacesFirstConnectionFailure
```

Expected: failure because current `ConnectAsync` returns before the read loop result is known.

- [ ] **Step 3: Update `OpenHabEventStreamClient.ConnectAsync`**

In `src/OpenHab.Core/Events/OpenHabEventStreamClient.cs`, replace `ConnectAsync` with:

```csharp
public async Task ConnectAsync(Uri sseUri, CancellationToken cancellationToken = default)
{
    var uriChanged = _sseUri is null || _sseUri != sseUri;
    if (IsConnected && !uriChanged)
    {
        return;
    }

    _sseUri = sseUri;

    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var previous = Interlocked.Exchange(ref _internalCts, cts);
    previous?.Cancel();
    previous?.Dispose();

    var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = Task.Run(() => ReadLoopAsync(sseUri, cts, firstAttempt), CancellationToken.None);

    await firstAttempt.Task.WaitAsync(cancellationToken);
}
```

Change `ReadLoopAsync` signature to:

```csharp
private async Task ReadLoopAsync(Uri sseUri, CancellationTokenSource cts, TaskCompletionSource firstAttempt)
```

After `response.EnsureSuccessStatusCode();`, add:

```csharp
Interlocked.Exchange(ref _isConnected, 1);
firstAttempt.TrySetResult();
```

In the non-cancellation catch, add before reconnect delay:

```csharp
firstAttempt.TrySetException(ex);
```

At the method exit after cancellation, add:

```csharp
firstAttempt.TrySetCanceled(ct);
```

- [ ] **Step 4: Run app and core test projects**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
```

Expected: both projects pass.

## Task 5: Commit

- [ ] **Step 1: Check diff**

Run:

```powershell
git diff -- src\OpenHab.App src\OpenHab.Core tests\OpenHab.App.Tests tests\OpenHab.Core.Tests
git diff --check
```

Expected: only app/core lifecycle changes and tests.

- [ ] **Step 2: Commit**

Run:

```powershell
git add src/OpenHab.App src/OpenHab.Core tests/OpenHab.App.Tests tests/OpenHab.Core.Tests
git commit -m "fix: make app lifecycle operations observable"
```

Expected: commit succeeds.

## Handoff

This session can merge before the Windows sitemap session. If `ConnectAsync` first-attempt behavior changes existing tests, preserve reconnect behavior: after the first successful connect, reconnects still remain inside the background read loop.
