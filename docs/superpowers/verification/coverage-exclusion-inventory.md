# Coverage Exclusion Inventory

This file is the reviewable source for files excluded from line coverage. It is not copied verbatim into tool configuration; Coverlet and Sonar use different path pattern syntax.

## Policy

Use tests first. Extract pure decisions into `OpenHab.App`, `OpenHab.Rendering`, `OpenHab.Sitemaps`, or `OpenHab.Core` before excluding code.

Use `[ExcludeFromCodeCoverage]` only for files that are thin wrappers around:

- WinUI or Windows App SDK activation,
- Win32 shell and tray APIs,
- global hotkey message windows,
- Windows registry startup integration,
- Windows credential APIs,
- toast notification COM/AppUserModelID integration,
- device state readers that depend on live OS state.

Do not apply `[ExcludeFromCodeCoverage]` to files that mix OS glue with parseable planning, mapping, formatting, validation, state transitions, or command selection. Extract those decisions first.

## Tool Mapping

- Coverlet: list files in `coverage.runsettings` under `ExcludeByFile` using `**/src/...` globs.
- Sonar: list files in `.github/workflows/sonarcloud.yml` under `sonar.coverage.exclusions` using repo-relative `src/...` patterns.
- Attribute-based tools: use `[ExcludeFromCodeCoverage]` on thin wrapper types. Large WinUI code-behind and control-factory files are excluded through file-pattern configuration after their testable decisions have been extracted to neutral layers.

## Excluded Files

| File | Reason | Verification |
| --- | --- | --- |
| `src/OpenHab.Windows.Tray/App.xaml.cs` | Windows App SDK activation and shell composition glue | Release build and smoke test |
| `src/OpenHab.Windows.Tray/AcrylicBlurHelper.cs` | Win32/DWM composition wrapper | Release build |
| `src/OpenHab.Windows.Tray/DwmWindowDecorations.cs` | Win32/DWM composition wrapper | Release build |
| `src/OpenHab.Windows.Tray/FlyoutWindow.xaml.cs` | WinUI flyout code-behind after runtime, rendering, search, and transition planning extraction | App/rendering tests, Release build, and smoke test |
| `src/OpenHab.Windows.Tray/MainWindow.xaml.cs` | WinUI main-window code-behind after shell, runtime, rendering, Main UI, search, and transition planning extraction | App/rendering tests, Release build, and smoke test |
| `src/OpenHab.Windows.Tray/MainUi/MainUiWebViewHost.xaml.cs` | WebView2 host glue after URL/auth policy extraction | Release build and smoke test |
| `src/OpenHab.Windows.Tray/Notifications/NotificationsPageControl.xaml.cs` | WinUI notification page binding and control glue after notification-store extraction | Notification tests, Release build, and smoke test |
| `src/OpenHab.Windows.Tray/Rendering/OpenHabIconImageSourceLoader.cs` | WinUI `ImageSource` loading and stream glue after icon URI/cache-key/SVG policy extraction | Rendering tests and Release build |
| `src/OpenHab.Windows.Tray/Rendering/CompositionAnimationHelper.cs` | WinUI composition animation wrapper | Release build |
| `src/OpenHab.Windows.Tray/Rendering/SitemapComboHelper.cs` | WinUI ComboBox display glue | Release build |
| `src/OpenHab.Windows.Tray/Rendering/SitemapControlFactory.cs` | WinUI control factory after row mapping, row policy, icon, chart, input, and transition decisions were extracted | Rendering tests, App tests, Release build, and smoke test |
| `src/OpenHab.Windows.Tray/Rendering/SitemapPageTransitionAnimator.cs` | WinUI animation wrapper | Release build |
| `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapIconAuthResolver.cs` | Thin settings-to-icon-auth adapter for WinUI sitemap rendering | Settings tests and Release build |
| `src/OpenHab.Windows.Tray/Rendering/SitemapSurface/SitemapSurfaceRenderer.cs` | WinUI `StackPanel` renderer after row planning extraction | App/rendering tests and Release build |
| `src/OpenHab.Windows.Tray/Settings/SettingsPageControl.xaml.cs` | WinUI settings page binding and control glue after settings/shortcut policy extraction | Settings and shortcut tests, Release build, and smoke test |
| `src/OpenHab.Windows.Tray/Shortcuts/GlobalHotkeyService.cs` | Win32 global hotkey registration | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/HotkeyMessageWindow.cs` | Win32 message-only window | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/RadialCommandMenuWindow.cs` | WinUI popup host after command planning extraction | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/ShortcutInteractiveCommandWindow.cs` | WinUI command popup host after command planning extraction | Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/ShortcutRecorderControl.cs` | WinUI keyboard capture glue after key mapping extraction | `ShortcutWindowsMapperTests` and Release build |
| `src/OpenHab.Windows.Tray/Shortcuts/ShortcutSettingsControls.cs` | WinUI shortcut settings controls after shortcut formatting extraction | Shortcut tests and Release build |
| `src/OpenHab.Windows.Tray/Tray/TrayIconService.cs` | `NotifyIcon` shell integration | Release build |
| `src/OpenHab.Windows.Tray/Tray/TrayFlyoutPositioner.cs` | Windows AppWindow/display-area positioning glue | Release build and smoke test |
| `src/OpenHab.Windows.Tray/Startup/StartupManager.cs` | Windows startup registry integration | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsBatteryInfoReader.cs` | Live OS battery reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsBluetoothInfoReader.cs` | Live OS Bluetooth reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsNetworkInfoReader.cs` | Live OS network reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsSessionInfoReader.cs` | Live OS session reader | Release build |
| `src/OpenHab.Windows.Tray/DeviceInfo/WindowsDeviceStateSnapshotSource.cs` | Composition of live OS readers | Release build |
| `src/OpenHab.Windows.Notifications/ShortcutRegistrar.cs` | Windows notification registration | Notification tests and Release build |
| `src/OpenHab.Windows.Notifications/ToastService.cs` | Windows toast API integration | Notification tests and Release build |
| `src/OpenHab.Core/Auth/WindowsCredentialStore.cs` | Windows Credential Manager wrapper | Core auth tests for non-Windows abstractions and Release build |
