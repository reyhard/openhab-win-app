# Portable Coverage Exclusions and Tray Extraction Plan

## Goal

Raise the meaningful coverage signal without pretending Windows shell glue is unit-testable. The implementation should:

- extract testable decisions out of WinUI/tray classes into UI-independent projects,
- keep Windows-specific but testable decisions in small internal tray helpers when moving them to a neutral layer would change dependencies,
- mark only remaining OS and framework glue with `[ExcludeFromCodeCoverage]`,
- make the exclusion policy portable through Coverlet runsettings,
- keep SonarQube aligned with the same exclusion inventory,
- preserve the current package-build crash fix and main-branch behavior.

This plan is written for execution from a separate git worktree and is split for subagent-driven development.

## Constraints

- Keep domain/runtime logic out of `OpenHab.Windows.Tray`.
- Do not annotate large mixed UI files with `[ExcludeFromCodeCoverage]`.
- Do not use PowerShell reflection against assemblies.
- Do not remove `sonar.coverage.exclusions`. Coverlet exclusions control generated coverage reports; Sonar still uses its own source scope and needs `sonar.coverage.exclusions` to remove files from Sonar coverage analysis.
- Preserve the package isolation fix in `src/OpenHab.Windows.Package/OpenHab.Windows.Package.wapproj`.

## Execution Preflight

Run this before implementing the plan:

```powershell
git status --short
git worktree add .worktrees\portable-coverage-exclusions -b feature/portable-coverage-exclusions main
Set-Location .worktrees\portable-coverage-exclusions
Copy-Item ..\..\docs\superpowers\plans\2026-05-17-portable-coverage-exclusions.md docs\superpowers\plans\2026-05-17-portable-coverage-exclusions.md
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
dotnet test OpenHab.Windows.sln
```

If `dotnet test OpenHab.Windows.sln` is blocked by package project/DesktopBridge resolution, run the direct test projects and record the reason in the final verification:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --configuration Release
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --configuration Release
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --configuration Release
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --configuration Release
```

## Work Slices

Use three subagents after the worktree is created:

- Agent A owns portable coverage configuration and documentation.
- Agent B owns coverage attributes on true glue files.
- Agent C owns pure logic extractions and tests.

No two agents should edit the same file. Agent A creates the initial coverage config and inventory, Agent B annotates true glue, and Agent C extracts testable logic. The coordinator owns final inventory reconciliation after Agents B and C complete, because those tasks can prove that a file should be removed from the exclusion list.

## Task 1: Add Portable Coverlet Configuration

Owner: Agent A

Create `coverage.runsettings` at the repository root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>opencover</Format>
          <ExcludeByAttribute>
            Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute
          </ExcludeByAttribute>
          <ExcludeByFile>
            **/src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs,
            **/src/OpenHab.Windows.Tray/DwmWindowDecorations.cs,
            **/src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs,
            **/src/OpenHab.Windows.Tray/Rendering/CompositionAnimationHelper.cs,
            **/src/OpenHab.Windows.Tray/Rendering/SitemapComboHelper.cs,
            **/src/OpenHab.Windows.Tray/Rendering/SitemapPageTransitionAnimator.cs,
            **/src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs,
            **/src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs,
            **/src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs,
            **/src/OpenHab.Windows.Tray/Shortcuts/ShortcutInteractiveCommandWindow.cs,
            **/src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs,
            **/src/OpenHab.Windows.Tray/Tray/TrayIconService.cs,
            **/src/OpenHab.Windows.Tray/Startup/StartupManager.cs,
            **/src/OpenHab.Windows.Tray/DeviceInfo/WindowsBatteryInfoReader.cs,
            **/src/OpenHab.Windows.Tray/DeviceInfo/WindowsBluetoothInfoReader.cs,
            **/src/OpenHab.Windows.Tray/DeviceInfo/WindowsNetworkInfoReader.cs,
            **/src/OpenHab.Windows.Tray/DeviceInfo/WindowsSessionInfoReader.cs,
            **/src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs,
            **/src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs,
            **/src/OpenHab.Windows.Notifications/ToastService.cs,
            **/src/OpenHab.Core/Auth/WindowsCredentialStore.cs
          </ExcludeByFile>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

Update `.github/workflows/sonarcloud.yml` so every `dotnet test` invocation that collects coverage uses the shared runsettings:

```powershell
dotnet test $testProject --configuration Release --no-restore --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
```

Remove command-line coverage collector parameters such as:

```text
-- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
```

Keep `sonar.cs.opencover.reportsPaths` unchanged.

Keep `sonar.coverage.exclusions` permanently for Sonar's denominator. Do not mechanically copy the Coverlet `**/...` globs into Sonar. Use the same inventory but render it in the syntax each tool expects:

- Coverlet runsettings: source-file globs such as `**/src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`.
- Sonar: repository-relative path patterns such as `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`.

Add a comment in the workflow explaining that `coverage.runsettings` controls generated OpenCover reports while `sonar.coverage.exclusions` controls Sonar's coverage analysis scope.

## Task 2: Document the Exclusion Inventory

Owner: Agent A

Create `docs/superpowers/verification/coverage-exclusion-inventory.md`:

```markdown
# Coverage Exclusion Inventory

This file is the reviewable source for files excluded from line coverage. It is not copied verbatim into tool configuration; Coverlet and Sonar use different path pattern syntax.

## Policy

Use tests first. Extract pure decisions into `OpenHab.App`, `OpenHab.Rendering`, `OpenHab.Sitemaps`, or `OpenHab.Core` before excluding code.

Use `[ExcludeFromCodeCoverage]` only for files that are thin wrappers around:

- WinUI or Windows App SDK activation,
- Win32 shell and tray APIs,
- global hotkey message windows,
- Windows registry startup integration,
- Windows credential APIs,
- toast notification COM/AppUserModelID integration,
- device state readers that depend on live OS state.

Do not apply `[ExcludeFromCodeCoverage]` to files that mix OS glue with parseable planning, mapping, formatting, validation, state transitions, or command selection. Extract those decisions first.

## Tool Mapping

- Coverlet: list files in `coverage.runsettings` under `ExcludeByFile` using `**/src/...` globs.
- Sonar: list files in `.github/workflows/sonarcloud.yml` under `sonar.coverage.exclusions` using repo-relative `src/...` patterns.
- Attribute-based tools: use `[ExcludeFromCodeCoverage]` on every excluded top-level type that remains in source.

## Excluded Files

| File | Reason | Verification |
| --- | --- | --- |
| `src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs` | Win32/DWM composition wrapper | Release build |
| `src/OpenHab.Windows.Tray/DwmWindowDecorations.cs` | Win32/DWM composition wrapper | Release build |
| `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs` | WebView2 host glue after URL/auth policy extraction | Release build and smoke test |
| `src/OpenHab.Windows.Tray/Rendering/CompositionAnimationHelper.cs` | WinUI composition animation wrapper | Release build |
| `src/OpenHab.Windows.Tray/Rendering/SitemapComboHelper.cs` | WinUI ComboBox display glue | Release build |
| `src/OpenHab.Windows.Tray/Rendering/SitemapPageTransitionAnimator.cs` | WinUI animation wrapper | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs` | Win32 global hotkey registration | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs` | Win32 message-only window | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs` | WinUI popup host after command planning extraction | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/ShortcutInteractiveCommandWindow.cs` | WinUI command popup host after command planning extraction | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs` | WinUI keyboard capture glue after key mapping extraction | `ShortcutWindowsMapperTests` and Release build |
| `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs` | `NotifyIcon` shell integration | Release build |
| `src/OpenHab.Windows.Tray/Startup/StartupManager.cs` | Windows startup registry integration | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsBatteryInfoReader.cs` | Live OS battery reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsBluetoothInfoReader.cs` | Live OS Bluetooth reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsNetworkInfoReader.cs` | Live OS network reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsSessionInfoReader.cs` | Live OS session reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs` | Composition of live OS readers | Release build |
| `src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs` | Windows notification registration | Notification tests and Release build |
| `src/OpenHab.Windows.Notifications/ToastService.cs` | Windows toast API integration | Notification tests and Release build |
| `src/OpenHab.Core/Auth/WindowsCredentialStore.cs` | Windows Credential Manager wrapper | Core auth tests for non-Windows abstractions and Release build |
```

If Task 3 or Task 4 proves a listed file still contains testable logic, remove that file from the inventory and extract the logic instead.

## Task 3: Annotate True Glue With `[ExcludeFromCodeCoverage]`

Owner: Agent B

For each file that remains in `coverage-exclusion-inventory.md`, add:

```csharp
using System.Diagnostics.CodeAnalysis;
```

and annotate every excluded top-level type in the file:

```csharp
[ExcludeFromCodeCoverage(Justification = "Thin Windows integration wrapper; behavior is verified by build and smoke tests.")]
```

Use a narrower justification when appropriate:

```csharp
[ExcludeFromCodeCoverage(Justification = "Win32 message-only window for global hotkey callbacks.")]
```

```csharp
[ExcludeFromCodeCoverage(Justification = "Windows Credential Manager wrapper around OS APIs.")]
```

```csharp
[ExcludeFromCodeCoverage(Justification = "WinUI composition animation wrapper.")]
```

Do not annotate:

- `src/OpenHab.Windows.Tray/App.xaml.cs`
- `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- `src/OpenHab.Windows.Tray/Rendering/OpenHabIconImageSourceLoader.cs`

Those files either contain orchestration or still have extractable decisions.

Before applying attributes, inspect each file for multiple top-level types:

```powershell
rg -n "class |record |struct |interface |enum " src\OpenHab.Windows.Tray\Shortcuts\GlobalHotkeyService.cs src\OpenHab.Windows.Tray\Tray\TrayIconService.cs src\OpenHab.Windows.Tray\DeviceInfo src\OpenHab.Windows.Notifications src\OpenHab.Core\Auth\WindowsCredentialStore.cs
```

If a non-glue record or helper is useful outside the OS wrapper, move it into a covered file instead of annotating it.

Verification for this task:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
dotnet build src\OpenHab.Windows.Notifications\OpenHab.Windows.Notifications.csproj --configuration Release
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --configuration Release
```

## Task 4: Extract Icon SVG Policy From the Tray Project

Owner: Agent C

Create `src/OpenHab.Windows.Tray/Rendering/OpenHabIconSvgPolicy.cs`.

Keep this helper in the tray project because the existing behavior accepts named `Microsoft.UI.Colors` values and `Windows.UI.Color`. Moving this to `OpenHab.Rendering` would either add Windows UI dependencies to a neutral project or silently drop supported inputs.

```csharp
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenHab.Windows.Tray.Rendering;

internal static partial class OpenHabIconSvgPolicy
{
    internal static bool LooksLikeSvg(string? mediaType, byte[] bytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sampleLength = Math.Min(bytes.Length, 256);
        if (sampleLength == 0)
        {
            return false;
        }

        var sample = Encoding.UTF8.GetString(bytes, 0, sampleLength).TrimStart('\uFEFF', '\t', '\r', '\n', ' ');
        return sample.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    internal static byte[]? TryApplySvgColorTint(byte[] svgBytes, string? iconColor)
    {
        if (string.IsNullOrWhiteSpace(iconColor))
        {
            return null;
        }

        if (!TryNormalizeColorToHex(iconColor, out var hexColor))
        {
            return null;
        }

        try
        {
            var svgText = Encoding.UTF8.GetString(svgBytes);
            if (string.IsNullOrWhiteSpace(svgText) || svgText.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var match = SvgOpenTagRegex().Match(svgText);
            if (!match.Success)
            {
                return null;
            }

            var replacement = $"<svg style=\"color:{hexColor};\"";
            var tinted = svgText[..match.Index] + replacement + svgText[(match.Index + match.Length)..];
            return Encoding.UTF8.GetBytes(tinted);
        }
        catch
        {
            return null;
        }
    }

    internal static bool TryNormalizeColorToHex(string? color, out string hex)
    {
        hex = string.Empty;
        if (!TryParseColor(color, out var parsed))
        {
            return false;
        }

        hex = $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}";
        return true;
    }

    private static bool TryParseColor(string? color, out global::Windows.UI.Color parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        var input = color.Trim();
        if (input.StartsWith('#'))
        {
            var hex = input[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(c => $"{c}{c}"));
            }

            if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            {
                parsed = CreateColor(
                    255,
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
                return true;
            }

            if (hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
            {
                parsed = CreateColor(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));
                return true;
            }
        }

        var property = typeof(Microsoft.UI.Colors).GetProperty(input, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (property?.PropertyType == typeof(global::Windows.UI.Color))
        {
            parsed = (global::Windows.UI.Color)property.GetValue(null)!;
            return true;
        }

        return false;
    }

    private static global::Windows.UI.Color CreateColor(byte a, byte r, byte g, byte b)
    {
        return global::Windows.UI.Color.FromArgb(a, r, g, b);
    }

    [GeneratedRegex("<svg\\b", RegexOptions.IgnoreCase, 100)]
    private static partial Regex SvgOpenTagRegex();
}
```

Modify `src/OpenHab.Windows.Tray/Rendering/OpenHabIconImageSourceLoader.cs` to delegate SVG detection and tinting to `OpenHabIconSvgPolicy`.

Expected integration pattern:

```csharp
if (OpenHabIconSvgPolicy.LooksLikeSvg(mediaType, bytes))
{
    var tintedBytes = OpenHabIconSvgPolicy.TryApplySvgColorTint(bytes, iconColor) ?? bytes;
    var svg = await CreateSvgFromBytesAsync(tintedBytes, rasterizePixelWidth, rasterizePixelHeight);
    if (svg is not null)
    {
        return svg;
    }
}
```

Remove these private members from `OpenHabIconImageSourceLoader` after delegation:

- `SvgOpenTagRegex`
- `TryApplySvgColorTint`
- `TryNormalizeColorToHex`
- `TryParseColor`
- `LooksLikeSvg`
- `CreateColor`

Add `tests/OpenHab.App.Tests/Rendering/OpenHabIconSvgPolicyTests.cs`:

```csharp
using System.Text;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.Rendering;

public sealed class OpenHabIconSvgPolicyTests
{
    [Theory]
    [InlineData("#123456", "#123456")]
    [InlineData("123456", "#123456")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("abc", "#AABBCC")]
    [InlineData("#80123456", "#123456")]
    [InlineData("Red", "#FF0000")]
    public void TryNormalizeColorToHex_NormalizesSupportedValues(string input, string expected)
    {
        var result = OpenHabIconSvgPolicy.TryNormalizeColorToHex(input, out var normalized);

        Assert.True(result);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12")]
    [InlineData("#12345g")]
    public void TryNormalizeColorToHex_RejectsUnsupportedValues(string input)
    {
        var result = OpenHabIconSvgPolicy.TryNormalizeColorToHex(input, out var normalized);

        Assert.False(result);
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void LooksLikeSvg_ReturnsTrueForSvgMediaType()
    {
        var result = OpenHabIconSvgPolicy.LooksLikeSvg("image/svg+xml", Encoding.UTF8.GetBytes("not svg"));

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeSvg_ReturnsTrueForSvgPayload()
    {
        var result = OpenHabIconSvgPolicy.LooksLikeSvg(null, Encoding.UTF8.GetBytes("  <svg viewBox=\"0 0 1 1\" />"));

        Assert.True(result);
    }

    [Fact]
    public void TryApplySvgColorTint_InsertsSvgColorStyle()
    {
        var svg = Encoding.UTF8.GetBytes("<svg viewBox=\"0 0 1 1\"><path fill=\"currentColor\" /></svg>");

        var tinted = OpenHabIconSvgPolicy.TryApplySvgColorTint(svg, "#0a1b2c");

        Assert.Equal(
            "<svg style=\"color:#0A1B2C;\" viewBox=\"0 0 1 1\"><path fill=\"currentColor\" /></svg>",
            Encoding.UTF8.GetString(tinted!));
    }

    [Fact]
    public void TryApplySvgColorTint_ReturnsNullForUnsupportedColor()
    {
        var svg = Encoding.UTF8.GetBytes("<svg><path fill=\"currentColor\" /></svg>");

        var tinted = OpenHabIconSvgPolicy.TryApplySvgColorTint(svg, "not-a-color");

        Assert.Null(tinted);
    }
}
```

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --configuration Release
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

## Task 5: Extract Search-Key Decision Logic Without Changing Back Navigation

Owner: Agent C

Create `src/OpenHab.App/Runtime/SitemapSearchKeyboardPlanner.cs`:

```csharp
namespace OpenHab.App.Runtime;

public enum SitemapKeyboardInput
{
    Escape,
    GoBack,
    Other
}

public enum SitemapSearchKeyboardAction
{
    None,
    CloseSearch
}

public readonly record struct SitemapSearchKeyboardState(
    bool HasVisibleSearchChrome,
    bool IsRefreshing);

public static class SitemapSearchKeyboardPlanner
{
    public static SitemapSearchKeyboardAction Plan(
        SitemapKeyboardInput input,
        SitemapSearchKeyboardState state)
    {
        if (input == SitemapKeyboardInput.Escape && state.HasVisibleSearchChrome)
        {
            return SitemapSearchKeyboardAction.CloseSearch;
        }

        if (input == SitemapKeyboardInput.GoBack && state.HasVisibleSearchChrome && !state.IsRefreshing)
        {
            return SitemapSearchKeyboardAction.CloseSearch;
        }

        return SitemapSearchKeyboardAction.None;
    }
}
```

Update `MainWindow.xaml.cs` and `FlyoutWindow.xaml.cs` keyboard handlers to use this planner only for the existing "close search chrome" decision. Do not move contextual back navigation into this planner in this pass.

Expected integration pattern:

```csharp
var searchAction = SitemapSearchKeyboardPlanner.Plan(
    e.Key switch
    {
        VirtualKey.Escape => SitemapKeyboardInput.Escape,
        VirtualKey.GoBack => SitemapKeyboardInput.GoBack,
        _ => SitemapKeyboardInput.Other
    },
    new SitemapSearchKeyboardState(HasVisibleSearchChrome, isRefreshing));

if (searchAction == SitemapSearchKeyboardAction.CloseSearch)
{
    CloseSearchChrome();
    e.Handled = true;
    return;
}
```

After this block, keep the current back-navigation code unchanged:

- `MainWindow`: non-`GoBack` keys return; `GoBack` calls `TryHandleContextualBackNavigation()`.
- `FlyoutWindow`: `GoBack` uses `CanStartSitemapBackTransition()` and `NavigateBackWithAnimationAsync()`.

This explicitly preserves the existing contextual settings/sitemap/Main UI ordering in `MainWindow`.

Add `tests/OpenHab.App.Tests/Runtime/SitemapSearchKeyboardPlannerTests.cs`:

```csharp
using OpenHab.App.Runtime;

namespace OpenHab.App.Tests.Runtime;

public sealed class SitemapSearchKeyboardPlannerTests
{
    [Fact]
    public void Plan_EscapeClosesVisibleSearch()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.Escape,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: true,
                IsRefreshing: false));

        Assert.Equal(SitemapSearchKeyboardAction.CloseSearch, action);
    }

    [Fact]
    public void Plan_EscapeDoesNothingWhenSearchIsClosed()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.Escape,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: false,
                IsRefreshing: false));

        Assert.Equal(SitemapSearchKeyboardAction.None, action);
    }

    [Fact]
    public void Plan_GoBackClosesVisibleSearchWhenNotRefreshing()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.GoBack,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: true,
                IsRefreshing: false));

        Assert.Equal(SitemapSearchKeyboardAction.CloseSearch, action);
    }

    [Fact]
    public void Plan_GoBackDoesNotCloseSearchWhileRefreshing()
    {
        var action = SitemapSearchKeyboardPlanner.Plan(
            SitemapKeyboardInput.GoBack,
            new SitemapSearchKeyboardState(
                HasVisibleSearchChrome: true,
                IsRefreshing: true));

        Assert.Equal(SitemapSearchKeyboardAction.None, action);
    }
}
```

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --configuration Release
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

## Task 6: Reconcile Sonar and Portable Exclusions

Owner: Coordinator

After Tasks 1 through 5 are merged, run local coverage:

```powershell
Remove-Item -Recurse -Force TestResults -ErrorAction SilentlyContinue
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --configuration Release --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --configuration Release --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --configuration Release --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --configuration Release --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
```

Inspect the generated OpenCover XML files and confirm excluded files are absent from the reports. Use text search only:

```powershell
rg -n "AcrylicBlurHelper|TrayIconService|WindowsCredentialStore|ToastService|OpenHabIconImageSourceLoader|OpenHabIconSvgPolicy|SitemapSearchKeyboardPlanner" TestResults
```

Expected result:

- true glue files are absent,
- `OpenHabIconImageSourceLoader` may remain present unless all logic moved out,
- `OpenHabIconSvgPolicy` is present and covered,
- `SitemapSearchKeyboardPlanner` is present and covered.

If excluded files still appear in OpenCover XML, fix `coverage.runsettings` glob patterns before changing Sonar configuration.

After local OpenCover proves the portable exclusions work:

- keep `sonar.coverage.exclusions` in the Sonar workflow using repo-relative path patterns,
- keep `coverage.runsettings` in the test commands using Coverlet source-file globs,
- update `coverage-exclusion-inventory.md` if extracted helper files should remain covered or if any type was moved out of an excluded file.

## Task 7: Final Verification

Owner: Coordinator

Run:

```powershell
dotnet test OpenHab.Windows.sln
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
.\build-package.ps1 -Configuration Release -Platform x64
```

If full solution tests are blocked by DesktopBridge or package restore behavior, run all direct test projects listed in the preflight and report the blocker exactly.

If `build-package.ps1` fails because Visual Studio DesktopBridge targets are unavailable, report that as an environment blocker. Do not replace package build verification with standalone `dotnet build` for the package project.

## Commit Plan

Create one commit after verification:

```powershell
git status --short
git add coverage.runsettings
git add .github\workflows\sonarcloud.yml
git add docs\superpowers\verification\coverage-exclusion-inventory.md
git add docs\superpowers\plans\2026-05-17-portable-coverage-exclusions.md
git add src\OpenHab.Windows.Tray\Rendering\OpenHabIconSvgPolicy.cs
git add src\OpenHab.Windows.Tray\Rendering\OpenHabIconImageSourceLoader.cs
git add src\OpenHab.App\Runtime\SitemapSearchKeyboardPlanner.cs
git add src\OpenHab.Windows.Tray\MainWindow.xaml.cs
git add src\OpenHab.Windows.Tray\FlyoutWindow.xaml.cs
git add tests\OpenHab.App.Tests\Rendering\OpenHabIconSvgPolicyTests.cs
git add tests\OpenHab.App.Tests\Runtime\SitemapSearchKeyboardPlannerTests.cs
git status --short
git commit -m "Make coverage exclusions portable"
```

If Agent B annotated additional glue files, stage those explicit files from `git status --short`. Do not stage broad `src` or `tests` directories without reviewing every path.

## Completion Criteria

- `coverage.runsettings` exists and is used by CI coverage collection.
- `[ExcludeFromCodeCoverage]` is applied only to true glue files listed in the inventory.
- At least two meaningful logic extractions have new unit tests.
- Local coverage XML excludes true glue files and includes the newly extracted policy/planner classes.
- Tray Release build passes.
- Package build is run or a precise environment blocker is reported.
