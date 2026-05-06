# openHAB Windows UI Bugfixes Plan

> **For agentic workers:** REQUIRED: Use superpowers-extended-cc:subagent-driven-development (if subagents available) or superpowers-extended-cc:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 5 critical UI bugs: toggle layout (label left/toggle right), subpage navigation errors, icons only working for custom icons, sitemap not selectable, and the UseWindows11Icons setting having no effect.

**Architecture:** Fixes span `OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs` (layout + icons), `OpenHab.App/Runtime/SitemapRuntimeController.cs` (navigation), and `OpenHab.Core.Api/OpenHabHttpClient.cs` (sitemap page URLs). All changes are surgical — no refactoring.

**Tech Stack:** .NET 10, WinUI 3, System.Text.Json

---

### Task 1: Fix Toggle Layout — Label Left, Toggle Right

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs:59-73`

- [ ] **Step 1: Write failing test**

No unit test exists for the factory's visual layout. We'll rely on the existing build/tests as regression.

- [ ] **Step 2: Rewrite CreateToggle to use horizontal layout**

Replace the `ToggleSwitch` with a horizontal `Grid` containing label on left and toggle on right:

```csharp
private static FrameworkElement CreateToggle(SitemapRowDescriptor row, Func<Task>? activateRow)
{
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

    var labelBlock = new TextBlock
    {
        Text = row.Label,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.WrapWholeWords,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 2
    };
    Grid.SetColumn(labelBlock, 0);
    grid.Children.Add(labelBlock);

    var toggle = new ToggleSwitch
    {
        IsOn = string.Equals(row.State, "ON", StringComparison.OrdinalIgnoreCase)
    };
    toggle.MinWidth = 0; // Prevent stretch
    Grid.SetColumn(toggle, 1);
    grid.Children.Add(toggle);

    if (row.Action == RenderActionKind.SendCommand && activateRow is not null)
    {
        toggle.Toggled += async (_, _) => await activateRow();
    }

    return grid;
}
```

This matches the design: `[icon] [label - stretch] [ToggleSwitch - right]`.

- [ ] **Step 3: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs
git commit -m "fix: toggle layout - label on left, ToggleSwitch on right horizontally"
```

---

### Task 2: Fix Navigation — Revert to Embedded Child Page

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs:124-146`

**Root cause:** `NavigateToChildAsync` calls `GetSitemapJsonAsync(childPage.Id)` which builds URL `rest/sitemaps/{pageId}` — treating the page ID as a sitemap name. The correct URL would be `rest/sitemaps/{sitemapName}/{pageId}`, but we don't track the current sitemap name. The embedded `widget.Children[0]` already contains the full child page data (all widgets, etc.) — openHAB returns linked pages inline in the sitemap JSON.

- [ ] **Step 1: Revert to using embedded child page**

Replace lines 130-136 with a simple normalize of the embedded child:

```csharp
var childPage = widget.Children[0];
var normalized = SitemapNormalizer.Normalize(childPage);
currentPage = normalized;

var descriptor = renderController.BuildCurrentDescriptor(normalized);
Current = Current with
{
    Descriptor = descriptor,
    StatusText = $"Navigated to: {normalized.Label}"
};
return true;
```

Remove the HTTP client construction and the server fetch.

- [ ] **Step 2: Check for unused `using` imports**

After removing the server fetch, `SelectPrimaryTransport` call is removed. Check if `using OpenHab.Core.Profiles;` is still needed (it is, used by `SendCommandForRowAsync` and `ActivateRowAsync`). All good.

- [ ] **Step 3: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add src/OpenHab.App/Runtime/SitemapRuntimeController.cs
git commit -m "fix: navigation uses embedded child page data instead of broken server URL"
```

---

### Task 2b: Make Title Clickable to Open Sitemap Selector

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`

- [ ] **Step 1: Add Tapped handler to TitleText in XAML**

```xml
<TextBlock x:Name="TitleText"
           FontSize="18"
           FontWeight="SemiBold"
           Text="openHAB"
           IsTapEnabled="True"
           Tapped="TitleText_Tapped" />
```

- [ ] **Step 2: Add handler in code-behind**

```csharp
private void TitleText_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
{
    SitemapCombo.IsDropDownOpen = true;
}
```

- [ ] **Step 3: Commit**

```bash
git add src/OpenHab.Windows.Tray/FlyoutWindow.xaml src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs
git commit -m "feat: clicking flyout title opens sitemap selection dropdown"
```

---

### Task 3: Wire UseWindows11Icons Setting

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs` (optional — pass setting through)
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs:114-118` (pass setting to factory)
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` (same)

**Root cause:** `UseWindows11Icons` exists in `AppSettings` but is never read by any rendering code. When enabled, common openHAB icon names should map to Segoe Fluent glyphs instead of HTTP-fetched PNG images.

- [ ] **Step 1: Create icon mapping in SitemapControlFactory**

Add a static mapping dictionary and a method to resolve icons:

```csharp
// In SitemapControlFactory class:
private static readonly Dictionary<string, string> Win11IconMap = new(StringComparer.OrdinalIgnoreCase)
{
    // Common openHAB item categories → Segoe Fluent glyphs
    ["light"] = "\uE706",        // Lightbulb
    ["switch"] = "\uE8A3",       // Toggle
    ["rollershutter"] = "\uE7A0", // Up/Down arrows (or use \uE76F)
    ["heating"] = "\uE7B2",      // Temperature
    ["temperature"] = "\uE7B2",  // Temperature
    ["humidity"] = "\uE7A6",     // Humidity/Droplet
    ["contact"] = "\uE8E1",      // Door/Contact
    ["motion"] = "\uE7A6",       // Motion (running man)
    ["alarm"] = "\uE7BA",        // Alert
    ["battery"] = "\uEBA0",      // Battery
    ["energy"] = "\uE994",       // Plug/Energy
    ["power"] = "\uE994",        // Power
    ["lock"] = "\uE72E",         // Lock
    ["door"] = "\uE8E1",         // Door
    ["window"] = "\uE8E1",       // Window (same as door)
    ["garagedoor"] = "\uE8E1",   // Garage door
    ["blinds"] = "\uE7A0",       // Blinds
    ["dimmer"] = "\uE706",       // Dimmer (lightbulb)
    ["colorpicker"] = "\uE790",  // Color palette
    ["speaker"] = "\uE7F5",      // Speaker
    ["tv"] = "\uE7F4",           // Screen
    ["receiver"] = "\uE7F5",     // Audio receiver
    ["network"] = "\uE701",      // WiFi
    ["presence"] = "\uE716",     // Person
    ["smoke"] = "\uE7BA",        // Alert/smoke
    ["siren"] = "\uE995",        // Siren
    ["camera"] = "\uE722",       // Camera
    ["fan"] = "\uE785",          // Fan
    ["pump"] = "\uE785",         // Pump
    ["water"] = "\uE7A6",        // Water
    ["gas"] = "\uE7A6",          // Gas
    ["co2"] = "\uE769",          // Air quality
    ["pressure"] = "\uE7A6",     // Pressure
    ["rain"] = "\uE7A6",         // Rain
    ["wind"] = "\uE7A6",         // Wind
    ["sun"] = "\uE706",          // Sun/brightness
    ["quality"] = "\uE769",      // Quality/chart
};

private static FontIcon? ResolveWin11Icon(string? iconName)
{
    if (string.IsNullOrWhiteSpace(iconName)) return null;
    if (Win11IconMap.TryGetValue(iconName, out var glyph))
    {
        return new FontIcon { Glyph = glyph, FontSize = 16, Opacity = 0.8 };
    }
    return null;
}
```

- [ ] **Step 2: Modify Create method to accept icon mode flag**

Add `bool useWindowsIcons = false` parameter to `Create()`:

```csharp
public static FrameworkElement Create(
    SitemapRowDescriptor row, 
    Func<Task>? activateRow, 
    Func<string, Task>? sendCommand = null, 
    Uri? baseUri = null,
    bool useWindowsIcons = false)
```

- [ ] **Step 3: Modify CreateRow to use Windows 11 icons when enabled**

In `CreateRow`, when `useWindowsIcons` is true and `iconName` is not null:
- Try to resolve a FontIcon via `ResolveWin11Icon`
- If resolved, use the FontIcon instead of the server-fetched Image
- If not resolved, fall back to the server Image

```csharp
private static Grid CreateRow(string label, string state, Uri? baseUri = null, string? iconName = null, bool useWindowsIcons = false)
{
    var hasIcon = iconName is not null && (baseUri is not null || useWindowsIcons);
    // ...
    
    if (hasIcon)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        
        FontIcon? winIcon = null;
        if (useWindowsIcons)
        {
            winIcon = ResolveWin11Icon(iconName);
        }
        
        if (winIcon is not null)
        {
            winIcon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(winIcon, 0);
            grid.Children.Add(winIcon);
        }
        else if (baseUri is not null)
        {
            var image = new Image { Source = ... };
            // existing image loading code
        }
    }
}
```

- [ ] **Step 4: Pass useWindowsIcons from FlyoutWindow and MainWindow**

In `FlyoutWindow.xaml.cs:114-118`:
```csharp
SitemapRows.Children.Add(SitemapControlFactory.Create(
    row, activateRow, sendCommand,
    settingsController.Current.LocalEndpoint,
    settingsController.Current.UseWindows11Icons));
```

Same for MainWindow.

- [ ] **Step 5: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors, 0 warnings

Run: `dotnet test OpenHab.Windows.sln --configuration Release`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.Windows.Tray/MainWindow.xaml.cs
git commit -m "feat: wire UseWindows11Icons setting with Segoe Fluent glyph mapping"
```

---

### Task 4: Final Build + Test Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 2: Full solution tests**

Run: `dotnet test OpenHab.Windows.sln --configuration Release`
Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add -A ':!.dotnet-main' ':!.sisyphus'
git commit -m "chore: final build and test verification after bugfixes"
```

---

## Dependency Graph

```
Task 1 (Toggle layout) — independent
Task 2 (Navigation fix) — independent  
Task 3 (Win11 icons) — independent (modifies same files as Task 1 but different sections)
Task 4 (Verification) — depends on Tasks 1-3
```

All 3 fix tasks are independent and can run in parallel.
