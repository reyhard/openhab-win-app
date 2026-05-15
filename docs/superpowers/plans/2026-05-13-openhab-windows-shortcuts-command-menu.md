# openHAB Windows Shortcuts Command Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the full shortcuts settings, radial command menu, action execution, and Windows global hotkey system described in `docs/superpowers/specs/2026-05-13-openhab-windows-shortcuts-command-menu-design.md`.

**Architecture:** Keep shortcut contracts, validation, settings persistence, and action execution in `OpenHab.App`; add only required item/state API contracts to `OpenHab.Core`; keep WinUI controls, radial menu rendering, and Win32 hotkey registration in `OpenHab.Windows.Tray`. Implement in staged slices so each layer has tests before UI wiring depends on it.

**Tech Stack:** .NET 10, C#, WinUI 3 / Windows App SDK, xUnit, existing `AppSettingsController`, existing `IOpenHabClient`, Win32 `RegisterHotKey`/`UnregisterHotKey` interop in the tray project.

---

## Scope Check

The approved spec covers settings, validation, item lookup, action execution, radial UI, and Windows hotkeys. These are not independent products because they share `AppSettings.Shortcuts`, shortcut conflict detection, connection-state gating, and action execution contracts. Keep this as one implementation plan, but land it milestone-by-milestone with commits after each task.

## File Structure

Create these app-layer files:

- `src/OpenHab.App/Shortcuts/ShortcutBinding.cs`  
  Owns `ShortcutModifier`, `ShortcutBinding`, `RadialActivationMode`, and formatting helpers.
- `src/OpenHab.App/Shortcuts/ShortcutAction.cs`  
  Owns `ShortcutAction`, `ShortcutCommandType`, and stable action model.
- `src/OpenHab.App/Shortcuts/ShortcutSettings.cs`  
  Owns `ShortcutSettings`, `BuiltInShortcutSettings`, defaults, and normalization.
- `src/OpenHab.App/Shortcuts/ShortcutValidation.cs`  
  Owns validation, duplicate detection, reserved shortcut handling, and action validation.
- `src/OpenHab.App/Shortcuts/ShortcutActionExecutor.cs`  
  Owns command mapping, disconnected gate, current-state toggle logic, and execution results.
- `src/OpenHab.App/Shortcuts/ShortcutIconCatalog.cs`  
  Owns stable icon IDs and display groups.

Modify these app/core files:

- `src/OpenHab.App/Settings/AppSettings.cs`  
  Add `ShortcutSettings Shortcuts`.
- `src/OpenHab.App/Settings/AppSettingsController.cs`  
  Normalize shortcut settings and expose `SetShortcutSettings`.
- `src/OpenHab.Core/Api/IOpenHabClient.cs`  
  Add item listing/state lookup contracts.
- `src/OpenHab.Core/Api/OpenHabHttpClient.cs`  
  Implement item listing/state lookup through openHAB REST.

Create these Windows-layer files:

- `src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs`  
  Reusable recorder UI built in code to match existing settings style.
- `src/OpenHab.Windows.Tray/Shortcuts/ShortcutSettingsControls.cs`  
  Factory/helper methods for cards, chips, tables, editor rows, and icon picker UI.
- `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs`  
  Borderless radial command menu window.
- `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs`  
  Win32 global hotkey registration, refresh, and event dispatch.
- `src/OpenHab.Windows.Tray/Shortcuts/ShortcutWindowsMapper.cs`  
  Maps app-layer shortcut bindings to Win32 modifier/key values and glyphs.

Modify these Windows-layer files:

- `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`  
  Add `Shortcuts` page, built-in cards, table, editor, and settings bindings.
- `src/OpenHab.Windows.Tray/App.xaml.cs`  
  Construct the action executor and hotkey service, wire command menu requests and direct action execution, dispose service on shutdown.
- `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`  
  No new packages expected. Add files only through SDK implicit include.

Modify or create these tests:

- `tests/OpenHab.App.Tests/Shortcuts/ShortcutBindingTests.cs`
- `tests/OpenHab.App.Tests/Shortcuts/ShortcutValidationTests.cs`
- `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionExecutorTests.cs`
- `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
- `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`
- `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`

## Task 1: Shortcut Models, Defaults, Formatting, And Settings Persistence

**Files:**
- Create: `src/OpenHab.App/Shortcuts/ShortcutBinding.cs`
- Create: `src/OpenHab.App/Shortcuts/ShortcutAction.cs`
- Create: `src/OpenHab.App/Shortcuts/ShortcutSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Test: `tests/OpenHab.App.Tests/Shortcuts/ShortcutBindingTests.cs`
- Test: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`

- [ ] **Step 1: Write failing formatting and default tests**

Create `tests/OpenHab.App.Tests/Shortcuts/ShortcutBindingTests.cs`:

```csharp
using OpenHab.App.Shortcuts;
using System.Collections.Immutable;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutBindingTests
{
    [Fact]
    public void FormatOrdersModifiersConsistently()
    {
        var binding = new ShortcutBinding(
            [ShortcutModifier.Shift, ShortcutModifier.Win, ShortcutModifier.Alt],
            "o");

        Assert.Equal("Win + Alt + Shift + O", ShortcutBindingFormatter.Format(binding));
    }

    [Fact]
    public void NormalizeUppercasesKeyAndOrdersModifiers()
    {
        var binding = new ShortcutBinding(
            [ShortcutModifier.Shift, ShortcutModifier.Win, ShortcutModifier.Win],
            "o");

        var normalized = ShortcutBindingFormatter.Normalize(binding);

        Assert.Equal([ShortcutModifier.Win, ShortcutModifier.Shift], normalized.Modifiers);
        Assert.Equal("O", normalized.Key);
    }

    [Fact]
    public void FormatNullReturnsUnassigned()
    {
        Assert.Equal("Unassigned", ShortcutBindingFormatter.Format(null));
    }
}
```

Append these tests to `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`:

```csharp
[Fact]
public void DefaultsIncludeShortcutSettings()
{
    var controller = CreateController();

    Assert.True(controller.Current.Shortcuts.CommandMenu.Enabled);
    Assert.Equal("Win + O", ShortcutBindingFormatter.Format(controller.Current.Shortcuts.CommandMenu.Binding));
    Assert.Equal(RadialActivationMode.Toggle, controller.Current.Shortcuts.CommandMenu.RadialActivationMode);
    Assert.False(controller.Current.Shortcuts.VoiceMode.Enabled);
    Assert.Null(controller.Current.Shortcuts.VoiceMode.Binding);
    Assert.Empty(controller.Current.Shortcuts.Actions);
}

[Fact]
public async Task CanPersistShortcutSettings()
{
    var controller = CreateController();
    var settings = ShortcutSettings.Default with
    {
        CommandMenu = ShortcutSettings.Default.CommandMenu with
        {
            Binding = new ShortcutBinding([ShortcutModifier.Ctrl, ShortcutModifier.Alt], "K"),
            RadialActivationMode = RadialActivationMode.Hold
        }
    };

    controller.SetShortcutSettings(settings);
    await controller.FlushAsync();

    var reloaded = CreateController();
    Assert.Equal("Ctrl + Alt + K", ShortcutBindingFormatter.Format(reloaded.Current.Shortcuts.CommandMenu.Binding));
    Assert.Equal(RadialActivationMode.Hold, reloaded.Current.Shortcuts.CommandMenu.RadialActivationMode);
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~ShortcutBindingTests|FullyQualifiedName~DefaultsIncludeShortcutSettings|FullyQualifiedName~CanPersistShortcutSettings"
```

Expected: compile fails because `OpenHab.App.Shortcuts` types and `AppSettings.Shortcuts` do not exist.

- [ ] **Step 3: Implement shortcut model files**

Create `src/OpenHab.App/Shortcuts/ShortcutBinding.cs`:

```csharp
using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public enum ShortcutModifier
{
    Win,
    Ctrl,
    Alt,
    Shift
}

public enum RadialActivationMode
{
    Toggle,
    Hold
}

public sealed record ShortcutBinding(
    ImmutableArray<ShortcutModifier> Modifiers,
    string Key);

public static class ShortcutBindingFormatter
{
    private static readonly ShortcutModifier[] ModifierOrder =
    [
        ShortcutModifier.Win,
        ShortcutModifier.Ctrl,
        ShortcutModifier.Alt,
        ShortcutModifier.Shift
    ];

    public static ShortcutBinding Normalize(ShortcutBinding binding)
    {
        var modifiers = ModifierOrder
            .Where(modifier => binding.Modifiers.Contains(modifier))
            .ToImmutableArray();
        return new ShortcutBinding(modifiers, NormalizeKey(binding.Key));
    }

    public static string Format(ShortcutBinding? binding)
    {
        if (binding is null)
        {
            return "Unassigned";
        }

        var normalized = Normalize(binding);
        var parts = normalized.Modifiers.Select(static modifier => modifier.ToString()).Append(normalized.Key);
        return string.Join(" + ", parts);
    }

    private static string NormalizeKey(string key)
    {
        var trimmed = (key ?? string.Empty).Trim();
        return trimmed.Length == 1 ? trimmed.ToUpperInvariant() : trimmed;
    }
}
```

Create `src/OpenHab.App/Shortcuts/ShortcutAction.cs`:

```csharp
namespace OpenHab.App.Shortcuts;

public enum ShortcutCommandType
{
    Toggle,
    OnOff,
    OpenClose,
    OpenSlider,
    OpenColorPicker,
    SendCommand
}

public sealed record ShortcutAction(
    string Id,
    string Name,
    string IconId,
    bool ShowInCommandMenu,
    ShortcutBinding? GlobalShortcut,
    string TargetItem,
    ShortcutCommandType CommandType,
    string? CommandValue);
```

Create `src/OpenHab.App/Shortcuts/ShortcutSettings.cs`:

```csharp
using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record BuiltInShortcutSettings(
    bool Enabled,
    ShortcutBinding? Binding,
    RadialActivationMode RadialActivationMode = RadialActivationMode.Toggle);

public sealed record ShortcutSettings(
    BuiltInShortcutSettings CommandMenu,
    BuiltInShortcutSettings VoiceMode,
    ImmutableArray<ShortcutAction> Actions)
{
    public static ShortcutSettings Default { get; } = new(
        new BuiltInShortcutSettings(
            Enabled: true,
            Binding: new ShortcutBinding([ShortcutModifier.Win], "O"),
            RadialActivationMode: RadialActivationMode.Toggle),
        new BuiltInShortcutSettings(
            Enabled: false,
            Binding: null,
            RadialActivationMode: RadialActivationMode.Toggle),
        []);

    public ShortcutSettings Normalized()
    {
        var commandMenuBinding = CommandMenu.Binding is null
            ? Default.CommandMenu.Binding
            : ShortcutBindingFormatter.Normalize(CommandMenu.Binding);

        var commandMenuMode = Enum.IsDefined(CommandMenu.RadialActivationMode)
            ? CommandMenu.RadialActivationMode
            : RadialActivationMode.Toggle;

        var actions = Actions.IsDefault
            ? []
            : Actions
                .Where(static action => !string.IsNullOrWhiteSpace(action.Id))
                .Select(static action => action with
                {
                    Id = action.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(action.Name) ? "Unnamed action" : action.Name.Trim(),
                    IconId = string.IsNullOrWhiteSpace(action.IconId) ? "custom" : action.IconId.Trim(),
                    GlobalShortcut = action.GlobalShortcut is null ? null : ShortcutBindingFormatter.Normalize(action.GlobalShortcut),
                    TargetItem = action.TargetItem?.Trim() ?? string.Empty,
                    CommandValue = string.IsNullOrWhiteSpace(action.CommandValue) ? null : action.CommandValue.Trim()
                })
                .ToImmutableArray();

        return new ShortcutSettings(
            CommandMenu with
            {
                Binding = commandMenuBinding,
                RadialActivationMode = commandMenuMode
            },
            VoiceMode with
            {
                Enabled = false,
                Binding = null,
                RadialActivationMode = RadialActivationMode.Toggle
            },
            actions);
    }
}
```

- [ ] **Step 4: Add settings property and setter**

Modify `src/OpenHab.App/Settings/AppSettings.cs`:

```csharp
using OpenHab.App.Shortcuts;
```

Add this property before `CachedMainUiPageLinks`:

```csharp
ShortcutSettings? Shortcuts = null,
```

Update `Default` to pass shortcut defaults:

```csharp
DeviceInfoSync: DeviceInfoSyncSettings.Default,
Shortcuts: ShortcutSettings.Default,
CachedMainUiPageLinks: []);
```

Modify `src/OpenHab.App/Settings/AppSettingsController.cs`:

```csharp
using OpenHab.App.Shortcuts;
```

Add this public method near the other settings setters:

```csharp
public void SetShortcutSettings(ShortcutSettings settings)
{
    ArgumentNullException.ThrowIfNull(settings);
    UpdateSettings(appSettings => appSettings with { Shortcuts = settings.Normalized() });
}
```

Update `NormalizeLoadedSettings` return object:

```csharp
Shortcuts = (settings.Shortcuts ?? ShortcutSettings.Default).Normalized(),
```

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~ShortcutBindingTests|FullyQualifiedName~DefaultsIncludeShortcutSettings|FullyQualifiedName~CanPersistShortcutSettings"
```

Expected: all selected tests pass.

Commit:

```powershell
git add src\OpenHab.App\Shortcuts src\OpenHab.App\Settings\AppSettings.cs src\OpenHab.App\Settings\AppSettingsController.cs tests\OpenHab.App.Tests\Shortcuts tests\OpenHab.App.Tests\AppSettingsControllerTests.cs
git commit -m "feat: add shortcut settings model"
```

## Task 2: Shortcut Validation, Conflict Detection, And Icon Catalog

**Files:**
- Create: `src/OpenHab.App/Shortcuts/ShortcutValidation.cs`
- Create: `src/OpenHab.App/Shortcuts/ShortcutIconCatalog.cs`
- Test: `tests/OpenHab.App.Tests/Shortcuts/ShortcutValidationTests.cs`

- [ ] **Step 1: Write failing validation tests**

Create `tests/OpenHab.App.Tests/Shortcuts/ShortcutValidationTests.cs`:

```csharp
using OpenHab.App.Shortcuts;
using System.Collections.Immutable;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutValidationTests
{
    [Fact]
    public void CommandMenuBindingRequiresModifierAndKey()
    {
        var result = ShortcutValidation.ValidateBinding(new ShortcutBinding([], "O"), "Command Menu", []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("modifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DuplicateShortcutReportsOwner()
    {
        var owner = new ShortcutBindingOwner("Movie Night", new ShortcutBinding([ShortcutModifier.Win], "M"));

        var result = ShortcutValidation.ValidateBinding(
            new ShortcutBinding([ShortcutModifier.Win], "M"),
            "Kitchen",
            [owner]);

        Assert.False(result.IsValid);
        Assert.Contains("Movie Night", Assert.Single(result.Errors));
    }

    [Fact]
    public void VoiceTypingShortcutIsReserved()
    {
        var result = ShortcutValidation.ValidateBinding(
            new ShortcutBinding([ShortcutModifier.Win], "V"),
            "Voice Mode",
            []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("reserved", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ShortcutCommandType.OnOff, "ON")]
    [InlineData(ShortcutCommandType.OnOff, "OFF")]
    [InlineData(ShortcutCommandType.OpenClose, "OPEN")]
    [InlineData(ShortcutCommandType.OpenClose, "CLOSE")]
    [InlineData(ShortcutCommandType.SendCommand, "PLAY")]
    public void ValidActionsPass(ShortcutCommandType type, string value)
    {
        var action = new ShortcutAction("a1", "Media", "play", true, null, "LivingRoom_Speaker", type, value);

        var result = ShortcutValidation.ValidateAction(action);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void IconCatalogIncludesMediaIcons()
    {
        var ids = ShortcutIconCatalog.All.Select(icon => icon.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("play", ids);
        Assert.Contains("pause", ids);
        Assert.Contains("stop", ids);
        Assert.Contains("cast", ids);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~ShortcutValidationTests"
```

Expected: compile fails because validation and icon catalog types do not exist.

- [ ] **Step 3: Implement validation**

Create `src/OpenHab.App/Shortcuts/ShortcutValidation.cs`:

```csharp
using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutValidationResult(bool IsValid, ImmutableArray<string> Errors)
{
    public static ShortcutValidationResult Valid { get; } = new(true, []);
    public static ShortcutValidationResult Invalid(params string[] errors) => new(false, [.. errors]);
}

public sealed record ShortcutBindingOwner(string OwnerName, ShortcutBinding Binding);

public static class ShortcutValidation
{
    private static readonly HashSet<string> ReservedBindings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Win + V"
    };

    public static ShortcutValidationResult ValidateBinding(
        ShortcutBinding? binding,
        string ownerName,
        IEnumerable<ShortcutBindingOwner> existingBindings,
        bool allowUnassigned = false)
    {
        if (binding is null)
        {
            return allowUnassigned
                ? ShortcutValidationResult.Valid
                : ShortcutValidationResult.Invalid($"{ownerName} must have a shortcut.");
        }

        var normalized = ShortcutBindingFormatter.Normalize(binding);
        var errors = new List<string>();

        if (normalized.Modifiers.Length == 0)
        {
            errors.Add($"{ownerName} shortcut must include at least one modifier.");
        }

        if (string.IsNullOrWhiteSpace(normalized.Key))
        {
            errors.Add($"{ownerName} shortcut must include a non-modifier key.");
        }

        if (IsRejectedSingleKey(normalized))
        {
            errors.Add($"{ownerName} shortcut cannot be {normalized.Key} by itself.");
        }

        var formatted = ShortcutBindingFormatter.Format(normalized);
        if (ReservedBindings.Contains(formatted))
        {
            errors.Add($"{formatted} is reserved by Windows or the app.");
        }

        foreach (var existing in existingBindings)
        {
            if (string.Equals(ShortcutBindingFormatter.Format(existing.Binding), formatted, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"This shortcut is already used by \"{existing.OwnerName}\".");
                break;
            }
        }

        return errors.Count == 0 ? ShortcutValidationResult.Valid : new ShortcutValidationResult(false, [.. errors]);
    }

    public static ShortcutValidationResult ValidateAction(ShortcutAction action)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(action.Id))
        {
            errors.Add("Action id is required.");
        }

        if (string.IsNullOrWhiteSpace(action.Name))
        {
            errors.Add("Action name is required.");
        }

        if (string.IsNullOrWhiteSpace(action.TargetItem))
        {
            errors.Add("Target item is required.");
        }

        if (!Enum.IsDefined(action.CommandType))
        {
            errors.Add("Action type is invalid.");
        }

        switch (action.CommandType)
        {
            case ShortcutCommandType.OnOff when !IsOneOf(action.CommandValue, "ON", "OFF"):
                errors.Add("On/Off actions require ON or OFF.");
                break;
            case ShortcutCommandType.OpenClose when !IsOneOf(action.CommandValue, "OPEN", "CLOSE"):
                errors.Add("Open/Close actions require OPEN or CLOSE.");
                break;
            case ShortcutCommandType.SendCommand when string.IsNullOrWhiteSpace(action.CommandValue):
                errors.Add("Send command actions require a command value.");
                break;
            case ShortcutCommandType.Toggle:
            case ShortcutCommandType.OpenSlider:
            case ShortcutCommandType.OpenColorPicker:
                break;
        }

        if (!action.ShowInCommandMenu && action.GlobalShortcut is null)
        {
            errors.Add("Action must be shown in the command menu, have a global shortcut, or both.");
        }

        return errors.Count == 0 ? ShortcutValidationResult.Valid : new ShortcutValidationResult(false, [.. errors]);
    }

    private static bool IsRejectedSingleKey(ShortcutBinding binding)
    {
        return binding.Modifiers.Length == 0 &&
            (string.Equals(binding.Key, "Escape", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(binding.Key, "Enter", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(binding.Key, "Tab", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOneOf(string? value, params string[] allowed)
    {
        return allowed.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: Implement icon catalog**

Create `src/OpenHab.App/Shortcuts/ShortcutIconCatalog.cs`:

```csharp
using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record ShortcutIconDefinition(string Id, string Label, string Group);

public static class ShortcutIconCatalog
{
    public static ImmutableArray<ShortcutIconDefinition> All { get; } =
    [
        new("light-bulb", "Bulb", "Lighting"),
        new("ceiling-light", "Ceiling light", "Lighting"),
        new("lamp", "Lamp", "Lighting"),
        new("strip-light", "Strip light", "Lighting"),
        new("brightness", "Brightness", "Lighting"),
        new("color-wheel", "Color", "Lighting"),
        new("power", "Power", "Lighting"),
        new("blinds", "Blinds", "Openings"),
        new("curtains", "Curtains", "Openings"),
        new("garage", "Garage", "Openings"),
        new("door", "Door", "Openings"),
        new("lock", "Lock", "Openings"),
        new("thermostat", "Thermostat", "Climate"),
        new("fan", "Fan", "Climate"),
        new("snowflake", "Cooling", "Climate"),
        new("flame", "Heating", "Climate"),
        new("humidity", "Humidity", "Climate"),
        new("play", "Play", "Media"),
        new("pause", "Pause", "Media"),
        new("stop", "Stop", "Media"),
        new("speaker", "Speaker", "Media"),
        new("tv", "TV", "Media"),
        new("cast", "Cast", "Media"),
        new("music", "Music", "Media"),
        new("volume", "Volume", "Media"),
        new("scene", "Scene", "Scenes and tools"),
        new("movie", "Movie", "Scenes and tools"),
        new("sleep", "Sleep", "Scenes and tools"),
        new("away", "Away", "Scenes and tools"),
        new("sparkle", "Sparkle", "Scenes and tools"),
        new("timer", "Timer", "Scenes and tools"),
        new("custom", "Custom", "Scenes and tools")
    ];

    public static bool Contains(string iconId)
    {
        return All.Any(icon => string.Equals(icon.Id, iconId, StringComparison.Ordinal));
    }
}
```

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~ShortcutValidationTests"
```

Expected: all selected tests pass.

Commit:

```powershell
git add src\OpenHab.App\Shortcuts tests\OpenHab.App.Tests\Shortcuts
git commit -m "feat: validate shortcut actions"
```

## Task 3: openHAB Item Listing And State Lookup Contracts

**Files:**
- Modify: `src/OpenHab.Core/Api/IOpenHabClient.cs`
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Modify: `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`
- Test: `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`

- [ ] **Step 1: Write failing core API tests**

Append to `tests/OpenHab.Core.Tests/OpenHabHttpClientTests.cs`:

```csharp
[Fact]
public async Task GetItemsParsesItemSummaries()
{
    var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""
        [
          { "name": "Light_LivingRoom", "label": "Living Room Light", "type": "Switch", "state": "ON" },
          { "name": "Speaker_Playback", "type": "Player", "state": "PLAY" }
        ]
        """)
    });
    var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

    var items = await client.GetItemsAsync(CancellationToken.None);

    Assert.Collection(items,
        item =>
        {
            Assert.Equal("Light_LivingRoom", item.Name);
            Assert.Equal("Living Room Light", item.Label);
            Assert.Equal("Switch", item.Type);
            Assert.Equal("ON", item.State);
        },
        item =>
        {
            Assert.Equal("Speaker_Playback", item.Name);
            Assert.Equal("Speaker_Playback", item.Label);
            Assert.Equal("Player", item.Type);
            Assert.Equal("PLAY", item.State);
        });
}

[Fact]
public async Task GetItemStateReturnsStateProperty()
{
    var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""{ "name": "Light", "state": "OFF" }""")
    });
    var client = new OpenHabHttpClient(new HttpClient(handler), new Uri("http://openhab:8080"));

    var state = await client.GetItemStateAsync("Light", CancellationToken.None);

    Assert.Equal("OFF", state);
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter "FullyQualifiedName~GetItemsParsesItemSummaries|FullyQualifiedName~GetItemStateReturnsStateProperty"
```

Expected: compile fails because `GetItemsAsync`, `GetItemStateAsync`, and `OpenHabItemSummary` do not exist.

- [ ] **Step 3: Add API contract**

Modify `src/OpenHab.Core/Api/IOpenHabClient.cs`:

```csharp
public sealed record OpenHabItemSummary(string Name, string Label, string Type, string? State);
```

Add to `IOpenHabClient`:

```csharp
Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken cancellationToken);
Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement HTTP methods**

Add these methods to `src/OpenHab.Core/Api/OpenHabHttpClient.cs`:

```csharp
public async Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("rest/items"));
    ApplyAuth(request);

    using var response = await _httpClient.SendAsync(request, cancellationToken);
    await ThrowIfFailedAsync(response, cancellationToken);

    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

    var results = new List<OpenHabItemSummary>();
    foreach (var element in json.RootElement.EnumerateArray())
    {
        var name = ReadString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        var label = ReadString(element, "label");
        var type = ReadString(element, "type") ?? string.Empty;
        var state = ReadString(element, "state");
        results.Add(new OpenHabItemSummary(name, string.IsNullOrWhiteSpace(label) ? name : label, type, state));
    }

    return results;
}

public async Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri($"rest/items/{Uri.EscapeDataString(itemName)}"));
    ApplyAuth(request);

    using var response = await _httpClient.SendAsync(request, cancellationToken);
    await ThrowIfFailedAsync(response, cancellationToken);

    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    return ReadString(json.RootElement, "state");
}
```

Modify `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs` to implement the new interface members:

```csharp
public List<OpenHabItemSummary> Items { get; } = new();
public Dictionary<string, string?> ItemStates { get; } = new(StringComparer.Ordinal);

public Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken cancellationToken)
{
    return Task.FromResult<IReadOnlyList<OpenHabItemSummary>>(Items);
}

public Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken)
{
    ItemStates.TryGetValue(itemName, out var state);
    return Task.FromResult(state);
}
```

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter "FullyQualifiedName~GetItemsParsesItemSummaries|FullyQualifiedName~GetItemStateReturnsStateProperty"
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~SitemapRuntimeControllerTests"
```

Expected: selected tests pass.

Commit:

```powershell
git add src\OpenHab.Core\Api tests\OpenHab.Core.Tests\OpenHabHttpClientTests.cs tests\OpenHab.App.Tests\Runtime\FakeOpenHabClient.cs
git commit -m "feat: add openhab item lookup api"
```

## Task 4: Shortcut Action Executor

**Files:**
- Create: `src/OpenHab.App/Shortcuts/ShortcutActionExecutor.cs`
- Test: `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionExecutorTests.cs`

- [ ] **Step 1: Write failing executor tests**

Create `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionExecutorTests.cs`:

```csharp
using OpenHab.App.Runtime;
using OpenHab.App.Shortcuts;
using OpenHab.Core.Api;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class ShortcutActionExecutorTests
{
    [Fact]
    public async Task BlocksExecutionWhenDisconnected()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => null, () => ConnectionState.Offline);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.Disconnected, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task SendCommandSendsConfiguredValue()
    {
        var client = new RecordingShortcutClient();
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.SendCommand, "PLAY");

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", "PLAY"), Assert.Single(client.Commands));
    }

    [Theory]
    [InlineData("ON", "OFF")]
    [InlineData("OFF", "ON")]
    public async Task ToggleReadsStateAndSendsOpposite(string current, string expected)
    {
        var client = new RecordingShortcutClient();
        client.ItemStates["LivingRoom_Item"] = current;
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.Toggle, null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("LivingRoom_Item", expected), Assert.Single(client.Commands));
    }

    [Fact]
    public async Task ToggleFailsForUnknownState()
    {
        var client = new RecordingShortcutClient();
        client.ItemStates["LivingRoom_Item"] = "NULL";
        var executor = new ShortcutActionExecutor(() => client, () => ConnectionState.Online);
        var action = Action(ShortcutCommandType.Toggle, null);

        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShortcutActionExecutionFailure.UnsupportedState, result.Failure);
        Assert.Empty(client.Commands);
    }

    private static ShortcutAction Action(ShortcutCommandType type, string? value)
    {
        return new ShortcutAction("a1", "Action", "play", true, null, "LivingRoom_Item", type, value);
    }

    private sealed class RecordingShortcutClient : IOpenHabClient
    {
        public List<(string ItemName, string Command)> Commands { get; } = new();
        public Dictionary<string, string?> ItemStates { get; } = new(StringComparer.Ordinal);

        public Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken)
        {
            Commands.Add((itemName, command));
            return Task.CompletedTask;
        }

        public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SitemapInfo>>([]);
        public Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OpenHabItemSummary>>([]);
        public Task<IReadOnlyList<OpenHab.Core.Ui.MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OpenHab.Core.Ui.MainUiPageComponent>>([]);

        public Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken)
        {
            ItemStates.TryGetValue(itemName, out var state);
            return Task.FromResult(state);
        }
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~ShortcutActionExecutorTests"
```

Expected: compile fails because executor result types do not exist.

- [ ] **Step 3: Implement executor**

Create `src/OpenHab.App/Shortcuts/ShortcutActionExecutor.cs`:

```csharp
using OpenHab.App.Runtime;
using OpenHab.Core.Api;

namespace OpenHab.App.Shortcuts;

public enum ShortcutActionExecutionFailure
{
    None,
    Disconnected,
    InvalidAction,
    MissingClient,
    UnsupportedState,
    CommandFailed,
    SurfaceRequired
}

public sealed record ShortcutActionExecutionResult(
    bool Succeeded,
    ShortcutActionExecutionFailure Failure,
    string Message)
{
    public static ShortcutActionExecutionResult Success { get; } = new(true, ShortcutActionExecutionFailure.None, "Action executed.");
    public static ShortcutActionExecutionResult Failed(ShortcutActionExecutionFailure failure, string message) => new(false, failure, message);
}

public sealed class ShortcutActionExecutor
{
    private readonly Func<IOpenHabClient?> getClient;
    private readonly Func<ConnectionState> getConnectionState;

    public ShortcutActionExecutor(Func<IOpenHabClient?> getClient, Func<ConnectionState> getConnectionState)
    {
        this.getClient = getClient;
        this.getConnectionState = getConnectionState;
    }

    public async Task<ShortcutActionExecutionResult> ExecuteAsync(
        ShortcutAction action,
        CancellationToken cancellationToken = default)
    {
        var validation = ShortcutValidation.ValidateAction(action);
        if (!validation.IsValid)
        {
            return ShortcutActionExecutionResult.Failed(
                ShortcutActionExecutionFailure.InvalidAction,
                string.Join(" ", validation.Errors));
        }

        if (getConnectionState() != ConnectionState.Online)
        {
            return ShortcutActionExecutionResult.Failed(
                ShortcutActionExecutionFailure.Disconnected,
                "openHAB is not connected.");
        }

        var client = getClient();
        if (client is null)
        {
            return ShortcutActionExecutionResult.Failed(
                ShortcutActionExecutionFailure.MissingClient,
                "No openHAB client is available.");
        }

        try
        {
            var command = await ResolveCommandAsync(client, action, cancellationToken);
            if (command is null)
            {
                return ShortcutActionExecutionResult.Failed(
                    ShortcutActionExecutionFailure.SurfaceRequired,
                    "This action requires an interactive command surface.");
            }

            await client.SendCommandAsync(action.TargetItem, command, cancellationToken);
            return ShortcutActionExecutionResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ShortcutActionExecutionResult.Failed(ShortcutActionExecutionFailure.UnsupportedState, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ShortcutActionExecutionResult.Failed(ShortcutActionExecutionFailure.CommandFailed, ex.Message);
        }
    }

    private static async Task<string?> ResolveCommandAsync(
        IOpenHabClient client,
        ShortcutAction action,
        CancellationToken cancellationToken)
    {
        return action.CommandType switch
        {
            ShortcutCommandType.Toggle => await ResolveToggleCommandAsync(client, action.TargetItem, cancellationToken),
            ShortcutCommandType.OnOff or ShortcutCommandType.OpenClose or ShortcutCommandType.SendCommand => action.CommandValue,
            ShortcutCommandType.OpenSlider or ShortcutCommandType.OpenColorPicker => null,
            _ => throw new InvalidOperationException("Unsupported shortcut command type.")
        };
    }

    private static async Task<string> ResolveToggleCommandAsync(
        IOpenHabClient client,
        string itemName,
        CancellationToken cancellationToken)
    {
        var state = await client.GetItemStateAsync(itemName, cancellationToken);
        return state?.ToUpperInvariant() switch
        {
            "ON" => "OFF",
            "OFF" => "ON",
            _ => throw new InvalidOperationException("Toggle actions require current state ON or OFF.")
        };
    }
}
```

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~ShortcutActionExecutorTests"
```

Expected: selected tests pass.

Commit:

```powershell
git add src\OpenHab.App\Shortcuts\ShortcutActionExecutor.cs tests\OpenHab.App.Tests\Shortcuts\ShortcutActionExecutorTests.cs
git commit -m "feat: execute shortcut actions"
```

## Task 5: Shortcuts Settings Page Shell And Built-In Cards

**Files:**
- Create: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutSettingsControls.cs`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Add Shortcuts page enum and navigation row**

Modify `SettingsPage` enum in `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`:

```csharp
private enum SettingsPage
{
    Root,
    Connection,
    General,
    Appearance,
    DeviceInfoSync,
    Shortcuts,
    About
}
```

In the root navigation case, insert the category between Device Info Sync and About:

```csharp
SettingsContent.Children.Add(CreateCategoryRow("\uE765", "Shortcuts", "Command menu and global shortcuts", SettingsPage.Shortcuts));
```

Add the switch case:

```csharp
case SettingsPage.Shortcuts:
    UpdateSettingsBreadcrumb("Shortcuts");
    SettingsSubtitleText.Text = "Configure global shortcuts and command menu actions.";
    BuildShortcutsSettingsPage();
    break;
```

- [ ] **Step 2: Create settings control helpers**

Create `src/OpenHab.Windows.Tray/Shortcuts/ShortcutSettingsControls.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Shortcuts;

namespace OpenHab.Windows.Tray.Shortcuts;

internal static class ShortcutSettingsControls
{
    public static Border CreateCard(UIElement content)
    {
        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = content
        };
    }

    public static StackPanel CreateShortcutChips(ShortcutBinding? binding)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        foreach (var part in ShortcutBindingFormatter.Format(binding).Split(" + ", StringSplitOptions.None))
        {
            panel.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock { Text = part }
            });
        }

        return panel;
    }
}
```

- [ ] **Step 3: Build Shortcuts page shell**

Add `using OpenHab.App.Shortcuts;` and `using OpenHab.Windows.Tray.Shortcuts;` to `SettingsPageControl.xaml.cs`.

Add this method:

```csharp
private void BuildShortcutsSettingsPage()
{
    AddSettingsSectionTitle("Built-in shortcuts");
    SettingsContent.Children.Add(BuildCommandMenuShortcutCard());
    SettingsContent.Children.Add(BuildVoiceModeShortcutCard());

    AddSettingsSectionTitle("Actions and shortcuts");
    SettingsContent.Children.Add(ShortcutSettingsControls.CreateCard(new TextBlock
    {
        Text = "No actions yet.\nAdd actions to make them available in the command menu.",
        Opacity = 0.72
    }));
}
```

Add these card methods:

```csharp
private UIElement BuildCommandMenuShortcutCard()
{
    var settings = settingsController.Current.Shortcuts ?? ShortcutSettings.Default;
    var panel = new StackPanel { Spacing = 10 };
    panel.Children.Add(new TextBlock { Text = "openHAB Command Menu", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
    panel.Children.Add(new TextBlock
    {
        Text = "Open the command menu anywhere and trigger saved actions.",
        Opacity = 0.68,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
    });
    panel.Children.Add(new ToggleSwitch
    {
        Header = "Enabled",
        IsOn = settings.CommandMenu.Enabled
    });
    panel.Children.Add(new TextBlock { Text = "Global shortcut" });
    panel.Children.Add(ShortcutSettingsControls.CreateShortcutChips(settings.CommandMenu.Binding));
    panel.Children.Add(new TextBlock { Text = "Activation mode" });
    panel.Children.Add(new ComboBox
    {
        ItemsSource = Enum.GetValues<RadialActivationMode>(),
        SelectedItem = settings.CommandMenu.RadialActivationMode,
        Width = 180
    });
    return ShortcutSettingsControls.CreateCard(panel);
}

private UIElement BuildVoiceModeShortcutCard()
{
    var panel = new StackPanel { Spacing = 8 };
    panel.Children.Add(new TextBlock { Text = "Voice Mode", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
    panel.Children.Add(new TextBlock
    {
        Text = "Planned voice shortcut, coming soon.",
        Opacity = 0.68,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
    });
    panel.Children.Add(new InfoBar
    {
        Severity = InfoBarSeverity.Informational,
        IsOpen = true,
        IsClosable = false,
        Message = "Coming soon. Shortcut is unassigned and not registered."
    });
    panel.Children.Add(ShortcutSettingsControls.CreateShortcutChips(null));
    return ShortcutSettingsControls.CreateCard(panel);
}
```

- [ ] **Step 4: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

- [ ] **Step 5: Commit**

Commit:

```powershell
git add src\OpenHab.Windows.Tray\Settings\SettingsPageControl.xaml.cs src\OpenHab.Windows.Tray\Shortcuts\ShortcutSettingsControls.cs
git commit -m "feat: add shortcuts settings page shell"
```

## Task 6: Shortcut Recorder Control

**Files:**
- Create: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Create recorder control**

Create `src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OpenHab.App.Shortcuts;
using Windows.System;

namespace OpenHab.Windows.Tray.Shortcuts;

internal sealed class ShortcutRecorderControl : UserControl
{
    private readonly StackPanel root = new() { Spacing = 6 };
    private readonly TextBlock errorText = new() { Opacity = 0.75 };
    private ShortcutBinding? binding;
    private bool isRecording;

    public event EventHandler<ShortcutBinding?>? BindingChanged;

    public ShortcutRecorderControl()
    {
        Content = root;
        IsTabStop = true;
        KeyDown += OnKeyDown;
        Refresh();
    }

    public ShortcutBinding? Binding
    {
        get => binding;
        set
        {
            binding = value;
            Refresh();
        }
    }

    public bool AllowClear { get; set; }

    public string? Error
    {
        get => errorText.Text;
        set
        {
            errorText.Text = value ?? string.Empty;
            errorText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Refresh()
    {
        root.Children.Clear();
        root.Children.Add(ShortcutSettingsControls.CreateShortcutChips(binding));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var edit = new Button { Content = isRecording ? "Recording..." : "Edit" };
        edit.Click += (_, _) =>
        {
            isRecording = true;
            Focus(FocusState.Programmatic);
            Refresh();
        };
        buttons.Children.Add(edit);

        if (AllowClear)
        {
            var clear = new Button { Content = "Clear" };
            clear.Click += (_, _) =>
            {
                binding = null;
                BindingChanged?.Invoke(this, binding);
                Refresh();
            };
            buttons.Children.Add(clear);
        }

        root.Children.Add(buttons);
        root.Children.Add(errorText);
        Error = Error;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!isRecording)
        {
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            isRecording = false;
            e.Handled = true;
            Refresh();
            return;
        }

        var modifiers = new List<ShortcutModifier>();
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ||
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            modifiers.Add(ShortcutModifier.Win);
        }
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            modifiers.Add(ShortcutModifier.Ctrl);
        }
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            modifiers.Add(ShortcutModifier.Alt);
        }
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            modifiers.Add(ShortcutModifier.Shift);
        }

        if (e.Key is VirtualKey.LeftWindows or VirtualKey.RightWindows or VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift)
        {
            e.Handled = true;
            return;
        }

        binding = ShortcutBindingFormatter.Normalize(new ShortcutBinding([.. modifiers], e.Key.ToString()));
        isRecording = false;
        e.Handled = true;
        BindingChanged?.Invoke(this, binding);
        Refresh();
    }
}
```

- [ ] **Step 2: Replace built-in shortcut chips with recorder**

In `BuildCommandMenuShortcutCard`, replace the chips line with:

```csharp
var recorder = new ShortcutRecorderControl
{
    Binding = settings.CommandMenu.Binding
};
recorder.BindingChanged += (_, binding) =>
{
    var current = settingsController.Current.Shortcuts ?? ShortcutSettings.Default;
    settingsController.SetShortcutSettings(current with
    {
        CommandMenu = current.CommandMenu with { Binding = binding }
    });
};
panel.Children.Add(recorder);
```

Leave Voice Mode as disabled `Unassigned` chips.

- [ ] **Step 3: Wire command menu toggle and mode persistence**

In `BuildCommandMenuShortcutCard`, assign local variables for the toggle and combo, then wire:

```csharp
enabledToggle.Toggled += (_, _) =>
{
    var current = settingsController.Current.Shortcuts ?? ShortcutSettings.Default;
    settingsController.SetShortcutSettings(current with
    {
        CommandMenu = current.CommandMenu with { Enabled = enabledToggle.IsOn }
    });
};

modeCombo.SelectionChanged += (_, _) =>
{
    if (modeCombo.SelectedItem is RadialActivationMode mode)
    {
        var current = settingsController.Current.Shortcuts ?? ShortcutSettings.Default;
        settingsController.SetShortcutSettings(current with
        {
            CommandMenu = current.CommandMenu with { RadialActivationMode = mode }
        });
    }
};
```

- [ ] **Step 4: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\Shortcuts\ShortcutRecorderControl.cs src\OpenHab.Windows.Tray\Settings\SettingsPageControl.xaml.cs
git commit -m "feat: add shortcut recorder control"
```

## Task 7: Actions Table, Editor, Icon Picker, And Item Selector

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutSettingsControls.cs`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Add table and editor helper methods**

Add these methods to `ShortcutSettingsControls.cs`:

```csharp
public static Grid CreateActionsTable(
    IReadOnlyList<ShortcutAction> actions,
    Action<ShortcutAction> edit,
    Action<ShortcutAction> delete)
{
    var grid = new Grid { RowSpacing = 0 };
    for (var i = 0; i < 7; i++)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
    }

    AddHeader(grid, "Icon", 0);
    AddHeader(grid, "Action name", 1);
    AddHeader(grid, "Availability", 2);
    AddHeader(grid, "Shortcut", 3);
    AddHeader(grid, "Target item", 4);
    AddHeader(grid, "Action type", 5);
    AddHeader(grid, "Actions", 6);

    var row = 1;
    foreach (var action in actions)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(grid, action.IconId, row, 0);
        AddCell(grid, action.Name, row, 1);
        AddCell(grid, FormatAvailability(action), row, 2);
        AddCell(grid, ShortcutBindingFormatter.Format(action.GlobalShortcut), row, 3);
        AddCell(grid, action.TargetItem, row, 4);
        AddCell(grid, action.CommandType.ToString(), row, 5);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var editButton = new Button { Content = "Edit" };
        editButton.Click += (_, _) => edit(action);
        var deleteButton = new Button { Content = "Delete" };
        deleteButton.Click += (_, _) => delete(action);
        buttons.Children.Add(editButton);
        buttons.Children.Add(deleteButton);
        Grid.SetRow(buttons, row);
        Grid.SetColumn(buttons, 6);
        grid.Children.Add(buttons);
        row++;
    }

    return grid;
}

public static ComboBox CreateIconPicker(string selectedIconId)
{
    var combo = new ComboBox { Width = 220 };
    foreach (var icon in ShortcutIconCatalog.All)
    {
        combo.Items.Add(new ComboBoxItem
        {
            Content = $"{icon.Group} - {icon.Label}",
            Tag = icon.Id,
            IsSelected = string.Equals(icon.Id, selectedIconId, StringComparison.Ordinal)
        });
    }

    return combo;
}

private static void AddHeader(Grid grid, string text, int column)
{
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    AddCell(grid, text, 0, column, true);
}

private static void AddCell(Grid grid, string text, int row, int column, bool header = false)
{
    var block = new TextBlock
    {
        Text = text,
        FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Margin = new Thickness(6),
        TextWrapping = TextWrapping.Wrap
    };
    Grid.SetRow(block, row);
    Grid.SetColumn(block, column);
    grid.Children.Add(block);
}

private static string FormatAvailability(ShortcutAction action)
{
    return (action.GlobalShortcut is not null, action.ShowInCommandMenu) switch
    {
        (true, true) => "Shortcut + Command menu",
        (true, false) => "Shortcut only",
        (false, true) => "Command menu only",
        _ => "Unavailable"
    };
}
```

- [ ] **Step 2: Add fields to settings control**

Add fields to `SettingsPageControl.xaml.cs`:

```csharp
private ShortcutAction? selectedShortcutAction;
private StackPanel? shortcutEditorPanel;
```

Add to `ResetSettingsControlReferences`:

```csharp
selectedShortcutAction = null;
shortcutEditorPanel = null;
```

- [ ] **Step 3: Replace empty actions state with table and editor**

Replace the `Actions and shortcuts` body in `BuildShortcutsSettingsPage`:

```csharp
var actionsPanel = new StackPanel { Spacing = 8 };
var addButton = new Button { Content = "Add action", HorizontalAlignment = HorizontalAlignment.Left };
addButton.Click += (_, _) =>
{
    selectedShortcutAction = new ShortcutAction(
        Guid.NewGuid().ToString("N"),
        "New action",
        "custom",
        true,
        null,
        string.Empty,
        ShortcutCommandType.SendCommand,
        null);
    RebuildShortcutEditor();
};
actionsPanel.Children.Add(addButton);

var actions = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Actions;
if (actions.Length == 0)
{
    actionsPanel.Children.Add(new TextBlock
    {
        Text = "No actions yet.\nAdd actions to make them available in the command menu.",
        Opacity = 0.72
    });
}
else
{
    actionsPanel.Children.Add(ShortcutSettingsControls.CreateActionsTable(
        actions,
        action =>
        {
            selectedShortcutAction = action;
            RebuildShortcutEditor();
        },
        action => DeleteShortcutAction(action.Id)));
}

shortcutEditorPanel = new StackPanel { Spacing = 8 };
actionsPanel.Children.Add(shortcutEditorPanel);
SettingsContent.Children.Add(ShortcutSettingsControls.CreateCard(actionsPanel));
```

- [ ] **Step 4: Add editor methods**

Add these methods to `SettingsPageControl.xaml.cs`:

```csharp
private void RebuildShortcutEditor()
{
    if (shortcutEditorPanel is null)
    {
        return;
    }

    shortcutEditorPanel.Children.Clear();
    if (selectedShortcutAction is null)
    {
        return;
    }

    var draft = selectedShortcutAction;
    var nameBox = new TextBox { Header = "Action name", Text = draft.Name, Width = 320 };
    var iconPicker = ShortcutSettingsControls.CreateIconPicker(draft.IconId);
    var showInMenu = new ToggleSwitch { Header = "Show in command menu", IsOn = draft.ShowInCommandMenu };
    var shortcut = new ShortcutRecorderControl { Binding = draft.GlobalShortcut, AllowClear = true };
    var targetItem = new TextBox { Header = "Target item", Text = draft.TargetItem, Width = 320 };
    var typeCombo = new ComboBox { Header = "Action type", ItemsSource = Enum.GetValues<ShortcutCommandType>(), SelectedItem = draft.CommandType, Width = 220 };
    var commandValue = new TextBox { Header = "Command value", Text = draft.CommandValue ?? string.Empty, Width = 220 };
    var validationText = new TextBlock { Opacity = 0.75 };

    shortcutEditorPanel.Children.Add(new TextBlock { Text = "Edit action", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
    shortcutEditorPanel.Children.Add(nameBox);
    shortcutEditorPanel.Children.Add(iconPicker);
    shortcutEditorPanel.Children.Add(showInMenu);
    shortcutEditorPanel.Children.Add(shortcut);
    shortcutEditorPanel.Children.Add(targetItem);
    shortcutEditorPanel.Children.Add(typeCombo);
    shortcutEditorPanel.Children.Add(commandValue);
    shortcutEditorPanel.Children.Add(validationText);

    var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
    var save = new Button { Content = "Save" };
    var cancel = new Button { Content = "Cancel" };
    save.Click += (_, _) =>
    {
        var iconId = iconPicker.SelectedItem is ComboBoxItem { Tag: string selectedIcon } ? selectedIcon : "custom";
        var type = typeCombo.SelectedItem is ShortcutCommandType selectedType ? selectedType : ShortcutCommandType.SendCommand;
        var updated = draft with
        {
            Name = nameBox.Text,
            IconId = iconId,
            ShowInCommandMenu = showInMenu.IsOn,
            GlobalShortcut = shortcut.Binding,
            TargetItem = targetItem.Text,
            CommandType = type,
            CommandValue = string.IsNullOrWhiteSpace(commandValue.Text) ? null : commandValue.Text.Trim()
        };
        var validation = ShortcutValidation.ValidateAction(updated);
        if (!validation.IsValid)
        {
            validationText.Text = string.Join(Environment.NewLine, validation.Errors);
            return;
        }

        SaveShortcutAction(updated);
    };
    cancel.Click += (_, _) =>
    {
        selectedShortcutAction = null;
        RebuildShortcutEditor();
    };
    buttons.Children.Add(save);
    buttons.Children.Add(cancel);
    shortcutEditorPanel.Children.Add(buttons);
}

private void SaveShortcutAction(ShortcutAction action)
{
    var current = settingsController.Current.Shortcuts ?? ShortcutSettings.Default;
    var actions = current.Actions.Where(existing => existing.Id != action.Id).Append(action).ToImmutableArray();
    settingsController.SetShortcutSettings(current with { Actions = actions });
    NavigateToSettingsPage(SettingsPage.Shortcuts);
}

private void DeleteShortcutAction(string actionId)
{
    var current = settingsController.Current.Shortcuts ?? ShortcutSettings.Default;
    var actions = current.Actions.Where(action => action.Id != actionId).ToImmutableArray();
    settingsController.SetShortcutSettings(current with { Actions = actions });
    NavigateToSettingsPage(SettingsPage.Shortcuts);
}
```

Add `using System.Collections.Immutable;`.

- [ ] **Step 5: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\Shortcuts\ShortcutSettingsControls.cs src\OpenHab.Windows.Tray\Settings\SettingsPageControl.xaml.cs
git commit -m "feat: edit shortcut actions in settings"
```

## Task 8: Radial Command Menu Window

**Files:**
- Create: `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs`

- [ ] **Step 1: Create radial menu window**

Create `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Shortcuts;
using Windows.System;

namespace OpenHab.Windows.Tray.Shortcuts;

internal sealed class RadialCommandMenuWindow : Window
{
    private readonly Grid root = new();
    private IReadOnlyList<ShortcutAction> actions = [];
    private Func<ShortcutAction, Task>? executeAction;
    private int selectedIndex;

    public RadialCommandMenuWindow()
    {
        Title = "openHAB Command Menu";
        Content = root;
        root.Width = 360;
        root.Height = 360;
        root.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        root.KeyDown += OnKeyDown;
    }

    public void ShowActions(IReadOnlyList<ShortcutAction> commandMenuActions, Func<ShortcutAction, Task> execute)
    {
        actions = commandMenuActions;
        executeAction = execute;
        selectedIndex = 0;
        Render();
        Activate();
    }

    public void CloseMenu()
    {
        AppWindow.Hide();
    }

    private void Render()
    {
        root.Children.Clear();

        if (actions.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "No command menu actions",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        var canvas = new Canvas();
        root.Children.Add(canvas);
        var centerX = 180d;
        var centerY = 180d;
        var radius = 120d;

        var close = new Button
        {
            Content = "Close",
            Width = 72,
            Height = 72
        };
        close.Click += (_, _) => CloseMenu();
        Canvas.SetLeft(close, centerX - 36);
        Canvas.SetTop(close, centerY - 36);
        canvas.Children.Add(close);

        for (var i = 0; i < actions.Count; i++)
        {
            var angle = (Math.PI * 2 * i / actions.Count) - Math.PI / 2;
            var action = actions[i];
            var button = new Button
            {
                Content = action.Name,
                Width = 92,
                Height = 48,
                Tag = action,
                BorderThickness = i == selectedIndex ? new Thickness(2) : new Thickness(1)
            };
            button.Click += async (_, _) => await ExecuteAndCloseAsync(action);
            Canvas.SetLeft(button, centerX + Math.Cos(angle) * radius - 46);
            Canvas.SetTop(button, centerY + Math.Sin(angle) * radius - 24);
            canvas.Children.Add(button);
        }
    }

    private async Task ExecuteAndCloseAsync(ShortcutAction action)
    {
        if (executeAction is not null)
        {
            await executeAction(action);
        }
        CloseMenu();
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            CloseMenu();
            e.Handled = true;
            return;
        }

        if (actions.Count == 0)
        {
            return;
        }

        if (e.Key is VirtualKey.Left or VirtualKey.Up)
        {
            selectedIndex = (selectedIndex - 1 + actions.Count) % actions.Count;
            Render();
            e.Handled = true;
            return;
        }

        if (e.Key is VirtualKey.Right or VirtualKey.Down)
        {
            selectedIndex = (selectedIndex + 1) % actions.Count;
            Render();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            await ExecuteAndCloseAsync(actions[selectedIndex]);
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 2: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\Shortcuts\RadialCommandMenuWindow.cs
git commit -m "feat: add radial command menu window"
```

## Task 9: Global Hotkey Service

**Files:**
- Create: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutWindowsMapper.cs`
- Create: `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs`

- [ ] **Step 1: Create Win32 shortcut mapper**

Create `src/OpenHab.Windows.Tray/Shortcuts/ShortcutWindowsMapper.cs`:

```csharp
using OpenHab.App.Shortcuts;
using Windows.System;

namespace OpenHab.Windows.Tray.Shortcuts;

internal static class ShortcutWindowsMapper
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static bool TryMap(ShortcutBinding binding, out uint modifiers, out uint key)
    {
        var normalized = ShortcutBindingFormatter.Normalize(binding);
        modifiers = 0;
        key = 0;

        foreach (var modifier in normalized.Modifiers)
        {
            modifiers |= modifier switch
            {
                ShortcutModifier.Win => ModWin,
                ShortcutModifier.Ctrl => ModControl,
                ShortcutModifier.Alt => ModAlt,
                ShortcutModifier.Shift => ModShift,
                _ => 0
            };
        }

        if (Enum.TryParse<VirtualKey>(normalized.Key, ignoreCase: true, out var virtualKey))
        {
            key = (uint)virtualKey;
            return true;
        }

        if (normalized.Key.Length == 1)
        {
            key = char.ToUpperInvariant(normalized.Key[0]);
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 2: Create hotkey service**

Create `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenHab.App.Shortcuts;
using WinRT.Interop;

namespace OpenHab.Windows.Tray.Shortcuts;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly Window window;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly Dictionary<int, ShortcutAction?> registrations = new();
    private IntPtr hwnd;
    private int nextId = 100;

    public event EventHandler? CommandMenuRequested;
    public event EventHandler<ShortcutAction>? ActionRequested;

    public GlobalHotkeyService(Window window, DispatcherQueue dispatcherQueue)
    {
        this.window = window;
        this.dispatcherQueue = dispatcherQueue;
        hwnd = WindowNative.GetWindowHandle(window);
        var source = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _ = source;
    }

    public IReadOnlyDictionary<string, string> Refresh(ShortcutSettings settings)
    {
        Clear();
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);

        if (settings.CommandMenu.Enabled && settings.CommandMenu.Binding is not null)
        {
            Register("Command Menu", settings.CommandMenu.Binding, null, failures);
        }

        foreach (var action in settings.Actions.Where(static action => action.GlobalShortcut is not null))
        {
            Register(action.Name, action.GlobalShortcut!, action, failures);
        }

        return failures;
    }

    public void HandleHotkeyMessage(int id)
    {
        if (!registrations.TryGetValue(id, out var action))
        {
            return;
        }

        dispatcherQueue.TryEnqueue(() =>
        {
            if (action is null)
            {
                CommandMenuRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ActionRequested?.Invoke(this, action);
            }
        });
    }

    public void Dispose()
    {
        Clear();
    }

    private void Register(
        string ownerName,
        ShortcutBinding binding,
        ShortcutAction? action,
        Dictionary<string, string> failures)
    {
        if (!ShortcutWindowsMapper.TryMap(binding, out var modifiers, out var key))
        {
            failures[ownerName] = "Shortcut could not be mapped to a Windows virtual key.";
            return;
        }

        var id = nextId++;
        if (!RegisterHotKey(hwnd, id, modifiers, key))
        {
            failures[ownerName] = "This shortcut could not be registered. It may already be used by Windows or another app.";
            return;
        }

        registrations[id] = action;
    }

    private void Clear()
    {
        foreach (var id in registrations.Keys.ToArray())
        {
            UnregisterHotKey(hwnd, id);
        }
        registrations.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
```

- [ ] **Step 3: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes. If the build shows that WinUI message interception is missing, continue to Task 10 before committing this task.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\Shortcuts\ShortcutWindowsMapper.cs src\OpenHab.Windows.Tray\Shortcuts\GlobalHotkeyService.cs
git commit -m "feat: register global shortcut hotkeys"
```

## Task 10: Wire App Startup, Connection Gating, And Radial Menu

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Add fields**

Add to `App.xaml.cs`:

```csharp
private ShortcutActionExecutor? shortcutActionExecutor;
private Shortcuts.GlobalHotkeyService? globalHotkeyService;
private Shortcuts.RadialCommandMenuWindow? radialCommandMenuWindow;
```

Add usings:

```csharp
using OpenHab.App.Shortcuts;
using OpenHab.Windows.Tray.Shortcuts;
```

- [ ] **Step 2: Add active client resolver**

Add this method to `App.xaml.cs`:

```csharp
private IOpenHabClient? CreateActiveShortcutClient()
{
    if (settingsController is null || runtimeController is null || httpClient is null)
    {
        return null;
    }

    var activeTransport = runtimeController.Current.ActiveTransport;
    if (activeTransport is null)
    {
        return null;
    }

    var endpoint = activeTransport == TransportKind.Local
        ? settingsController.Current.LocalEndpoint
        : settingsController.Current.CloudEndpoint;
    var auth = ResolveRuntimeAuthSync(settingsController, activeTransport.Value);
    return new OpenHabHttpClient(
        httpClient,
        endpoint,
        apiToken: auth.ApiToken,
        basicUserName: auth.BasicUserName,
        basicPassword: auth.BasicPassword);
}
```

- [ ] **Step 3: Construct executor and radial menu after main window creation**

In `OnLaunched`, after `mainWindow` is created:

```csharp
shortcutActionExecutor = new ShortcutActionExecutor(
    CreateActiveShortcutClient,
    () => runtimeController?.Current.ConnectionState ?? ConnectionState.Offline);
radialCommandMenuWindow = new RadialCommandMenuWindow();
```

- [ ] **Step 4: Wire hotkey service after `trayIcon` creation**

Add:

```csharp
globalHotkeyService = new GlobalHotkeyService(mainWindow, DispatcherQueue.GetForCurrentThread());
globalHotkeyService.CommandMenuRequested += (_, _) => OpenShortcutCommandMenuAsync();
globalHotkeyService.ActionRequested += (_, action) => _ = ExecuteShortcutActionAsync(action);
RefreshShortcutHotkeys();
settingsController.SettingsChanged += (_, _) => RefreshShortcutHotkeys();
```

Add methods:

```csharp
private void RefreshShortcutHotkeys()
{
    if (settingsController is null || globalHotkeyService is null)
    {
        return;
    }

    var settings = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
    var failures = globalHotkeyService.Refresh(settings);
    foreach (var failure in failures)
    {
        DiagnosticLogger.Warn($"Shortcut registration failed for {failure.Key}: {failure.Value}");
    }
}

private void OpenShortcutCommandMenuAsync()
{
    if (runtimeController?.Current.ConnectionState != ConnectionState.Online)
    {
        DiagnosticLogger.Info("Shortcut command menu ignored because openHAB is disconnected");
        return;
    }

    if (settingsController is null || radialCommandMenuWindow is null)
    {
        return;
    }

    var actions = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Actions
        .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid)
        .ToArray();
    radialCommandMenuWindow.ShowActions(actions, ExecuteShortcutActionAsync);
}

private async Task ExecuteShortcutActionAsync(ShortcutAction action)
{
    if (shortcutActionExecutor is null)
    {
        return;
    }

    var result = await shortcutActionExecutor.ExecuteAsync(action);
    if (!result.Succeeded)
    {
        DiagnosticLogger.Warn($"Shortcut action failed: {result.Failure}: {result.Message}");
    }
}
```

- [ ] **Step 5: Dispose hotkey service**

In shutdown/dispose path where existing services are cleaned up, add:

```csharp
globalHotkeyService?.Dispose();
globalHotkeyService = null;
```

- [ ] **Step 6: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\App.xaml.cs
git commit -m "feat: wire shortcut command menu runtime"
```

## Task 11: Windows Message Hook For Hotkeys

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs`

- [ ] **Step 1: Add Win32 message hook**

Modify `GlobalHotkeyService` to store a subclass delegate:

```csharp
private readonly SubclassProc subclassProc;
```

In constructor, after `hwnd` assignment:

```csharp
subclassProc = WindowProc;
SetWindowSubclass(hwnd, subclassProc, 1, IntPtr.Zero);
```

Add this method:

```csharp
private IntPtr WindowProc(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr refData)
{
    if (message == WmHotkey)
    {
        HandleHotkeyMessage((int)wParam);
        return IntPtr.Zero;
    }

    return DefSubclassProc(hWnd, message, wParam, lParam);
}
```

In `Dispose`, before `Clear()`:

```csharp
RemoveWindowSubclass(hwnd, subclassProc, 1);
```

Add interop:

```csharp
private delegate IntPtr SubclassProc(
    IntPtr hWnd,
    uint message,
    UIntPtr wParam,
    IntPtr lParam,
    UIntPtr subclassId,
    IntPtr refData);

[DllImport("comctl32.dll", SetLastError = true)]
private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

[DllImport("comctl32.dll", SetLastError = true)]
private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

[DllImport("comctl32.dll")]
private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam);
```

- [ ] **Step 2: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\Shortcuts\GlobalHotkeyService.cs
git commit -m "feat: handle global hotkey messages"
```

## Task 12: Connected-State Menu Closing And Status Feedback

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs`

- [ ] **Step 1: Close radial menu on connection loss**

After runtime controller construction in `App.xaml.cs`, subscribe:

```csharp
runtimeController.SnapshotChanged += (_, _) =>
{
    if (runtimeController.Current.ConnectionState != ConnectionState.Online)
    {
        radialCommandMenuWindow?.CloseMenu();
    }
};
```

- [ ] **Step 2: Improve failure status**

In `ExecuteShortcutActionAsync`, replace the warning-only body with:

```csharp
if (!result.Succeeded)
{
    DiagnosticLogger.Warn($"Shortcut action failed: {result.Failure}: {result.Message}");
    mainWindow?.SetShellStatusText(result.Message);
}
```

If `SetShellStatusText` is private, add this public method to `MainWindow.xaml.cs`:

```csharp
public void SetShellStatusText(string text)
{
    StatusText.Text = text;
}
```

- [ ] **Step 3: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\App.xaml.cs src\OpenHab.Windows.Tray\MainWindow.xaml.cs src\OpenHab.Windows.Tray\Shortcuts\RadialCommandMenuWindow.cs
git commit -m "feat: gate shortcuts on openhab connection"
```

## Task 13: Focus, Accessibility, And Visual Polish

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs`
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Add accessible names**

In `ShortcutRecorderControl.Refresh`, set automation names:

```csharp
Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(edit, "Edit shortcut");
Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(clear, "Clear shortcut");
```

In `RadialCommandMenuWindow.Render`, set names:

```csharp
Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Close command menu");
Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Run {action.Name}");
```

In settings built-in cards, set:

```csharp
Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(enabledToggle, "Enable openHAB command menu shortcut");
Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(modeCombo, "Command menu activation mode");
```

- [ ] **Step 2: Ensure keyboard tab stops**

Set on radial action buttons:

```csharp
IsTabStop = true
```

Set on recorder:

```csharp
UseSystemFocusVisuals = true
```

- [ ] **Step 3: Build and commit**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug
```

Expected: build passes.

Commit:

```powershell
git add src\OpenHab.Windows.Tray\Shortcuts src\OpenHab.Windows.Tray\Settings\SettingsPageControl.xaml.cs
git commit -m "feat: polish shortcuts accessibility"
```

## Task 14: Direct Test Gate

**Files:**
- No source edits unless tests expose defects.

- [ ] **Step 1: Run direct test gate**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all direct test projects pass.

- [ ] **Step 2: Run tray build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Expected: build passes.

- [ ] **Step 3: Commit verification fixes if needed**

If a test or build fails because of the shortcut changes, fix the smallest defect, rerun the failing command, then commit:

```powershell
git add <changed-files>
git commit -m "fix: stabilize shortcuts verification"
```

If no fixes are needed, do not create an empty commit.

## Manual Verification Checklist

Run the app from Visual Studio or `dotnet run` if the local environment supports WinUI launch.

- [ ] Open Settings > Shortcuts.
- [ ] Verify `openHAB Command Menu` defaults to enabled `Win + O`.
- [ ] Verify activation mode defaults to `Toggle`.
- [ ] Verify Voice Mode is disabled, `Coming soon`, and `Unassigned`.
- [ ] Add a light action with `light-bulb` icon and `ON` command.
- [ ] Add a media action with `play` icon and `PLAY` command.
- [ ] Verify duplicate shortcuts are rejected.
- [ ] Connect to openHAB and press `Win + O`.
- [ ] Verify radial menu opens only when connected.
- [ ] Disconnect or force offline and press `Win + O`.
- [ ] Verify radial menu does not open while disconnected.
- [ ] Verify direct action shortcut does not execute while disconnected.

