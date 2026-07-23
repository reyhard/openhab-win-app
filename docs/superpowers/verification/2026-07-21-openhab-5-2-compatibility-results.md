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
