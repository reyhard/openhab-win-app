# Language Selection Design

## Goal

Add Polish localization and a user-selectable app language setting. The default behavior remains system-driven localization, while users can override the app language to English or Polish.

## Scope

- Add a `pl-PL` WinUI resource file that mirrors the existing `en-US` resource keys.
- Add a persisted language setting to `OpenHab.App.Settings.AppSettings`.
- Add a language selector to the Windows settings UI, under Appearance.
- Apply the selected language during startup before localized text is read.
- Do not implement live language switching for already-loaded UI.

## Language Options

The UI selector contains:

- `System language` - no override; the app follows Windows language preferences.
- `English` - forces `en-US`.
- `Polski` - forces `pl-PL`.

The persisted representation should be stable and explicit. The recommended model is an enum-like value such as `System`, `English`, and `Polish`, or a small value object that normalizes to BCP-47 language tags. Unknown or invalid stored values must normalize back to `System`.

## Startup Application

The language setting is loaded from `AppSettingsController` before the app constructs shared localizers or major UI surfaces. If the setting is `System`, the app clears any override and lets Windows choose resources normally. If it is English or Polish, the app sets the WinUI resource/culture override before localized text is read.

This keeps `x:Uid` resource lookup, package manifest resources, and `WinUiTextLocalizer` aligned with the same language decision.

## Settings UI Behavior

The Appearance settings page gets a language combo box near the theme and icon options.

When the user changes the language:

- Save the new setting immediately.
- Keep the current running UI language unchanged.
- Show a restart-required information message only if the saved language setting differs from the language setting that was applied when this process started.
- Hide that message when the saved setting again matches the language setting currently loaded in the process.

Suggested message:

`Restart openHAB to apply the selected language.`

No live refresh is required or expected.

## Resource Fallback

The existing `WinUiTextLocalizer` fallback remains in place. Missing or failed WinUI resource lookups fall back to built-in English strings so localization problems do not crash the app.

Polish resources should have key parity with English resources. If a Polish string is missing during development, resource parity tests should fail.

## Testing

- `AppSettingsController` defaults language to `System`.
- Language settings round-trip through JSON.
- Invalid or unknown stored language values normalize to `System`.
- Changing language raises `SettingsChanged`.
- Polish `Resources.resw` has the same keys as English.
- `WinUiTextLocalizer` can resolve an injected Polish resource lookup in tests and still falls back to English on missing or failing lookup.
- Settings UI restart notice visibility is covered by a small deterministic helper rather than UI automation.
