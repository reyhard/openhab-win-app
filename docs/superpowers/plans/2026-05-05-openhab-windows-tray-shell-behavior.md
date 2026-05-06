# openHAB Windows Tray Shell Behavior Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows app behave like a real tray-first background app: closing the main window hides it instead of exiting, clicking the tray icon toggles a compact sitemap flyout, and the app keeps a separate larger main window path aligned with the mockup.

**Architecture:** Keep tray lifecycle rules testable in `OpenHab.App`, and keep WinUI / NotifyIcon specifics in `OpenHab.Windows.Tray`. Split the current single window into a compact `FlyoutWindow` for tray interaction and a larger `MainWindow` for the full app surface, both backed by the existing sitemap runtime and settings controller.

**Tech Stack:** .NET 10 SDK, C#, WinUI / Windows App SDK, `System.Windows.Forms.NotifyIcon`, xUnit.

---

## Why This Plan Exists

The current spec/status/docs leave a real behavior gap:

- The app currently opens a normal `MainWindow` on launch and uses the tray icon only for a context menu and double-click open.
- Clicking the window close button currently exits the visible shell instead of minimizing to background behavior.
- There is no single-click tray toggle for a compact sitemap flyout.
- The current UI is a single generic tabbed window, not the two-surface model shown in `.docs/mockup.png`:
  - compact flyout for quick sitemap access
  - separate larger main app window for expanded use

This plan is intentionally limited to tray shell behavior and flyout/main-window separation. It does not add live event streaming, subpage navigation, offline cache persistence, or a full dashboard implementation.

## Scope Boundary

Included:

- Background-app lifecycle: close hides to tray; explicit exit still terminates.
- Primary tray icon click toggles a compact flyout window.
- Secondary tray interaction still exposes context menu with `Open main window`, `Open flyout`, and `Exit`.
- A dedicated `FlyoutWindow` that renders the existing sitemap runtime in a compact surface.
- A dedicated `MainWindow` for the larger app path.
- Testable app-layer shell state controller covering launch, close, tray click, notification activation, and exit intent.
- Status doc update after implementation.

Excluded:

- Full dashboard cards/widgets from the mockup.
- WebView/Main UI fallback implementation.
- New sitemap features such as search, subpage navigation, or live updates.
- MSIX/startup integration.
- Real tray-anchor rect detection beyond a pragmatic cursor-based first implementation.

## File Structure

### App-layer shell orchestration

- Create `src/OpenHab.App/Tray/TrayShellSurface.cs`: enum describing `Flyout` vs `MainWindow`.
- Create `src/OpenHab.App/Tray/TrayShellState.cs`: immutable state for visible surface, background mode, and pending refresh.
- Create `src/OpenHab.App/Tray/TrayShellController.cs`: testable state machine for launch/close/tray/notification/exit behavior.
- Create `tests/OpenHab.App.Tests/Tray/TrayShellControllerTests.cs`: unit tests for background lifecycle and tray toggling.

### Windows tray shell

- Create `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`: compact flyout surface for sitemap-first interaction.
- Create `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`: flyout-specific binding and runtime refresh behavior.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`: turn the current shell into the larger main window path.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`: remove flyout-only concerns and wire close/hide behavior through the shell controller.
- Modify `src/OpenHab.Windows.Tray/App.xaml.cs`: create both windows, route lifecycle events, and keep app alive when windows are hidden.
- Modify `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`: add primary-click toggle and explicit commands for flyout vs main window.
- Create `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPlacement.cs`: simple placement model for flyout anchor coordinates.
- Create `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs`: place flyout near the tray based on current pointer/display area.

### Completion docs

- Create `docs/superpowers/status/2026-05-05-openhab-windows-tray-shell-behavior-status.md`: completion/verification record for this slice.

---

### Task 1: Define The Tray Shell State Machine In `OpenHab.App`

**Files:**
- Create: `src/OpenHab.App/Tray/TrayShellSurface.cs`
- Create: `src/OpenHab.App/Tray/TrayShellState.cs`
- Create: `src/OpenHab.App/Tray/TrayShellController.cs`
- Create: `tests/OpenHab.App.Tests/Tray/TrayShellControllerTests.cs`

- [ ] **Step 1: Write the failing tray shell tests**

Create `tests/OpenHab.App.Tests/Tray/TrayShellControllerTests.cs`:

```csharp
using OpenHab.App.Tray;

namespace OpenHab.App.Tests.Tray;

public sealed class TrayShellControllerTests
{
    [Fact]
    public void LaunchStartsInBackgroundWithFlyoutHidden()
    {
        var controller = new TrayShellController();

        controller.HandleLaunch();

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
        Assert.False(controller.Current.ShouldExitProcess);
    }

    [Fact]
    public void PrimaryTrayClickShowsFlyoutWhenHidden()
    {
        var controller = new TrayShellController();
        controller.HandleLaunch();

        controller.HandlePrimaryTrayClick();

        Assert.Equal(TrayShellSurface.Flyout, controller.Current.VisibleSurface);
        Assert.True(controller.Current.PendingRefresh);
    }

    [Fact]
    public void PrimaryTrayClickHidesFlyoutWhenAlreadyVisible()
    {
        var controller = new TrayShellController();
        controller.HandleLaunch();
        controller.HandlePrimaryTrayClick();

        controller.HandlePrimaryTrayClick();

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
    }

    [Fact]
    public void CloseRequestFromMainWindowHidesToTrayInsteadOfExiting()
    {
        var controller = new TrayShellController();
        controller.HandleLaunch();
        controller.HandleOpenMainWindow();

        controller.HandleWindowCloseRequested(TrayShellSurface.MainWindow);

        Assert.Equal(TrayShellSurface.None, controller.Current.VisibleSurface);
        Assert.True(controller.Current.IsRunningInBackground);
        Assert.False(controller.Current.ShouldExitProcess);
    }

    [Fact]
    public void NotificationActivationOpensMainWindow()
    {
        var controller = new TrayShellController();
        controller.HandleLaunch();

        controller.HandleNotificationActivated();

        Assert.Equal(TrayShellSurface.MainWindow, controller.Current.VisibleSurface);
        Assert.True(controller.Current.PendingRefresh);
    }

    [Fact]
    public void ExitRequestSetsShouldExitProcess()
    {
        var controller = new TrayShellController();
        controller.HandleLaunch();

        controller.HandleExitRequested();

        Assert.True(controller.Current.ShouldExitProcess);
    }
}
```

- [ ] **Step 2: Run the tray shell tests and verify failure**

Run: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter TrayShellControllerTests`

Expected: FAIL because `OpenHab.App.Tray` types do not exist.

- [ ] **Step 3: Implement the tray shell state machine**

Create `src/OpenHab.App/Tray/TrayShellSurface.cs`:

```csharp
namespace OpenHab.App.Tray;

public enum TrayShellSurface
{
    None = 0,
    Flyout = 1,
    MainWindow = 2
}
```

Create `src/OpenHab.App/Tray/TrayShellState.cs`:

```csharp
namespace OpenHab.App.Tray;

public sealed record TrayShellState(
    TrayShellSurface VisibleSurface,
    bool IsRunningInBackground,
    bool PendingRefresh,
    bool ShouldExitProcess)
{
    public static TrayShellState Initial { get; } = new(
        TrayShellSurface.None,
        IsRunningInBackground: true,
        PendingRefresh: false,
        ShouldExitProcess: false);
}
```

Create `src/OpenHab.App/Tray/TrayShellController.cs`:

```csharp
namespace OpenHab.App.Tray;

public sealed class TrayShellController
{
    public TrayShellState Current { get; private set; } = TrayShellState.Initial;

    public void HandleLaunch()
    {
        Current = TrayShellState.Initial;
    }

    public void HandlePrimaryTrayClick()
    {
        Current = Current.VisibleSurface == TrayShellSurface.Flyout
            ? Current with
            {
                VisibleSurface = TrayShellSurface.None,
                IsRunningInBackground = true,
                PendingRefresh = false
            }
            : Current with
            {
                VisibleSurface = TrayShellSurface.Flyout,
                IsRunningInBackground = false,
                PendingRefresh = true
            };
    }

    public void HandleOpenMainWindow()
    {
        Current = Current with
        {
            VisibleSurface = TrayShellSurface.MainWindow,
            IsRunningInBackground = false,
            PendingRefresh = true
        };
    }

    public void HandleNotificationActivated()
    {
        HandleOpenMainWindow();
    }

    public void HandleWindowCloseRequested(TrayShellSurface surface)
    {
        if (Current.ShouldExitProcess)
        {
            return;
        }

        Current = Current with
        {
            VisibleSurface = TrayShellSurface.None,
            IsRunningInBackground = true,
            PendingRefresh = false
        };
    }

    public void HandleRefreshCompleted()
    {
        Current = Current with { PendingRefresh = false };
    }

    public void HandleExitRequested()
    {
        Current = Current with
        {
            VisibleSurface = TrayShellSurface.None,
            IsRunningInBackground = false,
            PendingRefresh = false,
            ShouldExitProcess = true
        };
    }
}
```

- [ ] **Step 4: Run the tray shell tests and verify they pass**

Run: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter TrayShellControllerTests`

Expected: PASS with all 6 tests green.

- [ ] **Step 5: Commit the state-machine slice**

```powershell
git add src/OpenHab.App/Tray tests/OpenHab.App.Tests/Tray
git commit -m "feat: add tray shell state controller"
```

---

### Task 2: Add Tray Icon Commands For Flyout Toggle And Main Window Open

**Files:**
- Modify: `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`

- [ ] **Step 1: Write the intended tray interaction contract into the service**

Modify `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs` so the constructor becomes:

```csharp
public TrayIconService(
    Action toggleFlyout,
    Action openMainWindow,
    Action exitApplication)
```

and the tray interactions become:

```csharp
contextMenu = new ContextMenuStrip();
contextMenu.Items.Add("Open flyout", image: null, (_, _) => toggleFlyout());
contextMenu.Items.Add("Open main window", image: null, (_, _) => openMainWindow());
contextMenu.Items.Add("Exit", image: null, (_, _) => exitApplication());

notifyIcon = new NotifyIcon
{
    Icon = SystemIcons.Application,
    Text = "openHAB",
    ContextMenuStrip = contextMenu,
    Visible = true
};

mouseClickHandler = (_, args) =>
{
    if (args.Button == MouseButtons.Left)
    {
        toggleFlyout();
    }
};
notifyIcon.MouseClick += mouseClickHandler;
```

Also change disposal to detach `MouseClick` instead of `DoubleClick`.

- [ ] **Step 2: Build the tray project and verify the constructor mismatch failure**

Run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`

Expected: FAIL because `App.xaml.cs` still constructs `TrayIconService` with the old delegate list.

- [ ] **Step 3: Apply the service changes**

Use this full implementation for `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs`:

```csharp
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip contextMenu;
    private readonly MouseEventHandler mouseClickHandler;
    private int isDisposed;

    public TrayIconService(
        Action toggleFlyout,
        Action openMainWindow,
        Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(toggleFlyout);
        ArgumentNullException.ThrowIfNull(openMainWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open flyout", image: null, (_, _) => toggleFlyout());
        contextMenu.Items.Add("Open main window", image: null, (_, _) => openMainWindow());
        contextMenu.Items.Add("Exit", image: null, (_, _) => exitApplication());

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "openHAB",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        mouseClickHandler = (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                toggleFlyout();
            }
        };

        notifyIcon.MouseClick += mouseClickHandler;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        notifyIcon.MouseClick -= mouseClickHandler;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip = null;
        notifyIcon.Dispose();
        contextMenu.Dispose();
    }
}
```

- [ ] **Step 4: Rebuild and verify the project now fails only in `App.xaml.cs`**

Run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`

Expected: FAIL with constructor mismatch in `App.xaml.cs`, confirming the tray service change is in place.

- [ ] **Step 5: Commit the tray-icon interaction slice**

```powershell
git add src/OpenHab.Windows.Tray/Tray/TrayIconService.cs
git commit -m "feat: add tray icon flyout toggle interactions"
```

---

### Task 3: Split The Compact Flyout From The Main Window

**Files:**
- Create: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`
- Create: `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Create the compact flyout XAML from the current sitemap-first shell**

Create `src/OpenHab.Windows.Tray/FlyoutWindow.xaml`:

```xml
<Window
    x:Class="OpenHab.Windows.Tray.FlyoutWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="openHAB Flyout">
    <Grid Margin="16" RowSpacing="12" Width="360">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Grid ColumnSpacing="8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel Spacing="4">
                <TextBlock x:Name="TitleText"
                           FontSize="18"
                           FontWeight="SemiBold"
                           Text="openHAB - Home" />
                <TextBlock x:Name="StatusText"
                           Opacity="0.8" />
            </StackPanel>
            <Button Grid.Column="1"
                    Content="Open"
                    Click="OpenMainWindowButton_Click" />
        </Grid>

        <TextBox Grid.Row="1"
                 x:Name="SearchText"
                 Header="Search"
                 PlaceholderText="Search items, rooms, devices..." />

        <ScrollViewer Grid.Row="2">
            <StackPanel x:Name="SitemapRows" Spacing="8" />
        </ScrollViewer>

        <Grid Grid.Row="3" ColumnSpacing="8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="ConnectionText"
                       VerticalAlignment="Center" />
            <Button Grid.Column="1"
                    Content="Refresh"
                    Click="RefreshButton_Click" />
            <Button Grid.Column="2"
                    Content="Settings"
                    Click="SettingsButton_Click" />
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the flyout code-behind**

Create `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using OpenHab.App.Runtime;
using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.Windows.Tray;

public sealed partial class FlyoutWindow : Window
{
    private readonly SitemapRuntimeController runtimeController;
    private readonly Action openMainWindow;
    private bool isRefreshing;

    public FlyoutWindow(
        SitemapRuntimeController runtimeController,
        Action openMainWindow)
    {
        this.runtimeController = runtimeController;
        this.openMainWindow = openMainWindow;

        InitializeComponent();
    }

    public async Task RefreshRuntimeAsync()
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            await runtimeController.RefreshAsync(CancellationToken.None);
            RefreshBindings();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            isRefreshing = false;
        }
    }

    public void RefreshBindings()
    {
        var snapshot = runtimeController.Current;
        TitleText.Text = snapshot.Descriptor?.Title ?? "openHAB";
        StatusText.Text = snapshot.StatusText;
        ConnectionText.Text = snapshot.ConnectionState.ToString();

        SitemapRows.Children.Clear();
        var rows = snapshot.Descriptor?.Rows;
        if (rows is null)
        {
            return;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            SitemapRows.Children.Add(SitemapControlFactory.Create(row, null));
        }
    }

    private void OpenMainWindowButton_Click(object sender, RoutedEventArgs e)
    {
        openMainWindow();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRuntimeAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        openMainWindow();
    }
}
```

- [ ] **Step 3: Convert the existing `MainWindow` into the larger app surface**

Modify `src/OpenHab.Windows.Tray/MainWindow.xaml` to remove the tabbed flyout structure and leave a larger shell:

```xml
<Window
    x:Class="OpenHab.Windows.Tray.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="openHAB">
    <Grid Margin="20" ColumnSpacing="20">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="180" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <StackPanel Spacing="12">
            <TextBlock Text="openHAB"
                       FontSize="22"
                       FontWeight="SemiBold" />
            <Button Content="Overview" />
            <Button Content="Sitemap" />
            <Button Content="Settings" />
        </StackPanel>

        <Grid Grid.Column="1" RowSpacing="12">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>

            <StackPanel Spacing="4">
                <TextBlock x:Name="TitleText"
                           FontSize="24"
                           FontWeight="SemiBold" />
                <TextBlock x:Name="StatusText"
                           Opacity="0.8" />
            </StackPanel>

            <Grid Grid.Row="1" ColumnSpacing="8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBox x:Name="SitemapNameText"
                         Header="Sitemap name"
                         Width="260"
                         LostFocus="SitemapNameText_LostFocus" />
                <Button Grid.Column="1"
                        Margin="0,24,0,0"
                        Content="Load"
                        Click="LoadButton_Click" />
                <Button Grid.Column="2"
                        Margin="0,24,0,0"
                        Content="Refresh"
                        Click="RefreshButton_Click" />
            </Grid>

            <Grid Grid.Row="2" RowSpacing="12">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="*" />
                </Grid.RowDefinitions>

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
                    <PasswordBox x:Name="LocalTokenBox"
                                 Header="Local API token"
                                 PlaceholderText="Enter token (optional)"
                                 LostFocus="TokenBox_LostFocus"
                                 Tag="Local" />
                    <PasswordBox x:Name="CloudTokenBox"
                                 Header="Cloud API token"
                                 PlaceholderText="Enter token (optional)"
                                 LostFocus="TokenBox_LostFocus"
                                 Tag="Cloud" />
                </StackPanel>

                <ScrollViewer Grid.Row="1">
                    <StackPanel x:Name="SitemapRows" Spacing="8" />
                </ScrollViewer>
            </Grid>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 4: Keep the existing main-window code-behind, but remove any flyout-specific assumptions**

In `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`, keep the existing settings/runtime logic and add this constructor overload point for close interception later:

```csharp
private readonly Action requestHideToTray;

public MainWindow(
    AppSettingsController settingsController,
    SitemapRuntimeController runtimeController,
    Action requestHideToTray)
{
    this.settingsController = settingsController;
    this.runtimeController = runtimeController;
    this.requestHideToTray = requestHideToTray;

    InitializeComponent();
    InitializeSettingsControls();
    RefreshSettingsBindings();
    _ = LoadRuntimeAsync();
}
```

Do not yet add the close event in this task; the next task wires both windows through the shell controller.

- [ ] **Step 5: Build the tray project and verify the split compiles only after `App.xaml.cs` is updated**

Run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`

Expected: FAIL because `App.xaml.cs` still only knows about one window and the old constructor signatures.

---

### Task 4: Wire Launch, Close, Notification, And Tray Click Through The Shell Controller

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Refactor `App.xaml.cs` to own both windows and the shell controller**

Replace the top-level fields in `src/OpenHab.Windows.Tray/App.xaml.cs` with:

```csharp
private MainWindow? mainWindow;
private FlyoutWindow? flyoutWindow;
private TrayIconService? trayIcon;
private DispatcherQueue? uiDispatcherQueue;
private HttpClient? httpClient;
private NotificationPoller? notificationPoller;
private readonly TrayShellController shellController = new();
private int isShuttingDown;
```

Then update `OnLaunched` so the window creation path becomes:

```csharp
var settingsController = new AppSettingsController(credentialStore);
var renderController = new SitemapRenderController(settingsController);
httpClient = new HttpClient();
var runtimeController = new SitemapRuntimeController(
    settingsController,
    renderController,
    (transportKind, endpoint) =>
    {
        string? token = null;
        try { token = settingsController.GetApiTokenAsync(transportKind, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { }
        return new OpenHabHttpClient(httpClient, endpoint, apiToken: token);
    });

mainWindow = new MainWindow(
    settingsController,
    runtimeController,
    requestHideToTray: () => HandleWindowCloseRequested(TrayShellSurface.MainWindow));

flyoutWindow = new FlyoutWindow(
    runtimeController,
    openMainWindow: HandleOpenMainWindow);

trayIcon = new TrayIconService(
    toggleFlyout: HandlePrimaryTrayClick,
    openMainWindow: HandleOpenMainWindow,
    exitApplication: HandleExitRequested);

shellController.HandleLaunch();
ApplyShellStateAsync().GetAwaiter().GetResult();

_ = InitializeAsync(settingsController);
StartNotificationPolling(settingsController);
```

- [ ] **Step 2: Add the shell transition helpers**

Add these methods to `src/OpenHab.Windows.Tray/App.xaml.cs`:

```csharp
private void HandlePrimaryTrayClick()
{
    shellController.HandlePrimaryTrayClick();
    _ = ApplyShellStateAsync();
}

private void HandleOpenMainWindow()
{
    shellController.HandleOpenMainWindow();
    _ = ApplyShellStateAsync();
}

private void HandleWindowCloseRequested(TrayShellSurface surface)
{
    shellController.HandleWindowCloseRequested(surface);
    _ = ApplyShellStateAsync();
}

private void HandleExitRequested()
{
    shellController.HandleExitRequested();
    ShutdownTrayResources();
    Exit();
}

private async Task ApplyShellStateAsync()
{
    if (mainWindow is null || flyoutWindow is null)
    {
        return;
    }

    mainWindow.Hide();
    flyoutWindow.Hide();

    if (shellController.Current.VisibleSurface == TrayShellSurface.MainWindow)
    {
        mainWindow.Activate();
        if (shellController.Current.PendingRefresh)
        {
            await mainWindow.RefreshRuntimeAsync();
            shellController.HandleRefreshCompleted();
        }
    }
    else if (shellController.Current.VisibleSurface == TrayShellSurface.Flyout)
    {
        flyoutWindow.Activate();
        if (shellController.Current.PendingRefresh)
        {
            await flyoutWindow.RefreshRuntimeAsync();
            shellController.HandleRefreshCompleted();
        }
    }
}
```

- [ ] **Step 3: Intercept main-window close requests and hide instead**

In `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`, add:

```csharp
private void MainWindow_Closed(object sender, WindowEventArgs args)
{
    requestHideToTray();
}
```

and subscribe after `InitializeComponent()`:

```csharp
Closed += MainWindow_Closed;
```

If `Closed` proves too late for WinUI `Window`, switch to the existing `AppWindow.Closing` event and cancel the close there. The implementation target is still the same behavior: user close hides to tray, explicit `Exit` terminates.

- [ ] **Step 4: Route notification activation to the main window**

Change the notification activation handler in `StartNotificationPolling` from:

```csharp
_ = uiDispatcherQueue?.TryEnqueue(() => window?.Activate());
```

to:

```csharp
_ = uiDispatcherQueue?.TryEnqueue(() =>
{
    shellController.HandleNotificationActivated();
    _ = ApplyShellStateAsync();
});
```

- [ ] **Step 5: Build and verify the tray shell behavior compiles**

Run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`

Expected: PASS.

---

### Task 5: Position The Flyout Near The Tray Area

**Files:**
- Create: `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPlacement.cs`
- Create: `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Add a small placement model**

Create `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPlacement.cs`:

```csharp
namespace OpenHab.Windows.Tray.Tray;

public sealed record TrayFlyoutPlacement(double X, double Y, double Width, double Height);
```

- [ ] **Step 2: Add a pragmatic positioner**

Create `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs`:

```csharp
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace OpenHab.Windows.Tray.Tray;

public sealed class TrayFlyoutPositioner
{
    public TrayFlyoutPlacement Calculate(
        PointInt32 cursorPosition,
        double desiredWidth,
        double desiredHeight)
    {
        var displayArea = DisplayArea.GetFromPoint(cursorPosition, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;

        var x = Math.Max(workArea.X, Math.Min(cursorPosition.X - (int)desiredWidth, workArea.X + workArea.Width - (int)desiredWidth));
        var y = Math.Max(workArea.Y, workArea.Y + workArea.Height - (int)desiredHeight - 12);

        return new TrayFlyoutPlacement(x, y, desiredWidth, desiredHeight);
    }
}
```

- [ ] **Step 3: Apply the placement before showing the flyout**

In `src/OpenHab.Windows.Tray/App.xaml.cs`, add a field:

```csharp
private readonly TrayFlyoutPositioner flyoutPositioner = new();
```

Then inside the `TrayShellSurface.Flyout` branch of `ApplyShellStateAsync()`:

```csharp
var cursor = System.Windows.Forms.Cursor.Position;
var placement = flyoutPositioner.Calculate(
    new Windows.Graphics.PointInt32(cursor.X, cursor.Y),
    desiredWidth: 392,
    desiredHeight: 720);

var flyoutAppWindow = flyoutWindow.AppWindow;
flyoutAppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
    (int)placement.X,
    (int)placement.Y,
    (int)placement.Width,
    (int)placement.Height));

flyoutWindow.Activate();
```

- [ ] **Step 4: Build and verify the flyout placement code**

Run: `dotnet build src/OpenHab.Windows.Tray/OpenHab.Windows.Tray.csproj`

Expected: PASS.

- [ ] **Step 5: Commit the positioning slice**

```powershell
git add src/OpenHab.Windows.Tray/App.xaml.cs src/OpenHab.Windows.Tray/Tray
git commit -m "feat: position tray flyout near tray area"
```

---

### Task 6: Verify Behavior And Record Status

**Files:**
- Create: `docs/superpowers/status/2026-05-05-openhab-windows-tray-shell-behavior-status.md`

- [ ] **Step 1: Run the focused app tests**

Run: `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter TrayShellControllerTests`

Expected: PASS.

- [ ] **Step 2: Run the full solution tests**

Run: `dotnet test OpenHab.Windows.sln`

Expected: PASS with no test regressions.

- [ ] **Step 3: Run the release build**

Run: `dotnet build OpenHab.Windows.sln --configuration Release`

Expected: PASS with 0 new warnings and 0 new errors.

- [ ] **Step 4: Perform manual shell verification**

Verify these manual behaviors on Windows 11:

- Launch the app and confirm no large window steals focus; tray icon is visible.
- Left-click the tray icon and confirm the compact flyout opens near the tray area.
- Left-click the tray icon again and confirm the flyout hides.
- Open the main window from the tray menu and confirm it stays separate from the flyout.
- Click `X` on the main window and confirm the app remains running in background with the tray icon still visible.
- Trigger a toast activation path and confirm it opens the main window, not the flyout.
- Choose `Exit` from the tray menu and confirm the process exits and the tray icon disappears.

- [ ] **Step 5: Write the completion status**

Create `docs/superpowers/status/2026-05-05-openhab-windows-tray-shell-behavior-status.md`:

```markdown
# openHAB Windows Tray Shell Behavior Status

Date: 2026-05-05

## Source Documents

- Design spec: `docs/superpowers/specs/2026-05-04-openhab-windows-sitemap-client-design.md`
- Auth & notifications status: `docs/superpowers/status/2026-05-05-openhab-windows-auth-notifications-status.md`
- Tray shell behavior plan: `docs/superpowers/plans/2026-05-05-openhab-windows-tray-shell-behavior.md`
- Visual reference: `.docs/mockup.png`

## Completed

- Added a testable tray shell state controller that keeps background-app rules out of WinUI code-behind.
- Split the tray-first compact flyout from the larger main window surface.
- Changed tray interaction to single left-click flyout toggle plus explicit context-menu actions.
- Intercepted main-window close to hide-to-tray instead of exiting the process.
- Routed notification activation into the larger main window path.
- Positioned the flyout near the tray area using display-aware placement.

## Verification

- `dotnet test tests/OpenHab.App.Tests/OpenHab.App.Tests.csproj --filter TrayShellControllerTests`: replace with actual result.
- `dotnet test OpenHab.Windows.sln`: replace with actual result.
- `dotnet build OpenHab.Windows.sln --configuration Release`: replace with actual result.
- Manual shell verification on Windows 11: replace with actual outcome notes.

## Still Out Of Scope

- Dashboard cards/widgets matching the full mockup.
- Search/filter behavior inside the flyout.
- Live event stream updates.
- Subpage navigation.
- Offline cache persistence.
- WebView/Main UI fallback.
- Startup-with-Windows and packaging polish.
```

Do not commit the status file until the verification lines contain real command outcomes.

- [ ] **Step 6: Commit the status record**

```powershell
git add docs/superpowers/status/2026-05-05-openhab-windows-tray-shell-behavior-status.md
git commit -m "docs: record tray shell behavior status"
```

---

## Self-Review

Spec/mockup coverage:

- Tray-first behavior is now explicit and testable.
- Close-to-background behavior is directly covered.
- Compact flyout vs larger main window separation is directly covered.
- The mockup influences layout direction, but the plan does not over-commit to building every dashboard card in the same slice.

Gaps intentionally left out:

- Real search behavior.
- Rich dashboard composition.
- Event-driven live refresh.
- WebView/main-ui embedding.

Type consistency:

- `TrayShellSurface` is the single surface enum used across tests, app-layer logic, and WinUI wiring.
- `TrayShellController` owns behavior decisions; `App.xaml.cs` only applies state to actual windows.

Placeholder scan:

- No `TODO` or `TBD` markers remain.
- Every task includes exact files and commands.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-05-openhab-windows-tray-shell-behavior.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
