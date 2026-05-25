# openHAB Windows Voice Command Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in Android-style voice commands that recognize one spoken phrase on Windows and send it to a configurable openHAB item.

**Architecture:** Voice mode is modeled as a master shortcut setting plus a protected default `Voice` shortcut action. UI-independent shortcut normalization, validation, protected-action behavior, and command dispatch live in `OpenHab.App`; Windows speech recognition, flyout activation, hotkeys, confirmation UI, and manifest capability live in `OpenHab.Windows.Tray`.

**Tech Stack:** .NET 10, WinUI/Windows App SDK, `Windows.Media.SpeechRecognition.SpeechRecognizer`, existing openHAB REST command client, xUnit.

---

## File Structure

- `src/OpenHab.App/Shortcuts/ShortcutAction.cs` - add `ShortcutCommandType.Voice`.
- `src/OpenHab.App/Shortcuts/ShortcutSettings.cs` - replace locked Voice Mode behavior with real normalized voice settings.
- `src/OpenHab.App/Shortcuts/VoiceShortcutPolicy.cs` - central defaults and protected default Voice action rules.
- `src/OpenHab.App/Shortcuts/ShortcutValidation.cs` - allow Voice-specific validation rules.
- `src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs` - preserve protected Voice action from deletion.
- `src/OpenHab.App/Shortcuts/VoiceCommandExecutor.cs` - UI-independent execution of recognized phrases to openHAB item commands.
- `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs` - voice settings/default action persistence and migration coverage.
- `tests/OpenHab.App.Tests/Shortcuts/ShortcutValidationTests.cs` - Voice validation coverage.
- `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs` - protected action deletion coverage.
- `tests/OpenHab.App.Tests/Shortcuts/VoiceCommandExecutorTests.cs` - send/empty/disconnected/error diagnostics coverage.
- `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs` - app-owned English strings for Voice settings, action type, status, and errors.
- `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw` and `src/OpenHab.Windows.Tray/Strings/pl-PL/Resources.resw` - matching resource keys.
- `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs` - replace Voice stub with controls, add Voice command type, protect delete UI, and hide command value for Voice.
- `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs` - register Voice action hotkeys only while Voice Mode is enabled.
- `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs` - accept already-gated action list; keep internal validation.
- `src/OpenHab.Windows.Tray/FlyoutWindow.xaml` and `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` - add conditional microphone button between Search and Minimize.
- `src/OpenHab.Windows.Tray/Voice/VoiceRecognitionResult.cs` - Windows tray recognition result model.
- `src/OpenHab.Windows.Tray/Voice/WindowsSpeechVoiceRecognitionService.cs` - wrapper around `SpeechRecognizer`.
- `src/OpenHab.Windows.Tray/Voice/VoiceCommandConfirmationWindow.cs` - compact transcript confirmation surface.
- `src/OpenHab.Windows.Tray/App.xaml.cs` - coordinate voice activation from flyout, hotkey, and radial command menu.
- `src/OpenHab.Windows.Tray/Package.appxmanifest` - add microphone capability.

## Task 1: Voice Settings Model And Protected Default Action

**Files:**
- Modify: `src/OpenHab.App/Shortcuts/ShortcutAction.cs`
- Modify: `src/OpenHab.App/Shortcuts/ShortcutSettings.cs`
- Create: `src/OpenHab.App/Shortcuts/VoiceShortcutPolicy.cs`
- Modify: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`

- [ ] **Step 1: Write failing settings tests**

Append these tests to `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs` inside `AppSettingsControllerTests`:

```csharp
[Fact]
public void VoiceModeCanBeEnabledAndCreatesProtectedDefaultVoiceAction()
{
    var controller = CreateController();
    var updated = (controller.Current.Shortcuts ?? ShortcutSettings.Default).Normalized() with
    {
        VoiceMode = new VoiceModeShortcutSettings(Enabled: true, RequireConfirmationBeforeSending: false)
    };

    controller.SetShortcutSettings(updated);

    var shortcuts = AssertShortcuts(controller.Current);
    Assert.True(shortcuts.VoiceMode.Enabled);
    Assert.False(shortcuts.VoiceMode.RequireConfirmationBeforeSending);
    var voice = Assert.Single(shortcuts.Actions.Where(VoiceShortcutPolicy.IsProtectedDefaultVoiceAction));
    Assert.Equal(ShortcutCommandType.Voice, voice.CommandType);
    Assert.Equal("Voice command", voice.Name);
    Assert.Equal("microphone", voice.IconId);
    Assert.Equal("VoiceCommand", voice.TargetItem);
    Assert.Null(voice.CommandValue);
    Assert.True(voice.ShowInCommandMenu);
    Assert.Equal("Ctrl + Alt + I", ShortcutBindingFormatter.Format(voice.GlobalShortcut));
}

[Fact]
public void ReEnablingVoiceModePreservesEditedProtectedVoiceAction()
{
    var controller = CreateController();
    var enabled = (controller.Current.Shortcuts ?? ShortcutSettings.Default).Normalized() with
    {
        VoiceMode = new VoiceModeShortcutSettings(Enabled: true, RequireConfirmationBeforeSending: true)
    };
    controller.SetShortcutSettings(enabled);
    var protectedVoice = AssertShortcuts(controller.Current).Actions.Single(VoiceShortcutPolicy.IsProtectedDefaultVoiceAction);
    var edited = protectedVoice with
    {
        Name = "House voice",
        TargetItem = "MyVoiceItem",
        ShowInCommandMenu = false,
        GlobalShortcut = null
    };

    controller.SetShortcutSettings(AssertShortcuts(controller.Current) with
    {
        VoiceMode = new VoiceModeShortcutSettings(Enabled: false, RequireConfirmationBeforeSending: true),
        Actions = [edited]
    });
    controller.SetShortcutSettings(AssertShortcuts(controller.Current) with
    {
        VoiceMode = new VoiceModeShortcutSettings(Enabled: true, RequireConfirmationBeforeSending: true)
    });

    var shortcuts = AssertShortcuts(controller.Current);
    Assert.True(shortcuts.VoiceMode.Enabled);
    Assert.True(shortcuts.VoiceMode.RequireConfirmationBeforeSending);
    var voice = Assert.Single(shortcuts.Actions.Where(VoiceShortcutPolicy.IsProtectedDefaultVoiceAction));
    Assert.Equal("House voice", voice.Name);
    Assert.Equal("MyVoiceItem", voice.TargetItem);
    Assert.False(voice.ShowInCommandMenu);
    Assert.Null(voice.GlobalShortcut);
}

[Fact]
public void LegacyVoiceModeShortcutObjectLoadsAsDisabledVoiceModeSettings()
{
    Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
    File.WriteAllText(settingsFilePath, """
        {
          "Skin": 1,
          "EndpointMode": 0,
          "LocalEndpoint": "http://openhab:8080/",
          "CloudEndpoint": "https://myopenhab.org/",
          "SitemapName": "home",
          "Shortcuts": {
            "CommandMenu": {
              "Enabled": true,
              "Binding": { "Modifiers": [ 0 ], "Key": "O" },
              "RadialActivationMode": 0
            },
            "VoiceMode": {
              "Enabled": true,
              "Binding": { "Modifiers": [ 1, 2 ], "Key": "I" },
              "RadialActivationMode": 0
            },
            "Actions": []
          }
        }
        """);

    var controller = CreateController();
    var shortcuts = AssertShortcuts(controller.Current);

    Assert.True(shortcuts.VoiceMode.Enabled);
    Assert.False(shortcuts.VoiceMode.RequireConfirmationBeforeSending);
    Assert.Single(shortcuts.Actions.Where(VoiceShortcutPolicy.IsProtectedDefaultVoiceAction));
}
```

Replace the old `ShortcutSettingsNormalizationForcesVoiceModeDisabledAndUnassigned` test with this test:

```csharp
[Fact]
public void ShortcutSettingsNormalizationKeepsVoiceModeEnabledState()
{
    var controller = CreateController();
    var settings = ShortcutSettings.Default with
    {
        VoiceMode = new VoiceModeShortcutSettings(Enabled: true, RequireConfirmationBeforeSending: true)
    };

    controller.SetShortcutSettings(settings);
    var shortcuts = AssertShortcuts(controller.Current);

    Assert.True(shortcuts.VoiceMode.Enabled);
    Assert.True(shortcuts.VoiceMode.RequireConfirmationBeforeSending);
    Assert.Single(shortcuts.Actions.Where(VoiceShortcutPolicy.IsProtectedDefaultVoiceAction));
}
```

- [ ] **Step 2: Run settings tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~AppSettingsControllerTests" --logger "console;verbosity=minimal"
```

Expected: compile fails because `VoiceModeShortcutSettings`, `VoiceShortcutPolicy`, and `ShortcutCommandType.Voice` do not exist.

- [ ] **Step 3: Add Voice command type**

Replace `src/OpenHab.App/Shortcuts/ShortcutAction.cs` with:

```csharp
namespace OpenHab.App.Shortcuts;

public enum ShortcutCommandType
{
    Toggle,
    OnOff,
    OpenClose,
    OpenSlider,
    OpenColorPicker,
    SendCommand,
    Voice
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

- [ ] **Step 4: Add voice shortcut policy**

Create `src/OpenHab.App/Shortcuts/VoiceShortcutPolicy.cs`:

```csharp
using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public static class VoiceShortcutPolicy
{
    public const string ProtectedDefaultVoiceActionId = "built-in.voice.default";
    public const string DefaultVoiceActionName = "Voice command";
    public const string DefaultVoiceActionIconId = "microphone";
    public const string DefaultVoiceTargetItem = "VoiceCommand";

    public static ShortcutBinding DefaultVoiceShortcut { get; } =
        new([ShortcutModifier.Ctrl, ShortcutModifier.Alt], "I");

    public static ShortcutAction CreateDefaultVoiceAction()
    {
        return new ShortcutAction(
            ProtectedDefaultVoiceActionId,
            DefaultVoiceActionName,
            DefaultVoiceActionIconId,
            ShowInCommandMenu: true,
            GlobalShortcut: DefaultVoiceShortcut,
            TargetItem: DefaultVoiceTargetItem,
            CommandType: ShortcutCommandType.Voice,
            CommandValue: null);
    }

    public static bool IsProtectedDefaultVoiceAction(ShortcutAction? action)
    {
        return action is not null && IsProtectedDefaultVoiceAction(action.Id);
    }

    public static bool IsProtectedDefaultVoiceAction(string? actionId)
    {
        return string.Equals(actionId, ProtectedDefaultVoiceActionId, StringComparison.Ordinal);
    }

    public static ImmutableArray<ShortcutAction> EnsureProtectedDefaultVoiceAction(IEnumerable<ShortcutAction> actions)
    {
        var normalizedActions = actions.ToImmutableArray();
        if (normalizedActions.Any(IsProtectedDefaultVoiceAction))
        {
            return normalizedActions;
        }

        return normalizedActions.Insert(0, CreateDefaultVoiceAction());
    }
}
```

- [ ] **Step 5: Replace ShortcutSettings voice model**

Replace `src/OpenHab.App/Shortcuts/ShortcutSettings.cs` with:

```csharp
using System.Collections.Immutable;

namespace OpenHab.App.Shortcuts;

public sealed record BuiltInShortcutSettings(
    bool Enabled,
    ShortcutBinding? Binding,
    RadialActivationMode RadialActivationMode = RadialActivationMode.Toggle);

public sealed record VoiceModeShortcutSettings(
    bool Enabled,
    bool RequireConfirmationBeforeSending = false);

public sealed record ShortcutSettings(
    BuiltInShortcutSettings CommandMenu,
    VoiceModeShortcutSettings VoiceMode,
    ImmutableArray<ShortcutAction> Actions)
{
    public static ShortcutSettings Default { get; } = new(
        new BuiltInShortcutSettings(
            Enabled: true,
            Binding: new ShortcutBinding([ShortcutModifier.Win], "O"),
            RadialActivationMode: RadialActivationMode.Toggle),
        new VoiceModeShortcutSettings(
            Enabled: false,
            RequireConfirmationBeforeSending: false),
        []);

    public ShortcutSettings Normalized()
    {
        var commandMenu = CommandMenu ?? Default.CommandMenu;
        var voiceMode = VoiceMode ?? Default.VoiceMode;

        var commandMenuBinding = ShortcutBindingFormatter.TryNormalize(commandMenu.Binding, out var normalizedCommandMenuBinding)
            ? normalizedCommandMenuBinding
            : Default.CommandMenu.Binding;

        var commandMenuMode = Enum.IsDefined(commandMenu.RadialActivationMode)
            ? commandMenu.RadialActivationMode
            : RadialActivationMode.Toggle;

        var actions = Actions.IsDefault
            ? []
            : Actions
                .Where(static action => action is not null && !string.IsNullOrWhiteSpace(action.Id))
                .Select(static action =>
                {
                    var commandType = Enum.IsDefined(action!.CommandType)
                        ? action.CommandType
                        : ShortcutCommandType.SendCommand;

                    return action with
                    {
                        Id = action.Id.Trim(),
                        Name = string.IsNullOrWhiteSpace(action.Name) ? "Unnamed action" : action.Name.Trim(),
                        IconId = string.IsNullOrWhiteSpace(action.IconId) ? "custom" : action.IconId.Trim(),
                        GlobalShortcut = ShortcutBindingFormatter.TryNormalize(action.GlobalShortcut, out var normalizedShortcut) ? normalizedShortcut : null,
                        TargetItem = action.TargetItem?.Trim() ?? string.Empty,
                        CommandType = commandType,
                        CommandValue = commandType == ShortcutCommandType.Voice || string.IsNullOrWhiteSpace(action.CommandValue)
                            ? null
                            : action.CommandValue.Trim()
                    };
                })
                .ToImmutableArray();

        if (voiceMode.Enabled)
        {
            actions = VoiceShortcutPolicy.EnsureProtectedDefaultVoiceAction(actions);
        }

        return new ShortcutSettings(
            commandMenu with
            {
                Binding = commandMenuBinding,
                RadialActivationMode = commandMenuMode
            },
            voiceMode with
            {
                RequireConfirmationBeforeSending = voiceMode.RequireConfirmationBeforeSending
            },
            actions);
    }
}
```

- [ ] **Step 6: Add microphone icon to shortcut catalog**

In `src/OpenHab.App/Shortcuts/ShortcutIconCatalog.cs`, add this item to the media group after `volume`:

```csharp
new("microphone", "Microphone", MediaGroup),
```

- [ ] **Step 7: Run settings tests and verify they pass**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~AppSettingsControllerTests|FullyQualifiedName~ShortcutValidationTests" --logger "console;verbosity=minimal"
```

Expected: tests pass. If old assertions still expect disabled Voice Mode, update those assertions to the new disabled-default shape:

```csharp
Assert.False(shortcuts.VoiceMode.Enabled);
Assert.False(shortcuts.VoiceMode.RequireConfirmationBeforeSending);
```

- [ ] **Step 8: Commit model changes**

Run:

```powershell
git add src/OpenHab.App/Shortcuts tests/OpenHab.App.Tests/AppSettingsControllerTests.cs
git commit -m "feat: model voice shortcut settings"
```

## Task 2: Voice Validation And Protected Action Editing

**Files:**
- Modify: `src/OpenHab.App/Shortcuts/ShortcutValidation.cs`
- Modify: `src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs`
- Modify: `tests/OpenHab.App.Tests/Shortcuts/ShortcutValidationTests.cs`
- Modify: `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs`

- [ ] **Step 1: Write failing validation tests**

Append these tests to `tests/OpenHab.App.Tests/Shortcuts/ShortcutValidationTests.cs`:

```csharp
[Fact]
public void VoiceActionDoesNotRequireCommandValue()
{
    var action = new ShortcutAction(
        "voice",
        "Voice",
        "microphone",
        ShowInCommandMenu: true,
        GlobalShortcut: null,
        TargetItem: "VoiceCommand",
        CommandType: ShortcutCommandType.Voice,
        CommandValue: null);

    var result = ShortcutValidation.ValidateAction(action);

    Assert.True(result.IsValid);
}

[Fact]
public void VoiceActionMayBeHiddenAndUnassigned()
{
    var action = new ShortcutAction(
        "voice",
        "Voice",
        "microphone",
        ShowInCommandMenu: false,
        GlobalShortcut: null,
        TargetItem: "VoiceCommand",
        CommandType: ShortcutCommandType.Voice,
        CommandValue: null);

    var result = ShortcutValidation.ValidateAction(action);

    Assert.True(result.IsValid);
}

[Fact]
public void VoiceActionStillRequiresTargetItem()
{
    var action = new ShortcutAction(
        "voice",
        "Voice",
        "microphone",
        ShowInCommandMenu: false,
        GlobalShortcut: null,
        TargetItem: " ",
        CommandType: ShortcutCommandType.Voice,
        CommandValue: null);

    var result = ShortcutValidation.ValidateAction(action);

    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, error => error.Contains("target item", StringComparison.OrdinalIgnoreCase));
}
```

Append this test to `tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs`:

```csharp
[Fact]
public void RemoveActionPreservesProtectedDefaultVoiceAction()
{
    var planner = new ShortcutActionEditorPlanner();
    var protectedVoice = VoiceShortcutPolicy.CreateDefaultVoiceAction();

    var result = planner.RemoveAction([protectedVoice, Action("a2", "Second")], VoiceShortcutPolicy.ProtectedDefaultVoiceActionId);

    Assert.Equal(2, result.Length);
    Assert.Contains(result, VoiceShortcutPolicy.IsProtectedDefaultVoiceAction);
}
```

- [ ] **Step 2: Run shortcut tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~ShortcutValidationTests|FullyQualifiedName~ShortcutActionEditorPlannerTests" --logger "console;verbosity=minimal"
```

Expected: `VoiceActionMayBeHiddenAndUnassigned` and `RemoveActionPreservesProtectedDefaultVoiceAction` fail.

- [ ] **Step 3: Implement Voice validation rule**

In `src/OpenHab.App/Shortcuts/ShortcutValidation.cs`, replace:

```csharp
if (!action.ShowInCommandMenu && action.GlobalShortcut is null)
{
    errors.Add(text.Get("Shortcuts.Validation.ActionAvailabilityRequired"));
}
```

with:

```csharp
if (!action.ShowInCommandMenu && action.GlobalShortcut is null && action.CommandType != ShortcutCommandType.Voice)
{
    errors.Add(text.Get("Shortcuts.Validation.ActionAvailabilityRequired"));
}
```

No `case ShortcutCommandType.Voice` is needed in the command-value switch because `CommandValue` is ignored for Voice actions.

- [ ] **Step 4: Protect default Voice action from deletion**

In `src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs`, replace `RemoveAction` with:

```csharp
public ImmutableArray<ShortcutAction> RemoveAction(IEnumerable<ShortcutAction> actions, string actionId)
{
    var snapshot = actions.ToImmutableArray();
    if (VoiceShortcutPolicy.IsProtectedDefaultVoiceAction(actionId))
    {
        return snapshot;
    }

    return snapshot
        .Where(action => !string.Equals(action.Id, actionId, StringComparison.Ordinal))
        .ToImmutableArray();
}
```

- [ ] **Step 5: Run shortcut tests and verify they pass**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~ShortcutValidationTests|FullyQualifiedName~ShortcutActionEditorPlannerTests" --logger "console;verbosity=minimal"
```

Expected: tests pass.

- [ ] **Step 6: Commit validation changes**

Run:

```powershell
git add src/OpenHab.App/Shortcuts/ShortcutValidation.cs src/OpenHab.App/Shortcuts/ShortcutActionEditorPlanner.cs tests/OpenHab.App.Tests/Shortcuts/ShortcutValidationTests.cs tests/OpenHab.App.Tests/Shortcuts/ShortcutActionEditorPlannerTests.cs
git commit -m "feat: validate voice shortcut actions"
```

## Task 3: UI-Independent Voice Command Execution

**Files:**
- Create: `src/OpenHab.App/Shortcuts/VoiceCommandExecutor.cs`
- Create: `tests/OpenHab.App.Tests/Shortcuts/VoiceCommandExecutorTests.cs`

- [ ] **Step 1: Write failing executor tests**

Create `tests/OpenHab.App.Tests/Shortcuts/VoiceCommandExecutorTests.cs`:

```csharp
using OpenHab.App.Runtime;
using OpenHab.App.Shortcuts;
using OpenHab.Core.Api;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.Shortcuts;

public sealed class VoiceCommandExecutorTests
{
    [Fact]
    public async Task SendsRecognizedPhraseToVoiceTargetItem()
    {
        var client = new RecordingOpenHabClient();
        var executor = new VoiceCommandExecutor(() => client, () => ConnectionState.Online, _ => { });
        var action = VoiceShortcutPolicy.CreateDefaultVoiceAction() with { TargetItem = "HouseVoice" };

        var result = await executor.ExecuteAsync(action, "turn kitchen on", logPhrase: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(("HouseVoice", "turn kitchen on"), Assert.Single(client.Commands));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyPhraseDoesNotSend(string phrase)
    {
        var client = new RecordingOpenHabClient();
        var executor = new VoiceCommandExecutor(() => client, () => ConnectionState.Online, _ => { });

        var result = await executor.ExecuteAsync(VoiceShortcutPolicy.CreateDefaultVoiceAction(), phrase, logPhrase: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.EmptyPhrase, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task DisconnectedDoesNotCreateClient()
    {
        var clientCreated = false;
        var executor = new VoiceCommandExecutor(
            () =>
            {
                clientCreated = true;
                return new RecordingOpenHabClient();
            },
            () => ConnectionState.Offline,
            _ => { });

        var result = await executor.ExecuteAsync(VoiceShortcutPolicy.CreateDefaultVoiceAction(), "lights", logPhrase: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.Disconnected, result.Failure);
        Assert.False(clientCreated);
    }

    [Fact]
    public async Task InvalidNonVoiceActionDoesNotSend()
    {
        var client = new RecordingOpenHabClient();
        var executor = new VoiceCommandExecutor(() => client, () => ConnectionState.Online, _ => { });
        var action = new ShortcutAction("a1", "Not voice", "custom", true, null, "Item", ShortcutCommandType.Toggle, null);

        var result = await executor.ExecuteAsync(action, "lights", logPhrase: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VoiceCommandExecutionFailure.InvalidAction, result.Failure);
        Assert.Empty(client.Commands);
    }

    [Fact]
    public async Task NormalDiagnosticsDoNotLogPhrase()
    {
        var log = new List<string>();
        var client = new RecordingOpenHabClient();
        var executor = new VoiceCommandExecutor(() => client, () => ConnectionState.Online, log.Add);

        await executor.ExecuteAsync(VoiceShortcutPolicy.CreateDefaultVoiceAction(), "secret phrase", logPhrase: false, CancellationToken.None);

        Assert.DoesNotContain(log, entry => entry.Contains("secret phrase", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerboseDiagnosticsCanLogPhrase()
    {
        var log = new List<string>();
        var client = new RecordingOpenHabClient();
        var executor = new VoiceCommandExecutor(() => client, () => ConnectionState.Online, log.Add);

        await executor.ExecuteAsync(VoiceShortcutPolicy.CreateDefaultVoiceAction(), "secret phrase", logPhrase: true, CancellationToken.None);

        Assert.Contains(log, entry => entry.Contains("secret phrase", StringComparison.Ordinal));
    }

    private sealed class RecordingOpenHabClient : IOpenHabClient
    {
        public List<(string ItemName, string Command)> Commands { get; } = new();

        public Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken)
        {
            Commands.Add((itemName, command));
            return Task.CompletedTask;
        }

        public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken) => Task.FromResult("{}");
        public Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OpenHabItemSummary>>([]);
        public Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SitemapInfo>>([]);
        public Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MainUiPageComponent>>([]);
    }
}
```

- [ ] **Step 2: Run executor tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~VoiceCommandExecutorTests" --logger "console;verbosity=minimal"
```

Expected: compile fails because `VoiceCommandExecutor` and related result types do not exist.

- [ ] **Step 3: Implement voice command executor**

Create `src/OpenHab.App/Shortcuts/VoiceCommandExecutor.cs`:

```csharp
using OpenHab.App.Runtime;
using OpenHab.Core.Api;

namespace OpenHab.App.Shortcuts;

public enum VoiceCommandExecutionFailure
{
    None,
    Disconnected,
    InvalidAction,
    EmptyPhrase,
    MissingClient,
    CommandFailed
}

public sealed record VoiceCommandExecutionResult(
    bool Succeeded,
    VoiceCommandExecutionFailure Failure,
    string Message)
{
    public static VoiceCommandExecutionResult Success()
    {
        return new VoiceCommandExecutionResult(true, VoiceCommandExecutionFailure.None, string.Empty);
    }

    public static VoiceCommandExecutionResult Failed(VoiceCommandExecutionFailure failure, string message)
    {
        return new VoiceCommandExecutionResult(false, failure, message);
    }
}

public sealed class VoiceCommandExecutor
{
    private readonly Func<IOpenHabClient?> getClient;
    private readonly Func<ConnectionState> getConnectionState;
    private readonly Action<string> logDiagnostic;

    public VoiceCommandExecutor(
        Func<IOpenHabClient?> getClient,
        Func<ConnectionState> getConnectionState,
        Action<string> logDiagnostic)
    {
        this.getClient = getClient ?? throw new ArgumentNullException(nameof(getClient));
        this.getConnectionState = getConnectionState ?? throw new ArgumentNullException(nameof(getConnectionState));
        this.logDiagnostic = logDiagnostic ?? throw new ArgumentNullException(nameof(logDiagnostic));
    }

    public async Task<VoiceCommandExecutionResult> ExecuteAsync(
        ShortcutAction action,
        string recognizedPhrase,
        bool logPhrase,
        CancellationToken cancellationToken)
    {
        if (action.CommandType != ShortcutCommandType.Voice || !ShortcutValidation.ValidateAction(action).IsValid)
        {
            return VoiceCommandExecutionResult.Failed(
                VoiceCommandExecutionFailure.InvalidAction,
                "Voice command action is invalid.");
        }

        var phrase = (recognizedPhrase ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return VoiceCommandExecutionResult.Failed(
                VoiceCommandExecutionFailure.EmptyPhrase,
                "No voice command was recognized.");
        }

        try
        {
            if (getConnectionState() != ConnectionState.Online)
            {
                return VoiceCommandExecutionResult.Failed(
                    VoiceCommandExecutionFailure.Disconnected,
                    "Voice commands require an online openHAB connection.");
            }

            var client = getClient();
            if (client is null)
            {
                return VoiceCommandExecutionResult.Failed(
                    VoiceCommandExecutionFailure.MissingClient,
                    "Client is unavailable.");
            }

            logDiagnostic(logPhrase
                ? $"Sending voice command phrase to item '{action.TargetItem}': {phrase}"
                : $"Sending voice command phrase to item '{action.TargetItem}'.");

            await client.SendCommandAsync(action.TargetItem, phrase, cancellationToken).ConfigureAwait(false);
            return VoiceCommandExecutionResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return VoiceCommandExecutionResult.Failed(
                VoiceCommandExecutionFailure.CommandFailed,
                "Voice command could not be sent.");
        }
    }
}
```

- [ ] **Step 4: Run executor tests and verify they pass**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~VoiceCommandExecutorTests" --logger "console;verbosity=minimal"
```

Expected: tests pass.

- [ ] **Step 5: Commit executor changes**

Run:

```powershell
git add src/OpenHab.App/Shortcuts/VoiceCommandExecutor.cs tests/OpenHab.App.Tests/Shortcuts/VoiceCommandExecutorTests.cs
git commit -m "feat: execute recognized voice commands"
```

## Task 4: Settings UI, Localization, And Action Editor Integration

**Files:**
- Modify: `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs`
- Modify: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`
- Modify: `src/OpenHab.Windows.Tray/Strings/pl-PL/Resources.resw`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`

- [ ] **Step 1: Add English localization keys**

In `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs`, replace the existing Voice Mode entries:

```csharp
["Settings.Shortcuts.VoiceMode.Subtitle"] = "Coming soon. Voice shortcut is currently unassigned and unavailable.",
["Settings.Shortcuts.VoiceMode.ExpanderTitle"] = "Voice Mode",
["Settings.Shortcuts.VoiceMode.ExpanderSubtitle"] = "Planned voice shortcut, coming soon",
```

with:

```csharp
["Settings.Shortcuts.VoiceMode.Subtitle"] = "Single-shot speech recognition for openHAB voice commands",
["Settings.Shortcuts.VoiceMode.ExpanderTitle"] = "Voice Mode",
["Settings.Shortcuts.VoiceMode.ExpanderSubtitle"] = "Send recognized speech to an openHAB item",
["Settings.Shortcuts.VoiceMode.EnableAutomationName"] = "Enable voice commands",
["Settings.Shortcuts.VoiceMode.EnableTitle"] = "Enable voice commands",
["Settings.Shortcuts.VoiceMode.EnableSubtitle"] = "Show the flyout microphone button and allow Voice actions to run",
["Settings.Shortcuts.VoiceMode.ConfirmationTitle"] = "Require confirmation before sending",
["Settings.Shortcuts.VoiceMode.ConfirmationSubtitle"] = "Review the recognized phrase before it is sent to openHAB",
["Settings.Shortcuts.VoiceMode.PrivacyTitle"] = "Privacy",
["Settings.Shortcuts.VoiceMode.PrivacyText"] = "Windows free-form dictation can use Microsoft online speech services. Voice commands require Windows microphone and speech-recognition permissions. Normal diagnostics do not log recognized phrases; verbose diagnostics may log full spoken command text to diagnostics.log.",
["Settings.Shortcuts.VoiceMode.DefaultActionProtected"] = "The default Voice action is required while Voice Mode exists and cannot be deleted.",
```

Add this command type entry after `SendCommand`:

```csharp
["Settings.Shortcuts.CommandType.Voice"] = "Voice",
```

- [ ] **Step 2: Add RESW localization keys**

Add matching keys to `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw` near the existing Voice Mode keys:

```xml
  <data name="Settings.Shortcuts.VoiceMode.EnableAutomationName" xml:space="preserve">
    <value>Enable voice commands</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.EnableTitle" xml:space="preserve">
    <value>Enable voice commands</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.EnableSubtitle" xml:space="preserve">
    <value>Show the flyout microphone button and allow Voice actions to run</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.ConfirmationTitle" xml:space="preserve">
    <value>Require confirmation before sending</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.ConfirmationSubtitle" xml:space="preserve">
    <value>Review the recognized phrase before it is sent to openHAB</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.PrivacyTitle" xml:space="preserve">
    <value>Privacy</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.PrivacyText" xml:space="preserve">
    <value>Windows free-form dictation can use Microsoft online speech services. Voice commands require Windows microphone and speech-recognition permissions. Normal diagnostics do not log recognized phrases; verbose diagnostics may log full spoken command text to diagnostics.log.</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.DefaultActionProtected" xml:space="preserve">
    <value>The default Voice action is required while Voice Mode exists and cannot be deleted.</value>
  </data>
  <data name="Settings.Shortcuts.CommandType.Voice" xml:space="preserve">
    <value>Voice</value>
  </data>
```

Add the same keys to `src/OpenHab.Windows.Tray/Strings/pl-PL/Resources.resw` with English values if Polish translation is not available during implementation. English fallback values keep the resource complete:

```xml
  <data name="Settings.Shortcuts.VoiceMode.EnableAutomationName" xml:space="preserve">
    <value>Enable voice commands</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.EnableTitle" xml:space="preserve">
    <value>Enable voice commands</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.EnableSubtitle" xml:space="preserve">
    <value>Show the flyout microphone button and allow Voice actions to run</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.ConfirmationTitle" xml:space="preserve">
    <value>Require confirmation before sending</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.ConfirmationSubtitle" xml:space="preserve">
    <value>Review the recognized phrase before it is sent to openHAB</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.PrivacyTitle" xml:space="preserve">
    <value>Privacy</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.PrivacyText" xml:space="preserve">
    <value>Windows free-form dictation can use Microsoft online speech services. Voice commands require Windows microphone and speech-recognition permissions. Normal diagnostics do not log recognized phrases; verbose diagnostics may log full spoken command text to diagnostics.log.</value>
  </data>
  <data name="Settings.Shortcuts.VoiceMode.DefaultActionProtected" xml:space="preserve">
    <value>The default Voice action is required while Voice Mode exists and cannot be deleted.</value>
  </data>
  <data name="Settings.Shortcuts.CommandType.Voice" xml:space="preserve">
    <value>Voice</value>
  </data>
```

- [ ] **Step 3: Add settings control fields**

In `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`, add these fields next to the command menu shortcut fields:

```csharp
private ToggleSwitch? VoiceModeEnabledToggle;
private ToggleSwitch? VoiceModeConfirmationToggle;
```

In `ResetSettingsControlReferences`, set both to `null`:

```csharp
VoiceModeEnabledToggle = null;
VoiceModeConfirmationToggle = null;
```

- [ ] **Step 4: Include Voice in action type options**

In `CreateShortcutCommandTypeOptions`, add Voice after Send command:

```csharp
new(text.Get("Settings.Shortcuts.CommandType.SendCommand"), ShortcutCommandType.SendCommand),
new(text.Get("Settings.Shortcuts.CommandType.Voice"), ShortcutCommandType.Voice)
```

- [ ] **Step 5: Replace Voice Mode settings stub**

In `BuildShortcutsSettingsPage`, replace the current `voiceModeStateRow`, `voiceModeShortcutRow`, and disabled expander block with:

```csharp
VoiceModeEnabledToggle = new ToggleSwitch
{
    OnContent = string.Empty,
    OffContent = string.Empty,
    IsOn = settings.VoiceMode.Enabled
};
AutomationProperties.SetName(VoiceModeEnabledToggle, text.Get("Settings.Shortcuts.VoiceMode.EnableAutomationName"));
VoiceModeEnabledToggle.Toggled += VoiceModeEnabledToggle_Toggled;
var voiceEnabledRow = CreateSettingsToggleRow(
    "\uE720",
    text.Get("Settings.Shortcuts.VoiceMode.EnableTitle"),
    text.Get("Settings.Shortcuts.VoiceMode.EnableSubtitle"),
    VoiceModeEnabledToggle);

VoiceModeConfirmationToggle = new ToggleSwitch
{
    OnContent = string.Empty,
    OffContent = string.Empty,
    IsOn = settings.VoiceMode.RequireConfirmationBeforeSending
};
VoiceModeConfirmationToggle.Toggled += VoiceModeConfirmationToggle_Toggled;
var voiceConfirmationRow = CreateSettingsToggleRow(
    "\uE8EF",
    text.Get("Settings.Shortcuts.VoiceMode.ConfirmationTitle"),
    text.Get("Settings.Shortcuts.VoiceMode.ConfirmationSubtitle"),
    VoiceModeConfirmationToggle);

var privacyRow = CreateSettingsControlRow(
    "\uE946",
    text.Get("Settings.Shortcuts.VoiceMode.PrivacyTitle"),
    text.Get("Settings.Shortcuts.VoiceMode.PrivacyText"),
    new FontIcon
    {
        Glyph = "\uE946",
        FontSize = 18,
        Opacity = 0.72,
        VerticalAlignment = VerticalAlignment.Center
    },
    stretchControl: false);

SettingsContent.Children.Add(CreateSettingsExpander(
    text.Get("Settings.Shortcuts.VoiceMode.ExpanderTitle"),
    text.Get("Settings.Shortcuts.VoiceMode.ExpanderSubtitle"),
    CreateExpanderRows(voiceEnabledRow, voiceConfirmationRow, privacyRow),
    CreateSettingsToggleAction(VoiceModeEnabledToggle),
    isExpanded: settings.VoiceMode.Enabled));
```

- [ ] **Step 6: Add Voice Mode event handlers**

Add these methods near the command menu shortcut handlers:

```csharp
private void VoiceModeEnabledToggle_Toggled(object sender, RoutedEventArgs e)
{
    if (isRefreshingSettingsBindings || sender is not ToggleSwitch toggle)
    {
        return;
    }

    var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
    settingsController.SetShortcutSettings(shortcuts with
    {
        VoiceMode = shortcuts.VoiceMode with
        {
            Enabled = toggle.IsOn
        }
    });

    RefreshShortcutActionsSection();
}

private void VoiceModeConfirmationToggle_Toggled(object sender, RoutedEventArgs e)
{
    if (isRefreshingSettingsBindings || sender is not ToggleSwitch toggle)
    {
        return;
    }

    var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
    settingsController.SetShortcutSettings(shortcuts with
    {
        VoiceMode = shortcuts.VoiceMode with
        {
            RequireConfirmationBeforeSending = toggle.IsOn
        }
    });
}
```

- [ ] **Step 7: Refresh Voice controls**

In `RefreshSettingsBindings`, after command menu control refresh, add:

```csharp
if (VoiceModeEnabledToggle is not null)
{
    VoiceModeEnabledToggle.IsOn = shortcuts.VoiceMode.Enabled;
}

if (VoiceModeConfirmationToggle is not null)
{
    VoiceModeConfirmationToggle.IsOn = shortcuts.VoiceMode.RequireConfirmationBeforeSending;
}
```

- [ ] **Step 8: Protect default Voice delete UI**

In the action table row builder where `deleteButton` is created, replace the current delete button setup:

```csharp
var deleteButton = CreateActionIconButton("\uE74D", text.Get("Settings.Shortcuts.Actions.Delete"), action.Id);
deleteButton.Click += DeleteShortcutActionButton_Click;
actionsPanel.Children.Add(deleteButton);
```

with:

```csharp
var deleteButton = CreateActionIconButton("\uE74D", text.Get("Settings.Shortcuts.Actions.Delete"), action.Id);
if (VoiceShortcutPolicy.IsProtectedDefaultVoiceAction(action))
{
    deleteButton.IsEnabled = false;
    ToolTipService.SetToolTip(deleteButton, text.Get("Settings.Shortcuts.VoiceMode.DefaultActionProtected"));
}
else
{
    deleteButton.Click += DeleteShortcutActionButton_Click;
}
actionsPanel.Children.Add(deleteButton);
```

- [ ] **Step 9: Make Voice command value visibly unused**

After `ShortcutActionValueText` is created in the action editor, add:

```csharp
if (draftAction.CommandType == ShortcutCommandType.Voice)
{
    ShortcutActionValueText.IsEnabled = false;
    ShortcutActionValueText.Text = string.Empty;
    ShortcutActionValueText.PlaceholderText = "-";
}
```

In `SaveShortcutActionButton_Click`, set command value to null for Voice:

```csharp
CommandValue: selectedType == ShortcutCommandType.Voice ? null : ShortcutActionValueText?.Text);
```

In `GetCurrentShortcutActionEditorDraft`, set command value to null for Voice:

```csharp
CommandValue: selectedType == ShortcutCommandType.Voice ? null : ShortcutActionValueText.Text);
```

- [ ] **Step 10: Run localization and settings-related tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalizationResourceTests|FullyQualifiedName~ShortcutActionEditorPlannerTests|FullyQualifiedName~AppSettingsControllerTests" --logger "console;verbosity=minimal"
```

Expected: tests pass. If `LocalizationResourceTests` reports missing resource values, add the reported keys to both RESW files and rerun the command.

- [ ] **Step 11: Commit settings UI changes**

Run:

```powershell
git add src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw src/OpenHab.Windows.Tray/Strings/pl-PL/Resources.resw src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs
git commit -m "feat: wire voice mode settings"
```

## Task 5: Hotkey, Radial Menu, And Flyout Entry Points

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`

- [ ] **Step 1: Gate Voice action hotkey registration**

In `GlobalHotkeyService.Refresh`, replace the action registration loop with:

```csharp
foreach (var action in normalized.Actions)
{
    if (action.GlobalShortcut is null)
    {
        continue;
    }

    if (action.CommandType == ShortcutCommandType.Voice && !normalized.VoiceMode.Enabled)
    {
        continue;
    }

    if (!ShortcutValidation.ValidateAction(action).IsValid)
    {
        continue;
    }

    RegisterBinding(
        owner: $"Action: {action.Name}",
        binding: action.GlobalShortcut,
        action,
        seenBindings,
        failures);
}
```

- [ ] **Step 2: Gate Voice actions in radial command menu input**

In `App.xaml.cs`, add this helper near the shortcut methods:

```csharp
private static bool IsActionAvailableForCurrentShortcutMode(ShortcutSettings settings, ShortcutAction action)
{
    return action.CommandType != ShortcutCommandType.Voice || settings.VoiceMode.Enabled;
}
```

In `OpenShortcutCommandMenuAsync`, replace the actions query with:

```csharp
var actions = settings
    .Actions
    .Where(action => IsActionAvailableForCurrentShortcutMode(settings, action))
    .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid)
    .ToList();
```

- [ ] **Step 3: Route Voice action execution**

In `ExecuteShortcutActionAsync`, add this branch before interactive slider/color handling:

```csharp
if (action.CommandType == ShortcutCommandType.Voice)
{
    await StartVoiceCommandAsync(action);
    return;
}
```

`StartVoiceCommandAsync` is added in Task 6. For this task, add a temporary private method that compiles and logs a status:

```csharp
private Task StartVoiceCommandAsync(ShortcutAction action)
{
    SetShellStatusText("Voice command support is initializing.");
    return Task.CompletedTask;
}
```

Task 6 replaces this body with recognition and sending.

- [ ] **Step 4: Add flyout microphone button XAML**

In `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`, change the header column definitions from four columns to five columns:

```xml
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="*" />
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="Auto" />
<ColumnDefinition Width="Auto" />
```

Insert this button between `SearchButton` and the Minimize button:

```xml
<Button x:Name="VoiceCommandButton"
        Grid.Column="3"
        Width="34"
        Height="34"
        Padding="0"
        Visibility="Collapsed"
        ToolTipService.ToolTip="Voice command"
        Click="VoiceCommandButton_Click">
    <FontIcon Glyph="&#xE720;"
              FontSize="13"
              Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
</Button>
```

Change the Minimize button to `Grid.Column="4"`.

- [ ] **Step 5: Add flyout voice callback**

In `FlyoutWindow.xaml.cs`, add a field:

```csharp
private readonly Action requestVoiceCommand;
```

Add `Action requestVoiceCommand` to the constructor parameter list after `requestOpenNotifications`, assign it:

```csharp
this.requestVoiceCommand = requestVoiceCommand;
```

Add this method near other button handlers:

```csharp
private void VoiceCommandButton_Click(object sender, RoutedEventArgs e)
{
    requestVoiceCommand();
}
```

Add this helper:

```csharp
private void RefreshVoiceCommandButtonVisibility()
{
    var enabled = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized().VoiceMode.Enabled;
    VoiceCommandButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
}
```

Call `RefreshVoiceCommandButtonVisibility();` after `InitializeComponent();`, and inside `OnSettingsChanged`.

- [ ] **Step 6: Pass flyout voice callback from App**

In `CreateFlyoutWindow`, update the `FlyoutWindow` constructor call by adding:

```csharp
requestVoiceCommand: () =>
{
    _ = StartVoiceCommandFromFlyoutAsync();
},
```

Add this helper near other shortcut helpers:

```csharp
private Task StartVoiceCommandFromFlyoutAsync()
{
    var settings = (settingsController?.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
    if (!settings.VoiceMode.Enabled)
    {
        return Task.CompletedTask;
    }

    var action = settings.Actions.FirstOrDefault(VoiceShortcutPolicy.IsProtectedDefaultVoiceAction);
    return action is null ? Task.CompletedTask : StartVoiceCommandAsync(action);
}
```

- [ ] **Step 7: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug --no-restore
```

Expected: build passes. If the build reports that `FlyoutWindow` constructor call sites are missing the new callback, update every `new FlyoutWindow(...)` call with the `requestVoiceCommand` argument shown above.

- [ ] **Step 8: Commit entry point changes**

Run:

```powershell
git add src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs src/OpenHab.Windows.Tray/App.xaml.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs
git commit -m "feat: expose voice command entry points"
```

## Task 6: Windows Speech Recognition And Confirmation Flow

**Files:**
- Create: `src/OpenHab.Windows.Tray/Voice/VoiceRecognitionResult.cs`
- Create: `src/OpenHab.Windows.Tray/Voice/WindowsSpeechVoiceRecognitionService.cs`
- Create: `src/OpenHab.Windows.Tray/Voice/VoiceCommandConfirmationWindow.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Package.appxmanifest`

- [ ] **Step 1: Add recognition result model**

Create `src/OpenHab.Windows.Tray/Voice/VoiceRecognitionResult.cs`:

```csharp
namespace OpenHab.Windows.Tray.Voice;

internal enum VoiceRecognitionFailure
{
    None,
    NoMatch,
    Canceled,
    PermissionOrSpeechDisabled,
    Unavailable,
    Failed
}

internal sealed record VoiceRecognitionResult(
    bool Succeeded,
    string? Text,
    VoiceRecognitionFailure Failure,
    string Message)
{
    public static VoiceRecognitionResult Success(string text)
    {
        return new VoiceRecognitionResult(true, text, VoiceRecognitionFailure.None, string.Empty);
    }

    public static VoiceRecognitionResult Failed(VoiceRecognitionFailure failure, string message)
    {
        return new VoiceRecognitionResult(false, null, failure, message);
    }
}
```

- [ ] **Step 2: Add Windows speech recognition service**

Create `src/OpenHab.Windows.Tray/Voice/WindowsSpeechVoiceRecognitionService.cs`:

```csharp
using Windows.Media.SpeechRecognition;

namespace OpenHab.Windows.Tray.Voice;

internal sealed class WindowsSpeechVoiceRecognitionService
{
    public async Task<VoiceRecognitionResult> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var recognizer = new SpeechRecognizer();
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "openHAB voice command"));

            var compileResult = await recognizer.CompileConstraintsAsync().AsTask(cancellationToken);
            if (compileResult.Status != SpeechRecognitionResultStatus.Success)
            {
                return VoiceRecognitionResult.Failed(
                    VoiceRecognitionFailure.PermissionOrSpeechDisabled,
                    "Windows speech recognition is unavailable. Check microphone and online speech settings.");
            }

            var result = await recognizer.RecognizeAsync().AsTask(cancellationToken);
            if (result.Status == SpeechRecognitionResultStatus.Success
                && !string.IsNullOrWhiteSpace(result.Text))
            {
                return VoiceRecognitionResult.Success(result.Text.Trim());
            }

            return result.Status switch
            {
                SpeechRecognitionResultStatus.TimeoutExceeded => VoiceRecognitionResult.Failed(VoiceRecognitionFailure.NoMatch, "No voice command was recognized."),
                SpeechRecognitionResultStatus.UserCanceled => VoiceRecognitionResult.Failed(VoiceRecognitionFailure.Canceled, string.Empty),
                SpeechRecognitionResultStatus.AudioQualityFailure => VoiceRecognitionResult.Failed(VoiceRecognitionFailure.NoMatch, "Voice command was not clear enough."),
                _ => VoiceRecognitionResult.Failed(VoiceRecognitionFailure.NoMatch, "No voice command was recognized.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return VoiceRecognitionResult.Failed(
                VoiceRecognitionFailure.PermissionOrSpeechDisabled,
                "Microphone permission is disabled for openHAB.");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Voice recognition failed: {ex.GetType().Name}: {ex.Message}");
            return VoiceRecognitionResult.Failed(
                VoiceRecognitionFailure.Failed,
                "Voice recognition failed.");
        }
    }
}
```

- [ ] **Step 3: Add confirmation window**

Create `src/OpenHab.Windows.Tray/Voice/VoiceCommandConfirmationWindow.cs`:

```csharp
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenHab.Windows.Tray.Voice;

internal sealed class VoiceCommandConfirmationWindow : Window
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool completed;

    public VoiceCommandConfirmationWindow(string transcript)
    {
        Title = "Voice command";
        Width = 420;
        Height = 180;

        var root = new Grid
        {
            Padding = new Thickness(16),
            RowSpacing = 12,
            RequestedTheme = ElementTheme.Default
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Send voice command?",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var transcriptText = new TextBlock
        {
            Text = transcript,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(transcriptText, 1);
        root.Children.Add(transcriptText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var send = new Button { Content = "Send" };
        send.Click += (_, _) => Complete(true);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Complete(false);
        buttons.Children.Add(send);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Closed += (_, _) => Complete(false);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }
    }

    public Task<bool> WaitForDecisionAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                Complete(false);
                DispatcherQueue.TryEnqueue(Close);
            });
        }

        Activate();
        return completion.Task;
    }

    private void Complete(bool send)
    {
        if (completed)
        {
            return;
        }

        completed = true;
        completion.TrySetResult(send);
        Close();
    }
}
```

- [ ] **Step 4: Add microphone capability**

In `src/OpenHab.Windows.Tray/Package.appxmanifest`, add the microphone capability inside `<Capabilities>`:

```xml
<DeviceCapability Name="microphone" />
```

The capabilities block should become:

```xml
<Capabilities>
  <rescap:Capability Name="runFullTrust" />
  <DeviceCapability Name="microphone" />
</Capabilities>
```

- [ ] **Step 5: Wire services in App**

In `src/OpenHab.Windows.Tray/App.xaml.cs`, add:

```csharp
using OpenHab.Windows.Tray.Voice;
```

Add fields:

```csharp
private VoiceCommandExecutor? voiceCommandExecutor;
private WindowsSpeechVoiceRecognitionService? voiceRecognitionService;
private VoiceCommandConfirmationWindow? voiceConfirmationWindow;
private CancellationTokenSource? voiceCommandCts;
```

After `shortcutActionExecutor` initialization, add:

```csharp
voiceCommandExecutor = new VoiceCommandExecutor(
    CreateActiveShortcutClient,
    () => runtimeController?.Current.ConnectionState ?? ConnectionState.Offline,
    message => DiagnosticLogger.Info(message));
voiceRecognitionService = new WindowsSpeechVoiceRecognitionService();
```

In shutdown cleanup, add:

```csharp
voiceCommandCts?.Cancel();
voiceCommandCts?.Dispose();
voiceCommandCts = null;
voiceConfirmationWindow?.Close();
voiceConfirmationWindow = null;
voiceRecognitionService = null;
voiceCommandExecutor = null;
```

- [ ] **Step 6: Replace temporary StartVoiceCommandAsync**

Replace the temporary `StartVoiceCommandAsync` body from Task 5 with:

```csharp
private async Task StartVoiceCommandAsync(ShortcutAction action)
{
    var dispatcher = uiDispatcherQueue;
    if (dispatcher is not null && !dispatcher.HasThreadAccess)
    {
        _ = dispatcher.TryEnqueue(() => _ = StartVoiceCommandAsync(action));
        return;
    }

    var shortcutSettings = (settingsController?.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
    if (!shortcutSettings.VoiceMode.Enabled)
    {
        return;
    }

    if (voiceCommandCts is not null)
    {
        voiceConfirmationWindow?.Activate();
        return;
    }

    if (runtimeController?.Current.ConnectionState != ConnectionState.Online)
    {
        SetShellStatusText("Voice commands require an online openHAB connection.");
        return;
    }

    if (action.CommandType != ShortcutCommandType.Voice || !ShortcutValidation.ValidateAction(action).IsValid)
    {
        SetShellStatusText("Voice command action is invalid.");
        return;
    }

    var recognition = voiceRecognitionService;
    var executor = voiceCommandExecutor;
    if (recognition is null || executor is null)
    {
        SetShellStatusText("Voice command service is unavailable.");
        return;
    }

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    voiceCommandCts = cts;
    try
    {
        SetShellStatusText("Listening for voice command...");
        var recognitionResult = await recognition.RecognizeOnceAsync(cts.Token);
        if (!recognitionResult.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(recognitionResult.Message))
            {
                SetShellStatusText(recognitionResult.Message);
            }
            return;
        }

        var phrase = recognitionResult.Text ?? string.Empty;
        if (shortcutSettings.VoiceMode.RequireConfirmationBeforeSending)
        {
            voiceConfirmationWindow?.Close();
            var confirmation = new VoiceCommandConfirmationWindow(phrase);
            voiceConfirmationWindow = confirmation;
            confirmation.Closed += (_, _) =>
            {
                if (ReferenceEquals(voiceConfirmationWindow, confirmation))
                {
                    voiceConfirmationWindow = null;
                }
            };

            var approved = await confirmation.WaitForDecisionAsync(cts.Token);
            if (!approved)
            {
                SetShellStatusText("Voice command canceled.");
                return;
            }
        }

        var result = await executor.ExecuteAsync(
            action,
            phrase,
            logPhrase: settingsController?.Current.VerboseDiagnostics == true,
            cts.Token);
        if (!result.Succeeded)
        {
            SetShellStatusText(result.Message);
        }
        else
        {
            SetShellStatusText(string.Empty);
        }
    }
    catch (OperationCanceledException)
    {
        SetShellStatusText("Voice command canceled.");
    }
    finally
    {
        if (ReferenceEquals(voiceCommandCts, cts))
        {
            voiceCommandCts = null;
        }
    }
}
```

- [ ] **Step 7: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug --no-restore
```

Expected: build passes. If `SpeechRecognitionResultStatus.UserCanceled` is not available in the targeted SDK, replace that switch arm with the exact status name reported by the compiler and keep the result mapped to `VoiceRecognitionFailure.Canceled`.

- [ ] **Step 8: Commit recognition changes**

Run:

```powershell
git add src/OpenHab.Windows.Tray/Voice src/OpenHab.Windows.Tray/App.xaml.cs src/OpenHab.Windows.Tray/Package.appxmanifest
git commit -m "feat: recognize and send voice commands"
```

## Task 7: Verification

**Files:**
- No source edits expected unless verification exposes failures.

- [ ] **Step 1: Run direct App tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

Expected: all App tests pass.

- [ ] **Step 2: Run direct logic test projects**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

Expected: all tests pass.

- [ ] **Step 3: Build tray Release**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Expected: build passes with 0 errors.

- [ ] **Step 4: Build package because manifest changed**

Run:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

Expected: package build passes. If the environment lacks Visual Studio DesktopBridge/MSIX targets, record the exact missing-target message and do not claim package verification passed.

- [ ] **Step 5: Manual smoke check**

Run the tray app from Visual Studio or the built executable and verify:

1. Voice Mode disabled: flyout has no microphone button, no Voice action appears in radial command menu, and `Ctrl + Alt + I` does not activate voice.
2. Enable Voice Mode in Settings: protected `Voice command` action appears with target `VoiceCommand`, shortcut `Ctrl + Alt + I`, command type `Voice`, and command-menu visibility enabled.
3. The protected Voice action delete button is disabled or omitted.
4. Flyout shows the microphone button between Search and Minimize.
5. Voice action can be hidden from command menu and unassigned from shortcut while flyout mic still uses it.
6. Immediate-send mode listens once and sends recognized text to the configured target item.
7. Confirmation mode shows transcript and only sends after Send.
8. Normal diagnostics do not contain the recognized phrase.
9. Verbose diagnostics contain the recognized phrase.

- [ ] **Step 6: Commit verification fixes if needed**

If verification required code changes, run:

```powershell
git status --short
```

Then stage only the files changed for voice command verification and commit them. Example command when the only verification fix is in `App.xaml.cs`:

```powershell
git add src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "fix: stabilize voice command mode"
```

## Self-Review

- Spec coverage: The plan covers opt-in Voice Mode, protected default Voice action, configurable item through action editor, `Ctrl + Alt + I` default, command menu inclusion, flyout microphone visibility only when enabled, immediate send, confirmation option, privacy disclosure, normal versus verbose diagnostics, and out-of-scope receive-audio/raw-stream behavior.
- Scope check: The native openHAB-to-Windows audio sink and server-side STT are not included in any task.
- Type consistency: `VoiceModeShortcutSettings`, `VoiceShortcutPolicy`, `ShortcutCommandType.Voice`, and `VoiceCommandExecutor` are introduced before later tasks reference them.
