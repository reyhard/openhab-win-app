# openHAB Windows App Guide Design

Date: 2026-05-20

## Purpose

Create a repo-local public user guide for the openHAB Windows companion app at `docs/app-guide.md`.

The guide should be useful to end users reading the GitHub repository. It should describe setup, daily use, feature behavior, examples, privacy notes, and troubleshooting without exposing developer implementation details.

The style should be close to the existing openHAB documentation: direct, practical Markdown with short explanatory paragraphs, feature sections, bullets, and example Item definitions where they remove ambiguity.

## Output

Primary page:

- `docs/app-guide.md`

Repository entry point:

- Link the guide from `README.md`.

The page should be written as public user documentation, not as a project status report or developer guide.

## Audience

The guide is for openHAB users who want to run the Windows companion app.

Assume the reader:

- uses openHAB or myopenHAB,
- may know openHAB Items, sitemaps, and Main UI at a basic level,
- may not know this app's distinction between tray flyout, main window, native sitemap rendering, and embedded Main UI,
- needs practical setup examples.

Do not assume the reader understands the repository's internal project split, runtime controllers, tests, or implementation architecture.

## Content Style

Use a style similar to openHAB documentation pages such as the Android app guide:

- practical section headings,
- concise paragraphs,
- bullets and tables for scanability,
- examples for setup and automation,
- links to upstream openHAB docs instead of duplicating long upstream tutorials.

Avoid:

- class names, project-layer names, test commands, or internal architecture,
- implementation-progress language,
- exhaustive matrices for behavior users naturally expect,
- developer backlog details.

The guide can include HTML comments for future visuals where they are natural insertion points:

```md
<!-- Screenshot idea: Tray flyout with sitemap search open. -->
```

These comments should not make the rendered page look unfinished.

## Page Structure

### 1. openHAB Windows App Guide

Introduce the app as a Windows 11 companion app for openHAB with:

- tray flyout,
- main window,
- embedded openHAB Main UI,
- native sitemap rendering,
- Windows notifications,
- command menu and shortcuts,
- Device Info Sync.

Keep the introduction short.

### 2. Requirements

Mention:

- Windows 11,
- WebView2 Runtime for embedded Main UI,
- reachable openHAB server,
- optional myopenHAB account for cloud access and notifications.

### 3. Connection Setup

Explain local and cloud endpoints with examples:

- `http://192.168.1.3:8080`
- `http://openhab:8080`
- `https://myopenhab.org`

Explain endpoint modes:

| Mode | Behavior |
| --- | --- |
| Automatic | Try local first, then use cloud if local is unavailable. |
| Local only | Use only the local endpoint. |
| Cloud only | Use only the cloud endpoint. |

Explain credentials:

- local API token for local openHAB access,
- myopenHAB username/password for cloud access.

Link to official openHAB documentation for API token creation rather than duplicating the full token workflow.

### 4. Tray Flyout And Main Window

Make the distinction prominent:

- Tray flyout is for quick native sitemap access from the Windows tray.
- Main window is for embedded openHAB Main UI, Settings, Notifications, promoted Main UI pages, and an optional native sitemap pane.

State clearly:

- The flyout currently uses native sitemap rendering.
- The flyout does not currently host openHAB Main UI.

Add a future screenshot placeholder near this section.

### 5. Sitemaps

Describe the native sitemap renderer as support for openHAB sitemap use in the app.

Call out user-visible extras:

- breadcrumbs and subpage navigation,
- sitemap search.

Do not list expected sitemap basics such as item commands, live updates, charts, or icons as separate special features.

### 6. Notifications

Explain that the app can show openHAB Cloud notifications as Windows notifications when cloud credentials and notification polling are configured.

Cover:

- Windows toast notifications,
- in-app notification inbox,
- searching and filtering notifications,
- read and hidden notification states,
- tags and reference IDs,
- rich media when available,
- action buttons,
- log-only notifications,
- hide/remove notification behavior.

Mention Local-only mode limitation:

- New cloud notifications are unavailable in Local-only mode.

Include supported action examples:

```text
command:LivingRoom_Light:ON
ui:/
ui:navigate:/page/my_page
https://www.openhab.org
```

Explain these at user level:

- `command:*` sends a command to an Item,
- `ui:*` opens or navigates openHAB UI content,
- `http://` and `https://` open web links.

### 7. Command Menu And Shortcuts

Explain the command menu as a radial menu opened by a configurable global shortcut.

Explain action placement:

- an action can appear in the command menu only,
- an action can run from its own global shortcut only,
- an action can appear in the command menu and also have its own global shortcut.

Mention:

- the command menu itself has a configurable global shortcut,
- Windows or another app may reserve or intercept some shortcuts, especially some Windows-key combinations,
- shortcuts remain configurable even if openHAB is disconnected, but execution depends on an active connection.

Document available action types:

| Action type | What it does |
| --- | --- |
| Toggle | Reads an Item state and sends the opposite `ON`/`OFF` command when the state is known. |
| On / Off | Sends either `ON` or `OFF`. |
| Open / Close | Sends either `OPEN` or `CLOSE`. |
| Open slider | Opens a compact slider for a numeric or dimmer-style Item. |
| Open color picker | Opens a compact color picker for color-style Items. |
| Send command | Sends custom command text to the target Item. |
| Voice | Starts voice command mode when Voice Mode is enabled. |

Voice Mode wording:

- Mention that a Voice action can appear in the shortcuts/actions list.
- It is controlled by Voice Mode settings.
- Behavior depends on Windows speech availability and app configuration.
- Present it as one available action type, not as the main way to use the app.

Include examples:

| Example action | Placement | Type |
| --- | --- | --- |
| Movie scene | Command menu and global shortcut | `Send command` to `MovieScene` with `ON` |
| Desk lamp | Command menu only | `Toggle` on `Desk_Lamp` |
| Volume | Command menu only | `Open slider` on `Media_Volume` |
| Listen | Command menu or global shortcut | `Voice`, when Voice Mode is enabled |

### 8. Device Info Sync

Make this one of the more detailed feature sections.

Explain:

- Device Info Sync is opt-in.
- No device information is sent until enabled and mapped.
- Each signal can be mapped to an openHAB Item.
- Blank mappings disable individual signals.
- The device identifier is used to generate default Item names.
- The app uses the active openHAB connection rather than a separate server profile.
- Sync happens on startup, on the configured interval, and after relevant Windows or connection events.
- Failures should not block sitemap browsing, commands, notifications, or other app use.

List current signals:

| Signal | Typical openHAB Item | Values |
| --- | --- | --- |
| Battery level | `Number` | `0` to `100`, omitted when unavailable |
| Charging state | `Switch` | `ON` or `OFF` |
| Locked state | `Switch` | `ON` or `OFF` |
| Session state | `String` | `active`, `locked`, `sleep`, `resume`, `unknown` |
| Wi-Fi connected | `Switch` | `ON` or `OFF` |
| Wi-Fi name | `String` | SSID or `UNDEF` |
| Bluetooth connected | `Switch` | `ON` or `OFF` |
| Bluetooth device names | `String` | connected device names or `UNDEF` |
| openHAB connection | `String` | `online`, `degraded`, `offline`, `unknown` |
| Focus / DND | `Switch` or `String` | `ON`, `OFF`, or `UNSUPPORTED` |

Include example Item definitions using a generic `Desk` device identifier.

Include privacy notes:

- no Windows username,
- no IP address,
- no MAC address,
- no BSSID,
- no credentials,
- no tokens.

### 9. Appearance And Language

Cover:

- sitemap skin selection,
- icon style,
- theme behavior,
- language selection,
- restart-required note for language changes that are not live.

### 10. Privacy And Diagnostics

Mention local app state path:

```text
%LocalAppData%\OpenHab.WinApp
```

Mention useful files:

- `diagnostics.log`,
- `task-crash.log`,
- `settings.json`,
- `notifications.json`.

Explain:

- settings and notification history are stored locally,
- diagnostics are intended to redact credentials, tokens, passwords, and sensitive endpoint details,
- users should avoid posting full logs publicly because logs can still include private server data such as Item names or notification content.

### 11. Troubleshooting

Include practical entries for:

- connection failures,
- local API token issues,
- cloud username/password issues,
- Main UI or WebView2 not loading,
- notifications not appearing,
- Local-only mode and notifications,
- shortcut registration failures,
- Windows-key shortcut limitations,
- Device Info Sync Item names and Item types,
- Device Info Sync endpoint availability,
- finding logs.

### 12. Current Limitations

Keep this section short and user-facing:

- The app is not yet an official release-ready openHAB distribution.
- The tray flyout does not currently host Main UI.
- Cloud notifications depend on cloud credentials and polling.

Do not mention:

- media cache policy,
- sitemap search scope details,
- internal release blockers,
- test/build limitations.

## Links To Include

Use official openHAB docs where practical:

- openHAB Android app guide as style inspiration, not as a Windows behavior source.
- openHAB API token documentation.
- openHAB sitemaps documentation.
- openHAB Main UI documentation.
- openHAB Cloud notification documentation if linking to notification action syntax.

Only include links that help users complete setup or understand openHAB concepts.

## README Integration

Add a short link under the README features or current status area:

```md
For setup and feature usage, see [App Guide](docs/app-guide.md).
```

Do not turn the README into the full guide.

## Acceptance Criteria

- `docs/app-guide.md` exists and is written for users.
- `README.md` links to the guide.
- The guide explains connection setup with local token and cloud credential usage.
- The guide clearly distinguishes tray flyout and main window.
- The guide states that the tray flyout does not currently host Main UI.
- The sitemap section mentions native sitemap use, breadcrumbs, and search without over-documenting expected basics.
- The notifications section covers inbox, Windows toasts, cloud polling, actions, and Local-only limitations.
- The command menu section covers action placement, shortcuts, all available action types, and Voice action behavior.
- The Device Info Sync section documents all current signals, sync behavior, Item mappings, and privacy constraints.
- Troubleshooting includes Windows-key shortcut limitations.
- The guide avoids developer implementation details.
- The guide avoids media cache policy and sitemap search scope details.

## Out Of Scope

- Creating screenshots or GIFs in this implementation.
- Moving the guide to the upstream openHAB documentation site.
- Creating a multi-page manual.
- Documenting developer build, test, or architecture workflow.
- Adding new app features.
