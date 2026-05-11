# openHAB Quality Session 4 Windows Sitemap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify flyout and main-window sitemap row behavior behind shared Windows-layer row planning, rendering, icon auth, dispatcher replay, and chart cache policy.

**Architecture:** Keep WinUI element creation in `OpenHab.Windows.Tray`. Extract pure row planning first, then shared surface rendering, then wire both windows to the same coordinator.

**Tech Stack:** .NET 10, WinUI/Windows App SDK, xUnit, `SitemapControlFactory`, existing `SitemapRuntimeController`.

---

## Session Assignment

- **Codex instance:** Session 4, Windows sitemap surface.
- **Recommended model:** default Codex 5.3 Medium. Use high reasoning if it gets blocked on WinUI integration.
- **Worktree:** `.worktrees\quality-windows-sitemap`
- **Branch:** `quality/windows-sitemap-coordinator`
- **Must own exclusively:** `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`, `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`, `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`.

## Dependencies

- **Depends on:** No code dependency for starting.
- **Can run in parallel with:** Sessions 1, 2, and 3.
- **Should merge:** Last among the four sessions because it touches large WinUI files and has the highest conflict risk.
- **Soft dependency:** Session 3 may change runtime event-stream behavior, but this session only consumes `SitemapRuntimeSnapshot`.
- **Expected conflicts:** High if any other branch edits `FlyoutWindow.xaml.cs` or `MainWindow.xaml.cs`. Other sessions should not touch those files.

## Files

- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapIconAuthResolver.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapVisualRow.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapRowPlanner.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs`
- Create: `src/OpenHab.Windows.Tray/Rendering/DispatcherRefreshGate.cs`
- Create: `tests/OpenHab.App.Tests/SitemapSurface/SitemapRowPlannerTests.cs`
- Create: `tests/OpenHab.App.Tests/SitemapSurface/DispatcherRefreshGateTests.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`

## Task 1: Prepare Worktree

- [ ] **Step 1: Create or enter worktree**

Run from `D:\Source\Openhab\openhab-win-app`:

```powershell
git worktree add .worktrees\quality-windows-sitemap -b quality/windows-sitemap-coordinator
```

Expected: `.worktrees\quality-windows-sitemap` exists. If it already exists, run future commands from that directory.

- [ ] **Step 2: Inspect duplicate row paths**

Run:

```powershell
rg -n "RefreshRuntimeBindings|BuildMergedButtonGridRow|CreateRowElementForIndex|ResolveIconAuth|GetApiTokenSync|GetCloudCredentialsSync|DispatcherQueue.TryEnqueue|ButtonGrid" src\OpenHab.Windows.Tray tests
```

Expected: duplicate flyout/main window behavior is visible.

## Task 2: Pure Sitemap Row Planner

- [ ] **Step 1: Add row planner tests**

Create `tests/OpenHab.App.Tests/SitemapSurface/SitemapRowPlannerTests.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Rendering.SitemapSurface;

namespace OpenHab.App.Tests.SitemapSurface;

public sealed class SitemapRowPlannerTests
{
    [Fact]
    public void VisualRowsSkipButtonChildrenAndMergeVisibleButtonGridOptions()
    {
        var rows = new[]
        {
            Grid("Mode"),
            Button("Auto", "AUTO", visible: true),
            Button("Manual", "MANUAL", visible: false),
            Text("Temperature")
        };

        var visualRows = SitemapRowPlanner.BuildVisualRows(rows);

        Assert.Equal([0, 3], visualRows.Select(row => row.RowIndex).ToArray());
        Assert.Single(visualRows[0].Row.SelectionOptions);
        Assert.Equal("Auto", visualRows[0].Row.SelectionOptions[0].Label);
        Assert.Equal(1, visualRows[0].Row.SelectionOptions[0].SourceRowIndex);
    }

    [Fact]
    public void ExpandChangedIndicesMapsButtonChildToOwningGrid()
    {
        var rows = new[] { Grid("Mode"), Button("Auto", "AUTO", true), Button("Manual", "MANUAL", true), Text("Temperature") };

        var expanded = SitemapRowPlanner.ExpandChangedIndices([2], rows);

        Assert.Equal([0], expanded);
    }

    [Fact]
    public void TryResolveRowIndexFindsCurrentRowByStableKey()
    {
        var rows = new[] { Text("Kitchen", widgetId: "w-kitchen"), Text("Hall", widgetId: "w-hall") };
        var key = SitemapControlFactory.BuildRowIdentityKey(rows[1]);

        var found = SitemapRowPlanner.TryResolveRowIndex(rows, key, out var rowIndex);

        Assert.True(found);
        Assert.Equal(1, rowIndex);
    }

    private static SitemapRowDescriptor Text(string label, string? widgetId = null) =>
        new(label, null, RenderControlKind.Text, RenderActionKind.None, RenderDensity.Compact, [], WidgetId: widgetId);

    private static SitemapRowDescriptor Grid(string label) =>
        new(label, null, RenderControlKind.ButtonGrid, RenderActionKind.SendCommand, RenderDensity.Compact, []);

    private static SitemapRowDescriptor Button(string label, string command, bool visible) =>
        new(label, command, RenderControlKind.Button, RenderActionKind.SendCommand, RenderDensity.Compact, [],
            Command: command,
            IsVisible: visible);
}
```

- [ ] **Step 2: Run tests and confirm failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter SitemapRowPlannerTests
```

Expected: failure because planner does not exist.

- [ ] **Step 3: Add `SitemapVisualRow`**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapVisualRow.cs`:

```csharp
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed record SitemapVisualRow(
    int RowIndex,
    SitemapRowDescriptor Row,
    int NextDescriptorIndex);
```

- [ ] **Step 4: Add `SitemapRowPlanner`**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapRowPlanner.cs`:

```csharp
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public static class SitemapRowPlanner
{
    public static IReadOnlyList<SitemapVisualRow> BuildVisualRows(IReadOnlyList<SitemapRowDescriptor> rows)
    {
        var visualRows = new List<SitemapVisualRow>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Control == RenderControlKind.Button)
            {
                continue;
            }

            if (row.Control == RenderControlKind.ButtonGrid)
            {
                var merged = BuildMergedButtonGridRow(index, rows, out var nextIndex);
                visualRows.Add(new SitemapVisualRow(index, merged, nextIndex));
                index = nextIndex - 1;
                continue;
            }

            visualRows.Add(new SitemapVisualRow(index, row, index + 1));
        }

        return visualRows;
    }

    public static IReadOnlyList<int> ExpandChangedIndices(IReadOnlyList<int> changedIndices, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        var set = new SortedSet<int>();
        foreach (var index in changedIndices)
        {
            if (index < 0 || index >= rows.Count)
            {
                continue;
            }

            var effective = index;
            if (rows[index].Control == RenderControlKind.Button)
            {
                for (var scan = index - 1; scan >= 0; scan--)
                {
                    if (rows[scan].Control == RenderControlKind.ButtonGrid)
                    {
                        effective = scan;
                        break;
                    }

                    if (rows[scan].Control != RenderControlKind.Button)
                    {
                        break;
                    }
                }
            }

            if (rows[effective].Control != RenderControlKind.Button)
            {
                set.Add(effective);
            }
        }

        return set.ToArray();
    }

    public static bool TryResolveRowIndex(IReadOnlyList<SitemapRowDescriptor>? rows, string rowKey, out int rowIndex)
    {
        if (rows is null)
        {
            rowIndex = -1;
            return false;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(SitemapControlFactory.BuildRowIdentityKey(rows[index]), rowKey, StringComparison.Ordinal))
            {
                rowIndex = index;
                return true;
            }
        }

        rowIndex = -1;
        return false;
    }

    public static int CountVisualRows(IReadOnlyList<SitemapRowDescriptor> rows) => BuildVisualRows(rows).Count;

    public static SitemapRowDescriptor BuildMergedButtonGridRow(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows)
    {
        return BuildMergedButtonGridRow(gridIndex, rows, out _);
    }

    private static SitemapRowDescriptor BuildMergedButtonGridRow(int gridIndex, IReadOnlyList<SitemapRowDescriptor> rows, out int nextIndex)
    {
        var row = rows[gridIndex];
        var childOptions = new List<SitemapMapOption>();
        var scan = gridIndex + 1;
        while (scan < rows.Count && rows[scan].Control == RenderControlKind.Button)
        {
            var child = rows[scan];
            var command = child.Command ?? child.RawItemState ?? child.RawState ?? child.State ?? string.Empty;
            var isActive = string.Equals(child.RawItemState ?? child.RawState ?? child.State, "ON", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(child.Command, "ON", StringComparison.OrdinalIgnoreCase);
            childOptions.Add(new SitemapMapOption(
                command,
                child.Label,
                child.GridRow,
                child.GridColumn,
                isActive,
                child.Command,
                child.ReleaseCommand,
                child.Stateless,
                scan));
            scan++;
        }

        nextIndex = scan;
        var visibleChildOptions = childOptions.Where(option => option.SourceRowIndex.HasValue && rows[option.SourceRowIndex.Value].IsVisible).ToList();
        if (visibleChildOptions.Count > 0)
        {
            childOptions = visibleChildOptions;
        }

        return childOptions.Count > 0 ? row with { SelectionOptions = childOptions } : row;
    }
}
```

- [ ] **Step 5: Run row planner tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter SitemapRowPlannerTests
```

Expected: tests pass.

## Task 3: Shared Icon Auth and Dispatcher Replay

- [ ] **Step 1: Add icon auth resolver**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapIconAuthResolver.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed class SitemapIconAuthResolver(AppSettingsController settingsController)
{
    public SitemapControlFactory.IconAuthContext Resolve(TransportKind transportKind)
    {
        if (transportKind == TransportKind.Local)
        {
            return new SitemapControlFactory.IconAuthContext(
                ApiToken: GetApiToken(TransportKind.Local),
                BasicUserName: null,
                BasicPassword: null,
                TransportKind: transportKind);
        }

        var cloudCredentials = GetCloudCredentials();
        return new SitemapControlFactory.IconAuthContext(
            ApiToken: null,
            BasicUserName: cloudCredentials?.UserName,
            BasicPassword: cloudCredentials?.Password,
            TransportKind: transportKind);
    }

    private string? GetApiToken(TransportKind kind)
    {
        try { return settingsController.GetApiTokenAsync(kind, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private CloudCredentials? GetCloudCredentials()
    {
        try { return settingsController.GetCloudCredentialsAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { return null; }
    }
}
```

- [ ] **Step 2: Add dispatcher gate tests**

Create `tests/OpenHab.App.Tests/SitemapSurface/DispatcherRefreshGateTests.cs`:

```csharp
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.SitemapSurface;

public sealed class DispatcherRefreshGateTests
{
    [Fact]
    public void RequestRecordsPendingRefreshWhenEnqueueFails()
    {
        var gate = new DispatcherRefreshGate(_ => false);
        var refreshes = 0;

        gate.Request(() => refreshes++);

        Assert.Equal(0, refreshes);
        Assert.True(gate.HasPendingRefresh);
    }

    [Fact]
    public void DrainRunsOnePendingRefresh()
    {
        var queue = new Queue<Action>();
        var gate = new DispatcherRefreshGate(action =>
        {
            queue.Enqueue(action);
            return true;
        });
        var refreshes = 0;

        gate.Request(() => refreshes++);
        gate.Drain(() => refreshes++);
        while (queue.TryDequeue(out var action))
        {
            action();
        }

        Assert.Equal(1, refreshes);
        Assert.False(gate.HasPendingRefresh);
    }
}
```

- [ ] **Step 3: Add dispatcher gate**

Create `src/OpenHab.Windows.Tray/Rendering/DispatcherRefreshGate.cs`:

```csharp
namespace OpenHab.Windows.Tray.Rendering;

public sealed class DispatcherRefreshGate(Func<Action, bool> tryEnqueue)
{
    private int pendingRefresh;

    public bool HasPendingRefresh => Interlocked.CompareExchange(ref pendingRefresh, 0, 0) == 1;

    public void Request(Action refresh)
    {
        if (!tryEnqueue(() =>
            {
                Interlocked.Exchange(ref pendingRefresh, 0);
                refresh();
            }))
        {
            Interlocked.Exchange(ref pendingRefresh, 1);
        }
    }

    public void Drain(Action refresh)
    {
        if (Interlocked.Exchange(ref pendingRefresh, 0) == 0)
        {
            return;
        }

        Request(refresh);
    }
}
```

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "SitemapRowPlannerTests|DispatcherRefreshGateTests"
```

Expected: tests pass.

## Task 4: Shared Surface Renderer

- [ ] **Step 1: Add renderer**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs` using the API from the parent plan:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering.SitemapSurface;

public sealed class SitemapSurfaceRenderer(
    AppSettingsController settingsController,
    SitemapIconAuthResolver iconAuthResolver,
    Func<string, Task> activateByRowKey,
    Func<string, Task> navigateByRowKey,
    Func<string, string, Task> sendCommandByRowKey,
    Func<int, string, Task> sendCommandByRowIndex)
{
    private sealed record RenderedRowTag(int RowIndex, string RowKey, string VisualStateKey);

    public void Refresh(StackPanel rowsPanel, SitemapRuntimeSnapshot snapshot)
    {
        var rows = snapshot.Descriptor?.Rows;
        if (rows is null)
        {
            rowsPanel.Children.Clear();
            return;
        }

        if (snapshot.ChangedRowIndices is { Count: > 0 })
        {
            RefreshChangedRows(rowsPanel, rows, snapshot);
            return;
        }

        if (rowsPanel.Children.Count == SitemapRowPlanner.CountVisualRows(rows))
        {
            return;
        }

        rowsPanel.Children.Clear();
        foreach (var visualRow in SitemapRowPlanner.BuildVisualRows(rows))
        {
            var element = CreateRowElement(visualRow.RowIndex, visualRow.Row, snapshot);
            rowsPanel.Children.Add(element);
            SitemapControlFactory.SetVisibility(element, visualRow.Row.IsVisible);
        }
    }

    private void RefreshChangedRows(StackPanel rowsPanel, IReadOnlyList<SitemapRowDescriptor> rows, SitemapRuntimeSnapshot snapshot)
    {
        foreach (var index in SitemapRowPlanner.ExpandChangedIndices(snapshot.ChangedRowIndices, rows))
        {
            if (!TryFindRenderedRow(rowsPanel, index, out var existing, out var childIndex))
            {
                continue;
            }

            var row = rows[index].Control == RenderControlKind.ButtonGrid
                ? SitemapRowPlanner.BuildMergedButtonGridRow(index, rows)
                : rows[index];

            if (ShouldRebuild(existing, row, index))
            {
                rowsPanel.Children.RemoveAt(childIndex);
                rowsPanel.Children.Insert(childIndex, CreateRowElement(index, row, snapshot));
                continue;
            }

            SitemapControlFactory.UpdateState(existing, row);
            SetRenderedRowTag(existing, index, row);
        }
    }

    private FrameworkElement CreateRowElement(int index, SitemapRowDescriptor row, SitemapRuntimeSnapshot snapshot)
    {
        var iconTransport = snapshot.ActiveTransport ?? TransportKind.Local;
        var iconBaseUri = iconTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var iconAuth = iconAuthResolver.Resolve(iconTransport);

        if (row.Control == RenderControlKind.ButtonGrid)
        {
            Func<SitemapMapOption, bool, Task> sendGridCommand = (option, isRelease) =>
            {
                var expectedCommand = isRelease ? option.ReleaseCommand : option.ClickCommand ?? option.Command;
                if (string.IsNullOrWhiteSpace(expectedCommand) ||
                    string.Equals(expectedCommand, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                return option.SourceRowIndex.HasValue
                    ? sendCommandByRowIndex(option.SourceRowIndex.Value, expectedCommand)
                    : sendCommandByRowIndex(index, expectedCommand);
            };

            var element = SitemapControlFactory.Create(
                row,
                activateRow: null,
                sendCommand: null,
                iconBaseUri,
                settingsController.Current.UseWindows11Icons,
                iconAuth,
                chartDpi: (int)settingsController.Current.ChartQuality,
                sendButtonGridCommand: sendGridCommand);
            SetRenderedRowTag(element, index, row);
            return element;
        }

        var rowKey = SitemapControlFactory.BuildRowIdentityKey(row);
        Func<Task>? activateRow = row.Action switch
        {
            RenderActionKind.Navigate => () => navigateByRowKey(rowKey),
            RenderActionKind.SendCommand when row.Control == RenderControlKind.Toggle => () => activateByRowKey(rowKey),
            _ => null
        };
        Func<string, Task>? sendCommand = row.Action == RenderActionKind.SendCommand
            ? command => sendCommandByRowKey(rowKey, command)
            : null;

        var created = SitemapControlFactory.Create(
            row,
            activateRow,
            sendCommand,
            iconBaseUri,
            settingsController.Current.UseWindows11Icons,
            iconAuth,
            chartDpi: (int)settingsController.Current.ChartQuality);
        SetRenderedRowTag(created, index, row);
        return created;
    }

    private static bool TryFindRenderedRow(StackPanel rowsPanel, int rowIndex, out FrameworkElement element, out int childIndex)
    {
        for (var i = 0; i < rowsPanel.Children.Count; i++)
        {
            if (rowsPanel.Children[i] is FrameworkElement candidate &&
                candidate.Tag is RenderedRowTag tag &&
                tag.RowIndex == rowIndex)
            {
                element = candidate;
                childIndex = i;
                return true;
            }
        }

        element = null!;
        childIndex = -1;
        return false;
    }

    private static void SetRenderedRowTag(FrameworkElement element, int rowIndex, SitemapRowDescriptor row)
    {
        element.Tag = new RenderedRowTag(
            rowIndex,
            SitemapControlFactory.BuildRowIdentityKey(row),
            SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex));
    }

    private static bool ShouldRebuild(FrameworkElement element, SitemapRowDescriptor row, int rowIndex)
    {
        return element.Tag is RenderedRowTag tag &&
               !string.Equals(tag.VisualStateKey, SitemapControlFactory.BuildRowVisualStateKey(row, rowIndex), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: build succeeds before window wiring.

## Task 5: Wire Flyout and Main Window

- [ ] **Step 1: Wire `FlyoutWindow`**

In `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`, add fields:

```csharp
private readonly SitemapSurfaceRenderer sitemapSurfaceRenderer;
private readonly DispatcherRefreshGate snapshotRefreshGate;
private readonly DispatcherRefreshGate notificationRefreshGate;
```

In the constructor after assignments, initialize:

```csharp
var iconAuthResolver = new SitemapIconAuthResolver(settingsController);
sitemapSurfaceRenderer = new SitemapSurfaceRenderer(
    settingsController,
    iconAuthResolver,
    activateByRowKey: OnRowActivatedByKeyAsync,
    navigateByRowKey: OnRowNavigateByKeyAsync,
    sendCommandByRowKey: SendCommandForRowKeyAsync,
    sendCommandByRowIndex: (rowIndex, command) => runtimeController.SendCommandForRowAsync(rowIndex, command));
snapshotRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(action));
notificationRefreshGate = new DispatcherRefreshGate(action => DispatcherQueue.TryEnqueue(action));
```

Replace snapshot enqueue with:

```csharp
snapshotRefreshGate.Request(() => RefreshRuntimeBindings(targetRows: null));
```

Replace notification enqueue with:

```csharp
notificationRefreshGate.Request(RefreshNotificationBadge);
```

Replace `RefreshRuntimeBindings` with:

```csharp
internal void RefreshRuntimeBindings(StackPanel? targetRows = null)
{
    var rowsPanel = targetRows ?? ActiveRows;
    var snapshot = runtimeController.Current;
    RefreshChromeBindings(snapshot);
    sitemapSurfaceRenderer.Refresh(rowsPanel, snapshot);
    snapshotRefreshGate.Drain(() => RefreshRuntimeBindings(targetRows: null));
}
```

Update `TryResolveCurrentRowIndex` to:

```csharp
private bool TryResolveCurrentRowIndex(string rowKey, out int rowIndex)
{
    return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out rowIndex);
}
```

Delete flyout-local row planner and icon-auth methods after compilation succeeds.

- [ ] **Step 2: Wire `MainWindow`**

In `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`, add the same renderer/gate fields and constructor initialization as flyout.

Add helper methods:

```csharp
private Task OnRowActivatedByKeyAsync(string rowKey)
{
    return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
        ? OnRowActivatedAsync(rowIndex)
        : Task.CompletedTask;
}

private Task OnRowNavigateByKeyAsync(string rowKey)
{
    return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
        ? OnRowNavigateAsync(rowIndex)
        : Task.CompletedTask;
}

private Task SendCommandForRowKeyAsync(string rowKey, string command)
{
    return SitemapRowPlanner.TryResolveRowIndex(runtimeController.Current.Descriptor?.Rows, rowKey, out var rowIndex)
        ? runtimeController.SendCommandForRowAsync(rowIndex, command)
        : Task.CompletedTask;
}
```

Replace `RefreshRuntimeBindings` with the same shared renderer version used by flyout.

Replace notification enqueue with:

```csharp
notificationRefreshGate.Request(RefreshNotificationList);
```

Delete main-window duplicate icon-auth and ButtonGrid merge logic after compilation succeeds.

- [ ] **Step 3: Build after window wiring**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: build succeeds.

## Task 6: Chart Cache-Bust Policy

- [ ] **Step 1: Add chart URL test**

Append to `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`:

```csharp
[Fact]
public void BuildChartUrl_UsesStableUrlWhenCacheBustDisabled()
{
    var row = new SitemapRowDescriptor(
        "Power", "12", RenderControlKind.Chart, RenderActionKind.None, RenderDensity.Compact,
        [], ItemName: "Weather_Temperature", Period: "D");
    var baseUri = new Uri("http://localhost:8080/");

    var first = SitemapControlFactory.BuildChartUrl(row, baseUri, chartDpi: 192, cacheBust: false);
    var second = SitemapControlFactory.BuildChartUrl(row, baseUri, chartDpi: 192, cacheBust: false);

    Assert.Equal(first, second);
    Assert.DoesNotContain("random=", first!.ToString());
}
```

- [ ] **Step 2: Run test and confirm failure**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter BuildChartUrl_UsesStableUrlWhenCacheBustDisabled
```

Expected: compile failure because `cacheBust` parameter does not exist.

- [ ] **Step 3: Add cache-bust parameter**

In `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`, change `BuildChartUrl` signature to:

```csharp
public static Uri? BuildChartUrl(SitemapRowDescriptor row, Uri? baseUri, int chartDpi = 96, bool cacheBust = true)
```

Only append the random query when `cacheBust` is true:

```csharp
if (cacheBust)
{
    query.Add($"random={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
}
```

Keep existing callers on the default behavior.

## Task 7: Verify and Commit

- [ ] **Step 1: Run selected tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "SitemapRowPlannerTests|DispatcherRefreshGateTests|SitemapControlFactoryTests"
```

Expected: selected tests pass.

- [ ] **Step 2: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke check**

Run the app using the repo's normal local run path. Verify:

- Flyout and main window load the configured sitemap.
- ButtonGrid rows render as one visual row.
- Hidden ButtonGrid child buttons do not render as visible options.
- Toggle rows dispatch commands.
- Navigation rows move forward and back in both windows.
- Main window no longer applies changed row indices directly to the wrong visual child when ButtonGrid children are present.
- Local token and cloud credentials still load icons.

- [ ] **Step 4: Commit**

Run:

```powershell
git add src/OpenHab.Windows.Tray tests/OpenHab.App.Tests
git commit -m "refactor: share Windows sitemap surface coordination"
```

Expected: commit succeeds.

## Handoff

Merge this branch after Sessions 1, 2, and 3. During integration, resolve conflicts in `FlyoutWindow.xaml.cs` and `MainWindow.xaml.cs` by keeping the shared renderer, key-based command routing, and dispatcher replay gate.
