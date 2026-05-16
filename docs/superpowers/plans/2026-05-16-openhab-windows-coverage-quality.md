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
