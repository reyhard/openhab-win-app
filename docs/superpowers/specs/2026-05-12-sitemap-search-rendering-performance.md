# Sitemap Search Rendering Performance

Date: 2026-05-12

## Context

Sitemap search now debounces typed input and builds search descriptors asynchronously, but a visible freeze can still happen when a search result snapshot causes the flyout to rebuild or reconcile many WinUI row controls. The next performance work should verify whether the bottleneck is search traversal, row planning, row control creation, layout, icon loading, or structural reconciliation before choosing a larger UI change.

Likely hot paths:

- `FlyoutWindow.RefreshRuntimeBindings`
- `SitemapSurfaceRenderer.Refresh`
- `SitemapSurfaceRenderer.ReconcileStructuralRows`
- `SitemapControlFactory.Create`
- `SitemapRowPlanner.BuildVisualRows`
- `SitemapSearchDescriptorBuilder.Build`

## Profiling Workflow

Use Visual Studio Performance Profiler before committing to a rendering strategy.

Recommended run:

1. Start the app in Release configuration when practical.
2. Open `Debug` > `Performance Profiler...`.
3. Select `CPU Usage`.
4. Also select `UI Responsiveness` if available for the project/runtime combination.
5. Start profiling.
6. Open the flyout and type a search query that returns many rows and reproduces the freeze.
7. Stop collection after the freeze is visible.
8. Inspect the UI thread and expand the call tree around the likely hot paths above.

Interpretation:

- If `SitemapSearchDescriptorBuilder.Build` dominates, optimize search indexing or query matching.
- If `SitemapControlFactory.Create` dominates, reduce row control creation or defer expensive controls.
- If `StackPanel.Children` operations, `ReconcileStructuralRows`, measure/arrange, or layout dominate, optimize rendering with chunking or virtualization.
- If icon/image/chart loading dominates, defer or cache media-heavy row content.

## Option 1: Chunked Rendering

Chunked rendering keeps the current `StackPanel`-based renderer but splits large surface updates over multiple dispatcher turns. The renderer would create an initial small batch immediately, yield to the UI thread, then continue adding or reconciling rows in batches.

Design outline:

- Add a render generation id to `FlyoutWindow` or `SitemapSurfaceRenderer`.
- Increment the generation for every new sitemap snapshot.
- For large result sets, render the first batch synchronously, for example 20 to 40 visual rows.
- Schedule later batches through `DispatcherQueue.TryEnqueue`.
- Before each batch, compare the captured generation with the latest generation and stop if stale.
- Prefer enabling this first for search descriptors or visual row counts over a threshold.
- Keep normal small-page rendering synchronous to avoid unnecessary complexity.

Benefits:

- Smallest architectural change.
- Preserves the existing row factory, action wiring, search metadata, and rendering tests.
- Directly reduces long UI-thread stalls from many row creations.

Tradeoffs:

- All rows are still eventually created, so memory and total work do not improve as much as virtualization.
- Results may visibly fill in progressively.
- Requires careful stale-batch cancellation when the user types another search query.

Suggested first implementation:

- Apply chunking only when `snapshot.IsSearchActive` and visual row count is greater than 40.
- Render the first 30 rows immediately.
- Render subsequent batches of 30 rows per dispatcher tick.
- Cancel stale batches with a generation id.

## Option 2: Virtualized Rendering

Virtualization replaces the manual `StackPanel.Children` rendering model with an items control that only realizes visible rows. This is the stronger long-term fix for very large sitemaps or search result pages.

Design outline:

- Replace the row host with a virtualizing control such as `ListView` or an `ItemsRepeater`-style surface.
- Represent each visual row as a bindable item or view model.
- Convert `SitemapControlFactory` output into a reusable row control or item template strategy.
- Preserve row identity keys so commands, navigation, and search result source metadata still resolve correctly.
- Preserve complex controls such as toggles, sliders, button grids, charts, images, webviews, and fallback rows.
- Handle variable-height rows carefully because charts, images, and webviews can reduce virtualization efficiency.

Benefits:

- Best long-term scalability.
- Offscreen rows are not created, measured, arranged, or kept alive.
- Large search results should remain responsive even with many matches.

Tradeoffs:

- Larger refactor.
- Higher regression risk around row actions, visual polish, animations, focus, and mixed row heights.
- May require reworking existing partial update and structural reconcile logic.

Suggested approach:

- Prototype virtualization behind a separate renderer implementation rather than rewriting the current renderer in place.
- Start with read-only/text/toggle rows before enabling complex media and chart rows.
- Compare profiler traces against the current renderer before switching the flyout by default.

## Recommendation

Use profiling first. If the profiler confirms row creation or layout dominates, implement chunked rendering before virtualization. Chunking fits the current renderer and should reduce visible freezes with much less risk. Virtualization should remain the long-term path if chunking is insufficient for very large result sets or if profiler traces show sustained layout and memory pressure from realized offscreen rows.
