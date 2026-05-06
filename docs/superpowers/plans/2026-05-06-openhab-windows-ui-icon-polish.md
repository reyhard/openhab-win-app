# openHAB Windows UI Icon + Toggle Polish Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers-extended-cc:subagent-driven-development (if subagents available) or superpowers-extended-cc:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove redundant on/off text from toggle rows, restore dynamic icons across all sitemap row types, make Windows 11 icon replacement preserve dynamic/openHAB icon behavior, and polish the flyout/main window UI so it better matches the Windows 11 spec and mockup.

**Architecture:** Keep the existing layered flow intact: parse and normalize icon/state data in `OpenHab.Sitemaps`, convert widgets into neutral row descriptors in `OpenHab.Rendering`, and keep all WinUI-specific icon selection, row composition, and styling in `OpenHab.Windows.Tray`. Implement a shared icon-host path in the WinUI factory so toggle/text/navigation/selection rows all use the same icon rules and safe Win11 fallback behavior.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), xUnit, System.Text.Json

---

## File Structure Map

| File | Current Role | Planned Change |
|---|---|---|
| `src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs` | Parses sitemap widget JSON including `icon` and item state | Verify no gap in icon parsing assumptions; document any dynamic icon constraints in tests/comments if needed |
| `src/OpenHab.Sitemaps/Models/SitemapModels.cs` | Carries widget icon/state through parsed and normalized models | Keep model contract stable; verify it can support all row types without special-casing toggles |
| `src/OpenHab.Sitemaps/Runtime/SitemapNormalizer.cs` | Preserves icon/state into normalized widgets | Confirm icon propagation remains intact |
| `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs` | Neutral row descriptor contract (`IconName`, `State`, `RawState`, `Control`, etc.) | Refine descriptor expectations if needed for display-state vs raw-state separation |
| `src/OpenHab.Rendering/Skins/SitemapRowMapper.cs` | Maps normalized widgets to row descriptors | Preserve display-state transforms while keeping raw state usable for toggles and commands |
| `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs` | Builds WinUI controls for all row types; currently owns Win11 icon map and row layouts | Main implementation area: shared icon host, toggle layout cleanup, Win11 icon fallback logic, row visual consistency |
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml` | Flyout shell layout | Adjust overall spacing/padding/footer polish to better match mockup |
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` | Rebuilds flyout row list from descriptors | Keep rendering wiring intact while validating icon mode behavior |
| `src/OpenHab.Windows.Tray/MainWindow.xaml` | Main window shell layout and settings surface | Align spacing and row presentation with flyout polish where appropriate |
| `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` | Main window row rendering and settings interactions | Keep `UseWindows11Icons` wiring intact after factory changes |
| `src/OpenHab.App/Settings/AppSettings.cs` | App settings model | No behavioral change expected unless naming/docs need cleanup |
| `src/OpenHab.App/Settings/AppSettingsController.cs` | Persists and updates settings | Keep `UseWindows11Icons` flow unchanged unless refactor reveals a bug |
| `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs` | Rendering contract coverage | Extend with icon/row layout behavior tests where possible |
| `tests/OpenHab.Rendering.Tests/SitemapStateTransformTests.cs` | State transform coverage | Extend to prove toggle cleanup does not break state transform expectations |

---

## Implementation Notes

- The current redundant `Wyłączone/Włączone` text comes from `SitemapControlFactory.CreateToggle()` rendering both a `TextBlock` for `row.State` and a `ToggleSwitch`.
- The current missing-icon issue is partly structural: toggle rows do not use the same icon layout path as text/navigation rows.
- `UseWindows11Icons` currently relies on a static exact-match dictionary in `SitemapControlFactory.ResolveWin11Icon()`; this is too fragile for dynamic/openHAB icon names and must fall back safely.
- The spec and mockup point toward a more native Windows 11 list feel: consistent icon gutter, lighter hierarchy, better spacing, fewer obvious debug-layout artifacts, and less duplicate text.

---

### Task 1: Lock down the icon contract before changing UI composition

**Files:**
- Read/Modify: `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`
- Read/Modify: `src/OpenHab.Rendering/Skins/SitemapRowMapper.cs`
- Test: `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs`

- [ ] **Step 1: Inspect the descriptor contract and document intended icon behavior**

Confirm the implementation contract for row icons:
- `UseWindows11Icons = false` → prefer openHAB/server icon rendering.
- `UseWindows11Icons = true` → try Win11 semantic glyph replacement first, then fall back to openHAB/server icon if replacement is missing or ambiguous.

- [ ] **Step 2: Verify state/display separation in row mapping**

Confirm `SitemapRowMapper` preserves:
- `RawState` for command logic,
- transformed `State` for display where appropriate.

Do **not** move WinUI presentation concerns into the mapper.

- [ ] **Step 3: Add a mapper/descriptor regression test if the contract is unclear today**

Example test shape:

```csharp
[Fact]
public void ToRow_PreservesIconAndRawState_ForSwitchWidgets()
{
    var widget = new NormalizedSitemapWidget(
        SitemapWidgetType.Switch,
        "Kitchen",
        "OFF",
        "light",
        [],
        [],
        null,
        "Kitchen_Light");

    var row = SitemapRowMapper.ToRow(widget, RenderDensity.Comfortable);

    Assert.Equal("light", row.IconName);
    Assert.Equal("OFF", row.RawState);
}
```

- [ ] **Step 4: Run targeted rendering tests**

Run: `dotnet test OpenHab.Windows.sln --filter Sitemap`

Expected: existing rendering/sitemap tests pass; any new contract test passes.

---

### Task 2: Extract a shared icon-host path for all row types

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Test: `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs`

- [ ] **Step 1: Identify the current icon rendering logic in `CreateRow`**

Capture the existing responsibilities:
- determine whether an icon is present,
- pick Win11 glyph vs server image,
- reserve consistent left gutter width,
- align icon vertically with row content.

- [ ] **Step 2: Extract a helper for icon resolution and a helper for icon host creation**

Target helper split:

```csharp
private static FrameworkElement? CreateIconElement(string? iconName, Uri? baseUri, bool useWindowsIcons)
private static bool TryResolveWin11Glyph(string? iconName, out string glyph)
private static string? NormalizeIconKey(string? iconName)
```

Keep server-image fallback inside the factory, not the mapper.

- [ ] **Step 3: Extract a common row-shell helper**

Target shape:

```csharp
private static Grid CreateRowShell(bool hasIcon, bool hasTrailingControl)
```

The shell should standardize:
- icon column width,
- content column,
- trailing control/chevron column,
- consistent vertical alignment and spacing.

- [ ] **Step 4: Update existing text/navigation rows to use the shared helper**

Do this before touching toggles so the pattern is stable.

- [ ] **Step 5: Run build verification**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`

Expected: 0 errors.

---

### Task 3: Remove redundant toggle state text and move toggle rows onto the shared row shell

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

- [ ] **Step 1: Rewrite `CreateToggle()` to use the shared row shell**

Target composition:
- left icon,
- main label content,
- right `ToggleSwitch`.

Remove the explicit state `TextBlock` entirely.

- [ ] **Step 2: Keep toggle command logic based on raw state, not display text**

Do not infer toggle state from translated strings like `Wyłączone`.

- [ ] **Step 3: Ensure `ToggleSwitch.IsOn` still uses a reliable state source**

Prefer the raw source if available; otherwise keep current `ON`/`OFF` semantics explicit and localized-display-independent.

- [ ] **Step 4: Confirm toggle rows still wrap long labels properly**

Match the text behavior used elsewhere:
- `TextWrapping = WrapWholeWords`
- `TextTrimming = CharacterEllipsis`
- `MaxLines = 2`

- [ ] **Step 5: Run full rendering tests**

Run: `dotnet test OpenHab.Windows.sln --configuration Release --filter OpenHab.Rendering.Tests`

Expected: rendering tests pass.

---

### Task 4: Make Windows 11 icon replacement dynamic-aware and safe

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Test: `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs`

- [ ] **Step 1: Normalize icon names before dictionary lookup**

Add a normalization path that can safely improve matching for variants like:
- plural/synonym forms already present,
- prefix/suffix noise,
- common category variants.

Do **not** over-normalize in a way that destroys meaningful state-specific icon names.

- [ ] **Step 2: Change Win11 icon replacement to "try semantic glyph, else fallback"**

Rules:
- If the normalized icon key matches a known semantic icon, show Win11 glyph.
- If not, render the openHAB/server icon.
- Never return “no icon” solely because Win11 mapping failed.

- [ ] **Step 3: Ensure all row types use the same icon fallback rules**

Apply to:
- text rows,
- navigation rows,
- toggle rows,
- selection rows,
- slider rows,
- fallback rows where appropriate.

- [ ] **Step 4: Add targeted fallback tests**

Example scenarios to cover in tests:
- known icon name + Win11 mode on → glyph used,
- unknown icon name + Win11 mode on + base URI present → server icon path used,
- icon present + Win11 mode off → server icon path used.

- [ ] **Step 5: Run build + tests**

Run:
- `dotnet build OpenHab.Windows.sln --configuration Release`
- `dotnet test OpenHab.Windows.sln --configuration Release`

Expected: both pass.

---

### Task 5: Bring selection and slider rows onto the same visual rhythm

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

- [ ] **Step 1: Review slider row layout against the mockup/spec**

Current slider rows stack a label above the slider. Verify whether that should remain stacked or gain an icon/title shell with more consistent spacing.

- [ ] **Step 2: Review selection row layout against the mockup/spec**

Ensure label + combobox rows do not visually break the list.

- [ ] **Step 3: Apply the shared icon gutter and row spacing to slider/selection rows**

Goal: they should feel like members of the same list, not separate ad-hoc controls.

- [ ] **Step 4: Verify interactive behavior remains intact**

Check that:
- combo selection still sends commands,
- slider changes still send commands.

- [ ] **Step 5: Run targeted app/runtime tests if needed**

Run: `dotnet test OpenHab.Windows.sln --filter OpenHab.App.Tests`

Expected: app/runtime tests pass.

---

### Task 6: Polish flyout shell spacing and footer treatment to better match the mockup

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`

- [ ] **Step 1: Compare current flyout shell to the mockup and spec**

Focus on:
- outer margin,
- title/status spacing,
- list breathing room,
- footer button density,
- border/card treatment consistency.

- [ ] **Step 2: Adjust flyout shell spacing and list rhythm**

Likely changes:
- slightly more intentional top/header spacing,
- row spacing tuned to look less cramped,
- cleaner separation between list body and footer actions.

- [ ] **Step 3: Adjust main window list container to mirror the improved row rhythm**

Keep the main window visually aligned with the flyout without copying every shell detail.

- [ ] **Step 4: Avoid introducing broad style-system refactors**

This task is polish, not a full resource-dictionary redesign.

- [ ] **Step 5: Run build verification**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`

Expected: 0 errors.

---

### Task 7: Add focused regression coverage for the bugs fixed in this slice

**Files:**
- Modify/Create: `tests/OpenHab.Rendering.Tests/SitemapSkinTests.cs`
- Modify/Create: `tests/OpenHab.Rendering.Tests/SitemapStateTransformTests.cs`

- [ ] **Step 1: Add coverage for icon propagation to switch/toggle rows**

Ensure switch widgets keep their `IconName` and state fields through mapping.

- [ ] **Step 2: Add coverage for display-state transform vs raw-state behavior**

Target: display labels can be transformed without breaking command logic.

- [ ] **Step 3: Add coverage for Win11 icon fallback semantics**

At minimum verify behavior contracts, even if exact WinUI visual-tree assertions are not practical in unit tests.

- [ ] **Step 4: Run the full solution test suite**

Run: `dotnet test OpenHab.Windows.sln --configuration Release`

Expected: all tests pass.

---

## Suggested Commit Boundaries

1. `refactor: unify sitemap row icon hosting`
2. `fix: remove redundant toggle state text`
3. `fix: preserve fallback icons when Win11 icon mapping misses`
4. `style: polish sitemap row spacing and shell layout`
5. `test: cover icon fallback and toggle row behavior`

---

## Verification Checklist

- [ ] Toggle rows no longer show redundant `Włączone/Wyłączone` state text.
- [ ] Toggle rows still show the correct on/off switch position.
- [ ] Toggle rows can display icons.
- [ ] Text/navigation rows keep icons.
- [ ] Selection/slider rows visually align with the same row rhythm.
- [ ] `UseWindows11Icons = true` uses glyphs only when mapping succeeds.
- [ ] `UseWindows11Icons = true` falls back to openHAB/server icons when mapping does not succeed.
- [ ] Unknown icon names no longer disappear just because Win11 mode is enabled.
- [ ] Flyout spacing and footer treatment look closer to the mockup.
- [ ] `dotnet build OpenHab.Windows.sln --configuration Release` passes.
- [ ] `dotnet test OpenHab.Windows.sln --configuration Release` passes.

---

## Out of Scope

- Redesigning the entire flyout into the full dashboard composition shown in the marketing-style mockup.
- Adding new openHAB API surface or changing sitemap parsing semantics beyond what is needed for icon correctness.
- Event-stream/live-update architecture changes.
- WebView fallback work.
- Broader theming/resource-dictionary redesign.

---

## Execution Recommendation

Implement Tasks 1–4 first as a focused rendering slice, verify behavior, then do Tasks 5–7 as UI polish + regression hardening. The rendering factory (`SitemapControlFactory.cs`) is the highest-leverage file and should be stabilized before touching shell spacing.
