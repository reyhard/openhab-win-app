# Notification Reliability and Review Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop previously seen cloud notifications from being delivered again, make notification history persistence deterministic and crash-resistant, and resolve the remaining SSE-state, localization, notification-media-cache, and reconnect-task issues identified in the 2026-07-15 code review.

**Architecture:** Deliver notification reliability as the first, independently verified P0 slice. Replace the poller's clear-all seen-ID behavior with an ordered bounded recent-ID set seeded from the newest persisted notifications. Route notification-store writes through one per-path, coalescing writer that commits snapshots atomically and can be flushed before shutdown. Keep sitemap connection state in `OpenHab.App`, localized text behind `ITextLocalizer`, cache policy in the app layer, and WinUI-specific wiring in `OpenHab.Windows.Tray`.

**Tech Stack:** .NET 10, WinUI/Windows App SDK, xUnit, JSON file persistence, existing `ITextLocalizer` and app settings/runtime layers.

**Priority rule:** Tasks 1 and 2 are P0 and must be implemented, reviewed, and verified before Tasks 3-5 begin. Do not combine the P0 fixes with localization or cache work in one commit. The user's observed old-notification resend is the primary release risk covered by this plan.

**Verification rule:** Use direct project tests during iteration. Run all four direct test projects and the tray Release build before completion. A package build is not required unless implementation changes package, manifest, startup-task, notification-activation registration, signing, or packaging files.

---

### Task 1 (P0): Preserve Notification Deduplication Beyond the Capacity Boundary

**Files:**
- Create: `src/OpenHab.Windows.Notifications/RecentNotificationIdSet.cs`
- Modify: `src/OpenHab.Windows.Notifications/NotificationPoller.cs`
- Modify: `src/OpenHab.App/Notifications/NotificationStore.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`

- [ ] **Step 1: Add a regression that reproduces the observed resend**

In `NotificationPollerTests`, construct `preSeenIds` with more than the current 200-ID limit, including the ID returned by the fake cloud response. Poll twice and assert that neither poll stores nor raises the already-seen push notification.

The test must reproduce the current failure mode:

1. The first poll recognizes the returned ID as seen.
2. The current `seenIds.Count > MaxSeenIds` branch clears the complete set.
3. The second poll incorrectly raises the same notification.

Use a descriptive name such as:

```csharp
PollOnce_MoreThanCapacityPreSeenIds_DoesNotRedispatchPreviouslySeenNotification
```

- [ ] **Step 2: Add ordered-store seed tests**

Add tests for a new store query that returns only recent undismissed IDs in deterministic oldest-to-newest order. Cover:

- more stored notifications than the requested limit;
- dismissed notifications excluded from the seed;
- the newest IDs retained;
- `Created`, then `ReceivedAt`, then ID used as deterministic tie-breakers.

Prefer an API shaped like:

```csharp
public IReadOnlyList<string> GetRecentSeenUndismissedIds(int maxCount)
```

Reject non-positive `maxCount` with `ArgumentOutOfRangeException`.

- [ ] **Step 3: Run the focused tests and verify red**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationPollerTests|FullyQualifiedName~NotificationStoreTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: the poller regression fails because the second poll re-dispatches the old notification, and the store tests fail to compile until the ordered seed API exists.

- [ ] **Step 4: Implement a bounded recent-ID set**

Create `RecentNotificationIdSet` in `OpenHab.Windows.Notifications` with:

- a `HashSet<string>` for membership;
- a FIFO queue or linked list for eviction order;
- a fixed positive capacity;
- constructor seeding in oldest-to-newest order;
- `Add(string id)` returning `false` for an existing ID;
- oldest-only eviction while count exceeds capacity;
- no code path that clears every retained ID merely because the capacity was crossed.

Use a capacity of 500 so it aligns with the current `NotificationStore` maximum. Keep the value centralized rather than duplicating independent numeric literals in `App.xaml.cs` and `NotificationPoller`.

Do not expose a generic collection abstraction unless another current caller needs it; this helper exists to express notification deduplication semantics.

- [ ] **Step 5: Seed the poller with the newest persisted IDs**

Replace `NotificationStore.GetSeenUndismissedIds()` at poller construction with the ordered, capacity-bounded API. Pass the sequence oldest-to-newest so the most recent IDs survive if defensive trimming is needed.

In `NotificationPoller`:

- replace the raw `HashSet<string>` with `RecentNotificationIdSet`;
- preserve the existing `if (!recentIds.Add(normalized.Id)) continue;` behavior;
- delete the `seenIds.Clear()` rollover block;
- continue tracking push, log-only, and hide payload IDs so every notification kind remains deduplicated.

- [ ] **Step 6: Add eviction behavior coverage**

Through `NotificationPollerTests` or direct internal-helper tests, prove that:

- adding capacity + 1 distinct IDs evicts only the oldest;
- the newest ID remains seen;
- adding an existing ID does not grow the collection;
- crossing capacity repeatedly never makes all recent IDs unseen.

- [ ] **Step 7: Run notification tests and verify green**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationPollerTests|FullyQualifiedName~NotificationStoreTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: all notification poller and store tests pass, including the two-poll >200-ID regression.

- [ ] **Step 8: Commit the P0 deduplication fix**

```powershell
git add src/OpenHab.Windows.Notifications/RecentNotificationIdSet.cs src/OpenHab.Windows.Notifications/NotificationPoller.cs src/OpenHab.App/Notifications/NotificationStore.cs src/OpenHab.Windows.Tray/App.xaml.cs tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs
git commit -m "fix: retain recent notification ids"
```

---

### Task 2 (P0): Serialize and Atomically Flush Notification History

**Files:**
- Create: `src/OpenHab.App/Notifications/NotificationStorePersistenceQueue.cs`
- Modify: `src/OpenHab.App/Notifications/NotificationStore.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs`

- [ ] **Step 1: Add public-behavior persistence tests**

Add tests that rapidly perform at least 25 `AddOrUpdate` calls, await a new `FlushAsync`, construct a fresh store from the same path, and assert that every expected notification and the final state are present.

Also cover rapid mixed mutations:

- add notifications;
- mark read/unread;
- hide/unhide;
- update by reference ID;
- flush and reload;
- assert the reloaded state matches the final in-memory snapshot.

The initial red state may be a compile failure because `FlushAsync` does not exist. Do not use arbitrary sleeps as the synchronization mechanism.

- [ ] **Step 2: Add atomic-file behavior coverage**

Using a temporary directory, assert after `FlushAsync` that:

- `notifications.json` contains valid JSON;
- no temporary write file remains after success;
- a fresh store can load the complete final snapshot;
- a failed replacement leaves the last valid destination file loadable where this can be tested without platform-specific file locking.

- [ ] **Step 3: Run store tests and verify red**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationStoreTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: compile failure for the missing flush API or failing persistence assertions.

- [ ] **Step 4: Implement one writer per normalized storage path**

Create a small persistence coordinator owned by `OpenHab.App.Notifications`:

- key coordinators by `Path.GetFullPath(storageFilePath)` using case-insensitive path comparison;
- accept immutable serialized snapshots, never a live dictionary reference;
- allow only one file write at a time per path;
- coalesce pending writes to the newest snapshot while a write is active;
- ensure a mutation arriving while the writer is exiting starts or continues the writer without being stranded;
- order snapshot capture/version assignment and enqueue while holding `NotificationStore.syncRoot`, so concurrent callers cannot enqueue a stale snapshot after a newer one;
- expose `FlushAsync` as a generation barrier captured under the coordinator lock, not merely as the current worker task;
- complete or fault each barrier only after its captured generation, or a newer coalesced generation, has been attempted;
- use `TaskCompletionSource` with `RunContinuationsAsynchronously` and avoid UI-context capture in the writer.

Keep persistence decisions out of `OpenHab.Windows.Tray`.

- [ ] **Step 5: Write snapshots atomically**

For each queued snapshot:

1. Create the destination directory.
2. Write UTF-8 JSON to a temporary file in the same directory.
3. Flush and close the temporary file.
4. Replace the existing destination atomically where supported, or move the temporary file into place when creating the file for the first time.
5. Delete a leftover temporary file in `finally`.

Never truncate the current `notifications.json` before a complete replacement is ready. Log a privacy-safe warning through `DiagnosticLogger` when persistence fails; do not silently swallow the error.

- [ ] **Step 6: Route every store mutation through the queue**

Replace `_ = SaveAsync()` in `SaveIfEnabled` with an enqueue of the immutable final snapshot. Preserve current `Changed` event timing and in-memory behavior.

Add:

```csharp
public Task FlushAsync()
```

When `persistChanges` is false, `FlushAsync` should return `Task.CompletedTask`.

If the write satisfying a flush generation fails, log the safe failure, continue draining any newer pending snapshot, and fault that explicit flush barrier. Do not create unobserved faulted tasks merely because a background mutation queued a save.

- [ ] **Step 7: Flush after stopping the poller during app shutdown**

In `ShutdownTrayResourcesCore`:

1. Prevent notification polling reconfiguration/start once shutdown begins.
2. Await `NotificationPoller.DisposeAsync()`/`StopAsync()` so its in-flight callback has completed and no later store mutation can arrive.
3. Flush `notificationStore` before `Exit()`/`Environment.Exit(0)` can terminate the process.
4. Catch and log a safe persistence failure without preventing the rest of shutdown cleanup.

Refactor the normal tray-exit path to await this shutdown sequence before calling `Exit()` and `Environment.Exit(0)`. Do not enqueue cleanup to the UI dispatcher and immediately terminate the process. Keep `ProcessExit` as a best-effort fallback; it must not depend on work queued to a dispatcher that may no longer run.

The persistence and poller shutdown awaits must avoid UI-context capture. If a synchronous fallback bridge remains for process teardown, it may wait only on operations proven not to require the UI dispatcher.

- [ ] **Step 8: Run the P0 notification slice**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationPollerTests|FullyQualifiedName~NotificationStoreTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false
```

Expected: focused tests pass; tray Release build succeeds with 0 warnings and 0 errors.

- [ ] **Step 9: Manually validate the observed resend scenario before continuing**

With a backed-up local app state:

1. Start with more than 200 undismissed historical notifications.
2. Launch the app and allow at least two cloud polling intervals.
3. Confirm previously stored notifications do not produce new toasts.
4. Receive one genuinely new notification and confirm it is stored/toasted once.
5. Exit normally, relaunch, and allow two more polling intervals.
6. Confirm neither the historical notifications nor the new notification are resent.
7. Inspect `%localappdata%\OpenHab.WinApp\diagnostics.log` for persistence or poll errors without copying credentials into implementation notes.

- [ ] **Step 10: Commit the P0 persistence fix**

```powershell
git add src/OpenHab.App/Notifications/NotificationStorePersistenceQueue.cs src/OpenHab.App/Notifications/NotificationStore.cs src/OpenHab.Windows.Tray/App.xaml.cs tests/OpenHab.App.Tests/Notifications/NotificationStoreTests.cs tests/OpenHab.App.Tests/Notifications/NotificationPollerTests.cs
git commit -m "fix: serialize notification persistence"
```

---

### Task 3: Publish SSE Connection-State Changes and Observe Reconnect Tasks

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Modify: `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs`
- Modify: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`
- Modify: `src/OpenHab.Windows.Tray/Strings/pl-PL/Resources.resw`
- Test: `tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs`
- Test: `tests/OpenHab.App.Tests/Localization/LocalizationResourceTests.cs`

- [ ] **Step 1: Add connection-state publication tests**

Extend the existing fake event-stream client and add tests proving:

- `disconnected` changes an online snapshot to degraded and raises `SnapshotChanged` once;
- `reconnecting` changes the status text and raises `SnapshotChanged`;
- `connected` restores online state and raises when state actually changed;
- an identical repeated state/status does not raise a redundant snapshot event;
- the app-facing snapshot is already updated when an event handler reads `controller.Current`.

- [ ] **Step 2: Add a reconnect task-observation test**

Configure the fake client's `ConnectAsync` to fail. Assert that awaiting `ReconnectSitemapEventStreamAsync` observes the same failure rather than returning before the discarded connection task completes.

- [ ] **Step 3: Run runtime tests and verify red**

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter FullyQualifiedName~SitemapRuntimeControllerTests -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: snapshot publication assertions fail, and the reconnect task assertion fails because the current method discards `ConnectAsync`.

- [ ] **Step 4: Publish connection-state snapshots**

In `OnConnectionStateChanged`:

- compute the next connection state and localized status first;
- assign `Current` only when observable state/status changes;
- clear changed row indices as today;
- raise `SnapshotChanged` after assignment;
- leave UI dispatching to the existing Windows-layer refresh gates.

Add and use a localized key such as `Runtime.LiveUpdates.Reconnecting`. Do not retain the hardcoded English reconnecting sentence.

- [ ] **Step 5: Return the reconnect operation**

Remove the unnecessary `async` state machine and discarded task. Preserve the public method if it is useful to tests/callers, but return the actual connection task:

```csharp
public Task ReconnectSitemapEventStreamAsync(...)
```

Return `Task.CompletedTask` only when there is no event-stream client or subscription. Otherwise return/await `sitemapEventStreamClient.ConnectAsync(...)` so cancellation and failure are observable.

- [ ] **Step 6: Run runtime and localization tests**

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~SitemapRuntimeControllerTests|FullyQualifiedName~LocalizationResourceTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: all focused tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/OpenHab.App/Runtime/SitemapRuntimeController.cs src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs src/OpenHab.Windows.Tray/Strings tests/OpenHab.App.Tests/Runtime/SitemapRuntimeControllerTests.cs tests/OpenHab.App.Tests/Localization/LocalizationResourceTests.cs
git commit -m "fix: publish sitemap connection changes"
```

---

### Task 4: Remove Review-Identified Hardcoded User-Visible English

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Voice/VoiceCommandConfirmationWindow.cs`
- Modify: `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs`
- Modify: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`
- Modify: `src/OpenHab.Windows.Tray/Strings/pl-PL/Resources.resw`
- Test: `tests/OpenHab.App.Tests/Localization/LocalizationResourceTests.cs`
- Test: `tests/OpenHab.App.Tests/Localization/WinUiTextLocalizerTests.cs`

- [ ] **Step 1: Add source-regression assertions for the identified strings**

Extend `LocalizationResourceTests` with focused assertions that the affected runtime/UI files do not contain the known user-visible English literals found in review. Include at minimum:

- show/hide sitemap;
- expand/collapse navigation;
- no promoted pages;
- no/multiple sitemaps detected;
- Main UI load and credential retry errors;
- voice online/invalid/unavailable/listening/canceled statuses;
- disconnected/client unavailable/command-surface failure statuses;
- voice confirmation title, transcript label, send, cancel, and empty transcript fallback.

Keep the test targeted to user-visible literals; do not flag diagnostic messages, exception messages, protocol values, font names, or automation implementation identifiers.

- [ ] **Step 2: Run localization tests and verify red**

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalizationResourceTests|FullyQualifiedName~WinUiTextLocalizerTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: source-regression assertions fail for the current hardcoded literals.

- [ ] **Step 3: Add English fallback and English/Polish resource keys**

Add semantically named keys rather than reusing unrelated text. Keep `DefaultEnglishTextLocalizer`, `en-US/Resources.resw`, and `pl-PL/Resources.resw` in parity. Preserve placeholder parity.

Suggested key groups:

- `MainWindow.Sitemap.Show` / `MainWindow.Sitemap.Hide`;
- `MainWindow.Navigation.Expand` / `MainWindow.Navigation.Collapse`;
- `MainWindow.MainUiPages.Empty`;
- `Runtime.Sitemap.NoneDetected` / `Runtime.Sitemap.MultipleDetected`;
- `MainUi.Error.Unavailable` / `MainUi.Error.CheckEndpointAndCredentials`;
- `Voice.Status.*` and `Voice.Confirmation.*`;
- `Shortcuts.Status.Disconnected` / `Shortcuts.Status.ClientUnavailable` / `Shortcuts.Status.SurfaceFailed`.

- [ ] **Step 4: Route existing localized owners through `ITextLocalizer`**

- `MainWindow` already has an `ITextLocalizer`; use it for dynamic tooltips, automation names, and empty promoted-page text instead of overwriting localized XAML values with English.
- `App` already owns the process localizer; use it for banner and shell status text.
- Give `MainUiWebViewHost` an optional `ITextLocalizer` constructor dependency with `DefaultEnglishTextLocalizer.Instance` fallback, and pass `MainWindow`'s localizer from `CreateMainUiHost`.
- Give `VoiceCommandConfirmationWindow` an optional `ITextLocalizer` dependency and pass the app localizer at construction.
- Use `x:Uid` for static XAML text where the control is XAML-owned; use `ITextLocalizer` for dynamic code-generated text.

Do not introduce WinUI resource APIs into `OpenHab.App`.

- [ ] **Step 5: Run localization tests**

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalizationResourceTests|FullyQualifiedName~WinUiTextLocalizerTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: resource/placeholder parity and source-regression tests pass.

- [ ] **Step 6: Build and manually smoke Polish UI**

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false
```

With Polish selected and the app restarted, inspect:

- sitemap toggle tooltip and automation name in both states;
- collapsed/expanded navigation tooltip and automation name;
- no promoted pages state;
- no/multiple sitemap banner;
- Main UI error and retry surfaces;
- voice confirmation and voice failure/cancel statuses;
- reconnecting/disconnected runtime status.

Expected: no reviewed surface falls back to hardcoded English, and text is not truncated.

- [ ] **Step 7: Commit**

```powershell
git add src/OpenHab.Windows.Tray/App.xaml.cs src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/MainUi src/OpenHab.Windows.Tray/Voice/VoiceCommandConfirmationWindow.cs src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs src/OpenHab.Windows.Tray/Strings tests/OpenHab.App.Tests/Localization
git commit -m "fix: localize dynamic windows status text"
```

---

### Task 5: Bound and Invalidate the Notification Media Cache

**Files:**
- Create: `src/OpenHab.App/Notifications/NotificationMediaCache.cs`
- Modify: `src/OpenHab.App/Notifications/NotificationMediaResolver.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationMediaResolverTests.cs`
- Test: `tests/OpenHab.App.Tests/Notifications/NotificationMediaCacheTests.cs`
- Test: `tests/OpenHab.App.Tests/Rendering/SitemapMediaCacheInvalidationPolicyTests.cs` if the existing endpoint/auth profile signal is reused

- [ ] **Step 1: Add failing retention-policy tests**

Using a temporary cache directory and controlled timestamps, cover:

- entry-count eviction removes the oldest file only;
- total-byte eviction removes oldest files until under budget;
- age eviction removes expired files;
- the just-written file is retained when it is individually within `maxBytes`;
- cleanup of a locked or inaccessible file is best effort and does not fail media resolution;
- clearing the cache after an endpoint/auth profile change removes stale files without deleting outside the configured cache root.

- [ ] **Step 2: Run media tests and verify red**

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationMedia" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: compile failure for the missing cache policy or failing eviction assertions.

- [ ] **Step 3: Implement an app-layer cache owner**

Create `NotificationMediaCache` with conservative defaults:

- maximum 64 files;
- maximum 32 MiB total;
- maximum age 30 days;
- the existing 2 MiB per-download limit remains enforced by `NotificationMediaResolver`.

The cache owner should:

- resolve paths only beneath its configured root;
- write media through a temporary file and move it into place;
- update last-write/access metadata for the retained entry;
- prune by age first, then oldest-first for count and total bytes;
- expose best-effort `PruneAsync` and `Clear` operations;
- catch/log individual cleanup failures without blocking the text-only notification path.

Keep enumeration and deletion within one filesystem implementation. Do not pass computed cache paths to another shell for deletion.

- [ ] **Step 4: Inject and reuse the cache owner**

Have `App` create one notification-media cache for the process and pass it to each resolver rather than letting each toast create an unmanaged cache policy.

When the existing sitemap media cache profile detects endpoint or credential changes, also clear the notification media cache. This prevents stale authenticated media from surviving a profile switch. Keep profile comparison in the existing app-layer policy; do not expose credentials in keys or logs.

- [ ] **Step 5: Preserve graceful media fallback**

If cache write or cleanup fails:

- log only safe source kind, host, media type, and byte count;
- return `null` for the hero image when no safe local file exists;
- continue showing notification title/body/actions;
- never log tokens, basic credentials, full sensitive paths, or response bodies.

- [ ] **Step 6: Run media and invalidation tests**

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationMedia|FullyQualifiedName~SitemapMediaCacheInvalidationPolicyTests" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: all focused tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/OpenHab.App/Notifications/NotificationMediaCache.cs src/OpenHab.App/Notifications/NotificationMediaResolver.cs src/OpenHab.Windows.Tray/App.xaml.cs tests/OpenHab.App.Tests/Notifications tests/OpenHab.App.Tests/Rendering/SitemapMediaCacheInvalidationPolicyTests.cs
git commit -m "fix: bound notification media cache"
```

---

### Task 6: Full Verification, Manual Evidence, and Current-State Update

**Files:**
- Modify: `docs/superpowers/status/openhab-windows-current-state.md`
- Modify: `docs/superpowers/verification/coverage-exclusion-inventory.md` only if implementation changes an exclusion
- Verify: all files changed in Tasks 1-5

- [ ] **Step 1: Run formatting and diff checks**

```powershell
dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes
git diff --check
```

Expected: no formatting or whitespace errors. If `dotnet format` attempts to evaluate the packaging project and hits the documented DesktopBridge issue, run it against the changed direct projects and record the caveat.

- [ ] **Step 2: Run all direct tests**

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: all direct projects pass and exit cleanly.

- [ ] **Step 3: Run coverage for changed testable logic**

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --collect "XPlat Code Coverage" --settings coverage.runsettings -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Expected: new recent-ID, persistence-queue, runtime state, localization decision, and media-cache policy logic is represented in coverage. Do not add broad exclusions to make the metric pass.

- [ ] **Step 4: Run the tray Release build**

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false
```

Expected: build succeeds with 0 warnings and 0 errors. If files are locked by a running app, close it or try the documented Debug diagnostic build before treating the lock as a code failure.

- [ ] **Step 5: Perform the combined manual smoke**

Record results for:

- the >200-history, two-poll, restart notification resend scenario from Task 2;
- one new push, log-only, and hide notification;
- normal exit followed by notification-history reload;
- SSE disconnect/reconnect status and closing of offline-only shortcut UI;
- Polish dynamic tooltips, automation names, banners, Main UI errors, and voice text;
- multiple hero images followed by cache pruning and endpoint/profile change;
- text-only toast fallback when media resolution fails.

- [ ] **Step 6: Update the current-state page**

Update `docs/superpowers/status/openhab-windows-current-state.md` with:

- notification resend root cause and bounded-ID fix;
- serialized atomic notification persistence and shutdown flush;
- SSE snapshot publication/reconnect task behavior;
- completed dynamic localization coverage;
- notification media cache limits/invalidation;
- exact test counts, Release build result, coverage command result, and manual smoke evidence;
- any remaining limitations or deferred manual checks.

Do not put implementation-progress notes in `AGENTS.md`.

- [ ] **Step 7: Final review and status commit**

```powershell
git status --short
git diff --stat
git diff -- docs/superpowers/status/openhab-windows-current-state.md
git add docs/superpowers/status/openhab-windows-current-state.md
git commit -m "docs: record notification reliability verification"
```

Expected: only intentional implementation, tests, plan/status documentation, and any pre-existing user-owned untracked files remain.

---

## Completion Criteria

The work is complete only when all of the following are true:

- Previously seen notifications remain deduplicated when the retained history exceeds 200 entries and across restart.
- Capacity pressure evicts only the oldest tracked IDs; it never clears the full deduplication set.
- Notification history writes cannot overlap or leave an older snapshot as the final file.
- Normal shutdown flushes the latest notification state.
- SSE connected/disconnected/reconnecting changes reach snapshot listeners.
- Reconnect failures and cancellation are observable by callers.
- Review-identified dynamic user-visible text respects English/Polish resources, including accessibility names.
- Notification media storage has tested age, count, and byte limits and is invalidated on endpoint/auth profile change.
- All direct tests pass, App coverage is collected, the tray Release build passes, and manual resend/restart evidence is recorded.
