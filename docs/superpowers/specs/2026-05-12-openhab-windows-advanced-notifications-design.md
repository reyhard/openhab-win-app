# openHAB Windows Advanced Notifications Design

Date: 2026-05-12

## Goal

Make openHAB Cloud notifications behave like first-class Windows app notifications when openHAB rules use advanced notification options: custom title, tag, reference id, media attachment, click action, action buttons, hide/remove notifications, and log-only notifications.

## Context

The current Windows app polls `GET /api/v1/notifications`, stores notifications in `OpenHab.App.Notifications.NotificationStore`, and displays Windows toasts through `OpenHab.Windows.Notifications.ToastService`.

The app already has partial support for flat fields:

- `CloudNotification.Title`
- `CloudNotification.ReferenceId`
- `CloudNotification.OnClickAction`
- `CloudNotification.MediaAttachmentUrl`
- `CloudNotification.ActionButton1` through `ActionButton3`

openHAB Cloud currently persists the original notification body in a nested `payload` object. Advanced fields can arrive as payload fields, including:

- `payload.title`
- `payload.tag` or `payload.severity`
- `payload["reference-id"]`
- `payload["media-attachment-url"]`
- `payload.actions`
- `payload.type`

The openHAB Cloud Connector documentation defines advanced notification actions for `sendNotification` and `sendBroadcastNotification`, while `sendLogNotification` creates log entries without device push. Microsoft documents Windows app notification hero images through hero placement and buttons through toast action elements.

## Requirements

1. Custom notification title must be shown as the Windows notification title.
2. Tag must remain available for grouping, filtering, importance matching, and notification history display.
3. Reference id must replace/update existing client-side notifications that carry the same reference id.
4. Hide/remove notifications must hide matching entries by reference id or tag.
5. Media attachments must render as a Windows hero image when the attachment can be resolved to a local app-notification-safe URI.
6. Action buttons must render as Windows toast buttons and activate the app with the original openHAB action.
7. Notification click action must execute the openHAB action instead of only opening the main window.
8. `sendLogNotification` entries must be stored in the notification inbox but must not show a Windows toast.
9. Existing basic notifications must keep working.
10. Logs must avoid credentials, tokens, response bodies, and full sensitive URLs.

## Action Support

The client must support the action syntax documented by openHAB Cloud:

- `command:ItemName:CommandValue`: send `CommandValue` to `ItemName` through the same authenticated openHAB endpoint selection used by runtime/sitemap command handling.
- `ui:/basicui/app?w=0000&sitemap=main`: prefer native sitemap navigation when the URL can be mapped to a sitemap/page context; otherwise open the resolved openHAB path.
- `ui:/some/absolute/path`: open the resolved openHAB path in the app Main UI surface when available, with browser fallback.
- `ui:navigate:/page/my_floorplan_page`: open Main UI navigation for the page id when available, with browser fallback.
- `ui:popup:oh-clock-card`: open Main UI popup action when available, with browser fallback.
- `http://...` and `https://...`: open the URL.

The client should parse `rule:*` and `app:*` actions, log that they are unsupported, and avoid crashing or showing a broken UI. They are out of scope for this implementation.

## Architecture

Add a normalization step in `OpenHab.Windows.Notifications` that converts raw cloud notification JSON into a stable client model before the tray app stores or renders anything.

The normalizer must prefer nested `payload` values and use flat legacy fields as fallback. It should also support multiple likely wire names for advanced fields where openHAB Cloud or older servers differ:

- `reference-id`, `referenceId`
- `media-attachment-url`, `mediaAttachmentUrl`
- `on-click-action`, `onClickAction`
- `actions`, `actionButton1`, `actionButton2`, `actionButton3`

`App.xaml.cs` should consume the normalized model rather than deciding field precedence inline.

## Data Model

Create a Windows notification domain model in `OpenHab.Windows.Notifications`:

```csharp
public sealed record NormalizedCloudNotification(
    string Id,
    string Message,
    DateTimeOffset Created,
    string? Title,
    string? Icon,
    string? Tag,
    string? ReferenceId,
    string? OnClickAction,
    string? MediaAttachmentUrl,
    IReadOnlyList<NotificationActionButton> ActionButtons,
    CloudNotificationKind Kind,
    IReadOnlyList<NotificationHideTarget> HideTargets);
```

`CloudNotificationKind` values:

- `Push`: store and show toast.
- `LogOnly`: store but do not show toast.
- `Hide`: apply hide/remove targets and do not store as a visible new notification.

`NotificationHideTarget` supports reference id and tag matching.

## Polling And Reconciliation

`NotificationPoller` should keep fetching the existing cloud notification endpoint. After deserialization it should normalize every notification before deciding what to do:

- `Push`: add/update the store and raise `NotificationReceived`.
- `LogOnly`: add/update the store and do not raise `NotificationReceived`.
- `Hide`: hide matching stored notifications and do not raise `NotificationReceived`.

Seen-id behavior must not prevent reference-id updates. When a pushed notification has a reference id, `NotificationStore` should upsert by reference id rather than only by cloud `_id`. This ensures a later notification with the same reference id replaces the previous client-side entry.

If a notification disappears from the cloud list after being previously visible locally, the app may keep it in the local history unless the cloud sends an explicit hide payload. The explicit hide behavior is required; implicit deletion reconciliation is optional and should not be used as a substitute for hide payload handling.

## Store Behavior

Extend `NotificationStore` with focused APIs:

- `AddOrUpdateByReferenceId(...)`: replaces the previous visible notification with the same reference id while preserving a useful read/hidden state policy.
- `HideByReferenceId(string referenceId)`: hides matching notifications.
- `HideByTag(string tag)`: hides matching notifications.

When replacing by reference id:

- The stored row id should become the new cloud notification id.
- The old row with that reference id should not remain as a separate visible entry.
- If the old row was read, preserve read state.
- If the old row was hidden, keep it hidden unless the new payload is a normal push and product behavior explicitly wants a re-alert. The first implementation should preserve hidden state to respect user dismissal.

## Media Attachments

Add a media resolver in the Windows tray layer because it needs current settings, auth, endpoint selection, HTTP access, and local app cache paths.

Supported media attachment values:

- Absolute `http://` or `https://`: pass directly to the toast only if Windows can load it and it does not require app credentials. Prefer downloading to local cache when auth or reliability is needed.
- Absolute openHAB-relative path beginning with `/`: resolve against the active local/cloud endpoint, fetch with the correct auth, cache under `%localappdata%\OpenHab.WinApp\NotificationMedia`, and pass the local file URI to the toast.
- `item:ImageItemName`: fetch the item image through the active openHAB endpoint, cache it under `%localappdata%\OpenHab.WinApp\NotificationMedia`, and pass the local file URI to the toast.

Image fetch constraints:

- Max image bytes: 3 MB.
- Timeout: 5 seconds.
- Accepted media types: PNG, JPEG, GIF, BMP, WebP if Windows accepts it, and SVG only if toast APIs accept it in the target environment.
- Failed media resolution must not block the toast. Show the text/buttons without the image and write a redacted diagnostic log entry.

Use Windows hero image placement, not inline image placement, for media attachments.

## Toast Rendering

Extend `ToastService.Show` to accept a richer request object:

```csharp
public sealed record ToastNotificationRequest(
    string Title,
    string Body,
    IReadOnlyList<NotificationActionButton> Actions,
    string? LaunchAction,
    bool Important,
    string? Header,
    string? Tag,
    string? ReferenceId,
    Uri? AppLogoOverrideUri,
    Uri? HeroImageUri);
```

Toast construction rules:

- Add title as the first text line.
- Add body as the second text line.
- Add app logo override from the openHAB icon resolver when available.
- Add hero image from media resolver when available.
- Add up to three openHAB action buttons from the normalized action buttons.
- Include launch arguments for the notification click action when available.
- Include a stable toast tag/group when reference id or tag is available so Windows can replace/remove displayed notifications where supported.

Packaged and unpackaged activation paths must both call the same activation handler with the same raw argument string.

## Action Execution

Move action execution behind a small service in the tray layer:

```csharp
public sealed class NotificationActionExecutor
{
    public Task ExecuteAsync(string rawAction, CancellationToken cancellationToken);
}
```

Responsibilities:

- Parse actions with `NotificationActionParser`.
- Send `command:*` through `OpenHabHttpClient` using selected transport and matching auth.
- Open Main UI or browser paths for supported `ui:*` actions.
- Open external `http` and `https` URLs.
- Log unsupported `rule` and `app` actions.

Avoid creating unauthenticated ad hoc `HttpClient` command calls in `App.xaml.cs`.

## UI And Inbox

Notification history should display the normalized title, message, tag, icon, reference id, media presence, and actions where practical. The main requirement for this feature is not a new inbox layout; it is correctness of stored data and toast behavior.

If an advanced push includes a hero image, the inbox may show only text/icon initially. A later UI enhancement can add media thumbnails.

## Error Handling

- Invalid or unknown action button strings are skipped individually.
- More than three openHAB buttons are ignored after the first three.
- More than five Windows toast actions are never emitted.
- Invalid media attachment URLs are ignored with a redacted warning.
- Missing credentials should cause command/media fetch failures to be logged and skipped, not crash.
- `hideNotification` payloads without reference id or tag should be ignored with a warning.

## Testing

Add unit coverage for:

- Nested payload normalization.
- Flat legacy field fallback.
- Tag/severity precedence.
- `reference-id` replacement.
- `hideNotification` by reference id.
- `hideNotification` by tag.
- Log-only notification storage without toast raise.
- Action button parsing from individual fields and payload actions.
- Command action execution uses the authenticated endpoint and method.
- Media resolver maps `/path` and `item:ImageItem` to authenticated cache fetches.
- Toast request maps hero image, launch action, buttons, tag, and reference id into generated XML.

Run direct test projects during implementation. Run full solution tests and Release tray build before claiming completion when practical.

## Out Of Scope

- Native FCM/push registration for Windows.
- Implementing `rule:*` and `app:*` action execution.
- Rich media thumbnails in the inbox UI.
- Reply text boxes or other interactive input controls inside Windows toasts.
- Server-side changes to openHAB Cloud.

## References

- openHAB Cloud Connector advanced notification documentation: https://www.openhab.org/addons/integrations/openhabcloud/#title-tag-reference-id-media-attachments-actions
- Microsoft Windows app notification content, hero image, and buttons: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-content
- openHAB Cloud source contract inspected from `openhab/openhab-cloud`:
  - `src/types/models.ts`
  - `src/controllers/api.controller.ts`
  - `src/services/notification.service.ts`
