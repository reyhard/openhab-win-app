# openHAB Windows Coverage Quality Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise SonarQube coverage with meaningful tests by extracting testable behavior from WinUI code-behind while excluding only narrow OS/UI integration glue from coverage.

**Architecture:** Keep WinUI object construction in `OpenHab.Windows.Tray`, move decision logic into `OpenHab.App` or `OpenHab.Rendering`, and keep contracts covered by xUnit tests. Sonar exclusions are limited to files whose behavior is tied to OS windows, shell APIs, or toast infrastructure that should be verified by build/manual smoke gates instead of unit tests.

**Tech Stack:** .NET 10, WinUI/Windows App SDK, xUnit, SonarCloud/SonarQube Scanner for .NET, Coverlet OpenCover reports.

---

## Current Coverage Evidence

- Sonar project: `reyhard_openhab-win-app`
- Sonar coverage at plan time: `31.3%`
- Sonar line coverage at plan time: `31.8%`
- Sonar branch coverage at plan time: `30.1%`
- Local OpenCover module shape:
  - `OpenHab.Sitemaps`: about `89.9%`
  - `OpenHab.Rendering`: about `87.8%`
  - `OpenHab.App`: about `83.3%`
  - `OpenHab.Core`: about `77.7%`
  - `OpenHab.Windows.Notifications`: about `62.9%`
  - `openHAB` tray executable assembly: about `2.0%`

The low aggregate number is caused primarily by large WinUI/Windows shell files that Sonar includes in the coverage denominator but the current unit tests do not exercise.

## File Structure

- Modify: `.github/workflows/sonarcloud.yml`
  - Add narrow `sonar.coverage.exclusions` for OS/UI glue only.
  - Keep OpenCover import and test execution inside Sonar begin/end.
- Create: `docs/superpowers/verification/2026-05-16-sonar-coverage-baseline.md`
  - Record the current Sonar and local OpenCover baseline before extraction work.
- Create: `src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs`
  - Pure shortcut action editor planning and action list mutation logic extracted from `SettingsPageControl`.
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
  - Delegate shortcut action draft/create/update/delete/reorder decisions to `ShortcutActionEditorPlanner`.
- Create: `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs`
  - Unit coverage for shortcut action editor decisions.
- Create: `src/OpenHab.Rendering/SitemapSurface/SitemapRowVisualPolicy.cs`
  - Pure sitemap row visual decisions now mixed into `SitemapControlFactory`.
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
  - Delegate identity key, webview height, slider text formatting, input display formatting, and row policy decisions to `SitemapRowVisualPolicy`.
- Create: `tests/OpenHab.Rendering.Tests/SitemapSurface/SitemapRowVisualPolicyTests.cs`
  - Unit coverage for row visual policy.
- Create: `src/OpenHab.App/Runtime/SitemapNavigationTransitionPlanner.cs`
  - Pure navigation transition decisions shared by main window and flyout.
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
  - Delegate sitemap navigation transition decisions to `SitemapNavigationTransitionPlanner`.
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
  - Delegate sitemap navigation transition decisions to `SitemapNavigationTransitionPlanner`.
- Create: `tests/OpenHab.App.Tests/Runtime/SitemapNavigationTransitionPlannerTests.cs`
  - Unit coverage for forward/back/no-op/blocked transition decisions.
- Create: `src/OpenHab.App/MainUi/MainUiPagePromotionPlanner.cs`
  - Pure promoted Main UI page filtering, mapping, route, and sorting logic extracted from `MainUiPageDiscoveryService`.
- Modify: `src/OpenHab.App/MainUi/MainUiPageDiscoveryService.cs`
  - Delegate promoted Main UI page discovery mapping to `MainUiPagePromotionPlanner`.
- Create: `tests/OpenHab.App.Tests/MainUi/MainUiPagePromotionPlannerTests.cs`
  - Unit coverage for promoted page filtering, mapping, route escaping, raw metadata preservation, and ordering.

---

### Task 1: Record Coverage Baseline

**Files:**
- Create: `docs/superpowers/verification/2026-05-16-sonar-coverage-baseline.md`

- [ ] **Step 1: Query Sonar project measures**

Run MCP query for project `reyhard_openhab-win-app` with these metric keys:

```text
coverage,line_coverage,branch_coverage,lines_to_cover,uncovered_lines,conditions_to_cover,uncovered_conditions,ncloc
```

Expected current-shape result:

```text
coverage: 31.3
line_coverage: 31.8
branch_coverage: 30.1
lines_to_cover: 13728
uncovered_lines: 9360
conditions_to_cover: 6240
uncovered_conditions: 4363
```

- [ ] **Step 2: Generate local OpenCover reports**

Run:

```powershell
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
if (Test-Path TestResults) {
  $workspace = (Resolve-Path .).Path
  $target = (Resolve-Path TestResults).Path
  if (-not $target.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove path outside workspace: $target" }
  Remove-Item -LiteralPath $target -Recurse -Force
}
$testProjects = @(
  "tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj",
  "tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj",
  "tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj",
  "tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj"
)
foreach ($testProject in $testProjects) {
  dotnet test $testProject `
    --configuration Release `
    --no-restore `
    --collect "XPlat Code Coverage" `
    --results-directory TestResults `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
}
```

Expected: all four test projects pass and each emits `coverage.opencover.xml`.

- [ ] **Step 3: Summarize local coverage by module**

Run:

```powershell
$reports = Get-ChildItem TestResults -Recurse -Filter coverage.opencover.xml
$lineStates = @{}
foreach ($report in $reports) {
  [xml]$xml = Get-Content $report.FullName
  foreach ($module in $xml.CoverageSession.Modules.Module) {
    $moduleName = [string]$module.ModuleName
    $fileMap = @{}
    foreach ($file in $module.Files.File) { $fileMap[[string]$file.uid] = [string]$file.fullPath }
    foreach ($class in $module.Classes.Class) {
      foreach ($method in $class.Methods.Method) {
        if ($method.SequencePoints -eq $null) { continue }
        foreach ($sp in $method.SequencePoints.SequencePoint) {
          $filePath = $fileMap[[string]$sp.fileid]
          if ([string]::IsNullOrWhiteSpace($filePath) -or -not $filePath.Contains("\src\")) { continue }
          $key = "$moduleName|$filePath|$($sp.sl)"
          $visited = [int]$sp.vc -gt 0
          if (-not $lineStates.ContainsKey($key)) {
            $lineStates[$key] = [pscustomobject]@{ Module = $moduleName; Covered = $visited }
          } elseif ($visited) {
            $lineStates[$key].Covered = $true
          }
        }
      }
    }
  }
}
$lineStates.Values |
  Group-Object Module |
  ForEach-Object {
    $total = $_.Group.Count
    $covered = ($_.Group | Where-Object Covered).Count
    [pscustomobject]@{
      Module = $_.Name
      CoveredLines = $covered
      LinesToCover = $total
      LineCoverage = if ($total -eq 0) { 0 } else { [math]::Round(($covered / $total) * 100, 1) }
    }
  } |
  Sort-Object LineCoverage, Module |
  Format-Table -AutoSize
```

Expected: tray executable module named `openHAB` is the dominant low-coverage module.

- [ ] **Step 4: Write the baseline document**

Create `docs/superpowers/verification/2026-05-16-sonar-coverage-baseline.md`:

```markdown
# Sonar Coverage Baseline - 2026-05-16

## Sonar Project

- Project key: `reyhard_openhab-win-app`
- Coverage: `31.3%`
- Line coverage: `31.8%`
- Branch coverage: `30.1%`
- Lines to cover: `13,728`
- Uncovered lines: `9,360`
- Conditions to cover: `6,240`
- Uncovered conditions: `4,363`

## Local OpenCover Shape

| Module | Approximate line coverage |
| --- | ---: |
| `OpenHab.Sitemaps` | `89.9%` |
| `OpenHab.Rendering` | `87.8%` |
| `OpenHab.App` | `83.3%` |
| `OpenHab.Core` | `77.7%` |
| `OpenHab.Windows.Notifications` | `62.9%` |
| `openHAB` tray executable | `2.0%` |

## Interpretation

Coverage import is working. The low aggregate coverage is caused by large WinUI and Windows shell files in the tray executable assembly that are included in the coverage denominator but are not exercised by the unit test suite.

## Policy Direction

- Extract testable decision logic from WinUI code-behind into `OpenHab.App` and `OpenHab.Rendering`.
- Keep WinUI element creation and Windows API calls in `OpenHab.Windows.Tray`.
- Exclude only narrow OS/UI glue from coverage, and keep those files covered by build and manual smoke gates.
```

- [ ] **Step 5: Clean generated local coverage**

Run:

```powershell
$workspace = (Resolve-Path .).Path
$target = (Resolve-Path TestResults).Path
if (-not $target.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove path outside workspace: $target" }
Remove-Item -LiteralPath $target -Recurse -Force
```

Expected: `TestResults` is removed.

- [ ] **Step 6: Commit**

Run:

```powershell
git add docs/superpowers/verification/2026-05-16-sonar-coverage-baseline.md
git commit -m "Document Sonar coverage baseline"
```

---

### Task 2: Add Narrow Coverage Exclusions for OS/UI Glue

**Files:**
- Modify: `.github/workflows/sonarcloud.yml`

- [ ] **Step 1: Write the failing workflow assertion**

Run:

```powershell
$workflow = Get-Content .github\workflows\sonarcloud.yml -Raw
$required = @(
  'sonar.coverage.exclusions',
  'src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs',
  'src/OpenHab.Windows.Tray/DwmWindowDecorations.cs',
  'src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs',
  'src/OpenHab.Windows.Tray/Tray/TrayIconService.cs',
  'src/OpenHab.Windows.Tray/Startup/StartupManager.cs',
  'src/OpenHab.Windows.Tray/DeviceInfo/Windows*Reader.cs',
  'src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs',
  'src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs',
  'src/OpenHab.Windows.Notifications/ToastService.cs',
  'src/OpenHab.Core/Auth/WindowsCredentialStore.cs'
)
$missing = $required | Where-Object { -not $workflow.Contains($_) }
if ($missing) {
  Write-Error ("Missing expected Sonar coverage exclusion entries: " + ($missing -join ', '))
  exit 1
}
```

Expected before implementation: FAIL with missing exclusion entries.

- [ ] **Step 2: Add the exclusion property**

In `.github/workflows/sonarcloud.yml`, extend the `dotnet-sonarscanner begin` command:

```yaml
            /d:sonar.cs.opencover.reportsPaths="TestResults/**/coverage.opencover.xml" `
            /d:sonar.coverage.exclusions="src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs,src/OpenHab.Windows.Tray/DwmWindowDecorations.cs,src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs,src/OpenHab.Windows.Tray/Tray/TrayIconService.cs,src/OpenHab.Windows.Tray/Startup/StartupManager.cs,src/OpenHab.Windows.Tray/DeviceInfo/Windows*Reader.cs,src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs,src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs,src/OpenHab.Windows.Notifications/ToastService.cs,src/OpenHab.Core/Auth/WindowsCredentialStore.cs" `
            /d:sonar.qualitygate.wait=true
```

Do not exclude `src/OpenHab.Windows.Tray/**/*.cs`. Large files such as `SettingsPageControl.xaml.cs`, `SitemapControlFactory.cs`, `MainWindow.xaml.cs`, and `FlyoutWindow.xaml.cs` contain extractable behavior and must remain visible as improvement targets.

- [ ] **Step 3: Verify the workflow assertion passes**

Run the command from Step 1 again.

Expected: PASS.

- [ ] **Step 4: Verify whitespace**

Run:

```powershell
git diff --check -- .github\workflows\sonarcloud.yml
```

Expected: no output and exit code `0`.

- [ ] **Step 5: Commit**

Run:

```powershell
git add .github\workflows\sonarcloud.yml
git commit -m "Limit Sonar coverage exclusions to OS glue"
```

---

### Task 3: Extract Shortcut Action Editor Planning

**Files:**
- Create: `src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- Create: `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs`:

```csharp
using System.Collections.Immutable;
using OpenHab.App.Shortcuts;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutActionEditorPlannerTests
{
    [Fact]
    public void CreateDraft_ForNewAction_UsesSafeDefaults()
    {
        var draft = ShortcutActionEditorPlanner.CreateDraft(null);

        Assert.Null(draft.Id);
        Assert.Equal(string.Empty, draft.Name);
        Assert.Equal("custom", draft.IconId);
        Assert.True(draft.ShowInCommandMenu);
        Assert.Null(draft.GlobalShortcut);
        Assert.Equal(string.Empty, draft.TargetItem);
        Assert.Equal(ShortcutCommandType.Toggle, draft.CommandType);
        Assert.Null(draft.CommandValue);
    }

    [Fact]
    public void CreateDraft_ForExistingAction_CopiesValues()
    {
        var action = new ShortcutAction(
            "a1",
            "Kitchen",
            "light",
            true,
            new ShortcutBinding([ShortcutModifier.Ctrl], "K"),
            "KitchenLight",
            ShortcutCommandType.SendCommand,
            "ON");

        var draft = ShortcutActionEditorPlanner.CreateDraft(action);

        Assert.Equal("a1", draft.Id);
        Assert.Equal("Kitchen", draft.Name);
        Assert.Equal("light", draft.IconId);
        Assert.True(draft.ShowInCommandMenu);
        Assert.Equal("KitchenLight", draft.TargetItem);
        Assert.Equal(ShortcutCommandType.SendCommand, draft.CommandType);
        Assert.Equal("ON", draft.CommandValue);
    }

    [Fact]
    public void BuildAction_TrimsTextAndUsesGeneratedIdForNewAction()
    {
        var draft = new ShortcutActionEditorDraft(
            Id: null,
            Name: "  Kitchen  ",
            IconId: "  light  ",
            ShowInCommandMenu: true,
            GlobalShortcut: null,
            TargetItem: "  KitchenLight  ",
            CommandType: ShortcutCommandType.SendCommand,
            CommandValue: "  ON  ");

        var action = ShortcutActionEditorPlanner.BuildAction(draft, () => "generated");

        Assert.Equal("generated", action.Id);
        Assert.Equal("Kitchen", action.Name);
        Assert.Equal("light", action.IconId);
        Assert.Equal("KitchenLight", action.TargetItem);
        Assert.Equal("ON", action.CommandValue);
    }

    [Fact]
    public void UpsertAction_ReplacesExistingActionById()
    {
        var existing = new ShortcutAction("a1", "Old", "custom", true, null, "OldItem", ShortcutCommandType.Toggle, null);
        var updated = existing with { Name = "New", TargetItem = "NewItem" };

        var actions = ShortcutActionEditorPlanner.UpsertAction([existing], updated);

        Assert.Single(actions);
        Assert.Equal("New", actions[0].Name);
        Assert.Equal("NewItem", actions[0].TargetItem);
    }

    [Fact]
    public void UpsertAction_AppendsNewAction()
    {
        var existing = new ShortcutAction("a1", "Old", "custom", true, null, "OldItem", ShortcutCommandType.Toggle, null);
        var added = new ShortcutAction("a2", "New", "custom", true, null, "NewItem", ShortcutCommandType.Toggle, null);

        var actions = ShortcutActionEditorPlanner.UpsertAction([existing], added);

        Assert.Equal(["a1", "a2"], actions.Select(static action => action.Id));
    }

    [Fact]
    public void RemoveAction_RemovesMatchingId()
    {
        var first = new ShortcutAction("a1", "First", "custom", true, null, "FirstItem", ShortcutCommandType.Toggle, null);
        var second = new ShortcutAction("a2", "Second", "custom", true, null, "SecondItem", ShortcutCommandType.Toggle, null);

        var actions = ShortcutActionEditorPlanner.RemoveAction([first, second], "a1");

        Assert.Equal(["a2"], actions.Select(static action => action.Id));
    }

    [Fact]
    public void MoveAction_ClampsDestinationInsideList()
    {
        var first = new ShortcutAction("a1", "First", "custom", true, null, "FirstItem", ShortcutCommandType.Toggle, null);
        var second = new ShortcutAction("a2", "Second", "custom", true, null, "SecondItem", ShortcutCommandType.Toggle, null);
        var third = new ShortcutAction("a3", "Third", "custom", true, null, "ThirdItem", ShortcutCommandType.Toggle, null);

        var actions = ShortcutActionEditorPlanner.MoveAction([first, second, third], "a3", -10);

        Assert.Equal(["a3", "a1", "a2"], actions.Select(static action => action.Id));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter ShortcutActionEditorPlannerTests
```

Expected: FAIL because `ShortcutActionEditorPlanner` and `ShortcutActionEditorDraft` do not exist.

- [ ] **Step 3: Add the planner**

Create `src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs`:

```csharp
using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutActionEditorDraft(
    string? Id,
    string Name,
    string IconId,
    bool ShowInCommandMenu,
    ShortcutBinding? GlobalShortcut,
    string TargetItem,
    ShortcutCommandType CommandType,
    string? CommandValue);

public static class ShortcutActionEditorPlanner
{
    public static ShortcutActionEditorDraft CreateDraft(ShortcutAction? action) =>
        action is null
            ? new ShortcutActionEditorDraft(null, string.Empty, "custom", true, null, string.Empty, ShortcutCommandType.Toggle, null)
            : new ShortcutActionEditorDraft(
                action.Id,
                action.Name,
                action.IconId,
                action.ShowInCommandMenu,
                action.GlobalShortcut,
                action.TargetItem,
                action.CommandType,
                action.CommandValue);

    public static ShortcutAction BuildAction(ShortcutActionEditorDraft draft, Func<string> createId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(createId);

        var id = string.IsNullOrWhiteSpace(draft.Id) ? createId() : draft.Id.Trim();

        return new ShortcutAction(
            id,
            draft.Name.Trim(),
            string.IsNullOrWhiteSpace(draft.IconId) ? "custom" : draft.IconId.Trim(),
            draft.ShowInCommandMenu,
            draft.GlobalShortcut,
            draft.TargetItem.Trim(),
            Enum.IsDefined(draft.CommandType) ? draft.CommandType : ShortcutCommandType.SendCommand,
            string.IsNullOrWhiteSpace(draft.CommandValue) ? null : draft.CommandValue.Trim());
    }

    public static ImmutableArray<ShortcutAction> UpsertAction(IEnumerable<ShortcutAction> actions, ShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(action);

        var builder = actions.ToImmutableArray().ToBuilder();
        var index = builder.FindIndex(existing => string.Equals(existing.Id, action.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            builder[index] = action;
        }
        else
        {
            builder.Add(action);
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<ShortcutAction> RemoveAction(IEnumerable<ShortcutAction> actions, string actionId)
    {
        ArgumentNullException.ThrowIfNull(actions);

        return actions
            .Where(action => !string.Equals(action.Id, actionId, StringComparison.Ordinal))
            .ToImmutableArray();
    }

    public static ImmutableArray<ShortcutAction> MoveAction(IEnumerable<ShortcutAction> actions, string actionId, int destinationIndex)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var builder = actions.ToImmutableArray().ToBuilder();
        var sourceIndex = builder.FindIndex(action => string.Equals(action.Id, actionId, StringComparison.Ordinal));
        if (sourceIndex < 0)
        {
            return builder.ToImmutable();
        }

        var action = builder[sourceIndex];
        builder.RemoveAt(sourceIndex);
        var clampedDestination = Math.Clamp(destinationIndex, 0, builder.Count);
        builder.Insert(clampedDestination, action);
        return builder.ToImmutable();
    }
}
```

- [ ] **Step 4: Use the planner from settings code-behind**

In `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`, replace local shortcut draft/list mutation code with calls to:

```csharp
var draft = ShortcutActionEditorPlanner.CreateDraft(action);
var action = ShortcutActionEditorPlanner.BuildAction(draft, static () => Guid.NewGuid().ToString("N"));
var actions = ShortcutActionEditorPlanner.UpsertAction(settings.Actions, action);
var actions = ShortcutActionEditorPlanner.RemoveAction(settings.Actions, actionId);
var actions = ShortcutActionEditorPlanner.MoveAction(settings.Actions, actionId, destinationIndex);
```

Keep all WinUI control reading and writing inside `SettingsPageControl.xaml.cs`. Only draft/list decision behavior moves.

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter ShortcutActionEditorPlannerTests
```

Expected: PASS.

- [ ] **Step 6: Run App tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs
git commit -m "Extract shortcut action editor planning"
```

---

### Task 4: Extract Sitemap Row Visual Policy

**Files:**
- Create: `src/OpenHab.Rendering/SitemapSurface/SitemapRowVisualPolicy.cs`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Create: `tests/OpenHab.Rendering.Tests/SitemapSurface/SitemapRowVisualPolicyTests.cs`
- Optionally modify: `tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/OpenHab.Rendering.Tests/SitemapSurface/SitemapRowVisualPolicyTests.cs`:

```csharp
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.SitemapSurface;

namespace OpenHab.Rendering.Tests.SitemapSurface;

public sealed class SitemapRowVisualPolicyTests
{
    [Fact]
    public void BuildRowIdentityKey_UsesWidgetIdWhenPresent()
    {
        var row = new SitemapRowDescriptor("Lamp", "ON", RenderControlKind.Toggle, RenderActionKind.SendCommand, RenderDensity.Compact, [], WidgetId: "widget-1");

        var key = SitemapRowVisualPolicy.BuildRowIdentityKey(row);

        Assert.Equal("widget:widget-1", key);
    }

    [Fact]
    public void BuildRowIdentityKey_UsesSearchDescriptorWhenPresent()
    {
        var row = new SitemapRowDescriptor(
            "Lamp",
            "ON",
            RenderControlKind.Text,
            RenderActionKind.Navigate,
            RenderDensity.Compact,
            [],
            SearchPath: "home/lights/lamp");

        var key = SitemapRowVisualPolicy.BuildRowIdentityKey(row);

        Assert.Equal("search:home/lights/lamp", key);
    }

    [Fact]
    public void BuildRowVisualStateKey_ChangesWhenStateChanges()
    {
        var offRow = new SitemapRowDescriptor("Lamp", "OFF", RenderControlKind.Toggle, RenderActionKind.SendCommand, RenderDensity.Compact, []);
        var onRow = offRow with { State = "ON" };

        Assert.NotEqual(
            SitemapRowVisualPolicy.BuildRowVisualStateKey(offRow, 2),
            SitemapRowVisualPolicy.BuildRowVisualStateKey(onRow, 2));
    }

    [Theory]
    [InlineData("Brightness: 0 %", 42, "Brightness: 42 %")]
    [InlineData(null, 42, "42")]
    [InlineData("", 42, "42")]
    public void FormatSliderStateText_ReplacesFirstNumericToken(string? template, double value, string expected)
    {
        Assert.Equal(expected, SitemapRowVisualPolicy.FormatSliderStateText(template, value));
    }

    [Fact]
    public void ResolveWebviewHeight_UsesPositiveNumericHeight()
    {
        var row = new SitemapRowDescriptor(
            "Camera",
            null,
            RenderControlKind.Webview,
            RenderActionKind.None,
            RenderDensity.Comfortable,
            [],
            WebviewHeight: 360);

        Assert.Equal(360, SitemapRowVisualPolicy.ResolveWebviewHeight(row));
    }

    [Fact]
    public void ResolveWebviewHeight_FallsBackWhenHeightMissing()
    {
        var row = new SitemapRowDescriptor("Camera", null, RenderControlKind.Webview, RenderActionKind.None, RenderDensity.Comfortable, []);

        Assert.Equal(320, SitemapRowVisualPolicy.ResolveWebviewHeight(row));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter SitemapRowVisualPolicyTests
```

Expected: FAIL because `SitemapRowVisualPolicy` does not exist.

- [ ] **Step 3: Add the visual policy**

Create `src/OpenHab.Rendering/SitemapSurface/SitemapRowVisualPolicy.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Rendering.SitemapSurface;

public static partial class SitemapRowVisualPolicy
{
    private const double DefaultWebviewHeight = 320d;

    public static double ResolveWebviewHeight(SitemapRowDescriptor row) =>
        row.WebviewHeight is > 0 ? row.WebviewHeight.Value : DefaultWebviewHeight;

    public static string BuildRowIdentityKey(SitemapRowDescriptor row)
    {
        if (!string.IsNullOrWhiteSpace(row.WidgetId))
        {
            return $"widget:{row.WidgetId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(row.SearchPath))
        {
            return $"search:{row.SearchPath.Trim()}";
        }

        return $"row:{row.Control}:{row.Action}:{row.ItemName}:{row.Label}";
    }

    public static string BuildRowVisualStateKey(SitemapRowDescriptor row, int rowIndex) =>
        string.Join(
            '|',
            rowIndex.ToString(CultureInfo.InvariantCulture),
            BuildRowIdentityKey(row),
            row.Control,
            row.Action,
            row.Label,
            row.State,
            row.RawState,
            row.IsVisible,
            row.IconName,
            row.LabelColor,
            row.ValueColor,
            row.IconColor);

    public static string FormatSliderStateText(string? template, double value)
    {
        var formatted = Math.Round(value).ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(template))
        {
            return formatted;
        }

        return NumberTokenRegex().Replace(template, formatted, 1);
    }

    [GeneratedRegex("-?\\d+(?:[\\.,]\\d+)?")]
    private static partial Regex NumberTokenRegex();
}
```

- [ ] **Step 4: Delegate from `SitemapControlFactory`**

In `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`, change the existing pure methods:

```csharp
internal static double ResolveWebviewHeight(SitemapRowDescriptor row) =>
    SitemapRowVisualPolicy.ResolveWebviewHeight(row);

internal static string BuildRowIdentityKey(SitemapRowDescriptor row) =>
    SitemapRowVisualPolicy.BuildRowIdentityKey(row);

internal static string BuildRowVisualStateKey(SitemapRowDescriptor row, int rowIndex) =>
    SitemapRowVisualPolicy.BuildRowVisualStateKey(row, rowIndex);

private static string FormatSliderStateText(string? template, double value) =>
    SitemapRowVisualPolicy.FormatSliderStateText(template, value);
```

- [ ] **Step 5: Run rendering and app tests**

Run:

```powershell
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/OpenHab.Rendering/SitemapSurface/SitemapRowVisualPolicy.cs src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs tests/OpenHab.Rendering.Tests/SitemapSurface/SitemapRowVisualPolicyTests.cs tests/OpenHab.App.Tests/SitemapControlFactoryTests.cs
git commit -m "Extract sitemap row visual policy"
```

---

### Task 5: Extract Sitemap Navigation Transition Decisions

**Files:**
- Create: `src/OpenHab.App/Runtime/SitemapNavigationTransitionPlanner.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Create: `tests/OpenHab.App.Tests/Runtime/SitemapNavigationTransitionPlannerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/OpenHab.App.Tests/Runtime/SitemapNavigationTransitionPlannerTests.cs`:

```csharp
using OpenHab.App.Runtime;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapNavigationTransitionPlannerTests
{
    [Fact]
    public void PlanNavigate_BlocksWhenTransitionAlreadyRunning()
    {
        var plan = SitemapNavigationTransitionPlanner.PlanNavigate(isTransitionRunning: true, canNavigate: true, direction: SitemapNavigationDirection.Forward);

        Assert.False(plan.ShouldNavigate);
        Assert.False(plan.ShouldAnimate);
        Assert.Equal(SitemapNavigationDecisionReason.TransitionRunning, plan.Reason);
    }

    [Fact]
    public void PlanNavigate_BlocksWhenNavigationUnavailable()
    {
        var plan = SitemapNavigationTransitionPlanner.PlanNavigate(isTransitionRunning: false, canNavigate: false, direction: SitemapNavigationDirection.Forward);

        Assert.False(plan.ShouldNavigate);
        Assert.False(plan.ShouldAnimate);
        Assert.Equal(SitemapNavigationDecisionReason.NavigationUnavailable, plan.Reason);
    }

    [Theory]
    [InlineData(SitemapNavigationDirection.Forward)]
    [InlineData(SitemapNavigationDirection.Back)]
    public void PlanNavigate_AllowsAnimationWhenNavigationAvailable(SitemapNavigationDirection direction)
    {
        var plan = SitemapNavigationTransitionPlanner.PlanNavigate(isTransitionRunning: false, canNavigate: true, direction);

        Assert.True(plan.ShouldNavigate);
        Assert.True(plan.ShouldAnimate);
        Assert.Equal(direction, plan.Direction);
        Assert.Equal(SitemapNavigationDecisionReason.Ready, plan.Reason);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter SitemapNavigationTransitionPlannerTests
```

Expected: FAIL because the planner types do not exist.

- [ ] **Step 3: Add the planner**

Create `src/OpenHab.App/Runtime/SitemapNavigationTransitionPlanner.cs`:

```csharp
namespace OpenHab.App.Runtime;

public enum SitemapNavigationDirection
{
    Forward,
    Back
}

public enum SitemapNavigationDecisionReason
{
    Ready,
    TransitionRunning,
    NavigationUnavailable
}

public readonly record struct SitemapNavigationTransitionPlan(
    bool ShouldNavigate,
    bool ShouldAnimate,
    SitemapNavigationDirection Direction,
    SitemapNavigationDecisionReason Reason);

public static class SitemapNavigationTransitionPlanner
{
    public static SitemapNavigationTransitionPlan PlanNavigate(
        bool isTransitionRunning,
        bool canNavigate,
        SitemapNavigationDirection direction)
    {
        if (isTransitionRunning)
        {
            return new SitemapNavigationTransitionPlan(false, false, direction, SitemapNavigationDecisionReason.TransitionRunning);
        }

        if (!canNavigate)
        {
            return new SitemapNavigationTransitionPlan(false, false, direction, SitemapNavigationDecisionReason.NavigationUnavailable);
        }

        return new SitemapNavigationTransitionPlan(true, true, direction, SitemapNavigationDecisionReason.Ready);
    }
}
```

- [ ] **Step 4: Use the planner in main and flyout windows**

In both `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` and `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`, replace duplicated transition guard checks with:

```csharp
var plan = SitemapNavigationTransitionPlanner.PlanNavigate(
    _isPageTransitionRunning,
    canNavigate,
    direction == NavigationDirection.Back
        ? SitemapNavigationDirection.Back
        : SitemapNavigationDirection.Forward);

if (!plan.ShouldNavigate)
{
    return;
}
```

Keep WinUI animation calls and `NavigationDirection` conversion in the window classes.

- [ ] **Step 5: Run focused and App tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter SitemapNavigationTransitionPlannerTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 6: Run tray build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/OpenHab.App/Runtime/SitemapNavigationTransitionPlanner.cs src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs tests/OpenHab.App.Tests/Runtime/SitemapNavigationTransitionPlannerTests.cs
git commit -m "Extract sitemap navigation transition planning"
```

---

### Task 6: Extract Main UI Page Promotion Planning

**Files:**
- Create: `src/OpenHab.App/MainUi/MainUiPagePromotionPlanner.cs`
- Modify: `src/OpenHab.App/MainUi/MainUiPageDiscoveryService.cs`
- Create: `tests/OpenHab.App.Tests/MainUi/MainUiPagePromotionPlannerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/OpenHab.App.Tests/MainUi/MainUiPagePromotionPlannerTests.cs`:

```csharp
using System.Text.Json;
using OpenHab.App.MainUi;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.MainUi;

public sealed class MainUiPagePromotionPlannerTests
{
    [Fact]
    public void PlanPromotedLinks_FiltersNonSidebarAndBlankUidPages()
    {
        var pages = new[]
        {
            Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt"),
            Page("hidden", "Hidden", sidebar: false, order: "20", icon: "f7:eye-slash"),
            Page("   ", "Blank", sidebar: true, order: "30", icon: "f7:question")
        };

        var links = MainUiPagePromotionPlanner.PlanPromotedLinks(pages);

        var link = Assert.Single(links);
        Assert.Equal("energy", link.Uid);
    }

    [Fact]
    public void PlanPromotedLinks_UsesTrimmedUidWhenLabelMissing()
    {
        var pages = new[] { Page("  energy  ", null, sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPagePromotionPlanner.PlanPromotedLinks(pages));

        Assert.Equal("energy", link.Uid);
        Assert.Equal("energy", link.Label);
        Assert.Equal("/page/energy", link.Route);
    }

    [Fact]
    public void PlanPromotedLinks_EscapesUidInRoute()
    {
        var pages = new[] { Page("Floor Plan", "Floor Plan", sidebar: true, order: null, icon: null) };

        var link = Assert.Single(MainUiPagePromotionPlanner.PlanPromotedLinks(pages));

        Assert.Equal("/page/Floor%20Plan", link.Route);
    }

    [Fact]
    public void PlanPromotedLinks_PreservesRawIconTypeAndOrder()
    {
        var pages = new[] { Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt") };

        var link = Assert.Single(MainUiPagePromotionPlanner.PlanPromotedLinks(pages));

        Assert.Equal("f7:bolt", link.Icon);
        Assert.Equal("oh-layout-page", link.Type);
        Assert.Equal(10, link.Order);
    }

    [Fact]
    public void PlanPromotedLinks_SortsByOrderThenLabelThenUid()
    {
        var pages = new[]
        {
            Page("zeta", "Zeta", sidebar: true, order: "20", icon: null),
            Page("beta", "Beta", sidebar: true, order: "10", icon: null),
            Page("alpha", "alpha", sidebar: true, order: "10", icon: null),
            Page("omega", "Omega", sidebar: true, order: null, icon: null)
        };

        var links = MainUiPagePromotionPlanner.PlanPromotedLinks(pages);

        Assert.Collection(
            links,
            first => Assert.Equal("alpha", first.Uid),
            second => Assert.Equal("beta", second.Uid),
            third => Assert.Equal("zeta", third.Uid),
            fourth => Assert.Equal("omega", fourth.Uid));
    }

    [Fact]
    public void BuildPromotedLinks_DelegatesToPromotionPlanner()
    {
        var pages = new[]
        {
            Page("energy", "Energy", sidebar: true, order: "10", icon: "f7:bolt"),
            Page("hidden", "Hidden", sidebar: false, order: "20", icon: null)
        };

        var fromService = MainUiPageDiscoveryService.BuildPromotedLinks(pages);
        var fromPlanner = MainUiPagePromotionPlanner.PlanPromotedLinks(pages);

        Assert.Equal(fromPlanner, fromService);
    }

    private static MainUiPageComponent Page(string uid, string? label, bool sidebar, string? order, string? icon)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (label is not null)
        {
            values["label"] = JsonDocument.Parse(JsonSerializer.Serialize(label)).RootElement.Clone();
        }

        values["sidebar"] = JsonDocument.Parse(sidebar ? "true" : "false").RootElement.Clone();
        if (order is not null)
        {
            values["order"] = JsonDocument.Parse(JsonSerializer.Serialize(order)).RootElement.Clone();
        }

        if (icon is not null)
        {
            values["icon"] = JsonDocument.Parse(JsonSerializer.Serialize(icon)).RootElement.Clone();
        }

        return new MainUiPageComponent(uid, "oh-layout-page", values);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter MainUiPagePromotionPlannerTests
```

Expected: FAIL because `MainUiPagePromotionPlanner` does not exist.

- [ ] **Step 3: Add the planner**

Create `src/OpenHab.App/MainUi/MainUiPagePromotionPlanner.cs`:

```csharp
using OpenHab.Core.Ui;

namespace OpenHab.App.MainUi;

public static class MainUiPagePromotionPlanner
{
    public static IReadOnlyList<MainUiPageLink> PlanPromotedLinks(IEnumerable<MainUiPageComponent> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        return pages
            .Where(page => page.GetConfigBoolean("sidebar"))
            .Select(static page => page with { Uid = page.Uid.Trim() })
            .Where(static page => !string.IsNullOrWhiteSpace(page.Uid))
            .Select(ToLink)
            .OrderBy(static link => link.Order ?? int.MaxValue)
            .ThenBy(static link => link.Label, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static link => link.Uid, StringComparer.Ordinal)
            .ToArray();
    }

    private static MainUiPageLink ToLink(MainUiPageComponent page)
    {
        var label = page.GetConfigString("label");
        if (string.IsNullOrWhiteSpace(label))
        {
            label = page.Uid;
        }

        return new MainUiPageLink(
            page.Uid,
            label.Trim(),
            "/page/" + Uri.EscapeDataString(page.Uid),
            page.GetConfigString("icon"),
            page.Component,
            page.GetConfigInt32("order"));
    }
}
```

- [ ] **Step 4: Use the planner from `MainUiPageDiscoveryService`**

In `src/OpenHab.App/MainUi/MainUiPageDiscoveryService.cs`, replace inline promoted page filtering, mapping, and sorting with:

```csharp
return MainUiPagePromotionPlanner.PlanPromotedLinks(pages);
```

Keep HTTP discovery orchestration in `MainUiPageDiscoveryService`.

- [ ] **Step 5: Run focused and App tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter MainUiPagePromotionPlannerTests
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 6: Run tray build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore
```

Expected: PASS, unless the environment blocks NuGet access to `https://api.nuget.org/v3/index.json`.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/OpenHab.App/MainUi/MainUiPagePromotionPlanner.cs src/OpenHab.App/MainUi/MainUiPageDiscoveryService.cs tests/OpenHab.App.Tests/MainUi/MainUiPagePromotionPlannerTests.cs
git commit -m "Extract Main UI page promotion planning"
```

---

### Task 7: Verify Coverage Import and Improvement

**Files:**
- Modify: `docs/superpowers/verification/2026-05-16-sonar-coverage-baseline.md`

- [ ] **Step 1: Run direct tests**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore
```

Expected: PASS for all four projects.

- [ ] **Step 2: Run tray build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore
```

Expected: PASS.

- [ ] **Step 3: Run local OpenCover command**

Run the OpenCover command from Task 1 Step 2.

Expected: all four tests pass and four `coverage.opencover.xml` files exist.

- [ ] **Step 4: Verify coverage reports exist**

Run:

```powershell
$reports = Get-ChildItem TestResults -Recurse -Filter coverage.opencover.xml
if ($reports.Count -ne 4) {
  Write-Error "Expected 4 OpenCover reports but found $($reports.Count)."
  exit 1
}
$reports | Select-Object -ExpandProperty FullName
```

Expected: exactly four report paths.

- [ ] **Step 5: Clean local coverage output**

Run:

```powershell
$workspace = (Resolve-Path .).Path
$target = (Resolve-Path TestResults).Path
if (-not $target.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove path outside workspace: $target" }
Remove-Item -LiteralPath $target -Recurse -Force
```

Expected: `TestResults` is removed.

- [ ] **Step 6: Run SonarCloud workflow**

Push the branch and run the `SonarCloud` workflow from GitHub Actions.

Expected:

```text
Sonar coverage remains imported.
Project coverage is above the baseline value of 31.3%.
No new large non-glue files are hidden by coverage exclusions.
```

- [ ] **Step 7: Update the verification document**

Append to `docs/superpowers/verification/2026-05-16-sonar-coverage-baseline.md`:

```markdown
## Post-Extraction Verification

- Direct tests: passed.
- Tray Release build: passed.
- Local OpenCover report count: `4`.
- Sonar workflow: passed.
- New Sonar coverage: `[record value from MCP or Sonar UI]`.
- Coverage interpretation: coverage improved through extracted tested logic and narrow OS/UI glue exclusions, not by excluding broad `OpenHab.Windows.Tray` source scope.
```

- [ ] **Step 8: Commit verification update**

Run:

```powershell
git add docs/superpowers/verification/2026-05-16-sonar-coverage-baseline.md
git commit -m "Record coverage improvement verification"
```

---

## Rollout Notes

- Do not pursue all tasks as one large change. Each extraction should be a separate commit with focused tests.
- Do not exclude broad globs such as `src/OpenHab.Windows.Tray/**/*.cs`.
- If a planned extraction requires a large WinUI rewrite, stop and split that extraction into a smaller policy class first.
- Keep manual smoke coverage for excluded OS/UI glue in the release checklist.

## Self-Review

- Spec coverage: The plan covers baseline evidence, narrow coverage exclusions, extraction of settings shortcut logic, sitemap visual policy, shared navigation transition logic, Main UI page promotion logic, and verification.
- Placeholder scan: The plan does not use placeholder implementation steps. All new classes and tests include concrete code.
- Type consistency: Planned type names are consistent across tasks: `ShortcutActionEditorPlanner`, `SitemapRowVisualPolicy`, `SitemapNavigationTransitionPlanner`, and `MainUiPagePromotionPlanner`.

---

### Task 8: Cover Shortcut Windows Mapper Pure Branches

**Files:**
- Modify: `tests/OpenHab.App.Tests/Shortcuts/ShortcutWindowsMapperTests.cs`

**Scope and evidence:**

- SonarQube project `reyhard_openhab-win-app`, `main`, analyzed 2026-10-30, reports `77.5%` overall coverage, `79.3%` line coverage, and `73.7%` branch coverage.
- `src/OpenHab.Windows.Tray/Shortcuts/ShortcutWindowsMapper.cs` is a focused low-risk candidate: `68/103` lines covered (`66.0%` line coverage) and `65/148` conditions covered (`43.9%` branch coverage), leaving 35 lines and 83 conditions uncovered.
- Source inspection confirms the mapper is already a static, deterministic key/modifier/formatting translation helper. It has no Windows API calls beyond `VirtualKey` enum values, and `tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj` already references the tray project with bootstrap initialization disabled. Extend its existing test class only; do not extract, exclude, or modify production code unless a failing test demonstrates a real mapper correctness defect.

- [ ] **Step 1: Add focused failing mapper coverage tests**

  In `tests/OpenHab.App.Tests/Shortcuts/ShortcutWindowsMapperTests.cs`, add test-first cases that exercise currently untested behavior without asserting implementation details:

  - `TryMap` maps `Alt` alone and the complete `Win`/`Ctrl`/`Alt`/`Shift` combination to their native flag mask, including duplicate/out-of-order modifiers after `ShortcutBindingFormatter` normalization.
  - `TryMap` rejects a null binding, blank/whitespace key, a valid modifier with an unsupported key, and a normalized binding with no modifiers; assert that both `uint` out values remain zero on each rejection path.
  - `TryMapVirtualKey(ShortcutBinding?, ...)` rejects null, blank, `None`, unsupported text, and invalid function-key numbers (`F0`, `F25`); assert `VirtualKey.None` is returned on failure.
  - `TryMapVirtualKey` accepts trimmed/case-insensitive named keys and enum aliases (`" enter "`, `"pageup"`, `"left"`), parses one-character letters/digits, and covers `F1`/`F24` boundary values.
  - `FormatKey` covers representative supported navigation/editing keys not already covered (`Delete`, `Insert`, arrows, `Home`, `End`, `Enter`, `Tab`) and an unsupported non-modifier key returning an empty display string.
  - `IsModifierKey` covers the remaining left/right Windows, Control, Menu, and Shift variants plus a non-modifier key.

  Keep the existing key-format and modifier theories where they overlap; consolidate them only when doing so improves clarity. Do not add tests for private members through reflection.

- [ ] **Step 2: Run the focused tests to demonstrate the red state**

  Run before making any production change:

  ```powershell
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~ShortcutWindowsMapperTests --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  Expected: the new tests pass if the existing mapper behavior is correct. If one fails, retain the failure evidence and stop to determine whether it identifies a user-observable mapper contract defect; only then propose a narrowly scoped follow-up source task rather than changing this test-only task's scope.

- [ ] **Step 3: Verify the full App test project**

  Run:

  ```powershell
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  Expected: PASS. This is the direct-logic verification gate for the test-only change; no tray build or package build is required unless production code changes.

- [ ] **Step 4: Collect and inspect scoped coverage**

  Run:

  ```powershell
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~ShortcutWindowsMapperTests --collect "XPlat Code Coverage" --results-directory TestResults\ShortcutWindowsMapper -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
  $report = Get-ChildItem TestResults\ShortcutWindowsMapper -Recurse -Filter coverage.opencover.xml | Select-Object -First 1
  if ($null -eq $report) { throw "Shortcut Windows Mapper coverage report was not created." }
  [xml]$coverage = Get-Content $report.FullName
  $file = $coverage.CoverageSession.Modules.Module.Files.File | Where-Object { $_.fullPath -like '*\src\OpenHab.Windows.Tray\Shortcuts\ShortcutWindowsMapper.cs' }
  if ($null -eq $file) { throw "ShortcutWindowsMapper.cs was not present in the coverage report." }
  $file.fullPath
  ```

  Expected: the report contains `ShortcutWindowsMapper.cs`; record the resulting line/branch improvement from the subsequent Sonar analysis in the implementation evidence, not in this historical plan or the current-state status page.

- [ ] **Step 5: Clean generated coverage output**

  Run:

  ```powershell
  if (Test-Path TestResults\ShortcutWindowsMapper) {
    Remove-Item -LiteralPath TestResults\ShortcutWindowsMapper -Recurse -Force
  }
  ```

  Expected: only the task's generated coverage directory is removed; do not remove unrelated `TestResults` content.

- [ ] **Step 6: Verify formatting and whitespace**

  Run:

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.App.Tests\Shortcuts\ShortcutWindowsMapperTests.cs
  git diff --check -- tests\OpenHab.App.Tests\Shortcuts\ShortcutWindowsMapperTests.cs
  ```

  Expected: both commands pass with no formatting or whitespace errors.

- [ ] **Step 7: Commit the test-only improvement**

  Run:

  ```powershell
  git add tests\OpenHab.App.Tests\Shortcuts\ShortcutWindowsMapperTests.cs
  git commit -m "Cover shortcut Windows mapper branches"
  ```

  Expected: the commit contains test changes only. Do not add coverage exclusions, production-source changes, generated coverage reports, or current-status updates.

---

### Task 9: Cover Shortcut Editor Unsaved-Change Decisions

**Files:**
- Modify: `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs`
- Inspect only: `src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs` and `src/OpenHab.App/Shortcuts/ShortcutBindingFormatter.cs`

**Scope and decision:** The 2026-10-30 post-`d1e4698` Sonar analysis reports overall coverage `78.2%` and new-code coverage `73.3%` against an `80%` gate; the shortcut editor planner has `85.5%` line and `34.6%` branch coverage. Its existing tests cover creation, upsert, removal and movement, but none directly cover `DraftEqualsAction`, which determines whether edited shortcut settings differ from the saved action. This bounded, deterministic user-facing decision is preferable to mechanically exercising mapper enum fallbacks, testing static sample data, or expanding the already-large runtime suite. Coverage improvement and gate passage are not guaranteed.

- [ ] **Step 1: Add a focused comparison cohort**

  Extend the existing test class using a saved action with nonempty name, icon, target and command value, a supported command type, a visible-menu flag, and a non-null binding. Construct drafts with `CreateDraft` and assert:

  - An unchanged draft equals its saved action; leading/trailing whitespace in the draft name, icon, target and command value is ignored. A null draft command value equals a saved null command value, and an empty draft value equals saved null.
  - Equivalent shortcut bindings with reordered/duplicate modifiers compare equal when their formatted binding is equal; a genuinely different binding, and null versus a non-null binding, compare unequal. Check the formatter's actual normalization contract when selecting the inputs.
  - Change exactly one field at a time: name, icon, menu visibility, binding, target item, command type, or command value. Each independently returns false while all earlier fields remain equal, so short-circuiting cannot mask a comparison. Also assert text comparison remains ordinal/case-sensitive with a case-only name change.
  - Changing only the draft ID does not change equality, since this method compares editable values, not identity.

  Assert public results, not private implementation details. Do not add coverage exclusions, reflection, or production changes. If an expected contract fails, preserve the failure and stop for a separate correctness decision rather than altering the behavior to raise coverage.

- [ ] **Step 2: Run focused and direct logic gates**

  Run sequentially:

  ```powershell
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~ShortcutActionEditorPlannerTests --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  Expected: all pass against existing behavior; this test-only cohort is not expected to start red. If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` and report the known DesktopBridge import limitation separately if encountered. No tray or package build is required for test-only changes.

- [ ] **Step 3: Inspect coverage and formatting**

  Use a fresh, task-specific results directory without deleting pre-existing results:

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-ShortcutEditorCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~ShortcutActionEditorPlannerTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.App.Tests\Shortcuts\ShortcutActionEditorPlannerTests.cs
  git diff --check -- tests\OpenHab.App.Tests\Shortcuts\ShortcutActionEditorPlannerTests.cs
  ```

  Expected: tests and checks pass; inspect the fresh OpenCover report's `ShortcutActionEditorPlanner.cs` sequence and branch points, particularly `DraftEqualsAction`, against a comparable focused pre-change report. Local focused reports are not project-wide Sonar metrics. If no relevant branches improve, inspect the report and test inputs before accepting the cohort; retain tests only when they assert meaningful distinct behavior. Do not claim the Sonar gate is fixed without a subsequent server analysis of the pushed revision. Leave generated reports untracked and out of the commit.

---

### Task 10: Cover Sitemap Color Input and Conversion Decisions

**Files:**
- Modify: `tests/OpenHab.Rendering.Tests/SitemapUiLogicTests.cs`
- Inspect only: `src/OpenHab.Rendering/SitemapUiLogic.cs`

**Scope and decision:** The pushed `main` commit `179a708`, analyzed 2026-10-30 at 14:44:52 UTC, reports project coverage `78.5%` (previously `78.2%`), line `79.7%` (+0.1pp), branch `75.9%` (+0.7pp), 1,287 uncovered lines (-7), and 711 uncovered conditions (-19). The quality gate remains ERROR: `new_coverage` is `74.0%` versus `80%` (previously `73.3%`). Task 9's planner improved from `85.5%` line / `34.6%` branch (11 lines / 34 conditions uncovered) to `94.7%` / `71.2%` (4 / 15); the mapper after Task 8 was `83.5%` / `74.3%`. The original `179a708` run failed before scanner end on transient CS2012; its retry submitted analysis and failed only the still-red coverage gate.

`SitemapUiLogic.cs` has `86.2%` line / `55.3%` branch coverage, with 39 lines and 72 conditions uncovered. Its public color-input and conversion helpers are pure and directly affect color widget commands and previews; existing tests cover one HSB parse and one red hex command, but not the hue sectors, malformed input, or hexadecimal forms. This focused cohort has meaningful branch potential without expanding the very large runtime controller (`74.6%` / `61.4%`, 195 lines / 179 conditions uncovered), introducing HTTP/cache setup for the notification resolver, or pursuing unreachable formatter internals. Coverage impact and quality-gate passage are not guaranteed.

- [ ] **Step 1: Capture a comparable focused baseline**

  Before editing tests, collect a focused OpenCover report into a new task-specific directory (replace `$results` with a newly generated path for each run):

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-SitemapColorCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapUiLogicTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Retain the report path for comparison; do not delete existing results. Confirm the report contains `SitemapUiLogic.cs`.

- [ ] **Step 2: Add a focused public-behavior cohort**

  Extend the existing test class with theories/facts asserting:

  - `TryResolveOpenHabColor` converts HSB `0,100,100`, `60,100,100`, `120,100,100`, `180,100,100`, `240,100,100`, `300,100,100`, and `360,100,100` to opaque red, yellow, green, cyan, blue, magenta, and red respectively; assert all ARGB channels. Include `120,0,50` as gray `(255,128,128,128)`.
  - `BuildOpenHabColorCommand` converts opaque red, yellow, green, cyan, blue, magenta, black and white to `0,100,100`, `60,100,100`, `120,100,100`, `180,100,100`, `240,100,100`, `300,100,100`, `0,0,0` and `0,0,100` respectively. This covers hue selection, negative-hue wrap and achromatic handling using observable commands.
  - `TryParseOpenHabColorState` clamps `-10,150,-5` to `(0,100,0)` and `400,20,110` to `(360,20,100)`; rejects null/blank, fewer than three components, and invalid first, second, or third components. Assert failure rather than assuming all out parameters are reset after partial parsing.
  - `TryResolveOpenHabColor` accepts trimmed `#abc`, `#123456`, `#80123456`, and case-insensitive `red`, asserting exact ARGB values; rejects `#12`, `#gggggg`, and an unknown name. `NormalizeInputByHint` with `Color` turns `#00FF00` into `120,100,100` and preserves trimmed invalid input `not-a-color`.

  Use public APIs only. No exclusions, reflection, or production changes are in scope. If an asserted contract reveals an actual correctness problem, preserve the failing evidence and stop for separate correctness planning instead of changing production behavior to raise coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially:

  ```powershell
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapUiLogicTests --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  Expected: all pass against existing behavior; this test-only task is not expected to start red. If practical also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` and report the known DesktopBridge import limitation separately. No tray or package build is required for test-only changes.

- [ ] **Step 4: Compare coverage and check formatting**

  Repeat Step 1 with a fresh results path, compare the focused baseline and new report's sequence and branch points in the color parsing/conversion methods, and confirm relevant points improve. Focused OpenCover results are not project-wide Sonar metrics. Retain only meaningful distinct tests; do not promise gate passage without a subsequent Sonar analysis of the pushed revision. Leave generated reports untracked and out of the commit.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.Rendering.Tests\SitemapUiLogicTests.cs
  git diff --check -- tests\OpenHab.Rendering.Tests\SitemapUiLogicTests.cs
  ```

  Expected: both checks pass. Do not edit the status page or verification reports as part of this task.

---

### Task 11: Cover State Pattern Formatting Flags and Invalid Patterns

**Files:**
- Modify: `tests/OpenHab.Sitemaps.Tests/OpenHabStatePatternFormatterTests.cs`
- Inspect only: `src/OpenHab.Sitemaps/Runtime/OpenHabStatePatternFormatter.cs`

**Scope and decision:** Sonar analysis `2026-10-30T14:58:10+0000`, revision `127b4464759efc5e275b24106c5ed86e1f26b6b0`, reports project coverage 79.1% (line 80.0%, branch 77.0%; 1,265 uncovered lines and 678 uncovered conditions). The quality gate remains ERROR at 74.0% new coverage, threshold 80%. Task 10 raised `SitemapUiLogic.cs` to 94.0% line / 75.8% branch overall, but its new code remains 79.12%, with 3 uncovered lines and 16 conditions.

The formatter is a stronger narrow next cohort than chasing overall percentages: its new code has 81.03% coverage with 17 uncovered lines and 27 conditions, and existing tests cover basic formatting but omit common signs, zero padding, literal escapes, and malformed conversions. These are deterministic, user-visible display and safe-fallback contracts. `SitemapRowVisualPolicy.cs` is also viable (80.77%, 6 lines / 24 conditions), particularly slider precision, but its existing slider tests already exercise the main paths and some remaining conditions overlap formatter calls. The runtime controller has 18 lines / 21 conditions uncovered but 87.25% new coverage and more involved asynchronous setup; the notification resolver has only 1 line / 1 condition at 86.67%. `SseMessageParser.cs` has zero new lines to cover, and the mapper/planner are fully covered on new code. The new-code period and server aggregation determine the actual gate impact; neither an improvement nor reaching 80% is promised.

- [ ] **Step 1: Capture focused baseline**

  Before changing tests, run the following and retain the report path. Repeat with a new GUID after adding tests; never delete existing results.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-PatternCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --filter FullyQualifiedName~OpenHabStatePatternFormatterTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

- [ ] **Step 2: Add public formatting and fallback cases**

  Extend the existing theories with exact output assertions for:

  - Numeric signs and padding: `("%+06.1f", "3.2") -> "+003.2"`; `("%06.1f", "-3.2") -> "-003.2"`; `("%06.1f", "3.2") -> "0003.2"`; `("% 5d", "42") -> "   42"`; `("%-5d", "42") -> "42   "`; `("%+d", "-42") -> "-42"`.
  - Default float precision and escaped literals on both sides: `("%f", "1.25") -> "1.250000"`; `("%% %d %%", "42") -> "% 42 %"`; string input is not given a numeric sign: `("%+5s", "ab") -> "   ab"`.
  - Safe rejection (`null` result): null or empty raw state with `%d`; `%`, `%5`, `%.f`, `%q`, `%d %s`, and a trailing unescaped `%` after `%d`, all with raw `"42"`; `%d` with `"not-a-number"`; `%f` with `"not-a-number"`.

  Exercise only public APIs and do not change production code or exclusions. If a case reveals a genuine correctness problem, preserve the failure and stop for separate correctness planning; do not alter the contract merely to raise coverage. Avoid assuming unreachable private defensive branches can be hit.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; the new tests should pass against existing behavior, not necessarily start red:

  ```powershell
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --filter FullyQualifiedName~OpenHabStatePatternFormatterTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`; distinguish the known DesktopBridge import limitation from test failures. No tray or package build is required for test-only changes.

- [ ] **Step 4: Compare fresh OpenCover reports and verify formatting**

  Repeat Step 1 and compare baseline and after reports for `OpenHabStatePatternFormatter.cs`, using file IDs within each module and method sequence/branch points, not report-wide percentages. Inspect visited counts and source lines for the new sign, padding, escape and rejection cases; report covered/total sequence points and branch points before and after, and identify any points that did not change. Confirm both reports contain the target and use the same focused test filter and settings. OpenCover points are not Sonar new-code metrics; only a subsequent server analysis of the pushed revision can establish gate impact. Do not add reports to the commit or update status/verification reports.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.Sitemaps.Tests\OpenHabStatePatternFormatterTests.cs
  git diff --check -- tests\OpenHab.Sitemaps.Tests\OpenHabStatePatternFormatterTests.cs
  ```

  Expected: all tests and checks pass and meaningful targeted coverage improves; if it does not, inspect the points and retain only distinct behavioral tests.

---

### Task 12: Cover Slider Precision and Display Fallback Decisions

**Files:**
- Modify: `tests/OpenHab.Rendering.Tests/SitemapSurface/SitemapRowVisualPolicyTests.cs`
- Inspect only: `src/OpenHab.Rendering/SitemapSurface/SitemapRowVisualPolicy.cs`

**Scope and decision:** Sonar analysis `2026-10-30T15:11:11+0000`, revision `cf3daaa0715ef062b3e02521e541bc562a996de0`, reports project coverage 79.5%, line 80.3%, branch 77.7%, 1,251 uncovered lines and 656 uncovered conditions; the new-code gate remains ERROR at 75.1% against 80%. Task 11 improved formatter new coverage to 96.55%, with 3 uncovered new lines and 5 conditions. `SitemapRowVisualPolicy.cs` has 80.77% new coverage with 6 uncovered lines and 24 conditions (92.1% line / 70.0% branch overall). Its existing tests cover ordinary slider precision and one pattern rejection, but omit meaningful precision boundaries and fallback inputs. The slider display/command policy is deterministic and user-facing, and offers a bounded new-code opportunity, not merely a low-total-coverage target.

`SitemapUiLogic.cs` is also a candidate at 79.12% new coverage with 3 lines / 16 conditions, but Task 10 already covered its color cohort; without line-level evidence a further broad sweep is less focused. The runtime controller has 18 / 21 new misses but requires navigating a 1,909-line asynchronous controller. The notification resolver has only 1 / 1 new miss, and the SSE parser has no new-code lines. These counts do not establish which particular proposed tests hit Sonar new code; check actual local points and subsequent Sonar analysis. Gate passage is not promised.

- [ ] **Step 1: Capture a focused baseline**

  Before editing tests, run the command below and retain its report path. Repeat with a new GUID after edits; do not delete existing results.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-SliderPolicyCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowVisualPolicyTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm the target source appears in the report. Use the same filter, configuration and settings for both runs.

- [ ] **Step 2: Add slider behavior cases**

  Extend existing theories or add focused facts, asserting exact invariant strings through the public APIs:

  - `FormatSliderStateText` with `template: "old 1,000 °C"`, value `2.5`, step `null` returns `"old 2.500 °C"`; template `"no numeric value"`, value `2.5` returns `"2.5"`. A valid pattern `"%.1f °C"` takes precedence over template `"old 1,000 °C"` and step `0.001`, yielding `"2.5 °C"`.
  - With unsupported pattern `"%q"`, template `"value 1.00 V"`, value `2.5`, step `null`, fallback yields `"value 2.50 V"`; whitespace pattern and negative step with template `null`, value `2.5` yield `"2.5"`.
  - `FormatSliderStateText(null, 1.23456)` yields `"1.2346"`; template `"0.00000 V"`, value `1.2`, yields `"1.2000 V"`, exercising the four-decimal cap. `FormatSliderCommand(1.23456, null)` yields `"1.2346"`; `FormatSliderCommand(-1.25, 0.01)` yields `"-1.25"`, and `(2d, 0.01)` yields `"2"` after trimming step-driven zeros.

  Assert observable behavior only; no production changes, exclusions, or reflection. If an expected contract fails, preserve evidence and stop for separate correctness planning rather than altering behavior for coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; test-only additions should pass existing behavior:

  ```powershell
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowVisualPolicyTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`, distinguishing the known DesktopBridge import limitation from test failures. No tray or package build is required for test-only changes.

- [ ] **Step 4: Compare coverage and check formatting**

  Repeat Step 1 into a fresh directory. For each report, resolve the target file ID within its module and compare covered/total sequence and branch points for `SitemapRowVisualPolicy.cs`, including source lines and per-method visited points for slider formatting, precision and fallback. Report before/after counts and unchanged relevant points; if no targeted points improve, inspect inputs and retain only distinct meaningful contracts. Focused OpenCover points are not Sonar new-code metrics; only a subsequent server analysis of the pushed revision establishes gate impact. Keep reports out of the commit, and do not edit status or verification reports.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.Rendering.Tests\SitemapSurface\SitemapRowVisualPolicyTests.cs
  git diff --check -- tests\OpenHab.Rendering.Tests\SitemapSurface\SitemapRowVisualPolicyTests.cs
  ```

  Expected: focused and direct tests and checks pass; assess measured coverage rather than assuming gate passage.

---

### Task 13: Cover Sitemap Row Identity and Media Refresh Keys

**Files:**
- Modify: `tests/OpenHab.Rendering.Tests/SitemapSurface/SitemapRowVisualPolicyTests.cs`
- Inspect only: `src/OpenHab.Rendering/SitemapSurface/SitemapRowVisualPolicy.cs`

**Scope and decision:** The pushed `main` revision `bef8e44de6c8a13ccc377022ac8a9cfa6f91d1ff`, analyzed 2026-10-30 at 15:22:54 UTC, has project coverage 79.5%, line 80.3%, branch 77.9%, 1,248 uncovered lines and 652 uncovered conditions. The quality gate remains ERROR at 75.3% new coverage against 80%. Task 12 improved the visual policy to 85.26% new coverage and 96.1% line / 75.0% branch overall, but Sonar still reports uncovered lines 62–64, line 55 at 1/2 conditions, line 70 at 2/18, and line 74 at 5/6. Source inspection maps these to identity fallback and media visual-key construction, not slider formatting. Existing direct tests cover search and widget identities and non-media state stability, but not the item/anonymous identity fallbacks or media key inputs. These pure keys govern row matching and media refresh, making the cohort meaningful and bounded; compiler-generated conditions may not all be reachable independently.

`SitemapUiLogic.cs` remains at 79.12% new coverage with 3 new lines / 16 conditions missed, but its prior color cohort and aggregate counts alone do not identify a comparably specific uncovered behavior. The asynchronous runtime controller has 18 / 21 misses at 87.25% new coverage and a much larger setup; the notification resolver has 1 / 1 at 86.67% and requires HTTP/cache orchestration. Do not infer gate impact from file percentages or promise passage.

- [ ] **Step 1: Capture a focused baseline**

  Before test changes, collect a report using the command below; retain its path. Repeat with a new GUID after changes. Do not delete existing results.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-RowKeyCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowVisualPolicyTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm `SitemapRowVisualPolicy.cs` appears in the report; use the same configuration, filter and settings for both runs.

- [ ] **Step 2: Add public identity and media-key cases**

  Extend the existing test class, asserting observable key strings or equality/inequality:

  - With no search key or widget ID, an item row produces `item:` identity including its item name, control, action, label and optional icon, command, release command and period. Assert null optional values produce empty fields; use populated values to show relevant changes alter the key.
  - With blank search key, widget ID and item name, assert the anonymous `row:` fallback includes control, action, icon and label; verify null icon produces an empty field and a populated icon changes the identity. Preserve the existing search-over-widget precedence test.
  - For each of `Webview`, `Mapview` and `Video`, hold identity constant and show URL, encoding and height changes independently change the visual key. Assert a fully null media payload produces empty `url`, `raw`, `encoding` and `height` fields. Test raw-state precedence: `RawItemState` over `RawState` over `State`, falling through to empty only when all are null; changing a lower-priority value while a higher-priority value exists leaves the key unchanged.
  - For a non-media control, changes to these media-only fields and row index leave the visual key unchanged; changing an included command or release command changes it. Use stable widget identity to isolate visual-key decisions.

  Exercise public APIs only, with no reflection, exclusions or production changes. If a contract reveals a genuine defect, preserve the failing evidence and stop for separate correctness planning rather than altering production behavior to improve coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; new tests should pass existing behavior:

  ```powershell
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRowVisualPolicyTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`, distinguishing the known DesktopBridge import limitation from test failures. No tray or package build is required for test-only changes.

- [ ] **Step 4: Compare coverage and verify formatting**

  Repeat Step 1 in a fresh directory. Resolve the target file ID within each report's module and compare covered/total sequence and branch points for the file and specifically `BuildRowIdentityKey` and `BuildRowVisualStateKey`, including source lines and unchanged points. If targeted points do not improve, inspect inputs and retain only distinct meaningful contracts. Focused OpenCover is not Sonar new-code coverage; only a subsequent server analysis of the pushed revision establishes gate impact. Keep reports out of commits and do not edit status or verification reports.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.Rendering.Tests\SitemapSurface\SitemapRowVisualPolicyTests.cs
  git diff --check -- tests\OpenHab.Rendering.Tests\SitemapSurface\SitemapRowVisualPolicyTests.cs
  ```

  Expected: tests and formatting checks pass; report observed coverage rather than assuming any gate result.

---

### Task 14: Cover Input-Hint Date and Time Normalization

**Files:**
- Modify: `tests/OpenHab.Rendering.Tests/SitemapUiLogicTests.cs`
- Inspect only: `src/OpenHab.Rendering/SitemapUiLogic.cs`

**Scope and decision:** The pushed `main` revision `346ac2c85540b318a026b118e095459838e8860f`, analyzed 2026-10-30 at 16:22:09 UTC, reports project coverage 79.8%, line 80.4%, branch 78.6%, 1,245 uncovered lines and 632 uncovered conditions. The quality gate remains ERROR: `new_coverage` is 75.97% against the 80% threshold; the new period has 2,455 lines to cover, 701 uncovered new lines and 125 uncovered new conditions. Task 13 closed `SitemapRowVisualPolicy.cs` at 100% overall and new coverage, so that file no longer offers remaining points. `SitemapUiLogic.cs` is 79.12% new coverage with exactly 3 uncovered new lines and 16 uncovered new conditions (94.0% line / 75.8% branch overall), and Sonar reports lines 247, 248 and 249 as the uncovered ones: the `SitemapInputHint.Date`, `.Time` and `.DateTime` formatted-output arms of public `NormalizeInputByHint`. That is a distinct cohort from Task 10's color arms, and the existing tests cover only the number and color arms.

The other candidates are weaker for a bounded next step. `SitemapRuntimeController.cs` still needs asynchronous runtime, event-stream and reconcile setup spread across a very large file; `NotificationMediaResolver.cs` has very few uncovered new points but needs HTTP, notification-media cache and image orchestration; `SseMessageParser.cs` has no comparable uncovered new lines. Only a later Sonar analysis of the pushed revision can establish gate impact, so this task promises no coverage number and no quality-gate passage.

- [ ] **Step 1: Capture a focused baseline**

  Before editing tests, collect a focused OpenCover report into a fresh task-specific directory using the existing `SitemapUiLogicTests` filter and repository coverage settings; retain the report path for comparison and do not delete existing results:

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-InputHintCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapUiLogicTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm the report contains `SitemapUiLogic.cs`, and use the same filter, configuration and settings for both runs.

- [ ] **Step 2: Add precise public normalization cases**

  Extend the existing test class with the Date, Time and DateTime hints of `NormalizeInputByHint`, asserting exact strings only through the public API. Make the culture assumption explicit: set and restore `CultureInfo.CurrentCulture` inside the test, or assert only invariant inputs, instead of depending on the machine's default culture.

  - Valid invariant inputs produce the documented normalized formats `yyyy-MM-dd`, `hh\:mm\:ss` and `yyyy-MM-dd'T'HH:mm:ss`: Date `"  2024-02-29  "` becomes `"2024-02-29"`; Time `"  12:34  "` becomes `"12:34:00"`, including the seconds the format appends; DateTime `"  2024-02-29T12:34:56  "` becomes `"2024-02-29T12:34:56"`. Add a Date input carrying a time component, such as `"2024-02-29T12:34:56"` to `"2024-02-29"`, as a second deterministic invariant case rather than assuming offset conversion.
  - Unparsable input falls through to the trimmed raw value: Date `"  not-a-date  "` becomes `"not-a-date"`; Time `"  not-a-time  "` becomes `"not-a-time"`; DateTime `"  not-a-datetime  "` becomes `"not-a-datetime"`.
  - Culture-specific input parses through the current-culture fallback and still yields the normalized invariant output, for example `"29.02.2024"` under `pl-PL` becoming `"2024-02-29"`. Keep this case only if the runner resolves that culture; otherwise drop it rather than weakening the assertion.
  - Null, empty and whitespace-only input return `null` for each of the three hints.
  - The `SitemapInputHint.Number` leading-zero arm already has coverage, so add a plain-number case such as `" 12.50 "` becoming `"12.5"` only if the Step 4 report shows the plain-number arm still uncovered and the case adds distinct meaning.

  Assert public results rather than private parsing helpers. If an expectation exposes a genuine correctness defect, preserve the failing evidence and stop for separate correctness planning instead of changing production behavior to raise coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; these test-only additions are expected to pass against existing behavior rather than start red:

  ```powershell
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapUiLogicTests --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false` and report the known DesktopBridge `.wapproj` import limitation separately from test failures. No tray or package build is required for a test-only change.

- [ ] **Step 4: Compare fresh coverage and verify formatting**

  Repeat Step 1 into a new GUID directory. Resolve the `SitemapUiLogic.cs` file ID inside each report's module and compare covered/total sequence and branch points for `NormalizeInputByHint` and its Date, Time and DateTime parsers, including source lines 247, 248 and 249, before and after. Report the before/after counts and any points that did not change, and retain only tests that assert distinct meaningful contracts. Focused OpenCover points are not Sonar new-code metrics; only a subsequent server analysis of the pushed revision establishes gate impact.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.Rendering.Tests\SitemapUiLogicTests.cs
  git diff --check -- tests\OpenHab.Rendering.Tests\SitemapUiLogicTests.cs
  ```

  Expected: all tests, the coverage comparison and both checks pass, with observed coverage reported rather than an assumed gate result.

  This task changes tests only. Do not change production code, add or adjust coverage exclusions, edit the status page or verification documents, or commit generated coverage reports or temporary result directories.

---

### Task 15: Exclude Non-.NET Compatibility Scripts from Coverage

**Files:**
- Modify: `.github/workflows/sonarcloud.yml`
- Modify: `docs/superpowers/verification/coverage-exclusion-inventory.md`
- Verify only, no edit: `coverage.runsettings`, `scripts/Test-OpenHabServerCompatibility.ps1`, `scripts/Sync-AppVersion.ps1`, `tests/CompatibilityProbe/Test-OpenHabServerCompatibility.Integration.ps1`, `tests/CompatibilityProbe/FakeOpenHabCompatibilityServer.ps1`, `tests/CompatibilityProbe/StalledCompatibilityParser.ps1`

**Scope and evidence:** The pushed `main` revision `88911d5ddd31ead25a77437f7a7381cf3672cfa5`, analyzed 2026-10-30 at `2026-10-30T16:42:08+0000`, reports overall coverage `80.0%`, line coverage `80.5%`, and branch coverage `79.0%`. The latest analysis, `2026-10-30T19:39:23+0000` on revision `cbab6e2124f3c2d9e41338c3c60c3a27721d6317`, reports the quality gate ERROR only on new-code coverage at `75.97%` against the `80%` threshold; that new-code period contains 2,455 lines to cover, 701 uncovered lines, and 125 uncovered conditions. A per-file ranking of the uncovered new lines in that `cbab6e2` analysis shows 607 of 701 (86.6%) are PowerShell: `scripts/Test-OpenHabServerCompatibility.ps1` (304), `tests/CompatibilityProbe/Test-OpenHabServerCompatibility.Integration.ps1` (133), `scripts/Sync-AppVersion.ps1` (83), `tests/CompatibilityProbe/FakeOpenHabCompatibilityServer.ps1` (79), and `tests/CompatibilityProbe/StalledCompatibilityParser.ps1` (8). All five report `0%` because the C# OpenCover collection can only instrument .NET assemblies, so `dotnet test` can neither execute nor measure these scripts and those lines cannot be covered by this gate.

The user explicitly approved excluding these non-.NET scripts from Sonar coverage. Scope is exactly those five PowerShell tooling files. The compatibility probe and its fake-server and stalled-parser fixtures are verified by their own integration script (`tests/CompatibilityProbe/Test-OpenHabServerCompatibility.Integration.ps1`) rather than by unit coverage, and `scripts/Sync-AppVersion.ps1` is release tooling exercised by `bump-app-version.yml` and the CI `-CheckOnly` step. No C# file is added to coverage or CPD exclusions: `sonar.cpd.exclusions` keeps only the three existing WinUI files, and every `src/**/*.cs` coverage target from Tasks 1-14 stays visible. This exclusion is not a substitute for the earlier test work and must not be used to hide testable logic; it only stops counting script files that the C# coverage toolchain cannot measure.

- [ ] **Step 1: Add the five paths to `sonar.coverage.exclusions`**

  The workflow currently passes one `sonar.coverage.exclusions` property as a single quoted, comma-separated, repo-relative path list inside the `dotnet-sonarscanner begin` invocation. Keep exactly one `/d:sonar.coverage.exclusions=` argument, because a duplicate `/d:` for the same property overrides the earlier value instead of merging with it.

  Keep the list readable without retyping values or breaking the `begin` arguments: move the existing single-line value into a dedicated array in the same `run:` block before the `begin` command, one path per line, keep the existing entries in their current order, and add the five new paths at the end.

  ```powershell
  $sonarCoverageExclusions = @(
    'src/OpenHab.Windows.Tray/App.xaml.cs',
    'src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs',
    'src/OpenHab.Windows.Tray/DwmWindowDecorations.cs',
    'src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs',
    'src/OpenHab.Windows.Tray/MainWindow.xaml.cs',
    'src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs',
    'src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml.cs',
    'src/OpenHab.Windows.Tray/Rendering/OpenHabIconImageSourceLoader.cs',
    'src/OpenHab.Windows.Tray/Rendering/CompositionAnimationHelper.cs',
    'src/OpenHab.Windows.Tray/Rendering/SitemapComboHelper.cs',
    'src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs',
    'src/OpenHab.Windows.Tray/Rendering/SitemapPageTransitionAnimator.cs',
    'src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapIconAuthResolver.cs',
    'src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs',
    'src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs',
    'src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs',
    'src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs',
    'src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs',
    'src/OpenHab.Windows.Tray/Shortcuts/ShortcutInteractiveCommandWindow.cs',
    'src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs',
    'src/OpenHab.Windows.Tray/Shortcuts/ShortcutSettingsControls.cs',
    'src/OpenHab.Windows.Tray/Tray/TrayIconService.cs',
    'src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs',
    'src/OpenHab.Windows.Tray/Startup/StartupManager.cs',
    'src/OpenHab.Windows.Tray/DeviceInfo/WindowsBatteryInfoReader.cs',
    'src/OpenHab.Windows.Tray/DeviceInfo/WindowsBluetoothInfoReader.cs',
    'src/OpenHab.Windows.Tray/DeviceInfo/WindowsNetworkInfoReader.cs',
    'src/OpenHab.Windows.Tray/DeviceInfo/WindowsSessionInfoReader.cs',
    'src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs',
    'src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs',
    'src/OpenHab.Windows.Notifications/ToastService.cs',
    'src/OpenHab.Core/Auth/WindowsCredentialStore.cs',
    'scripts/Test-OpenHabServerCompatibility.ps1',
    'scripts/Sync-AppVersion.ps1',
    'tests/CompatibilityProbe/Test-OpenHabServerCompatibility.Integration.ps1',
    'tests/CompatibilityProbe/FakeOpenHabCompatibilityServer.ps1',
    'tests/CompatibilityProbe/StalledCompatibilityParser.ps1'
  ) -join ','
  ```

  Then pass that variable as the single property value, preserving the trailing backtick that continues the command so `/d:sonar.qualitygate.wait=true` stays part of the same invocation:

  ```yaml
              /d:sonar.coverage.exclusions="$sonarCoverageExclusions" `
  ```

  Use Sonar's comma-separated relative-path glob syntax: repo-relative paths with forward slashes, no `./` prefix, no `**/` prefix, matching how the existing entries are written. Explicit relative paths are sufficient globs for these five single files. Do not touch `sonar.cpd.exclusions`, do not add any `.cs` path, and do not convert the whole property to a wildcard such as `scripts/*.ps1` or `tests/CompatibilityProbe/*.ps1`, which would hide future files that are not part of this approved scope.

- [ ] **Step 2: Update the coverage exclusion inventory**

  In `docs/superpowers/verification/coverage-exclusion-inventory.md`, follow the existing structure instead of adding a new document:

  - Add one row per file to the `## Excluded Files` table, using the same `File | Reason | Verification` columns and the existing backticked repo-relative path style. Use a reason such as `PowerShell compatibility tooling; not measurable by dotnet test/OpenCover` and the verification `tests/CompatibilityProbe/Test-OpenHabServerCompatibility.Integration.ps1` for the compatibility probe, fake server, stalled parser and integration script, and `bump-app-version.yml` plus the CI `Sync-AppVersion.ps1 -CheckOnly` step for `scripts/Sync-AppVersion.ps1`.
  - Record the evidence in the same entry: analysis `2026-10-30T19:39:23+0000`, revision `cbab6e2124f3c2d9e41338c3c60c3a27721d6317`, the `607` of `701` uncovered new lines, and the per-file counts `304/133/83/79/8`; note that the `80.0`/`80.5`/`79.0` project figures come from the earlier `2026-10-30T16:42:08+0000` analysis of revision `88911d5ddd31ead25a77437f7a7381cf3672cfa5` rather than from the `cbab6e2` analysis.
  - Extend `## Tool Mapping` with an explicit note that these five entries are Sonar-only: `XPlat Code Coverage` with `coverage.runsettings` instruments .NET assemblies and never reports `.ps1` files, so no `coverage.runsettings` entry exists or is required for them. This records the rationale for the otherwise missing third location instead of silently skipping one of the three documentation points required by `AGENTS.md`.
  - State in that note that the exclusion removes no C# coverage target and that the scripts remain verified by their own integration/release workflow.

- [ ] **Step 3: Verify the workflow structure and path syntax**

  Run structural assertions instead of relying on eyeballing the long line:

  ```powershell
  $workflow = Get-Content .github\workflows\sonarcloud.yml -Raw
  $propertyCount = ([regex]::Matches($workflow, '/d:sonar\.coverage\.exclusions')).Count
  if ($propertyCount -ne 1) { throw "Expected exactly one sonar.coverage.exclusions argument, found $propertyCount." }
  if (-not $workflow.Contains('/d:sonar.coverage.exclusions="$sonarCoverageExclusions" `')) { throw 'Coverage exclusions are not passed through the dedicated variable with a continuation backtick.' }
  if (-not $workflow.Contains("'src/OpenHab.Core/Auth/WindowsCredentialStore.cs'")) { throw 'Existing C# coverage exclusion entries were not preserved.' }
  if (-not $workflow.Contains(") -join ','")) { throw 'Coverage exclusion array is not joined with commas.' }
  if (([regex]::Matches($workflow, 'sonar\.cpd\.exclusions')).Count -ne 1) { throw 'Expected exactly one sonar.cpd.exclusions argument.' }
  $expectedCpd = 'sonar.cpd.exclusions="src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs,src/OpenHab.Windows.Tray/MainWindow.xaml.cs,src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs"'
  if (-not $workflow.Contains($expectedCpd)) { throw 'sonar.cpd.exclusions must keep exactly its three existing WinUI entries.' }
  foreach ($path in @(
    'scripts/Test-OpenHabServerCompatibility.ps1',
    'scripts/Sync-AppVersion.ps1',
    'tests/CompatibilityProbe/Test-OpenHabServerCompatibility.Integration.ps1',
    'tests/CompatibilityProbe/FakeOpenHabCompatibilityServer.ps1',
    'tests/CompatibilityProbe/StalledCompatibilityParser.ps1'
  )) {
    if (([regex]::Matches($workflow, [regex]::Escape($path))).Count -ne 1) { throw "Expected exactly one occurrence of $path." }
  }
  ```

  Expected: all assertions pass, the variable is declared in the same `run:` block before `dotnet-sonarscanner begin`, and the quoted value still ends with the backtick continuation so `/d:sonar.qualitygate.wait=true` remains part of the same command. Also review the rendered `begin` line by reading it after the edit to confirm no stray quote, missing comma, or split-line break was introduced.

  If the `powershell-yaml` module is available, additionally run `Get-Content .github\workflows\sonarcloud.yml -Raw | ConvertFrom-Yaml | Out-Null` and expect no parse error; if it is not installed, rely on the structural assertions plus the line review rather than adding a new tool dependency.

- [ ] **Step 4: Verify whitespace and formatting**

  ```powershell
  git diff --check -- .github\workflows\sonarcloud.yml docs\superpowers\verification\coverage-exclusion-inventory.md
  ```

  Expected: no output and exit code `0`, with no trailing-whitespace or mixed line-ending changes in either file.

- [ ] **Step 5: Confirm the .NET gates and the PowerShell verification path**

  Run the C# gates that remain the regression signal for this configuration change:

  ```powershell
  dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  Expected: C# tests pass. Note the known limitation explicitly: this command cannot validate the five excluded `.ps1` scripts and does not exercise Sonar exclusion behavior, because `XPlat Code Coverage` and `dotnet test` only instrument .NET assemblies; a full-solution run may also surface the known DesktopBridge `.wapproj` import limitation, which must be reported separately from test failures.

  Then run the script-side verification that actually covers the compatibility tooling:

  ```powershell
  pwsh -NoProfile -File tests\CompatibilityProbe\Test-OpenHabServerCompatibility.Integration.ps1
  ```

  Expected: PASS against the fake server. If the environment cannot run it, record the reason and do not claim the excluded scripts are verified.

- [ ] **Step 6: Confirm the post-push Sonar analysis recomputes coverage**

  After committing and pushing, wait for the `SonarCloud` workflow and check the analysis of the pushed revision via the Sonar API/UI. Expect:

  - the five `.ps1` paths no longer appear in coverage measures or per-file coverage lists;
  - `lines_to_cover` and `uncovered_lines` are recomputed without those files, and the new-code metric is recomputed for the new period;
  - the visible effect is the removal of 607 previously uncounted-for-coverage new lines, so `new_coverage` should be recalculated against a smaller denominator.

  Do not promise a specific gate result or state that the gate is fixed. Report the observed `new_coverage`, `lines_to_cover`, `uncovered_lines` and the presence/absence of the five paths exactly as returned. If the paths still appear, confirm the scanner received the property (the analysis log should show a single merged `sonar.coverage.exclusions` value) before treating it as a coverage-metric problem.

- [ ] **Step 7: Independent review, then commit**

  Ask an independent reviewer to confirm three things before committing: the argument count and quoting in `sonarcloud.yml` are correct and the script still parses; the excluded set is exactly the five approved PowerShell files with no C# additions to `sonar.coverage.exclusions` or `sonar.cpd.exclusions`; and the inventory rows plus the `coverage.runsettings` rationale satisfy the three-place documentation rule.

  ```powershell
  git add .github\workflows\sonarcloud.yml docs\superpowers\verification\coverage-exclusion-inventory.md
  git commit -m "Exclude non-.NET compatibility scripts from coverage"
  ```

  Expected: the commit contains only those two files. Do not change `coverage.runsettings`, production code, tests, workflows beyond the exclusion value, the current-state status page, or other verification evidence as part of this task.

---

### Task 16: Repair SonarCloud Failure Diagnostics PowerShell Syntax

**Files:**
- Modify: `.github/workflows/sonarcloud.yml` only.

**Scope and evidence:** The failure-only `Explain failed SonarCloud quality gate` step has an unterminated regex string at line 211, introduced by `cbab6e2`. It cannot affect a green run, but its script fails to parse when diagnostics are needed. Analysis `2026-10-30T20:25:29+0000` of `32d4eca9ae028cf4350627d66b1905fa76c6d5e8` is GREEN; this repair is independent of coverage and must not alter the hardened workflow, exclusions, gate behavior, or secrets handling.

- [ ] **Step 1: Fix the condition only**

  Replace the malformed condition with exactly:

  ```powershell
  if ($env:SONAR_PULL_REQUEST -match '^\d+$') {
  ```

  Review `git diff -- .github/workflows/sonarcloud.yml` to confirm this is the only workflow change relative to the starting revision. Do not change coverage configuration or other diagnostics logic.

- [ ] **Step 2: Parse the actual diagnostics block and check whitespace**

  Extract the named step's `run:` block from the workflow without executing it, then parse it using the PowerShell AST parser:

  ```powershell
  $workflow = Get-Content .github\workflows\sonarcloud.yml -Raw
  $match = [regex]::Match($workflow, '(?ms)^      - name: Explain failed SonarCloud quality gate\r?\n.*?^        run: \|\r?\n(?<script>.*?)(?=^      - name: |\z)')
  if (-not $match.Success) { throw 'Diagnostics run block not found.' }
  $script = $match.Groups['script'].Value
  $tokens = $null
  $errors = $null
  [System.Management.Automation.Language.Parser]::ParseInput($script, [ref]$tokens, [ref]$errors) | Out-Null
  if ($errors.Count -ne 0) { $errors | Format-List | Out-String | Write-Error; throw 'Diagnostics block has PowerShell parse errors.' }
  git diff --check
  ```

  Expected: zero AST parse errors and `git diff --check` exits zero. This is a syntax check, not proof of a live red-gate API response. Do not edit status or verification reports; do not mix this fix with Task 17.

---

### Task 17: Cover Notification List Ordering and Presentation

**Files:**
- Modify: `tests/OpenHab.App.Tests/Notifications/NotificationsPageControlTests.cs`.
- Inspect only: `src/OpenHab.Windows.Tray/Notifications/NotificationListProjection.cs`, `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs`.

**Scope and decision:** The green analysis `2026-10-30T20:25:29+0000` for `32d4eca9ae028cf4350627d66b1905fa76c6d5e8` reports overall coverage 85.6%, line 89.1%, branch 79.0%, and new coverage 92.3% against 80%; 5,731 lines are coverable, 626 uncovered, and the new period has 1,848 lines, 94 uncovered lines and 125 uncovered conditions. Task 15's five PowerShell exclusions are confirmed effective. The gate is already green, so choose behavior rather than gate pressure.

`NotificationListProjection.cs` has 13 uncovered lines and 10 conditions (75% line / 37.5% branch). It is deterministic projection despite its tray location: existing `NotificationsPageControlTests` cover name sorting and a 12-minute label, but not date order, ties, elapsed boundaries, or read/severity presentation. It uses only the `Visibility` enum, not live WinUI controls or OS calls. `NotificationMediaCache` already tests eviction, age, traversal and atomic cleanup; its remaining misses are less cohesive. `SitemapUiLogic` now mostly has partial guards after Task 14; the runtime controller is much larger and asynchronous. `WindowsFocusInfoReader` has testable injected decisions already exercised, but much of its remaining body is process/OS integration. This task promises no gate or numerical improvement.

- [ ] **Step 1: Capture focused baseline**

  Before editing tests, run the following with a fresh GUID directory, retaining the report path; repeat identically after edits using a new GUID. Do not delete existing results.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-NotificationProjectionCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationsPageControlTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm `NotificationListProjection.cs` is in the report and use the same configuration, filter and settings both times.

- [ ] **Step 2: Add deterministic projection behavior cases**

  Extend the existing class and its notification factory as appropriate. Use a fixed UTC `now`, `DefaultEnglishTextLocalizer.Instance`, and assertions through `CreateRows` and the returned view models:

  - Verify ascending and descending creation-date order with distinct timestamps; for equal timestamps assert title then message use ordinal case-insensitive order and IDs break remaining ties ordinally. For name order assert the same message/ID tie-breakers and retain the existing null-title localized fallback test. Assert row identity and data are preserved.
  - Cover elapsed boundaries: future and 59 seconds give `Just now`; 1 minute gives `1m ago`, 59 minutes `59m ago`, 1 hour `1h ago`, 23 hours `23h ago`, and 1 day `1d ago`. Assert results by ID, not assumed input order.
  - Test all read/dismissed combinations: only neither read nor dismissed is unread; assert `UnreadVisibility` is `Visible` or `Collapsed` accordingly. For null, whitespace and populated severity, assert `SeverityText` preserves non-null text and `SeverityVisibility` is collapsed for blank, visible otherwise. Assert a null title becomes `openHAB` and icon/message values survive projection.
  - Assert null notifications and null localizer each throw `ArgumentNullException`; empty input returns an empty list. Avoid reflection, actual UI creation, sleeps, machine clock or OS integration.

  No production change or coverage exclusion is in scope. If a case exposes a genuine correctness issue, preserve failure evidence and stop for separate correctness planning instead of changing behavior to raise coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; tests should pass existing behavior rather than necessarily start red:

  ```powershell
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationsPageControlTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`, distinguishing the known DesktopBridge import limitation from test failures. No tray or package build is required for test-only changes.

- [ ] **Step 4: Compare focused coverage and check formatting**

  Repeat Step 1 in a fresh directory. Resolve the target file ID within each module and compare covered/total sequence and branch points, including source lines and per-method points for `CreateRows`, `FormatElapsedTime`, and view-model properties. Report baseline and post counts and unchanged relevant points; retain only distinct meaningful tests if points do not improve. Focused OpenCover is not Sonar new-code coverage; only a subsequent server analysis can establish actual impact. Leave generated reports out of version control.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.App.Tests\Notifications\NotificationsPageControlTests.cs
  git diff --check
  ```

  Expected: direct tests and formatting checks pass. Do not edit status or verification reports or add production changes or exclusions absent a separately considered correctness finding.

---

### Task 18: Cover Main UI Page Configuration Value Conversion

**Files:**
- Create: `tests/OpenHab.Core.Tests/Ui/MainUiPageComponentTests.cs`.
- Inspect only: `src/OpenHab.Core/Ui/MainUiPageComponent.cs`, `tests/OpenHab.Core.Tests/Api/OpenHabHttpClientMainUiPageTests.cs`.

**Scope and decision:** The verified analysis `2026-10-30T20:53:38+0000` for pushed revision `fe0165204a56f40e3c811b884a60bcbd2f9ff016` has a green gate: overall coverage 85.9%, line 89.3%, branch 79.3%, 5,731 lines to cover, 613 uncovered lines and 610 uncovered conditions. New code has 93.07% coverage, 1,848 lines, 81 uncovered lines and 115 uncovered conditions. Task 16's diagnostics fix was pushed and analyzed successfully; Task 17's `NotificationListProjection.cs` is confirmed at 100% line and branch.

`MainUiPageComponent.cs` has 8 uncovered lines and 12 uncovered conditions (74.2% line / 53.8% branch). Its public conversion methods interpret server-provided JSON for promoted-page labels, sidebar visibility and order. Existing Core API tests exercise common string, boolean and integer values but do not directly cover the conversion matrix. These are bounded, deterministic input decisions, including meaningful fallback behavior, rather than OS/UI integration. `NotificationMediaCache` includes filesystem-failure and defensive paths; `SitemapUiLogic` mostly retains partial guards after Task 14. The runtime controller is much larger and asynchronous, and the media resolver needs HTTP/cache orchestration; the SSE parser is a viable separate cohort but should not be bundled here. No coverage or gate improvement is promised: the gate is already green.

- [ ] **Step 1: Capture a focused baseline**

  Before creating the new tests, collect the existing Main UI page API tests as the comparable focused baseline. Retain its fresh report path and repeat the identical filter, settings and configuration after adding tests; do not delete existing results. The filter includes both the existing class and the new class, so the baseline remains meaningful even before the latter exists.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-MainUiConfigCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenHabHttpClientMainUiPageTests|FullyQualifiedName~MainUiPageComponentTests" --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm the report contains `MainUiPageComponent.cs` and retain the path for comparison.

- [ ] **Step 2: Add public JSON conversion cases**

  Construct components with cloned `JsonElement` values and assert exact results through their public methods, without HTTP, reflection or UI:

  - `GetConfigString`: missing key and JSON null, object and array return null; string preserves its contents, number returns raw JSON text (including a fractional value), and booleans return lowercase `true` and `false`.
  - `GetConfigBoolean`: missing key, JSON false, invalid string, number, null and object return false; JSON true and case-insensitive string `"TrUe"` return true; string `"false"` returns false.
  - `GetConfigInt32`: missing key returns null; numeric and invariant integer strings, including signed and whitespace-padded strings, produce the expected integers; JSON fractional and out-of-range numbers, invalid or out-of-range strings, boolean and null return null. Include both Int32 boundaries.

  Keep cases distinct and independent of machine culture. These are tests of existing behavior, not a request to broaden accepted input. If a case reveals a real correctness issue, preserve its failure and stop for a separate correctness decision; do not change production code or coverage exclusions merely to increase coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; the test-only cases should pass existing behavior:

  ```powershell
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OpenHabHttpClientMainUiPageTests|FullyQualifiedName~MainUiPageComponentTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`, reporting the known DesktopBridge import limitation separately from test failures. No tray or package build is required for this test-only change.

- [ ] **Step 4: Compare focused coverage and check formatting**

  Repeat Step 1 in a new GUID directory. Resolve the source file ID within each report's module and compare covered/total sequence and branch points for `MainUiPageComponent.cs` and each of its three conversion methods, including source lines and unchanged points. Report baseline and post counts; if targeted points do not improve, inspect inputs and retain only distinct meaningful behavioral tests. Focused OpenCover points are not project-wide or new-code Sonar metrics; only a later server analysis establishes actual impact. Do not add generated reports to version control.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.Core.Tests\Ui\MainUiPageComponentTests.cs
  git diff --check
  ```

  Expected: tests and checks pass. Do not edit status or verification reports, or add production changes or exclusions absent a separately considered correctness finding.

---

### Task 19: Cover SSE Event Envelope and Payload Decisions

**Files:**
- Modify: `tests/OpenHab.Core.Tests/Events/SseMessageParserTests.cs`.
- Inspect only: `src/OpenHab.Core/Events/SseMessageParser.cs`.

**Scope and decision:** Verified analysis `2026-10-30T21:03:31+0000` of pushed `de248d6611342e7395c212d96a5eb5eb647238bc` has gate OK: overall coverage 86.1%, line 89.4%, branch 79.7%, with 5,731 lines to cover, 605 uncovered lines and 598 uncovered conditions. New code is 93.07%, with 1,848 lines, 81 uncovered lines and 115 uncovered conditions. Task 18 is confirmed at 100% line and branch for `MainUiPageComponent.cs`.

`SseMessageParser.cs` has 9 uncovered lines and 14 conditions (81.6% line / 65.0% branch). Its 84-line pure parser makes a cohesive boundary around incoming SSE messages; existing tests cover three valid event types, a non-item topic, blank lines and comments, but omit malformed JSON, missing envelope fields, topic structure, payload values and unknown event types. These are genuine event acceptance and rejection decisions with no HTTP, filesystem or timing setup. The 1,909-line async runtime controller has a larger opportunity but would require selecting and staging a separate tightly bounded runtime behavior; the media cache's eviction paths already have tests and its remaining paths include filesystem failures, while the resolver requires HTTP/cache orchestration. The shortcut mapper's remaining enum fallbacks are partly redundant after Task 8, and the sitemap parser has broader, mixed widget decisions and substantial existing fixture coverage. None is bundled here. The gate is already green; no numerical improvement or gate outcome is promised.

- [ ] **Step 1: Capture a focused baseline**

  Before editing, collect the existing parser tests into a fresh task-specific directory; retain the report path and repeat the identical command with a new GUID after editing. Do not delete existing results.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-SseParserCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SseMessageParserTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm `SseMessageParser.cs` appears in the report; use the same configuration, filter and settings for both runs.

- [ ] **Step 2: Add event parsing cases**

  Extend the existing test class through `ParseLine`, constructing valid outer JSON with serialized nested payload strings where useful. Assert:

  - Non-data fields such as `event: update` and `data:{}` without the required space return null; malformed outer JSON and malformed nested payload JSON return null.
  - Each of `topic`, `type`, and `payload` missing independently, and each explicitly JSON null independently, returns null with the other envelope fields valid. Use object-shaped JSON; do not assume wrong-typed envelope values or a non-object root are caught, since `GetString`/`TryGetProperty` can throw `InvalidOperationException`.
  - With a valid payload and event type, topics with fewer than three segments, an incorrect first segment, and an incorrect second segment return null. Retain the existing non-item test; do not invent an empty-item-name validation absent from the current parser.
  - Missing payload `value` and JSON null, object, or array values return null; numeric `12.50` yields the exact raw text `12.50`, and boolean `true` and `false` yield lowercase text in a typed state event. Assert event type, item name, state, topic and type for accepted cases.
  - A valid envelope and payload with an unsupported event type returns null. Existing string-valued state and command tests remain the positive controls.

  Avoid reflection and real event streams. No production changes or coverage exclusions are in scope; if a case reveals a genuine correctness issue, preserve the failure and stop for separate correctness planning rather than changing behavior to raise coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; test-only additions should pass existing behavior:

  ```powershell
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SseMessageParserTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`, reporting the known DesktopBridge import limitation separately from test failures. No tray or package build is required for this test-only change.

- [ ] **Step 4: Compare focused coverage and check formatting**

  Repeat Step 1 in a new GUID directory. Resolve the target file ID within each report's module and compare covered/total sequence and branch points for `ParseLine`, `ExtractItemName` and `GetValueAsString`, including source lines, before/after counts and unchanged points. If targeted points do not improve, inspect inputs and retain only distinct meaningful behavioral tests. Focused OpenCover points are not project-wide or new-code Sonar metrics; only a subsequent server analysis establishes actual impact. Do not add generated reports to version control.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.Core.Tests\Events\SseMessageParserTests.cs
  git diff --check
  ```

  Expected: tests and checks pass. Do not edit status or verification reports, add production changes or exclusions absent a separately considered correctness finding, or commit as part of planning.

---

### Task 20: Harden SSE Parser Against Wrong JSON Kinds

**Files:**
- Modify: `src/OpenHab.Core/Events/SseMessageParser.cs`.
- Modify: `tests/OpenHab.Core.Tests/Events/SseMessageParserTests.cs`.

**Scope and decision:** Task 19 review surfaced a correctness finding, not a coverage-driven behavior change: `TryGetProperty` on a non-object root and `GetString` on a wrong-typed envelope field can throw `InvalidOperationException`, which the existing `JsonException` catch does not handle. A malformed or hostile server event could therefore fault parsing rather than be ignored. This also applies to the parsed nested payload root. Reject non-object outer and nested roots and wrong-typed `topic`, `type`, and `payload` values by returning null without throwing, while preserving all existing valid behavior. No coverage or quality-gate outcome is promised.

- [ ] **Step 1: Add failing regression tests first**

  Extend `SseMessageParserTests` through the public `ParseLine` API. Assert null without exceptions for outer roots `[]`, `null`, and a JSON string; also exercise those roots as serialized nested payload strings inside otherwise valid envelopes. For each of `topic`, `type`, and `payload`, independently substitute a JSON number, boolean, array, and object while keeping the other fields valid, and assert null. Use synthetic data only. Preserve every existing Task 19 test and its assertions; update the now-obsolete explanatory comment when the fix lands, without weakening or deleting tests. Retain malformed outer and nested JSON tests that actually reach `JsonDocument.Parse` and exercise the genuine `JsonException` path.

- [ ] **Step 2: Capture the red state**

  Before changing production code, run:

  ```powershell
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SseMessageParserTests --logger "console;verbosity=normal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  Expected: new wrong-kind cases fail with `InvalidOperationException`. Capture the exact test names, exception messages and relevant stack frames from this run as red evidence; distinguish any build or environment failure from the expected parser failure. Do not include real server payloads or sensitive data in evidence.

- [ ] **Step 3: Implement the smallest fix and show green**

  In the parser, guard both roots before property access and guard envelope string access using explicit `ValueKind` checks or a small safe string-access helper. Preserve existing null, missing-field, valid event, payload-value and malformed JSON behavior; keep the existing genuine `JsonException` handling. Avoid a broad catch that swallows unrelated exceptions. Do not log payloads, endpoints, credentials, or other sensitive data; do not change logging or SSE stream ownership. No unrelated refactors or coverage exclusions are in scope.

  Repeat the Step 2 focused command. Expected: all existing and new `SseMessageParserTests` pass, including the malformed JSON cases; record the green result alongside the red evidence.

- [ ] **Step 4: Run direct logic verification sequentially**

  ```powershell
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  Expected: all four projects pass. If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`; report the known DesktopBridge `.wapproj` import limitation separately from test failures.

- [ ] **Step 5: Check formatting and scope**

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include src\OpenHab.Core\Events\SseMessageParser.cs tests\OpenHab.Core.Tests\Events\SseMessageParserTests.cs
  git diff --check
  ```

  Expected: both checks pass. Review the diff for the two scoped files and retained Task 19 assertions. Do not edit status or verification reports, add exclusions, or commit as part of this task.

---

### Task 21: Cover Notification Media Cache Boundary and Pruning Decisions

**Files:**
- Modify: `tests/OpenHab.App.Tests/Notifications/NotificationMediaCacheTests.cs`.
- Inspect only: `src/OpenHab.App/Notifications/NotificationMediaCache.cs`.

**Scope and decision:** Verified analysis `2026-10-30T21:20:52+0000` of pushed `2325b774949218044d0bfbc1457473e2b6d1b573` reports gate OK, overall coverage 86.4% (line 89.6%, branch 80.2%), 5,736 lines to cover, 596 uncovered lines and 584 uncovered conditions; new code is 93.11%, with 1,856 lines, 81 uncovered lines and 115 uncovered conditions. Tasks 19/20 are confirmed: `SseMessageParser.cs` is 54/54 lines and 46/46 conditions. The gate is already green.

`NotificationMediaCache.cs` has 13 uncovered lines and 10 uncovered conditions (86.2% line / 80.8% branch). Existing tests exercise ordinary count, byte and age eviction, traversal, locked-file best effort and successful temporary cleanup, but omit meaningful boundaries and pruning decisions. The existing temporary directory and injected clock support deterministic behavioral tests without production hooks. Not all reported misses are necessarily reachable: enumeration/inspection races and some defensive path checks should not be forced through flaky OS behavior. `ShortcutWindowsMapper.cs` has 17 uncovered lines / 38 conditions, but Task 8 already addressed its pure branches and several remaining fallbacks are normalized away or redundant. `SitemapRuntimeController.cs` has 195 / 179 across many asynchronous clusters and needs a separately selected bounded behavior. Neither is included here. No coverage increase or gate outcome is promised.

- [ ] **Step 1: Capture a focused baseline**

  Before editing, collect the existing cache tests with a fresh GUID directory; retain the report path and repeat the identical command with a new GUID afterward. Do not delete existing results.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-NotificationMediaCacheCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationMediaCacheTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm `NotificationMediaCache.cs` appears in the report; use the same configuration, filter and settings for both runs.

- [ ] **Step 2: Add bounded observable cache cases**

  Extend the existing tests using isolated temporary roots and the existing injected clock, with fixed whole-second UTC timestamps; no sleeps, reflection, filesystem injection or new production test hooks. Assert public results and on-disk contents:

  - Constructor rejects zero and negative `maxFiles`, `maxBytes` and `maxAge` with `ArgumentOutOfRangeException`; positive limits are accepted. An oversized payload returns null without creating a cache file; a payload exactly at the byte limit is stored with its exact bytes. Verify an existing file remains unchanged after an oversized attempt.
  - With an explicit clock and `PruneAsync`, create entries just older than, exactly at, and newer than the age threshold; assert only the older entry is removed. On a missing root, `PruneAsync` and `Clear` leave it absent.
  - Create equal-timestamp files exceeding the count limit before calling `PruneAsync`; assert the ordinal-ignore-case path tie-break deletes the alphabetically first and keeps the other. Separately set the clock backward before storing a new file so it sorts oldest while capacity is exceeded: assert the newly stored file remains and the older existing file is removed, with exact retained bytes.
  - Store the same key using extensions with and without a leading dot; assert the same normalized local path, replacement bytes and no temporary artifacts. For `.` and `..` keys and an extension containing a path separator, assert null and no file outside the root. Do not assume the defensive root-prefix rejection can be reached independently of filename validation.
  - Use a regular file as the cache root to deterministically make directory creation fail; assert `StoreAsync` returns null, the blocker remains unchanged, and after removing that test-owned blocker a subsequent store succeeds. For an already-cancelled token with an otherwise valid payload, assert cancellation is observable and no file is written. Do not depend on access-control configuration or nondeterministic filesystem races.

  These cases test current behavior; retain only distinct meaningful cases and avoid claiming each maps to a particular Sonar condition without local point evidence. No production changes or exclusions are in scope absent a real correctness finding; if found, preserve evidence and stop for separate correctness planning.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially:

  ```powershell
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationMediaCacheTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`, reporting the known DesktopBridge import limitation separately from test failures. No tray or package build is required for test-only changes.

- [ ] **Step 4: Compare focused coverage and check formatting**

  Repeat Step 1 in a new GUID directory. Resolve the target source file ID in each report's module and compare covered/total sequence and branch points, source lines and per-method points for the constructor, `StoreAsync`, `PruneAsync`, `Clear`, `ResolveCachePath` and `PruneCore`; report baseline, post and unchanged relevant points. Focused OpenCover is not Sonar project-wide or new-code coverage; only a later server analysis establishes actual impact. Leave generated reports untracked.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.App.Tests\Notifications\NotificationMediaCacheTests.cs
  git diff --check
  ```

  Expected: tests and checks pass. Review the scoped diff; do not edit status or verification reports, add exclusions or production changes absent a separately considered correctness finding, or commit as part of this task.

---

### Task 22: Cover Shortcut Windows Key Round-Trip Mappings

**Files:**
- Modify: `tests/OpenHab.App.Tests/Shortcuts/ShortcutWindowsMapperTests.cs`.
- Inspect only: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutWindowsMapper.cs`, `src/OpenHab.App/Shortcuts/ShortcutBinding.cs`.

**Scope and decision:** Verified analysis `2026-10-30T21:34:11+0000` of pushed `3a3dcadd221641ace010e8d665a69498c77c3719` reports gate OK, overall coverage 86.5% (line 89.7%, branch 80.5%), 5,736 lines to cover, 593 uncovered lines and 576 uncovered conditions; new code is 93.50%, with 1,856 lines, 78 uncovered lines and 107 uncovered conditions. Task 21's `NotificationMediaCache.cs` is confirmed at 89.4% line / 96.2% branch. The gate is already green.

`ShortcutWindowsMapper.cs` has 17 uncovered lines / 38 uncovered conditions (83.5% line / 74.3% branch). Task 8 covered modifier flags, invalid bindings, representative key mappings and all named `FormatKey` arms separately, but most editing/navigation display names have not been mapped back through either binding API. That round trip is a real recorder-to-hotkey contract; it is bounded and deterministic. Do not assume all misses are reachable: `TryNormalize` removes undefined modifiers and blank keys, so the modifier switch default and private empty-key guard cannot be reached through these APIs; `Enum.TryParse` precedes the function-key fallback and most named-key switch arms. In contrast, `Backspace` maps via its alias switch. The private `None` check is reachable through a nonblank binding, and unsupported key rejection is already covered. Numeric enum strings and unusual function spellings need contract review, not coverage-driven assumptions. `NotificationMediaResolver.cs` is a viable later cohort (18 lines / 35 conditions), but needs response/cache setup; the mapper's display-to-binding contract is smaller. No coverage increase or gate outcome is promised.

- [ ] **Step 1: Capture a focused baseline**

  Before editing, collect existing tests into a fresh GUID directory; retain the report path and repeat with a new GUID after editing. Do not delete existing results. Tray mapper tests live in `OpenHab.App.Tests`.

  ```powershell
  $results = Join-Path $env:TEMP ("OpenHab-ShortcutRoundTripCoverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~ShortcutWindowsMapperTests --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory $results -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  Get-ChildItem $results -Recurse -Filter coverage.opencover.xml | Select-Object -ExpandProperty FullName
  ```

  Confirm `ShortcutWindowsMapper.cs` appears; use the same configuration, filter and settings for both runs.

- [ ] **Step 2: Add deterministic public mapping cases**

  Extend the existing class through its accessible mapper methods, without reflection or live hotkey registration:

  - For `VirtualKey.Space`, `Back`, `Delete`, `Insert`, `Up`, `Down`, `Left`, `Right`, `PageUp`, `PageDown`, `Home`, `End`, `Enter` and `Tab`, assert the exact existing `FormatKey` display string, then bind that string with Ctrl and assert `TryMapVirtualKey` returns the original key and `TryMap` returns the Ctrl mask and the corresponding native virtual-key value. Explicitly retain `Back` / `Backspace` as the alias case.
  - Apply the same round trip to boundaries `A`, `Z`, `Number0`, `Number9`, `F1` and `F24`; assert exact display text and values. Test trimmed mixed-case `" backspace "` and `" delete "` with a valid modifier, asserting both APIs agree on the key and modifier mask.
  - Assert that a binding with only an undefined `ShortcutModifier` and key `A` maps via `TryMapVirtualKey` to `A` but `TryMap` rejects it with zero outputs; with Ctrl plus that undefined modifier, `TryMap` succeeds with only the Ctrl mask. This exercises public normalization behavior without attempting to reach the mapper's impossible modifier default.
  - Keep existing invalid-key and no-modifier assertions. For an unsupported key with Ctrl, assert the false result and zero key only; do not assert the modifier output is zero after mapping has started. Do not manufacture tests for private defensive branches or assume every new case changes a coverage point.

  These are tests of existing behavior. No production changes or exclusions are in scope; if a case reveals a real correctness issue, preserve the failure and stop for separate correctness planning rather than changing behavior to raise coverage.

- [ ] **Step 3: Run focused and direct logic gates**

  Run sequentially; tests should pass existing behavior:

  ```powershell
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~ShortcutWindowsMapperTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
  ```

  If practical, also run `dotnet test OpenHab.Windows.sln -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`, reporting the known DesktopBridge import limitation separately from test failures. No tray or package build is required for test-only changes.

- [ ] **Step 4: Compare focused coverage and check formatting**

  Repeat Step 1 in a fresh directory. Resolve the target source file ID within each report's module; compare covered/total sequence and branch points, source lines and per-method points for `TryMap`, both `TryMapVirtualKey` overloads and `FormatKey`, including baseline, post and unchanged relevant points. If points do not improve, retain only tests establishing distinct meaningful contracts. Focused OpenCover is not Sonar project-wide or new-code coverage; only a later server analysis establishes actual impact. Leave generated reports untracked.

  ```powershell
  dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes --include tests\OpenHab.App.Tests\Shortcuts\ShortcutWindowsMapperTests.cs
  git diff --check -- tests\OpenHab.App.Tests\Shortcuts\ShortcutWindowsMapperTests.cs docs\superpowers\plans\2026-05-16-openhab-windows-coverage-quality.md
  ```

  Expected: tests and checks pass. Review the scoped diff; do not edit status or verification reports, add production changes or exclusions absent a separately considered correctness finding, or commit as part of planning.
