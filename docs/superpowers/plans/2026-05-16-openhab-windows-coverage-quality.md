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
