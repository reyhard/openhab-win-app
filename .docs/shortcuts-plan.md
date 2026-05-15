### Step 1 — Add Shortcuts category shell

Create a new Settings category:

```
Shortcuts
Command menu and global shortcuts
```

Add it to the Settings category list between **Device Info Sync** and **About**.

Create page:

```
SettingsShortcutsPage
```

Initial page structure:

```
Shortcuts
Configure global shortcuts and command menu actions.

Built-in shortcuts
Actions and shortcuts
```

No behavior yet, only navigation and layout.

------

### Step 2 — Add settings data model and defaults

Add persistent settings for:

```
type ShortcutBinding = {
  modifiers: string[];
  key: string;
};

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
commandMenu.enabled = true
commandMenu.binding = Win + O

voiceMode.enabled = false
voiceMode.binding = Win + V

actions = []
```

Voice Mode should be stored but not functional yet.

------

### Step 3 — Implement built-in shortcut accordion cards

Add two expandable cards:

```
openHAB Command Menu
Voice Mode
```

Command Menu card:

```
Toggle: On / Off
Description: Open the command menu anywhere and trigger saved actions.
Expanded content:
- Uses actions configured below.
- Global shortcut: Win + O
```

Voice Mode card:

```
Toggle: Off
Description: Planned voice shortcut, coming soon.
Expanded content:
- Coming soon badge
- Not implemented in this release.
- Disabled shortcut field
```

Do not show command menu preview here. Keep preview only in the action editor.

------

### Step 4 — Create reusable ShortcutRecorder component

Create:

```
ShortcutRecorder
```

Responsibilities:

```
Display binding as chips
Open recording mode
Capture keydown
Normalize modifiers
Save binding
Cancel recording
Show validation errors
```

Validation rules:

```
Must include at least one modifier
Must include a non-modifier key
Reject Escape-only, Enter-only, Tab-only
Reject duplicate binding inside app settings
Warn when OS registration fails later
```

Use this component for:

```
Command Menu shortcut
Voice Mode shortcut, disabled
User action shortcut
```

------

### Step 5 — Add action model

Add:

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

Add helper functions:

```
createDefaultShortcutAction()
validateShortcutAction()
formatShortcutBinding()
formatActionAvailability()
```

------

### Step 6 — Implement Actions table

Create:

```
ShortcutActionsTable
```

Columns:

```
Icon
Action name
Availability
Shortcut
Target item
Action type
Command value
Edit/Delete
```

Empty state:

```
No actions yet.
Add actions to make them available in the command menu.
```

Example rows may be used as mock/dev data only, not production defaults.

------

### Step 7 — Implement Add/Edit action panel

Create:

```
ShortcutActionEditor
```

Fields:

```
Action name
Icon
Show in command menu
Global shortcut optional
Target item
Command type
Command value / default
```

Behavior:

```
Selecting a row opens editor
Add action creates draft
Save validates and persists
Cancel discards draft
Delete asks confirmation
```

Command type UI:

```
Toggle              -> no command value needed
On/Off              -> dropdown ON / OFF
Open/Close          -> dropdown OPEN / CLOSE
Open slider         -> no command value
Open color picker   -> no command value
Send command        -> free text value
```

------

### Step 8 — Add icon picker

Create simple first version:

```
IconPicker
```

Initial icon set:

```
Light
Blinds
Brightness
Color
Scene
Power
Door
Thermostat
Media
Custom
```

Store icon as stable string ID:

```
"light" | "blinds" | "brightness" | ...
```

Use the selected icon in:

```
Actions table
Editor preview
Command menu
```

------

### Step 9 — Add item selector

Create:

```
OpenHabItemSelector
```

First implementation can be a searchable combo box using already available openHAB items.

Display:

```
Item label
Item name
Item type
```

Store:

```
targetItem = item.name
```

Later improvement:

```
Filter command types based on item type
```

Example:

```
Dimmer -> Open slider
Color -> Open color picker
Switch -> On/Off or Toggle
Rollershutter -> Open/Close
```

------

### Step 10 — Implement command menu action execution

Create action executor:

```
executeShortcutAction(action: ShortcutAction)
```

Behavior:

```
toggle:
- read current item state
- send opposite ON/OFF where possible

onOff:
- send ON or OFF

openClose:
- send OPEN or CLOSE

sendCommand:
- send configured commandValue

openSlider:
- open slider flyout for target item

openColorPicker:
- open color picker flyout for target item
```

If target item is missing:

```
Show error toast
Do not crash
```

------

### Step 11 — Implement command menu integration

Command menu should show actions where:

```
showInCommandMenu === true
```

Each command menu entry uses:

```
icon
name
target item
command type
```

Selecting entry calls:

```
executeShortcutAction(action)
```

Command menu opened by:

```
Win + O
Tray/menu button if available
Existing app UI entry if available
```

------

### Step 12 — Implement global shortcut registration

Register shortcuts at app startup and whenever settings change:

```
Command Menu shortcut
User action shortcuts
```

Do not register Voice Mode shortcut yet.

Rules:

```
If command menu disabled -> unregister its shortcut
If action has no shortcut -> do not register
If registration fails -> show inline warning in settings
If duplicate shortcut exists -> block save
```

------

### Step 13 — Add shortcut conflict detection

Detect conflicts between:

```
Command Menu shortcut
User action shortcuts
Future Voice Mode shortcut
Reserved internal app shortcuts
```

Show specific message:

```
This shortcut is already used by “Movie Night”.
```

For OS-level conflicts:

```
This shortcut could not be registered. It may already be used by Windows or another app.
```

------

### Step 14 — Add import/export-safe persistence

Ensure shortcuts/actions are included in existing app config backup/export if such system exists.

Add migration:

```
If shortcuts settings are missing, create defaults.
If old/invalid actions exist, skip invalid fields and preserve rest.
```

------

### Step 15 — Add tests

Add unit tests for:

```
Shortcut formatting
Shortcut validation
Conflict detection
Action validation
Command value mapping
Settings migration
```

Add UI tests for:

```
Open Shortcuts settings page
Toggle command menu shortcut
Edit shortcut binding
Add action
Edit action
Delete action
Show disabled Voice Mode card
```

------

### Step 16 — Polish and accessibility

Add:

```
Keyboard navigation
Accessible labels for toggles and shortcut edit buttons
Focus states
High contrast compatibility
Screen reader labels
Reduced motion compatibility
```

Make sure command menu and editor are usable without a mouse.

------

### Step 17 — Final QA checklist

Verify:

```
Win + O opens command menu
Disabling command menu unregisters shortcut
Changing Win + O updates registration
Command menu shows configured actions
Command menu hides actions with showInCommandMenu = false
Action shortcut executes without opening command menu
Voice Mode appears but cannot be enabled accidentally
Duplicate shortcuts are blocked
Invalid actions cannot be saved
Settings persist after app restart
```