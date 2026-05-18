# Crowdin Translations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Crowdin-managed localization for the Windows companion app without pushing WinUI-specific resource loading into the core, sitemap, or rendering layers.

**Architecture:** Use WinUI/Windows App SDK `.resw` resources as the single Crowdin source format. Static XAML text should use `x:Uid` where practical; code-generated UI and Windows shell strings should use a small Windows-localization adapter backed by `Microsoft.Windows.ApplicationModel.Resources.ResourceLoader`. Runtime/controller strings that live below the WinUI layer should be localized through a UI-independent text-provider interface with an invariant English fallback for tests.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK, `.resw` resource files, Crowdin `crowdin.yml`, xUnit XML/resource validation tests.

---

## Reference Findings

openhab-android uses a repository-root `crowdin.yml` and keeps English source strings in Android XML resource files. Crowdin writes translated files back into locale-specific resource folders, and the README tells contributors not to submit direct PRs against generated translation folders. Its current config maps `/mobile/src/main/res/values/strings.xml` to `/mobile/src/main/res/values-%two_letters_code%/strings.xml` and has Android-specific locale mappings such as `pt-BR -> pt-rBR`, `he -> iw`, and `zh-CN -> zh-rCN`.

For this app, do not copy the Android `values-*` path or Android language-code mapping. WinUI localization uses `Strings/<BCP-47>/Resources.resw`; Crowdin should write translations with `%locale%`, for example `Strings/pl-PL/Resources.resw`, `Strings/de-DE/Resources.resw`, and `Strings/pt-BR/Resources.resw`.

Microsoft's WinUI guidance says hard-coded XAML/code/manifest strings should move into `.resw`; XAML can bind resource properties with `x:Uid`; packaged apps should declare supported manifest languages instead of relying only on `x-generate`.

Sources:
- https://github.com/openhab/openhab-android/blob/main/crowdin.yml
- https://github.com/openhab/openhab-android#localization
- https://learn.microsoft.com/windows/apps/winui/winui3/localize-winui3-app

## Current App Localization Surface

There are no existing `.resw` or `.resx` files in this repo. User-facing English strings currently appear in these areas:

- Static WinUI XAML: `MainWindow.xaml`, `FlyoutWindow.xaml`, `SettingsPageControl.xaml`, `NotificationsPageControl.xaml`, `MainUiWebViewHost.xaml`.
- Code-generated settings UI: `SettingsPageControl.xaml.cs`, `ShortcutRecorderControl.cs`, `ShortcutSettingsControls.cs`.
- Code-generated notifications UI: `NotificationsPageControl.xaml.cs`, `ToastService.cs`, `ToastNotificationXmlBuilder.cs` request inputs.
- Shell and tray UI: `MainWindow.xaml.cs`, `FlyoutWindow.xaml.cs`, `TrayIconService.cs`, `ShortcutInteractiveCommandWindow.cs`, `RadialCommandMenuWindow.cs`.
- Sitemap fallback labels and diagnostics in Windows rendering: `SitemapControlFactory.cs`.
- UI-visible runtime/app status strings: `SitemapRuntimeController.cs`, `SitemapSearchDescriptorBuilder.cs`, `ShortcutActionExecutor.cs`, `ShortcutValidation.cs`.

Do not translate data that comes from openHAB servers or the user, such as sitemap labels, item names, notification payload titles/bodies, endpoint URLs, command values, diagnostic exception details, and item states. Translate only app-owned chrome, labels, fallback text, command captions, tooltips, dialogs, and template strings.

## File Structure

- Create `crowdin.yml`: repository-root Crowdin configuration modeled on openhab-android.
- Create `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`: source English UI strings.
- Create `src/OpenHab.Windows.Tray/Localization/AppResourceKeys.cs`: constants for non-XAML resource keys used by code.
- Create `src/OpenHab.Windows.Tray/Localization/WinUiTextLocalizer.cs`: ResourceLoader-backed implementation.
- Create `src/OpenHab.App/Localization/ITextLocalizer.cs`: UI-independent abstraction for App-layer user-visible strings.
- Create `src/OpenHab.App/Localization/InvariantTextLocalizer.cs`: dictionary-backed fallback used by tests and non-Windows callers.
- Create `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs`: singleton fallback with the English keys used by App-layer logic.
- Modify `src/OpenHab.Windows.Tray/*.xaml` and nested XAML controls: add `x:Uid` and remove static localizable literals where possible.
- Modify `src/OpenHab.Windows.Tray/*.cs` and `src/OpenHab.Windows.Notifications/*.cs`: replace app-owned literals with localizer calls.
- Modify selected `src/OpenHab.App/*` controllers/validators: accept/use `ITextLocalizer` where they return UI-visible strings.
- Modify `src/OpenHab.Windows.Package/Package.appxmanifest` and `src/OpenHab.Windows.Tray/Package.appxmanifest`: use `ms-resource:` for localizable description text and declare supported languages after the Crowdin project language list is known.
- Modify `README.md` and `CONTRIBUTING.md`: add localization guidance matching openhab-android.
- Create `tests/OpenHab.App.Tests/Localization/LocalizationResourceTests.cs`: parse resources and validate required keys.

## Resource Naming Rules

Use stable, domain-prefixed keys:

- XAML property keys: `MainWindow_ToggleSitemapButton.ToolTipService.ToolTip`, `NotificationsPage_MarkAllReadButton.Content`.
- Code lookup keys: `Settings.Connection.EndpointMode.Title`, `Settings.Connection.EndpointMode.Description`.
- Template keys: `Notifications.Elapsed.MinutesAgo`, `Runtime.Connection.ConnectedViaCloud`.
- Do not encode English text into key names.
- Keep translator comments in `.resw` for placeholders, examples, and strings that must stay short.
- Preserve placeholders as .NET composite formatting (`{0}`, `{1}`) in code keys. Do not use interpolated strings directly for localizable templates.

Example `.resw` entry:

```xml
<data name="Runtime.Connection.ConnectedViaCloud" xml:space="preserve">
  <value>Connected via cloud ({0})</value>
  <comment>{0}: connection state, for example Online, Degraded, or Offline.</comment>
</data>
```

## Task 1: Add Crowdin Source Resource And Config

**Files:**
- Create: `crowdin.yml`
- Create: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`
- Modify: `README.md`
- Modify: `CONTRIBUTING.md`

- [ ] **Step 1: Create the English source `.resw`**

Start with app identity, navigation, flyout, notifications, settings categories, common buttons, runtime status, shortcut dialogs, and sitemap fallbacks. Include translator guidance comments at the top of the file:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <!-- Attention translators: translate app-owned UI text only. Do not translate openHAB, item names, URL examples, file names, shortcut key names, or placeholders like {0}. -->
  <data name="App.Name" xml:space="preserve">
    <value>openHAB</value>
    <comment>Product name. Usually not translated.</comment>
  </data>
  <data name="App.Description" xml:space="preserve">
    <value>openHAB Windows companion app</value>
  </data>
  <data name="Common.Cancel" xml:space="preserve">
    <value>Cancel</value>
  </data>
  <data name="Common.Delete" xml:space="preserve">
    <value>Delete</value>
  </data>
  <data name="MainWindow_SidebarCollapseButton.ToolTipService.ToolTip" xml:space="preserve">
    <value>Collapse navigation</value>
  </data>
  <data name="MainWindow_SidebarCollapseButton.AutomationProperties.Name" xml:space="preserve">
    <value>Collapse navigation</value>
  </data>
  <data name="NotificationsPage_MarkAllReadButton.Content" xml:space="preserve">
    <value>Mark all read</value>
  </data>
  <data name="Notifications.Empty.NoNotifications" xml:space="preserve">
    <value>No notifications</value>
  </data>
  <data name="Notifications.Elapsed.JustNow" xml:space="preserve">
    <value>Just now</value>
  </data>
  <data name="Notifications.Elapsed.MinutesAgo" xml:space="preserve">
    <value>{0}m ago</value>
    <comment>{0}: number of elapsed minutes. Keep short for narrow notification rows.</comment>
  </data>
</root>
```

- [ ] **Step 2: Add `crowdin.yml`**

Use `%locale%` for WinUI BCP-47 folders:

```yaml
commit_message: >
  New translations %original_file_name% (%language%)

  Signed-off-by: openHAB Bot <bot@openhab.org>
append_commit_message: false
files:
  - source: /src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw
    translation: /src/OpenHab.Windows.Tray/Strings/%locale%/%original_file_name%
```

If the openHAB organization requires a different bot sign-off identity, replace the placeholder before enabling Crowdin PRs.

- [ ] **Step 3: Document the workflow**

Add a README localization section modeled on openhab-android:

```markdown
## Localization

English source strings live in `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`.
Translated `src/OpenHab.Windows.Tray/Strings/<locale>/Resources.resw` files are managed through Crowdin.
Please do not submit direct pull requests for generated translation files; submit translations through the Crowdin project once it is published.
```

Add the same rule to `CONTRIBUTING.md`, with a note that app-owned text belongs in `Resources.resw` and server/user-provided text must not be translated.

- [ ] **Step 4: Commit**

```powershell
git add crowdin.yml README.md CONTRIBUTING.md src\OpenHab.Windows.Tray\Strings\en-US\Resources.resw
git commit -s -m "docs: add Crowdin localization plan foundation"
```

## Task 2: Add Resource Lookup Abstractions

**Files:**
- Create: `src/OpenHab.App/Localization/ITextLocalizer.cs`
- Create: `src/OpenHab.App/Localization/InvariantTextLocalizer.cs`
- Create: `src/OpenHab.App/Localization/DefaultEnglishTextLocalizer.cs`
- Create: `src/OpenHab.Windows.Tray/Localization/AppResourceKeys.cs`
- Create: `src/OpenHab.Windows.Tray/Localization/WinUiTextLocalizer.cs`
- Test: `tests/OpenHab.App.Tests/Localization/LocalizationResourceTests.cs`

- [ ] **Step 1: Add the App-layer interface**

```csharp
namespace OpenHab.App.Localization;

public interface ITextLocalizer
{
    string Get(string key);

    string Format(string key, params object[] args);
}
```

- [ ] **Step 2: Add invariant fallback**

```csharp
namespace OpenHab.App.Localization;

public sealed class InvariantTextLocalizer(IReadOnlyDictionary<string, string> strings) : ITextLocalizer
{
    public string Get(string key) =>
        strings.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);
}
```

- [ ] **Step 3: Add default English fallback**

```csharp
namespace OpenHab.App.Localization;

public static class DefaultEnglishTextLocalizer
{
    public static ITextLocalizer Instance { get; } = new InvariantTextLocalizer(Strings);

    private static readonly IReadOnlyDictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Runtime.Status.Loading"] = "Loading...",
        ["Runtime.Connection.Failed"] = "Connection failed.",
        ["Runtime.LiveUpdates.UnavailableRefreshManually"] = "Live updates unavailable. Refresh manually.",
        ["Sitemap.Search.ResultsTitle"] = "Search results",
        ["Shortcuts.Validation.TargetItemRequired"] = "Target Item is required."
    };
}
```

- [ ] **Step 4: Add Windows resource loader**

```csharp
using Microsoft.Windows.ApplicationModel.Resources;
using OpenHab.App.Localization;

namespace OpenHab.Windows.Tray.Localization;

internal sealed class WinUiTextLocalizer : ITextLocalizer
{
    private readonly ResourceLoader resourceLoader = new();

    public string Get(string key)
    {
        var value = resourceLoader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public string Format(string key, params object[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), args);
}
```

- [ ] **Step 5: Add resource key constants for code-owned strings**

```csharp
namespace OpenHab.Windows.Tray.Localization;

internal static class AppResourceKeys
{
    public const string CommonCancel = "Common.Cancel";
    public const string CommonDelete = "Common.Delete";
    public const string NotificationsEmptyNoNotifications = "Notifications.Empty.NoNotifications";
    public const string NotificationsElapsedJustNow = "Notifications.Elapsed.JustNow";
    public const string NotificationsElapsedMinutesAgo = "Notifications.Elapsed.MinutesAgo";
}
```

- [ ] **Step 6: Add resource validation test**

```csharp
using System.Xml.Linq;
using Xunit;

namespace OpenHab.App.Tests.Localization;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void EnglishResourcesContainUniqueNames()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "OpenHab.Windows.Tray", "Strings", "en-US", "Resources.resw"));

        var document = XDocument.Load(path);
        var names = document.Descendants("data")
            .Select(node => (string?)node.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Common.Cancel", names);
        Assert.Contains("Notifications.Elapsed.MinutesAgo", names);
    }
}
```

- [ ] **Step 7: Run test**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore --filter LocalizationResourceTests -p:UseSharedCompilation=false
```

Expected: test passes.

- [ ] **Step 8: Commit**

```powershell
git add src\OpenHab.App\Localization src\OpenHab.Windows.Tray\Localization tests\OpenHab.App.Tests\Localization
git commit -s -m "feat: add localization resource lookup"
```

## Task 3: Localize Static XAML Chrome

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml`
- Modify: `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml`
- Modify: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`

- [ ] **Step 1: Add `x:Uid` to static elements**

Example changes:

```xml
<Button x:Name="SidebarCollapseButton"
        x:Uid="MainWindow_SidebarCollapseButton"
        Width="34"
        Height="30"
        Padding="0"
        Click="SidebarCollapseButton_Click">
```

```xml
<TextBlock x:Name="HomeNavText"
           x:Uid="MainWindow_HomeNavText" />
```

```xml
<TextBox x:Name="NotificationSearchBox"
         x:Uid="NotificationsPage_SearchBox"
         MinWidth="220"
         TextChanged="NotificationSearchBox_TextChanged" />
```

- [ ] **Step 2: Add matching `.resw` property keys**

```xml
<data name="MainWindow_HomeNavText.Text" xml:space="preserve">
  <value>Home</value>
</data>
<data name="NotificationsPage_SearchBox.PlaceholderText" xml:space="preserve">
  <value>Search notifications</value>
</data>
```

- [ ] **Step 3: Keep non-translatable product text literal where clearer**

Keep `Title="openHAB"` and icon asset names literal unless manifest/title localization later requires `ms-resource:App.Name`.

- [ ] **Step 4: Build tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Debug --no-restore -p:UseSharedCompilation=false
```

Expected: build passes.

- [ ] **Step 5: Commit**

```powershell
git add src\OpenHab.Windows.Tray
git commit -s -m "feat: localize static WinUI chrome"
```

## Task 4: Localize Code-Generated Windows UI

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs`
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/ShortcutInteractiveCommandWindow.cs`
- Modify: `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs`
- Modify: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`
- Modify: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`

- [ ] **Step 1: Pass `ITextLocalizer` into code-generated controls**

For `SettingsPageControl`, add a constructor dependency and keep a default overload only if tests require it:

```csharp
private readonly ITextLocalizer text;

public SettingsPageControl(
    AppSettingsController settingsController,
    Func<Task> refreshRuntimeAsync,
    Action<string> setStatusText,
    ITextLocalizer? text = null)
{
    this.settingsController = settingsController;
    this.refreshRuntimeAsync = refreshRuntimeAsync;
    this.setStatusText = setStatusText;
    this.text = text ?? DefaultEnglishTextLocalizer.Instance;
    InitializeComponent();
    InitializeSettingsControls();
    RefreshSettingsBindings();
}
```

- [ ] **Step 2: Replace settings literals**

Example:

```csharp
SettingsSubtitleText.Text = text.Get("Settings.Root.Subtitle");
SettingsContent.Children.Add(CreateCategoryRow(
    "\uE713",
    text.Get("Settings.Connection.Title"),
    text.Get("Settings.Connection.Subtitle"),
    SettingsPageKind.Connection));
```

- [ ] **Step 3: Replace notification literals and elapsed templates**

```csharp
private string GetEmptyNotificationsText(NotificationVisibilityFilter filter, string searchText)
{
    if (!string.IsNullOrWhiteSpace(searchText))
    {
        return text.Get("Notifications.Empty.NoMatches");
    }

    return filter switch
    {
        NotificationVisibilityFilter.Unread => text.Get("Notifications.Empty.NoUnread"),
        NotificationVisibilityFilter.Read => text.Get("Notifications.Empty.NoRead"),
        NotificationVisibilityFilter.Hidden => text.Get("Notifications.Empty.NoHidden"),
        _ => text.Get("Notifications.Empty.NoNotifications")
    };
}

private string FormatElapsedTime(TimeSpan elapsed)
{
    if (elapsed.TotalMinutes < 1)
    {
        return text.Get("Notifications.Elapsed.JustNow");
    }

    return elapsed.TotalHours < 1
        ? text.Format("Notifications.Elapsed.MinutesAgo", (int)elapsed.TotalMinutes)
        : elapsed.TotalDays < 1
            ? text.Format("Notifications.Elapsed.HoursAgo", (int)elapsed.TotalHours)
            : text.Format("Notifications.Elapsed.DaysAgo", (int)elapsed.TotalDays);
}
```

- [ ] **Step 4: Replace tray menu literals**

```csharp
new MenuFlyoutItem { Text = text.Get("Tray.OpenFlyout"), Command = new RelayCommand(toggleFlyout) }
```

- [ ] **Step 5: Replace shortcut dialog literals**

Use resource keys for dialog titles, button labels, errors, and status text such as `ShortcutRecorder.Dialog.Title`, `ShortcutRecorder.Save`, `ShortcutRecorder.Error.RequiresModifier`, `ShortcutRecorder.Status.Cleared`.

- [ ] **Step 6: Replace sitemap fallback labels**

Translate app-owned fallback text only:

```csharp
Content = text.Get("Sitemap.Action.Run");
Text = text.Get("Sitemap.WebView.NoUrlConfigured");
Content = text.Get("Sitemap.WebView.OpenInBrowser");
Text = text.Get("Sitemap.Chart.RequiresItem");
```

- [ ] **Step 7: Run targeted tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false
```

Expected: all App tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src\OpenHab.Windows.Tray src\OpenHab.Windows.Notifications tests\OpenHab.App.Tests
git commit -s -m "feat: localize generated Windows UI text"
```

## Task 5: Localize App-Layer Runtime Text Safely

**Files:**
- Modify: `src/OpenHab.App/Runtime/SitemapRuntimeController.cs`
- Modify: `src/OpenHab.App/Runtime/SitemapSearchDescriptorBuilder.cs`
- Modify: `src/OpenHab.App/Shortcuts/ShortcutActionExecutor.cs`
- Modify: `src/OpenHab.App/Shortcuts/ShortcutValidation.cs`
- Modify: related tests under `tests/OpenHab.App.Tests`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs` or the composition root that creates controllers
- Modify: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`

- [ ] **Step 1: Add optional localizer parameters**

Example for `SitemapRuntimeController`:

```csharp
private readonly ITextLocalizer text;

public SitemapRuntimeController(
    AppSettingsController settingsController,
    Func<TransportKind, Uri, IOpenHabClient> clientFactory,
    Func<TransportKind, Uri, IOpenHabEventStreamClient> eventStreamClientFactory,
    ITextLocalizer? text = null)
{
    this.text = text ?? DefaultEnglishTextLocalizer.Instance;
}
```

- [ ] **Step 2: Replace UI-visible status strings**

```csharp
Current = Current with
{
    IsBusy = true,
    StatusText = text.Get("Runtime.Status.Loading"),
    ChangedRowIndices = []
};
```

```csharp
StatusText = text.Get("Runtime.LiveUpdates.UnavailableRefreshManually");
```

Keep `SafeDiagnosticText.ForUserStatus(ex, fallback)` fallback strings localizable by passing localized fallback values:

```csharp
StatusText = SafeDiagnosticText.ForUserStatus(error, text.Get("Runtime.Connection.Failed"));
```

- [ ] **Step 3: Replace search title**

```csharp
private readonly ITextLocalizer text;
private string SearchTitle => text.Get("Sitemap.Search.ResultsTitle");
```

- [ ] **Step 4: Replace shortcut validation errors**

Use localized templates for errors that reach settings UI:

```csharp
errors.Add(text.Get("Shortcuts.Validation.TargetItemRequired"));
errors.Add(text.Format("Shortcuts.Validation.BindingAlreadyUsed", owner.DisplayName));
```

- [ ] **Step 5: Inject the WinUI localizer at the composition root**

Where `AppSettingsController`, `SitemapRuntimeController`, and Windows controls are created, create one shared instance:

```csharp
var text = new WinUiTextLocalizer();
runtimeController = new SitemapRuntimeController(settingsController, clientFactory, eventStreamFactory, text);
```

- [ ] **Step 6: Update tests to use invariant text**

Tests that assert English status text should pass `DefaultEnglishTextLocalizer.Instance` explicitly or continue relying on default fallback. Keep assertions on English output for invariant tests.

- [ ] **Step 7: Run App tests**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false
```

Expected: all App tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src\OpenHab.App src\OpenHab.Windows.Tray tests\OpenHab.App.Tests
git commit -s -m "feat: localize app runtime status text"
```

## Task 6: Localize Package Manifest Text

**Files:**
- Modify: `src/OpenHab.Windows.Package/Package.appxmanifest`
- Modify: `src/OpenHab.Windows.Tray/Package.appxmanifest`
- Modify: `src/OpenHab.Windows.Tray/Strings/en-US/Resources.resw`

- [ ] **Step 1: Add manifest resource keys**

```xml
<data name="AppDisplayName" xml:space="preserve">
  <value>openHAB</value>
  <comment>Product name. Usually not translated.</comment>
</data>
<data name="AppDescription" xml:space="preserve">
  <value>openHAB Windows companion app</value>
</data>
```

- [ ] **Step 2: Replace localizable manifest description**

```xml
<uap:VisualElements DisplayName="ms-resource:AppDisplayName"
                    Description="ms-resource:AppDescription"
                    Square150x150Logo="Assets\Square150x150Logo.png"
                    Square44x44Logo="Assets\Square44x44Logo.png"
                    BackgroundColor="transparent">
```

- [ ] **Step 3: Keep `x-generate` until languages are approved**

Keep:

```xml
<Resources>
  <Resource Language="x-generate" />
</Resources>
```

After the Crowdin project has an approved supported-language list, replace with explicit `<Resource Language="en-US" />` and translated languages if package validation requires it. Do not guess the official language list in code.

- [ ] **Step 4: Run package validation when DesktopBridge targets are available**

Run:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

Expected: package build passes and generated manifest includes resource references.

- [ ] **Step 5: Commit**

```powershell
git add src\OpenHab.Windows.Package\Package.appxmanifest src\OpenHab.Windows.Tray\Package.appxmanifest src\OpenHab.Windows.Tray\Strings\en-US\Resources.resw
git commit -s -m "feat: localize package manifest text"
```

## Task 7: Add Localization Validation And CI Guardrails

**Files:**
- Modify: `tests/OpenHab.App.Tests/Localization/LocalizationResourceTests.cs`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Validate XAML `x:Uid` keys**

Extend the test to scan XAML files for `x:Uid` and require matching `.resw` keys for common localizable properties:

```csharp
var xamlFiles = Directory.GetFiles(
    Path.Combine(repoRoot, "src", "OpenHab.Windows.Tray"),
    "*.xaml",
    SearchOption.AllDirectories);

var uids = xamlFiles
    .SelectMany(File.ReadLines)
    .SelectMany(line => Regex.Matches(line, "x:Uid=\"([^\"]+)\"").Select(match => match.Groups[1].Value))
    .Distinct(StringComparer.Ordinal)
    .ToArray();

foreach (var uid in uids)
{
    Assert.Contains(resourceNames, name => name.StartsWith(uid + ".", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Validate required code keys**

Add a static list of keys used by `AppResourceKeys` and required App-layer keys. Assert each key exists in `Resources.resw`.

- [ ] **Step 3: Validate placeholders**

For translated resource files that exist locally, assert each translated value preserves the placeholder set from `en-US`:

```csharp
private static string[] Placeholders(string value) =>
    Regex.Matches(value, "\\{\\d+[^}]*\\}")
        .Select(match => match.Value)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
```

- [ ] **Step 4: Add CI step**

The App test project already runs in CI, so no separate workflow is required if validation lives in `OpenHab.App.Tests`. If a Crowdin CLI dry-run is later desired, add it only after the Crowdin project ID and authentication model are approved.

- [ ] **Step 5: Run direct gate**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false
```

Expected: all direct tests and Release tray build pass.

- [ ] **Step 6: Commit**

```powershell
git add tests\OpenHab.App.Tests\Localization .github\workflows\ci.yml
git commit -s -m "test: validate localization resources"
```

## Task 8: Enable Crowdin Project Workflow

**Files:**
- Modify: `README.md`
- Modify: `CONTRIBUTING.md`
- Optional modify: `.github/workflows/crowdin.yml`

- [ ] **Step 1: Create or request the Crowdin project**

Use the openHAB Crowdin organization if available. Suggested project slug: `openhab-windows`.

- [ ] **Step 2: Configure GitHub integration**

Prefer the same operational model as openhab-android: Crowdin reads `crowdin.yml`, manages translated resource folders, and opens signed translation PRs. Confirm the commit author/sign-off identity before enabling automated PRs.

- [ ] **Step 3: Decide whether direct translation PRs are allowed**

Recommended policy: do not accept manual PRs against `src/OpenHab.Windows.Tray/Strings/*/Resources.resw` except `en-US`. Direct language edits should go through Crowdin to preserve contributor attribution, comments, and consistency.

- [ ] **Step 4: Optional Crowdin GitHub Action**

Only add an action if maintainers prefer CI-driven upload/download over Crowdin's GitHub integration. Required secrets would be `CROWDIN_PERSONAL_TOKEN` and project ID configuration. Do not add unactionable secrets to CI before ownership is decided.

- [ ] **Step 5: Commit documentation updates**

```powershell
git add README.md CONTRIBUTING.md .github\workflows
git commit -s -m "docs: document Crowdin translation workflow"
```

## Verification Summary

Before claiming the feature complete:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release --no-restore -p:UseSharedCompilation=false
```

For package/manifest changes, also run:

```powershell
.\build-package.ps1 -Configuration Release -Platform x64
```

Manual smoke check:

- Switch Windows display language to one downloaded Crowdin language.
- Launch the tray app.
- Verify tray context menu, flyout, main window navigation, settings pages, notifications page, shortcut dialogs, sitemap fallback buttons, and package display description use localized text.
- Verify openHAB-provided sitemap labels, item names, notification bodies, URLs, and diagnostic details are not machine-translated or altered.

## Baseline Evidence Collected While Writing This Plan

Worktree:

```text
D:\Source\Openhab\openhab-win-app\.worktrees\crowdin-translations-plan
```

Baseline:

- `dotnet test OpenHab.Windows.sln` first failed under sandboxed networking because NuGet restore could not reach `https://api.nuget.org/v3/index.json`.
- Retried with approved network access. Restore succeeded, but the solution command still exited non-zero because the SDK MSBuild path lacks `Microsoft.DesktopBridge.props` for `OpenHab.Windows.Package.wapproj`, matching the documented DesktopBridge caveat. The same run also hit transient output locks from `VBCSCompiler`.
- Direct project gate passed with shared compilation disabled:
  - `OpenHab.Core.Tests`: 79 passed.
  - `OpenHab.Sitemaps.Tests`: 39 passed.
  - `OpenHab.Rendering.Tests`: 101 passed.
  - `OpenHab.App.Tests`: 435 passed.

## Self-Review

- Spec coverage: Plan covers Crowdin config, WinUI resource format, Android comparison, XAML and code extraction, lower-layer localization, manifest localization, tests, docs, and maintainer workflow.
- Placeholder scan: No `TBD`/`TODO` placeholders remain. The only optional section is explicitly conditioned on maintainer choice for Crowdin GitHub Action ownership.
- Type consistency: The localizer interface, WinUI implementation, resource key constants, and tests use consistent names.
