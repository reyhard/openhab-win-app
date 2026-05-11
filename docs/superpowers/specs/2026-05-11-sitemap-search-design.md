# Sitemap Search Design

Date: 2026-05-11

## Purpose

Add search to the tray flyout sitemap renderer so a user can quickly find sitemap labels in the current navigation context without opening the main UI.

Search should feel like a temporary sitemap page, not like a separate results dialog. The normal sitemap renderer should continue to own row layout and controls.

## Scope

In scope:

- Flyout sitemap search.
- Current page subtree search.
- Labels-only matching.
- Virtual search-results page rendered through the existing sitemap descriptor and surface renderer.
- Frame matches that include their child widgets.
- Live updates from sitemap SSE events, command reconciliation, manual refresh, and visibility changes.

Out of scope for this slice:

- Whole-sitemap global search.
- Matching item names, states, raw values, commands, or metadata.
- Fuzzy matching, ranking, or highlighting matched substrings.
- Main window search.
- Persisting search history.
- `Ctrl+F` or `/` entry shortcuts.

## UX

The flyout header adds a search icon immediately to the left of the minimize/collapse button.

When search is inactive:

- The flyout behaves as it does today.
- Breadcrumbs remain visible when the current page depth is greater than one.

When search is active:

- The breadcrumb row is replaced by a search input.
- The search input receives focus automatically.
- The search icon uses an active or accent visual state.
- The content area renders a virtual search-results sitemap page.
- The virtual page starts with a small context heading, for example `Search results`.
- The heading includes compact context such as `3 results in current section and child pages`.
- The input includes a clear/close affordance.
- `Esc` exits search mode and restores the normal current page and breadcrumb row.

Keyboard shortcuts:

- `Esc` exits active search mode.
- Search mode entry is icon-click-only in the first slice.

Empty query behavior:

- An empty or whitespace-only query shows the normal current page.
- It does not show an empty search page.

No-match behavior:

- Render a lightweight empty state in the content area.
- Text: `No matching sitemap elements`.

## Search Semantics

Search scope is the current page subtree:

- The current real page is searched.
- Child pages reachable from widgets on the current page are searched recursively.
- Pages outside the current page subtree are not searched.

Matching uses visible labels only:

- Widget labels and frame labels are matched.
- Item names are not matched.
- States and raw item states are not matched.
- Hidden widgets are not matched.

Initial matching is:

- Trimmed.
- Case-insensitive.
- Culture-invariant substring matching.

Frame behavior:

- If a frame label matches, the frame appears in results and its visible child widgets are included beneath it.
- If only child widgets match, include enough grouping context to make the result understandable without deep nesting.
- The result list should be flat with grouping context rather than arbitrarily nested.

Duplicate labels:

- Show all matches.
- Use grouping and source context to distinguish them.

Unsupported widgets:

- If an unsupported widget label matches, render the existing fallback row behavior.
- Fallback actions should continue to route through the main UI or browser fallback path.

## Architecture

`SitemapRuntimeController` owns search state and virtual results. This keeps search UI-independent and close to the existing sitemap page, back stack, descriptor, and row action logic.

The flyout owns only shell interaction:

- Search icon click.
- Search active visual state.
- Replacing `BreadcrumbBar` with the search input.
- Focus handling.
- Query text changes.
- Clear and `Esc` handling.

The renderer should not know that search results are synthetic. It receives a normal `SitemapRenderDescriptor`.

Search state is temporary:

- Activating search does not push onto the real sitemap back stack.
- Clearing search restores the normal descriptor for the still-current real page.
- Navigating normally while search is active exits search mode unless the navigation is a search result action that intentionally opens the source page.

## Runtime Models

Add a UI-independent search result model in `OpenHab.App`. Search state depends on runtime navigation and action routing, so it belongs beside `SitemapRuntimeController` rather than in `OpenHab.Sitemaps`.

`SitemapSearchResult` should include:

- Match kind: frame, row, child-row, empty-state if represented as a row.
- Source page id.
- Source page path labels.
- Source widget id when available.
- Source widget path.
- Source widget label.
- Source widget type and render-relevant data.
- Original row index only as an optimization when the source row is on the current real page.

Do not use row index as the source of truth.

Source identity rules:

- Prefer `WidgetId`.
- If `WidgetId` is missing, fall back to a stable widget path based on page id plus label, type, item, and action.
- Use row index only as a last-resort disambiguator.
- If a source cannot be resolved uniquely, fail safely instead of commanding the wrong widget.

Search descriptor builder:

- Converts `SitemapSearchResult` entries into a `SitemapRenderDescriptor`.
- Reuses existing row mapping where possible.
- Adds grouping/context rows through descriptor data rather than WinUI-only controls.
- Preserves action metadata needed to route commands, navigation, and fallback actions back to the source widget.

## Action Routing

For result rows from the current real page:

- Row activation and commands may reuse existing row-index methods after verifying the current widget identity still matches.

For result rows from child pages:

- Resolve the source widget against the latest current sitemap tree by widget id or stable source path before executing.
- Commands execute against the original widget item.
- Fallback actions use the original source widget.
- Navigation actions navigate to the original child page, not deeper into the synthetic search page.

For stale results:

- If the source widget disappeared, became hidden, or cannot be resolved uniquely, return `false`.
- Recompute active search results from the latest sitemap tree.
- Do not log sensitive endpoint details or raw response bodies.

## Live Updates And Reconciliation

Search results must stay live while search mode is active.

SSE behavior:

- Apply sitemap SSE widget events to the underlying normalized sitemap state first.
- If search is active, recompute the virtual search descriptor for the current query after applying the event.
- State changes update existing matching result rows.
- Visibility changes affect membership. A matching row that becomes hidden disappears. A hidden row that becomes visible and matches appears.

Command behavior:

- Commands triggered from search results run through the same command and reconcile path as normal rows.
- The post-command reconcile refresh recomputes active search results before notifying the UI.

Manual refresh behavior:

- Manual refresh keeps search mode active.
- Results are recomputed for the same query from the refreshed sitemap tree.

Sitemap order changes:

- Recomputed results follow the latest sitemap order.
- Widget ids prevent order changes from breaking action routing.
- Missing ids with duplicate fallback identities are ambiguous and must fail safely for actions.

Partial row update behavior:

- Active search should not blindly use normal partial row updates, because visibility or frame inclusion can change the synthetic result structure.
- Treat active-search SSE and reconcile changes as descriptor recomputation.

Current page disappearance:

- If the current page disappears during refresh or reconcile, follow the existing runtime behavior for resolving/falling back to a page.
- Clear search if the original subtree is no longer valid.

## Error Handling

- Empty query restores normal current page rendering.
- No matches render the empty state.
- Stale source actions fail safely and recompute.
- Ambiguous duplicate fallback identities fail safely for actions.
- Search should not expose credentials, tokens, full endpoint URLs, or raw server payloads in status text or diagnostics.

## Testing

App/runtime tests:

- Search inactive preserves the normal current descriptor.
- Empty query restores the normal current descriptor.
- Query searches the current page subtree only.
- Labels-only matching ignores item names.
- Labels-only matching ignores state values.
- Hidden widgets are excluded from matches.
- Frame match includes visible child widgets.
- Child-page match appears with grouping context.
- Duplicate labels all appear.
- Clearing search restores the original descriptor and navigation state.
- Manual refresh while search is active recomputes results.
- SSE state change updates an existing search result row.
- SSE visibility false removes a matching result.
- SSE visibility true adds a matching result.
- Command from a search result triggers reconcile and updates the search descriptor.
- Frame inclusion updates when a child widget visibility changes.
- Reordered widgets keep action routing by widget id.
- Reordered child page results render in latest sitemap order.
- Missing widget id with duplicate fallback identity fails safely rather than commanding the wrong row.
- Stale source action fails safely.

Windows-layer tests where practical:

- Breadcrumb row visibility switches with search mode.
- Search input replaces breadcrumbs and receives focus.
- Search icon toggles active state.
- Clear or `Esc` exits search mode.
- Existing renderer receives a normal descriptor; no special synthetic renderer path is introduced.

Verification gate after implementation:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Run the full solution gate when practical:

```powershell
dotnet test OpenHab.Windows.sln
```
