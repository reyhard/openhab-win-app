# Toolkit Settings Scaffolding Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the remaining custom settings page scaffolding with `CommunityToolkit.WinUI.Controls.SettingsControls` while preserving current settings behavior and navigation flow.

**Architecture:** Keep `SettingsPageControl` as the orchestration surface, but move static structure (page body, section containers, category navigation rows) from imperative C# builders into declarative Toolkit `SettingsCard`/`SettingsExpander` composition in XAML plus thin code-behind wiring. Preserve existing settings controller interactions, validation paths, and breadcrumb/page transition behavior during the migration by replacing scaffolding incrementally page-by-page.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK, `CommunityToolkit.WinUI.Controls.SettingsControls`, xUnit, C#.

---

### Task 1: Baseline Mapping And Safety Net

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- Modify: `tests/OpenHab.App.Tests/Settings/SettingsPageTransitionPlannerTests.cs` (only if transition coverage gaps are found during extraction)

- [ ] **Step 1: Capture current scaffolding entry points before edits**

Document in code comments (temporary migration notes near methods) the methods being retired or reduced:
- `CreateCategoryRow(...)`
- `CreateSettingsControlRow(...)`
- `CreateSettingsToggleRow(...)`
- `CreateSettingsCardForContent(...)`
- `BuildConnectionSettingsPage(...)`
- `BuildGeneralSettingsPage(...)`
- `BuildAppearanceSettingsPage(...)`
- `BuildDeviceInfoSyncSettingsPage(...)`
- `BuildShortcutsSettingsPage(...)`
- `BuildAboutSettingsPage(...)`

- [ ] **Step 2: Add/adjust transition-planner tests only if page flow logic changes**

If settings-page ordering or route registration changes, update/add test cases in:
`tests/OpenHab.App.Tests/Settings/SettingsPageTransitionPlannerTests.cs`

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter FullyQualifiedName~SettingsPageTransitionPlannerTests
```

Expected: transition tests pass and preserve root/subpage direction expectations.

### Task 2: Move Root Category Scaffolding To Toolkit XAML

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Define Toolkit-based root settings list in XAML**

Create a named root container in XAML using Toolkit `SettingsCard` instances for:
- Connection
- General
- Appearance
- Device Info Sync
- Shortcuts
- About

Each card keeps current icon/title/subtitle semantics and uses click handling routed to existing navigation methods.

- [ ] **Step 2: Replace `CreateCategoryRow(...)` usage with XAML-bound cards**

Remove dynamic creation of root rows from `NavigateToSettingsPage(SettingsPageKind.Root)` and switch to:
- toggling visibility/content hosts
- binding/updating header/subtitle text only
- attaching click delegates to named card controls

- [ ] **Step 3: Verify root page still navigates identically**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: tray project builds and root settings categories still navigate to the same destinations.

### Task 3: Convert Section Containers To Toolkit-First Layout

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Introduce reusable XAML section hosts for subpages**

Add XAML hosts for page-level sections so code-behind no longer creates outer chrome elements imperatively (Toolkit cards/expanders become the structural container; code-behind only assigns values and hooks events).

- [ ] **Step 2: Keep row-level controls imperative for first pass**

Do not migrate control value wiring yet. Keep existing `ComboBox`, `TextBox`, `PasswordBox`, `ToggleSwitch`, and `NumberBox` creation/wiring methods intact, but insert these controls into Toolkit-hosted containers.

- [ ] **Step 3: Retire redundant custom border/grid scaffolding helpers**

Remove or reduce helper methods that only provided pre-Toolkit visual structure and are no longer needed after XAML section hosts are in place.

### Task 4: Page-By-Page Subpage Migration (Conservative Sequence)

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Connection page**

Move page structure to Toolkit containers, keep existing endpoint/token behavior and save/refresh logic unchanged.

- [ ] **Step 2: General and Appearance pages**

Migrate these pages next because they have simpler control interactions; preserve launch-at-startup, flyout width, poll interval, theme, skin, and icon toggle behavior.

- [ ] **Step 3: Device Info Sync page**

Migrate after simpler pages; preserve all mapping textbox references and enable/disable UI states.

- [ ] **Step 4: Shortcuts page**

Migrate last due to highest interaction complexity (recorders, editor mode, action list/state); preserve existing validation and conflict messaging behavior.

- [ ] **Step 5: About page**

Finish with Toolkit card structure for logs/version display and keep existing button handlers unchanged.

### Task 5: Cleanup, Verification, And Handoff

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- Verify: `tests/OpenHab.App.Tests/Settings/SettingsPageTransitionPlannerTests.cs`
- Verify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`

- [ ] **Step 1: Remove dead scaffolding code paths**

Delete obsolete helper methods and fields only after all pages render through Toolkit containers and compile cleanly.

- [ ] **Step 2: Run targeted settings/runtime safety tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~SettingsPageTransitionPlannerTests|FullyQualifiedName~AppSettingsControllerTests"
```

Expected: tests pass with no behavior regressions in settings persistence/normalization and page transitions.

- [ ] **Step 3: Run tray build gates**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Expected: both configurations succeed.

- [ ] **Step 4: Run full solution tests when environment supports DesktopBridge**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: all test projects run; if DesktopBridge import issue appears, record as known environment limitation per existing quality-gate docs.

