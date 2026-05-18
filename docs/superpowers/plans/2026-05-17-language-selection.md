# Language Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Polish localization plus a startup-only language override with a settings restart notice.

**Architecture:** Persist a small `AppLanguage` enum in `OpenHab.App.Settings`, apply it from `OpenHab.Windows.Tray` before UI/localizer construction, and keep WinUI resources as the source of localized text. The settings page exposes language selection and uses a deterministic helper to decide whether the restart notice should be visible.

**Tech Stack:** .NET 10, WinUI/Windows App SDK resource `.resw`, xUnit, existing `AppSettingsController` persistence.

---

### Task 1: Persist App Language

**Files:**
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Test: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`

- [ ] **Step 1: Write failing settings tests**

Add tests asserting:

```csharp
Assert.Equal(AppLanguage.System, controller.Current.AppLanguage);
controller.SetAppLanguage(AppLanguage.Polish);
await controller.FlushAsync();
Assert.Equal(AppLanguage.Polish, reloaded.Current.AppLanguage);
Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetAppLanguage((AppLanguage)999));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "AppLanguage|Language" -p:UseSharedCompilation=false`

Expected: compile failure because `AppLanguage` and `SetAppLanguage` do not exist.

- [ ] **Step 3: Implement settings model**

Add:

```csharp
public enum AppLanguage
{
    System,
    English,
    Polish
}
```

Add `AppLanguage AppLanguage = AppLanguage.System` to `AppSettings`, add `SetAppLanguage(AppLanguage language)` to `AppSettingsController`, and normalize undefined loaded enum values back to `AppSettings.Default.AppLanguage`.

- [ ] **Step 4: Run settings tests**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "AppLanguage|Language" -p:UseSharedCompilation=false`

Expected: language settings tests pass.

### Task 2: Apply Language at Startup

**Files:**
- Create: `src/OpenHab.Windows.Tray/Localization/AppLanguageRuntime.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Localization/AppLanguageRuntimeTests.cs`

- [ ] **Step 1: Write failing runtime helper tests**

Add tests for:

```csharp
Assert.Null(AppLanguageRuntime.ToLanguageTag(AppLanguage.System));
Assert.Equal("en-US", AppLanguageRuntime.ToLanguageTag(AppLanguage.English));
Assert.Equal("pl-PL", AppLanguageRuntime.ToLanguageTag(AppLanguage.Polish));
Assert.False(AppLanguageRuntime.ShouldShowRestartNotice(AppLanguage.System, AppLanguage.System));
Assert.True(AppLanguageRuntime.ShouldShowRestartNotice(AppLanguage.Polish, AppLanguage.System));
Assert.False(AppLanguageRuntime.ShouldShowRestartNotice(AppLanguage.Polish, AppLanguage.Polish));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter AppLanguageRuntimeTests -p:UseSharedCompilation=false`

Expected: compile failure because `AppLanguageRuntime` does not exist.

- [ ] **Step 3: Implement runtime helper and startup hook**

Create `AppLanguageRuntime` with `ToLanguageTag`, `ShouldShowRestartNotice`, and an `ApplyLanguage(AppLanguage language)` method that sets `Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` to the selected tag or clears it for system language. In `App.xaml.cs`, construct/load `AppSettingsController`, store the applied language, call `ApplyLanguage(settingsController.Current.AppLanguage)`, then construct shared localizers and UI services.

- [ ] **Step 4: Run runtime helper tests**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter AppLanguageRuntimeTests -p:UseSharedCompilation=false`

Expected: tests pass.

### Task 3: Add Polish Resources

**Files:**
- Create: `src/OpenHab.Windows.Tray/Strings/pl-PL/Resources.resw`
- Modify: `tests/OpenHab.App.Tests/Localization/LocalizationResourceTests.cs`

- [ ] **Step 1: Write failing parity expectation**

Ensure `LocalizationResourceTests` includes `pl-PL/Resources.resw` in the translated resource parity check.

- [ ] **Step 2: Run resource tests to verify they fail**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter LocalizationResourceTests -p:UseSharedCompilation=false`

Expected: fail because `pl-PL/Resources.resw` is missing.

- [ ] **Step 3: Add Polish resources**

Copy all keys from `en-US/Resources.resw` to `pl-PL/Resources.resw` and translate app chrome, settings, runtime status, command text, and fallback text into Polish while preserving placeholders such as `{0}`.

- [ ] **Step 4: Run resource tests**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter LocalizationResourceTests -p:UseSharedCompilation=false`

Expected: key parity passes.

### Task 4: Add Settings UI Selector

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Localization/AppLanguageRuntimeTests.cs`

- [ ] **Step 1: Extend helper tests for notice visibility**

Assert the restart notice is visible only when saved language differs from the process-applied language:

```csharp
Assert.True(AppLanguageRuntime.ShouldShowRestartNotice(AppLanguage.English, AppLanguage.Polish));
Assert.False(AppLanguageRuntime.ShouldShowRestartNotice(AppLanguage.System, AppLanguage.System));
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter AppLanguageRuntimeTests -p:UseSharedCompilation=false`

Expected: tests pass after Task 2, or fail if the helper needs adjustment.

- [ ] **Step 3: Add language selector**

In `SettingsPageControl`, add `AppLanguageOption`, `AppLanguageCombo`, and a restart `InfoBar` or text block in `BuildAppearanceSettingsPage`. Save changes through `settingsController.SetAppLanguage(option.Language)`. Bind restart notice visibility through `AppLanguageRuntime.ShouldShowRestartNotice(settingsController.Current.AppLanguage, appliedLanguage)`.

- [ ] **Step 4: Run app settings tests**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false`

Expected: all App tests pass.

### Task 5: Verify and Commit

**Files:**
- All changed files from Tasks 1-4.

- [ ] **Step 1: Run targeted verification**

Run: `dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false`

Expected: 0 failed.

- [ ] **Step 2: Run tray Release build**

Run: `dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false`

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run package build**

Run: `.\build-package.ps1 -Configuration Release -Platform x64`

Expected: package build succeeds with 0 errors.

- [ ] **Step 4: Commit**

Run:

```bash
git add src/OpenHab.App/Settings src/OpenHab.Windows.Tray tests/OpenHab.App.Tests docs/superpowers/plans/2026-05-17-language-selection.md
git commit -s -m "feat: add language selection"
```
