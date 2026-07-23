# openHAB 5.2 Compatibility Verification

Date: 2026-07-23
Compatibility commit: `3607006f4403d12d3b37f804bc20e575d09dd7d1`

This record distinguishes automated contract coverage from live and manual certification. It does not authorize a release.

## Automated results

Environment: Windows `10.0.26200` (win-x64), .NET SDK `10.0.204` (`e7aa4b537d`). Fresh checks were serialized with `-m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`.

The direct tests, coverage, Release build, and formatter checks below were run at `ebee350cf12eab68d82f347e660843c3d16ebf9d`; the current compatibility head differs only by the offline probe-helper policy commit `3607006f4403d12d3b37f804bc20e575d09dd7d1`. The controlled fake integration was rerun fresh at `3607006`.

| Project | Result | Test duration | Command elapsed |
| --- | --- | ---: | ---: |
| `OpenHab.Core.Tests` | 138 passed, 0 failed, 0 skipped | 934 ms | 14.015 s |
| `OpenHab.Sitemaps.Tests` | 50 passed, 0 failed, 0 skipped | 76 ms | 5.739 s |
| `OpenHab.Rendering.Tests` | 129 passed, 0 failed, 0 skipped | 87 ms | 8.796 s |
| `OpenHab.App.Tests` | 660 passed, 0 failed, 0 skipped | 1 s | 57.448 s |

Total: `977` passed, `0` failed, `0` skipped. The supplied Task 1 baseline at `7a5bdb07ab2966866fb070d6e261472c2db4d19d` did not retain per-command durations; none are inferred here.

Fresh coverage runs used `coverage.runsettings` without changing thresholds or exclusions. The generated OpenCover reports contain `OpenHabSitemapJsonParser`, `SitemapEventParser`, `OpenHabEventStreamClient`, `OpenHabHttpClient`, and `SitemapRuntimeController`.

`dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false` passed in 46.511 s with 0 warnings and 0 errors.

## Fixture results

Date: 2026-07-23. Capture commit: `0d7ae6606e73997c71882aca17ed7abb553a963b`.

Sanitized genuine captures were made from disposable official openHAB `5.1.4` and `5.2.0` containers, then the containers were removed. Both fixture sets parse through the automated tests. The 5.1.4 capture preserves the legacy ButtonGrid representation; the 5.2.0 capture preserves nested Button widgets, empty arrays, and opaque variable-width widget identifiers. SSE fixtures cover real `event:`/`data:` framing. Fixture payloads and test reports are sanitized; no personal endpoint was accessed.

The client/SSE/runtime tests also cover legacy and standard subscription locations, Location-header precedence, valid SSE `data:` spacing, authenticated request construction, valid `200`/`202`/`204` success statuses, exact context-aware event matching, and non-verbose server-payload diagnostics.

## Live 5.1.4 results

Date: 2026-07-23. The disposable default 5.1.4 image was contacted only on loopback. The repeatable probe reached REST but safely stopped at `sitemap-list` because no synthetic sitemap or dedicated writable Item was configured. It is not a successful live/authenticated certification result.

## Live 5.2.0 results

Date: 2026-07-23. The disposable default 5.2.0 image was contacted only on loopback and likewise safely stopped at `sitemap-list`. A separate disposable root HTTP check returned `200 text/html`, and Main UI page discovery returned `200 application/json` with `[]`; these are server HTTP observations, not a WebView2 or authenticated certification result.

## Main UI manual smoke

Date: 2026-07-23. Pending. No packaged/tray/WinUI app or embedded WebView2 control was launched. Lower-layer Main UI/shell tests passed `66/66` earlier in the compatibility work, and source inspection found generic same-origin hosting without route-specific additions. Promoted managed pages, file-backed/read-only pages, Chat/log/voice routes, authentication/retry, session/profile isolation, navigation, and sitemap coexistence have not been manually certified.

Server-provided Main UI features are not native Windows-app features. In particular, this result does not claim a native Chat, MCP, voice, logs, persistence, or editing implementation.

## Authentication matrix

Date: 2026-07-23. API-token and Basic authentication have automated REST and SSE construction/contract coverage. No live authenticated endpoint was used.

| Matrix cell | Live result |
| --- | --- |
| openHAB 5.1.4 local + API token | Pending |
| openHAB 5.2.0 local + API token | Pending |
| openHAB 5.2.0 local + Basic authentication | Pending |
| openHAB 5.2.0 through myopenHAB | Pending |

The repeatable probe requires an explicit endpoint and never commands an Item unless `-WritableItemName` is supplied. Use only a dedicated reversible test Item; the script retains the original state in memory and restores it in `finally`. A restoration failure is a failed probe. Probe reports redact credentials, authorization headers, raw payloads, states, and exception stacks.

Probe checks on this commit: both PowerShell scripts parsed without syntax errors, `ftp://invalid` returned exit code `2`, and the controlled loopback fake integration passed freshly in 30.614 seconds. The fake suite covers successful write/restore, forced restoration failure, sitemap-list shape rejection, version preflight, Bearer and Basic request headers without report leakage, and bounded stalled reads. It is a controlled test double, not a live server certification result.

## Packaging result

Date: 2026-07-23. Package gate: not required and not run. This task changed documentation only; it did not change package, manifest, dependency, startup, notification activation, identity, signing, or version files. No version bump, release metadata, package identity, or signing change was made.

## Formatting and repository checks

Date: 2026-07-23. `git diff --check` passed (52 ms) before documentation edits. Repository-wide `dotnet format OpenHab.Windows.sln --no-restore --verify-no-changes` exited `2` after 28.773 s because of existing whitespace debt in `SitemapUiLogic.cs`, `ShortcutActionExecutor.cs`, `RadialCommandMenuWindow.cs`, and `ShortcutRecorderControl.cs`. These files are outside the compatibility change set. The compatibility-changed C# files passed scoped format verification (24.311 s). Documentation changes add no C# formatting scope.

## Known limitations

- The authenticated local/API-token/Basic, myopenHAB, and full live sitemap probe matrix is pending.
- The embedded WebView2/manual Main UI matrix is pending; default disposable HTTP checks and lower-layer tests are not substitutes.
- Controlled fake-probe coverage does not substitute for a configured live server matrix.
- Product-wide manual UI, performance, accessibility, package ownership/signing/distribution, and notification resend smoke gates listed in the current-state document remain open.
- The fake probe’s Location-precedence path accepts either subscription id in its test assertion, although implementation unit/spec review verified the production client chooses the HTTP Location header first.

## Release recommendation

- [ ] Ready
- [ ] Ready with documented limitations
- [x] Not ready

The automated compatibility contract evidence is green, but mandatory live/authentication/myopenHAB and embedded-WebView/manual certification are incomplete. Existing product release blockers also remain. Do not publish a compatibility release or change release metadata until these gates have recorded evidence.
