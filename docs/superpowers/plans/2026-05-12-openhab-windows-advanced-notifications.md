# OpenHAB Windows Advanced Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement advanced openHAB Cloud notification parity for Windows toasts and local notification history.

**Architecture:** Normalize raw cloud notifications into a stable model before storage/rendering, then route store updates, toast rendering, media resolution, and action execution through focused services. Keep cloud payload parsing in `OpenHab.Windows.Notifications`, history semantics in `OpenHab.App.Notifications`, and UI/auth/runtime concerns in `OpenHab.Windows.Tray`.

**Tech Stack:** .NET 10, C#, System.Text.Json, WinUI 3 / Windows App SDK, CommunityToolkit WinUI Notifications, xUnit, existing `NotificationPoller`, `NotificationStore`, `ToastService`, and tray app startup flow.

---

## File Structure

- Modify `src/OpenHab.Windows.Notifications/CloudNotification.cs`: add nested `payload` support and raw extension data.
- Create `src/OpenHab.Windows.Notifications/CloudNotificationKind.cs`: normalized notification kind enum.
- Create `src/OpenHab.Windows.Notifications/NotificationHideTarget.cs`: hide target record.
- Create `src/OpenHab.Windows.Notifications/NormalizedCloudNotification.cs`: stable normalized model.
- Create `src/OpenHab.Windows.Notifications/CloudNotificationNormalizer.cs`: field precedence, payload parsing, action extraction, hide/log classification.
- Modify `src/OpenHab.Windows.Notifications/NotificationAction.cs`: make parser trim values, reject invalid buttons, and parse payload action arrays/objects.
- Create `src/OpenHab.Windows.Notifications/ToastNotificationRequest.cs`: rich toast request model.
- Create `src/OpenHab.Windows.Notifications/ToastNotificationXmlBuilder.cs`: testable toast XML generation.
- Modify `src/OpenHab.Windows.Notifications/ToastService.cs`: use rich request model, hero image, launch action, tag/group replacement, shared activation event.
- Modify `src/OpenHab.Windows.Notifications/NotificationPoller.cs`: raise normalized notification events and call callbacks for push/log/hide decisions.
- Modify `src/OpenHab.App/Notifications/NotificationStore.cs`: add reference replacement and hide-by-reference/tag.
- Create `src/OpenHab.Windows.Tray/Notifications/NotificationMediaResolver.cs`: authenticated media fetch and cache.
- Create `src/OpenHab.Windows.Tray/Notifications/NotificationActionExecutor.cs`: command/ui/http action execution.
- Modify `src/OpenHab.Windows.Tray/App.xaml.cs`: consume normalized notifications, resolve media, show rich toasts, handle click/button activation through executor, remove ad hoc command sending.
- Modify `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`: adapt simple tray balloon call to rich toast API or keep compatibility wrapper.
- Create or modify tests under `tests/OpenHab.App.Tests/Notifications`: normalizer, store, action parser, toast XML, media resolver, action executor, poller behavior.

---

## Task 1: Cloud Notification Normalization

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/CloudNotification.cs`
- Create: `src/OpenHab.Windows.Notifications/CloudNotificationKind.cs`
- Create: `src/OpenHab.Windows.Notifications/NotificationHideTarget.cs`
- Create: `src/OpenHab.Windows.Notifications/NormalizedCloudNotification.cs`
- Create: `src/OpenHab.Windows.Notifications/CloudNotificationNormalizer.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/CloudNotificationNormalizerTests.cs`

- [ ] **Step 1: Write failing normalizer tests**

Create `tests/OpenHab.App.Tests/Notifications/CloudNotificationNormalizerTests.cs`:

```csharp
using System.Text.Json;
using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class CloudNotificationNormalizerTests
{
    private static CloudNotification Deserialize(string json)
    {
        return JsonSerializer.Deserialize<CloudNotification>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public void Normalize_PrefersNestedPayloadAdvancedFields()
    {
        var raw = Deserialize("""
        {
          "_id": "cloud-1",
          "message": "Fallback message",
          "created": "2026-05-12T10:00:00Z",
          "title": "Flat Title",
          "tag": "flat-tag",
          "payload": {
            "message": "Motion detected in the apartment!",
            "title": "Motion Detected",
            "icon": "motion",
            "tag": "Motion Tag",
            "reference-id": "motion-id-1234",
            "media-attachment-url": "item:VaccumingRobot_01_CleaningMap",
            "on-click-action": "ui:navigate:/page/security",
            "actionButton1": "Turn on the light=command:BulbDesk_01_Switch:ON",
            "actionButton2": "Dismiss=command:BulbKitchen_01_Switch:ON"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal("cloud-1", normalized.Id);
        Assert.Equal("Motion detected in the apartment!", normalized.Message);
        Assert.Equal("Motion Detected", normalized.Title);
        Assert.Equal("motion", normalized.Icon);
        Assert.Equal("Motion Tag", normalized.Tag);
        Assert.Equal("motion-id-1234", normalized.ReferenceId);
        Assert.Equal("item:VaccumingRobot_01_CleaningMap", normalized.MediaAttachmentUrl);
        Assert.Equal("ui:navigate:/page/security", normalized.OnClickAction);
        Assert.Equal(CloudNotificationKind.Push, normalized.Kind);
        Assert.Collection(
            normalized.ActionButtons,
            first =>
            {
                Assert.Equal("Turn on the light", first.Title);
                Assert.Equal("command", first.Type);
                Assert.Equal("BulbDesk_01_Switch:ON", first.Payload);
            },
            second =>
            {
                Assert.Equal("Dismiss", second.Title);
                Assert.Equal("command", second.Type);
                Assert.Equal("BulbKitchen_01_Switch:ON", second.Payload);
            });
    }

    [Fact]
    public void Normalize_FallsBackToFlatLegacyFields()
    {
        var raw = Deserialize("""
        {
          "_id": "legacy-1",
          "message": "Legacy body",
          "created": "2026-05-12T10:00:00Z",
          "title": "Legacy title",
          "icon": "motion",
          "severity": "Warning",
          "referenceId": "legacy-reference",
          "onClickAction": "https://openhab.org",
          "mediaAttachmentUrl": "https://example.test/camera.jpg",
          "actionButton1": "Open=https://example.test"
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal("Legacy body", normalized.Message);
        Assert.Equal("Legacy title", normalized.Title);
        Assert.Equal("motion", normalized.Icon);
        Assert.Equal("Warning", normalized.Tag);
        Assert.Equal("legacy-reference", normalized.ReferenceId);
        Assert.Equal("https://openhab.org", normalized.OnClickAction);
        Assert.Equal("https://example.test/camera.jpg", normalized.MediaAttachmentUrl);
        Assert.Single(normalized.ActionButtons);
    }

    [Fact]
    public void Normalize_ClassifiesLogOnlyPayload()
    {
        var raw = Deserialize("""
        {
          "_id": "log-1",
          "message": "Saved only",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "message": "Saved only",
            "type": "log"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(CloudNotificationKind.LogOnly, normalized.Kind);
        Assert.Empty(normalized.HideTargets);
    }

    [Fact]
    public void Normalize_ClassifiesHideByReferenceIdAndTag()
    {
        var raw = Deserialize("""
        {
          "_id": "hide-1",
          "message": "",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "type": "hideNotification",
            "reference-id": "motion-id-1234",
            "tag": "Motion Tag"
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Equal(CloudNotificationKind.Hide, normalized.Kind);
        Assert.Contains(normalized.HideTargets, t => t.Kind == NotificationHideTargetKind.ReferenceId && t.Value == "motion-id-1234");
        Assert.Contains(normalized.HideTargets, t => t.Kind == NotificationHideTargetKind.Tag && t.Value == "Motion Tag");
    }

    [Fact]
    public void Normalize_ParsesPayloadActionsArray()
    {
        var raw = Deserialize("""
        {
          "_id": "actions-1",
          "message": "Body",
          "created": "2026-05-12T10:00:00Z",
          "payload": {
            "actions": [
              "Light=command:Light:ON",
              { "title": "Main UI", "action": "ui:navigate:/page/overview" }
            ]
          }
        }
        """);

        var normalized = CloudNotificationNormalizer.Normalize(raw);

        Assert.Collection(
            normalized.ActionButtons,
            first => Assert.Equal("Light", first.Title),
            second => Assert.Equal("Main UI", second.Title));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter CloudNotificationNormalizerTests
```

Expected: build fails because `CloudNotificationNormalizer`, `NormalizedCloudNotification`, `CloudNotificationKind`, and hide target types do not exist.

- [ ] **Step 3: Add raw payload support**

Replace `src/OpenHab.Windows.Notifications/CloudNotification.cs` with:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenHab.Windows.Notifications;

public sealed record CloudNotification(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("created")] DateTimeOffset Created,
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("referenceId")] string? ReferenceId,
    [property: JsonPropertyName("onClickAction")] string? OnClickAction,
    [property: JsonPropertyName("mediaAttachmentUrl")] string? MediaAttachmentUrl,
    [property: JsonPropertyName("actionButton1")] string? ActionButton1,
    [property: JsonPropertyName("actionButton2")] string? ActionButton2,
    [property: JsonPropertyName("actionButton3")] string? ActionButton3,
    [property: JsonPropertyName("payload")] JsonElement? Payload);
```

- [ ] **Step 4: Add normalized model files**

Create `src/OpenHab.Windows.Notifications/CloudNotificationKind.cs`:

```csharp
namespace OpenHab.Windows.Notifications;

public enum CloudNotificationKind
{
    Push,
    LogOnly,
    Hide
}
```

Create `src/OpenHab.Windows.Notifications/NotificationHideTarget.cs`:

```csharp
namespace OpenHab.Windows.Notifications;

public enum NotificationHideTargetKind
{
    ReferenceId,
    Tag
}

public sealed record NotificationHideTarget(NotificationHideTargetKind Kind, string Value);
```

Create `src/OpenHab.Windows.Notifications/NormalizedCloudNotification.cs`:

```csharp
namespace OpenHab.Windows.Notifications;

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

- [ ] **Step 5: Implement normalizer**

Create `src/OpenHab.Windows.Notifications/CloudNotificationNormalizer.cs`:

```csharp
using System.Text.Json;

namespace OpenHab.Windows.Notifications;

public static class CloudNotificationNormalizer
{
    public static NormalizedCloudNotification Normalize(CloudNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = notification.Payload;
        var message = FirstNonEmpty(
            GetPayloadString(payload, "message"),
            notification.Message) ?? string.Empty;
        var title = FirstNonEmpty(
            GetPayloadString(payload, "title"),
            notification.Title);
        var icon = FirstNonEmpty(
            GetPayloadString(payload, "icon"),
            notification.Icon);
        var tag = FirstNonEmpty(
            GetPayloadString(payload, "tag"),
            GetPayloadString(payload, "severity"),
            notification.Tag,
            notification.Severity);
        var referenceId = FirstNonEmpty(
            GetPayloadString(payload, "reference-id"),
            GetPayloadString(payload, "referenceId"),
            notification.ReferenceId);
        var onClickAction = FirstNonEmpty(
            GetPayloadString(payload, "on-click-action"),
            GetPayloadString(payload, "onClickAction"),
            notification.OnClickAction);
        var mediaAttachmentUrl = FirstNonEmpty(
            GetPayloadString(payload, "media-attachment-url"),
            GetPayloadString(payload, "mediaAttachmentUrl"),
            notification.MediaAttachmentUrl);
        var kind = ResolveKind(payload);
        var hideTargets = BuildHideTargets(kind, referenceId, tag);
        var buttons = kind == CloudNotificationKind.Hide
            ? []
            : BuildActionButtons(notification, payload);

        return new NormalizedCloudNotification(
            notification.Id,
            message.Trim(),
            notification.Created,
            TrimToNull(title),
            TrimToNull(icon),
            TrimToNull(tag),
            TrimToNull(referenceId),
            TrimToNull(onClickAction),
            TrimToNull(mediaAttachmentUrl),
            buttons,
            kind,
            hideTargets);
    }

    private static CloudNotificationKind ResolveKind(JsonElement? payload)
    {
        var type = GetPayloadString(payload, "type");
        if (string.Equals(type, "hideNotification", StringComparison.OrdinalIgnoreCase))
        {
            return CloudNotificationKind.Hide;
        }

        if (string.Equals(type, "log", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "logOnly", StringComparison.OrdinalIgnoreCase))
        {
            return CloudNotificationKind.LogOnly;
        }

        return CloudNotificationKind.Push;
    }

    private static IReadOnlyList<NotificationHideTarget> BuildHideTargets(
        CloudNotificationKind kind,
        string? referenceId,
        string? tag)
    {
        if (kind != CloudNotificationKind.Hide)
        {
            return [];
        }

        var targets = new List<NotificationHideTarget>();
        if (!string.IsNullOrWhiteSpace(referenceId))
        {
            targets.Add(new NotificationHideTarget(NotificationHideTargetKind.ReferenceId, referenceId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            targets.Add(new NotificationHideTarget(NotificationHideTargetKind.Tag, tag.Trim()));
        }

        return targets;
    }

    private static IReadOnlyList<NotificationActionButton> BuildActionButtons(
        CloudNotification notification,
        JsonElement? payload)
    {
        var buttons = new List<NotificationActionButton>();
        AddButton(buttons, GetPayloadString(payload, "actionButton1") ?? notification.ActionButton1);
        AddButton(buttons, GetPayloadString(payload, "actionButton2") ?? notification.ActionButton2);
        AddButton(buttons, GetPayloadString(payload, "actionButton3") ?? notification.ActionButton3);
        AddPayloadActions(buttons, payload);
        return buttons.Take(3).ToList();
    }

    private static void AddPayloadActions(List<NotificationActionButton> buttons, JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } payloadElement
            || !payloadElement.TryGetProperty("actions", out var actions))
        {
            return;
        }

        if (actions.ValueKind == JsonValueKind.String)
        {
            AddButton(buttons, actions.GetString());
            return;
        }

        if (actions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var action in actions.EnumerateArray())
        {
            if (action.ValueKind == JsonValueKind.String)
            {
                AddButton(buttons, action.GetString());
            }
            else if (action.ValueKind == JsonValueKind.Object)
            {
                var title = GetString(action, "title") ?? GetString(action, "label");
                var rawAction = GetString(action, "action");
                if (!string.IsNullOrWhiteSpace(title)
                    && !string.IsNullOrWhiteSpace(rawAction)
                    && NotificationActionParser.TryParse(rawAction) is { } parsed)
                {
                    buttons.Add(new NotificationActionButton(title.Trim(), parsed.Type, parsed.Payload));
                }
            }
        }
    }

    private static void AddButton(List<NotificationActionButton> buttons, string? rawButton)
    {
        if (NotificationActionParser.TryParseButton(rawButton) is { } button)
        {
            buttons.Add(button);
        }
    }

    private static string? GetPayloadString(JsonElement? payload, string propertyName)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }

        return GetString(element, propertyName);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
```

- [ ] **Step 6: Run normalizer tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter CloudNotificationNormalizerTests
```

Expected: all `CloudNotificationNormalizerTests` pass.

- [ ] **Step 7: Commit**

```powershell
git add src\OpenHab.Windows.Notifications\CloudNotification.cs src\OpenHab.Windows.Notifications\CloudNotificationKind.cs src\OpenHab.Windows.Notifications\NotificationHideTarget.cs src\OpenHab.Windows.Notifications\NormalizedCloudNotification.cs src\OpenHab.Windows.Notifications\CloudNotificationNormalizer.cs tests\OpenHab.App.Tests\Notifications\CloudNotificationNormalizerTests.cs
git commit -m "Normalize advanced cloud notifications"
```

---

## Task 2: Notification Store Replacement And Hide Semantics

**Files:**
- Modify: `src/OpenHab.App/Notifications/NotificationStore.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`

- [ ] **Step 1: Add failing store tests**

Append to `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`:

```csharp
[Fact]
public void AddOrUpdate_ReplacesVisibleNotificationWithSameReferenceId()
{
    var store = new NotificationStore();
    var first = new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);
    var second = first.AddMinutes(1);

    store.AddOrUpdate(
        "cloud-1",
        "First body",
        first,
        title: "First",
        referenceId: "motion-id-1234");
    store.MarkRead("cloud-1");

    store.AddOrUpdate(
        "cloud-2",
        "Second body",
        second,
        title: "Second",
        referenceId: "motion-id-1234");

    var all = store.GetNotifications(NotificationVisibilityFilter.All, null);
    var notification = Assert.Single(all);
    Assert.Equal("cloud-2", notification.Id);
    Assert.Equal("Second body", notification.Message);
    Assert.Equal("Second", notification.Title);
    Assert.Equal("motion-id-1234", notification.ReferenceId);
    Assert.True(notification.IsRead);
}

[Fact]
public void AddOrUpdate_ByReferenceIdPreservesHiddenState()
{
    var store = new NotificationStore();
    var created = new DateTimeOffset(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);

    store.AddOrUpdate("old", "Old", created, referenceId: "ref-1");
    store.Hide("old");

    store.AddOrUpdate("new", "New", created.AddMinutes(1), referenceId: "ref-1");

    var notification = Assert.Single(store.GetNotifications(NotificationVisibilityFilter.All, null));
    Assert.Equal("new", notification.Id);
    Assert.True(notification.IsDismissed);
    Assert.True(notification.IsRead);
}

[Fact]
public void HideByReferenceId_HidesAllMatchingNotifications()
{
    var store = new NotificationStore();
    var created = DateTimeOffset.UtcNow;
    store.AddOrUpdate("a", "A", created, referenceId: "ref-1");
    store.AddOrUpdate("b", "B", created, referenceId: "ref-1");
    store.AddOrUpdate("c", "C", created, referenceId: "other");

    store.HideByReferenceId("ref-1");

    Assert.True(store.IsDismissed("a"));
    Assert.True(store.IsDismissed("b"));
    Assert.False(store.IsDismissed("c"));
}

[Fact]
public void HideByTag_HidesAllMatchingNotificationsCaseInsensitively()
{
    var store = new NotificationStore();
    var created = DateTimeOffset.UtcNow;
    store.AddOrUpdate("a", "A", created, severity: "Motion Tag");
    store.AddOrUpdate("b", "B", created, severity: "motion tag");
    store.AddOrUpdate("c", "C", created, severity: "Other");

    store.HideByTag("MOTION TAG");

    Assert.True(store.IsDismissed("a"));
    Assert.True(store.IsDismissed("b"));
    Assert.False(store.IsDismissed("c"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~NotificationStoreTests"
```

Expected: build fails because `HideByReferenceId` and `HideByTag` do not exist, and reference replacement is not implemented.

- [ ] **Step 3: Implement reference replacement and hide APIs**

In `src/OpenHab.App/Notifications/NotificationStore.cs`, inside `AddOrUpdate`, replace the first `if (notifications.TryGetValue(id, out var existing))` block with this logic:

```csharp
var existingKey = id;
StoredNotification? existing = null;
if (notifications.TryGetValue(id, out var byId))
{
    existing = byId;
}
else if (!string.IsNullOrWhiteSpace(referenceId))
{
    var byReference = notifications.Values
        .Where(n => string.Equals(n.ReferenceId, referenceId.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(n => n.Created)
        .FirstOrDefault();
    if (byReference is not null)
    {
        existingKey = byReference.Id;
        existing = byReference;
    }
}

if (existing is not null)
{
    var updated = existing with
    {
        Id = id,
        Message = message,
        Title = title ?? existing.Title,
        Icon = icon ?? existing.Icon,
        Severity = severity ?? existing.Severity,
        Created = created,
        ReferenceId = referenceId ?? existing.ReferenceId,
        OnClickAction = onClickAction ?? existing.OnClickAction,
        MediaAttachmentUrl = mediaAttachmentUrl ?? existing.MediaAttachmentUrl,
        ActionButton1 = actionButton1 ?? existing.ActionButton1,
        ActionButton2 = actionButton2 ?? existing.ActionButton2,
        ActionButton3 = actionButton3 ?? existing.ActionButton3
    };
    if (!string.Equals(existingKey, id, StringComparison.Ordinal))
    {
        notifications.Remove(existingKey);
    }
    notifications[id] = updated;
    mutated = true;
}
else
{
    var stored = new StoredNotification(
        Id: id,
        Message: message,
        Title: title,
        Icon: icon,
        Severity: severity,
        Created: created,
        ReceivedAt: DateTimeOffset.UtcNow,
        IsRead: false,
        IsDismissed: false,
        ReferenceId: referenceId,
        OnClickAction: onClickAction,
        MediaAttachmentUrl: mediaAttachmentUrl,
        ActionButton1: actionButton1,
        ActionButton2: actionButton2,
        ActionButton3: actionButton3);
    notifications[id] = stored;
    mutated = true;

    if (notifications.Count > MaxEntries)
    {
        TrimExcessLocked();
    }
}
```

Add these public methods near `Hide`:

```csharp
public void HideByReferenceId(string referenceId)
{
    HideWhere(n => string.Equals(n.ReferenceId, referenceId, StringComparison.OrdinalIgnoreCase));
}

public void HideByTag(string tag)
{
    HideWhere(n => string.Equals(n.Severity, tag, StringComparison.OrdinalIgnoreCase));
}

private void HideWhere(Func<StoredNotification, bool> predicate)
{
    bool mutated = false;
    lock (syncRoot)
    {
        foreach (var key in notifications.Keys.ToList())
        {
            var existing = notifications[key];
            if (predicate(existing) && !existing.IsDismissed)
            {
                notifications[key] = existing with { IsDismissed = true, IsRead = true };
                mutated = true;
            }
        }
    }

    if (mutated)
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }
}
```

- [ ] **Step 4: Run store tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~NotificationStoreTests"
```

Expected: all `NotificationStoreTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src\OpenHab.App\Notifications\NotificationStore.cs tests\OpenHab.App.Tests\Notifications\NotificationStoreTests.cs
git commit -m "Support notification replacement and hide targets"
```

---

## Task 3: Testable Toast Request And Hero Image Rendering

**Files:**
- Create: `src/OpenHab.Windows.Notifications/ToastNotificationRequest.cs`
- Create: `src/OpenHab.Windows.Notifications/ToastNotificationXmlBuilder.cs`
- Modify: `src/OpenHab.Windows.Notifications/ToastService.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/ToastNotificationXmlBuilderTests.cs`

- [ ] **Step 1: Add failing toast XML tests**

Create `tests/OpenHab.App.Tests/Notifications/ToastNotificationXmlBuilderTests.cs`:

```csharp
using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class ToastNotificationXmlBuilderTests
{
    [Fact]
    public void BuildXml_IncludesHeroImageButtonsLaunchAndScenario()
    {
        var request = new ToastNotificationRequest(
            Title: "Motion Detected",
            Body: "Motion detected in the apartment!",
            Actions:
            [
                new NotificationActionButton("Turn on the light", "command", "BulbDesk_01_Switch:ON"),
                new NotificationActionButton("Dismiss", "command", "BulbKitchen_01_Switch:ON")
            ],
            LaunchAction: "ui:navigate:/page/security",
            Important: true,
            Header: "Motion Tag",
            Tag: "Motion Tag",
            ReferenceId: "motion-id-1234",
            AppLogoOverrideUri: new Uri("file:///C:/cache/icon.png"),
            HeroImageUri: new Uri("file:///C:/cache/hero.png"));

        var xml = ToastNotificationXmlBuilder.BuildXml(request).GetXml();
        var text = xml.GetXml();

        Assert.Contains("Motion Detected", text);
        Assert.Contains("Motion detected in the apartment!", text);
        Assert.Contains("placement=\"hero\"", text);
        Assert.Contains("file:///C:/cache/hero.png", text);
        Assert.Contains("Turn on the light", text);
        Assert.Contains("Dismiss", text);
        Assert.Contains("command:BulbDesk_01_Switch:ON", text);
        Assert.Contains("ui:navigate:/page/security", text);
        Assert.Contains("scenario=\"urgent\"", text);
    }

    [Fact]
    public void BuildXml_OmitsOptionalFieldsWhenMissing()
    {
        var request = new ToastNotificationRequest(
            "openHAB",
            "Basic notification",
            [],
            null,
            false,
            null,
            null,
            null,
            null,
            null);

        var text = ToastNotificationXmlBuilder.BuildXml(request).GetXml().GetXml();

        Assert.Contains("Basic notification", text);
        Assert.DoesNotContain("placement=\"hero\"", text);
        Assert.DoesNotContain("<actions>", text);
        Assert.DoesNotContain("scenario=\"urgent\"", text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter ToastNotificationXmlBuilderTests
```

Expected: build fails because `ToastNotificationRequest` and `ToastNotificationXmlBuilder` do not exist.

- [ ] **Step 3: Add request model**

Create `src/OpenHab.Windows.Notifications/ToastNotificationRequest.cs`:

```csharp
namespace OpenHab.Windows.Notifications;

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

- [ ] **Step 4: Add XML builder**

Create `src/OpenHab.Windows.Notifications/ToastNotificationXmlBuilder.cs`:

```csharp
using CommunityToolkit.WinUI.Notifications;
using Windows.Data.Xml.Dom;

namespace OpenHab.Windows.Notifications;

public static class ToastNotificationXmlBuilder
{
    public static XmlDocument BuildXml(ToastNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new ToastContentBuilder()
            .AddText(request.Title)
            .AddText(request.Body);

        if (!string.IsNullOrWhiteSpace(request.LaunchAction))
        {
            builder.AddArgument("action", request.LaunchAction.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.Header))
        {
            var headerId = BuildHeaderId(request);
            builder.AddHeader(headerId, request.Header.Trim(), "openhab:open");
        }

        if (request.AppLogoOverrideUri is not null)
        {
            builder.AddAppLogoOverride(request.AppLogoOverrideUri);
        }

        if (request.HeroImageUri is not null)
        {
            builder.AddHeroImage(request.HeroImageUri);
        }

        foreach (var action in request.Actions.Take(3))
        {
            builder.AddButton(
                action.Title,
                ToastActivationType.Foreground,
                $"{action.Type}:{action.Payload}");
        }

        var xml = builder.GetXml();
        if (request.Important)
        {
            xml.DocumentElement.SetAttribute("scenario", "urgent");
        }

        return xml;
    }

    public static string? BuildToastTag(ToastNotificationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ReferenceId))
        {
            return "ref-" + StableSuffix(request.ReferenceId);
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            return "tag-" + StableSuffix(request.Tag);
        }

        return null;
    }

    public static string? BuildToastGroup(ToastNotificationRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Tag) ? null : StableSuffix(request.Tag);
    }

    private static string BuildHeaderId(ToastNotificationRequest request)
    {
        return BuildToastGroup(request) ?? "openhab";
    }

    private static string StableSuffix(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes)[..16];
    }
}
```

- [ ] **Step 5: Modify ToastService to use request model**

In `src/OpenHab.Windows.Notifications/ToastService.cs`, keep existing simple overloads but route them through the request model:

```csharp
public static void Show(string title, string body)
{
    Show(new ToastNotificationRequest(title, body, [], null, false, null, null, null, null, null));
}

public static void Show(string title, string body, IReadOnlyList<NotificationActionButton>? actions)
{
    Show(new ToastNotificationRequest(title, body, actions ?? [], null, false, null, null, null, null, null));
}

public static void Show(ToastNotificationRequest request)
{
    if (!isAvailable || !isInitialized) return;
    ShowInternal(request);
}
```

Replace `ShowInternal(...)` with:

```csharp
private static void ShowInternal(ToastNotificationRequest request)
{
    var seq = Interlocked.Increment(ref _toastSequence);
    DiagnosticLogger.Info(
        $"Toast.Show#{seq} begin title=\"{request.Title}\" actions={request.Actions.Count} " +
        $"packaged={isPackaged} important={request.Important} tag={request.Tag ?? "<none>"} threadId={Environment.CurrentManagedThreadId}");

    try
    {
        var xml = ToastNotificationXmlBuilder.BuildXml(request);
        var toast = new ToastNotification(xml);
        var toastTag = ToastNotificationXmlBuilder.BuildToastTag(request);
        var toastGroup = ToastNotificationXmlBuilder.BuildToastGroup(request);
        if (!string.IsNullOrWhiteSpace(toastTag))
        {
            toast.Tag = toastTag;
        }

        if (!string.IsNullOrWhiteSpace(toastGroup))
        {
            toast.Group = toastGroup;
        }

        if (request.Important)
        {
            toast.Priority = ToastNotificationPriority.High;
        }

        if (isPackaged)
        {
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }
        else
        {
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
        }

        DiagnosticLogger.Info($"Toast.Show#{seq} done");
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Error(
            $"Toast.Show#{seq} FAILED - {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}",
            ex);
        if (ex is InvalidOperationException || ex is COMException)
        {
            isAvailable = false;
            DiagnosticLogger.Warn("ToastService disabled due to persistent failure");
        }
    }
}
```

Replace the existing compatibility overload that accepts `important`, `header`, `tag`, and `appLogoOverrideUri` with:

```csharp
public static void Show(
    string title,
    string body,
    IReadOnlyList<NotificationActionButton>? actions,
    bool important,
    string? header,
    string? tag,
    Uri? appLogoOverrideUri)
{
    Show(new ToastNotificationRequest(
        title,
        body,
        actions ?? [],
        LaunchAction: null,
        Important: important,
        Header: header,
        Tag: tag,
        ReferenceId: null,
        AppLogoOverrideUri: appLogoOverrideUri,
        HeroImageUri: null));
}
```

- [ ] **Step 6: Run toast XML tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter ToastNotificationXmlBuilderTests
```

Expected: all `ToastNotificationXmlBuilderTests` pass.

- [ ] **Step 7: Commit**

```powershell
git add src\OpenHab.Windows.Notifications\ToastNotificationRequest.cs src\OpenHab.Windows.Notifications\ToastNotificationXmlBuilder.cs src\OpenHab.Windows.Notifications\ToastService.cs tests\OpenHab.App.Tests\Notifications\ToastNotificationXmlBuilderTests.cs
git commit -m "Render rich notification toast content"
```

---

## Task 4: Poller Push/Log/Hide Flow

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs`

- [ ] **Step 1: Add failing poller tests**

Create `tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs`:

```csharp
using System.Net;
using System.Text;
using OpenHab.Windows.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationPollerTests
{
    [Fact]
    public async Task PollOnce_RaisesPushAndStoresLogButDoesNotRaiseLogToast()
    {
        var json = """
        [
          {
            "_id": "push-1",
            "message": "Push",
            "created": "2026-05-12T10:00:00Z",
            "payload": { "message": "Push" }
          },
          {
            "_id": "log-1",
            "message": "Log",
            "created": "2026-05-12T10:01:00Z",
            "payload": { "message": "Log", "type": "log" }
          }
        ]
        """;
        using var httpClient = new HttpClient(new StaticJsonHandler(json)) { BaseAddress = new Uri("https://myopenhab.org/") };
        var stored = new List<NormalizedCloudNotification>();
        var raised = new List<NormalizedCloudNotification>();
        using var poller = new NotificationPoller(
            httpClient,
            new Uri("https://myopenhab.org/"),
            onNewNotification: stored.Add);
        poller.NotificationReceived += (_, notification) => raised.Add(notification);

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Equal(["push-1", "log-1"], stored.Select(n => n.Id));
        Assert.Equal(["push-1"], raised.Select(n => n.Id));
        Assert.Equal(CloudNotificationKind.LogOnly, stored[1].Kind);
    }

    [Fact]
    public async Task PollOnce_AppliesHideTargets()
    {
        var json = """
        [
          {
            "_id": "hide-1",
            "message": "",
            "created": "2026-05-12T10:00:00Z",
            "payload": {
              "type": "hideNotification",
              "reference-id": "motion-id-1234",
              "tag": "Motion Tag"
            }
          }
        ]
        """;
        using var httpClient = new HttpClient(new StaticJsonHandler(json)) { BaseAddress = new Uri("https://myopenhab.org/") };
        var hideTargets = new List<NotificationHideTarget>();
        using var poller = new NotificationPoller(
            httpClient,
            new Uri("https://myopenhab.org/"),
            onHideNotification: target => hideTargets.Add(target));

        await poller.PollOnceForTestingAsync(CancellationToken.None);

        Assert.Contains(hideTargets, t => t.Kind == NotificationHideTargetKind.ReferenceId && t.Value == "motion-id-1234");
        Assert.Contains(hideTargets, t => t.Kind == NotificationHideTargetKind.Tag && t.Value == "Motion Tag");
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter NotificationPollerTests
```

Expected: build fails because `NotificationPoller` still exposes `CloudNotification` events and has no test poll method or hide callback.

- [ ] **Step 3: Change poller event/callback types**

In `src/OpenHab.Windows.Notifications/NotificationPoller.cs`:

```csharp
private readonly Action<NormalizedCloudNotification>? onNewNotification;
private readonly Action<NotificationHideTarget>? onHideNotification;

public event EventHandler<NormalizedCloudNotification>? NotificationReceived;
```

Change the constructor callback parameters to:

```csharp
Action<NormalizedCloudNotification>? onNewNotification = null,
Action<NotificationHideTarget>? onHideNotification = null)
```

Assign both fields.

- [ ] **Step 4: Normalize notifications in PollOnceAsync**

Inside the `foreach`, replace direct `CloudNotification` handling with:

```csharp
var normalized = CloudNotificationNormalizer.Normalize(notification);
if (normalized.Kind == CloudNotificationKind.Hide)
{
    foreach (var target in normalized.HideTargets)
    {
        onHideNotification?.Invoke(target);
    }

    continue;
}

if (seenIds.Add(normalized.Id))
{
    if (isDismissedFunc is not null && isDismissedFunc(normalized.Id))
    {
        DiagnosticLogger.Info($"Skipping dismissed notification: Id={normalized.Id}");
        continue;
    }

    newCount++;
    DiagnosticLogger.Info($"New notification: Id={normalized.Id}, Tag={normalized.Tag ?? "none"}, Kind={normalized.Kind}");
    onNewNotification?.Invoke(normalized);
    if (normalized.Kind == CloudNotificationKind.Push)
    {
        RaiseNotification(normalized);
    }
}
```

Change `RaiseNotification` signature:

```csharp
private void RaiseNotification(NormalizedCloudNotification notification)
```

Add test-only method as `internal`:

```csharp
internal Task PollOnceForTestingAsync(CancellationToken cancellationToken)
{
    return PollOnceAsync(cancellationToken);
}
```

Add this item to `src/OpenHab.Windows.Notifications/OpenHab.Windows.Notifications.csproj` so `PollOnceForTestingAsync` is available to the test project:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="OpenHab.App.Tests" />
</ItemGroup>
```

- [ ] **Step 5: Update call sites**

In `src/OpenHab.Windows.Tray/App.xaml.cs`, update the `NotificationPoller` construction temporarily so it compiles:

```csharp
onNewNotification: n =>
{
    notificationStore?.AddOrUpdate(
        n.Id, n.Message, n.Created, n.Title, n.Icon, n.Tag,
        n.ReferenceId, n.OnClickAction, n.MediaAttachmentUrl,
        n.ActionButtons.ElementAtOrDefault(0)?.ToRawButton(),
        n.ActionButtons.ElementAtOrDefault(1)?.ToRawButton(),
        n.ActionButtons.ElementAtOrDefault(2)?.ToRawButton());
},
onHideNotification: target =>
{
    if (target.Kind == NotificationHideTargetKind.ReferenceId)
    {
        notificationStore?.HideByReferenceId(target.Value);
    }
    else
    {
        notificationStore?.HideByTag(target.Value);
    }
});
```

Add helper in `NotificationAction.cs`:

```csharp
public static string ToRawButton(this NotificationActionButton button)
{
    return $"{button.Title}={button.Type}:{button.Payload}";
}
```

Task 6 will refine this app integration.

- [ ] **Step 6: Run poller tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter NotificationPollerTests
```

Expected: all `NotificationPollerTests` pass.

- [ ] **Step 7: Commit**

```powershell
git add src\OpenHab.Windows.Notifications\NotificationPoller.cs src\OpenHab.Windows.Notifications\NotificationAction.cs src\OpenHab.Windows.Notifications\OpenHab.Windows.Notifications.csproj src\OpenHab.Windows.Tray\App.xaml.cs tests\OpenHab.App.Tests\Notifications\NotificationPollerTests.cs
git commit -m "Route normalized notification poll results"
```

---

## Task 5: Media Attachment Resolver

**Files:**
- Create: `src/OpenHab.Windows.Tray/Notifications/NotificationMediaResolver.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationMediaResolverTests.cs`

- [ ] **Step 1: Add failing media resolver tests**

Create `tests/OpenHab.App.Tests/Notifications/NotificationMediaResolverTests.cs`:

```csharp
using System.Net;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Tray.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationMediaResolverTests
{
    [Fact]
    public async Task ResolveAsync_FetchesRelativePathWithLocalBearerAuth()
    {
        var handler = new CaptureHandler(new byte[] { 1, 2, 3 }, "image/png");
        using var httpClient = new HttpClient(handler);
        var resolver = new NotificationMediaResolver(
            httpClient,
            () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.LocalOnly,
                LocalEndpoint = new Uri("http://openhab.local:8080/")
            },
            _ => "local-token",
            _ => null,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var uri = await resolver.ResolveAsync("/static/camera.jpg", CancellationToken.None);

        Assert.NotNull(uri);
        Assert.Equal("http://openhab.local:8080/static/camera.jpg", handler.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task ResolveAsync_FetchesImageItemState()
    {
        var handler = new CaptureHandler(new byte[] { 1, 2, 3 }, "image/png");
        using var httpClient = new HttpClient(handler);
        var resolver = new NotificationMediaResolver(
            httpClient,
            () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.CloudOnly,
                CloudEndpoint = new Uri("https://myopenhab.org/")
            },
            _ => null,
            _ => new CloudCredentials("user@example.com", "secret"),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var uri = await resolver.ResolveAsync("item:Camera_Image", CancellationToken.None);

        Assert.NotNull(uri);
        Assert.Equal("https://myopenhab.org/rest/items/Camera_Image/state", handler.RequestUri!.ToString());
        Assert.Equal("Basic", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsAbsoluteHttpUriWithoutFetch()
    {
        var handler = new CaptureHandler(new byte[] { 1, 2, 3 }, "image/png");
        using var httpClient = new HttpClient(handler);
        var resolver = new NotificationMediaResolver(
            httpClient,
            () => AppSettings.Default,
            _ => null,
            _ => null,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        var uri = await resolver.ResolveAsync("https://example.test/camera.jpg", CancellationToken.None);

        Assert.Equal("https://example.test/camera.jpg", uri!.ToString());
        Assert.Null(handler.RequestUri);
    }

    private sealed class CaptureHandler(byte[] body, string mediaType) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return Task.FromResult(response);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter NotificationMediaResolverTests
```

Expected: build fails because `NotificationMediaResolver` does not exist.

- [ ] **Step 3: Implement media resolver**

Create `src/OpenHab.Windows.Tray/Notifications/NotificationMediaResolver.cs`:

```csharp
using System.Net.Http.Headers;
using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Profiles;

namespace OpenHab.Windows.Tray.Notifications;

public sealed class NotificationMediaResolver
{
    private const int MaxBytes = 3 * 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly Func<AppSettings> getSettings;
    private readonly Func<TransportKind, string?> getApiToken;
    private readonly Func<TransportKind, CloudCredentials?> getCloudCredentials;
    private readonly string cacheDirectory;

    public NotificationMediaResolver(
        HttpClient httpClient,
        Func<AppSettings> getSettings,
        Func<TransportKind, string?> getApiToken,
        Func<TransportKind, CloudCredentials?> getCloudCredentials,
        string? cacheDirectory = null)
    {
        this.httpClient = httpClient;
        this.getSettings = getSettings;
        this.getApiToken = getApiToken;
        this.getCloudCredentials = getCloudCredentials;
        this.cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenHab.WinApp",
            "NotificationMedia");
    }

    public async Task<Uri?> ResolveAsync(string? mediaAttachmentUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaAttachmentUrl))
        {
            return null;
        }

        var value = mediaAttachmentUrl.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        var settings = getSettings();
        var transport = settings.EndpointMode == EndpointMode.CloudOnly ? TransportKind.Cloud : TransportKind.Local;
        var endpoint = transport == TransportKind.Cloud ? settings.CloudEndpoint : settings.LocalEndpoint;

        Uri mediaUri;
        if (value.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            var itemName = value["item:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return null;
            }

            mediaUri = new Uri(endpoint, $"rest/items/{Uri.EscapeDataString(itemName)}/state");
        }
        else if (value.StartsWith("/", StringComparison.Ordinal))
        {
            mediaUri = new Uri(endpoint, value.TrimStart('/'));
        }
        else
        {
            return null;
        }

        return await FetchToCacheAsync(mediaUri, transport, cancellationToken);
    }

    private async Task<Uri?> FetchToCacheAsync(Uri uri, TransportKind transport, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        ApplyAuth(request, transport);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            DiagnosticLogger.Warn($"Notification media request failed: status={(int)response.StatusCode}, path='{uri.PathAndQuery}'");
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            DiagnosticLogger.Warn($"Notification media skipped due to invalid size: bytes={bytes.Length}, path='{uri.PathAndQuery}'");
            return null;
        }

        Directory.CreateDirectory(cacheDirectory);
        var extension = ExtensionFor(response.Content.Headers.ContentType?.MediaType);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.ToString())));
        var path = Path.Combine(cacheDirectory, $"{hash}{extension}");
        await File.WriteAllBytesAsync(path, bytes, timeout.Token);
        return new Uri(path);
    }

    private void ApplyAuth(HttpRequestMessage request, TransportKind transport)
    {
        if (transport == TransportKind.Local)
        {
            var token = getApiToken(transport);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return;
        }

        var credentials = getCloudCredentials(transport);
        if (!string.IsNullOrWhiteSpace(credentials?.UserName))
        {
            var raw = $"{credentials.UserName}:{credentials.Password ?? string.Empty}";
            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
        }
    }

    private static string ExtensionFor(string? mediaType)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => ".img"
        };
    }
}
```

- [ ] **Step 4: Run media resolver tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter NotificationMediaResolverTests
```

Expected: all `NotificationMediaResolverTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src\OpenHab.Windows.Tray\Notifications\NotificationMediaResolver.cs tests\OpenHab.App.Tests\Notifications\NotificationMediaResolverTests.cs
git commit -m "Resolve notification media attachments"
```

---

## Task 6: Notification Action Execution

**Files:**
- Modify: `src/OpenHab.Windows.Notifications/NotificationAction.cs`
- Create: `src/OpenHab.Windows.Tray/Notifications/NotificationActionExecutor.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationActionExecutorTests.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationActionTests.cs`

- [ ] **Step 1: Add failing action parser tests**

Append to `tests/OpenHab.App.Tests/Notifications/NotificationActionTests.cs`:

```csharp
[Fact]
public void Parse_ButtonRejectsEmptyTitleOrAction()
{
    Assert.Null(NotificationActionParser.TryParseButton("=command:Light:ON"));
    Assert.Null(NotificationActionParser.TryParseButton("Light="));
}

[Fact]
public void Parse_TrimsActionParts()
{
    var result = NotificationActionParser.TryParse(" command:Light:ON ");

    Assert.NotNull(result);
    Assert.Equal("command", result.Type);
    Assert.Equal("Light:ON", result.Payload);
}
```

- [ ] **Step 2: Add failing executor tests**

Create `tests/OpenHab.App.Tests/Notifications/NotificationActionExecutorTests.cs`:

```csharp
using System.Net;
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Tray.Notifications;

namespace OpenHab.App.Tests.Notifications;

public sealed class NotificationActionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CommandUsesAuthenticatedOpenHabEndpoint()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var executor = new NotificationActionExecutor(
            httpClient,
            () => AppSettings.Default with
            {
                EndpointMode = EndpointMode.CloudOnly,
                CloudEndpoint = new Uri("https://myopenhab.org/")
            },
            _ => null,
            _ => new CloudCredentials("user@example.com", "secret"),
            _ => { });

        await executor.ExecuteAsync("command:BulbDesk_01_Switch:ON", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://myopenhab.org/rest/items/BulbDesk_01_Switch", handler.RequestUri!.ToString());
        Assert.Equal("ON", handler.Body);
        Assert.Equal("Basic", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task ExecuteAsync_HttpsOpensUrl()
    {
        var opened = new List<string>();
        var executor = new NotificationActionExecutor(
            new HttpClient(new CaptureHandler()),
            () => AppSettings.Default,
            _ => null,
            _ => null,
            opened.Add);

        await executor.ExecuteAsync("https://openhab.org", CancellationToken.None);

        Assert.Equal(["https://openhab.org"], opened);
    }

    [Fact]
    public async Task ExecuteAsync_UiAbsolutePathResolvesAgainstEndpoint()
    {
        var opened = new List<string>();
        var executor = new NotificationActionExecutor(
            new HttpClient(new CaptureHandler()),
            () => AppSettings.Default with { LocalEndpoint = new Uri("http://openhab.local:8080/") },
            _ => null,
            _ => null,
            opened.Add);

        await executor.ExecuteAsync("ui:/some/absolute/path", CancellationToken.None);

        Assert.Equal(["http://openhab.local:8080/some/absolute/path"], opened);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public string? AuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "NotificationActionTests|NotificationActionExecutorTests"
```

Expected: parser tests fail or executor type is missing.

- [ ] **Step 4: Harden parser**

In `src/OpenHab.Windows.Notifications/NotificationAction.cs`, update `TryParse` and `TryParseButton`:

```csharp
public static NotificationAction? TryParse(string? rawAction)
{
    if (string.IsNullOrWhiteSpace(rawAction))
        return null;

    ReadOnlySpan<char> span = rawAction.AsSpan().Trim();

    if (span.StartsWith("https://".AsSpan(), StringComparison.OrdinalIgnoreCase))
        return new NotificationAction("https", span.ToString());

    if (span.StartsWith("http://".AsSpan(), StringComparison.OrdinalIgnoreCase))
        return new NotificationAction("http", span.ToString());

    int colonIndex = span.IndexOf(':');
    if (colonIndex < 0)
        return new NotificationAction(span.ToString(), string.Empty);

    string type = span[..colonIndex].ToString().Trim();
    string payload = span[(colonIndex + 1)..].ToString().Trim();
    if (string.IsNullOrWhiteSpace(type))
        return null;

    return new NotificationAction(type, payload);
}

public static NotificationActionButton? TryParseButton(string? rawButton)
{
    if (string.IsNullOrWhiteSpace(rawButton))
        return null;

    ReadOnlySpan<char> span = rawButton.AsSpan().Trim();
    int eqIndex = span.IndexOf('=');
    if (eqIndex <= 0 || eqIndex == span.Length - 1)
        return null;

    string title = span[..eqIndex].ToString().Trim();
    string actionPart = span[(eqIndex + 1)..].ToString().Trim();
    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(actionPart))
        return null;

    NotificationAction? action = TryParse(actionPart);
    if (action is null)
        return null;

    return new NotificationActionButton(title, action.Type, action.Payload);
}
```

- [ ] **Step 5: Implement executor**

Create `src/OpenHab.Windows.Tray/Notifications/NotificationActionExecutor.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Notifications;

namespace OpenHab.Windows.Tray.Notifications;

public sealed class NotificationActionExecutor
{
    private readonly HttpClient httpClient;
    private readonly Func<AppSettings> getSettings;
    private readonly Func<TransportKind, string?> getApiToken;
    private readonly Func<TransportKind, CloudCredentials?> getCloudCredentials;
    private readonly Action<string> openExternal;

    public NotificationActionExecutor(
        HttpClient httpClient,
        Func<AppSettings> getSettings,
        Func<TransportKind, string?> getApiToken,
        Func<TransportKind, CloudCredentials?> getCloudCredentials,
        Action<string>? openExternal = null)
    {
        this.httpClient = httpClient;
        this.getSettings = getSettings;
        this.getApiToken = getApiToken;
        this.getCloudCredentials = getCloudCredentials;
        this.openExternal = openExternal ?? OpenWithShell;
    }

    public async Task ExecuteAsync(string rawAction, CancellationToken cancellationToken)
    {
        var action = NotificationActionParser.TryParse(rawAction);
        if (action is null)
        {
            DiagnosticLogger.Warn($"Unparseable notification action: '{rawAction}'");
            return;
        }

        switch (action.Type.ToLowerInvariant())
        {
            case "command":
                await ExecuteCommandAsync(action.Payload, cancellationToken);
                break;
            case "ui":
                OpenUi(action.Payload);
                break;
            case "http":
            case "https":
                openExternal(action.Payload);
                break;
            case "rule":
            case "app":
                DiagnosticLogger.Warn($"Notification action type '{action.Type}' is not implemented on Windows");
                break;
            default:
                DiagnosticLogger.Warn($"Unknown notification action type: '{action.Type}'");
                break;
        }
    }

    private async Task ExecuteCommandAsync(string payload, CancellationToken cancellationToken)
    {
        var colonIndex = payload.IndexOf(':');
        if (colonIndex <= 0 || colonIndex == payload.Length - 1)
        {
            DiagnosticLogger.Warn($"Invalid command notification action payload: '{payload}'");
            return;
        }

        var itemName = payload[..colonIndex];
        var command = payload[(colonIndex + 1)..];
        var settings = getSettings();
        var transport = settings.EndpointMode == EndpointMode.CloudOnly ? TransportKind.Cloud : TransportKind.Local;
        var endpoint = transport == TransportKind.Cloud ? settings.CloudEndpoint : settings.LocalEndpoint;
        var credentials = transport == TransportKind.Cloud ? getCloudCredentials(transport) : null;
        var client = new OpenHabHttpClient(
            httpClient,
            endpoint,
            apiToken: transport == TransportKind.Local ? getApiToken(transport) : null,
            basicUserName: credentials?.UserName,
            basicPassword: credentials?.Password);

        await client.SendCommandAsync(itemName, command, cancellationToken);
    }

    private void OpenUi(string payload)
    {
        var settings = getSettings();
        var endpoint = settings.EndpointMode == EndpointMode.CloudOnly ? settings.CloudEndpoint : settings.LocalEndpoint;
        var target = payload.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || payload.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? payload
            : new Uri(endpoint, payload.TrimStart('/')).ToString();

        openExternal(target);
    }

    private static void OpenWithShell(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
```

- [ ] **Step 6: Run action tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "NotificationActionTests|NotificationActionExecutorTests"
```

Expected: parser and executor tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src\OpenHab.Windows.Notifications\NotificationAction.cs src\OpenHab.Windows.Tray\Notifications\NotificationActionExecutor.cs tests\OpenHab.App.Tests\Notifications\NotificationActionTests.cs tests\OpenHab.App.Tests\Notifications\NotificationActionExecutorTests.cs
git commit -m "Execute notification actions"
```

---

## Task 7: Tray App Integration

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`
- Test: existing notification tests plus focused build.

- [ ] **Step 1: Add app fields**

In `src/OpenHab.Windows.Tray/App.xaml.cs`, add using:

```csharp
using OpenHab.Windows.Tray.Notifications;
```

Add fields near existing notification fields:

```csharp
private NotificationMediaResolver? notificationMediaResolver;
private NotificationActionExecutor? notificationActionExecutor;
```

- [ ] **Step 2: Initialize services**

After `httpClient = new HttpClient();`, add:

```csharp
notificationMediaResolver = new NotificationMediaResolver(
    httpClient,
    getSettings: () => this.settingsController?.Current ?? AppSettings.Default,
    getApiToken: kind => this.settingsController is null ? null : GetApiTokenSync(this.settingsController, kind),
    getCloudCredentials: kind => this.settingsController is null ? null : GetCloudCredentialsSync(this.settingsController));

notificationActionExecutor = new NotificationActionExecutor(
    httpClient,
    getSettings: () => this.settingsController?.Current ?? AppSettings.Default,
    getApiToken: kind => this.settingsController is null ? null : GetApiTokenSync(this.settingsController, kind),
    getCloudCredentials: kind => this.settingsController is null ? null : GetCloudCredentialsSync(this.settingsController));
```

- [ ] **Step 3: Wire activation through executor**

In `StartNotificationPolling`, replace the existing `ToastService.NotificationActivated` handler with:

```csharp
ToastService.NotificationActivated += (_, args) =>
{
    _ = HandleNotificationActivationAsync(args.Argument);
};
ToastService.PackagedActivated += arguments =>
{
    _ = HandleNotificationActivationAsync(arguments);
};
```

Add method:

```csharp
private async Task HandleNotificationActivationAsync(string? arguments)
{
    var action = ExtractToastAction(arguments);
    if (!string.IsNullOrWhiteSpace(action))
    {
        var executor = notificationActionExecutor;
        if (executor is not null)
        {
            await executor.ExecuteAsync(action, CancellationToken.None);
        }

        return;
    }

    _ = uiDispatcherQueue?.TryEnqueue(() =>
    {
        if (shellController is null) return;
        shellController.HandleNotificationActivated();
        _ = ApplyShellStateAsync();
    });
}

private static string? ExtractToastAction(string? arguments)
{
    if (string.IsNullOrWhiteSpace(arguments))
    {
        return null;
    }

    var prefix = "action=";
    foreach (var part in arguments.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(part[prefix.Length..]);
        }
    }

    return arguments;
}
```

- [ ] **Step 4: Store normalized notifications and hide targets**

Ensure the poller constructor uses normalized callback:

```csharp
onNewNotification: n =>
{
    notificationStore?.AddOrUpdate(
        n.Id, n.Message, n.Created, n.Title, n.Icon, n.Tag,
        n.ReferenceId, n.OnClickAction, n.MediaAttachmentUrl,
        n.ActionButtons.ElementAtOrDefault(0)?.ToRawButton(),
        n.ActionButtons.ElementAtOrDefault(1)?.ToRawButton(),
        n.ActionButtons.ElementAtOrDefault(2)?.ToRawButton());
},
onHideNotification: target =>
{
    if (target.Kind == NotificationHideTargetKind.ReferenceId)
    {
        notificationStore?.HideByReferenceId(target.Value);
    }
    else
    {
        notificationStore?.HideByTag(target.Value);
    }
});
```

- [ ] **Step 5: Show rich toast requests**

Replace the `notificationPoller.NotificationReceived` body with:

```csharp
notificationPoller.NotificationReceived += (_, notification) =>
{
    DiagnosticLogger.Info($"Notification received - tag: {notification.Tag ?? "none"}");
    var importantTags = settingsController.Current.ImportantNotificationTags;
    var isImportant = IsImportantNotification(notification.Tag, importantTags);
    var toastHeader = ResolveNotificationHeader(notification.Tag);

    _ = ShowNotificationToastAsync(
        notification,
        isImportant,
        toastHeader);
};
```

Replace `ShowNotificationToastAsync` signature and body:

```csharp
private async Task ShowNotificationToastAsync(
    NormalizedCloudNotification notification,
    bool isImportant,
    string? toastHeader)
{
    Uri? appLogoOverrideUri = null;
    Uri? heroImageUri = null;
    try
    {
        using var iconCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        appLogoOverrideUri = await ResolveNotificationAppLogoOverrideUriAsync(notification.Icon, iconCts.Token);
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Warn($"Notification icon cache failed: {ex.GetType().Name}: {ex.Message}");
    }

    try
    {
        var resolver = notificationMediaResolver;
        if (resolver is not null)
        {
            heroImageUri = await resolver.ResolveAsync(notification.MediaAttachmentUrl, CancellationToken.None);
        }
    }
    catch (Exception ex)
    {
        DiagnosticLogger.Warn($"Notification media resolution failed: {ex.GetType().Name}: {ex.Message}");
    }

    ToastService.Show(new ToastNotificationRequest(
        Title: string.IsNullOrWhiteSpace(notification.Title) ? "openHAB" : notification.Title,
        Body: notification.Message,
        Actions: notification.ActionButtons,
        LaunchAction: notification.OnClickAction,
        Important: isImportant,
        Header: toastHeader,
        Tag: notification.Tag,
        ReferenceId: notification.ReferenceId,
        AppLogoOverrideUri: appLogoOverrideUri,
        HeroImageUri: heroImageUri));
}
```

Remove old `HandleNotificationActionAsync`, `ExecuteCommandActionAsync`, `OpenUiAction`, and `OpenUrlAction` methods from `App.xaml.cs` after executor integration compiles.

- [ ] **Step 6: Keep tray balloon compatibility**

In `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`, keep `ShowBalloon` as-is if `ToastService.Show(string,string)` remains. If the simple overload was removed, replace with:

```csharp
ToastService.Show(new ToastNotificationRequest(title, text, [], null, false, null, null, null, null, null));
```

- [ ] **Step 7: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: build succeeds.

- [ ] **Step 8: Run notification tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter "FullyQualifiedName~Notifications"
```

Expected: notification tests pass.

- [ ] **Step 9: Commit**

```powershell
git add src\OpenHab.Windows.Tray\App.xaml.cs src\OpenHab.Windows.Tray\Tray\TrayIconService.cs
git commit -m "Wire advanced notifications into tray app"
```

---

## Task 8: Verification And Status Update

**Files:**
- Modify: `docs/superpowers/status/openhab-windows-current-state.md`

- [ ] **Step 1: Run direct test gate**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all direct test projects pass.

- [ ] **Step 2: Run tray release build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Expected: Release tray app build succeeds. If files are locked by a running app, close the app or run Debug build before diagnosing code.

- [ ] **Step 3: Attempt full solution gate**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: pass when DesktopBridge targets and restore prerequisites are available. If blocked by `Microsoft.DesktopBridge.props`, record that known environment blocker.

- [ ] **Step 4: Update current state doc**

In `docs/superpowers/status/openhab-windows-current-state.md`, add a dated bullet under `Shipped Product Shape`:

```markdown
- Advanced openHAB Cloud notification payloads are normalized before storage/rendering, including custom title, reference replacement, hide targets, log-only entries, hero-image media attachments, and command/UI/URL action buttons.
```

Add a dated verification note under `Verification Gates` if the file already has suitable wording:

```markdown
- 2026-05-12 advanced notifications: direct test gate and tray Release build were run after implementation; see branch commit history for command output.
```

- [ ] **Step 5: Commit status update**

```powershell
git add docs\superpowers\status\openhab-windows-current-state.md
git commit -m "docs: update advanced notifications status"
```

- [ ] **Step 6: Final diff review**

Run:

```powershell
git status --short
git log --oneline --decorate -5
git diff main...HEAD --stat
```

Expected: worktree is clean except intentionally untracked local artifacts, recent commits show this feature, and diff stat only includes notification implementation, tests, and status docs.
