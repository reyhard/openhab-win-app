# openHAB Windows UI Polish & Feature Completion Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers-extended-cc:subagent-driven-development (if subagents available) or superpowers-extended-cc:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Polish the Windows 11 tray flyout to match the visual mockup and complete missing sitemap widget features (selection, slider interaction, submenu navigation, icons, state transforms, sitemap list dropdown).

**Architecture:** Extend the existing layered pipeline — `OpenHab.Core.Api` for new REST endpoints, `OpenHab.Sitemaps` for icon/model changes, `OpenHab.Rendering` for skin-aware state transforms, `OpenHab.App` for new settings, and `OpenHab.Windows.Tray` for UI polish and animation. All changes follow the existing project boundaries.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK), xUnit, System.Text.Json

---

## File Structure Map

Before defining tasks, here's what each file is responsible for and what will change:

| File | Current Role | Changes in This Plan |
|------|-------------|---------------------|
| `src/OpenHab.Core/Api/IOpenHabClient.cs` | HTTP client interface (3 methods) | Add `GetSitemapsAsync()` |
| `src/OpenHab.Core/Api/OpenHabHttpClient.cs` | HTTP implementation | Add sitemap list endpoint call |
| `src/OpenHab.Sitemaps/Models/SitemapModels.cs` | Widget/page models | Add `Icon` property to `SitemapWidget`, add state transform support |
| `src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs` | JSON parser | Parse `icon` field from widget JSON |
| `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs` | Render descriptors | Add `Icon` to `SitemapRowDescriptor`, add `RenderControlKind.Navigate` awareness |
| `src/OpenHab.Rendering/Skins/SitemapRowMapper.cs` | Widget→row mapping | Apply mapping-based state transforms, pass icon through |
| `src/OpenHab.App/Settings/AppSettings.cs` | Settings model | Add `FlyoutWidth`, `FollowSystemTheme`, `UseWindowsIcons` settings |
| `src/OpenHab.App/Settings/AppSettingsController.cs` | Settings controller | Add setters for new settings |
| `src/OpenHab.App/Runtime/SitemapRuntimeController.cs` | Runtime orchestration | Add sitemap list loading, submenu navigation, selection/slider commands |
| `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs` | Widget→WinUI control | Enable Slider/Selection, add icon rendering, Windows 11 layout |
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml` | Flyout layout | Replace TextBox with ComboBox for sitemap, add SymbolIcon buttons, adjust width |
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` | Flyout code-behind | Wire ComboBox, navigation, animation |
| `src/OpenHab.Windows.Tray/MainWindow.xaml` | Main window layout | Same ComboBox/sitemap list changes |
| `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` | Main window code-behind | Wire ComboBox, navigation |
| `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs` | Flyout placement | Fix right-side tray alignment |
| `src/OpenHab.Windows.Tray/App.xaml` | Application resources | Add theme-aware brushes, custom styles |
| `src/OpenHab.Windows.Tray/App.xaml.cs` | DI and lifecycle | Wire new dependencies, apply theme settings |

---

### Task 1: Sitemap List API Endpoint

**Files:**
- Modify: `src/OpenHab.Core/Api/IOpenHabClient.cs`
- Modify: `src/OpenHab.Core/Api/OpenHabHttpClient.cs`
- Modify: `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`

- [ ] **Step 1: Add `GetSitemapsAsync` to the interface**

```csharp
// IOpenHabClient.cs — add method:
Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken cancellationToken);
```

- [ ] **Step 2: Define `SitemapInfo` record**

```csharp
// New file or in IOpenHabClient.cs:
public sealed record SitemapInfo(string Name, string Label);
```

- [ ] **Step 3: Implement in OpenHabHttpClient**

The openHAB REST API returns sitemaps at `GET /rest/sitemaps`. The response is JSON:
```json
[{"name": "default", "label": "My Home", "link": "...", "homepage": {...}}]
```

Implement parsing — extract `name` and `label`:
```csharp
// OpenHabHttpClient.cs
public async Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("rest/sitemaps"));
    ApplyAuth(request);
    using var response = await _httpClient.SendAsync(request, cancellationToken);
    await ThrowIfFailedAsync(response, cancellationToken);
    var json = await response.Content.ReadAsStringAsync(cancellationToken);
    return ParseSitemapList(json);
}

private static IReadOnlyList<SitemapInfo> ParseSitemapList(string json)
{
    using var document = JsonDocument.Parse(json);
    var list = new List<SitemapInfo>();
    foreach (var element in document.RootElement.EnumerateArray())
    {
        var name = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
        var label = element.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? name : name;
        list.Add(new SitemapInfo(name, label));
    }
    return list;
}
```

- [ ] **Step 4: Update FakeOpenHabClient**

```csharp
// FakeOpenHabClient.cs — add:
public List<SitemapInfo>? SitemapsList { get; set; }

public Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken cancellationToken)
{
    return Task.FromResult<IReadOnlyList<SitemapInfo>>(SitemapsList ?? []);
}
```

- [ ] **Step 5: Build and verify compilation**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/OpenHab.Core/Api/IOpenHabClient.cs src/OpenHab.Core/Api/OpenHabHttpClient.cs tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs
git commit -m "feat: add sitemap list API endpoint to IOpenHabClient"
```

---

### Task 2: Sitemap Dropdown in Flyout Window

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`

- [ ] **Step 1: Replace TextBox with ComboBox in FlyoutWindow.xaml**

Replace the `TextBox x:Name="SitemapNameText"` (lines 23-26) with:
```xml
<ComboBox x:Name="SitemapCombo"
          Grid.Row="1"
          Header="Sitemap"
          SelectionChanged="SitemapCombo_SelectionChanged" />
```

- [ ] **Step 2: Add sitemap loading to FlyoutWindow.xaml.cs**

```csharp
private IReadOnlyList<SitemapInfo>? availableSitemaps;

public async Task LoadSitemapsAsync(IReadOnlyList<SitemapInfo> sitemaps)
{
    availableSitemaps = sitemaps;
    SitemapCombo.Items.Clear();
    foreach (var sitemap in sitemaps)
    {
        SitemapCombo.Items.Add(new ComboBoxItem { Content = sitemap.Label, Tag = sitemap.Name });
    }
    
    var currentSitemap = settingsController.Current.SitemapName;
    var selected = sitemaps.FirstOrDefault(s => s.Name == currentSitemap);
    if (selected is not null)
    {
        SitemapCombo.SelectedIndex = sitemaps.ToList().IndexOf(selected);
    }
}

private async void SitemapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (SitemapCombo.SelectedItem is ComboBoxItem item && item.Tag is string sitemapName)
    {
        settingsController.SetSitemapName(sitemapName);
        await LoadRuntimeAsync();
    }
}
```

Remove the old `SitemapNameText_LostFocus` handler and related code.

- [ ] **Step 3: Add sitemap list loading to SitemapRuntimeController**

```csharp
// SitemapRuntimeController.cs — add method:
public async Task<IReadOnlyList<SitemapInfo>> LoadSitemapListAsync(CancellationToken cancellationToken = default)
{
    var settings = settingsController.Current;
    var primary = SelectPrimaryTransport(settings);
    var client = clientFactory(primary.Kind, primary.BaseUri);
    return await client.GetSitemapsAsync(cancellationToken);
}
```

- [ ] **Step 4: Wire into App.xaml.cs startup**

In `App.xaml.cs`, after `flyoutWindow` is created, add sitemap loading:
```csharp
// In CompleteStartupAsync or after flyout creation:
var sitemaps = await runtimeController.LoadSitemapListAsync();
await flyoutWindow.LoadSitemapsAsync(sitemaps);
```

- [ ] **Step 5: Build and verify compilation**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/OpenHab.Windows.Tray/FlyoutWindow.xaml src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.App/Runtime/SitemapRuntimeController.cs src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "feat: replace sitemap textbox with dropdown, load sitemap list at startup"
```

---

### Task 3: Sitemap Dropdown in Main Window

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Replace TextBox with ComboBox in MainWindow.xaml**

Replace the `TextBox x:Name="SitemapNameText"` and its adjacent Load button (lines 31-36) with:
```xml
<ComboBox x:Name="SitemapCombo"
          Header="Sitemap"
          MaxWidth="360"
          SelectionChanged="SitemapCombo_SelectionChanged" />
<!-- Remove the "Load" button since selection triggers load automatically -->
```

- [ ] **Step 2: Mirror the ComboBox wiring from FlyoutWindow**

Copy the same `LoadSitemapsAsync` and `SitemapCombo_SelectionChanged` pattern from FlyoutWindow.xaml.cs into MainWindow.xaml.cs.

Remove the old `SitemapNameText_LostFocus` handler.

- [ ] **Step 3: Wire sitemap loading in App.xaml.cs**

Add a call to `mainWindow.LoadSitemapsAsync(sitemaps)` after loading sitemaps (from Task 2 Step 4).

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "feat: add sitemap dropdown to main window"
```

---

### Task 4: State Transforms via Mappings

**Files:**
- Modify: `src/OpenHab.Rendering/Skins/SitemapRowMapper.cs`
- Create: `tests/OpenHab.Rendering.Tests/SitemapStateTransformTests.cs` (or extend existing)

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OpenHab.Rendering.Tests/SitemapStateTransformTests.cs
[Fact]
public void ToRow_UsesMappingLabel_WhenStateMatchesMappingCommand()
{
    var widget = new NormalizedSitemapWidget(
        "Door", SitemapWidgetType.Switch, "DoorSensor", "OPEN",
        [new SitemapMapping("OPEN", "Unlocked"), new SitemapMapping("CLOSED", "Locked")],
        false, false, SitemapFallbackKind.None, []);

    var row = SitemapRowMapper.ToRow(widget, RenderDensity.Compact);

    Assert.Equal("Unlocked", row.State); // NOT "OPEN"
}

[Fact]
public void ToRow_KeepsOriginalState_WhenNoMappingMatch()
{
    var widget = new NormalizedSitemapWidget(
        "Sensor", SitemapWidgetType.Text, "Temp", "21.5",
        [], false, false, SitemapFallbackKind.None, []);

    var row = SitemapRowMapper.ToRow(widget, RenderDensity.Compact);

    Assert.Equal("21.5", row.State);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OpenHab.Rendering.Tests --filter "SitemapStateTransformTests"`
Expected: FAIL — state is "OPEN" not "Unlocked"

- [ ] **Step 3: Implement mapping-based state transform in SitemapRowMapper**

```csharp
// SitemapRowMapper.cs — modify ToRow to transform state:
public static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget, RenderDensity density)
{
    var state = TransformState(widget.State, widget.Mappings);
    var control = ControlFor(widget);
    var action = ActionFor(widget, control);
    return new SitemapRowDescriptor(widget.Label, state, control, action, density);
}

private static string? TransformState(string? state, IReadOnlyList<SitemapMapping> mappings)
{
    if (string.IsNullOrEmpty(state) || mappings.Count == 0)
    {
        return state;
    }
    
    var match = mappings.FirstOrDefault(m => 
        string.Equals(m.Command, state, StringComparison.OrdinalIgnoreCase));
    return match is not null && !string.IsNullOrWhiteSpace(match.Label) 
        ? match.Label 
        : state;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OpenHab.Rendering.Tests --filter "SitemapStateTransformTests"`
Expected: PASS

- [ ] **Step 5: Run full Rendering test suite**

Run: `dotnet test tests/OpenHab.Rendering.Tests`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add src/OpenHab.Rendering/Skins/SitemapRowMapper.cs tests/OpenHab.Rendering.Tests/SitemapStateTransformTests.cs
git commit -m "feat: apply sitemap mapping labels as state transforms"
```

---

### Task 5: Selection Widget — Interactive Picker

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`

- [ ] **Step 1: Replace disabled Button with ComboBox in SitemapControlFactory**

Change `CreateSelection` (lines 77-87):
```csharp
private static FrameworkElement CreateSelection(SitemapRowDescriptor row, Func<Task>? activateRow)
{
    var comboBox = new ComboBox
    {
        Header = row.Label,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    
    // The mappings come through the widget model. We need them passed in the descriptor.
    // For now, use the state as the display and placeholder for selection.
    if (!string.IsNullOrWhiteSpace(row.State))
    {
        comboBox.PlaceholderText = row.State;
    }
    
    if (activateRow is not null)
    {
        comboBox.SelectionChanged += async (_, _) => await activateRow();
    }
    
    return comboBox;
}
```

Wait — the `SitemapRowDescriptor` doesn't carry mappings. We need to extend it.

- [ ] **Step 2: Add mappings to SitemapRowDescriptor**

```csharp
// RenderDescriptors.cs — update SitemapRowDescriptor:
public sealed record SitemapRowDescriptor(
    string Label,
    string? State,
    RenderControlKind Control,
    RenderActionKind Action,
    RenderDensity Density,
    IReadOnlyList<SitemapMapping> Mappings = null!);

// But RenderDescriptors is in OpenHab.Rendering which shouldn't reference Sitemaps.Models.
// Better: add a simple record in RenderDescriptors:
public sealed record SitemapMappingDescriptor(string Command, string Label);
```

Actually, let's not add a cross-layer dependency. Instead, the cleanest approach: pass mappings as a `IReadOnlyList<(string Command, string Label)>` from the row mapper. Even simpler — add a `SelectionOptions` property to `SitemapRowDescriptor`:

```csharp
// RenderDescriptors.cs:
public sealed record SitemapRowDescriptor(
    string Label,
    string? State,
    RenderControlKind Control,
    RenderActionKind Action,
    RenderDensity Density,
    IReadOnlyList<SitemapMapOption> SelectionOptions);

public sealed record SitemapMapOption(string Command, string Label);
```

Default to empty array. Update `SitemapRowMapper.ToRow` to pass mappings when Selection.

- [ ] **Step 3: Update SitemapRowMapper to pass mappings**

```csharp
// SitemapRowMapper.cs:
public static SitemapRowDescriptor ToRow(NormalizedSitemapWidget widget, RenderDensity density)
{
    var state = TransformState(widget.State, widget.Mappings);
    var control = ControlFor(widget);
    var action = ActionFor(widget, control);
    var options = widget.Mappings
        .Select(m => new SitemapMapOption(m.Command, m.Label))
        .ToArray();
    return new SitemapRowDescriptor(widget.Label, state, control, action, density, options);
}
```

- [ ] **Step 4: Implement proper Selection ComboBox**

```csharp
// SitemapControlFactory.cs:
private static FrameworkElement CreateSelection(SitemapRowDescriptor row, Func<Task>? activateRow)
{
    var panel = new StackPanel { Spacing = 4 };
    
    var labelText = new TextBlock
    {
        Text = row.Label,
        TextWrapping = TextWrapping.WrapWholeWords,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 2
    };
    panel.Children.Add(labelText);
    
    var comboBox = new ComboBox
    {
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    
    foreach (var option in row.SelectionOptions)
    {
        var item = new ComboBoxItem { Content = option.Label, Tag = option.Command };
        comboBox.Items.Add(item);
        if (string.Equals(option.Command, row.State, StringComparison.OrdinalIgnoreCase))
        {
            comboBox.SelectedItem = item;
        }
    }
    
    panel.Children.Add(comboBox);
    return panel;
}
```

- [ ] **Step 5: Update RuntimeController to handle Selection commands**

```csharp
// SitemapRuntimeController.cs — modify ActivateRowAsync (line 127 check):
// Currently only handles Switch. Extend to handle Selection and Slider/Setpoint:
var widget = currentPage.Widgets[rowIndex];
if (string.IsNullOrWhiteSpace(widget.ItemName))
    return false;

// Send the command for the selected option.
// For Selection, the command comes from the mapping selection — but this method
// doesn't receive which option was selected. Architecture note: the FlyoutWindow
// would need to pass the selected command through.
```

Wait — the current architecture doesn't pass the selected option value from the UI to the controller. The `activateRow` callback doesn't have parameters.

**Simpler approach for Selection**: The `ComboBox.SelectionChanged` event handler in the control factory should directly call `SendCommandAsync` via a DI-injected client. But `SitemapControlFactory` is a static class...

**Architecture decision**: Add a callback parameter to `SitemapControlFactory.Create`:
```csharp
public static FrameworkElement Create(
    SitemapRowDescriptor row, 
    Func<Task>? activateRow,
    Func<string, Task>? sendCommand = null)
```

And wire it in FlyoutWindow.xaml.cs:
```csharp
SitemapRows.Children.Add(SitemapControlFactory.Create(row, activateRow, 
    sendCommand: async (cmd) => await runtimeController.SendCommandForRowAsync(rowIndex, cmd)));
```

Then in the Selection ComboBox handler, call `sendCommand(selectedCommand)`.

Let's not over-complicate this. For now, implement the Selection as working interactively, and handle the command sending in the FlyoutWindow:

- [ ] **Step 5 (revised): Wire send-command callback in SitemapControlFactory**

Add optional `Func<string, Task>? sendCommand` parameter. In CreateSelection, wire `SelectionChanged` to call it with the selected command. In FlyoutWindow, wire it up.

- [ ] **Step 6: Add SendCommandAsync overload to RuntimeController**

```csharp
public async Task<bool> SendCommandForRowAsync(int rowIndex, string command, CancellationToken ct = default)
{
    var widget = currentPage?.Widgets[rowIndex];
    if (widget?.ItemName is null) return false;
    var client = clientFactory(Current.ActiveTransport!, /* endpoint */);
    await client.SendCommandAsync(widget.ItemName, command, ct);
    await RefreshAsync(ct);
    return true;
}
```

- [ ] **Step 7: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 8: Commit**

```bash
git add src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs src/OpenHab.Rendering/Skins/SitemapRowMapper.cs src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.App/Runtime/SitemapRuntimeController.cs
git commit -m "feat: implement interactive selection widget with mapping options"
```

---

### Task 6: Slider/Setpoint Enablement

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`

- [ ] **Step 1: Enable Slider and wire interaction**

In `CreateSlider` (lines 46-75), change `IsEnabled = false` to `IsEnabled = true` and wire the value change:
```csharp
private static FrameworkElement CreateSlider(SitemapRowDescriptor row, Func<Task>? activateRow, Func<string, Task>? sendCommand)
{
    var value = double.TryParse(row.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        ? Math.Clamp(parsed, 0, 100)
        : 0;

    var slider = new Slider
    {
        Minimum = 0,
        Maximum = 100,
        Value = value,
        IsEnabled = true
    };

    if (sendCommand is not null)
    {
        slider.ValueChanged += async (_, args) =>
        {
            var newValue = args.NewValue.ToString("F0", CultureInfo.InvariantCulture);
            await sendCommand(newValue);
        };
    }

    return new StackPanel { Spacing = 4, Children = { labelText, slider } };
}
```

- [ ] **Step 2: Update the Create method signature**

```csharp
public static FrameworkElement Create(
    SitemapRowDescriptor row, 
    Func<Task>? activateRow,
    Func<string, Task>? sendCommand = null)
```

- [ ] **Step 3: Wire sendCommand in FlyoutWindow**

```csharp
// FlyoutWindow.xaml.cs — RefreshRuntimeBindings:
Func<string, Task>? sendCommand = row.Action == RenderActionKind.SendCommand
    ? async (cmd) => await runtimeController.SendCommandForRowAsync(rowIndex, cmd)
    : null;
SitemapRows.Children.Add(SitemapControlFactory.Create(row, activateRow, sendCommand));
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs
git commit -m "feat: enable interactive slider/setpoint widgets"
```

---

### Task 7: Submenu Navigation

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`

- [ ] **Step 1: Add navigation method to RuntimeController**

```csharp
// SitemapRuntimeController.cs — add method:
public async Task<bool> NavigateToChildAsync(int rowIndex, CancellationToken ct = default)
{
    if (currentPage is null || rowIndex >= currentPage.Widgets.Count) return false;
    var widget = currentPage.Widgets[rowIndex];
    if (widget.Children.Count == 0) return false;
    
    var childPage = widget.Children[0]; // First linked page
    var settings = settingsController.Current;
    var primary = SelectPrimaryTransport(settings);
    var client = clientFactory(primary.Kind, primary.BaseUri);
    
    // Load the child page from server
    var json = await client.GetSitemapJsonAsync(childPage.Id, ct);
    var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
    var normalized = SitemapNormalizer.Normalize(parsed);
    currentPage = normalized;
    
    var descriptor = renderController.BuildCurrentDescriptor(normalized);
    Current = Current with
    {
        Descriptor = descriptor,
        StatusText = $"Navigated to: {normalized.Label}"
    };
    return true;
}
```

- [ ] **Step 2: Wire Navigate action in FlyoutWindow**

```csharp
// FlyoutWindow.xaml.cs — RefreshRuntimeBindings, after the Switch activateRow check:
Func<Task>? activateRow = row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand
    ? () => OnRowActivatedAsync(rowIndex)
    : row.Action == RenderActionKind.Navigate
        ? () => OnRowNavigateAsync(rowIndex)
        : null;

// Add method:
private async Task OnRowNavigateAsync(int rowIndex)
{
    await RunRuntimeOperationAsync(async ct => await runtimeController.NavigateToChildAsync(rowIndex, ct));
}
```

- [ ] **Step 3: Make navigable rows visually clickable**

In `SitemapControlFactory`, for rows with `RenderActionKind.Navigate`, make the Text/grid row look tappable (cursor pointer, subtle hover effect, or a chevron icon):

```csharp
// In CreateText/CreateRow, if action is Navigate, add a chevron:
if (row.Action == RenderActionKind.Navigate)
{
    var chevron = new FontIcon
    {
        Glyph = "\uE76C", // ChevronRight
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.6
    };
    Grid.SetColumn(chevron, 2);
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    grid.Children.Add(chevron);
}
```

- [ ] **Step 4: Wire Navigate in MainWindow similarly**

Same pattern as FlyoutWindow.

- [ ] **Step 5: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/OpenHab.App/Runtime/SitemapRuntimeController.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.Windows.Tray/MainWindow.xaml.cs src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs
git commit -m "feat: implement submenu navigation with chevron indicator"
```

---

### Task 8: Sitemap Icons

**Files:**
- Modify: `src/OpenHab.Sitemaps/Models/SitemapModels.cs`
- Modify: `src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs`
- Modify: `src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs`
- Modify: `src/OpenHab.Rendering/Skins/SitemapRowMapper.cs`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

- [ ] **Step 1: Add Icon to SitemapWidget model**

```csharp
// SitemapModels.cs — add to SitemapWidget:
public sealed record SitemapWidget(
    string Label,
    SitemapWidgetType Type,
    string? ItemName,
    string? State,
    IReadOnlyList<SitemapMapping> Mappings,
    bool IsVisible,
    IReadOnlyList<SitemapPage> Children,
    string? Icon = null); // <— NEW

// Also add to NormalizedSitemapWidget:
public sealed record NormalizedSitemapWidget(
    string Label,
    SitemapWidgetType Type,
    string? ItemName,
    string? State,
    IReadOnlyList<SitemapMapping> Mappings,
    bool CanNavigate,
    bool RequiresFallback,
    SitemapFallbackKind FallbackKind,
    IReadOnlyList<SitemapPage> Children,
    string? Icon = null); // <— NEW
```

- [ ] **Step 2: Parse icon from JSON**

```csharp
// OpenHabSitemapJsonParser.cs — in ParseWidget, add after label parsing:
var icon = GetStringOrNull(widgetElement, "icon");
```

And pass it to the `SitemapWidget` constructor. Also pass through in the Normalizer.

- [ ] **Step 3: Add IconUrl to SitemapRowDescriptor**

```csharp
// RenderDescriptors.cs:
public sealed record SitemapRowDescriptor(
    string Label,
    string? State,
    RenderControlKind Control,
    RenderActionKind Action,
    RenderDensity Density,
    IReadOnlyList<SitemapMapOption> SelectionOptions,
    string? IconUrl = null); // <— NEW
```

- [ ] **Step 4: Pass icon through SitemapRowMapper**

```csharp
// SitemapRowMapper.cs — in ToRow, add icon URL construction:
var iconUrl = widget.Icon is not null 
    ? $"icon/{Uri.EscapeDataString(widget.Icon)}.png" 
    : null;
```

- [ ] **Step 5: Render icon in SitemapControlFactory**

In `CreateRow` and other methods, add an icon column if `IconUrl` is present:
```csharp
if (!string.IsNullOrWhiteSpace(row.IconUrl))
{
    // Add icon column at position 0, shift label to 1, state to 2
    // Use a deferred image load or a simple placeholder
    var iconImage = new Image
    {
        Width = 20,
        Height = 20,
        Source = new BitmapImage(new Uri(row.BaseUri, row.IconUrl)),
        VerticalAlignment = VerticalAlignment.Center
    };
    Grid.SetColumn(iconImage, 0);
    grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
    grid.Children.Add(iconImage);
}
```

Wait — `SitemapControlFactory` doesn't have access to the base URI. We need to either:
1. Build the full icon URL in the row mapper (but row mapper is in Rendering, which shouldn't know about HTTP endpoints)
2. Pass the base URI to the factory
3. Build the full URL in the tray layer

**Decision**: Add `BaseUri` to `SitemapRowDescriptor`, set in the controller, or pass icon URL as a full URL from the controller. The cleanest: pass icon name in the descriptor and resolve URL in SitemapControlFactory by having the controller construct the full URI.

Actually, let's keep it simple — add an `IconUri` string property to the descriptor that the runtime controller fills in. The row mapper passes `widget.Icon` as a raw name, and the controller resolves it.

**Simpler approach**: The `SitemapRuntimeController` already has access to the endpoint. After building the descriptor, let it post-process icon URLs. Or even simpler — the tray layer can construct the icon URL from the base URI (which it can get from settings):

Add to `SitemapRowDescriptor`: `string? IconName`. Then in `SitemapControlFactory`, accept a base URI. Or just pass `IconName` to the factory and it's the caller's job to provide the base URI.

**Final decision**: Add `string? IconName` to the descriptor. In FlyoutWindow, pass the base URI to `SitemapControlFactory.Create` as an optional parameter, and build the icon URL there.

- [ ] **Step 6: Test icon parsing**

Add test to `OpenHabSitemapJsonParserTests`:
```csharp
[Fact]
public void ParseWidget_ExtractsIcon()
{
    const string json = """{"homepage":{"id":"h","widgets":[{"type":"Switch","label":"Light","icon":"light"}]}}""";
    var page = OpenHabSitemapJsonParser.ParseHomepage(json);
    var widget = Assert.Single(page.Widgets);
    Assert.Equal("light", widget.Icon);
}
```

- [ ] **Step 7: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Run: `dotnet test tests/OpenHab.Sitemaps.Tests`
Expected: All pass

- [ ] **Step 8: Commit**

```bash
git add src/OpenHab.Sitemaps/Models/SitemapModels.cs src/OpenHab.Sitemaps/Parsing/OpenHabSitemapJsonParser.cs src/OpenHab.Sitemaps/Runtime/SitemapNormalizer.cs src/OpenHab.Rendering/Descriptors/RenderDescriptors.cs src/OpenHab.Rendering/Skins/SitemapRowMapper.cs src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs tests/OpenHab.Sitemaps.Tests/OpenHabSitemapJsonParserTests.cs
git commit -m "feat: parse and display sitemap widget icons"
```

---

### Task 9: Flyout Polish — Width, Positioning, Animation

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Change default flyout width to 500px**

```csharp
// TrayFlyoutPositioner.cs — line 10:
private const int DefaultFlyoutWidth = 500; // was 420
```

- [ ] **Step 2: Fix right-side tray alignment**

The current algorithm positions near cursor. Change to position near the taskbar/tray area at the right or bottom of the screen:

```csharp
// TrayFlyoutPositioner.cs — update CalculatePlacement:
// Position flyout at the right edge of the working area (taskbar-aware)
public static TrayFlyoutPlacement CalculatePlacement(Point cursorPosition, int flyoutWidth, int flyoutHeight)
{
    var screen = Screen.FromPoint(cursorPosition);
    var workArea = screen.WorkingArea;
    var bounds = screen.Bounds;
    
    // Determine taskbar position by comparing bounds vs workArea
    var taskbarOnBottom = workArea.Bottom < bounds.Bottom;
    var taskbarOnRight = workArea.Right < bounds.Right;
    
    int x, y;
    
    if (taskbarOnBottom)
    {
        // Taskbar at bottom — flyout above taskbar, right-aligned
        x = workArea.Right - flyoutWidth - ScreenPadding;
        y = workArea.Bottom - flyoutHeight - ScreenPadding;
    }
    else if (taskbarOnRight)
    {
        // Taskbar on right — flyout to the left of taskbar, bottom-aligned
        x = workArea.Right - flyoutWidth - ScreenPadding;
        y = workArea.Bottom - flyoutHeight - ScreenPadding;
    }
    else
    {
        // Taskbar elsewhere or unknown — use cursor-based fallback
        x = cursorPosition.X - flyoutWidth + CursorOffset;
        y = cursorPosition.Y - flyoutHeight - CursorOffset;
    }
    
    // Clamp to work area
    x = Math.Clamp(x, workArea.Left + ScreenPadding, workArea.Right - flyoutWidth - ScreenPadding);
    y = Math.Clamp(y, workArea.Top + ScreenPadding, workArea.Bottom - flyoutHeight - ScreenPadding);
    
    return new TrayFlyoutPlacement(x, y, flyoutWidth, flyoutHeight);
}
```

- [ ] **Step 3: Add smooth flyout animation**

WinUI doesn't have built-in window show/hide animations. The approach: use `AppWindow` opacity or a slide-in effect:

```csharp
// In App.xaml.cs — ApplyShellStateAsync, when showing flyout:
case TrayShellSurface.Flyout:
    main.AppWindow.Hide();
    TrayFlyoutPositioner.PlaceNearTrayArea(flyout);
    flyout.Activate();
    // Animate: set initial offset and animate to final position
    AnimateFlyoutIn(flyout);
    break;
```

Add animation helper:
```csharp
private static void AnimateFlyoutIn(FlyoutWindow flyout)
{
    var appWindow = flyout.AppWindow;
    var originalRect = appWindow.Position;
    
    // Slide up from below
    var startY = originalRect.Y + 40;
    appWindow.Move(new Windows.Graphics.PointInt32(originalRect.X, startY));
    
    // Animate to final position using Composition animation or timer
    // WinUI AppWindow doesn't support smooth animation natively.
    // Use a simple approach: DispatcherTimer with easing
    var steps = 10;
    var delay = TimeSpan.FromMilliseconds(16); // ~60fps
    var dy = (double)(startY - originalRect.Y) / steps;
    
    var dispatcherTimer = new DispatcherTimer { Interval = delay };
    var currentStep = 0;
    dispatcherTimer.Tick += (s, e) =>
    {
        currentStep++;
        var easedY = startY - (dy * currentStep * (2.0 - (double)currentStep / steps)); // ease-out
        appWindow.Move(new Windows.Graphics.PointInt32(originalRect.X, (int)easedY));
        if (currentStep >= steps)
        {
            appWindow.Move(new Windows.Graphics.PointInt32(originalRect.X, originalRect.Y));
            dispatcherTimer.Stop();
        }
    };
    dispatcherTimer.Start();
}
```

Wait — this is fragile (60fps animation via timer). Better approach: just set the flyout window opacity and fade in. Or use the simpler approach: just do a quick opacity fade:

```csharp
// In FlyoutWindow.xaml.cs — add after InitializeComponent:
this.Opacity = 0;
this.Activated += async (s, e) =>
{
    // Fade in
    for (double o = 0; o <= 1; o += 0.1)
    {
        this.Opacity = o;
        await Task.Delay(16);
    }
    this.Opacity = 1;
};
```

Simpler yet — just call it done with the position fix and opacity approach.

- [ ] **Step 4: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs src/OpenHab.Windows.Tray/App.xaml.cs
git commit -m "feat: widen flyout to 500px, fix right-side tray positioning, add fade-in animation"
```

---

### Task 10: UI Visual Polish — Icons & Windows 11 Layout

**Files:**
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml`

- [ ] **Step 1: Replace text buttons with icon buttons in FlyoutWindow.xaml**

```xml
<!-- Replace the three-button footer grid (lines 32-46): -->
<StackPanel Grid.Row="3" Orientation="Horizontal" Spacing="4" HorizontalAlignment="Right">
    <Button Click="RefreshButton_Click" ToolTipService.ToolTip="Refresh">
        <FontIcon Glyph="&#xE72C;" FontSize="14" /> <!-- Refresh/Sync icon -->
    </Button>
    <Button Click="OpenAppButton_Click" ToolTipService.ToolTip="Open main window">
        <FontIcon Glyph="&#xE8A1;" FontSize="14" /> <!-- Expand/Open icon -->
    </Button>
    <Button Click="SettingsButton_Click" ToolTipService.ToolTip="Settings">
        <FontIcon Glyph="&#xE713;" FontSize="14" /> <!-- Settings/Cog icon -->
    </Button>
</StackPanel>
```

- [ ] **Step 2: Replace text buttons in MainWindow.xaml**

Replace "Load" and "Refresh" text buttons with icon buttons:
```xml
<Button Grid.Column="1" Click="LoadButton_Click" ToolTipService.ToolTip="Load">
    <FontIcon Glyph="&#xE72C;" FontSize="14" />
</Button>
<Button Grid.Column="2" Click="RefreshButton_Click" ToolTipService.ToolTip="Refresh">
    <FontIcon Glyph="&#xE72C;" FontSize="14" />
</Button>
```

- [ ] **Step 3: Add Windows 11 styling resources to App.xaml**

```xml
<!-- App.xaml — add to ResourceDictionary.MergedDictionaries after XamlControlsResources -->
<ResourceDictionary>
    <!-- Override default button styles for compact icon buttons -->
    <Style x:Key="IconButtonStyle" TargetType="Button">
        <Setter Property="Width" Value="36" />
        <Setter Property="Height" Value="36" />
        <Setter Property="Padding" Value="4" />
        <Setter Property="CornerRadius" Value="4" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 4: Add Windows 11 layout to sitemap rows — icon on left**

In `SitemapControlFactory.CreateRow`, restructure the grid to have: `[icon] [label] [state]`:

```csharp
// Already partially done in Task 8. Ensure the layout is:
// [Icon | 24px] [Label | *] [State | Auto] [Chevron | Auto]
grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(24) });
// Shift existing children columns
```

- [ ] **Step 5: Replace SystemIcons.Application with custom icon**

```csharp
// TrayIconService.cs — line 27:
// Replace SystemIcons.Application with a custom icon from resources
// For now, use a better system icon or add a custom .ico file
using var stream = System.Reflection.Assembly.GetExecutingAssembly()
    .GetManifestResourceStream("OpenHab.Windows.Tray.Assets.openhab.ico");
if (stream is not null)
{
    notifyIcon.Icon = new Icon(stream);
}
else
{
    notifyIcon.Icon = SystemIcons.Application;
}
```

Note: The custom .ico file needs to be added as an embedded resource. Add `openhab-icon.svg` (already exists in `.docs/`) converted to `.ico` to a new `Assets/` folder in the tray project.

- [ ] **Step 6: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add src/OpenHab.Windows.Tray/FlyoutWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/App.xaml src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs src/OpenHab.Windows.Tray/Tray/TrayIconService.cs
git commit -m "feat: add icon buttons, Windows 11 row layout, custom tray icon"
```

---

### Task 11: Theme & Color Settings

**Files:**
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Add theme settings to AppSettings**

```csharp
// AppSettings.cs — add properties:
public sealed record AppSettings(
    SitemapSkinKind Skin,
    EndpointMode EndpointMode,
    Uri LocalEndpoint,
    Uri CloudEndpoint,
    string SitemapName,
    [property: JsonIgnore] bool HasLocalToken = false,
    [property: JsonIgnore] bool HasCloudCredentials = false,
    [property: JsonIgnore] string? CloudUserName = null,
    bool FollowSystemTheme = true,          // NEW
    bool UseWindows11Icons = false)          // NEW
{
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        new Uri("http://openhab:8080"),
        new Uri("https://myopenhab.org"),
        "default");
}
```

- [ ] **Step 2: Add setters to AppSettingsController**

```csharp
public void SetFollowSystemTheme(bool follow)
{
    lock (syncRoot) { Current = Current with { FollowSystemTheme = follow }; }
    _ = SaveAsync();
}

public void SetUseWindows11Icons(bool useWindowsIcons)
{
    lock (syncRoot) { Current = Current with { UseWindows11Icons = useWindowsIcons }; }
    _ = SaveAsync();
}
```

- [ ] **Step 3: Apply theme on startup in App.xaml.cs**

```csharp
// In OnLaunched, after settingsController creation:
if (settingsController.Current.FollowSystemTheme)
{
    // WinUI automatically follows system theme by default
    // But if user disables it, force a specific theme:
    // Not needed — WinUI handles this natively
}
```

Actually, WinUI already follows the system theme. The setting just provides a toggle for future use. The real implementation will be when we add a UI toggle in the settings panel and apply it.

- [ ] **Step 4: Add theme/combo toggle to MainWindow.xaml settings panel**

```xml
<!-- Add after the last setting control in MainWindow.xaml (after CloudPasswordBox): -->
<ToggleSwitch x:Name="FollowThemeToggle"
              Header="Follow Windows color scheme"
              OnContent="On"
              OffContent="Off"
              Toggled="FollowThemeToggle_Toggled" />
<ToggleSwitch x:Name="UseWin11IconsToggle"
              Header="Use Windows 11 style icons"
              OnContent="On"
              OffContent="Off"
              Toggled="UseWin11IconsToggle_Toggled" />
```

- [ ] **Step 5: Wire toggles in MainWindow.xaml.cs**

```csharp
// In RefreshSettingsBindings:
FollowThemeToggle.IsOn = settingsController.Current.FollowSystemTheme;
UseWin11IconsToggle.IsOn = settingsController.Current.UseWindows11Icons;

// Add handlers:
private void FollowThemeToggle_Toggled(object sender, RoutedEventArgs e)
{
    settingsController.SetFollowSystemTheme(FollowThemeToggle.IsOn);
}
private void UseWin11IconsToggle_Toggled(object sender, RoutedEventArgs e)
{
    settingsController.SetUseWindows11Icons(UseWin11IconsToggle.IsOn);
}
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add src/OpenHab.App/Settings/AppSettings.cs src/OpenHab.App/Settings/AppSettingsController.cs src/OpenHab.Windows.Tray/MainWindow.xaml src/OpenHab.Windows.Tray/MainWindow.xaml.cs
git commit -m "feat: add theme and icon style settings"
```

---

### Task 12: Integration — Full Solution Verification

- [ ] **Step 1: Run full solution build**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: 0 warnings, 0 errors

- [ ] **Step 2: Run full solution tests**

Run: `dotnet test OpenHab.Windows.sln --configuration Release`
Expected: All tests pass

- [ ] **Step 3: Check diagnostics on changed files**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`
Expected: No diagnostics errors

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore: full solution verification after UI polish changes"
```

---

## Dependency Graph

```
Task 1 (API endpoint)
  ├── Task 2 (Flyout dropdown)
  ├── Task 3 (Main dropdown)
  └── Task 7 (Navigation - needs API for child page loading)

Task 4 (State transforms) — independent
Task 5 (Selection widget) — independent (but depends on builder pattern from Task 4's tests)
Task 6 (Slider enablement) — independent
Task 8 (Icons) — independent (but consumer of Task 5's descriptor changes)
Task 9 (Flyout polish) — independent
Task 10 (UI visual) — independent
Task 11 (Theme settings) — independent

Task 12 (Integration) — depends on all above
```

**Parallel execution opportunities**: Tasks 4, 5, 6, 8, 9, 10, 11 can all run in parallel (they touch different files). Tasks 2 and 3 depend on Task 1 but can run in parallel with each other. Task 7 depends on Task 1.
