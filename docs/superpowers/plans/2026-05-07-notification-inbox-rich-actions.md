# Notification Inbox & Rich Actions Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers-extended-cc:subagent-driven-development (if subagents available) or superpowers-extended-cc:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent notification history with an in-app inbox UI, rich toast action buttons (parsing openHAB Cloud action syntax), and prevent re-displaying previously-shown/dismissed notifications across app restarts.

**Architecture:** Extend the existing `OpenHab.Windows.Notifications` project with richer `CloudNotification` fields and an `NotificationAction` parser. Add a `NotificationStore` in `OpenHab.App` following the `AppSettingsController` JSON-file persistence pattern. Add a notifications tab to `MainWindow` alongside the existing sitemap+settings layout. Wire `ToastService` to render action buttons from notification data.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), CommunityToolkit.WinUI.Notifications, System.Text.Json

**Source docs:**
- Status: `docs/superpowers/status/2026-05-05-openhab-windows-auth-notifications-status.md` (lines 96-99 list these features as "Still Out Of Scope")
- Debugging: `docs/superpowers/status/2026-05-06-openhab-windows-notification-debugging.md`
- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- openHAB Cloud actions: https://www.openhab.org/addons/integrations/openhabcloud/ (action syntax: `command:`, `ui:`, `http:`, `rule:`, `app:`)

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/OpenHab.Windows.Notifications/NotificationAction.cs` | Model + parser for openHAB Cloud action syntax |
| `src/OpenHab.App/Notifications/NotificationStore.cs` | JSON file persistence for notification history and read/dismissed status |
| `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs` | Unit tests for store serialization, dedup, trimming |
| `tests/OpenHab.App.Tests/Notifications/NotificationActionTests.cs` | Unit tests for action parsing |

---

## Data Model: Extended CloudNotification

The current model captures only `Id`, `Message`, `Created`, `Icon`, `Severity`. The openHAB Cloud notification API can include additional fields set by rules via `sendNotification`/`sendBroadcastNotification`:

```csharp
// Extended CloudNotification record (src/OpenHab.Windows.Notifications/CloudNotification.cs)
public sealed record CloudNotification(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("severity")] string? Severity,
    // New fields (all optional — not every notification has them):
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("referenceId")] string? ReferenceId,
    [property: JsonPropertyName("onClickAction")] string? OnClickAction,
    [property: JsonPropertyName("mediaAttachmentUrl")] string? MediaAttachmentUrl,
    [property: JsonPropertyName("actionButton1")] string? ActionButton1,
    [property: JsonPropertyName("actionButton2")] string? ActionButton2,
    [property: JsonPropertyName("actionButton3")] string? ActionButton3
);
```

The `Tag` field (shown in openHAB Cloud docs as separate from severity) is already represented by `Severity` — the cloud connector renames this parameter but they map to the same MongoDB field.

---

## Data Model: NotificationAction

Action button strings from the cloud use the format `Title=actionType:payload`. The click action is just `actionType:payload` without the title prefix.

```csharp
// NotificationAction record
public sealed record NotificationAction(string Type, string Payload);

// NotificationActionButton record
public sealed record NotificationActionButton(string Title, string Type, string Payload);

// Static parser
public static class NotificationActionParser
{
    public static NotificationAction? TryParse(string? rawAction);
    public static NotificationActionButton? TryParseButton(string? rawButton);
    // rawButton format: "Title=command:ItemName:ON"
    // Returns null if no `=` separator present
    // rawAction format: "command:ItemName:ON"
}

// Enforce exact casing from JSON
// Supported action types:
// - "command" → command:$itemName:$commandString
// - "ui"     → ui:$path
// - "http" or "https" → treated as URL to open
// - "rule"   → rule:$ruleId:$prop1Key=$prop1Value,...
// - "app"    → app:android=$appId,ios=$appId:$path
```

---

## Data Model: StoredNotification (persistence)

```csharp
// New file: src/OpenHab.App/Notifications/NotificationStore.cs
public sealed record StoredNotification(
    string Id,
    string Message,
    string? Title,
    string? Icon,
    string? Severity,
    DateTimeOffset Created,
    DateTimeOffset ReceivedAt,     // local timestamp when first seen
    bool IsRead,
    bool IsDismissed,
    string? ReferenceId,
    string? OnClickAction,
    string? MediaAttachmentUrl,
    string? ActionButton1,
    string? ActionButton2,
    string? ActionButton3
);

public sealed class NotificationStore
{
    // Persistence: %LocalAppData%\OpenHab.WinApp\notifications.json
    // Pattern: identical to AppSettingsController — lock, JsonSerializer, fire-and-forget save
    // Max stored: 500 entries (oldest dismissed entries trimmed first)
    
    public IReadOnlyList<StoredNotification> GetAll();
    public IReadOnlySet<string> GetSeenUndismissedIds(); // returns IDs of all non-dismissed notifications
    public int UnreadCount { get; }
    public bool IsSeen(string notificationId);
    public bool IsDismissed(string notificationId);      // skip re-toasting
    public void AddOrUpdate(
        string id, string message, DateTimeOffset created,
        string? title = null, string? icon = null, string? severity = null,
        string? referenceId = null, string? onClickAction = null,
        string? mediaAttachmentUrl = null,
        string? actionButton1 = null, string? actionButton2 = null, string? actionButton3 = null);
    public void MarkRead(string id);
    public void MarkUnread(string id);
    public void Dismiss(string id);
    public void DismissAll();
    public event EventHandler? Changed;                  // fire when state changes (for UI refresh)
}
```

---

## Persistence File Format

`%LOCALAPPDATA%\OpenHab.WinApp\notifications.json`:
```json
{
  "notifications": [
    {
      "id": "664a1b2c...",
      "message": "Front door opened",
      "title": "Door Alert",
      "icon": "door",
      "severity": "warning",
      "created": "2026-05-07T10:30:00Z",
      "receivedAt": "2026-05-07T10:30:05Z",
      "isRead": false,
      "isDismissed": false,
      "referenceId": null,
      "onClickAction": "ui:/basicui/app?w=0000&sitemap=home",
      "mediaAttachmentUrl": null,
      "actionButton1": "Turn on light=command:KitchenLight:ON",
      "actionButton2": null,
      "actionButton3": null
    }
  ]
}
```

---

## UI: MainWindow Tabbed Layout

Change `MainWindow.xaml` from the current 2-column grid (sitemap | settings) to a tabbed interface. This is the least-disruptive change:

**Option A (Recommended): Pivot/TabView in Column 1**

Keep the current layout but wrap the right column in a `Pivot` control so tabs switch between Sitemap, Notifications, and Settings:

```xml
<!-- Column 1 (right side) becomes tabbed -->
<Pivot Grid.Row="3" Grid.Column="1">
    <PivotItem Header="Notifications">
        <!-- Notification ListView here -->
    </PivotItem>
    <PivotItem Header="Settings">
        <!-- Existing settings panel -->
    </PivotItem>
</Pivot>
```

Column 0 remains the sitemap viewer. The `Notifications` tab shows a `ListView` with:
- Unread indicator (bold title + blue dot)
- Timestamp (relative: "2 hours ago")
- Severity badge (color-coded)
- Message preview (first line, truncated)
- Action buttons per item: "Mark read", "Dismiss"
- Header bar with "Dismiss all" button and unread count

---

## Toast Notification Actions

`ToastService` gets a new overload that renders action buttons:

```csharp
public static void Show(string title, string body, IReadOnlyList<NotificationActionButton>? actions)
{
    if (!isAvailable) return; // Continue without actions in degraded mode
    
    var builder = new ToastContentBuilder()
        .AddText(title)
        .AddText(body);
    
    if (actions is not null)
    {
        foreach (var action in actions)
        {
            // Use the title as button text, encoded action as argument
            builder.AddButton(action.Title, ToastActivationType.Foreground, 
                $"{action.Type}:{action.Payload}");
        }
    }
    
    var toast = new ToastNotification(builder.GetXml());
    ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
}
```

Toast activation handler in `App.xaml.cs` parses the argument and dispatches to the appropriate action handler.

**Toast action handling** (in `App.xaml.cs` or a new `NotificationActionHandler`):

| Action Type | Behavior |
|-------------|----------|
| `command:ItemName:ON` | Send HTTP POST to openHAB REST API `/rest/items/ItemName` |
| `ui:path` | Navigate MainWindow to the sitemap/UI path if applicable |
| `http:` / `https:` | Open URL in default browser (`Process.Start`) |
| `rule:...` | Log warning — rule execution from client not implemented (future) |
| `app:...` | Log warning — app launching from notification not implemented (future) |

---

## NotificationPoller Integration

The poller currently uses an in-memory `HashSet<string> seenIds` (max 200, cleared when full). This must be replaced with `NotificationStore`:

1. **Constructor**: Accept `NotificationStore` parameter
2. **On start**: Hydrate `seenIds` from `store.GetAll().Select(n => n.Id)` — this prevents re-toasting on restart
3. **On new notification**: 
   - Create `StoredNotification` with `IsRead=false`, `IsDismissed=false`
   - Call `store.AddOrUpdate(notification)`
   - Only fire `NotificationReceived` if notification is NOT already dismissed
4. **Dedup logic unchanged**: Still uses `HashSet` for runtime dedup, but backed by store on startup

---

## App.xaml.cs Wiring

Changes to `App.xaml.cs`:

```csharp
// New field
private NotificationStore? notificationStore;

// In OnLaunched, after settingsController creation:
notificationStore = new NotificationStore();

// In StartNotificationPolling, wire store delegates (NOT the concrete type):
notificationPoller = new NotificationPoller(
    httpClient!,
    settings.CloudEndpoint,
    basicUserName: cloudCredentials?.UserName,
    basicPassword: cloudCredentials?.Password,
    dispatcher: uiDispatcherQueue,
    preSeenIds: notificationStore.GetSeenUndismissedIds(),           // hydrate seenIds
    isDismissedFunc: id => notificationStore.IsDismissed(id),       // skip dismissed
    onNewNotification: n => notificationStore.AddOrUpdate(
        n.Id, n.Message, n.Created, n.Title, n.Icon, n.Severity,
        n.ReferenceId, n.OnClickAction, n.MediaAttachmentUrl,
        n.ActionButton1, n.ActionButton2, n.ActionButton3)   // persist via field mapping
);

// Toast activation: handle action arguments
ToastService.NotificationActivated += (_, args) =>
{
    if (!string.IsNullOrEmpty(args.Argument))
    {
        _ = HandleNotificationActionAsync(args.Argument);
        return;
    }
    // Existing: open main window (simple tap with no action buttons)
    _ = uiDispatcherQueue?.TryEnqueue(() =>
    {
        if (shellController is null) return;
        shellController.HandleNotificationActivated();
        _ = ApplyShellStateAsync();
    });
};

// Expose store to MainWindow:
mainWindow = new MainWindow(
    settingsController,
    runtimeController,
    notificationStore,  // NEW
    // ...existing params
);
```

---

## Tasks

### Task 1: Extend CloudNotification model

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/CloudNotification.cs`

- [ ] **Step 1: Add new optional fields**

Add `Title`, `ReferenceId`, `OnClickAction`, `MediaAttachmentUrl`, `ActionButton1`, `ActionButton2`, `ActionButton3` properties with `[JsonPropertyName]` attributes. All are `string?` (optional).

- [ ] **Step 2: Build and verify no regressions**

Run: `dotnet build src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj --configuration Debug`
Expected: 0 errors, 0 warnings

### Task 2: Create NotificationAction parser

**Files:**
- Create: `src/OpenHab.Windows.Notifications/NotificationAction.cs`
- Create: `tests/OpenHab.App.Tests/Notifications/NotificationActionTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Parse_CommandAction_ReturnsCorrectTypeAndPayload() { ... }
[Fact] public void Parse_UiAction_ReturnsCorrectTypeAndPayload() { ... }
[Fact] public void Parse_HttpUrl_ReturnsHttpType() { ... }
[Fact] public void Parse_NullInput_ReturnsNull() { ... }
[Fact] public void Parse_ButtonWithTitle_ExtractsTitleAndAction() { ... }
[Fact] public void Parse_ButtonWithoutEquals_ReturnsNull() { ... }
[Fact] public void Parse_EmptyString_ReturnsNull() { ... }
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "NotificationAction" --configuration Debug`
Expected: FAIL (class not found)

- [ ] **Step 3: Implement `NotificationActionParser`**

Static class with `TryParse(string?)` → `NotificationAction?` and `TryParseButton(string?)` → `NotificationActionButton?`. Define `NotificationAction` and `NotificationActionButton` records in the same file (small, focused).

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "NotificationAction" --configuration Debug`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Notifications/NotificationAction.cs tests/OpenHab.App.Tests/Notifications/
git commit -m "feat: add NotificationAction parser for openHAB Cloud action syntax"
```

### Task 3: Create NotificationStore with JSON persistence

**Files:**
- Create: `src/OpenHab.App/Notifications/NotificationStore.cs`
- Create: `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void AddOrUpdate_NewNotification_StoresCorrectly() { ... }
[Fact] public void AddOrUpdate_DuplicateId_UpdatesExisting() { ... }
[Fact] public void IsSeen_KnownId_ReturnsTrue() { ... }
[Fact] public void IsSeen_UnknownId_ReturnsFalse() { ... }
[Fact] public void IsDismissed_DismissedNotification_ReturnsTrue() { ... }
[Fact] public void MarkRead_SetsIsReadTrue() { ... }
[Fact] public void Dismiss_SetsIsDismissedTrue() { ... }
[Fact] public void DismissAll_DismissesAllNotifications() { ... }
[Fact] public void UnreadCount_ReflectsUnreadNotifications() { ... }
[Fact] public void Store_SurvivesRoundTrip_LoadsCorrectly() { ... }
[Fact] public void Trim_RemovesOldestDismissed_WhenExceedsMax() { ... }
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "NotificationStore" --configuration Debug`
Expected: FAIL

- [ ] **Step 3: Implement `NotificationStore`**

Follow the `AppSettingsController` persistence pattern exactly:
- Storage path: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenHab.WinApp", "notifications.json")`
- Thread safety: `lock (syncRoot)`
- Serialization: `System.Text.Json` with `WriteIndented = true`
- Error handling: best-effort, silent catch on I/O errors
- Max entries: 500 (trim oldest dismissed first)
- Fire `Changed` event on any mutation
- `TryLoad()` in constructor; `SaveAsync()` fire-and-forget

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter "NotificationStore" --configuration Debug`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.App/Notifications/ tests/OpenHab.App.Tests/Notifications/
git commit -m "feat: add NotificationStore with JSON persistence for notification history"
```

### Task 4: Add persistence-aware parameters to NotificationPoller

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/NotificationPoller.cs`

**Design decision:** `NotificationPoller` lives in `OpenHab.Windows.Notifications` which only references `OpenHab.Core`. `NotificationStore` lives in `OpenHab.App`. Rather than adding a reverse project reference, the poller accepts simple data/delegates from the caller (`App.xaml.cs`) — it has zero knowledge of `NotificationStore` as a type.

- [ ] **Step 1: Add constructor parameters for persistence integration**

Add two optional parameters to `NotificationPoller` constructor:

```csharp
// Add to existing constructor (after dispatcher parameter):
IReadOnlySet<string>? preSeenIds = null,        // hydrate seenIds on start
Func<string, bool>? isDismissedFunc = null,     // check if notification was dismissed
Action<CloudNotification>? onNewNotification = null  // persist new arrival
```

On construction:
```csharp
if (preSeenIds is not null)
{
    foreach (var id in preSeenIds)
        seenIds.Add(id);
}
```

- [ ] **Step 2: Skip dismissed notifications in poll loop**

In `PollOnceAsync`, before firing `NotificationReceived`:
```csharp
if (isDismissedFunc is not null && isDismissedFunc(notification.Id))
{
    DiagnosticLogger.Info($"Skipping dismissed notification: Id={notification.Id}");
    seenIds.Add(notification.Id);  // still track as seen
    continue;
}
```

- [ ] **Step 3: Persist new notifications via callback**

After `RaiseNotification(notification)`:
```csharp
onNewNotification?.Invoke(notification);
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj --configuration Debug`
Expected: 0 errors, 0 warnings (zero project reference changes needed)

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Notifications/NotificationPoller.cs
git commit -m "feat: add persistence-aware parameters to NotificationPoller (preSeenIds, isDismissed, onNewNotification)"
```

### Task 5: Add action-button support to ToastService

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/ToastService.cs`

- [ ] **Step 1: Add overloaded `Show` method**

```csharp
public static void Show(string title, string body, IReadOnlyList<NotificationActionButton>? actions)
```
Renders toast buttons from the action list using `ToastContentBuilder.AddButton()`. If `isAvailable` is false, silently skip (degraded mode).

- [ ] **Step 2: Change NotificationActivated event delegate type**

**CRITICAL:** The current `public static event EventHandler? NotificationActivated;` uses bare `EventHandler` which carries no argument data. This must change to pass the toast activation argument string through to subscribers.

Change the event declaration to:
```csharp
public static event EventHandler<ToastNotificationActivatedEventArgsCompat>? NotificationActivated;
```

Update `HandleToastActivated` to pass the full `ToastNotificationActivatedEventArgsCompat` through (which carries `.Argument` — the action string like `"command:KitchenLight:ON"`):
```csharp
private static void HandleToastActivated(ToastNotificationActivatedEventArgsCompat args)
{
    DiagnosticLogger.Info("User activated a toast notification");
    NotificationActivated?.Invoke(null, args);
}
```

Note: All existing subscribers that used `EventHandler` must be updated to `EventHandler<ToastNotificationActivatedEventArgsCompat>`.

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj --configuration Debug`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.Windows.Notifications/ToastService.cs
git commit -m "feat: add action button support to ToastService"
```

### Task 6: Wire NotificationStore and actions into App.xaml.cs

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Create NotificationStore**

In `OnLaunched`, after `settingsController` creation: `notificationStore = new NotificationStore();`

- [ ] **Step 2: Pass store to NotificationPoller and wire toast action buttons**

In `StartNotificationPolling`, pass `notificationStore` to the poller constructor. Update the `NotificationReceived` handler to **parse the notification's `actionButton1`/`2`/`3` fields into `NotificationActionButton` objects** and pass them to `ToastService.Show()`:

```csharp
notificationPoller.NotificationReceived += (_, notification) =>
{
    var title = notification.Title ?? (notification.Severity is not null
        ? $"[{notification.Severity}] openHAB"
        : "openHAB");
    var body = notification.Message.Length > 200
        ? notification.Message[..197] + "..."
        : notification.Message;
    
    // Parse action buttons from the notification data
    var actionButtons = new List<NotificationActionButton>();
    TryAddButton(notification.ActionButton1, actionButtons);
    TryAddButton(notification.ActionButton2, actionButtons);
    TryAddButton(notification.ActionButton3, actionButtons);
    
    ToastService.Show(title, body, actionButtons.Count > 0 ? actionButtons : null);
};
```

Where `TryAddButton` calls `NotificationActionParser.TryParseButton(raw)` and adds non-null results.

- [ ] **Step 3: Handle toast action activation**

Add `HandleNotificationActionAsync(string actionArg)` method that parses the action and dispatches:
- `command:ItemName:ON` → send HTTP POST to openHAB REST API `/rest/items/{ItemName}` with the command value
- `ui:` → if sitemap path, navigate MainWindow; otherwise open browser
- `http:`/`https:` → `Process.Start(url)` in default browser
- `rule:`/`app:` → log warning (not implemented; iOS-only features)

- [ ] **Step 4: Pass store to MainWindow**

Update `MainWindow` constructor to accept `NotificationStore`.

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug`
Expected: 0 errors, 0 warnings

- [ ] **Step 6: Commit**

```bash
git add src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "feat: wire NotificationStore and toast actions into app startup"
```

### Task 7: Add notification inbox tab to MainWindow

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Restructure MainWindow with Pivot tabs**

Replace the right-column StackPanel with a `Pivot` containing two `PivotItem`s: "Notifications" and "Settings". Keep left column (sitemap) unchanged.

```xml
<Pivot Grid.Row="3" Grid.Column="1" x:Name="SidePanelPivot">
    <PivotItem Header="Notifications">
        <!-- Notification list -->
    </PivotItem>
    <PivotItem Header="Settings">
        <!-- Existing settings content -->
    </PivotItem>
</Pivot>
```

- [ ] **Step 2: Build notification list UI**

Inside the Notifications `PivotItem`:
- Header bar: "Notifications" title + unread count badge + "Dismiss all" button
- `ListView` (or programmatic `StackPanel`) bound to `notificationStore.GetAll()`
- Each item template shows:
  - Left: severity color dot + icon (from notification `icon` field)
  - Center: title (or first line of message) + relative timestamp + message preview
  - Right: "Mark read" / "Dismiss" buttons
  - Bold text for unread items
- Empty state: "No notifications" text when list is empty

- [ ] **Step 3: Implement code-behind**

In `MainWindow.xaml.cs`:
- Accept `NotificationStore` in constructor
- Subscribe to `notificationStore.Changed` to refresh the list
- "Mark read" → `notificationStore.MarkRead(id)`
- "Dismiss" → `notificationStore.Dismiss(id)`
- "Dismiss all" → `notificationStore.DismissAll()`
- Handle `NotificationReceived` to auto-refresh and update unread count

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj --configuration Debug`
Expected: 0 errors, 0 warnings

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs
git commit -m "feat: add notification inbox tab to MainWindow"
```

### Task 8: Full solution build and test

**Files:**
- All modified files across the solution

- [ ] **Step 1: Full solution build**

Run: `dotnet build OpenHab.Windows.sln --configuration Debug`
Expected: 0 errors, 0 warnings

- [ ] **Step 2: Run all tests**

Run: `dotnet test OpenHab.Windows.sln --configuration Debug`
Expected: All new notification tests pass. Pre-existing WinRT-related failures (51 in OpenHab.App.Tests) remain unchanged.

- [ ] **Step 3: Run LSP diagnostics**

Check diagnostics on all modified files:
- `src/OpenHab.Windows.Notifications/CloudNotification.cs`
- `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- `src/OpenHab.Windows.Notifications/ToastService.cs`
- `src/OpenHab.App/Notifications/NotificationStore.cs`
- `src/OpenHab.Windows.Tray/App.xaml.cs`
- `src/OpenHab.Windows.Tray/MainWindow.xaml`
- `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
Expected: 0 errors, 0 warnings on modified files

- [ ] **Step 4: Commit final integration**

```bash
git add -A
git commit -m "feat: complete notification inbox, persistence, and rich toast actions"
```

---

## Verification Checklist

- [ ] `dotnet build OpenHab.Windows.sln --configuration Debug` → 0 errors, 0 warnings
- [ ] `dotnet test OpenHab.Windows.sln --configuration Debug` → all new tests pass
- [ ] NotificationStore survives restart (notifications.json persists)
- [ ] Dismissed/read notifications not re-shown as toasts on relaunch
- [ ] Inbox UI shows notification list with read/dismiss actions
- [ ] Toast action buttons render correctly (when COM available)
- [ ] Degraded mode: app still works without COM toast availability
- [ ] No credentials or tokens logged (DiagnosticLogger redaction maintained)
- [ ] LSP diagnostics clean on all modified files
