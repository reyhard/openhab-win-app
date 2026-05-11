# Sitemap Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add flyout sitemap search that renders a live virtual results page for labels in the current page subtree.

**Architecture:** `SitemapRuntimeController` owns search state, source resolution, descriptor recomputation, and live update behavior. The flyout owns only chrome: search button, replacing breadcrumbs with a search box, focus, clear, and escape handling. `SitemapSurfaceRenderer` continues to render ordinary `SitemapRenderDescriptor` instances; synthetic result identity is carried as descriptor metadata, not through a parallel renderer.

**Tech Stack:** .NET 10, C#, WinUI / Windows App SDK, xUnit.

---

## File Structure

- Modify `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`: add optional search/source metadata to `SitemapRowDescriptor`.
- Modify `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`: prefer synthetic search row keys when building row identity keys.
- Create `src/OpenHab.App/Runtime/SitemapSearchModels.cs`: search query/result/source records.
- Create `src/OpenHab.App/Runtime/SitemapSearchDescriptorBuilder.cs`: UI-independent search traversal and virtual descriptor construction.
- Modify `src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs`: expose active search state, query, and result count for flyout chrome.
- Modify `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`: search lifecycle, active descriptor recomputation, live SSE/reconcile integration, and search result action routing.
- Modify `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`: add search button and replaceable breadcrumb/search row.
- Modify `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`: bind search chrome to runtime state and route row actions through runtime search-aware methods.
- Add tests in `tests/OpenHab.App.Tests/Runtime/SitemapSearchDescriptorBuilderTests.cs`.
- Extend tests in `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`.
- Extend tests in `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs` if this file already covers row identity.

---

### Task 1: Descriptor Metadata And Row Identity

**Files:**
- Modify: `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Test: `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`

- [ ] **Step 1: Write failing row identity test**

Add this test to `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs` near the existing `BuildRowIdentityKey` tests:

```csharp
[Fact]
public void BuildRowIdentityKey_PrefersSearchResultKey()
{
    var row = new SitemapRowDescriptor(
        "Lampka nocna",
        "OFF",
        RenderControlKind.Toggle,
        RenderActionKind.SendCommand,
        RenderDensity.Comfortable,
        [],
        ItemName: "Bedroom_Lamp",
        WidgetId: "real-widget-id",
        SearchResultKey: "search:home/lights/bedroom-lamp");

    Assert.Equal("search:home/lights/bedroom-lamp", SitemapControlFactory.BuildRowIdentityKey(row));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter BuildRowIdentityKey_PrefersSearchResultKey
```

Expected: FAIL because `SitemapRowDescriptor` does not have `SearchResultKey`.

- [ ] **Step 3: Add descriptor metadata**

In `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`, append these optional parameters to `SitemapRowDescriptor` after `HeightRows`:

```csharp
    int? HeightRows = null,
    string? SearchResultKey = null,
    string? SourcePageId = null,
    string? SourceWidgetId = null)
```

Update the convenience constructor call at the bottom of the record to pass the new values:

```csharp
        : this(Label, State, Control, Action, Density, [], null, null, null, null, null, false, null, null, null, null, true, null, null, null, null, null, true, null, null, null, SitemapInputHint.Auto, null, null, null, null, null)
```

- [ ] **Step 4: Prefer synthetic search key in row identity**

In `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`, change the start of `BuildRowIdentityKey` to:

```csharp
    internal static string BuildRowIdentityKey(SitemapRowDescriptor row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!string.IsNullOrWhiteSpace(row.SearchResultKey))
        {
            return row.SearchResultKey;
        }

        if (!string.IsNullOrWhiteSpace(row.WidgetId))
```

- [ ] **Step 5: Run test to verify it passes**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter BuildRowIdentityKey_PrefersSearchResultKey
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs
git commit -m "Add sitemap search row identity metadata"
```

---

### Task 2: Search Descriptor Builder

**Files:**
- Create: `src/OpenHab.App/Runtime/SitemapSearchModels.cs`
- Create: `src/OpenHab.App/Runtime/SitemapSearchDescriptorBuilder.cs`
- Test: `tests/OpenHab.App.Tests/Runtime/SitemapSearchDescriptorBuilderTests.cs`

- [ ] **Step 1: Write failing builder tests**

Create `tests/OpenHab.App.Tests/Runtime/SitemapSearchDescriptorBuilderTests.cs`:

```csharp
using OpenHab.App.Runtime;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;
using System.IO;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapSearchDescriptorBuilderTests
{
    private readonly SitemapRenderController renderController = new(new AppSettingsController(
        settingsFilePath: Path.Combine(Path.GetTempPath(), "OpenHab.App.Tests", Guid.NewGuid().ToString("N"), "settings.json")));

    [Fact]
    public void EmptyQueryReturnsNormalDescriptorAndNoSources()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "   ", renderController);

        Assert.False(result.IsSearchActive);
        Assert.Equal(normal, result.Descriptor);
        Assert.Empty(result.SourcesByResultKey);
    }

    [Fact]
    public void QueryMatchesVisibleLabelsOnly()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Lampka", renderController);

        Assert.True(result.IsSearchActive);
        Assert.Equal(2, result.ResultCount);
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Lampka nocna");
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Lampka mobilna");
        Assert.DoesNotContain(result.Descriptor.Rows, row => row.Label == "Hidden Lampka");
        Assert.DoesNotContain(result.Descriptor.Rows, row => row.Label == "Desk");
    }

    [Fact]
    public void QueryIgnoresItemNamesAndStateValues()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var byItem = SitemapSearchDescriptorBuilder.Build(page, normal, "Kitchen_Light", renderController);
        var byState = SitemapSearchDescriptorBuilder.Build(page, normal, "22.5", renderController);

        Assert.Equal(0, byItem.ResultCount);
        Assert.Equal(0, byState.ResultCount);
        Assert.Contains("No matching sitemap elements", byItem.Descriptor.Rows[0].Label, StringComparison.Ordinal);
        Assert.Contains("No matching sitemap elements", byState.Descriptor.Rows[0].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameMatchIncludesVisibleChildren()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Automatyka", renderController);

        Assert.True(result.IsSearchActive);
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Automatyka świateł" && row.IsSectionHeader);
        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Tryb lampki");
        Assert.DoesNotContain(result.Descriptor.Rows, row => row.Label == "Hidden Child");
    }

    [Fact]
    public void ChildPageMatchIncludesGroupingContextAndSourceMetadata()
    {
        var page = CreateSearchPage();
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Timer", renderController);

        Assert.Contains(result.Descriptor.Rows, row => row.Label == "Automatyka świateł" && row.IsSectionHeader);
        var timer = Assert.Single(result.Descriptor.Rows.Where(row => row.Label == "Timer"));
        Assert.NotNull(timer.SearchResultKey);
        Assert.Equal("automation", timer.SourcePageId);
        Assert.Equal("timer-widget", timer.SourceWidgetId);
        Assert.True(result.SourcesByResultKey.ContainsKey(timer.SearchResultKey!));
    }

    [Fact]
    public void RecomputedResultsFollowLatestSitemapOrder()
    {
        var page = CreateSearchPage(lampsReversed: true);
        var normal = renderController.BuildCurrentDescriptor(page);

        var result = SitemapSearchDescriptorBuilder.Build(page, normal, "Lampka", renderController);
        var labels = result.Descriptor.Rows
            .Where(row => row.Label.StartsWith("Lampka", StringComparison.Ordinal))
            .Select(row => row.Label)
            .ToArray();

        Assert.Equal(["Lampka mobilna", "Lampka nocna"], labels);
    }

    private static NormalizedSitemapPage CreateSearchPage(bool lampsReversed = false)
    {
        var lampA = new SitemapWidget(
            "Lampka nocna",
            SitemapWidgetType.Switch,
            "Bedroom_Lamp",
            "OFF",
            [],
            true,
            [],
            WidgetId: "lamp-night");
        var lampB = new SitemapWidget(
            "Lampka mobilna",
            SitemapWidgetType.Switch,
            "Mobile_Lamp",
            "OFF",
            [],
            true,
            [],
            WidgetId: "lamp-mobile");

        var lamps = lampsReversed ? new[] { lampB, lampA } : [lampA, lampB];
        var widgets = new List<SitemapWidget>();
        widgets.AddRange(lamps);
        widgets.Add(new SitemapWidget("Hidden Lampka", SitemapWidgetType.Text, "Hidden_Lamp", "ON", [], false, []));
        widgets.Add(new SitemapWidget("Desk", SitemapWidgetType.Text, "Kitchen_Light", "22.5", [], true, []));
        widgets.Add(new SitemapWidget(
            "Automatyka świateł",
            SitemapWidgetType.Frame,
            null,
            null,
            [],
            true,
            [
                new SitemapPage("automation", "Automatyka świateł", [
                    new SitemapWidget("Tryb lampki", SitemapWidgetType.Text, "Lamp_Mode", "AUTO", [], true, [], WidgetId: "mode-widget"),
                    new SitemapWidget("Timer", SitemapWidgetType.Text, "Lamp_Timer", "10", [], true, [], WidgetId: "timer-widget"),
                    new SitemapWidget("Hidden Child", SitemapWidgetType.Text, "Hidden_Child", "ON", [], false, [])
                ])
            ],
            WidgetId: "automation-frame"));

        return SitemapNormalizer.Normalize(new SitemapPage("home", "Home", widgets));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter SitemapSearchDescriptorBuilderTests
```

Expected: FAIL because `SitemapSearchDescriptorBuilder` and search models do not exist.

- [ ] **Step 3: Add search models**

Create `src/OpenHab.App/Runtime/SitemapSearchModels.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;

namespace OpenHab.App.Runtime;

public enum SitemapSearchMatchKind
{
    Row,
    Frame,
    ChildRow
}

public sealed record SitemapSearchSource(
    string ResultKey,
    SitemapSearchMatchKind MatchKind,
    string SourcePageId,
    IReadOnlyList<string> SourcePagePath,
    string? SourceWidgetId,
    string SourceWidgetPath,
    string SourceWidgetLabel,
    SitemapWidgetType SourceWidgetType,
    int? CurrentPageRowIndex);

public sealed record SitemapSearchBuildResult(
    SitemapRenderDescriptor Descriptor,
    bool IsSearchActive,
    string Query,
    int ResultCount,
    IReadOnlyDictionary<string, SitemapSearchSource> SourcesByResultKey);
```

- [ ] **Step 4: Add descriptor builder**

Create `src/OpenHab.App/Runtime/SitemapSearchDescriptorBuilder.cs`:

```csharp
using System.Globalization;
using OpenHab.App.Sitemaps;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.App.Runtime;

public static class SitemapSearchDescriptorBuilder
{
    private const string SearchPageId = "__search__";

    public static SitemapSearchBuildResult Build(
        NormalizedSitemapPage currentPage,
        SitemapRenderDescriptor normalDescriptor,
        string? query,
        SitemapRenderController renderController)
    {
        ArgumentNullException.ThrowIfNull(currentPage);
        ArgumentNullException.ThrowIfNull(normalDescriptor);
        ArgumentNullException.ThrowIfNull(renderController);

        var normalizedQuery = (query ?? string.Empty).Trim();
        if (normalizedQuery.Length == 0)
        {
            return new SitemapSearchBuildResult(normalDescriptor, false, string.Empty, 0, new Dictionary<string, SitemapSearchSource>());
        }

        var rows = new List<SitemapRowDescriptor>
        {
            CreateHeaderRow("Search results", $"Current section and child pages")
        };
        var sources = new Dictionary<string, SitemapSearchSource>(StringComparer.Ordinal);
        var resultCount = 0;

        AddMatchesFromPage(
            currentPage,
            normalDescriptor,
            renderController,
            currentPage,
            normalizedQuery,
            [currentPage.Label],
            rows,
            sources,
            ref resultCount,
            includeCurrentPageIndices: true);

        if (resultCount == 0)
        {
            rows.Add(new SitemapRowDescriptor(
                "No matching sitemap elements",
                null,
                RenderControlKind.Text,
                RenderActionKind.None,
                RenderDensity.Comfortable,
                [],
                IsVisible: true));
        }
        else
        {
            rows[0] = CreateHeaderRow("Search results", $"{resultCount.ToString(CultureInfo.InvariantCulture)} results in current section and child pages");
        }

        var descriptor = new SitemapRenderDescriptor(normalDescriptor.Skin, SearchPageId, "Search results", rows);
        return new SitemapSearchBuildResult(descriptor, true, normalizedQuery, resultCount, sources);
    }

    private static void AddMatchesFromPage(
        NormalizedSitemapPage rootPage,
        SitemapRenderDescriptor rootDescriptor,
        SitemapRenderController renderController,
        NormalizedSitemapPage page,
        string query,
        IReadOnlyList<string> pagePath,
        List<SitemapRowDescriptor> rows,
        Dictionary<string, SitemapSearchSource> sources,
        ref int resultCount,
        bool includeCurrentPageIndices)
    {
        var pageDescriptor = string.Equals(page.Id, rootPage.Id, StringComparison.Ordinal)
            ? rootDescriptor
            : renderController.BuildCurrentDescriptor(page);

        for (var index = 0; index < page.Widgets.Count; index++)
        {
            var widget = page.Widgets[index];
            if (!widget.IsVisible)
            {
                continue;
            }

            var row = pageDescriptor.Rows[index];
            var labelMatches = Matches(widget.Label, query);
            var widgetPath = BuildWidgetPath(page.Id, index, widget);
            var source = new SitemapSearchSource(
                BuildResultKey(page.Id, index, widget),
                labelMatches && widget.Type == SitemapWidgetType.Frame ? SitemapSearchMatchKind.Frame : SitemapSearchMatchKind.Row,
                page.Id,
                pagePath,
                widget.WidgetId,
                widgetPath,
                widget.Label,
                widget.Type,
                includeCurrentPageIndices ? index : null);

            if (labelMatches)
            {
                rows.Add(ToSearchRow(row, source));
                sources[source.ResultKey] = source;
                resultCount++;
            }

            foreach (var child in widget.Children)
            {
                var normalizedChild = SitemapNormalizer.Normalize(child);
                var childPath = pagePath.Concat([normalizedChild.Label]).ToArray();
                var childRowsBefore = rows.Count;
                var childResultCountBefore = resultCount;

                AddMatchesFromPage(
                    rootPage,
                    rootDescriptor,
                    renderController,
                    normalizedChild,
                    query,
                    childPath,
                    rows,
                    sources,
                    ref resultCount,
                    includeCurrentPageIndices: false);

                if ((labelMatches && widget.Type == SitemapWidgetType.Frame) || resultCount > childResultCountBefore)
                {
                    InsertGroupHeaderIfMissing(rows, childRowsBefore, widget.Label);
                }
            }
        }
    }

    private static bool Matches(string label, string query) =>
        label.Contains(query, StringComparison.InvariantCultureIgnoreCase);

    private static SitemapRowDescriptor ToSearchRow(SitemapRowDescriptor row, SitemapSearchSource source)
    {
        return row with
        {
            SearchResultKey = source.ResultKey,
            SourcePageId = source.SourcePageId,
            SourceWidgetId = source.SourceWidgetId
        };
    }

    private static SitemapRowDescriptor CreateHeaderRow(string label, string? state)
    {
        return new SitemapRowDescriptor(
            label,
            state,
            RenderControlKind.Text,
            RenderActionKind.None,
            RenderDensity.Comfortable,
            [],
            IsSectionHeader: true,
            IsVisible: true);
    }

    private static void InsertGroupHeaderIfMissing(List<SitemapRowDescriptor> rows, int insertIndex, string label)
    {
        if (insertIndex < rows.Count && rows[insertIndex].Label == label && rows[insertIndex].IsSectionHeader)
        {
            return;
        }

        rows.Insert(insertIndex, CreateHeaderRow(label, null));
    }

    private static string BuildResultKey(string pageId, int index, NormalizedSitemapWidget widget)
    {
        if (!string.IsNullOrWhiteSpace(widget.WidgetId))
        {
            return $"search:widget:{widget.WidgetId}";
        }

        return $"search:path:{BuildWidgetPath(pageId, index, widget)}";
    }

    private static string BuildWidgetPath(string pageId, int index, NormalizedSitemapWidget widget)
    {
        var item = widget.ItemName ?? string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{pageId}/{widget.Type}/{widget.Label}/{item}/{widget.ActionSignature()}/{index}");
    }

    private static string ActionSignature(this NormalizedSitemapWidget widget)
    {
        return widget.CanNavigate ? "navigate" : widget.RequiresFallback ? "fallback" : "default";
    }
}
```

- [ ] **Step 5: Run builder tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter SitemapSearchDescriptorBuilderTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/OpenHab.App/Runtime/SitemapSearchModels.cs src/OpenHab.App/Runtime/SitemapSearchDescriptorBuilder.cs tests/OpenHab.App.Tests/Runtime/SitemapSearchDescriptorBuilderTests.cs
git commit -m "Add sitemap search descriptor builder"
```

---

### Task 3: Runtime Search Lifecycle

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs`
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Test: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`

- [ ] **Step 1: Write failing lifecycle tests**

Add these tests to `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`:

```csharp
[Fact]
public async Task ApplySearchQueryBuildsVirtualDescriptorWithoutChangingBreadcrumbs()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: true));
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

    await controller.LoadAsync();
    var normalDescriptor = controller.Current.Descriptor;
    var normalBreadcrumbs = controller.Current.Breadcrumbs.ToArray();

    controller.ApplySearchQuery("Lampka");

    Assert.True(controller.Current.IsSearchActive);
    Assert.Equal("Lampka", controller.Current.SearchQuery);
    Assert.NotEqual(normalDescriptor, controller.Current.Descriptor);
    Assert.Equal(normalBreadcrumbs, controller.Current.Breadcrumbs);
    Assert.Contains(controller.Current.Descriptor!.Rows, row => row.Label == "Lampka nocna");
}

[Fact]
public async Task ClearSearchRestoresNormalDescriptor()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: true));
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

    await controller.LoadAsync();
    var normalDescriptor = controller.Current.Descriptor;

    controller.ApplySearchQuery("Lampka");
    controller.ClearSearch();

    Assert.False(controller.Current.IsSearchActive);
    Assert.Equal(string.Empty, controller.Current.SearchQuery);
    Assert.Equal(normalDescriptor, controller.Current.Descriptor);
}

[Fact]
public async Task EmptySearchQueryRestoresNormalDescriptor()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: true));
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

    await controller.LoadAsync();
    var normalDescriptor = controller.Current.Descriptor;

    controller.ApplySearchQuery("Lampka");
    controller.ApplySearchQuery(" ");

    Assert.False(controller.Current.IsSearchActive);
    Assert.Equal(normalDescriptor, controller.Current.Descriptor);
}
```

Add this helper in the test class:

```csharp
private static string HomepageSearchJson(string lightState, bool lampVisible)
{
    var visible = lampVisible ? "true" : "false";
    return $$"""
        {
          "homepage": {
            "id": "home",
            "title": "Home",
            "widgets": [
              {
                "widgetId": "lamp-night",
                "type": "Switch",
                "label": "Lampka nocna [{{lightState}}]",
                "item": {
                  "name": "Bedroom_Lamp",
                  "state": "{{lightState}}"
                },
                "visibility": {{visible}}
              },
              {
                "widgetId": "desk",
                "type": "Text",
                "label": "Desk [ON]",
                "item": {
                  "name": "Desk_Item",
                  "state": "ON"
                },
                "visibility": true
              }
            ]
          }
        }
        """;
}
```

- [ ] **Step 2: Run lifecycle tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "ApplySearchQueryBuildsVirtualDescriptorWithoutChangingBreadcrumbs|ClearSearchRestoresNormalDescriptor|EmptySearchQueryRestoresNormalDescriptor"
```

Expected: FAIL because snapshot fields and controller methods do not exist.

- [ ] **Step 3: Extend runtime snapshot**

In `src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs`, add fields after `ChangedRowIndices`:

```csharp
    IReadOnlyList<int> ChangedRowIndices,
    bool IsSearchActive = false,
    string SearchQuery = "",
    int SearchResultCount = 0)
```

The existing `Initial` constructor can omit the optional values.

- [ ] **Step 4: Add runtime search fields and public methods**

In `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`, add fields:

```csharp
    private string searchQuery = string.Empty;
    private Dictionary<string, SitemapSearchSource> activeSearchSources = new(StringComparer.Ordinal);
    private SitemapRenderDescriptor? normalDescriptorBeforeSearch;
```

Add methods:

```csharp
    public void ApplySearchQuery(string? query)
    {
        searchQuery = (query ?? string.Empty).Trim();
        RebuildSearchDescriptor();
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSearch()
    {
        searchQuery = string.Empty;
        activeSearchSources.Clear();
        normalDescriptorBeforeSearch = null;
        if (currentPage is not null)
        {
            var descriptor = renderController.BuildCurrentDescriptor(currentPage);
            Current = Current with
            {
                Descriptor = descriptor,
                IsSearchActive = false,
                SearchQuery = string.Empty,
                SearchResultCount = 0,
                ChangedRowIndices = []
            };
        }
        else
        {
            Current = Current with
            {
                IsSearchActive = false,
                SearchQuery = string.Empty,
                SearchResultCount = 0,
                ChangedRowIndices = []
            };
        }
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildSearchDescriptor()
    {
        if (currentPage is null || Current.Descriptor is null)
        {
            activeSearchSources.Clear();
            Current = Current with { IsSearchActive = false, SearchQuery = string.Empty, SearchResultCount = 0 };
            return;
        }

        if (searchQuery.Length == 0)
        {
            activeSearchSources.Clear();
            normalDescriptorBeforeSearch = null;
            var descriptor = renderController.BuildCurrentDescriptor(currentPage);
            Current = Current with
            {
                Descriptor = descriptor,
                IsSearchActive = false,
                SearchQuery = string.Empty,
                SearchResultCount = 0,
                ChangedRowIndices = []
            };
            return;
        }

        var normalDescriptor = renderController.BuildCurrentDescriptor(currentPage);
        normalDescriptorBeforeSearch = normalDescriptor;
        var search = SitemapSearchDescriptorBuilder.Build(currentPage, normalDescriptor, searchQuery, renderController);
        activeSearchSources = new Dictionary<string, SitemapSearchSource>(search.SourcesByResultKey, StringComparer.Ordinal);
        Current = Current with
        {
            Descriptor = search.Descriptor,
            IsSearchActive = true,
            SearchQuery = search.Query,
            SearchResultCount = search.ResultCount,
            ChangedRowIndices = []
        };
    }

    private void ReapplySearchIfActive()
    {
        if (searchQuery.Length > 0)
        {
            RebuildSearchDescriptor();
        }
    }
```

- [ ] **Step 5: Reapply search after normal descriptor updates**

In `LoadDescriptorAsync`, `NavigateToChildAsync`, `NavigateBack`, `NavigateToBreadcrumb`, and `ReconcileCurrentPageAsync`, clear search on navigation and reapply search after refresh/reconcile.

Use these rules:

```csharp
// At the start of NavigateToChildAsync, NavigateBack, NavigateToBreadcrumb:
searchQuery = string.Empty;
activeSearchSources.Clear();
normalDescriptorBeforeSearch = null;
```

After successful descriptor assignment in `RefreshAsyncInternal` and `ReconcileCurrentPageAsync`, call:

```csharp
ReapplySearchIfActive();
```

When `ReapplySearchIfActive` is called, keep existing breadcrumbs unchanged.

- [ ] **Step 6: Run lifecycle tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "ApplySearchQueryBuildsVirtualDescriptorWithoutChangingBreadcrumbs|ClearSearchRestoresNormalDescriptor|EmptySearchQueryRestoresNormalDescriptor"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs src/OpenHab.App/Runtime/SitemapRuntimeController.cs tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs
git commit -m "Add sitemap runtime search lifecycle"
```

---

### Task 4: Search-Aware Runtime Actions

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`

- [ ] **Step 1: Write failing action routing tests**

Add these tests to `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`:

```csharp
[Fact]
public async Task ActivateSearchResultSendsCommandToSourceWidget()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: true));
    localClient.EnqueueSitemapJson(HomepageSearchJson("ON", lampVisible: true));
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

    await controller.LoadAsync();
    controller.ApplySearchQuery("Lampka");
    var row = Assert.Single(controller.Current.Descriptor!.Rows.Where(r => r.Label == "Lampka nocna"));

    var activated = await controller.ActivateRowByKeyAsync(row.SearchResultKey!);

    Assert.True(activated);
    Assert.Equal(("Bedroom_Lamp", "ON"), Assert.Single(localClient.CommandsSent));
}

[Fact]
public async Task StaleSearchResultDoesNotCommandWrongWidget()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: true));
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: false));
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

    await controller.LoadAsync();
    controller.ApplySearchQuery("Lampka");
    var row = Assert.Single(controller.Current.Descriptor!.Rows.Where(r => r.Label == "Lampka nocna"));
    await controller.RefreshAsync();

    var activated = await controller.ActivateRowByKeyAsync(row.SearchResultKey!);

    Assert.False(activated);
    Assert.Empty(localClient.CommandsSent);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "ActivateSearchResultSendsCommandToSourceWidget|StaleSearchResultDoesNotCommandWrongWidget"
```

Expected: FAIL because search-aware action methods do not exist.

- [ ] **Step 3: Add search-aware action methods**

In `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`, add methods:

```csharp
    public Task<bool> ActivateRowByKeyAsync(string rowKey, CancellationToken cancellationToken = default)
    {
        if (Current.IsSearchActive && activeSearchSources.TryGetValue(rowKey, out var source))
        {
            return ActivateSearchSourceAsync(source, cancellationToken);
        }

        return TryResolveCurrentDescriptorRow(rowKey, out var rowIndex)
            ? ActivateRowAsync(rowIndex, cancellationToken)
            : Task.FromResult(false);
    }

    public Task<bool> SendCommandForRowKeyAsync(string rowKey, string command, CancellationToken cancellationToken = default)
    {
        if (Current.IsSearchActive && activeSearchSources.TryGetValue(rowKey, out var source))
        {
            return SendCommandForSearchSourceAsync(source, command, cancellationToken);
        }

        return TryResolveCurrentDescriptorRow(rowKey, out var rowIndex)
            ? SendCommandForRowAsync(rowIndex, command, cancellationToken)
            : Task.FromResult(false);
    }

    public Task<bool> NavigateRowByKeyAsync(string rowKey, CancellationToken cancellationToken = default)
    {
        if (Current.IsSearchActive && activeSearchSources.TryGetValue(rowKey, out var source))
        {
            return NavigateToSearchSourceAsync(source, cancellationToken);
        }

        return TryResolveCurrentDescriptorRow(rowKey, out var rowIndex)
            ? NavigateToChildAsync(rowIndex, cancellationToken)
            : Task.FromResult(false);
    }
```

Add private helpers. The exact row key resolver must use the same logic as the descriptor metadata: if a row has `SearchResultKey`, compare that; else compare widget id, item, and label fallback. Keep this helper in `SitemapRuntimeController` rather than referencing Windows-layer `SitemapControlFactory`.

```csharp
    private bool TryResolveCurrentDescriptorRow(string rowKey, out int rowIndex)
    {
        rowIndex = -1;
        var rows = Current.Descriptor?.Rows;
        if (rows is null)
        {
            return false;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (BuildRuntimeRowKey(rows[index]) == rowKey)
            {
                rowIndex = index;
                return true;
            }
        }

        return false;
    }

    private static string BuildRuntimeRowKey(SitemapRowDescriptor row)
    {
        if (!string.IsNullOrWhiteSpace(row.SearchResultKey))
        {
            return row.SearchResultKey;
        }
        if (!string.IsNullOrWhiteSpace(row.WidgetId))
        {
            return $"widget:{row.WidgetId}";
        }
        if (!string.IsNullOrWhiteSpace(row.ItemName))
        {
            return $"item:{row.ItemName}:{row.Control}:{row.Action}:{row.Label}:{row.IconName ?? string.Empty}:{row.Command ?? string.Empty}:{row.ReleaseCommand ?? string.Empty}:{row.Period ?? string.Empty}";
        }
        return $"row:{row.Control}:{row.Action}:{row.IconName ?? string.Empty}:{row.Label}";
    }
```

Implement `ActivateSearchSourceAsync`, `SendCommandForSearchSourceAsync`, and `NavigateToSearchSourceAsync` by resolving the source widget against `currentPage`. Prefer widget id; if missing, resolve by source page id and source widget path. If zero or more than one candidate resolves, return `false` and call `RebuildSearchDescriptor()`.

- [ ] **Step 4: Update flyout to call runtime key methods**

In `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`, replace:

```csharp
    private Task OnRowActivatedByKeyAsync(string rowKey)
    {
        return TryResolveCurrentRowIndex(rowKey, out var rowIndex)
            ? OnRowActivatedAsync(rowIndex)
            : Task.CompletedTask;
    }
```

with:

```csharp
    private async Task OnRowActivatedByKeyAsync(string rowKey)
    {
        if (isRefreshing)
        {
            return;
        }

        await RunRuntimeOperationAsync(ct => runtimeController.ActivateRowByKeyAsync(rowKey, ct));
    }
```

Replace `OnRowNavigateByKeyAsync` with:

```csharp
    private async Task OnRowNavigateByKeyAsync(string rowKey)
    {
        if (isRefreshing)
        {
            return;
        }

        if (runtimeController.Current.IsSearchActive)
        {
            await RunRuntimeOperationAsync(ct => runtimeController.NavigateRowByKeyAsync(rowKey, ct));
            RefreshRuntimeBindings(ActiveRows);
            return;
        }

        if (TryResolveCurrentRowIndex(rowKey, out var rowIndex))
        {
            await OnRowNavigateAsync(rowIndex);
        }
    }
```

Replace `SendCommandForRowKeyAsync` with:

```csharp
    private Task SendCommandForRowKeyAsync(string rowKey, string command)
    {
        return runtimeController.SendCommandForRowKeyAsync(rowKey, command);
    }
```

- [ ] **Step 5: Run action routing tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "ActivateSearchResultSendsCommandToSourceWidget|StaleSearchResultDoesNotCommandWrongWidget"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/OpenHab.App/Runtime/SitemapRuntimeController.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs
git commit -m "Route sitemap search result actions"
```

---

### Task 5: Live SSE, Visibility, Refresh, And Ordering Behavior

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Test: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`

- [ ] **Step 1: Write failing live update tests**

Add these tests to `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`:

```csharp
[Fact]
public async Task SearchDescriptorUpdatesFromSitemapWidgetEventState()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: true));
    var eventClient = new FakeEventStreamClient();
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

    await controller.LoadAsync();
    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
    controller.ApplySearchQuery("Lampka");

    eventClient.FireWidgetEvent(new SitemapWidgetEvent("lamp-night", null, null, true, "Bedroom_Lamp", "ON", "default", "home", false));

    var row = Assert.Single(controller.Current.Descriptor!.Rows.Where(r => r.Label == "Lampka nocna"));
    Assert.Equal("ON", row.State);
    Assert.True(controller.Current.IsSearchActive);
}

[Fact]
public async Task SearchDescriptorRemovesResultWhenSitemapWidgetEventHidesIt()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchJson("OFF", lampVisible: true));
    var eventClient = new FakeEventStreamClient();
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient(), eventClient);

    await controller.LoadAsync();
    await controller.StartSitemapEventStreamAsync(new Uri("http://localhost:8080"), "default", "home");
    controller.ApplySearchQuery("Lampka");

    eventClient.FireWidgetEvent(new SitemapWidgetEvent("lamp-night", null, null, false, "Bedroom_Lamp", "OFF", "default", "home", false));

    Assert.DoesNotContain(controller.Current.Descriptor!.Rows, r => r.Label == "Lampka nocna");
    Assert.Contains(controller.Current.Descriptor!.Rows, r => r.Label == "No matching sitemap elements");
}

[Fact]
public async Task RefreshWhileSearchActiveRecomputesFromLatestSitemapOrder()
{
    var settings = CreateSettingsController();
    settings.SetSitemapName("default");
    var localClient = new FakeOpenHabClient();
    localClient.EnqueueSitemapJson(HomepageSearchOrderJson(reversed: false));
    localClient.EnqueueSitemapJson(HomepageSearchOrderJson(reversed: true));
    var controller = CreateRuntimeController(settings, localClient, new FakeOpenHabClient());

    await controller.LoadAsync();
    controller.ApplySearchQuery("Lampka");
    await controller.RefreshAsync();

    var labels = controller.Current.Descriptor!.Rows
        .Where(r => r.Label.StartsWith("Lampka", StringComparison.Ordinal))
        .Select(r => r.Label)
        .ToArray();
    Assert.Equal(["Lampka mobilna", "Lampka nocna"], labels);
}
```

Add helper:

```csharp
private static string HomepageSearchOrderJson(bool reversed)
{
    var first = reversed ? "Lampka mobilna" : "Lampka nocna";
    var second = reversed ? "Lampka nocna" : "Lampka mobilna";
    var firstId = reversed ? "lamp-mobile" : "lamp-night";
    var secondId = reversed ? "lamp-night" : "lamp-mobile";
    return $$"""
        {
          "homepage": {
            "id": "home",
            "title": "Home",
            "widgets": [
              {
                "widgetId": "{{firstId}}",
                "type": "Switch",
                "label": "{{first}} [OFF]",
                "item": { "name": "{{firstId}}_Item", "state": "OFF" },
                "visibility": true
              },
              {
                "widgetId": "{{secondId}}",
                "type": "Switch",
                "label": "{{second}} [OFF]",
                "item": { "name": "{{secondId}}_Item", "state": "OFF" },
                "visibility": true
              }
            ]
          }
        }
        """;
}
```

- [ ] **Step 2: Run live update tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "SearchDescriptorUpdatesFromSitemapWidgetEventState|SearchDescriptorRemovesResultWhenSitemapWidgetEventHidesIt|RefreshWhileSearchActiveRecomputesFromLatestSitemapOrder"
```

Expected: FAIL until active search recomputation is wired into SSE and refresh paths.

- [ ] **Step 3: Recompute search after widget events**

In `ApplyWidgetEvent`, after `Current = Current with { Descriptor = renderController.BuildCurrentDescriptor(currentPage), ChangedRowIndices = changedIndices };`, add:

```csharp
        if (searchQuery.Length > 0)
        {
            RebuildSearchDescriptor();
        }
```

When search is active, `ChangedRowIndices` should be `[]` because the synthetic descriptor structure may change.

- [ ] **Step 4: Recompute search after refresh and reconcile**

In successful `RefreshAsyncInternal` branches and in `ReconcileCurrentPageAsync`, ensure `currentPage` is updated before calling:

```csharp
if (searchQuery.Length > 0)
{
    RebuildSearchDescriptor();
}
```

Do not clear search on manual refresh.

- [ ] **Step 5: Run live update tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "SearchDescriptorUpdatesFromSitemapWidgetEventState|SearchDescriptorRemovesResultWhenSitemapWidgetEventHidesIt|RefreshWhileSearchActiveRecomputesFromLatestSitemapOrder"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/OpenHab.App/Runtime/SitemapRuntimeController.cs tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs
git commit -m "Keep sitemap search results live"
```

---

### Task 6: Flyout Search Chrome

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`

- [ ] **Step 1: Add search controls in XAML**

In `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`, change the header grid column definitions from three columns to four columns:

```xml
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="*" />
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="Auto" />
```

Insert this search button before the minimize button:

```xml
<Button x:Name="SearchButton"
        Grid.Column="2"
        Width="34"
        Height="34"
        Padding="0"
        ToolTipService.ToolTip="Search sitemap"
        Click="SearchButton_Click">
    <FontIcon x:Name="SearchButtonIcon"
              Glyph="&#xE721;"
              FontSize="13" />
</Button>
```

Move the minimize button to `Grid.Column="3"`.

Replace the standalone `BreadcrumbBar` with a grid in row 1:

```xml
<Grid Grid.Row="1"
      x:Name="NavigationSearchRow"
      Margin="0,2,0,0">
    <BreadcrumbBar x:Name="BreadcrumbBar"
                   ItemClicked="BreadcrumbBar_ItemClicked">
        <BreadcrumbBar.ItemTemplate>
            <DataTemplate>
                <TextBlock Text="{Binding Label}"
                           FontFamily="{Binding FontFamily}"
                           FontSize="{Binding FontSize}" />
            </DataTemplate>
        </BreadcrumbBar.ItemTemplate>
    </BreadcrumbBar>

    <AutoSuggestBox x:Name="SitemapSearchBox"
                    Visibility="Collapsed"
                    PlaceholderText="Search current section..."
                    QueryIcon="Find"
                    TextChanged="SitemapSearchBox_TextChanged"
                    QuerySubmitted="SitemapSearchBox_QuerySubmitted" />
</Grid>
```

- [ ] **Step 2: Add flyout fields**

In `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`, add:

```csharp
    private bool isUpdatingSearchBox;
```

- [ ] **Step 3: Update chrome binding for search state**

In `RefreshChromeBindings`, after setting title/status and breadcrumb items, add:

```csharp
        var searchActive = snapshot.IsSearchActive;
        BreadcrumbBar.Visibility = !searchActive && rawBreadcrumbs.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        SitemapSearchBox.Visibility = searchActive ? Visibility.Visible : Visibility.Collapsed;
        SearchButtonIcon.Foreground = searchActive
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : null;

        if (!isUpdatingSearchBox && SitemapSearchBox.Text != snapshot.SearchQuery)
        {
            isUpdatingSearchBox = true;
            SitemapSearchBox.Text = snapshot.SearchQuery;
            isUpdatingSearchBox = false;
        }
```

Remove or replace the existing direct `BreadcrumbBar.Visibility = ...` assignment so this new block is the only visibility decision.

- [ ] **Step 4: Add search event handlers**

In `FlyoutWindow.xaml.cs`, add:

```csharp
    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (runtimeController.Current.IsSearchActive)
        {
            runtimeController.ClearSearch();
            RefreshRuntimeBindings(ActiveRows);
            return;
        }

        runtimeController.ApplySearchQuery(SitemapSearchBox.Text);
        RefreshRuntimeBindings(ActiveRows);
        SitemapSearchBox.Focus(FocusState.Programmatic);
    }

    private void SitemapSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (isUpdatingSearchBox || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        runtimeController.ApplySearchQuery(sender.Text);
        RefreshRuntimeBindings(ActiveRows);
    }

    private void SitemapSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        runtimeController.ApplySearchQuery(sender.Text);
        RefreshRuntimeBindings(ActiveRows);
    }
```

- [ ] **Step 5: Add escape handling**

In `MainContent_KeyDown` or the flyout's existing key handler, add this before back navigation:

```csharp
        if (e.Key == VirtualKey.Escape && runtimeController.Current.IsSearchActive)
        {
            e.Handled = true;
            runtimeController.ClearSearch();
            RefreshRuntimeBindings(ActiveRows);
            return;
        }
```

- [ ] **Step 6: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```powershell
git add src/OpenHab.Windows.Tray/FlyoutWindow.xaml src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs
git commit -m "Add flyout sitemap search chrome"
```

---

### Task 7: Direct Test Gate And Polish

**Files:**
- Modify only files touched by earlier tasks if verification exposes issues.

- [ ] **Step 1: Run direct test gate**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all pass.

- [ ] **Step 2: Run tray build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Fix any failures with focused commits**

For each failing test or build error, make the smallest change that preserves the approved design. Commit each independent fix by staging the feature files affected by the failure:

```powershell
git add src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs src/OpenHab.App/Runtime/SitemapSearchModels.cs src/OpenHab.App/Runtime/SitemapSearchDescriptorBuilder.cs src/OpenHab.App/Runtime/SitemapRuntimeSnapshot.cs src/OpenHab.App/Runtime/SitemapRuntimeController.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs tests/OpenHab.App.Tests/Runtime/SitemapSearchDescriptorBuilderTests.cs tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs
git commit -m "Fix sitemap search verification issue"
```

- [ ] **Step 4: Optional full solution gate**

Run when DesktopBridge prerequisites are available:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: passes, or fails only for known DesktopBridge environment prerequisites documented in `docs/superpowers/verification/openhab-windows-quality-gates.md`.

---

## Self-Review Notes

Spec coverage:

- Flyout search chrome is covered by Task 6.
- Current page subtree, labels-only matching, frame child inclusion, duplicate labels, no matches, and ordering are covered by Task 2.
- Runtime search lifecycle and clearing behavior are covered by Task 3.
- Interactive virtual rows and stale source safety are covered by Task 4.
- SSE, visibility, command reconcile, refresh, and order changes are covered by Task 5.
- Direct verification is covered by Task 7.

Implementation constraints:

- Do not move WinUI concerns into `OpenHab.App`, `OpenHab.Sitemaps`, or `OpenHab.Rendering`.
- Do not add a parallel renderer for search results.
- Keep search row metadata optional so normal sitemap rows remain unchanged.
- Prefer widget ids for source identity and fail safely when fallback identity is ambiguous.
