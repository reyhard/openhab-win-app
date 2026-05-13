# Settings Toolkit Expanders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the tray app's custom settings expander/card chrome with Windows Community Toolkit `SettingsExpander` and `SettingsCard` controls.

**Architecture:** Keep `SettingsPageControl` as the owner of settings page construction and preserve current event handlers, state references, and validation paths. Add the Toolkit SettingsControls package only to `OpenHab.Windows.Tray`, then update helper methods so existing settings rows are hosted inside Toolkit controls instead of local `Border`/`Expander` chrome.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK, `CommunityToolkit.WinUI.Controls.SettingsControls`, C# helper-generated UI.

---

### Task 1: Add SettingsControls Package

**Files:**
- Modify: `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`

- [ ] **Step 1: Add the NuGet package reference**

Add this package reference beside the existing tray project references:

```xml
<PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" Version="8.2.251219" />
```

- [ ] **Step 2: Restore/build the tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: NuGet restore succeeds and the tray project still compiles.

### Task 2: Replace Local Settings Chrome With Toolkit Controls

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Import Toolkit controls**

Add:

```csharp
using CommunityToolkit.WinUI.Controls;
```

- [ ] **Step 2: Convert grouped settings cards**

Change `CreateSettingsGroup(...)` so it returns a `StackPanel` of Toolkit `SettingsCard` rows. Each row should be hosted with `ContentAlignment = ContentAlignment.Left` and `HorizontalContentAlignment = HorizontalAlignment.Stretch` so existing custom row grids continue to stretch and preserve their own icon/title/control layout.

- [ ] **Step 3: Convert expander row containers**

Change `CreateExpanderRows(...)` so it returns a `List<SettingsCard>` instead of a custom bordered `StackPanel`. Each item wraps one existing row grid in a Toolkit `SettingsCard` with left content alignment.

- [ ] **Step 4: Convert top-level expanders**

Change `CreateSettingsExpander(...)` to return Toolkit `SettingsExpander`. Set `Header`, `Description`, `Content` for the right-side header action, and populate `Items` from the `SettingsCard` list.

- [ ] **Step 5: Preserve disabled Voice Mode**

Keep Voice Mode collapsed by default and preserve the existing `Opacity = 0.72` visual on its row content by applying opacity to each generated `SettingsCard` item.

### Task 3: Verify And Commit

**Files:**
- Verify: `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`
- Verify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Build Debug**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build succeeds with 0 errors.

- [ ] **Step 2: Build Release**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Commit**

Run:

```powershell
git add docs\superpowers\plans\2026-05-13-settings-toolkit-expanders.md src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj src\OpenHab.Windows.Tray\Settings\SettingsPageControl.xaml.cs
git commit -m "refactor: use toolkit settings expanders"
```
