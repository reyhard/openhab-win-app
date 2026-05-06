Yes — I would split this into **parallel subagent workstreams**, with a shared contract-first approach so people can work independently without blocking each other.

Important note on notifications: the Android app does **not** just receive arbitrary local openHAB events directly. The official Android README says notifications are received through an **openHAB Cloud connection**, and the F-Droid flavor removes FCM and therefore cannot receive push notifications from openHAB Cloud in the same way. ([GitHub](https://github.com/openhab/openhab-android)) The openHAB Cloud Connector documentation also states that openHAB Cloud acts as a connector to Firebase Cloud Messaging for mobile app notifications. ([openhab.org](https://www.openhab.org/addons/integrations/openhabcloud/?utm_source=chatgpt.com))

For Windows, we need to design this carefully because the Android path is based around **FCM device registration**, while Windows native notifications normally use **local App Notifications** or **Windows Push Notification Services**. Microsoft’s Windows App SDK supports local app notifications and notification actions. ([learn.microsoft.com](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/?utm_source=chatgpt.com))

------

# 1. Subagent plan

## Agent A — Product / UX / Interaction Owner

Responsibility:

```text
Own the Windows 11 experience:
- tray icon behavior
- flyout layout
- main app shell
- notification UX
- widgets UX
- settings UX
- first-run setup
```

Deliverables:

```text
/design/flyout-spec.md
/design/main-app-spec.md
/design/notification-spec.md
/design/widget-spec.md
/design/settings-spec.md
/design/keyboard-accessibility.md
```

Key decisions:

```text
Flyout width / height
Right-side tray anchoring behavior
Close-on-focus-lost behavior
Pinned shortcuts layout
Sitemap vs Main UI tab behavior
Offline / reconnect states
Notification action labels
```

Acceptance criteria:

```text
Designer/dev can implement without asking how each screen should behave.
Every visible control has states: loading, success, error, offline, disabled.
```

------

## Agent B — Windows Shell Integration Agent

Responsibility:

```text
Build the Windows-specific host:
- system tray icon
- tray context menu
- flyout positioning
- single-instance app behavior
- startup with Windows
- protocol activation
- notification activation
```

Deliverables:

```text
src/OpenHab.Windows.App/Services/TrayIconService.cs
src/OpenHab.Windows.App/Services/FlyoutPositioningService.cs
src/OpenHab.Windows.App/Services/StartupService.cs
src/OpenHab.Windows.App/Services/ProtocolActivationService.cs
src/OpenHab.Windows.App/Services/SingleInstanceService.cs
```

Acceptance criteria:

```text
openHAB icon is visible near the right-side tray area.
Left-click opens flyout anchored to the tray area.
Right-click opens menu: Open, Settings, Reload, Exit.
App does not create duplicate instances.
Flyout opens on the correct monitor and respects DPI scaling.
```

------

## Agent C — openHAB API / Core Integration Agent

Responsibility:

```text
Implement communication with local openHAB and myopenHAB:
- REST API client
- authentication
- item commands
- sitemap loading
- Main UI URL handling
- event stream
- connection state
```

Deliverables:

```text
src/OpenHab.Windows.Core/Api/OpenHabRestClient.cs
src/OpenHab.Windows.Core/Api/OpenHabCloudClient.cs
src/OpenHab.Windows.Core/Api/OpenHabEventStreamClient.cs
src/OpenHab.Windows.Core/Models/OpenHabItem.cs
src/OpenHab.Windows.Core/Models/OpenHabSitemap.cs
src/OpenHab.Windows.Core/Models/OpenHabServerProfile.cs
src/OpenHab.Windows.Core/Contracts/IOpenHabClient.cs
```

Acceptance criteria:

```text
Can connect to local openHAB server.
Can connect to myopenHAB remote URL.
Can fetch items.
Can send commands.
Can load available sitemaps.
Can detect online/offline state.
Can reconnect without freezing UI.
```

------

## Agent D — Notification / Cloud Agent

Responsibility:

```text
Investigate and implement notification transport:
- Android app behavior analysis
- myopenHAB device registration API
- Windows notification abstraction
- local notification rules
- cloud notification polling or push strategy
- notification action routing
```

Deliverables:

```text
docs/notifications-cloud-research.md
src/OpenHab.Windows.Core/Notifications/NotificationEnvelope.cs
src/OpenHab.Windows.Core/Notifications/NotificationRegistrationService.cs
src/OpenHab.Windows.Core/Notifications/OpenHabCloudNotificationClient.cs
src/OpenHab.Windows.App/Services/WindowsNotificationService.cs
src/OpenHab.Windows.Tests/NotificationRuleTests.cs
```

Initial finding from Android:

The Android app registers itself with openHAB Cloud using an endpoint shaped like:

```text
addAndroidRegistration?deviceId=<id>&deviceModel=<name>&regId=<fcm-token>
```

This appears in the Android notification registration worker discussed by the openHAB community, where the app gets an FCM token and sends it to the cloud registration endpoint. ([openHAB Community](https://community.openhab.org/t/manually-register-devices-on-myopenhab/146922?utm_source=chatgpt.com))

The Android app also checks a notification settings endpoint such as:

```text
/api/v1/settings/notifications
```

before registering with cloud, according to logs in an Android app issue. ([GitHub](https://github.com/openhab/openhab-android/issues/2606?utm_source=chatgpt.com))

Design implication:

```text
For Windows we should not pretend to be Android unless the openHAB Cloud backend officially supports that.
```

Recommended notification strategy:

```text
Phase 1:
- Local Windows notifications from local/remote event stream.
- Works without myopenHAB push registration.
- Good for users whose PC is online and app is running.

Phase 2:
- myopenHAB notification inbox polling.
- Similar in spirit to the F-Droid fallback, which community discussion says polls openHAB Cloud rather than using FCM.
- Good enough for Windows because desktop apps can run in tray.

Phase 3:
- Proper cloud push support.
- Requires either:
  a) openHAB Cloud support for Windows/WNS registration, or
  b) a new openHAB Cloud endpoint for generic desktop notification registration.
```

Community discussion says the Play Store Android app uses FCM and the F-Droid version polls for new notifications. ([openHAB Community](https://community.openhab.org/t/android-app-send-pop-up-notification-from-app-to-the-phone-notification-screen-when-item-state-changes/153276?utm_source=chatgpt.com)) That makes polling a practical Windows MVP path while avoiding unofficial FCM hacks.

Acceptance criteria:

```text
Local openHAB item-change notification works.
Cloud notification polling works against myopenHAB if authenticated.
Windows toast can open flyout/main app.
Notification buttons can trigger actions.
Notification history appears in app.
No token or private event payload appears in logs.
```

------

## Agent E — Flyout / Native UI Agent

Responsibility:

```text
Build the native tray flyout UI:
- Sitemap tab
- Main UI tab entry
- search
- shortcut buttons
- native controls
- cached state rendering
```

Deliverables:

```text
src/OpenHab.Windows.App/Views/TrayFlyoutWindow.xaml
src/OpenHab.Windows.App/ViewModels/TrayFlyoutViewModel.cs
src/OpenHab.Windows.App/Controls/DeviceRow.xaml
src/OpenHab.Windows.App/Controls/ShortcutButton.xaml
src/OpenHab.Windows.App/Controls/ConnectionStatusBadge.xaml
```

Acceptance criteria:

```text
Flyout renders immediately using cached state.
Controls update after REST/event-stream refresh.
Toggle, dimmer, rollershutter, contact, number, string items render correctly.
Search filters locally.
Shortcut buttons execute commands.
Offline state is visible but does not crash.
```

------

## Agent F — Main App / WebView Agent

Responsibility:

```text
Build the larger main app:
- sidebar
- overview
- embedded Main UI
- embedded Sitemap fallback
- settings
- diagnostics page
```

Deliverables:

```text
src/OpenHab.Windows.App/Views/MainWindow.xaml
src/OpenHab.Windows.App/Views/SettingsWindow.xaml
src/OpenHab.Windows.App/Views/MainUiPage.xaml
src/OpenHab.Windows.App/Views/SitemapPage.xaml
src/OpenHab.Windows.App/ViewModels/MainViewModel.cs
src/OpenHab.Windows.App/Services/WebViewSessionService.cs
```

Acceptance criteria:

```text
Main UI opens in WebView2.
Sitemap can open in native mode or WebView fallback.
User can switch server profile.
User can open diagnostics.
WebView is not kept alive unnecessarily when app is minimized to tray.
```

------

## Agent G — Storage / Security Agent

Responsibility:

```text
Persist settings safely:
- server profiles
- secure token storage
- notification rules
- shortcut definitions
- widget config
- settings migrations
```

Deliverables:

```text
src/OpenHab.Windows.Core/Storage/SettingsService.cs
src/OpenHab.Windows.Core/Storage/ShortcutStore.cs
src/OpenHab.Windows.Core/Storage/NotificationRuleStore.cs
src/OpenHab.Windows.Core/Security/CredentialService.cs
src/OpenHab.Windows.Core/Security/CertificateTrustService.cs
```

Acceptance criteria:

```text
Tokens are not stored in plain JSON.
Settings migrations are tested.
Self-signed certificates require explicit user approval.
Logs redact URLs/tokens/passwords.
```

------

## Agent H — Widgets Agent

Responsibility:

```text
Implement optional Windows Widgets support:
- widget provider
- widget cards
- cached state feed
- quick scene actions
```

Deliverables:

```text
src/OpenHab.Windows.Widgets/WidgetProvider.cs
src/OpenHab.Windows.Widgets/WidgetDefinitions/HomeWeatherWidget.json
src/OpenHab.Windows.Widgets/WidgetDefinitions/EnergyWidget.json
src/OpenHab.Windows.Widgets/WidgetDefinitions/SecurityWidget.json
src/OpenHab.Windows.Widgets/WidgetDefinitions/QuickScenesWidget.json
```

Windows widget providers are supported for packaged Win32 apps and PWAs, so this should be a later milestone after MSIX packaging is stable. ([learn.microsoft.com](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/?utm_source=chatgpt.com))

Acceptance criteria:

```text
Widget provider registers correctly.
Widgets render cached state.
Actions open app or send command.
Widget feature can be disabled.
```

------

## Agent I — Test / Automation / CI Agent

Responsibility:

```text
Create automated test infrastructure:
- unit tests
- integration tests
- UI automation tests
- mock openHAB server
- notification tests
- packaging tests
- CI pipeline
```

Deliverables:

```text
tests/OpenHab.Windows.UnitTests/
tests/OpenHab.Windows.IntegrationTests/
tests/OpenHab.Windows.UiTests/
tests/OpenHab.TestServer/
.github/workflows/build.yml
.github/workflows/test.yml
.github/workflows/package.yml
docs/testing.md
```

Acceptance criteria:

```text
Tests run from command line.
CI builds app.
CI runs unit and integration tests.
Nightly workflow runs UI tests on Windows runner.
Mock openHAB server supports items, commands, sitemaps, events, notification endpoints.
```

Microsoft has official guidance for testing Windows App SDK / WinUI 3 apps, including unit testing with MSTest, and Visual Studio includes a Unit Test App template for WinUI-based apps. ([learn.microsoft.com](https://learn.microsoft.com/en-us/windows/apps/develop/testing/?utm_source=chatgpt.com))

------

# 2. Workstream sequencing

## Phase 0 — Shared contracts

All agents start from contracts, not implementation.

```text
Duration: 1 sprint
Owner: Tech lead + all agents
```

Create:

```text
IOpenHabClient
INotificationService
ITrayIconService
ISettingsService
IShortcutService
IEventStreamClient
ICloudNotificationClient
```

Also create shared models:

```text
OpenHabItem
OpenHabCommand
OpenHabServerProfile
SitemapNode
ShortcutDefinition
NotificationRule
NotificationEnvelope
ConnectionState
```

This allows UI, API, notifications, and tests to progress in parallel.

------

## Phase 1 — MVP skeleton

Parallelizable:

```text
Agent B: tray + flyout host
Agent C: REST client
Agent E: flyout UI
Agent G: settings + credentials
Agent I: mock server + unit test setup
```

Output:

```text
Tray app connects to mock openHAB and toggles fake items.
```

------

## Phase 2 — Real openHAB integration

Parallelizable:

```text
Agent C: real REST/event stream
Agent E: native item rendering
Agent F: Main UI WebView
Agent I: integration tests using mock + optional real container
```

Output:

```text
User can connect to real local openHAB and control items.
```

------

## Phase 3 — Notifications

Parallelizable:

```text
Agent D: notification research + cloud polling prototype
Agent B: protocol activation
Agent I: notification automation harness
Agent E/F: open item from notification
```

Output:

```text
Local notifications and cloud-polled notifications appear as native Windows notifications.
```

------

## Phase 4 — Polish and widgets

Parallelizable:

```text
Agent A: final UX polish
Agent H: widgets
Agent I: packaging and smoke tests
Agent G: privacy/security review
```

Output:

```text
Packaged beta build.
```

------

# 3. Testing strategy

The key is to test most behavior **without a real openHAB server**, then run a smaller set of tests against a real or containerized openHAB.

## Test pyramid

```text
Many unit tests
↓
Several integration tests with mock openHAB server
↓
Few UI automation tests
↓
Few manual exploratory tests
```

Do not rely mostly on manual testing. Tray apps, reconnect behavior, notification actions, and DPI/multi-monitor cases break easily.

------

# 4. Automated test projects

Recommended test layout:

```text
tests/
├─ OpenHab.Windows.UnitTests/
│  ├─ OpenHabRestClientTests.cs
│  ├─ SitemapParserTests.cs
│  ├─ NotificationRuleMatcherTests.cs
│  ├─ ShortcutCommandTests.cs
│  ├─ SettingsMigrationTests.cs
│  └─ CredentialRedactionTests.cs
│
├─ OpenHab.Windows.IntegrationTests/
│  ├─ MockServerConnectionTests.cs
│  ├─ EventStreamReconnectTests.cs
│  ├─ CloudNotificationPollingTests.cs
│  ├─ CommandRoundTripTests.cs
│  └─ WebViewUrlResolutionTests.cs
│
├─ OpenHab.Windows.UiTests/
│  ├─ TrayFlyoutTests.cs
│  ├─ MainWindowTests.cs
│  ├─ SettingsTests.cs
│  └─ NotificationActivationTests.cs
│
└─ OpenHab.TestServer/
   ├─ Program.cs
   ├─ ItemsController.cs
   ├─ SitemapController.cs
   ├─ EventsController.cs
   ├─ CloudNotificationsController.cs
   └─ TestData/
      ├─ items.json
      ├─ sitemap-home.json
      └─ notifications.json
```

------

# 5. Unit tests

Use these for deterministic business logic.

## API client tests

Test:

```text
Builds correct URLs
Adds authorization headers
Serializes commands correctly
Handles 401 / 403 / 404 / 500
Handles offline server
Redacts secrets from errors
```

Example test cases:

```text
SendCommandAsync("LivingRoom_Light", "ON")
→ POST /rest/items/LivingRoom_Light
→ body "ON"

GetItemsAsync()
→ GET /rest/items
→ maps JSON to OpenHabItem[]
```

## Sitemap parser tests

Test:

```text
Frame nesting
Groups
Switch
Slider
Selection
Setpoint
Rollershutter
Icons
Visibility flags
Mappings
Unsupported widget fallback
```

## Notification rule tests

Test:

```text
Item changed CLOSED → OPEN triggers rule.
Item changed OPEN → CLOSED does not trigger wrong rule.
Quiet hours suppress non-critical notification.
Critical rule overrides quiet hours.
Notification action opens expected route.
```

## Settings migration tests

Test:

```text
v1 settings migrate to v2
Unknown fields are preserved or ignored safely
Corrupt settings file falls back gracefully
Credentials are never copied into logs
```

------

# 6. Mock openHAB server

This is important. Build a small test server that behaves like enough of openHAB and myopenHAB to test the app.

## Mock endpoints

```http
GET  /rest/items
GET  /rest/items/{itemName}
POST /rest/items/{itemName}
GET  /rest/sitemaps
GET  /rest/sitemaps/{sitemapName}
GET  /rest/events
GET  /rest/things
GET  /basicui/app?sitemap=home
GET  /overview
```

## Mock cloud endpoints

```http
GET  /api/v1/settings/notifications
GET  /api/v1/notifications
POST /api/v1/notifications/{id}/hide
POST /api/v1/notifications/{id}/ack
```

## Android-compatible research endpoints

Only for research/prototyping, not necessarily final production:

```http
GET /addAndroidRegistration?deviceId=...&deviceModel=...&regId=...
```

This endpoint is known from Android registration behavior, but Windows should not depend on Android registration unless openHAB Cloud maintainers confirm it is acceptable. ([openHAB Community](https://community.openhab.org/t/manually-register-devices-on-myopenhab/146922?utm_source=chatgpt.com))

------

# 7. Integration tests

Integration tests should launch the mock server and test real HTTP flows.

## Example scenarios

```text
Connect to server
→ app calls /rest/items
→ connection state becomes Connected

Send command
→ app POSTs command
→ mock server updates item state
→ app receives event
→ UI model updates

Server goes offline
→ event stream drops
→ app marks Offline
→ reconnect timer starts
→ server returns
→ state recovers

Cloud notification polling
→ mock cloud has new notification
→ app polls /api/v1/notifications
→ notification envelope is created
→ Windows notification service receives ShowAsync call
```

For notification tests, abstract the Windows notification API behind:

```csharp
public interface IWindowsNotificationPlatform
{
    Task ShowAsync(NotificationEnvelope envelope);
    Task RemoveAsync(string notificationId);
}
```

Then integration tests use a fake implementation, not the real Windows notification center.

------

# 8. UI automation tests

UI tests should be limited but cover critical journeys.

Microsoft documents testing Windows App SDK / WinUI 3 apps and recommends automated validation as part of app development. ([learn.microsoft.com](https://learn.microsoft.com/en-us/windows/apps/develop/testing/?utm_source=chatgpt.com)) MSTest has WinUI-related support, and MSTest 3.4 added support for WinUI applications in MSTest.Runner. ([Microsoft for Developers](https://devblogs.microsoft.com/dotnet/introducing-mstest-34/?utm_source=chatgpt.com))

Recommended approach:

```text
MVP:
- ViewModel tests heavily
- A few WinUI MSTest UI-thread tests for controls
- Optional WinAppDriver/Appium-style E2E tests only for smoke flows

Later:
- Full UI automation for release candidates
```

## UI test cases

```text
Open app
→ first-run setup appears

Enter server URL/token
→ save
→ main app opens

Click tray icon
→ flyout appears

Search "light"
→ only light items visible

Click "All lights off"
→ command sent

Switch Sitemap/Main UI tab
→ correct content appears

Open Settings
→ profile is visible

Offline server
→ connection badge changes

Notification click
→ app opens target item/page
```

## Making UI tests reliable

Add automation IDs to all important controls:

```xml
<Button
    x:Name="AllLightsOffButton"
    AutomationProperties.AutomationId="Shortcut_AllLightsOff" />

<TextBox
    AutomationProperties.AutomationId="SearchBox" />

<ListView
    AutomationProperties.AutomationId="FlyoutItemList" />
```

Add a test mode:

```text
--test-mode
--mock-server-url=http://localhost:5055
--disable-real-notifications
--reset-profile
```

This avoids relying on real user settings or real notifications during CI.

------

# 9. Notification testing

Notifications need separate layers.

## Layer 1 — Rule matching

Pure unit tests:

```text
Given item event
Given notification rules
Expect notification envelope or none
```

## Layer 2 — Cloud polling

Integration tests with mock myopenHAB:

```text
Mock /api/v1/settings/notifications returns enabled
Mock /api/v1/notifications returns one unread notification
App maps it to NotificationEnvelope
App marks it handled/hidden after action
```

## Layer 3 — Windows notification adapter

Use fake adapter in normal CI:

```text
FakeWindowsNotificationPlatform
→ records notification title/body/actions
→ tests assert content
```

Use real Windows App SDK notification smoke test only on Windows runner or manual release validation:

```text
Create notification
Click action
Verify protocol activation handler receives arguments
```

Windows App SDK notifications can launch the app when clicked or trigger app-defined actions, which is exactly what we need for “View”, “Open UI”, “Dismiss”, and “Run scene” behavior. ([learn.microsoft.com](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/?utm_source=chatgpt.com))

------

# 10. myopenHAB notification plan

## What Android appears to do

From the current public information:

```text
Play Store/full Android build:
- Uses Firebase Cloud Messaging.
- Registers a device/token with openHAB Cloud.
- Receives push notifications.

F-Droid/foss build:
- FCM removed.
- Does not receive push notifications the same way.
- Community discussion says it polls openHAB Cloud for new notifications.
```

The Android README confirms the app can receive notifications through an openHAB Cloud connection, and that the F-Droid/foss flavor has FCM removed and cannot receive push notifications from openHAB Cloud. ([GitHub](https://github.com/openhab/openhab-android))

## Windows recommendation

For this Windows app, implement notifications in this order:

### Step 1 — Local event-driven notifications

```text
Connect to local openHAB or myopenHAB REST/event stream.
User defines notification rules.
App shows local Windows notifications.
```

Pros:

```text
No cloud backend changes.
Works fast.
Easy to test.
Privacy-friendly.
```

Cons:

```text
Only works while PC app is running.
Does not receive historical myopenHAB push inbox unless separately polled.
```

### Step 2 — Cloud notification polling

```text
Authenticate against myopenHAB.
Poll notification endpoint periodically.
Show unread notifications as Windows notifications.
Acknowledge/hide after action.
```

Pros:

```text
Matches the F-Droid-style fallback concept.
No Windows push infrastructure needed.
Works well for tray app.
```

Cons:

```text
Not instant.
Needs careful rate limiting.
Endpoint behavior must be confirmed against current myopenHAB/openHAB Cloud implementation.
```

### Step 3 — Official Windows push support

```text
Add openHAB Cloud support for Windows device registrations.
Use Windows Push Notification Services or a generic desktop push channel.
Register Windows device with cloud.
Receive push even when app is not actively polling, depending on Windows constraints.
```

Pros:

```text
Clean long-term design.
Real push notifications.
No Android impersonation.
```

Cons:

```text
Requires openHAB Cloud backend changes.
More packaging/signing/AAD/Partner Center complexity.
```

I would **not** make Windows app register as Android with a fake FCM token. It is brittle, possibly incompatible with myopenHAB policy, and likely to break. The Android endpoint is useful as research, not as the Windows production contract.

------

# 11. CI pipeline

## Pull request pipeline

Run on every PR:

```yaml
name: PR

on:
  pull_request:

jobs:
  build-test:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore
        run: dotnet restore OpenHab.Windows.sln

      - name: Build
        run: dotnet build OpenHab.Windows.sln --configuration Release --no-restore

      - name: Unit tests
        run: dotnet test tests/OpenHab.Windows.UnitTests/OpenHab.Windows.UnitTests.csproj --configuration Release --no-build

      - name: Integration tests
        run: dotnet test tests/OpenHab.Windows.IntegrationTests/OpenHab.Windows.IntegrationTests.csproj --configuration Release --no-build
```

## Nightly pipeline

Run heavier tests nightly:

```text
Build Release
Run unit tests
Run integration tests
Run UI smoke tests
Build MSIX package
Run package install smoke test
Upload artifacts
```

## Release pipeline

```text
Build signed MSIX
Run smoke tests
Generate changelog
Publish GitHub release artifact
Optionally submit to Microsoft Store
Optionally generate winget manifest
```

------

# 12. Autotest matrix

## Unit

```text
Every PR
Fast
No Windows UI dependency where possible
```

## WinUI unit/UI-thread tests

```text
Every PR or nightly
Windows runner only
Tests XAML controls, converters, viewmodels with dispatcher
```

## Integration

```text
Every PR
Mock server
Real HTTP
No real openHAB credentials
```

## UI smoke

```text
Nightly
Windows runner
Test mode
Mock server
```

## Real-device/manual

```text
Before beta release
Windows 11 laptop
High DPI monitor
Multi-monitor setup
Local openHAB
myopenHAB account
Self-signed HTTPS
Offline/reconnect
Sleep/resume
Explorer restart
```

------

# 13. Subagent ownership table

| Area                | Owner   | Can work in parallel? | Blocks                           |
| ------------------- | ------- | --------------------- | -------------------------------- |
| UX specs            | Agent A | Yes                   | Helps all UI work                |
| Tray integration    | Agent B | Yes                   | Needed for MVP shell             |
| REST/event API      | Agent C | Yes                   | Needed by flyout, notifications  |
| Notifications/cloud | Agent D | Yes                   | Needs API contracts              |
| Flyout UI           | Agent E | Yes                   | Needs mock data contracts        |
| Main app/WebView    | Agent F | Yes                   | Mostly independent               |
| Storage/security    | Agent G | Yes                   | Needed before real credentials   |
| Widgets             | Agent H | Later                 | Depends on packaging/state cache |
| Testing/CI          | Agent I | Yes                   | Should start immediately         |

------

# 14. Definition of done for MVP

```text
1. App installs on Windows 11.
2. App starts minimized to right-side tray.
3. Clicking tray icon opens flyout near time/network/sound area.
4. User can configure local openHAB or myopenHAB URL.
5. App securely stores credentials/token.
6. Flyout shows cached item states.
7. User can search items.
8. User can execute shortcut buttons.
9. User can open Main UI in WebView2.
10. User can receive local Windows notifications from item events.
11. Cloud notification polling prototype works against mock server.
12. Unit/integration tests pass in CI.
13. No secrets appear in logs.
14. App survives server offline/reconnect.
15. App exits cleanly and removes tray icon.
```

For the first beta, I would target **local event notifications + cloud polling**, not real cloud push. Real cloud push should be coordinated with openHAB Cloud maintainers so Windows has a proper registration path instead of borrowing Android’s FCM-specific flow.