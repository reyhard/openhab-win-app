# openHAB Windows UI Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first native Windows vertical slice: WinUI app shell, tray presence, minimal flyout host, in-memory sitemap rendering, and an in-memory settings surface for skin and endpoint mode.

**Architecture:** Keep behavior that can be tested without WinUI in a new `OpenHab.App` library: app settings, sample sitemap state, skin selection, endpoint mode selection, and render descriptor orchestration. Add a thin `OpenHab.Windows.Tray` Windows App SDK executable that owns WinUI windows, tray icon lifetime, and conversion from render descriptors to controls. Real openHAB connectivity remains limited to the existing `OpenHabHttpClient`; live event streams, persisted settings, secure credentials, packaging, and real sitemap JSON parsing are separate plans.

**Tech Stack:** .NET 10 SDK, C#, xUnit, Windows App SDK/WinUI 3, `System.Windows.Forms.NotifyIcon` for the first tray slice, existing `OpenHab.Core`, `OpenHab.Sitemaps`, and `OpenHab.Rendering` libraries.

---

## Scope Boundary

This plan intentionally builds a thin UI slice only.

Included:
- Windows app shell project.
- Tray icon with open/exit actions.
- Minimal flyout-style WinUI window.
- In-memory sample normalized sitemap rendered through the existing Basic and Windows 11 skins.
- In-memory settings page for skin selection and endpoint mode.
- Unit tests for app-state and descriptor orchestration.
- Build verification for the Windows executable.

Excluded:
- Secure credential storage.
- Persisted settings and migrations.
- Event stream client.
- Actual sitemap JSON REST payload parsing.
- WebView2/Main UI fallback surface.
- Native notifications and activation routing.
- Cloud notification polling.
- Real Windows device state collection and scheduling.
- Cached offline sitemap state.
- MSIX packaging, startup integration, signing, and release workflow.
- Windows Widgets.

## File Structure

- Create `src/OpenHab.App/OpenHab.App.csproj`: UI-independent app state and orchestration library.
- Create `src/OpenHab.App/Settings/AppSettings.cs`: immutable app settings model for selected skin, endpoint mode, and endpoint URIs.
- Create `src/OpenHab.App/Settings/AppSettingsController.cs`: in-memory settings mutations with validation.
- Create `src/OpenHab.App/Sitemaps/SampleSitemapFactory.cs`: deterministic sample normalized sitemap used by the first UI slice.
- Create `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`: maps current settings and sample sitemap into render descriptors.
- Create `tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj`: xUnit tests for the app library.
- Create `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`: settings validation and mutation tests.
- Create `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs`: skin selection/render descriptor tests.
- Create `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`: Windows App SDK/WinUI executable.
- Create `src/OpenHab.Windows.Tray/App.xaml`: WinUI app resources.
- Create `src/OpenHab.Windows.Tray/App.xaml.cs`: app startup and shutdown wiring.
- Create `src/OpenHab.Windows.Tray/MainWindow.xaml`: compact flyout host with sitemap/settings tabs.
- Create `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`: view wiring and command handlers.
- Create `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`: `NotifyIcon` lifetime and menu actions.
- Create `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`: descriptor-to-WinUI-control mapping.
- Modify `OpenHab.Windows.sln`: add `OpenHab.App`, `OpenHab.App.Tests`, and `OpenHab.Windows.Tray`.
- Modify `.gitignore`: ignore WinUI build output if generated paths are not already covered.
- Create `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`: completion status after implementation.

---

### Task 1: Add Testable App State Project

**Files:**
- Create: `src/OpenHab.App/OpenHab.App.csproj`
- Create: `src/OpenHab.App/Settings/AppSettings.cs`
- Create: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Create: `tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj`
- Create: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`
- Modify: `OpenHab.Windows.sln`

- [ ] **Step 1: Create project and test project**

Run:

```powershell
dotnet new classlib -n OpenHab.App -o src/OpenHab.App --framework net10.0
dotnet new xunit -n OpenHab.App.Tests -o tests/OpenHab.App.Tests --framework net10.0
dotnet add src/OpenHab.App/OpenHab.App.csproj reference src/OpenHab.Core/OpenHab.Core.csproj
dotnet add src/OpenHab.App/OpenHab.App.csproj reference src/OpenHab.Sitemaps/OpenHab.Sitemaps.csproj
dotnet add src/OpenHab.App/OpenHab.App.csproj reference src/OpenHab.Rendering/OpenHab.Rendering.csproj
dotnet add tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj reference src/OpenHab.App/OpenHab.App.csproj
dotnet sln OpenHab.Windows.sln add src/OpenHab.App/OpenHab.App.csproj
dotnet sln OpenHab.Windows.sln add tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
```

Expected: all commands exit with code `0`.

- [ ] **Step 2: Remove template files**

Delete:

```text
src/OpenHab.App/Class1.cs
tests/OpenHab.App.Tests/UnitTest1.cs
```

- [ ] **Step 3: Write failing settings tests**

Create `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Tests;

public sealed class AppSettingsControllerTests
{
    [Fact]
    public void DefaultsUseWindows11SkinAndAutomaticEndpointMode()
    {
        var controller = new AppSettingsController();

        Assert.Equal(SitemapSkinKind.Windows11, controller.Current.Skin);
        Assert.Equal(EndpointMode.Automatic, controller.Current.EndpointMode);
        Assert.Equal(new Uri("http://openhab.local:8080"), controller.Current.LocalEndpoint);
        Assert.Equal(new Uri("https://myopenhab.org"), controller.Current.CloudEndpoint);
    }

    [Fact]
    public void CanChangeSkinAndEndpointMode()
    {
        var controller = new AppSettingsController();

        controller.SetSkin(SitemapSkinKind.Basic);
        controller.SetEndpointMode(EndpointMode.CloudOnly);

        Assert.Equal(SitemapSkinKind.Basic, controller.Current.Skin);
        Assert.Equal(EndpointMode.CloudOnly, controller.Current.EndpointMode);
    }

    [Fact]
    public void RejectsRelativeEndpointUris()
    {
        var controller = new AppSettingsController();

        var exception = Assert.Throws<ArgumentException>(() =>
            controller.SetEndpoints(new Uri("/rest", UriKind.Relative), new Uri("https://myopenhab.org")));

        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run tests and verify failure**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
```

Expected: fail because `OpenHab.App.Settings` types do not exist.

- [ ] **Step 5: Implement settings model and controller**

Create `src/OpenHab.App/Settings/AppSettings.cs`:

```csharp
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Settings;

public sealed record AppSettings(
    SitemapSkinKind Skin,
    EndpointMode EndpointMode,
    Uri LocalEndpoint,
    Uri CloudEndpoint)
{
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        new Uri("http://openhab.local:8080"),
        new Uri("https://myopenhab.org"));
}
```

Create `src/OpenHab.App/Settings/AppSettingsController.cs`:

```csharp
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Settings;

public sealed class AppSettingsController
{
    public AppSettings Current { get; private set; } = AppSettings.Default;

    public void SetSkin(SitemapSkinKind skin)
    {
        Current = Current with { Skin = skin };
    }

    public void SetEndpointMode(EndpointMode endpointMode)
    {
        Current = Current with { EndpointMode = endpointMode };
    }

    public void SetEndpoints(Uri localEndpoint, Uri cloudEndpoint)
    {
        ArgumentNullException.ThrowIfNull(localEndpoint);
        ArgumentNullException.ThrowIfNull(cloudEndpoint);

        if (!localEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Local endpoint must be an absolute URI.", nameof(localEndpoint));
        }

        if (!cloudEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Cloud endpoint must be an absolute URI.", nameof(cloudEndpoint));
        }

        Current = Current with
        {
            LocalEndpoint = localEndpoint,
            CloudEndpoint = cloudEndpoint
        };
    }
}
```

- [ ] **Step 6: Run tests and verify pass**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
```

Expected: all `OpenHab.App.Tests` pass.

- [ ] **Step 7: Commit app settings foundation**

Run:

```powershell
git add OpenHab.Windows.sln src/OpenHab.App tests/OpenHab.App.Tests
git commit -m "feat: add app settings state"
```

---

### Task 2: Add Sample Sitemap Render Controller

**Files:**
- Create: `src/OpenHab.App/Sitemaps/SampleSitemapFactory.cs`
- Create: `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`
- Create: `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs`

- [ ] **Step 1: Write failing render controller tests**

Create `tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.App.Tests;

public sealed class SitemapRenderControllerTests
{
    [Fact]
    public void BuildsWindows11DescriptorByDefault()
    {
        var settings = new AppSettingsController();
        var controller = new SitemapRenderController(settings);

        var descriptor = controller.BuildCurrentDescriptor();

        Assert.Equal(SitemapSkinKind.Windows11, descriptor.Skin);
        Assert.Equal("home", descriptor.PageId);
        Assert.Equal("Home", descriptor.Title);
        Assert.Contains(descriptor.Rows, row => row.Control == RenderControlKind.Toggle && row.Action == RenderActionKind.SendCommand);
        Assert.Contains(descriptor.Rows, row => row.Control == RenderControlKind.Slider && row.Action == RenderActionKind.SendCommand);
    }

    [Fact]
    public void UsesBasicSkinWhenSelected()
    {
        var settings = new AppSettingsController();
        settings.SetSkin(SitemapSkinKind.Basic);
        var controller = new SitemapRenderController(settings);

        var descriptor = controller.BuildCurrentDescriptor();

        Assert.Equal(SitemapSkinKind.Basic, descriptor.Skin);
        Assert.All(descriptor.Rows, row => Assert.Equal(RenderDensity.Compact, row.Density));
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
```

Expected: fail because `OpenHab.App.Sitemaps` types do not exist.

- [ ] **Step 3: Implement sample sitemap factory**

Create `src/OpenHab.App/Sitemaps/SampleSitemapFactory.cs`:

```csharp
using OpenHab.Sitemaps.Models;

namespace OpenHab.App.Sitemaps;

public static class SampleSitemapFactory
{
    public static NormalizedSitemapPage CreateHomePage()
    {
        return new NormalizedSitemapPage(
            "home",
            "Home",
            new[]
            {
                new NormalizedSitemapWidget(
                    "Living Room Light",
                    SitemapWidgetType.Switch,
                    "LivingRoom_Light",
                    "OFF",
                    new[] { new SitemapMapping("ON", "On"), new SitemapMapping("OFF", "Off") },
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>()),
                new NormalizedSitemapWidget(
                    "Hallway Temperature",
                    SitemapWidgetType.Text,
                    "Hallway_Temperature",
                    "21.4 C",
                    Array.Empty<SitemapMapping>(),
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>()),
                new NormalizedSitemapWidget(
                    "Kitchen Dimmer",
                    SitemapWidgetType.Slider,
                    "Kitchen_Dimmer",
                    "42",
                    Array.Empty<SitemapMapping>(),
                    CanNavigate: false,
                    RequiresFallback: false,
                    SitemapFallbackKind.None,
                    Array.Empty<SitemapPage>())
            });
    }
}
```

- [ ] **Step 4: Implement render controller**

Create `src/OpenHab.App/Sitemaps/SitemapRenderController.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.Rendering.Descriptors;
using OpenHab.Rendering.Skins;

namespace OpenHab.App.Sitemaps;

public sealed class SitemapRenderController
{
    private readonly AppSettingsController settingsController;

    public SitemapRenderController(AppSettingsController settingsController)
    {
        this.settingsController = settingsController;
    }

    public SitemapRenderDescriptor BuildCurrentDescriptor()
    {
        var page = SampleSitemapFactory.CreateHomePage();
        ISitemapSkin skin = settingsController.Current.Skin switch
        {
            SitemapSkinKind.Basic => new BasicSitemapSkin(),
            SitemapSkinKind.Windows11 => new Windows11SitemapSkin(),
            _ => throw new InvalidOperationException($"Unsupported sitemap skin '{settingsController.Current.Skin}'.")
        };

        return skin.Render(page);
    }
}
```

- [ ] **Step 5: Run tests and verify pass**

Run:

```powershell
dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj
```

Expected: all `OpenHab.App.Tests` pass.

- [ ] **Step 6: Commit sample render controller**

Run:

```powershell
git add src/OpenHab.App/Sitemaps tests/OpenHab.App.Tests/SitemapRenderControllerTests.cs
git commit -m "feat: add sample sitemap render controller"
```

---

### Task 3: Add Windows App SDK Shell Project

**Files:**
- Create: `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`
- Create: `src/OpenHab.Windows.Tray/App.xaml`
- Create: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Create: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Create: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`
- Modify: `OpenHab.Windows.sln`

- [ ] **Step 1: Create Windows executable project file**

Create `src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWinUI>true</UseWinUI>
    <UseWindowsForms>true</UseWindowsForms>
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260317003" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\OpenHab.App\OpenHab.App.csproj" />
    <ProjectReference Include="..\OpenHab.Core\OpenHab.Core.csproj" />
    <ProjectReference Include="..\OpenHab.Rendering\OpenHab.Rendering.csproj" />
  </ItemGroup>
</Project>
```

This pins the stable Windows App SDK 1.8.6 package listed by Microsoft on 2026-03-31. If restore fails because the package is unavailable in the local NuGet cache, stop and verify the currently installed/restorable Windows App SDK version before changing the version.

- [ ] **Step 2: Add project to solution**

Run:

```powershell
dotnet sln OpenHab.Windows.sln add src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
```

Expected: command exits with code `0`.

- [ ] **Step 3: Add WinUI app resource file**

Create `src/OpenHab.Windows.Tray/App.xaml`:

```xml
<Application
    x:Class="OpenHab.Windows.Tray.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 4: Add app startup wiring**

Create `src/OpenHab.Windows.Tray/App.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Windows.Tray.Tray;

namespace OpenHab.Windows.Tray;

public partial class App : Application
{
    private MainWindow? window;
    private TrayIconService? trayIcon;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settingsController = new AppSettingsController();
        var renderController = new SitemapRenderController(settingsController);

        window = new MainWindow(settingsController, renderController);
        trayIcon = new TrayIconService(
            showWindow: () =>
            {
                window.Activate();
                window.Refresh();
            },
            exitApplication: () =>
            {
                trayIcon?.Dispose();
                Exit();
            });

        window.Activate();
    }
}
```

- [ ] **Step 5: Add minimal window XAML**

Create `src/OpenHab.Windows.Tray/MainWindow.xaml`:

```xml
<Window
    x:Class="OpenHab.Windows.Tray.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="openHAB">
    <Grid Margin="16" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TextBlock x:Name="TitleText"
                   FontSize="20"
                   FontWeight="SemiBold" />

        <TabView Grid.Row="1"
                 IsAddTabButtonVisible="False">
            <TabViewItem Header="Sitemap">
                <ScrollViewer>
                    <StackPanel x:Name="SitemapRows" Spacing="8" />
                </ScrollViewer>
            </TabViewItem>
            <TabViewItem Header="Settings">
                <StackPanel Spacing="12" MaxWidth="420">
                    <ComboBox x:Name="SkinCombo"
                              Header="Skin"
                              SelectionChanged="SkinCombo_SelectionChanged" />
                    <ComboBox x:Name="EndpointModeCombo"
                              Header="Endpoint mode"
                              SelectionChanged="EndpointModeCombo_SelectionChanged" />
                    <TextBox x:Name="LocalEndpointText"
                             Header="Local endpoint"
                             LostFocus="EndpointText_LostFocus" />
                    <TextBox x:Name="CloudEndpointText"
                             Header="Cloud endpoint"
                             LostFocus="EndpointText_LostFocus" />
                </StackPanel>
            </TabViewItem>
        </TabView>
    </Grid>
</Window>
```

- [ ] **Step 6: Add initial window code-behind**

Create `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.Windows.Tray;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRenderController renderController;
    private bool isRefreshing;

    public MainWindow(AppSettingsController settingsController, SitemapRenderController renderController)
    {
        this.settingsController = settingsController;
        this.renderController = renderController;

        InitializeComponent();
        InitializeSettingsControls();
        Refresh();
    }

    public void Refresh()
    {
        isRefreshing = true;
        var descriptor = renderController.BuildCurrentDescriptor();
        TitleText.Text = descriptor.Title;
        SitemapRows.Children.Clear();

        foreach (var row in descriptor.Rows)
        {
            SitemapRows.Children.Add(SitemapControlFactory.Create(row));
        }

        SkinCombo.SelectedItem = settingsController.Current.Skin;
        EndpointModeCombo.SelectedItem = settingsController.Current.EndpointMode;
        LocalEndpointText.Text = settingsController.Current.LocalEndpoint.ToString();
        CloudEndpointText.Text = settingsController.Current.CloudEndpoint.ToString();
        isRefreshing = false;
    }

    private void InitializeSettingsControls()
    {
        SkinCombo.ItemsSource = Enum.GetValues<SitemapSkinKind>();
        EndpointModeCombo.ItemsSource = Enum.GetValues<EndpointMode>();
    }

    private void SkinCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || SkinCombo.SelectedItem is not SitemapSkinKind skin)
        {
            return;
        }

        settingsController.SetSkin(skin);
        Refresh();
    }

    private void EndpointModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshing || EndpointModeCombo.SelectedItem is not EndpointMode endpointMode)
        {
            return;
        }

        settingsController.SetEndpointMode(endpointMode);
    }

    private void EndpointText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(LocalEndpointText.Text, UriKind.Absolute, out var localEndpoint)
            && Uri.TryCreate(CloudEndpointText.Text, UriKind.Absolute, out var cloudEndpoint))
        {
            settingsController.SetEndpoints(localEndpoint, cloudEndpoint);
        }
    }
}
```

- [ ] **Step 7: Build and verify expected missing helper failure**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
```

Expected: fail because `TrayIconService` and `SitemapControlFactory` do not exist yet.

- [ ] **Step 8: Commit shell project**

Run:

```powershell
git add OpenHab.Windows.sln src/OpenHab.Windows.Tray
git commit -m "feat: add winui tray shell project"
```

---

### Task 4: Add Tray Icon Service

**Files:**
- Create: `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`

- [ ] **Step 1: Implement tray icon service**

Create `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;

    public TrayIconService(Action showWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", image: null, (_, _) => showWindow());
        menu.Items.Add("Exit", image: null, (_, _) => exitApplication());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "openHAB",
            ContextMenuStrip = menu,
            Visible = true
        };

        notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
```

- [ ] **Step 2: Build and verify remaining missing helper failure**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
```

Expected: fail only because `SitemapControlFactory` does not exist.

- [ ] **Step 3: Commit tray service**

Run:

```powershell
git add src/OpenHab.Windows.Tray/Tray/TrayIconService.cs
git commit -m "feat: add tray icon service"
```

---

### Task 5: Render Descriptors Into WinUI Controls

**Files:**
- Create: `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`

- [ ] **Step 1: Implement descriptor control factory**

Create `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenHab.Rendering.Descriptors;

namespace OpenHab.Windows.Tray.Rendering;

public static class SitemapControlFactory
{
    public static FrameworkElement Create(SitemapRowDescriptor row)
    {
        return row.Control switch
        {
            RenderControlKind.Toggle => CreateToggle(row),
            RenderControlKind.Slider => CreateSlider(row),
            RenderControlKind.Selection => CreateSelection(row),
            RenderControlKind.Fallback => CreateFallback(row),
            _ => CreateText(row)
        };
    }

    private static FrameworkElement CreateText(SitemapRowDescriptor row)
    {
        return CreateRow(row.Label, row.State ?? string.Empty);
    }

    private static FrameworkElement CreateToggle(SitemapRowDescriptor row)
    {
        var toggle = new ToggleSwitch
        {
            Header = row.Label,
            IsOn = string.Equals(row.State, "ON", StringComparison.OrdinalIgnoreCase)
        };

        return toggle;
    }

    private static FrameworkElement CreateSlider(SitemapRowDescriptor row)
    {
        var value = double.TryParse(row.State, out var parsed) ? parsed : 0;
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = row.Label },
                new Slider { Minimum = 0, Maximum = 100, Value = value }
            }
        };
    }

    private static FrameworkElement CreateSelection(SitemapRowDescriptor row)
    {
        return new Button
        {
            Content = string.IsNullOrWhiteSpace(row.State) ? row.Label : $"{row.Label}: {row.State}",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static FrameworkElement CreateFallback(SitemapRowDescriptor row)
    {
        return new Button
        {
            Content = row.Label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false
        };
    }

    private static FrameworkElement CreateRow(string label, string state)
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = state, VerticalAlignment = VerticalAlignment.Center }
            }
        };
    }
}
```

- [ ] **Step 2: Fix grid column assignment**

Modify `CreateRow` in `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs` so the second `TextBlock` is assigned to column `1`:

```csharp
private static FrameworkElement CreateRow(string label, string state)
{
    var grid = new Grid
    {
        ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            new ColumnDefinition { Width = GridLength.Auto }
        }
    };

    grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

    var stateText = new TextBlock { Text = state, VerticalAlignment = VerticalAlignment.Center };
    Grid.SetColumn(stateText, 1);
    grid.Children.Add(stateText);

    return grid;
}
```

- [ ] **Step 3: Build the Windows app project**

Run:

```powershell
dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj
```

Expected: build succeeds. If it fails on Windows App SDK package restore, stop and resolve the installed/restorable Windows App SDK version before editing app code.

- [ ] **Step 4: Commit rendering control factory**

Run:

```powershell
git add src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs
git commit -m "feat: render sitemap descriptors in winui"
```

---

### Task 6: Full Solution Verification And Status Update

**Files:**
- Modify: `.gitignore`
- Create: `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`

- [ ] **Step 1: Run full test suite**

Run:

```powershell
dotnet test OpenHab.Windows.sln
```

Expected: all test projects pass, including `OpenHab.App.Tests`.

- [ ] **Step 2: Run release build**

Run:

```powershell
dotnet build OpenHab.Windows.sln --configuration Release
```

Expected: release build succeeds with no warnings introduced by this plan.

- [ ] **Step 3: Update generated-output ignores only if needed**

Run:

```powershell
git status --short --ignored
```

Expected: build outputs remain ignored. If WinUI generated directories appear as untracked files, add only those generated paths to `.gitignore`; do not ignore source files, project files, XAML files, or docs.

- [ ] **Step 4: Write completion status**

Create `docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md`:

```markdown
# openHAB Windows UI Slice Status

Date: 2026-05-05

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Foundation status: `docs/superpowers/status/2026-05-05-openhab-windows-foundation-status.md`
- UI slice plan: `docs/superpowers/plans/2026-05-05-openhab-windows-ui-slice.md`

## Completed

- Added `OpenHab.App` for UI-independent app settings and sample sitemap rendering state.
- Added app tests for skin selection, endpoint mode selection, endpoint URI validation, and descriptor generation.
- Added `OpenHab.Windows.Tray` Windows App SDK shell.
- Added a tray icon with open and exit actions.
- Added a compact WinUI flyout host with sitemap and settings tabs.
- Rendered the in-memory normalized sitemap through the existing Basic and Windows 11 skin descriptors.

## Verification

- `dotnet test OpenHab.Windows.sln`: executor must record the exact pass/fail counts from the completed run before committing this status file.
- `dotnet build OpenHab.Windows.sln --configuration Release`: executor must record the exact warning/error counts from the completed run before committing this status file.

## Still Out Of Scope

- Secure credential storage.
- Persisted settings and migrations.
- Real sitemap JSON parsing.
- Event stream live updates.
- WebView2 fallback surface.
- Native notifications.
- MSIX packaging and signing.
```

Do not commit this status file until the two verification lines contain the actual command outcomes.

- [ ] **Step 5: Commit verification status**

Run:

```powershell
git add .gitignore docs/superpowers/status/2026-05-05-openhab-windows-ui-slice-status.md
git commit -m "docs: record ui slice status"
```

---

## Self-Review Checklist

- The plan builds only the next thin UI slice suggested by the foundation status.
- The plan adds a testable app-state layer before adding WinUI code.
- The plan does not include live openHAB events, settings persistence, secure credential storage, packaging, or notification routing.
- Every implementation task has exact files, commands, and expected results.
- Verification requires `dotnet test OpenHab.Windows.sln` and `dotnet build OpenHab.Windows.sln --configuration Release`.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-05-openhab-windows-ui-slice.md`.

Two execution options:

1. **Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.
