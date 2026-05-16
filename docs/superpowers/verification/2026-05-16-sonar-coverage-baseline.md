# Sonar Coverage Baseline - 2026-05-16

## Sonar Project

- Query timestamp: `2026-05-16 09:57:29 +02:00`
- Branch: `main`
- Commit: `c23361e`
- Project key: `reyhard_openhab-win-app`
- Coverage: `31.6%`
- Line coverage: `32.2%`
- Branch coverage: `30.1%`
- Lines to cover: `13,576`
- Uncovered lines: `9,204`
- Conditions to cover: `6,182`
- Uncovered conditions: `4,320`
- NCLOC: `22,053`

Note: the plan-time expected snapshot recorded `31.3%` coverage, `31.8%` line coverage, `30.1%` branch coverage, `13,728` lines to cover, `9,360` uncovered lines, `6,240` conditions to cover, and `4,363` uncovered conditions. The live Sonar query differs slightly, so this file records the live measures.

## Local OpenCover Shape

| Module | Approximate line coverage |
| --- | ---: |
| `OpenHab.Sitemaps` | `89.9%` |
| `OpenHab.Rendering` | `87.8%` |
| `OpenHab.App` | `83.3%` |
| `OpenHab.Core` | `77.7%` |
| `OpenHab.Windows.Notifications` | `62.9%` |
| `openHAB` tray executable | `2.0%` |

- Run context: `dotnet test` on `tests/OpenHab.Core.Tests/OpenHab.Core.Tests.csproj`, `tests/OpenHab.Sitemaps.Tests/OpenHab.Sitemaps.Tests.csproj`, `tests/OpenHab.Rendering.Tests/OpenHab.Rendering.Tests.csproj`, and `tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj` with `--configuration Release --no-restore --collect "XPlat Code Coverage" --results-directory TestResults -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover`.
- Output context: attachments were written under `TestResults/*/coverage.opencover.xml` while the working tree was on branch `main` at commit `c23361e`.

## Interpretation

The live Sonar measures and the local OpenCover reports both show the same shape: shared libraries are high coverage, while the tray executable remains the low-coverage outlier in the denominator.

## Coverage Policy Evidence

No coverage policy change is recorded in this baseline artifact. This file records measurement evidence only.

## Post-Implementation Verification

- Local commit under verification: `eb46c56`.
- Sonar MCP snapshot after local commits, before a new remote analysis: coverage `31.6%`, line coverage `32.2%`, branch coverage `30.1%`, lines to cover `13,576`, uncovered lines `9,204`, conditions to cover `6,182`, uncovered conditions `4,320`, NCLOC `22,053`.
- Quality gate snapshot after local commits, before a new remote analysis: `ERROR` due to `new_coverage` `29.6%` below the `80%` threshold; reliability, security, maintainability, duplication, and hotspot conditions were OK.
- Local direct tests: `OpenHab.Sitemaps.Tests` passed with 39 tests; `OpenHab.Rendering.Tests` and `OpenHab.App.Tests` commands exited `0`; `OpenHab.Core.Tests` was blocked by `NU1301` socket access denial to `https://api.nuget.org/v3/index.json` even when retried with elevated sandbox permissions.
- Local tray Release build: blocked by the same `NU1301` socket access denial to `https://api.nuget.org/v3/index.json` even when retried with elevated sandbox permissions.
- Local coverage report regeneration was not completed because the Core test project could not run in this environment without NuGet access.
