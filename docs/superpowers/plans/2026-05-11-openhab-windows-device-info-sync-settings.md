# openHAB Windows Device Info Sync Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in Device Info Sync for Windows device/connectivity state and rewrite the right-side Settings tab into grouped Windows 11-style subpages.

**Architecture:** Expand pure device-state mapping in `OpenHab.Core`, persist user mappings in `OpenHab.App.Settings`, add an app-layer sync service that sends mapped states through `IOpenHabClient`, and keep Windows API collectors plus WinUI settings UI in `OpenHab.Windows.Tray`. Settings remain in the right-side panel beside the sitemap.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK, xUnit, existing `IOpenHabClient`, `AppSettingsController`, `SitemapRuntimeController`, and Windows tray shell.

---

## File Structure

- Modify `src/OpenHab.Core/DeviceState/DeviceStateSnapshot.cs`: add Wi-Fi, connection, and Focus/DND fields.
- Modify `src/OpenHab.Core/DeviceState/DeviceStateMapping.cs`: add target Item names for the new signals.
- Modify `src/OpenHab.Core/DeviceState/DeviceStateMapper.cs`: map the expanded snapshot into openHAB state updates.
- Modify `tests/OpenHab.Core.Tests/DeviceStateMapperTests.cs`: cover the full first-release signal set.
- Create `src/OpenHab.App/Settings/DeviceInfoSyncSettings.cs`: persisted settings, interval bounds, default signal names, sanitization helpers.
- Modify `src/OpenHab.App/Settings/AppSettings.cs`: add `DeviceInfoSyncSettings DeviceInfoSync`.
- Modify `src/OpenHab.App/Settings/AppSettingsController.cs`: normalize Device Info Sync settings and add setters used by UI.
- Modify `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`: settings serialization, migration/defaults, interval validation, default Item names.
- Create `src/OpenHab.App/DeviceInfo/DeviceInfoSyncStatus.cs`: immutable last-attempt/last-success result snapshot.
- Create `src/OpenHab.App/DeviceInfo/IDeviceStateSnapshotSource.cs`: app-layer abstraction for Windows collectors.
- Create `src/OpenHab.App/DeviceInfo/DeviceInfoSyncService.cs`: scheduling, batching, mapping, sending, and status.
- Create `tests/OpenHab.App.Tests/DeviceInfo/DeviceInfoSyncServiceTests.cs`: non-UI service tests.
- Modify `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`: record `SetItemStateAsync` calls and allow injected failure.
- Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs`: aggregate Windows collectors and runtime connection state.
- Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsBatteryInfoReader.cs`: battery/charging reader.
- Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsNetworkInfoReader.cs`: Wi-Fi connected/name reader.
- Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsFocusInfoReader.cs`: Focus/DND reader via `FocusSessionManager`.
- Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsSessionInfoReader.cs`: session state holder and lock/resume hooks.
- Modify `src/OpenHab.Windows.Tray/App.xaml.cs`: start/stop Device Info Sync service.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml`: replace the current long Settings form with a settings content host.
- Modify `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`: add right-panel settings navigation and subpage builders.

## Scope Notes

This plan intentionally leaves the left-side sitemap renderer alone. It also leaves Bluetooth, display state, idle time, power mode, next alarm, IP address, BSSID, and MAC address out of scope.

---

### Task 1: Expand Core Device-State Mapping

**Files:**
- Modify: `src/OpenHab.Core/DeviceState/DeviceStateSnapshot.cs`
- Modify: `src/OpenHab.Core/DeviceState/DeviceStateMapping.cs`
- Modify: `src/OpenHab.Core/DeviceState/DeviceStateMapper.cs`
- Test: `tests/OpenHab.Core.Tests/DeviceStateMapperTests.cs`

- [ ] **Step 1: Replace mapper tests with full signal coverage**

Replace `tests/OpenHab.Core.Tests/DeviceStateMapperTests.cs` with:

```csharp
using System.Globalization;
using OpenHab.Core.DeviceState;

namespace OpenHab.Core.Tests;

public sealed class DeviceStateMapperTests
{
    [Fact]
    public void MapsAllConfiguredDeviceInfoSignals()
    {
        var mapping = new DeviceStateMapping(
            "PcBatteryLevel",
            "PcChargingState",
            "PcLockedState",
            "PcSessionState",
            "PcWifiConnected",
            "PcWifiName",
            "PcOpenHabConnection",
            "PcFocusState");
        var snapshot = new DeviceStateSnapshot(
            BatteryLevelPercent: 87,
            IsCharging: true,
            IsLocked: true,
            SessionState: "locked",
            IsWifiConnected: true,
            WifiName: "HomeNet",
            OpenHabConnectionState: "online",
            FocusState: "ON");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([
            new DeviceStateUpdate("PcBatteryLevel", "87"),
            new DeviceStateUpdate("PcChargingState", "ON"),
            new DeviceStateUpdate("PcLockedState", "ON"),
            new DeviceStateUpdate("PcSessionState", "locked"),
            new DeviceStateUpdate("PcWifiConnected", "ON"),
            new DeviceStateUpdate("PcWifiName", "HomeNet"),
            new DeviceStateUpdate("PcOpenHabConnection", "online"),
            new DeviceStateUpdate("PcFocusState", "ON")
        ], updates);
    }

    [Fact]
    public void OmitsUnmappedDeviceInfoItems()
    {
        var mapping = new DeviceStateMapping(
            BatteryLevelItem: null,
            ChargingStateItem: null,
            LockedStateItem: "PcLockedState",
            SessionStateItem: null,
            WifiConnectedItem: null,
            WifiNameItem: null,
            OpenHabConnectionItem: null,
            FocusStateItem: null);
        var snapshot = new DeviceStateSnapshot(50, false, false, "active", false, null, "offline", "OFF");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([new DeviceStateUpdate("PcLockedState", "OFF")], updates);
    }

    [Fact]
    public void OmitsUpdatesWhenSnapshotFieldsAreNull()
    {
        var mapping = new DeviceStateMapping(
            "PcBatteryLevel",
            "PcChargingState",
            "PcLockedState",
            "PcSessionState",
            "PcWifiConnected",
            "PcWifiName",
            "PcOpenHabConnection",
            "PcFocusState");
        var snapshot = new DeviceStateSnapshot(null, null, null, null, null, null, null, null);

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Empty(updates);
    }

    [Fact]
    public void MapsWifiDisconnectedNameAsUndefWhenWifiNameItemIsConfigured()
    {
        var mapping = new DeviceStateMapping(
            null,
            null,
            null,
            null,
            WifiConnectedItem: "PcWifiConnected",
            WifiNameItem: "PcWifiName",
            OpenHabConnectionItem: null,
            FocusStateItem: null);
        var snapshot = new DeviceStateSnapshot(
            null,
            null,
            null,
            null,
            IsWifiConnected: false,
            WifiName: null,
            OpenHabConnectionState: null,
            FocusState: null);

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([
            new DeviceStateUpdate("PcWifiConnected", "OFF"),
            new DeviceStateUpdate("PcWifiName", "UNDEF")
        ], updates);
    }

    [Fact]
    public void MapsFocusUnsupportedAsStringState()
    {
        var mapping = new DeviceStateMapping(null, null, null, null, null, null, null, "PcFocusState");
        var snapshot = new DeviceStateSnapshot(null, null, null, null, null, null, null, "UNSUPPORTED");

        var updates = DeviceStateMapper.Map(snapshot, mapping);

        Assert.Equal([new DeviceStateUpdate("PcFocusState", "UNSUPPORTED")], updates);
    }

    [Fact]
    public void ThrowsForNullSnapshot()
    {
        var mapping = new DeviceStateMapping("PcBatteryLevel", null, null, null, null, null, null, null);

        var ex = Assert.Throws<ArgumentNullException>(() => DeviceStateMapper.Map(null!, mapping));

        Assert.Equal("snapshot", ex.ParamName);
    }

    [Fact]
    public void ThrowsForNullMapping()
    {
        var snapshot = new DeviceStateSnapshot(87, true, true, "locked", true, "HomeNet", "online", "ON");

        var ex = Assert.Throws<ArgumentNullException>(() => DeviceStateMapper.Map(snapshot, null!));

        Assert.Equal("mapping", ex.ParamName);
    }

    [Fact]
    public void FormatsBatteryPercentUsingInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            var mapping = new DeviceStateMapping("PcBatteryLevel", null, null, null, null, null, null, null);
            var snapshot = new DeviceStateSnapshot(87, null, null, null, null, null, null, null);

            var updates = DeviceStateMapper.Map(snapshot, mapping);

            Assert.Equal([new DeviceStateUpdate("PcBatteryLevel", "87")], updates);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
```

- [ ] **Step 2: Run the core mapper tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter DeviceStateMapperTests
```

Expected: compile failure because `DeviceStateSnapshot` and `DeviceStateMapping` do not have the new constructor parameters.

- [ ] **Step 3: Expand `DeviceStateSnapshot`**

Replace `src/OpenHab.Core/DeviceState/DeviceStateSnapshot.cs` with:

```csharp
namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateSnapshot(
    int? BatteryLevelPercent,
    bool? IsCharging,
    bool? IsLocked,
    string? SessionState,
    bool? IsWifiConnected,
    string? WifiName,
    string? OpenHabConnectionState,
    string? FocusState);
```

- [ ] **Step 4: Expand `DeviceStateMapping`**

Replace `src/OpenHab.Core/DeviceState/DeviceStateMapping.cs` with:

```csharp
namespace OpenHab.Core.DeviceState;

public sealed record DeviceStateMapping(
    string? BatteryLevelItem,
    string? ChargingStateItem,
    string? LockedStateItem,
    string? SessionStateItem,
    string? WifiConnectedItem,
    string? WifiNameItem,
    string? OpenHabConnectionItem,
    string? FocusStateItem);
```

- [ ] **Step 5: Expand `DeviceStateMapper`**

Replace `src/OpenHab.Core/DeviceState/DeviceStateMapper.cs` with:

```csharp
using System.Globalization;

namespace OpenHab.Core.DeviceState;

public static class DeviceStateMapper
{
    public static IReadOnlyList<DeviceStateUpdate> Map(DeviceStateSnapshot snapshot, DeviceStateMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapping);

        var updates = new List<DeviceStateUpdate>();

        if (mapping.BatteryLevelItem is not null && snapshot.BatteryLevelPercent is not null)
        {
            updates.Add(new DeviceStateUpdate(
                mapping.BatteryLevelItem,
                snapshot.BatteryLevelPercent.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (mapping.ChargingStateItem is not null && snapshot.IsCharging is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.ChargingStateItem, ToSwitchState(snapshot.IsCharging.Value)));
        }

        if (mapping.LockedStateItem is not null && snapshot.IsLocked is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.LockedStateItem, ToSwitchState(snapshot.IsLocked.Value)));
        }

        if (mapping.SessionStateItem is not null && snapshot.SessionState is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.SessionStateItem, snapshot.SessionState));
        }

        if (mapping.WifiConnectedItem is not null && snapshot.IsWifiConnected is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.WifiConnectedItem, ToSwitchState(snapshot.IsWifiConnected.Value)));
        }

        if (mapping.WifiNameItem is not null)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.WifiName))
            {
                updates.Add(new DeviceStateUpdate(mapping.WifiNameItem, snapshot.WifiName));
            }
            else if (snapshot.IsWifiConnected == false)
            {
                updates.Add(new DeviceStateUpdate(mapping.WifiNameItem, "UNDEF"));
            }
        }

        if (mapping.OpenHabConnectionItem is not null && snapshot.OpenHabConnectionState is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.OpenHabConnectionItem, snapshot.OpenHabConnectionState));
        }

        if (mapping.FocusStateItem is not null && snapshot.FocusState is not null)
        {
            updates.Add(new DeviceStateUpdate(mapping.FocusStateItem, snapshot.FocusState));
        }

        return updates;
    }

    private static string ToSwitchState(bool value)
    {
        return value ? "ON" : "OFF";
    }
}
```

- [ ] **Step 6: Run the core mapper tests and verify they pass**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj --filter DeviceStateMapperTests
```

Expected: PASS.

- [ ] **Step 7: Commit Task 1**

Run:

```powershell
git add src\OpenHab.Core\DeviceState tests\OpenHab.Core.Tests\DeviceStateMapperTests.cs
git commit -m "feat: expand device info state mapping"
```

---

### Task 2: Add Device Info Sync Settings

**Files:**
- Create: `src/OpenHab.App/Settings/DeviceInfoSyncSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettings.cs`
- Modify: `src/OpenHab.App/Settings/AppSettingsController.cs`
- Test: `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs`

- [ ] **Step 1: Add settings tests**

Append these tests to `tests/OpenHab.App.Tests/AppSettingsControllerTests.cs` inside `AppSettingsControllerTests`:

```csharp
    [Fact]
    public void DefaultsDisableDeviceInfoSync()
    {
        var controller = CreateController();

        Assert.False(controller.Current.DeviceInfoSync.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(controller.Current.DeviceInfoSync.DeviceIdentifier));
        Assert.Equal(15, controller.Current.DeviceInfoSync.SyncIntervalMinutes);
        Assert.True(controller.Current.DeviceInfoSync.HasAnyMapping);
    }

    [Fact]
    public void DeviceInfoSyncDefaultItemNamesUseSanitizedIdentifier()
    {
        var settings = DeviceInfoSyncSettings.CreateDefault("Desk PC!");

        Assert.Equal("DeskPC", settings.DeviceIdentifier);
        Assert.Equal("DeskPCBatteryLevel", settings.BatteryLevelItem);
        Assert.Equal("DeskPCChargingState", settings.ChargingStateItem);
        Assert.Equal("DeskPCLockedState", settings.LockedStateItem);
        Assert.Equal("DeskPCSessionState", settings.SessionStateItem);
        Assert.Equal("DeskPCWifiConnected", settings.WifiConnectedItem);
        Assert.Equal("DeskPCWifiName", settings.WifiNameItem);
        Assert.Equal("DeskPCOpenHabConnection", settings.OpenHabConnectionItem);
        Assert.Equal("DeskPCFocusState", settings.FocusStateItem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(240)]
    public void SetDeviceInfoSyncSettingsAcceptsIntervalBounds(int interval)
    {
        var controller = CreateController();
        var settings = controller.Current.DeviceInfoSync with { IsEnabled = true, SyncIntervalMinutes = interval };

        controller.SetDeviceInfoSyncSettings(settings);

        Assert.Equal(interval, controller.Current.DeviceInfoSync.SyncIntervalMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void SetDeviceInfoSyncSettingsRejectsOutOfRangeInterval(int interval)
    {
        var controller = CreateController();
        var settings = controller.Current.DeviceInfoSync with { SyncIntervalMinutes = interval };

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetDeviceInfoSyncSettings(settings));
    }

    [Fact]
    public async Task DeviceInfoSyncSettingsRoundTripThroughJson()
    {
        var controller = CreateController();
        var settings = DeviceInfoSyncSettings.CreateDefault("Desk") with
        {
            IsEnabled = true,
            SyncIntervalMinutes = 30,
            WifiNameItem = null
        };

        controller.SetDeviceInfoSyncSettings(settings);
        await controller.FlushAsync();

        var reloaded = CreateController();
        Assert.True(reloaded.Current.DeviceInfoSync.IsEnabled);
        Assert.Equal("Desk", reloaded.Current.DeviceInfoSync.DeviceIdentifier);
        Assert.Equal(30, reloaded.Current.DeviceInfoSync.SyncIntervalMinutes);
        Assert.Null(reloaded.Current.DeviceInfoSync.WifiNameItem);
    }
```

- [ ] **Step 2: Run settings tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter AppSettingsControllerTests
```

Expected: compile failure because `DeviceInfoSyncSettings` and `AppSettings.DeviceInfoSync` do not exist.

- [ ] **Step 3: Add `DeviceInfoSyncSettings`**

Create `src/OpenHab.App/Settings/DeviceInfoSyncSettings.cs`:

```csharp
using System.Text.RegularExpressions;
using OpenHab.Core.DeviceState;

namespace OpenHab.App.Settings;

public sealed record DeviceInfoSyncSettings(
    bool IsEnabled,
    string DeviceIdentifier,
    int SyncIntervalMinutes,
    string? BatteryLevelItem,
    string? ChargingStateItem,
    string? LockedStateItem,
    string? SessionStateItem,
    string? WifiConnectedItem,
    string? WifiNameItem,
    string? OpenHabConnectionItem,
    string? FocusStateItem)
{
    public const int MinSyncIntervalMinutes = 1;
    public const int MaxSyncIntervalMinutes = 240;
    public const int DefaultSyncIntervalMinutes = 15;

    private static readonly Regex InvalidIdentifierCharacters = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    public bool HasAnyMapping =>
        BatteryLevelItem is not null ||
        ChargingStateItem is not null ||
        LockedStateItem is not null ||
        SessionStateItem is not null ||
        WifiConnectedItem is not null ||
        WifiNameItem is not null ||
        OpenHabConnectionItem is not null ||
        FocusStateItem is not null;

    public static DeviceInfoSyncSettings Default { get; } = CreateDefault(Environment.MachineName);

    public static DeviceInfoSyncSettings CreateDefault(string rawIdentifier)
    {
        var identifier = SanitizeDeviceIdentifier(rawIdentifier);
        return new DeviceInfoSyncSettings(
            IsEnabled: false,
            DeviceIdentifier: identifier,
            SyncIntervalMinutes: DefaultSyncIntervalMinutes,
            BatteryLevelItem: identifier + "BatteryLevel",
            ChargingStateItem: identifier + "ChargingState",
            LockedStateItem: identifier + "LockedState",
            SessionStateItem: identifier + "SessionState",
            WifiConnectedItem: identifier + "WifiConnected",
            WifiNameItem: identifier + "WifiName",
            OpenHabConnectionItem: identifier + "OpenHabConnection",
            FocusStateItem: identifier + "FocusState");
    }

    public DeviceStateMapping ToMapping()
    {
        return new DeviceStateMapping(
            NormalizeItemName(BatteryLevelItem),
            NormalizeItemName(ChargingStateItem),
            NormalizeItemName(LockedStateItem),
            NormalizeItemName(SessionStateItem),
            NormalizeItemName(WifiConnectedItem),
            NormalizeItemName(WifiNameItem),
            NormalizeItemName(OpenHabConnectionItem),
            NormalizeItemName(FocusStateItem));
    }

    public DeviceInfoSyncSettings Normalized()
    {
        var identifier = SanitizeDeviceIdentifier(DeviceIdentifier);
        var interval = SyncIntervalMinutes;
        if (interval < MinSyncIntervalMinutes || interval > MaxSyncIntervalMinutes)
        {
            interval = DefaultSyncIntervalMinutes;
        }

        return this with
        {
            DeviceIdentifier = identifier,
            SyncIntervalMinutes = interval,
            BatteryLevelItem = NormalizeItemName(BatteryLevelItem),
            ChargingStateItem = NormalizeItemName(ChargingStateItem),
            LockedStateItem = NormalizeItemName(LockedStateItem),
            SessionStateItem = NormalizeItemName(SessionStateItem),
            WifiConnectedItem = NormalizeItemName(WifiConnectedItem),
            WifiNameItem = NormalizeItemName(WifiNameItem),
            OpenHabConnectionItem = NormalizeItemName(OpenHabConnectionItem),
            FocusStateItem = NormalizeItemName(FocusStateItem)
        };
    }

    public static string SanitizeDeviceIdentifier(string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "WindowsDevice" : value.Trim();
        var sanitized = InvalidIdentifierCharacters.Replace(trimmed, string.Empty);
        return string.IsNullOrWhiteSpace(sanitized) ? "WindowsDevice" : sanitized;
    }

    private static string? NormalizeItemName(string? itemName)
    {
        return string.IsNullOrWhiteSpace(itemName) ? null : itemName.Trim();
    }
}
```

- [ ] **Step 4: Add Device Info Sync to `AppSettings`**

Modify the `AppSettings` primary constructor in `src/OpenHab.App/Settings/AppSettings.cs` by inserting `DeviceInfoSyncSettings? DeviceInfoSync = null` before the `[property: JsonIgnore]` properties:

```csharp
public sealed record AppSettings(
    SitemapSkinKind Skin,
    EndpointMode EndpointMode,
    Uri LocalEndpoint,
    Uri CloudEndpoint,
    string SitemapName,
    bool FollowSystemTheme = true,
    bool UseWindows11Icons = false,
    int FlyoutWidth = 460,
    FlyoutAnimationSpeed AnimationSpeed = FlyoutAnimationSpeed.Default,
    int NotificationPollIntervalSeconds = 30,
    bool LaunchAtStartup = true,
    ChartQuality ChartQuality = ChartQuality.High,
    DeviceInfoSyncSettings? DeviceInfoSync = null,
    [property: JsonIgnore] bool HasLocalToken = false,
    [property: JsonIgnore] bool HasCloudCredentials = false,
    [property: JsonIgnore] string? CloudUserName = null)
```

Update `Default` to pass the setting explicitly:

```csharp
    public static AppSettings Default { get; } = new(
        SitemapSkinKind.Windows11,
        EndpointMode.Automatic,
        new Uri("http://openhab:8080"),
        new Uri("https://myopenhab.org"),
        string.Empty,
        AnimationSpeed: FlyoutAnimationSpeed.Default,
        NotificationPollIntervalSeconds: 30,
        DeviceInfoSync: DeviceInfoSyncSettings.Default);
```

- [ ] **Step 5: Add settings controller validation and setter**

In `src/OpenHab.App/Settings/AppSettingsController.cs`, add a method near the other `Set...` methods:

```csharp
    public void SetDeviceInfoSyncSettings(DeviceInfoSyncSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SyncIntervalMinutes < DeviceInfoSyncSettings.MinSyncIntervalMinutes
            || settings.SyncIntervalMinutes > DeviceInfoSyncSettings.MaxSyncIntervalMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                $"Device Info Sync interval must be between {DeviceInfoSyncSettings.MinSyncIntervalMinutes} and {DeviceInfoSyncSettings.MaxSyncIntervalMinutes} minutes.");
        }

        UpdateSettings(current => current with { DeviceInfoSync = settings.Normalized() });
    }
```

Then update `NormalizeLoadedSettings` so the final return normalizes device sync:

```csharp
        var deviceInfoSync = settings.DeviceInfoSync?.Normalized() ?? DeviceInfoSyncSettings.Default;

        return settings with
        {
            FlyoutWidth = width,
            NotificationPollIntervalSeconds = interval,
            DeviceInfoSync = deviceInfoSync
        };
```

- [ ] **Step 6: Run settings tests and verify they pass**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter AppSettingsControllerTests
```

Expected: PASS.

- [ ] **Step 7: Commit Task 2**

Run:

```powershell
git add src\OpenHab.App\Settings tests\OpenHab.App.Tests\AppSettingsControllerTests.cs
git commit -m "feat: add device info sync settings"
```

---

### Task 3: Add App-Layer Device Info Sync Service

**Files:**
- Create: `src/OpenHab.App/DeviceInfo/DeviceInfoSyncStatus.cs`
- Create: `src/OpenHab.App/DeviceInfo/IDeviceStateSnapshotSource.cs`
- Create: `src/OpenHab.App/DeviceInfo/DeviceInfoSyncService.cs`
- Modify: `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`
- Test: `tests/OpenHab.App.Tests/DeviceInfo/DeviceInfoSyncServiceTests.cs`

- [ ] **Step 1: Extend `FakeOpenHabClient` to record state updates**

Modify `tests/OpenHab.App.Tests/Runtime/FakeOpenHabClient.cs`:

```csharp
public List<(string ItemName, string State)> StatesSet { get; } = new();
public Exception? SetItemStateFailure { get; set; }
```

Replace `SetItemStateAsync` with:

```csharp
    public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken)
    {
        if (SetItemStateFailure is not null)
        {
            return Task.FromException(SetItemStateFailure);
        }

        StatesSet.Add((itemName, state));
        return Task.CompletedTask;
    }
```

- [ ] **Step 2: Add service tests**

Create `tests/OpenHab.App.Tests/DeviceInfo/DeviceInfoSyncServiceTests.cs`:

```csharp
using OpenHab.App.DeviceInfo;
using OpenHab.App.Settings;
using OpenHab.App.Tests.Runtime;
using OpenHab.Core.DeviceState;

namespace OpenHab.App.Tests.DeviceInfo;

public sealed class DeviceInfoSyncServiceTests
{
    [Fact]
    public async Task TriggerSyncAsyncDoesNothingWhenDisabled()
    {
        var client = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "OFF"));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = false },
            () => client,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Empty(client.StatesSet);
        Assert.Equal("Disabled", service.CurrentStatus.LastResult);
    }

    [Fact]
    public async Task TriggerSyncAsyncSendsConfiguredMappedStates()
    {
        var client = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "ON"));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Contains(("DeskBatteryLevel", "87"), client.StatesSet);
        Assert.Contains(("DeskChargingState", "ON"), client.StatesSet);
        Assert.Contains(("DeskLockedState", "OFF"), client.StatesSet);
        Assert.Contains(("DeskSessionState", "active"), client.StatesSet);
        Assert.Contains(("DeskWifiConnected", "ON"), client.StatesSet);
        Assert.Contains(("DeskWifiName", "HomeNet"), client.StatesSet);
        Assert.Contains(("DeskOpenHabConnection", "online"), client.StatesSet);
        Assert.Contains(("DeskFocusState", "ON"), client.StatesSet);
        Assert.Equal("8 Items updated", service.CurrentStatus.LastResult);
        Assert.NotNull(service.CurrentStatus.LastSuccessfulSync);
    }

    [Fact]
    public async Task TriggerSyncAsyncSkipsBlankItemMappings()
    {
        var client = new FakeOpenHabClient();
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, true, false, "active", true, "HomeNet", "online", "OFF"));
        var settings = DeviceInfoSyncSettings.CreateDefault("Desk") with
        {
            IsEnabled = true,
            WifiNameItem = null,
            FocusStateItem = null
        };
        var service = new DeviceInfoSyncService(() => settings, () => client, source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.DoesNotContain(client.StatesSet, update => update.ItemName == "DeskWifiName");
        Assert.DoesNotContain(client.StatesSet, update => update.ItemName == "DeskFocusState");
        Assert.Equal("6 Items updated", service.CurrentStatus.LastResult);
    }

    [Fact]
    public async Task TriggerSyncAsyncCapturesFailureWithoutThrowing()
    {
        var client = new FakeOpenHabClient
        {
            SetItemStateFailure = new InvalidOperationException("network down")
        };
        var source = new FakeSnapshotSource(new DeviceStateSnapshot(87, null, null, null, null, null, null, null));
        var service = new DeviceInfoSyncService(
            () => DeviceInfoSyncSettings.CreateDefault("Desk") with { IsEnabled = true },
            () => client,
            source);

        await service.TriggerSyncAsync(CancellationToken.None);

        Assert.Equal("InvalidOperationException: network down", service.CurrentStatus.LastError);
        Assert.Null(service.CurrentStatus.LastSuccessfulSync);
    }

    private sealed class FakeSnapshotSource(DeviceStateSnapshot snapshot) : IDeviceStateSnapshotSource
    {
        public Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(snapshot);
        }
    }
}
```

- [ ] **Step 3: Run service tests and verify they fail**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter DeviceInfoSyncServiceTests
```

Expected: compile failure because the service and status types do not exist.

- [ ] **Step 4: Add service status**

Create `src/OpenHab.App/DeviceInfo/DeviceInfoSyncStatus.cs`:

```csharp
namespace OpenHab.App.DeviceInfo;

public sealed record DeviceInfoSyncStatus(
    DateTimeOffset? LastAttemptedSync,
    DateTimeOffset? LastSuccessfulSync,
    string? LastResult,
    string? LastError)
{
    public static DeviceInfoSyncStatus Initial { get; } = new(null, null, null, null);
}
```

- [ ] **Step 5: Add snapshot source abstraction**

Create `src/OpenHab.App/DeviceInfo/IDeviceStateSnapshotSource.cs`:

```csharp
using OpenHab.Core.DeviceState;

namespace OpenHab.App.DeviceInfo;

public interface IDeviceStateSnapshotSource
{
    Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Add `DeviceInfoSyncService`**

Create `src/OpenHab.App/DeviceInfo/DeviceInfoSyncService.cs`:

```csharp
using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.DeviceState;

namespace OpenHab.App.DeviceInfo;

public sealed class DeviceInfoSyncService : IDisposable
{
    private readonly Func<DeviceInfoSyncSettings> getSettings;
    private readonly Func<IOpenHabClient?> getClient;
    private readonly IDeviceStateSnapshotSource snapshotSource;
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private Timer? timer;
    private int isDisposed;

    public DeviceInfoSyncService(
        Func<DeviceInfoSyncSettings> getSettings,
        Func<IOpenHabClient?> getClient,
        IDeviceStateSnapshotSource snapshotSource)
    {
        this.getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        this.getClient = getClient ?? throw new ArgumentNullException(nameof(getClient));
        this.snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
    }

    public DeviceInfoSyncStatus CurrentStatus { get; private set; } = DeviceInfoSyncStatus.Initial;

    public void Start()
    {
        var settings = getSettings();
        timer?.Dispose();
        timer = new Timer(
            _ => _ = TriggerSyncAsync(CancellationToken.None),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMinutes(settings.SyncIntervalMinutes));
    }

    public void RefreshInterval()
    {
        if (timer is null)
        {
            return;
        }

        var settings = getSettings();
        timer.Change(TimeSpan.FromMinutes(settings.SyncIntervalMinutes), TimeSpan.FromMinutes(settings.SyncIntervalMinutes));
    }

    public async Task TriggerSyncAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref isDisposed) != 0)
        {
            return;
        }

        await syncGate.WaitAsync(cancellationToken);
        try
        {
            var attempted = DateTimeOffset.UtcNow;
            var settings = getSettings().Normalized();
            if (!settings.IsEnabled)
            {
                CurrentStatus = CurrentStatus with
                {
                    LastAttemptedSync = attempted,
                    LastResult = "Disabled",
                    LastError = null
                };
                return;
            }

            var client = getClient();
            if (client is null)
            {
                CurrentStatus = CurrentStatus with
                {
                    LastAttemptedSync = attempted,
                    LastResult = null,
                    LastError = "No openHAB client available"
                };
                return;
            }

            var snapshot = await snapshotSource.CaptureAsync(cancellationToken);
            var updates = DeviceStateMapper.Map(snapshot, settings.ToMapping());
            if (updates.Count == 0)
            {
                CurrentStatus = CurrentStatus with
                {
                    LastAttemptedSync = attempted,
                    LastResult = "No configured Items",
                    LastError = null
                };
                return;
            }

            foreach (var update in updates)
            {
                await client.SetItemStateAsync(update.ItemName, update.State, cancellationToken);
            }

            CurrentStatus = new DeviceInfoSyncStatus(
                LastAttemptedSync: attempted,
                LastSuccessfulSync: DateTimeOffset.UtcNow,
                LastResult: updates.Count == 1 ? "1 Item updated" : $"{updates.Count} Items updated",
                LastError: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CurrentStatus = CurrentStatus with
            {
                LastAttemptedSync = DateTimeOffset.UtcNow,
                LastError = $"{ex.GetType().Name}: {ex.Message}"
            };
            DiagnosticLogger.Warn($"Device Info Sync failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            syncGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        timer?.Dispose();
        syncGate.Dispose();
    }
}
```

- [ ] **Step 7: Run service tests and verify they pass**

Run:

```powershell
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj --filter DeviceInfoSyncServiceTests
```

Expected: PASS.

- [ ] **Step 8: Commit Task 3**

Run:

```powershell
git add src\OpenHab.App\DeviceInfo tests\OpenHab.App.Tests\DeviceInfo tests\OpenHab.App.Tests\Runtime\FakeOpenHabClient.cs
git commit -m "feat: add device info sync service"
```

---

### Task 4: Add Windows Device Snapshot Source

**Files:**
- Create: `src/OpenHab.Windows.Tray/DeviceInfo/WindowsBatteryInfoReader.cs`
- Create: `src/OpenHab.Windows.Tray/DeviceInfo/WindowsNetworkInfoReader.cs`
- Create: `src/OpenHab.Windows.Tray/DeviceInfo/WindowsFocusInfoReader.cs`
- Create: `src/OpenHab.Windows.Tray/DeviceInfo/WindowsSessionInfoReader.cs`
- Create: `src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs`
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Add battery reader**

Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsBatteryInfoReader.cs`:

```csharp
using Windows.System.Power;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed record WindowsBatteryInfo(int? BatteryLevelPercent, bool? IsCharging);

internal sealed class WindowsBatteryInfoReader
{
    public WindowsBatteryInfo Read()
    {
        var report = PowerManager.BatteryStatus;
        var isCharging = report switch
        {
            BatteryStatus.Charging => true,
            BatteryStatus.Discharging => false,
            BatteryStatus.Idle => true,
            BatteryStatus.NotPresent => null,
            _ => null
        };

        var level = TryReadBatteryLevel();
        return new WindowsBatteryInfo(level, isCharging);
    }

    private static int? TryReadBatteryLevel()
    {
        try
        {
            var aggregateBattery = Windows.Devices.Power.Battery.AggregateBattery;
            var report = aggregateBattery.GetReport();
            if (report.FullChargeCapacityInMilliwattHours is null ||
                report.RemainingCapacityInMilliwattHours is null ||
                report.FullChargeCapacityInMilliwattHours <= 0)
            {
                return null;
            }

            var percent = (double)report.RemainingCapacityInMilliwattHours.Value /
                report.FullChargeCapacityInMilliwattHours.Value * 100;
            return Math.Clamp((int)Math.Round(percent), 0, 100);
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 2: Add network reader**

Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsNetworkInfoReader.cs`:

```csharp
using Windows.Networking.Connectivity;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed record WindowsNetworkInfo(bool? IsWifiConnected, string? WifiName);

internal sealed class WindowsNetworkInfoReader
{
    public WindowsNetworkInfo Read()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                return new WindowsNetworkInfo(false, null);
            }

            var isWifi = profile.IsWlanConnectionProfile;
            if (!isWifi)
            {
                return new WindowsNetworkInfo(false, null);
            }

            var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            return new WindowsNetworkInfo(true, string.IsNullOrWhiteSpace(ssid) ? null : ssid);
        }
        catch
        {
            return new WindowsNetworkInfo(null, null);
        }
    }
}
```

- [ ] **Step 3: Add Focus reader**

Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsFocusInfoReader.cs`:

```csharp
using Windows.UI.Shell;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed class WindowsFocusInfoReader
{
    public string ReadState()
    {
        try
        {
            if (!FocusSessionManager.IsSupported)
            {
                return "UNSUPPORTED";
            }

            return FocusSessionManager.GetDefault().IsFocusActive ? "ON" : "OFF";
        }
        catch
        {
            return "UNSUPPORTED";
        }
    }
}
```

- [ ] **Step 4: Add session reader**

Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsSessionInfoReader.cs`:

```csharp
namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed class WindowsSessionInfoReader
{
    private volatile string sessionState = "active";
    private volatile bool isLocked;

    public bool IsLocked => isLocked;

    public string SessionState => sessionState;

    public void MarkActive()
    {
        isLocked = false;
        sessionState = "active";
    }

    public void MarkLocked()
    {
        isLocked = true;
        sessionState = "locked";
    }

    public void MarkSleep()
    {
        sessionState = "sleep";
    }

    public void MarkResume()
    {
        isLocked = false;
        sessionState = "resume";
    }
}
```

- [ ] **Step 5: Add aggregate snapshot source**

Create `src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs`:

```csharp
using OpenHab.App.DeviceInfo;
using OpenHab.App.Runtime;
using OpenHab.Core.DeviceState;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed class WindowsDeviceStateSnapshotSource(
    SitemapRuntimeController runtimeController,
    WindowsBatteryInfoReader batteryReader,
    WindowsNetworkInfoReader networkReader,
    WindowsFocusInfoReader focusReader,
    WindowsSessionInfoReader sessionReader) : IDeviceStateSnapshotSource
{
    public Task<DeviceStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var battery = batteryReader.Read();
        var network = networkReader.Read();

        var snapshot = new DeviceStateSnapshot(
            BatteryLevelPercent: battery.BatteryLevelPercent,
            IsCharging: battery.IsCharging,
            IsLocked: sessionReader.IsLocked,
            SessionState: sessionReader.SessionState,
            IsWifiConnected: network.IsWifiConnected,
            WifiName: network.WifiName,
            OpenHabConnectionState: runtimeController.Current.ConnectionState.ToString().ToLowerInvariant(),
            FocusState: focusReader.ReadState());

        return Task.FromResult(snapshot);
    }
}
```

- [ ] **Step 6: Build the Windows tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: PASS with `Windows.UI.Shell.FocusSessionManager` available from the Windows target SDK used by this project.

- [ ] **Step 7: Commit Task 4**

Run:

```powershell
git add src\OpenHab.Windows.Tray\DeviceInfo
git commit -m "feat: add Windows device info collectors"
```

---

### Task 5: Wire Device Info Sync Into App Startup And Events

**Files:**
- Modify: `src/OpenHab.Windows.Tray/App.xaml.cs`

- [ ] **Step 1: Add fields and using directives**

In `src/OpenHab.Windows.Tray/App.xaml.cs`, add:

```csharp
using Microsoft.Win32;
using OpenHab.App.DeviceInfo;
using OpenHab.Windows.Tray.DeviceInfo;
using Windows.Networking.Connectivity;
using Windows.UI.Shell;
```

Add fields to `App`:

```csharp
    private DeviceInfoSyncService? deviceInfoSyncService;
    private WindowsSessionInfoReader? sessionInfoReader;
```

- [ ] **Step 2: Create sync service after runtime controller creation**

After `runtimeController = new SitemapRuntimeController(...);`, add:

```csharp
        sessionInfoReader = new WindowsSessionInfoReader();
        var snapshotSource = new WindowsDeviceStateSnapshotSource(
            runtimeController,
            new WindowsBatteryInfoReader(),
            new WindowsNetworkInfoReader(),
            new WindowsFocusInfoReader(),
            sessionInfoReader);
        deviceInfoSyncService = new DeviceInfoSyncService(
            () => settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default,
            CreateDeviceInfoClient,
            snapshotSource);
```

Add this private `App` method near `CreateEventStreamClient`:

```csharp
    private IOpenHabClient? CreateDeviceInfoClient()
    {
        if (settingsController is null || httpClient is null)
        {
            return null;
        }

        var settings = settingsController.Current;
        var transport = settings.EndpointMode == EndpointMode.CloudOnly
            ? TransportKind.Cloud
            : TransportKind.Local;
        var endpoint = transport == TransportKind.Local ? settings.LocalEndpoint : settings.CloudEndpoint;
        var auth = ResolveRuntimeAuthSync(settingsController, transport);
        return new OpenHabHttpClient(
            httpClient,
            endpoint,
            apiToken: auth.ApiToken,
            basicUserName: auth.BasicUserName,
            basicPassword: auth.BasicPassword);
    }
```

- [ ] **Step 3: Start service after startup initialization**

In `CompleteStartupAsync`, after `await InitializeAsync(settingsController);`, add:

```csharp
        deviceInfoSyncService?.Start();
        _ = deviceInfoSyncService?.TriggerSyncAsync(CancellationToken.None);
        RegisterDeviceInfoSyncEvents();
```

- [ ] **Step 4: Add event registration**

Add these methods to `App.xaml.cs`:

```csharp
    private void RegisterDeviceInfoSyncEvents()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;

        try
        {
            if (FocusSessionManager.IsSupported)
            {
                FocusSessionManager.GetDefault().IsFocusActiveChanged += OnFocusActiveChanged;
            }
        }
        catch
        {
            // Focus state remains best-effort.
        }
    }

    private void UnregisterDeviceInfoSyncEvents()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;

        try
        {
            if (FocusSessionManager.IsSupported)
            {
                FocusSessionManager.GetDefault().IsFocusActiveChanged -= OnFocusActiveChanged;
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (sessionInfoReader is null)
        {
            return;
        }

        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            sessionInfoReader.MarkLocked();
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            sessionInfoReader.MarkActive();
        }

        _ = deviceInfoSyncService?.TriggerSyncAsync(CancellationToken.None);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (sessionInfoReader is not null)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                sessionInfoReader.MarkSleep();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                sessionInfoReader.MarkResume();
            }
        }

        _ = deviceInfoSyncService?.TriggerSyncAsync(CancellationToken.None);
    }

    private void OnNetworkStatusChanged(object sender)
    {
        _ = deviceInfoSyncService?.TriggerSyncAsync(CancellationToken.None);
    }

    private void OnFocusActiveChanged(FocusSessionManager sender, object args)
    {
        _ = deviceInfoSyncService?.TriggerSyncAsync(CancellationToken.None);
    }
```

- [ ] **Step 5: Dispose service and unregister events**

In `ShutdownTrayResourcesCore`, before disposing `httpClient`, add:

```csharp
        UnregisterDeviceInfoSyncEvents();
        deviceInfoSyncService?.Dispose();
        deviceInfoSyncService = null;
```

- [ ] **Step 6: Build the Windows tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit Task 5**

Run:

```powershell
git add src\OpenHab.Windows.Tray\App.xaml.cs
git commit -m "feat: start device info sync service"
```

---

### Task 6: Rewrite Right-Side Settings Into Category Subpages

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml`
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Replace Settings tab XAML content host**

In `src/OpenHab.Windows.Tray/MainWindow.xaml`, replace the current `<PivotItem Header="Settings">...</PivotItem>` content with:

```xml
            <PivotItem Header="Settings">
                <Border BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                        BorderThickness="1"
                        CornerRadius="8"
                        Padding="12">
                    <Grid RowDefinitions="Auto,*">
                        <Grid x:Name="SettingsHeader"
                              ColumnDefinitions="Auto,*"
                              ColumnSpacing="8"
                              Margin="0,0,0,12">
                            <Button x:Name="SettingsBackButton"
                                    Grid.Column="0"
                                    Content="&#xE72B;"
                                    FontFamily="Segoe Fluent Icons"
                                    Width="32"
                                    Height="32"
                                    Padding="0"
                                    Visibility="Collapsed"
                                    Click="SettingsBackButton_Click" />
                            <StackPanel Grid.Column="1" Spacing="2">
                                <TextBlock x:Name="SettingsTitle"
                                           Text="Settings"
                                           FontSize="18"
                                           FontWeight="SemiBold" />
                                <TextBlock x:Name="SettingsSubtitle"
                                           Style="{StaticResource CaptionTextBlockStyle}"
                                           Opacity="0.65"
                                           Text="Configure openHAB Windows" />
                            </StackPanel>
                        </Grid>
                        <ScrollViewer Grid.Row="1">
                            <StackPanel x:Name="SettingsContent" Spacing="8" />
                        </ScrollViewer>
                    </Grid>
                </Border>
            </PivotItem>
```

- [ ] **Step 2: Add settings page enum and refresh entry point**

In `MainWindow.xaml.cs`, add this enum near the fields:

```csharp
    private enum SettingsPage
    {
        Home,
        Connection,
        General,
        Appearance,
        DeviceInfoSync,
        About
    }

    private SettingsPage currentSettingsPage = SettingsPage.Home;
```

In the constructor, after `RefreshSettingsBindings();`, call:

```csharp
        ShowSettingsPage(SettingsPage.Home);
```

- [ ] **Step 3: Add settings navigation builder**

Add these methods to `MainWindow.xaml.cs`:

```csharp
    private void ShowSettingsPage(SettingsPage page)
    {
        currentSettingsPage = page;
        SettingsContent.Children.Clear();
        SettingsBackButton.Visibility = page == SettingsPage.Home ? Visibility.Collapsed : Visibility.Visible;

        switch (page)
        {
            case SettingsPage.Home:
                SettingsTitle.Text = "Settings";
                SettingsSubtitle.Text = "Configure openHAB Windows";
                AddSettingsCategory("Connection", "Endpoints, credentials, sitemap", "\uE774", SettingsPage.Connection);
                AddSettingsCategory("General", "Startup, flyout, notifications", "\uE713", SettingsPage.General);
                AddSettingsCategory("Appearance", "Theme, icons, charts", "\uE790", SettingsPage.Appearance);
                AddSettingsCategory("Device Info Sync", "Battery, Wi-Fi, Focus, connection state", "\uE7F4", SettingsPage.DeviceInfoSync);
                AddSettingsCategory("About", "Version, diagnostics", "\uE946", SettingsPage.About);
                break;
            case SettingsPage.Connection:
                SettingsTitle.Text = "Connection";
                SettingsSubtitle.Text = "Endpoints, credentials, and sitemap selection";
                BuildConnectionSettingsPage();
                break;
            case SettingsPage.General:
                SettingsTitle.Text = "General";
                SettingsSubtitle.Text = "Startup, flyout, and notifications";
                BuildGeneralSettingsPage();
                break;
            case SettingsPage.Appearance:
                SettingsTitle.Text = "Appearance";
                SettingsSubtitle.Text = "Theme, icons, and charts";
                BuildAppearanceSettingsPage();
                break;
            case SettingsPage.DeviceInfoSync:
                SettingsTitle.Text = "Device Info Sync";
                SettingsSubtitle.Text = "Sync Windows device information to openHAB Items";
                BuildDeviceInfoSyncSettingsPage();
                break;
            case SettingsPage.About:
                SettingsTitle.Text = "About";
                SettingsSubtitle.Text = "Version and diagnostics";
                BuildAboutSettingsPage();
                break;
        }
    }

    private void AddSettingsCategory(string title, string subtitle, string glyph, SettingsPage page)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Width = 28
        };

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = subtitle,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        });

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } }, ColumnSpacing = 10 };
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var chevron = new FontIcon { Glyph = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12 };
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);

        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10, 12, 10)
        };
        button.Click += (_, _) => ShowSettingsPage(page);
        SettingsContent.Children.Add(button);
    }

    private void SettingsBackButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsPage(SettingsPage.Home);
    }
```

- [ ] **Step 4: Move existing controls into subpage builders**

Implement `BuildConnectionSettingsPage`, `BuildGeneralSettingsPage`, `BuildAppearanceSettingsPage`, and `BuildAboutSettingsPage` by creating the same control names currently used in XAML (`EndpointModeCombo`, `LocalEndpointText`, `CloudEndpointText`, `LocalTokenBox`, `CloudUserNameText`, `CloudPasswordBox`, `LaunchAtStartupToggle`, `FlyoutWidthBox`, `NotificationPollBox`, `SkinCombo`, `FollowThemeToggle`, `UseWin11IconsToggle`, `ViewLogsButton`, `VersionText`) before calling `RefreshSettingsBindings`.

Use this helper pattern for each control:

```csharp
    private TextBox AddTextBox(string header, string? placeholder = null)
    {
        var box = new TextBox { Header = header, PlaceholderText = placeholder ?? string.Empty };
        SettingsContent.Children.Add(box);
        return box;
    }
```

Keep existing event handlers. After constructing dynamic controls, assign handlers in code:

```csharp
        EndpointModeCombo.SelectionChanged += EndpointModeCombo_SelectionChanged;
        LocalEndpointText.LostFocus += EndpointText_LostFocus;
        CloudEndpointText.LostFocus += EndpointText_LostFocus;
```

Use private nullable fields for dynamically created settings controls. Rename current references in `RefreshSettingsBindings` and existing event handlers from generated XAML fields to these fields:

```text
SkinCombo -> skinCombo
EndpointModeCombo -> endpointModeCombo
LocalEndpointText -> localEndpointText
CloudEndpointText -> cloudEndpointText
LocalTokenBox -> localTokenBox
CloudUserNameText -> cloudUserNameText
CloudPasswordBox -> cloudPasswordBox
FollowThemeToggle -> followThemeToggle
UseWin11IconsToggle -> useWin11IconsToggle
LaunchAtStartupToggle -> launchAtStartupToggle
FlyoutWidthBox -> flyoutWidthBox
NotificationPollBox -> notificationPollBox
ViewLogsButton -> viewLogsButton
VersionText -> versionText
```

Add null guards at the start of event handlers that can fire after navigating away from a subpage. Example:

```csharp
        if (localEndpointText is null || cloudEndpointText is null)
        {
            return;
        }
```

- [ ] **Step 5: Build the Windows tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: PASS. Fix compile errors from moved controls by either making dynamic fields nullable or keeping the original XAML control names in the visual tree.

- [ ] **Step 6: Commit Task 6**

Run:

```powershell
git add src\OpenHab.Windows.Tray\MainWindow.xaml src\OpenHab.Windows.Tray\MainWindow.xaml.cs
git commit -m "feat: rewrite settings as grouped subpages"
```

---

### Task 7: Add Device Info Sync Settings UI

**Files:**
- Modify: `src/OpenHab.Windows.Tray/MainWindow.xaml.cs`

- [ ] **Step 1: Add Device Info Sync page builder**

Add `BuildDeviceInfoSyncSettingsPage` to `MainWindow.xaml.cs`:

```csharp
    private TextBox? deviceIdentifierText;
    private NumberBox? deviceInfoIntervalBox;
    private ToggleSwitch? deviceInfoEnabledToggle;
    private readonly Dictionary<string, TextBox> deviceInfoItemBoxes = new(StringComparer.Ordinal);

    private void BuildDeviceInfoSyncSettingsPage()
    {
        deviceInfoItemBoxes.Clear();
        var settings = settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default;

        deviceInfoEnabledToggle = new ToggleSwitch
        {
            Header = "Sync device information",
            OnContent = "On",
            OffContent = "Off",
            IsOn = settings.IsEnabled
        };
        deviceInfoEnabledToggle.Toggled += DeviceInfoEnabledToggle_Toggled;
        SettingsContent.Children.Add(deviceInfoEnabledToggle);

        if (!settings.IsEnabled)
        {
            SettingsContent.Children.Add(new TextBlock
            {
                Text = "Device Info Sync is disabled.",
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        deviceIdentifierText = AddTextBox("Device identifier");
        deviceIdentifierText.Text = settings.DeviceIdentifier;
        deviceIdentifierText.LostFocus += DeviceInfoSettings_LostFocus;

        deviceInfoIntervalBox = new NumberBox
        {
            Header = "Sync interval (minutes)",
            Minimum = DeviceInfoSyncSettings.MinSyncIntervalMinutes,
            Maximum = DeviceInfoSyncSettings.MaxSyncIntervalMinutes,
            SmallChange = 1,
            LargeChange = 15,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = settings.SyncIntervalMinutes
        };
        deviceInfoIntervalBox.ValueChanged += DeviceInfoIntervalBox_ValueChanged;
        SettingsContent.Children.Add(deviceInfoIntervalBox);

        AddDeviceInfoItemBox("Battery level", "Number", nameof(DeviceInfoSyncSettings.BatteryLevelItem), settings.BatteryLevelItem);
        AddDeviceInfoItemBox("Charging state", "Switch/String", nameof(DeviceInfoSyncSettings.ChargingStateItem), settings.ChargingStateItem);
        AddDeviceInfoItemBox("Locked state", "Switch", nameof(DeviceInfoSyncSettings.LockedStateItem), settings.LockedStateItem);
        AddDeviceInfoItemBox("Session state", "String", nameof(DeviceInfoSyncSettings.SessionStateItem), settings.SessionStateItem);
        AddDeviceInfoItemBox("Wi-Fi connected", "Switch", nameof(DeviceInfoSyncSettings.WifiConnectedItem), settings.WifiConnectedItem);
        AddDeviceInfoItemBox("Wi-Fi name", "String", nameof(DeviceInfoSyncSettings.WifiNameItem), settings.WifiNameItem);
        AddDeviceInfoItemBox("openHAB connection", "String", nameof(DeviceInfoSyncSettings.OpenHabConnectionItem), settings.OpenHabConnectionItem);
        AddDeviceInfoItemBox("Focus / DND", "Switch/String", nameof(DeviceInfoSyncSettings.FocusStateItem), settings.FocusStateItem);
    }
```

- [ ] **Step 2: Add signal row helper and save method**

Add:

```csharp
    private void AddDeviceInfoItemBox(string label, string itemType, string key, string? value)
    {
        var box = AddTextBox($"{label} Item", itemType);
        box.Text = value ?? string.Empty;
        box.LostFocus += DeviceInfoSettings_LostFocus;
        deviceInfoItemBoxes[key] = box;
    }

    private void SaveDeviceInfoSettingsFromUi()
    {
        var current = settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default;
        var interval = deviceInfoIntervalBox is null || double.IsNaN(deviceInfoIntervalBox.Value)
            ? current.SyncIntervalMinutes
            : (int)Math.Round(deviceInfoIntervalBox.Value);

        var updated = current with
        {
            IsEnabled = deviceInfoEnabledToggle?.IsOn ?? current.IsEnabled,
            DeviceIdentifier = deviceIdentifierText?.Text ?? current.DeviceIdentifier,
            SyncIntervalMinutes = interval,
            BatteryLevelItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.BatteryLevelItem), current.BatteryLevelItem),
            ChargingStateItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.ChargingStateItem), current.ChargingStateItem),
            LockedStateItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.LockedStateItem), current.LockedStateItem),
            SessionStateItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.SessionStateItem), current.SessionStateItem),
            WifiConnectedItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.WifiConnectedItem), current.WifiConnectedItem),
            WifiNameItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.WifiNameItem), current.WifiNameItem),
            OpenHabConnectionItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.OpenHabConnectionItem), current.OpenHabConnectionItem),
            FocusStateItem = GetDeviceInfoItem(nameof(DeviceInfoSyncSettings.FocusStateItem), current.FocusStateItem)
        };

        settingsController.SetDeviceInfoSyncSettings(updated);
    }

    private string? GetDeviceInfoItem(string key, string? fallback)
    {
        return deviceInfoItemBoxes.TryGetValue(key, out var box) ? box.Text : fallback;
    }
```

- [ ] **Step 3: Add UI event handlers**

Add:

```csharp
    private void DeviceInfoEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (isRefreshing)
        {
            return;
        }

        SaveDeviceInfoSettingsFromUi();
        ShowSettingsPage(SettingsPage.DeviceInfoSync);
    }

    private void DeviceInfoSettings_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isRefreshing)
        {
            return;
        }

        SaveDeviceInfoSettingsFromUi();
    }

    private void DeviceInfoIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (isRefreshing || double.IsNaN(args.NewValue))
        {
            return;
        }

        var interval = (int)Math.Round(args.NewValue);
        if (interval < DeviceInfoSyncSettings.MinSyncIntervalMinutes ||
            interval > DeviceInfoSyncSettings.MaxSyncIntervalMinutes)
        {
            RefreshSettingsBindings();
            return;
        }

        SaveDeviceInfoSettingsFromUi();
    }
```

- [ ] **Step 4: Update `RefreshSettingsBindings`**

At the end of `RefreshSettingsBindings`, add:

```csharp
        if (currentSettingsPage == SettingsPage.DeviceInfoSync && deviceInfoEnabledToggle is not null)
        {
            var deviceInfo = settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default;
            deviceInfoEnabledToggle.IsOn = deviceInfo.IsEnabled;
            if (deviceIdentifierText is not null)
            {
                deviceIdentifierText.Text = deviceInfo.DeviceIdentifier;
            }

            if (deviceInfoIntervalBox is not null)
            {
                deviceInfoIntervalBox.Value = deviceInfo.SyncIntervalMinutes;
            }
        }
```

- [ ] **Step 5: Build the Windows tray project**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit Task 7**

Run:

```powershell
git add src\OpenHab.Windows.Tray\MainWindow.xaml.cs
git commit -m "feat: add device info sync settings UI"
```

---

### Task 8: Verification And Manual Smoke

**Files:**
- Modify only if verification finds defects in files changed by Tasks 1-7.

- [ ] **Step 1: Run direct test gate**

Run:

```powershell
dotnet test tests\OpenHab.Core.Tests\OpenHab.Core.Tests.csproj
dotnet test tests\OpenHab.Sitemaps.Tests\OpenHab.Sitemaps.Tests.csproj
dotnet test tests\OpenHab.Rendering.Tests\OpenHab.Rendering.Tests.csproj
dotnet test tests\OpenHab.App.Tests\OpenHab.App.Tests.csproj
```

Expected: all direct test projects pass.

- [ ] **Step 2: Run tray build**

Run:

```powershell
dotnet build src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj --configuration Release
```

Expected: PASS. If files cannot be copied because the app is running from Visual Studio, close the app or rerun a Debug build before diagnosing code.

- [ ] **Step 3: Run full/package gate when DesktopBridge is available**

Run:

```powershell
dotnet test OpenHab.Windows.sln
.\build-package.ps1 -Configuration Release -Platform x64
```

Expected: PASS when `Microsoft.DesktopBridge.props` is installed. If the package project reports missing DesktopBridge targets, record that as the known environment issue from `docs/superpowers/verification/openhab-windows-quality-gates.md`.

- [ ] **Step 4: Manual Settings smoke check**

Run the app and verify:

```text
Main window opens.
Left sitemap still renders.
Right side has Notifications and Settings tabs.
Settings opens the category list.
Connection, General, Appearance, Device Info Sync, and About open as subpages.
Back button returns to the settings category list.
Device Info Sync disabled state hides mapping fields.
Device Info Sync enabled state shows identifier, interval, status, and mapping fields.
Changing Sync interval persists after closing and reopening the app.
Blank signal Item names persist as disabled signals.
```

- [ ] **Step 5: Manual Device Info Sync smoke check**

Configure one harmless openHAB String/Number Item, enable Device Info Sync, and verify:

```text
diagnostics.log shows a Device Info Sync attempt.
No credentials, tokens, BSSID, MAC address, IP address, or raw server response body appear in diagnostics.log.
The configured Item receives the expected state.
Sitemap commands still work after a sync failure.
```

- [ ] **Step 6: Commit verification fixes**

If verification required fixes, commit them:

```powershell
git add src tests
git commit -m "fix: stabilize device info sync settings"
```

If no fixes were required, do not create an empty commit.

---

## Self-Review Checklist

- Spec coverage: Tasks 1-3 cover pure telemetry, settings, sender, and failure isolation. Tasks 4-5 cover Windows collection and startup/shutdown wiring. Tasks 6-7 cover the right-side settings rewrite and Device Info Sync UI. Task 8 covers verification and manual checks.
- Privacy: The plan sends SSID only when configured and never sends BSSID, MAC, IP, username, tokens, passwords, or raw server bodies.
- Layering: Core remains pure, App stays UI-independent, and Windows APIs stay in `OpenHab.Windows.Tray`.
- Known risk: Task 6 is the largest UI task. If implementation makes `MainWindow.xaml.cs` too large, split settings builders into adjacent Windows-layer files before committing Task 6.
