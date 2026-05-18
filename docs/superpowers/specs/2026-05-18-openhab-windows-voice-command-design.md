# openHAB Windows Voice Command Design

Date: 2026-05-18

## Goal

Add Android-style voice commands to the Windows tray app. The v1 feature converts a short spoken phrase to text on Windows, then sends that text as a command to a configurable openHAB item.

The first implementation should be small, opt-in, and aligned with the existing shortcut/action architecture. It must not add native openHAB-to-Windows audio sink support, raw microphone streaming, bundled STT engines, wake-word listening, or success toasts.

## User Behavior

Voice commands are enabled through the existing Shortcuts settings area. When disabled, no voice entry points are present:

- No flyout microphone button.
- No registered voice hotkey.
- No Voice entry in the radial command menu.

When enabled, users can start a single-shot voice command from:

- A microphone button in the flyout header between Search and Minimize.
- A global shortcut configured on a Voice shortcut action.
- A Voice action in the radial command menu.

The default behavior is immediate send:

1. User activates voice.
2. The app listens once.
3. The app converts speech to text through Windows speech recognition.
4. The app sends the recognized phrase as a command to the configured openHAB item.
5. Normal success is quiet.

The user can enable `Require confirmation before sending`. When this option is on, recognition opens a compact confirmation surface with the transcript and Send/Cancel actions. The command is sent only after Send.

## Privacy

The settings UI must disclose the v1 privacy tradeoff clearly before users enable voice commands:

- Windows free-form dictation can use Microsoft online speech services.
- Voice commands require Windows microphone and speech-recognition permissions.
- Normal diagnostics do not log recognized phrases.
- Verbose diagnostics may log full recognized phrases to the local `diagnostics.log`.

Because spoken phrases can expose household details, normal logs should avoid transcript text. Verbose diagnostics are explicitly user-enabled and may include the phrase to support troubleshooting.

## Settings And Shortcut Model

The existing disabled Voice Mode stub becomes a minimal master toggle plus voice-specific options:

- Enable voice commands.
- Require confirmation before sending.
- Privacy/setup disclosure text.

Voice shortcut configuration belongs in Actions and shortcuts, not directly in the Voice Mode expander.

Add a new `ShortcutCommandType.Voice`.

When Voice Mode is enabled, the app creates a protected default Voice action if one does not already exist:

- ID: reserved stable ID such as `built-in.voice.default`.
- Name: `Voice command`.
- Icon: microphone/voice icon.
- Command type: `Voice`.
- Target item: `VoiceCommand`.
- Global shortcut: `Ctrl + Alt + I`.
- Show in command menu: enabled.
- Command value: unused.

The protected default Voice action can be edited, reordered, assigned or unassigned a shortcut, shown or hidden in the command menu, renamed, re-iconed, and pointed at another target item. It cannot be deleted. The delete button should be disabled or omitted for this action with a clear tooltip or status reason.

User-created additional Voice actions may be deleted normally.

Disabling Voice Mode does not delete the protected default Voice action. This preserves user edits for later re-enable.

## Voice Action Validation

Voice actions differ from other shortcut actions:

- `TargetItem` is required.
- `CommandValue` is ignored and not required.
- Voice is the only action type allowed to have neither a global shortcut nor command-menu visibility. This is allowed because the flyout microphone button can still invoke the protected default Voice action while Voice Mode is enabled.
- Other action types still require command-menu visibility, a global shortcut, or both.

Duplicate shortcut validation still includes Voice actions.

The Voice Mode master toggle is the outer gate:

- Voice action hotkeys register only when Voice Mode is enabled.
- Voice actions appear in the command menu only when Voice Mode is enabled and the action has `ShowInCommandMenu`.
- The flyout microphone button appears only when Voice Mode is enabled.

## Architecture

Keep the feature layered.

### OpenHab.App

Own UI-independent settings and logic:

- Extend shortcut settings normalization so Voice Mode is no longer forced disabled.
- Add Voice Mode settings for enabled state and confirmation requirement.
- Add default/protected Voice action creation.
- Add pure logic for validating, preserving, and preventing deletion of the protected Voice action.
- Add an execution planner/dispatcher that takes recognized text and a Voice action and sends the phrase to the action target item through `IOpenHabClient.SendCommandAsync`.

### OpenHab.Core

No new REST API is required for v1. Existing `SendCommandAsync(itemName, command)` can send the recognized phrase to the configured String item.

### OpenHab.Windows.Tray

Own Windows-specific recognition and surfaces:

- Add a Windows speech recognition service around `Windows.Media.SpeechRecognition.SpeechRecognizer`.
- Add flyout microphone button wiring, hidden when Voice Mode is disabled.
- Extend hotkey registration so Voice action shortcuts are registered only when Voice Mode is enabled.
- Extend the radial command menu input list to include Voice actions only when Voice Mode is enabled.
- Add a compact confirmation window only when confirmation is enabled.
- Coordinate concurrency so only one recognition or confirmation flow is active at a time.

`App.xaml.cs` should coordinate activation from flyout, hotkey, and radial menu. The flyout microphone button invokes the protected default Voice action.

## Execution Flow

1. A visible/registered voice entry point is activated.
2. The app resolves the selected Voice action. The flyout microphone button uses the protected default Voice action.
3. The app verifies openHAB is online and the Voice action has a valid target item.
4. The app starts single-shot Windows speech recognition.
5. If recognition returns non-empty text:
   - If confirmation is off, send the phrase immediately.
   - If confirmation is on, show the confirmation surface and wait for Send/Cancel.
6. On send, call `SendCommandAsync(targetItem, recognizedText, cancellationToken)`.
7. Normal success does not show a toast.

If an already queued activation fires after Voice Mode is disabled, it should no-op quietly as a defensive stale-path guard. This is not a normal user-facing validation path because disabled entry points are hidden and unregistered.

## Error Handling

Expected errors:

- Disconnected: do not start listening; show status that voice commands require an online openHAB connection.
- Missing microphone permission or Windows speech disabled: show status pointing to Windows microphone/speech settings.
- Speech recognizer unavailable: show status and log safe technical detail.
- No match, canceled, timeout, or empty transcript: do not send a command.
- Send failure: show status that the voice command could not be sent.
- Concurrent activation: ignore, or focus the existing confirmation surface if one is open.

Diagnostics should use existing privacy-safe logging conventions. Normal diagnostics omit phrases; verbose diagnostics may include full recognized phrases.

## Out Of Scope

The v1 design does not include:

- Native openHAB-to-Windows audio sink.
- Raw microphone streaming to openHAB.
- Server-side STT integration.
- Bundled Whisper, Vosk, or another local STT engine.
- Wake-word or always-listening mode.
- Success toasts.
- Main window voice button.
- A disabled receive-audio placeholder.

Future privacy-friendly options can be considered later, especially openHAB-side Whisper/Vosk STT or a local Windows STT backend. Those should be designed as a separate feature after the Android-style text-command path ships.

## Testing

Add focused unit coverage for UI-independent behavior:

- Voice Mode can be enabled and is not normalized back to disabled.
- Enabling Voice Mode creates the protected default Voice action with `Ctrl + Alt + I`, `VoiceCommand`, command type `Voice`, and command-menu visibility enabled.
- Re-enabling Voice Mode preserves edits to the protected default Voice action.
- Protected Voice action deletion is prevented by the shortcut action planning logic.
- `Voice` action validation does not require `CommandValue`.
- `Voice` is the only action type allowed without shortcut and without command-menu visibility.
- Duplicate shortcut validation includes Voice actions.
- Voice action execution sends the recognized phrase to the configured target item.
- Empty/no-match/canceled recognition does not send.
- Confirmation mode blocks send until approved.
- Normal diagnostics omit phrases; verbose diagnostics can include them.

Tray verification should include a Release build because the implementation touches WinUI, hotkeys, and likely microphone capability declarations.

Primary expected gates:

- `dotnet test OpenHab.Windows.sln`
- `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Release`

If package manifest capabilities change, also run:

- `.\build-package.ps1 -Configuration Release -Platform x64`
