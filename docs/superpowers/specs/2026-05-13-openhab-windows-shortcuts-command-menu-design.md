# openHAB Windows Shortcuts And Command Menu Design

Date: 2026-05-13

## Purpose

Add a full shortcuts system to the openHAB Windows app:

- a `Shortcuts` settings category,
- configurable global shortcut bindings,
- user-defined command actions,
- a radial openHAB command menu,
- direct action shortcuts,
- Windows global hotkey registration,
- command execution against the active openHAB connection.

The first-release design is contract-first and staged. The spec defines the complete feature end to end, but the implementation should land in safe milestones with tests at each layer.

## Current Context

The app is a Windows 11 tray app with a larger main window and a Settings surface extracted into `OpenHab.Windows.Tray.Settings.SettingsPageControl`. Settings persist through `OpenHab.App.Settings.AppSettingsController`. Runtime sitemap command dispatch already flows through `OpenHab.App.Runtime.SitemapRuntimeController` and `OpenHab.Core.Api.IOpenHabClient.SendCommandAsync`.

The shortcuts feature must preserve the existing project split:

- `OpenHab.App` owns UI-independent settings, validation, conflict detection, command-menu state, and action execution orchestration.
- `OpenHab.Core` owns openHAB API contracts and HTTP implementation.
- `OpenHab.Windows.Tray` owns WinUI controls, radial command menu UI, Windows hotkey registration, and shell wiring.
- `OpenHab.Sitemaps` and `OpenHab.Rendering` remain unchanged unless a later implementation needs reusable descriptor metadata.

## Visual Direction

Use the restrained hybrid direction based on the approved mockup review:

- keep the calm, neutral Windows 11 settings style from the first mockup,
- borrow only useful affordances from the second mockup, such as a clearer action editor, shortcut chips, import action affordance if import exists later, and readable availability states,
- avoid the stronger color palette and visually busy workspace from the second mockup,
- do not use fake production action rows.

Cards should use the existing app's settings style and modest radius. The radial command menu should be visually distinct enough to feel like a command surface, but it should still use system colors, clear focus states, and restrained accents.

## User-Facing Feature Scope

### Settings Category

Add a new Settings category between `Device Info Sync` and `About`:

```text
Shortcuts
Command menu and global shortcuts
```

The page title is `Shortcuts` and the subtitle is:

```text
Configure global shortcuts and command menu actions.
```

Sections:

```text
Built-in shortcuts
Actions and shortcuts
```

### Built-In Shortcuts

#### openHAB Command Menu

Default:

```text
Enabled: true
Binding: Win + O
Radial activation mode: Toggle
```

Description:

```text
Open the command menu anywhere and trigger saved actions.
```

Expanded content:

- states that it uses actions configured below,
- shows the editable global shortcut,
- shows the radial activation mode control,
- may show a small command menu preview when it helps explain the feature.

The command menu shortcut is registered only while enabled and only while a valid binding exists. If openHAB is disconnected, pressing the shortcut must not open the radial menu.

#### Voice Mode

Voice Mode is a planned future feature and is not functional in this release.

Default:

```text
Enabled: false
Binding: null
Display: Unassigned
```

The card shows `Coming soon`, remains disabled, and does not register a global shortcut. It must not use `Win + V`, because Windows already reserves that shortcut for voice typing.

### Radial Activation Mode

The command menu supports two activation modes:

```text
Toggle
Hold
```

`Toggle` mode:

- pressing the command menu shortcut opens the radial menu,
- selecting an action executes it,
- `Escape`, the center close target, outside click, or losing focus closes without executing.

`Hold` mode:

- holding the shortcut opens the radial menu,
- pointer movement or keyboard navigation chooses an action,
- releasing the shortcut executes the highlighted action,
- releasing on the center/cancel target closes without executing,
- losing connection while held cancels the menu.

The first implementation may prefer `Toggle` as the default because it is easier to discover and more accessible. `Hold` must be designed with keyboard and accessibility support, not only pointer gestures.

## Settings Model

Add shortcut settings to `AppSettings`:

```csharp
public sealed record ShortcutSettings(
    BuiltInShortcutSettings CommandMenu,
    BuiltInShortcutSettings VoiceMode,
    ImmutableArray<ShortcutAction> Actions);

public sealed record BuiltInShortcutSettings(
    bool Enabled,
    ShortcutBinding? Binding,
    RadialActivationMode? RadialActivationMode = null);

public sealed record ShortcutBinding(
    ImmutableArray<ShortcutModifier> Modifiers,
    string Key);

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
```

Defaults:

```text
commandMenu.enabled = true
commandMenu.binding = Win + O
commandMenu.radialActivationMode = Toggle

voiceMode.enabled = false
voiceMode.binding = null
actions = []
```

Missing settings migrate to defaults. Invalid shortcut settings normalize to safe defaults. Invalid actions are removed or repaired only when that can be done without changing user intent; otherwise they are skipped and a diagnostic warning is logged without sensitive values.

## Action Model

Add a UI-independent model in `OpenHab.App`:

```csharp
public sealed record ShortcutAction(
    string Id,
    string Name,
    string IconId,
    bool ShowInCommandMenu,
    ShortcutBinding? GlobalShortcut,
    string TargetItem,
    ShortcutCommandType CommandType,
    string? CommandValue);

public enum ShortcutCommandType
{
    Toggle,
    OnOff,
    OpenClose,
    OpenSlider,
    OpenColorPicker,
    SendCommand
}
```

`Id` is stable and generated by the app. `IconId` is a stable string ID from the icon catalog. `TargetItem` stores the openHAB item name, not the label. `CommandValue` stores the exact command text needed for explicit command types.

Availability is derived:

```text
Shortcut + Command menu
Command menu only
Shortcut only
Unavailable
```

An action is unavailable if it has neither `ShowInCommandMenu` nor a shortcut, or if validation fails.

## Icon Picker

The icon picker must support enough first-release choices to distinguish similar devices, such as two separate bulbs using different light icons.

Store icons as stable string IDs. The initial catalog should be grouped:

Lighting:

```text
light-bulb
ceiling-light
lamp
strip-light
brightness
color-wheel
power
```

Openings:

```text
blinds
curtains
garage
door
lock
```

Climate:

```text
thermostat
fan
snowflake
flame
humidity
```

Media:

```text
play
pause
stop
speaker
tv
cast
music
volume
```

Scenes and tools:

```text
scene
movie
sleep
away
sparkle
timer
custom
```

The picker can begin as a grouped grid or combo-style picker. Custom image upload is out of scope for the first release.

## Item Selector

Create a searchable item selector that shows:

```text
Item label
Item name
Item type
```

It stores `TargetItem = item.Name`.

This requires item listing support in `OpenHab.Core` if the current API surface cannot provide it. Add the smallest suitable contract, for example:

```csharp
Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken ct);
```

The selector remains usable while disconnected only for cached items if such a cache exists. If there is no item cache, the selector should show a disconnected/empty state and allow manual item-name entry only if validation clearly marks it as unverified.

Later improvement: filter suggested command types by item type.

## Shortcut Recorder

Create a reusable WinUI control in `OpenHab.Windows.Tray`, backed by app-layer formatting and validation helpers.

Responsibilities:

- display binding as chips,
- start recording mode from an edit button,
- capture keydown,
- normalize modifier order,
- save binding,
- cancel recording,
- clear optional bindings,
- show validation errors.

Modifier display order:

```text
Win + Ctrl + Alt + Shift + Key
```

Validation rules:

- command menu binding must include at least one modifier,
- action shortcut binding must include at least one modifier,
- all bindings must include one non-modifier key,
- reject `Escape`, `Enter`, or `Tab` by themselves,
- reject duplicate bindings within app settings,
- reject bindings reserved by the app,
- allow optional action shortcuts to be cleared,
- show OS registration failures separately from validation failures.

Conflict message example:

```text
This shortcut is already used by "Movie Night".
```

OS registration failure message:

```text
This shortcut could not be registered. It may already be used by Windows or another app.
```

## Actions Table

Create `ShortcutActionsTable` in the Windows layer with data supplied by an app-layer view model/state object.

Columns:

```text
Icon
Action name
Availability
Shortcut
Target item
Action type
Command value
Actions
```

Row actions:

```text
Edit
Delete
```

Empty state:

```text
No actions yet.
Add actions to make them available in the command menu.
```

Production defaults contain no example actions.

## Action Editor

Create an add/edit panel or inline editor that follows the restrained hybrid visual direction. It can be a right-side panel on wide layouts and a stacked panel on narrow layouts.

Fields:

```text
Action name
Icon
Show in command menu
Global shortcut optional
Target item
Action type
Command value / default behavior
```

Behavior:

- selecting a row opens the editor,
- `Add action` creates a draft,
- `Save` validates and persists,
- `Cancel` discards draft changes,
- delete asks for confirmation,
- invalid actions cannot be saved,
- disconnected state does not block editing or saving a structurally valid action.

Command type UI:

```text
Toggle            -> no command value
On/Off            -> ON / OFF dropdown
Open/Close        -> OPEN / CLOSE dropdown
Open slider       -> no command value
Open color picker -> no command value
Send command      -> free text command value
```

`OpenSlider` and `OpenColorPicker` must either open a real compact command surface or be blocked from saving until that surface exists. They must not silently do nothing.

Media actions such as play, pause, and stop use `SendCommand` with user-provided command values. The media icon group makes those actions easy to distinguish, while the command value remains openHAB-specific.

## Radial Command Menu

Create a radial command menu in the Windows layer. It opens from:

- the command menu global shortcut,
- app UI entry points if present,
- tray or menu entry if added later.

It displays actions where:

```text
ShowInCommandMenu == true
```

Actions are arranged around a center close/cancel target. Each action displays:

```text
icon
name
optional target/item hint
```

Required behavior:

- do not open when openHAB is disconnected,
- close if connection drops while the menu is open,
- support mouse selection,
- support keyboard navigation and `Enter` execution,
- support `Escape` cancel,
- preserve focus state and high contrast usability,
- avoid showing invalid actions,
- show an empty state if connected but no command-menu actions exist.

If more actions exist than can be comfortably shown at once, the first release may use pagination or a deterministic maximum with a clear `More` affordance. The spec should not require an overcrowded radial menu.

## Action Execution

Create an app-layer executor, for example:

```csharp
Task<ShortcutActionExecutionResult> ExecuteAsync(
    ShortcutAction action,
    ShortcutExecutionContext context,
    CancellationToken cancellationToken);
```

Execution behavior:

```text
Toggle:
- read current item state,
- send OFF when current state is ON,
- send ON when current state is OFF,
- fail visibly for unsupported or unknown states.

OnOff:
- send ON or OFF.

OpenClose:
- send OPEN or CLOSE.

SendCommand:
- send configured command value.

OpenSlider:
- open compact slider command surface for target item.

OpenColorPicker:
- open compact color picker command surface for target item.
```

For `Toggle`, do not guess when the current item state cannot be read. The executor should return a failure result and the Windows layer should show a clear status/toast message.

Action execution uses the app's active openHAB transport. If there is no active transport or the connection is offline, execution is blocked before sending commands.

## Global Hotkey Registration

Create a Windows-layer hotkey service responsible for registering and unregistering:

- command menu shortcut,
- user action shortcuts.

Do not register Voice Mode.

Registration rules:

- register at app startup after settings load,
- refresh registrations whenever shortcut settings change,
- unregister the command menu shortcut when disabled,
- unregister removed or cleared action shortcuts,
- block duplicate shortcuts before save,
- record OS-level registration failures separately so the settings page can show warnings,
- do not log sensitive endpoint or credential data.

Hotkey press handling:

- command menu shortcut opens the radial menu only when openHAB is connected,
- direct action shortcuts execute only when openHAB is connected,
- disconnected hotkey presses do not open a stale UI and do not crash,
- optional non-intrusive status/toast feedback may be shown for explicit user-triggered failures.

## Disconnected And Failure States

Disconnected behavior is part of the feature contract:

- command menu hotkey must not activate the radial menu while openHAB is disconnected,
- direct action shortcuts must not execute while disconnected,
- settings remains editable while disconnected,
- valid actions can be saved while disconnected,
- connection state does not replace structural validation,
- radial menu closes if connection drops while open,
- command send failure shows a clear failure message,
- missing target item shows an error and does not crash,
- OS hotkey registration failure shows inline settings warning.

## Data Flow

Settings edit flow:

```text
WinUI draft controls
  -> app-layer validation and normalization
  -> AppSettings.Shortcuts
  -> AppSettingsController serialized save queue
  -> SettingsChanged
  -> Windows hotkey registrar refresh
```

Command menu flow:

```text
registered command menu hotkey
  -> connected-state gate
  -> radial menu state
  -> selected ShortcutAction
  -> ShortcutActionExecutor
  -> active openHAB client
  -> command send or compact command surface
  -> status/toast result
```

Direct action shortcut flow:

```text
registered action hotkey
  -> connected-state gate
  -> ShortcutActionExecutor
  -> active openHAB client
  -> command result
```

## Accessibility

Required:

- all toggles have accessible names,
- shortcut edit, clear, save, and cancel buttons have accessible names,
- table rows are keyboard navigable,
- editor fields are reachable by keyboard,
- radial menu is usable without a mouse,
- `Toggle` activation mode supports normal keyboard flow,
- `Hold` activation mode has a keyboard-compatible behavior,
- focus states are visible,
- high contrast mode remains usable,
- reduced motion settings are respected.

The radial menu may animate, but animation must not be required for understanding state or making a selection.

## Testing

Add app/core unit tests for:

- shortcut formatting,
- shortcut normalization,
- shortcut validation,
- duplicate/conflict detection,
- reserved shortcut handling,
- action validation,
- command value mapping,
- settings default and migration behavior,
- disconnected-state gating,
- radial activation mode decision logic,
- action executor command mapping,
- toggle failure when item state is unknown.

Add or extend Windows-layer targeted tests where practical for:

- opening Settings > Shortcuts,
- built-in command menu defaults,
- Voice Mode disabled and unassigned,
- shortcut recorder edit/cancel/save,
- action add/edit/delete,
- icon picker groups including media icons,
- radial menu connected-state gate,
- radial toggle mode behavior,
- radial hold mode behavior where testable,
- direct action shortcut blocked while disconnected.

Primary verification remains the documented direct test gate:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Use the tray build gate after Windows-layer UI/hotkey changes:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Use `build-package.ps1` for package readiness when DesktopBridge prerequisites are available.

## Milestones

### Milestone 1: Contracts And Settings Shell

- Add shortcut models and defaults.
- Add normalization and migration.
- Add validation and formatting helpers.
- Add Shortcuts category/page shell.
- Add tests for settings, validation, and migration.

### Milestone 2: Settings Editing Experience

- Add built-in shortcut cards.
- Add `ShortcutRecorder`.
- Add actions table.
- Add action editor.
- Add larger grouped icon picker.
- Add item selector or manual/cached item fallback.
- Add tests for settings interactions and helper logic.

### Milestone 3: Execution Contracts

- Add action executor.
- Add item state lookup needed for `Toggle`.
- Add command mapping.
- Add disconnected-state gates.
- Add tests for command execution and failure cases.

### Milestone 4: Radial Command Menu

- Add radial menu UI.
- Add toggle activation mode.
- Add hold activation mode.
- Add keyboard support and connected-state gating.
- Add empty and disconnected handling.

### Milestone 5: Windows Hotkey Registration

- Add global hotkey service.
- Register command menu and action shortcuts at startup.
- Refresh registrations on settings changes.
- Report OS registration conflicts inline.
- Verify direct shortcuts and command menu shortcut.

### Milestone 6: Polish And Accessibility

- Improve focus order.
- Verify screen reader names.
- Verify high contrast.
- Verify reduced motion.
- Verify no mouse dependency.
- Run full relevant verification gates.

## Non-Goals

Out of scope for the first release:

- Voice Mode execution,
- custom image upload for icons,
- multi-step macros,
- conditional actions,
- cloud-synced shortcuts,
- importing/exporting actions unless the app already has a general settings backup/export path,
- pretending the app is connected when openHAB is offline.

## Acceptance Criteria

Settings:

- user can open Settings > Shortcuts,
- command menu built-in card defaults to enabled `Win + O`,
- command menu activation mode defaults to `Toggle`,
- Voice Mode appears as `Coming soon`, disabled, and `Unassigned`,
- user can add, edit, and delete actions,
- user can choose a wider icon set including media icons,
- duplicate shortcuts are blocked before save,
- invalid actions cannot be saved,
- settings persist after restart.

Command menu:

- `Win + O` opens the radial menu only while openHAB is connected,
- radial menu displays configured command-menu actions,
- radial menu supports keyboard and mouse operation,
- `Toggle` mode opens/closes predictably,
- `Hold` mode supports hold/release execution and cancel,
- radial menu closes or cancels if connection drops,
- actions hidden from the command menu are not shown.

Execution:

- direct action shortcuts execute only while connected,
- explicit command actions send expected commands,
- toggle reads state and sends the opposite ON/OFF only when safe,
- media actions can send play/pause/stop style commands through `SendCommand`,
- missing target items and failed sends show clear failures,
- no disconnected hotkey press crashes or opens stale UI.

