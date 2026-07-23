# openHAB 5.2 Compatibility Verification

Date: 2026-07-23

## Baseline

The pre-capture baseline was supplied with this task from base commit `7a5bdb07ab2966866fb070d6e261472c2db4d19d` on .NET SDK `10.0.204`:

| Command | Result |
| --- | --- |
| `OpenHab.Core.Tests` | 79/79 passed |
| `OpenHab.Sitemaps.Tests` | 44/44 passed |
| `OpenHab.Rendering.Tests` | 129/129 passed |
| `OpenHab.App.Tests` | 644/644 passed |
| Tray Release build | 0 warnings, 0 errors |

The handoff did not retain per-command durations. No source or project-file changes are included in this fixture-only task.

## Test server configuration

Two isolated, disposable local Docker containers were run from the official images:

| Release | Image digest | Local endpoint | Sitemap ButtonGrid definition |
| --- | --- | --- | --- |
| 5.1.4 | `sha256:d583a280a8a8cdbff5bcebe5bd7d04a7839769350a7e54f600b4aaa26162392f` | `http://127.0.0.1:18081` | Deprecated legacy `buttons` definition |
| 5.2.0 | `sha256:450d2175af9f3ddf0720ed3efd4b1cac2bb2445b76c202c8dedeb7f7b2fdf8a9` | `http://127.0.0.1:18082` | Nested `Button` widgets |

Both servers used the same synthetic `compatibility` sitemap containing frames, text, mapped/unmapped switches, selection, slider, setpoint, a 12-button grid, a linked page, chart, image, video, webview, input, visibility, and label/value/icon color rules. They contained only `Compatibility_Switch`, `Compatibility_Dimmer`, `Compatibility_Number`, `Compatibility_Mode`, and `Compatibility_Text`. No authentication was configured: this was an unauthenticated local transport capture, not the configured personal openHAB instance.

The two containers were removed after capture. The official images were retained in the local Docker cache; no unrelated container or configured personal instance was inspected or changed.

## Captured endpoint matrix

Every listed endpoint was requested from each running release. JSON endpoints returned `HTTP/1.1 200 OK`, `Content-Type: application/json`, and `Vary: Accept-Encoding, User-Agent`; the subscription response was JSON `200 OK`; the page-bound SSE endpoint was `200 OK`, `Content-Type: text/event-stream`. Subscription metadata included a response-body `context.headers.Location` array. The fixture paths contain sanitized bodies; response URLs use `https://openhab.test`.

| Method | Relative endpoint | Fixture |
| --- | --- | --- |
| GET | `/rest/sitemaps` | `sitemaps/list.json` |
| GET | `/rest/sitemaps/compatibility` | `sitemaps/home.json` and the extracted `sitemaps/button-grid.json` |
| GET | `/rest/items` | `items/list.json` |
| GET | `/rest/items/Compatibility_Switch` | `items/test-item.json` |
| GET | `/rest/ui/components/ui:page` | `main-ui/pages.json` |
| POST | `/rest/sitemaps/events/subscribe` | `events/subscription-response.json` |
| GET | `/rest/sitemaps/events/{subscriptionId}?sitemap=compatibility&pageid=compatibility` | `events/widget-update.sse` |

The synthetic test servers had no Main UI page components configured, so both captured `main-ui/pages.json` bodies are genuine empty arrays; the empty arrays are intentionally retained.

## Observed 5.1.4 vs 5.2.0 differences

- 5.1.4 represents the 12-button grid as one `Buttongrid` with 12 legacy `mappings` and no child widgets. Its captured ID is `0006`.
- 5.2.0 represents the grid as a `Buttongrid` with 12 nested `Button` widgets and an empty `mappings` array. It uses variable-width opaque IDs, including `2_000610` and `2_000611` for buttons 10 and 11.
- 5.2.0 also emits `forceAsItem: false` on the captured Chart widget and has a different JSON field order. These fixtures preserve both release shapes rather than normalizing them.
- Both releases retained empty `widgets` and `mappings` arrays where returned, and both emitted real `event:`/`data:` SSE framing after the synthetic switch changed state.

## Upstream change references

- Variable-width sitemap IDs: [openhab/openhab-core#5466](https://github.com/openhab/openhab-core/pull/5466).
- Sitemap DTO compatibility and empty arrays: [openhab/openhab-core#5593](https://github.com/openhab/openhab-core/pull/5593).
- Sitemap OpenAPI schema renaming: [openhab/openhab-core#5523](https://github.com/openhab/openhab-core/pull/5523).

## Sanitization review

All fixture JSON parsed successfully. Each SSE fixture contains `event: event` and `data:` frames for `Compatibility_Switch`. A repository scan found no `127.0.0.1`, `localhost`, bearer/basic credentials, passwords, tokens, or email-like strings in `tests/CompatibilityFixtures`. No real server, username, credential, private label, or private URL was used. `https://openhab.test` is the sanitized server origin and `https://example.invalid/...` media URLs are synthetic.

## Pending implementation tasks

Use these fixtures for parser, normalizer, runtime event matching, and transport tests. Do not add server-version branches; preserve widget IDs as opaque strings and normalize legacy and nested ButtonGrid shapes at the sitemap parser boundary.

## Final results

- Both versions were captured from running instances.
- Fixtures are logically equivalent and use the same synthetic sitemap/item configuration.
- The 5.2 fixture contains nested Buttons and variable-width IDs; the 5.1 fixture retains the legacy representation.
- JSON, SSE framing, ButtonGrid contract assertions, sanitization scan, and `git diff --check` are required final checks for this task.

## Task 6 — Embedded Main UI host validation

Date: 2026-07-23

App commit: `3bd11326a1fe0b75d3848fbf1a7915f9b027bf97`

openHAB version: `5.2.0` (`openhab/openhab:5.2.0`, image `sha256:450d2175af9f3ddf0720ed3efd4b1cac2bb2445b76c202c8dedeb7f7b2fdf8a9`)

### Safe local evidence

One isolated disposable container, `openhab-task6-webview-520`, was started with no configuration mounts, credentials, or access to the configured personal instance. It listened only on `http://127.0.0.1:18082` and was stopped and removed after the check.

| Check | Endpoint/authentication | Result | Limitation |
| --- | --- | --- | --- |
| Main UI server root | Local `http://127.0.0.1:18082/`; unauthenticated | `HTTP 200`, `Content-Type: text/html`, document title `openHAB`; a subsequent server request completed in 157 ms. | This is an HTTP-server probe, not an embedded WebView2 load or a navigation-time measurement. |
| Main UI page discovery | Local `http://127.0.0.1:18082/rest/ui/components/ui:page`; unauthenticated | `HTTP 200`, `application/json`, body `[]`; an empty component collection is handled by the existing discovery pipeline. | The default disposable server had no configured/promoted or file-backed pages. |
| Lower-layer Main UI and shell contracts | `OpenHab.App.Tests`; no server credentials | Passed: `66/66` using `dotnet test tests\\OpenHab.App.Tests\\OpenHab.App.Tests.csproj --no-restore --filter "FullyQualifiedName~MainUi|FullyQualifiedName~MainWindowShellController|FullyQualifiedName~MainWindowShellAnimationPlanner" --logger "console;verbosity=minimal" -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`. The covered contracts include URL sanitization/same-origin rules, promoted-page discovery/planning, route synchronization, and sitemap-pane preservation during Main UI page selection. | These unit tests do not instantiate the WinUI/WebView2 control. |

### Host review

`MainUiWebViewHost` uses the existing generic same-origin request/auth handling, route tracking, retry surface, and `window.open` policy: same-origin new windows stay in the host; external `http`/`https` URLs are delegated to the system browser; unsupported schemes are rejected. `MainWindow` maintains shell route synchronization and retains the native sitemap pane while Main UI is selected. No route-specific Chat, log, voice, persistence, settings, YAML, or file-backed-page code was found or added.

No production change is justified: no embedded-host failure was reproduced, and the server-side additions are intended to remain normal same-origin Main UI content.

### Pending manual evidence — not claimed

- No packaged/tray or WinUI app was launched, and no WebView2 control was instantiated. The in-app browser surface was unavailable in this session; a normal-browser check would not establish embedded-host behaviour.
- 5.1.4 Main UI, 5.2.0 myopenHAB, Basic authentication, API-token authentication, invalid-credential retry, reverse-proxy path-prefix navigation, managed/file-backed/read-only pages, Chat, logs, voice permission, popup policy, external-browser handoff, back navigation, hide/show route coherence, Main UI/native-sitemap live coexistence, token/session lifecycle, and profile-switch cookie isolation remain manual validation items.
- No diagnostics, screenshot, or recording is attached because no embedded app session occurred. The disposable server had no credentials, cloud endpoint, pages, Chat/log/voice configuration, or sitemap configuration.
