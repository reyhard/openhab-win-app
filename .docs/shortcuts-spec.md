```
# Shortcuts Settings Specification

## Feature

Add a new `Shortcuts` category to the Settings tab of the openHAB Windows app.

The feature allows users to:

- Enable or disable the global openHAB Command Menu shortcut.
- Configure the openHAB Command Menu shortcut binding.
- See a planned Voice Mode shortcut setting.
- Create custom user actions.
- Optionally assign global shortcuts to user actions.
- Show selected actions inside the openHAB Command Menu.
- Execute commands against openHAB Items.

---

## Settings Navigation

Add a new Settings category:

```text
Shortcuts
Command menu and global shortcuts
```

Location:

```
Settings
- Connection
- General
- Appearance
- Device Info Sync
- Shortcuts
- About
```

Icon suggestion:

```
Keyboard / shortcuts / command icon
```

------

## Page Layout

Page title:

```
Shortcuts
```

Subtitle:

```
Configure global shortcuts and command menu actions.
```

Sections:

```
Built-in shortcuts
Actions and shortcuts
```

------

## Built-in Shortcuts

### openHAB Command Menu

Default state:

```
Enabled
```

Default shortcut:

```
Win + O
```

Description:

```
Open the command menu anywhere and trigger saved actions.
```

Expanded content:

```
Uses actions configured below.

Global shortcut: Win + O
```

Behavior:

- Toggle enables/disables shortcut registration.
- Shortcut can be edited.
- Shortcut conflict validation must run before saving.
- This shortcut opens the command menu.

------

### Voice Mode

Default state:

```
Disabled
```

Default shortcut shown:

```
Win + V
```

Status:

```
Coming soon
```

Description:

```
Planned voice shortcut, coming soon.
```

Expanded content:

```
Not implemented in this release.
```

Behavior:

- Display only.
- Do not register global shortcut.
- Toggle should remain off or disabled.
- Shortcut editor should be disabled.

------

## User Actions

Users can create custom actions used by:

```
Command menu
Global shortcut
Both
```

Each action has:

```
type ShortcutAction = {
  id: string;
  name: string;
  icon: string;
  showInCommandMenu: boolean;
  globalShortcut: ShortcutBinding | null;
  targetItem: string;
  commandType:
    | "toggle"
    | "onOff"
    | "openClose"
    | "openSlider"
    | "openColorPicker"
    | "sendCommand";
  commandValue: string | number | boolean | null;
};
```

Shortcut binding:

```
type ShortcutBinding = {
  modifiers: Array<"Win" | "Ctrl" | "Alt" | "Shift">;
  key: string;
};
```

------

## Actions Table

Columns:

```
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

```
Edit
Delete
```

Availability values:

```
Shortcut + Command menu
Command menu only
Shortcut only
```

Empty state:

```
No actions yet.
Add actions to make them available in the command menu.
```

------

## Action Editor

Fields:

```
Action name
Icon
Show in command menu
Global shortcut optional
Target item
Command type
Command value / default behavior
```

Command type behavior:

```
Toggle
- No command value required.
- Reads current state and sends opposite ON/OFF if supported.

On/Off
- Command value: ON or OFF.

Open/Close
- Command value: OPEN or CLOSE.

Open slider
- Opens slider UI for the target item.
- No immediate command value.

Open color picker
- Opens color picker UI for the target item.
- No immediate command value.

Send command
- Free text command value.
```

------

## Shortcut Recorder

Reusable component:

```
ShortcutRecorder
```

Used by:

```
Command Menu shortcut
Voice Mode shortcut, disabled
User action shortcut
```

Requirements:

- Display shortcut as key chips.
- Edit button starts recording mode.
- Capture keyboard input.
- Normalize key order.
- Allow cancel.
- Validate before save.

Modifier order:

```
Win + Ctrl + Alt + Shift + Key
```

Validation:

- Must include at least one modifier.
- Must include one non-modifier key.
- Reject duplicate shortcut bindings.
- Reject invalid empty shortcut.
- Show conflict messages inline.

Conflict message example:

```
This shortcut is already used by “Movie Night”.
```

OS registration failure message:

```
This shortcut could not be registered. It may already be used by Windows or another app.
```

------

## Persistence

Settings shape:

```
type ShortcutSettings = {
  commandMenu: {
    enabled: boolean;
    binding: ShortcutBinding;
  };
  voiceMode: {
    enabled: boolean;
    binding: ShortcutBinding;
  };
  actions: ShortcutAction[];
};
```

Defaults:

```
{
  commandMenu: {
    enabled: true,
    binding: {
      modifiers: ["Win"],
      key: "O"
    }
  },
  voiceMode: {
    enabled: false,
    binding: {
      modifiers: ["Win"],
      key: "V"
    }
  },
  actions: []
}
```

Storage keys:

```
shortcuts.commandMenu.enabled
shortcuts.commandMenu.binding
shortcuts.voiceMode.enabled
shortcuts.voiceMode.binding
shortcuts.actions
```

Migration:

- If shortcut settings are missing, create defaults.
- If an action contains invalid fields, preserve valid fields and ignore invalid ones.
- Do not crash on malformed settings.

------

## Command Menu Integration

Command menu should display actions where:

```
action.showInCommandMenu === true
```

Each command menu item uses:

```
Icon
Name
Target item
Command type
```

Selecting an action calls:

```
executeShortcutAction(action)
```

------

## Global Shortcut Registration

Register on app startup:

```
Command Menu shortcut
User action shortcuts
```

Do not register:

```
Voice Mode shortcut
```

When settings change:

```
Unregister old shortcut
Register new shortcut
Show error if registration fails
```

If Command Menu is disabled:

```
Unregister its shortcut
```

If user action has no shortcut:

```
Do not register shortcut
```

------

## Action Execution

Function:

```
executeShortcutAction(action: ShortcutAction)
```

Behavior:

```
toggle:
- Read current item state.
- Send opposite command where supported.

onOff:
- Send ON or OFF.

openClose:
- Send OPEN or CLOSE.

sendCommand:
- Send configured commandValue.

openSlider:
- Open slider flyout for target item.

openColorPicker:
- Open color picker flyout for target item.
```

Error handling:

```
If target item is missing, show error toast.
If command send fails, show error toast.
Do not crash.
```

------

## Accessibility

Requirements:

- All toggles have accessible labels.
- Shortcut edit buttons have accessible names.
- Action rows are keyboard navigable.
- Editor fields are reachable by keyboard.
- Command menu actions are reachable by keyboard.
- Focus state must be visible.
- High contrast mode should remain usable.

------

## Acceptance Criteria

### Settings page

- User can open Settings > Shortcuts.
- Built-in shortcuts section is visible.
- Actions and shortcuts section is visible.

### Command Menu shortcut

- Default binding is `Win + O`.
- Toggle enables/disables registration.
- Changing shortcut persists after restart.
- Duplicate shortcut is blocked.

### Voice Mode

- Voice Mode appears in UI.
- It is marked Coming soon.
- It does not register a shortcut.
- It cannot be used to start voice mode yet.

### Actions

- User can add an action.
- User can edit an action.
- User can delete an action.
- User can choose whether action appears in command menu.
- User can assign optional global shortcut.
- User can select target openHAB Item.
- User can select command type.
- User can configure command value where required.

### Command menu

- Command menu shows only actions with `showInCommandMenu = true`.
- Selecting an action executes expected behavior.
- Disabled or invalid actions are not shown.

### Global shortcuts

- User action shortcut executes action directly.
- Command Menu shortcut opens command menu.
- Conflicting shortcuts are rejected.
- Failed OS shortcut registration shows warning.

------

## Non-goals For First Release

- Voice Mode execution.
- Advanced conditional actions.
- Multi-step macros.
- Import/export UI unless existing settings export already supports it.