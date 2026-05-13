using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Settings;
using OpenHab.App.Shortcuts;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Shortcuts;
using OpenHab.Windows.Tray.Startup;

namespace OpenHab.Windows.Tray.Settings;

public sealed partial class SettingsPageControl : UserControl
{
    private sealed record AppColorThemeOption(string Label, AppColorTheme Theme)
    {
        public override string ToString() => Label;
    }

    private static readonly AppColorThemeOption[] AppColorThemeOptions =
    [
        new("Dark", AppColorTheme.Dark),
        new("Bright", AppColorTheme.Bright),
        new("Follow System Settings", AppColorTheme.FollowSystemSettings)
    ];

    private enum SettingsPage
    {
        Root,
        Connection,
        General,
        Appearance,
        DeviceInfoSync,
        Shortcuts,
        About
    }

    private readonly AppSettingsController settingsController;
    private readonly Func<Task> refreshRuntimeAsync;
    private readonly Action<string> setStatusText;
    private bool suppressTokenEditTracking;
    private bool isRefreshingSettingsBindings;
    private bool localTokenEdited;
    private bool cloudTokenEdited;
    private bool cloudUserNameEdited;
    private bool suppressFlyoutWidthChange;

    private ComboBox? SkinCombo;
    private ComboBox? EndpointModeCombo;
    private TextBox? LocalEndpointText;
    private TextBox? CloudEndpointText;
    private PasswordBox? LocalTokenBox;
    private TextBox? CloudUserNameText;
    private PasswordBox? CloudPasswordBox;
    private ComboBox? AppColorThemeCombo;
    private ToggleSwitch? UseWin11IconsToggle;
    private ToggleSwitch? LaunchAtStartupToggle;
    private NumberBox? FlyoutWidthBox;
    private NumberBox? NotificationPollBox;
    private TextBox? ImportantNotificationTagsText;
    private ToggleSwitch? DeviceInfoSyncEnabledToggle;
    private ToggleSwitch? CommandMenuEnabledToggle;
    private ShortcutRecorderControl? CommandMenuShortcutRecorder;
    private ComboBox? CommandMenuActivationModeCombo;
    private string? editingShortcutActionId;
    private bool creatingShortcutAction;
    private TextBox? ShortcutActionNameText;
    private ComboBox? ShortcutActionIconCombo;
    private ToggleSwitch? ShortcutActionShowInCommandMenuToggle;
    private ShortcutRecorderControl? ShortcutActionGlobalShortcutRecorder;
    private TextBox? ShortcutActionTargetItemText;
    private ComboBox? ShortcutActionTypeCombo;
    private TextBox? ShortcutActionValueText;
    private TextBlock? ShortcutActionEditorErrorText;
    private TextBlock? DeviceInfoSyncDisabledText;
    private TextBox? DeviceInfoSyncIdentifierText;
    private NumberBox? DeviceInfoSyncIntervalBox;
    private readonly Dictionary<string, TextBox> deviceInfoSyncItemMappingTexts = new(StringComparer.Ordinal);
    private Button? ViewLogsButton;
    private TextBlock? VersionText;

    public SettingsPageControl(
        AppSettingsController settingsController,
        Func<Task> refreshRuntimeAsync,
        Action<string> setStatusText)
    {
        this.settingsController = settingsController;
        this.refreshRuntimeAsync = refreshRuntimeAsync;
        this.setStatusText = setStatusText;
        InitializeComponent();
        InitializeSettingsControls();
        RefreshSettingsBindings();
    }

    public void ShowRoot()
    {
        NavigateToSettingsPage(SettingsPage.Root);
    }

    private void InitializeSettingsControls()
    {
        NavigateToSettingsPage(SettingsPage.Root);
    }

    private void NavigateToSettingsPage(SettingsPage page)
    {
        ResetSettingsControlReferences();
        SettingsContent.Children.Clear();

        switch (page)
        {
            case SettingsPage.Root:
                UpdateSettingsBreadcrumb(null);
                SettingsSubtitleText.Text = "Choose a category";
                SettingsContent.Children.Add(CreateCategoryRow("\uE713", "Connection", "Endpoints and credentials", SettingsPage.Connection));
                SettingsContent.Children.Add(CreateCategoryRow("\uE770", "General", "Startup, flyout width, notifications", SettingsPage.General));
                SettingsContent.Children.Add(CreateCategoryRow("\uE790", "Appearance", "Skin, theme, icon style", SettingsPage.Appearance));
                SettingsContent.Children.Add(CreateCategoryRow("\uE7F4", "Device Info Sync", "Configure device metadata sync", SettingsPage.DeviceInfoSync));
                SettingsContent.Children.Add(CreateCategoryRow("\uE765", "Shortcuts", "Command menu and global shortcuts", SettingsPage.Shortcuts));
                SettingsContent.Children.Add(CreateCategoryRow("\uE946", "About", "Logs and version", SettingsPage.About));
                break;
            case SettingsPage.Connection:
                UpdateSettingsBreadcrumb("Connection");
                SettingsSubtitleText.Text = "Endpoints and credentials";
                BuildConnectionSettingsPage();
                break;
            case SettingsPage.General:
                UpdateSettingsBreadcrumb("General");
                SettingsSubtitleText.Text = "Startup and runtime behavior";
                BuildGeneralSettingsPage();
                break;
            case SettingsPage.Appearance:
                UpdateSettingsBreadcrumb("Appearance");
                SettingsSubtitleText.Text = "Visual options";
                BuildAppearanceSettingsPage();
                break;
            case SettingsPage.DeviceInfoSync:
                UpdateSettingsBreadcrumb("Device Info Sync");
                SettingsSubtitleText.Text = "Configure device metadata sync";
                BuildDeviceInfoSyncSettingsPage();
                break;
            case SettingsPage.Shortcuts:
                UpdateSettingsBreadcrumb("Shortcuts");
                SettingsSubtitleText.Text = "Configure global shortcuts and command menu actions.";
                BuildShortcutsSettingsPage();
                break;
            case SettingsPage.About:
                UpdateSettingsBreadcrumb("About");
                SettingsSubtitleText.Text = "Diagnostics and version";
                BuildAboutSettingsPage();
                break;
        }

        RefreshSettingsBindings();
    }

    private void UpdateSettingsBreadcrumb(string? pageTitle)
    {
        var isRoot = string.IsNullOrWhiteSpace(pageTitle);
        SettingsBreadcrumbRootButton.Visibility = isRoot ? Visibility.Collapsed : Visibility.Visible;
        SettingsBreadcrumbChevron.Visibility = isRoot ? Visibility.Collapsed : Visibility.Visible;
        SettingsTitleText.Text = isRoot ? "Settings" : pageTitle;
    }

    private void AddSettingsSectionTitle(string title)
    {
        SettingsContent.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        });
    }

    private Button CreateCategoryRow(string glyph, string title, string subtitle, SettingsPage destination)
    {
        var button = new Button
        {
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var textPanel = new StackPanel { Spacing = 2 };
        textPanel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        textPanel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Opacity = 0.68,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        });
        Grid.SetColumn(textPanel, 1);
        row.Children.Add(textPanel);

        var chevron = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75
        };
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);

        button.Content = row;
        button.Click += async (_, _) => await NavigateToSettingsPageWithDiscardConfirmationAsync(destination);
        return button;
    }

    private void BuildConnectionSettingsPage()
    {
        EndpointModeCombo = new ComboBox
        {
            Width = 220
        };
        EndpointModeCombo.ItemsSource = Enum.GetValues<EndpointMode>();
        EndpointModeCombo.SelectionChanged += EndpointModeCombo_SelectionChanged;
        var endpointModeRow = CreateSettingsControlRow(
            "\uE713",
            "Endpoint mode",
            "Choose how the app selects local or cloud connectivity",
            EndpointModeCombo);

        LocalEndpointText = new TextBox
        {
            Width = 520
        };
        LocalEndpointText.LostFocus += EndpointText_LostFocus;
        var localEndpointRow = CreateSettingsControlRow(
            "\uE839",
            "Local endpoint",
            "Base URL for your openHAB server on the local network",
            LocalEndpointText);

        CloudEndpointText = new TextBox
        {
            Width = 520
        };
        CloudEndpointText.LostFocus += EndpointText_LostFocus;
        var cloudEndpointRow = CreateSettingsControlRow(
            "\uE753",
            "Cloud endpoint",
            "Base URL for the myopenHAB cloud service",
            CloudEndpointText);

        LocalTokenBox = new PasswordBox
        {
            PlaceholderText = "Enter token (optional)",
            Tag = "Local",
            Width = 520
        };
        LocalTokenBox.GotFocus += TokenBox_GotFocus;
        LocalTokenBox.PasswordChanged += TokenBox_PasswordChanged;
        LocalTokenBox.LostFocus += TokenBox_LostFocus;
        var localTokenRow = CreateSettingsControlRow(
            "\uE72E",
            "Local API token",
            "Optional bearer token used with the local endpoint",
            LocalTokenBox);

        CloudUserNameText = new TextBox
        {
            PlaceholderText = "Enter myopenHAB email",
            Width = 520
        };
        CloudUserNameText.TextChanged += CloudUserNameText_TextChanged;
        CloudUserNameText.LostFocus += CloudCredentials_LostFocus;
        var cloudUserNameRow = CreateSettingsControlRow(
            "\uE77B",
            "Cloud email / username",
            "Account used to sign in to myopenHAB",
            CloudUserNameText);

        CloudPasswordBox = new PasswordBox
        {
            PlaceholderText = "Enter myopenHAB password",
            Width = 520
        };
        CloudPasswordBox.PasswordChanged += CloudPasswordBox_PasswordChanged;
        CloudPasswordBox.LostFocus += CloudCredentials_LostFocus;
        var cloudPasswordRow = CreateSettingsControlRow(
            "\uE72E",
            "Cloud password",
            "Password used only for the configured cloud account",
            CloudPasswordBox);

        SettingsContent.Children.Add(CreateSettingsGroup(
            endpointModeRow,
            localEndpointRow,
            cloudEndpointRow,
            localTokenRow,
            cloudUserNameRow,
            cloudPasswordRow));
    }

    private void BuildGeneralSettingsPage()
    {
        LaunchAtStartupToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty
        };
        LaunchAtStartupToggle.Toggled += LaunchAtStartupToggle_Toggled;
        var launchRow = CreateSettingsToggleRow(
            "\uE7C1",
            "Launch at startup",
            "Start openHAB automatically when you sign in to Windows",
            LaunchAtStartupToggle);

        FlyoutWidthBox = new NumberBox
        {
            Minimum = AppSettingsController.MinFlyoutWidth,
            Maximum = AppSettingsController.MaxFlyoutWidth,
            SmallChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Width = 220
        };
        FlyoutWidthBox.ValueChanged += FlyoutWidthBox_ValueChanged;
        var flyoutWidthRow = CreateSettingsControlRow(
            "\uE7F4",
            "Flyout width",
            "Width of the tray flyout in pixels",
            FlyoutWidthBox);

        NotificationPollBox = new NumberBox
        {
            Minimum = AppSettingsController.MinNotificationPollIntervalSeconds,
            Maximum = AppSettingsController.MaxNotificationPollIntervalSeconds,
            SmallChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Width = 220
        };
        NotificationPollBox.ValueChanged += NotificationPollBox_ValueChanged;
        var notificationPollRow = CreateSettingsControlRow(
            "\uE7F4",
            "Notification check interval",
            "How often the app checks for openHAB notifications, in seconds",
            NotificationPollBox);

        ImportantNotificationTagsText = new TextBox
        {
            PlaceholderText = "critical, warning, security",
            Width = 320
        };
        ImportantNotificationTagsText.LostFocus += ImportantNotificationTagsText_LostFocus;
        var importantTagsRow = CreateSettingsControlRow(
            "\uE7BA",
            "Important notification tags",
            "Comma-separated tags/severities that should be sent as important notifications",
            ImportantNotificationTagsText);

        SettingsContent.Children.Add(CreateSettingsGroup(launchRow, flyoutWidthRow, notificationPollRow, importantTagsRow));
    }

    private void BuildAppearanceSettingsPage()
    {
        SkinCombo = new ComboBox
        {
            Width = 220
        };
        SkinCombo.ItemsSource = Enum.GetValues<SitemapSkinKind>();
        SkinCombo.SelectionChanged += SkinCombo_SelectionChanged;
        var skinRow = CreateSettingsControlRow(
            "\uE790",
            "Skin",
            "Choose the sitemap rendering style",
            SkinCombo);

        AppColorThemeCombo = new ComboBox
        {
            Width = 220,
            ItemsSource = AppColorThemeOptions
        };
        AppColorThemeCombo.SelectionChanged += AppColorThemeCombo_SelectionChanged;
        var themeRow = CreateSettingsControlRow(
            "\uE771",
            "App color theme",
            "Choose the main window and flyout color mode",
            AppColorThemeCombo);

        UseWin11IconsToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty
        };
        UseWin11IconsToggle.Toggled += UseWin11IconsToggle_Toggled;
        var iconStyleRow = CreateSettingsToggleRow(
            "\uE8A5",
            "Use Windows 11 style icons",
            "Prefer Fluent-style symbols for sitemap widgets",
            UseWin11IconsToggle);

        SettingsContent.Children.Add(CreateSettingsGroup(skinRow, themeRow, iconStyleRow));
    }

    private void BuildDeviceInfoSyncSettingsPage()
    {
        var syncContent = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        DeviceInfoSyncEnabledToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty
        };
        DeviceInfoSyncEnabledToggle.Toggled += DeviceInfoSyncEnabledToggle_Toggled;
        var enabledAction = CreateSettingsToggleAction(DeviceInfoSyncEnabledToggle);

        var current = settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default;
        if (!current.IsEnabled)
        {
            DeviceInfoSyncDisabledText = new TextBlock
            {
                Text = "Device Info Sync is disabled. Turn it on to configure identifier, interval, and item mappings.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            };
            syncContent.Children.Add(DeviceInfoSyncDisabledText);
            SettingsContent.Children.Add(CreateSettingsExpander(
                "Device Info Sync",
                "Send selected Windows device state to openHAB Items",
                syncContent,
                enabledAction));
            return;
        }

        DeviceInfoSyncIdentifierText = new TextBox
        {
            Header = "Device identifier"
        };
        DeviceInfoSyncIdentifierText.LostFocus += DeviceInfoSyncField_LostFocus;
        syncContent.Children.Add(DeviceInfoSyncIdentifierText);

        DeviceInfoSyncIntervalBox = new NumberBox
        {
            Header = "Sync interval (minutes)",
            Minimum = DeviceInfoSyncSettings.MinSyncIntervalMinutes,
            Maximum = DeviceInfoSyncSettings.MaxSyncIntervalMinutes,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        DeviceInfoSyncIntervalBox.ValueChanged += DeviceInfoSyncIntervalBox_ValueChanged;
        syncContent.Children.Add(DeviceInfoSyncIntervalBox);

        SettingsContent.Children.Add(CreateSettingsExpander(
            "Device Info Sync",
            "Send selected Windows device state to openHAB Items",
            syncContent,
            enabledAction));

        AddSettingsSectionTitle("openHAB Item mappings");
        var mappingContent = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AddDeviceInfoSyncMappingTextBox(mappingContent, "BatteryLevelItem", "Battery level", "BatteryLevel");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "ChargingStateItem", "Charging state", "ChargingState");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "LockedStateItem", "Locked state", "LockedState");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "SessionStateItem", "Session state", "SessionState");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "WifiConnectedItem", "Wi-Fi connected", "WifiConnected");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "WifiNameItem", "Wi-Fi name", "WifiName");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "OpenHabConnectionItem", "openHAB connection", "OpenHabConnection");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "FocusStateItem", "Focus / DND", "FocusState");
        SettingsContent.Children.Add(CreateSettingsExpander(
            "Item suffixes",
            "The device identifier is added automatically before each suffix",
            mappingContent));
    }

    private void AddDeviceInfoSyncMappingTextBox(StackPanel target, string key, string title, string placeholder)
    {
        var textBox = new TextBox
        {
            Header = title,
            PlaceholderText = placeholder,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        textBox.LostFocus += DeviceInfoSyncField_LostFocus;
        deviceInfoSyncItemMappingTexts[key] = textBox;
        target.Children.Add(textBox);
    }

    private static Border CreateSettingsGroup(params FrameworkElement[] rows)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        for (var index = 0; index < rows.Length; index++)
        {
            stack.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = index == rows.Length - 1 ? new Thickness(0) : new Thickness(0, 0, 0, 1),
                Child = rows[index]
            });
        }

        return new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static Grid CreateSettingsToggleRow(string glyph, string title, string subtitle, ToggleSwitch toggle)
    {
        return CreateSettingsControlRow(glyph, title, subtitle, CreateSettingsToggleAction(toggle));
    }

    private static StackPanel CreateExpanderRows(params FrameworkElement[] rows)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        for (var index = 0; index < rows.Length; index++)
        {
            stack.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = index == rows.Length - 1 ? new Thickness(0) : new Thickness(0, 0, 0, 1),
                Child = rows[index]
            });
        }

        return stack;
    }

    private static Grid CreateSettingsControlRow(string glyph, string title, string subtitle, FrameworkElement control)
    {
        var row = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            Opacity = 0.82,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var textPanel = new StackPanel { Spacing = 2 };
        textPanel.Children.Add(new TextBlock
        {
            Text = title,
            TextWrapping = TextWrapping.Wrap
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Opacity = 0.68,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(textPanel, 1);
        row.Children.Add(textPanel);

        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 2);
        row.Children.Add(control);

        return row;
    }

    private static FrameworkElement CreateCommandMenuPreview(IEnumerable<ShortcutAction> actions)
    {
        var visibleActions = actions
            .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid)
            .Take(6)
            .ToArray();

        if (visibleActions.Length == 0)
        {
            return new TextBlock
            {
                Text = "No actions",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var preview = new Canvas
        {
            Width = 220,
            Height = 132,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var center = CreatePreviewNode("\uE711", "Close", 50, true);
        Canvas.SetLeft(center, 85);
        Canvas.SetTop(center, 41);
        preview.Children.Add(center);

        var radius = 48d;
        var centerX = 110d;
        var centerY = 66d;
        for (var i = 0; i < visibleActions.Length; i++)
        {
            var action = visibleActions[i];
            var angle = ((Math.PI * 2d) * i / visibleActions.Length) - (Math.PI / 2d);
            var node = CreatePreviewNode(RadialCommandMenuWindow.ResolveShortcutGlyph(action.IconId), action.Name, 38, false);
            Canvas.SetLeft(node, centerX + (Math.Cos(angle) * radius) - 19);
            Canvas.SetTop(node, centerY + (Math.Sin(angle) * radius) - 19);
            preview.Children.Add(node);
        }

        return preview;
    }

    private static Border CreatePreviewNode(string glyph, string label, double size, bool isCenter)
    {
        var node = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2d),
            Background = (Brush)Application.Current.Resources[
                isCenter ? "AccentFillColorSecondaryBrush" : "SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = isCenter ? 16 : 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        ToolTipService.SetToolTip(node, label);
        return node;
    }

    private static StackPanel CreateSettingsToggleAction(ToggleSwitch toggle)
    {
        var stateText = new TextBlock
        {
            Text = toggle.IsOn ? "On" : "Off",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 24
        };
        toggle.Toggled += (_, _) => stateText.Text = toggle.IsOn ? "On" : "Off";
        toggle.VerticalAlignment = VerticalAlignment.Center;

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionPanel.Children.Add(stateText);
        actionPanel.Children.Add(toggle);
        return actionPanel;
    }

    private void BuildAboutSettingsPage()
    {
        ViewLogsButton = new Button
        {
            Content = "View diagnostic logs",
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
        };
        ViewLogsButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        ViewLogsButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        ViewLogsButton.Click += ViewLogsButton_Click;
        SettingsContent.Children.Add(ViewLogsButton);

        VersionText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Opacity = 0.5,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        };
        SettingsContent.Children.Add(VersionText);
    }

    private void BuildShortcutsSettingsPage()
    {
        var settings = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();

        AddSettingsSectionTitle("Built-in shortcuts");

        CommandMenuEnabledToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            IsOn = settings.CommandMenu.Enabled
        };
        AutomationProperties.SetName(CommandMenuEnabledToggle, "Enable openHAB command menu shortcut");
        CommandMenuEnabledToggle.Toggled += CommandMenuEnabledToggle_Toggled;
        var globalShortcutRow = CreateSettingsControlRow(
            "\uE765",
            "Global shortcut",
            "Keyboard shortcut for opening command menu from anywhere",
            CreateCommandMenuShortcutRecorder(settings.CommandMenu.Binding));

        CommandMenuActivationModeCombo = new ComboBox
        {
            Width = 220,
            ItemsSource = Enum.GetValues<RadialActivationMode>(),
            SelectedItem = settings.CommandMenu.RadialActivationMode
        };
        AutomationProperties.SetName(CommandMenuActivationModeCombo, "Command menu activation mode");
        CommandMenuActivationModeCombo.SelectionChanged += CommandMenuActivationModeCombo_SelectionChanged;
        var activationModeRow = CreateSettingsControlRow(
            "\uE7C1",
            "Activation mode",
            "Choose whether the command menu toggles or stays open while held",
            CommandMenuActivationModeCombo);
        var previewRow = CreateSettingsControlRow(
            "\uE8FD",
            "Command menu preview",
            "Actions currently visible in the radial command menu",
            CreateCommandMenuPreview(settings.Actions));

        SettingsContent.Children.Add(CreateSettingsExpander(
            "openHAB Command Menu",
            "Built-in global shortcut for opening the command menu",
            CreateExpanderRows(globalShortcutRow, activationModeRow, previewRow),
            CreateSettingsToggleAction(CommandMenuEnabledToggle)));

        var voiceModeStateRow = CreateSettingsControlRow(
            "\uE720",
            "Voice mode",
            "Coming soon. Voice shortcut is currently unassigned and unavailable.",
            new TextBlock
            {
                Text = "Disabled",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            });

        var voiceModeShortcutRow = CreateSettingsControlRow(
            "\uE765",
            "Shortcut",
            "No shortcut assigned yet",
            ShortcutSettingsControls.CreateShortcutChips(null));

        var voiceModeContent = CreateExpanderRows(voiceModeStateRow, voiceModeShortcutRow);
        voiceModeContent.Opacity = 0.72;
        SettingsContent.Children.Add(CreateSettingsExpander(
            "Voice Mode",
            "Planned voice shortcut, coming soon",
            voiceModeContent,
            new TextBlock
            {
                Text = "Disabled",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            },
            isExpanded: false));

        var actionsHeader = new Grid
        {
            ColumnSpacing = 10
        };
        actionsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionsHeader.Children.Add(new TextBlock
        {
            Text = "Actions and shortcuts",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var addActionButton = new Button
        {
            Content = "Add action",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        addActionButton.Click += AddShortcutActionButton_Click;
        Grid.SetColumn(addActionButton, 1);
        actionsHeader.Children.Add(addActionButton);
        SettingsContent.Children.Add(actionsHeader);

        var actionsCardStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var actionHeader = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(12, 8, 12, 8)
        };
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        AddActionTableHeaderCell(actionHeader, "Icon", 0);
        AddActionTableHeaderCell(actionHeader, "Action name", 1);
        AddActionTableHeaderCell(actionHeader, "Availability", 2);
        AddActionTableHeaderCell(actionHeader, "Shortcut", 3);
        AddActionTableHeaderCell(actionHeader, "Target item", 4);
        AddActionTableHeaderCell(actionHeader, "Action type", 5);
        AddActionTableHeaderCell(actionHeader, "Command value", 6);
        AddActionTableHeaderCell(actionHeader, "Actions", 7);
        actionsCardStack.Children.Add(actionHeader);

        if (settings.Actions.Length == 0)
        {
            actionsCardStack.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 12, 12, 12),
                Child = new TextBlock
                {
                    Text = "No actions yet." + Environment.NewLine + "Add actions to make them available in the command menu.",
                    Opacity = 0.72,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }
        else
        {
            for (var i = 0; i < settings.Actions.Length; i++)
            {
                actionsCardStack.Children.Add(CreateShortcutActionRow(settings.Actions[i]));
            }
        }

        SettingsContent.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = actionsCardStack
            }
        });

        var draftAction = ResolveShortcutActionDraft(settings.Actions);
        if (draftAction is null)
        {
            return;
        }

        AddSettingsSectionTitle(creatingShortcutAction ? "Add action" : "Edit action");
        ShortcutActionNameText = new TextBox { Text = draftAction.Name };
        var nameRow = CreateSettingsControlRow("\uE8D2", "Action name", "Display name used in settings and command menu", ShortcutActionNameText);

        ShortcutActionIconCombo = new ComboBox
        {
            Width = 280,
            ItemsSource = ShortcutIconCatalog.All,
            DisplayMemberPath = "Label"
        };
        ShortcutActionIconCombo.SelectedItem = ShortcutIconCatalog.All.FirstOrDefault(icon => string.Equals(icon.Id, draftAction.IconId, StringComparison.Ordinal))
            ?? ShortcutIconCatalog.All.FirstOrDefault(icon => string.Equals(icon.Id, "custom", StringComparison.Ordinal));
        var iconRow = CreateSettingsControlRow("\uE8D4", "Icon", "Select an icon from the shortcut icon catalog", ShortcutActionIconCombo);

        ShortcutActionShowInCommandMenuToggle = new ToggleSwitch
        {
            IsOn = draftAction.ShowInCommandMenu,
            OnContent = string.Empty,
            OffContent = string.Empty
        };
        var showInMenuRow = CreateSettingsControlRow("\uE8FD", "Show in command menu", "Controls whether this action appears in the command menu", CreateSettingsToggleAction(ShortcutActionShowInCommandMenuToggle));

        ShortcutActionGlobalShortcutRecorder = new ShortcutRecorderControl
        {
            Binding = draftAction.GlobalShortcut,
            AllowClear = true,
            Error = null
        };
        var globalShortcutEditorRow = CreateSettingsControlRow("\uE765", "Global shortcut", "Optional shortcut. Leave unassigned if not needed", ShortcutActionGlobalShortcutRecorder);

        ShortcutActionTargetItemText = new TextBox
        {
            Text = draftAction.TargetItem
        };
        var targetItemRow = CreateSettingsControlRow("\uE7F4", "Target item", "Enter openHAB item name manually for now", ShortcutActionTargetItemText);

        ShortcutActionTypeCombo = new ComboBox
        {
            Width = 280,
            ItemsSource = Enum.GetValues<ShortcutCommandType>(),
            SelectedItem = draftAction.CommandType
        };
        var typeRow = CreateSettingsControlRow("\uE8EF", "Action type", "Choose command behavior", ShortcutActionTypeCombo);

        ShortcutActionValueText = new TextBox
        {
            Text = draftAction.CommandValue ?? string.Empty
        };
        var commandValueRow = CreateSettingsControlRow("\uE756", "Command value", "Required for SendCommand, and constrained for OnOff/OpenClose", ShortcutActionValueText);

        var actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var saveButton = new Button { Content = "Save" };
        saveButton.Click += SaveShortcutActionButton_Click;
        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelShortcutActionButton_Click;
        actionButtons.Children.Add(saveButton);
        actionButtons.Children.Add(cancelButton);

        ShortcutActionEditorErrorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };

        SettingsContent.Children.Add(ShortcutSettingsControls.CreateSettingsCard(
            nameRow,
            iconRow,
            showInMenuRow,
            globalShortcutEditorRow,
            targetItemRow,
            typeRow,
            commandValueRow,
            new Grid
            {
                Padding = new Thickness(12, 12, 12, 8),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            ShortcutActionEditorErrorText,
                            actionButtons
                        }
                    }
                }
            }));
    }

    private Expander CreateSettingsExpander(
        string title,
        string subtitle,
        UIElement content,
        UIElement? action = null,
        bool isExpanded = true)
    {
        if (content is FrameworkElement contentElement)
        {
            contentElement.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        var contentHost = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content
        };

        var expander = new Expander
        {
            Header = CreateSettingsHeader(title, subtitle, action),
            Content = contentHost,
            IsExpanded = isExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        return expander;
    }

    private static Grid CreateSettingsHeader(string title, string subtitle, UIElement? action)
    {
        var header = new Grid
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            ColumnSpacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel { Spacing = 2 };
        textPanel.Children.Add(new TextBlock { Text = title });
        textPanel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Opacity = 0.68,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(textPanel);

        if (action is FrameworkElement actionElement)
        {
            actionElement.Margin = new Thickness(0, 0, 24, 0);
            Grid.SetColumn(actionElement, 1);
            header.Children.Add(actionElement);
        }

        return header;
    }

    private void ResetSettingsControlReferences()
    {
        SkinCombo = null;
        EndpointModeCombo = null;
        LocalEndpointText = null;
        CloudEndpointText = null;
        LocalTokenBox = null;
        CloudUserNameText = null;
        CloudPasswordBox = null;
        AppColorThemeCombo = null;
        UseWin11IconsToggle = null;
        LaunchAtStartupToggle = null;
        FlyoutWidthBox = null;
        NotificationPollBox = null;
        ImportantNotificationTagsText = null;
        DeviceInfoSyncEnabledToggle = null;
        CommandMenuEnabledToggle = null;
        CommandMenuShortcutRecorder = null;
        CommandMenuActivationModeCombo = null;
        ShortcutActionNameText = null;
        ShortcutActionIconCombo = null;
        ShortcutActionShowInCommandMenuToggle = null;
        ShortcutActionGlobalShortcutRecorder = null;
        ShortcutActionTargetItemText = null;
        ShortcutActionTypeCombo = null;
        ShortcutActionValueText = null;
        ShortcutActionEditorErrorText = null;
        DeviceInfoSyncDisabledText = null;
        DeviceInfoSyncIdentifierText = null;
        DeviceInfoSyncIntervalBox = null;
        deviceInfoSyncItemMappingTexts.Clear();
        ViewLogsButton = null;
        VersionText = null;
    }

    private void RefreshSettingsBindings()
    {
        isRefreshingSettingsBindings = true;
        try
        {
            if (SkinCombo is not null)
            {
                SkinCombo.SelectedItem = settingsController.Current.Skin;
            }
            if (EndpointModeCombo is not null)
            {
                EndpointModeCombo.SelectedItem = settingsController.Current.EndpointMode;
            }
            if (LocalEndpointText is not null)
            {
                LocalEndpointText.Text = settingsController.Current.LocalEndpoint.ToString();
            }
            if (CloudEndpointText is not null)
            {
                CloudEndpointText.Text = settingsController.Current.CloudEndpoint.ToString();
            }

            suppressTokenEditTracking = true;
            if (LocalTokenBox is not null)
            {
                LocalTokenBox.Password = string.Empty;
            }
            if (CloudPasswordBox is not null)
            {
                CloudPasswordBox.Password = string.Empty;
            }
            if (CloudUserNameText is not null)
            {
                CloudUserNameText.Text = settingsController.Current.CloudUserName ?? string.Empty;
            }
            suppressTokenEditTracking = false;

            if (AppColorThemeCombo is not null)
            {
                AppColorThemeCombo.SelectedItem = AppColorThemeOptions.First(option => option.Theme == settingsController.Current.AppColorTheme);
            }
            if (UseWin11IconsToggle is not null)
            {
                UseWin11IconsToggle.IsOn = settingsController.Current.UseWindows11Icons;
            }
            if (LaunchAtStartupToggle is not null)
            {
                LaunchAtStartupToggle.IsOn = settingsController.Current.LaunchAtStartup;
            }
            suppressFlyoutWidthChange = true;
            if (FlyoutWidthBox is not null)
            {
                FlyoutWidthBox.Value = settingsController.Current.FlyoutWidth;
            }
            if (NotificationPollBox is not null)
            {
                NotificationPollBox.Value = settingsController.Current.NotificationPollIntervalSeconds;
            }
            if (ImportantNotificationTagsText is not null)
            {
                ImportantNotificationTagsText.Text = string.Join(", ", settingsController.Current.ImportantNotificationTags);
            }

            var deviceInfoSync = settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default;
            var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
            if (DeviceInfoSyncEnabledToggle is not null)
            {
                DeviceInfoSyncEnabledToggle.IsOn = deviceInfoSync.IsEnabled;
            }
            if (CommandMenuEnabledToggle is not null)
            {
                CommandMenuEnabledToggle.IsOn = shortcuts.CommandMenu.Enabled;
            }
            if (CommandMenuShortcutRecorder is not null)
            {
                CommandMenuShortcutRecorder.Binding = shortcuts.CommandMenu.Binding;
                CommandMenuShortcutRecorder.Error = null;
            }
            if (CommandMenuActivationModeCombo is not null)
            {
                CommandMenuActivationModeCombo.SelectedItem = shortcuts.CommandMenu.RadialActivationMode;
            }
            if (DeviceInfoSyncIdentifierText is not null)
            {
                DeviceInfoSyncIdentifierText.Text = deviceInfoSync.DeviceIdentifier;
            }
            if (DeviceInfoSyncIntervalBox is not null)
            {
                DeviceInfoSyncIntervalBox.Value = deviceInfoSync.SyncIntervalMinutes;
            }
            SetDeviceInfoSyncMappingText("BatteryLevelItem", deviceInfoSync.BatteryLevelItem);
            SetDeviceInfoSyncMappingText("ChargingStateItem", deviceInfoSync.ChargingStateItem);
            SetDeviceInfoSyncMappingText("LockedStateItem", deviceInfoSync.LockedStateItem);
            SetDeviceInfoSyncMappingText("SessionStateItem", deviceInfoSync.SessionStateItem);
            SetDeviceInfoSyncMappingText("WifiConnectedItem", deviceInfoSync.WifiConnectedItem);
            SetDeviceInfoSyncMappingText("WifiNameItem", deviceInfoSync.WifiNameItem);
            SetDeviceInfoSyncMappingText("OpenHabConnectionItem", deviceInfoSync.OpenHabConnectionItem);
            SetDeviceInfoSyncMappingText("FocusStateItem", deviceInfoSync.FocusStateItem);
            suppressFlyoutWidthChange = false;

            if (LocalTokenBox is not null)
            {
                LocalTokenBox.PlaceholderText = settingsController.Current.HasLocalToken
                    ? "Stored token configured. Type to replace, or leave unchanged."
                    : "Enter token (optional)";
            }
            if (CloudPasswordBox is not null)
            {
                CloudPasswordBox.PlaceholderText = settingsController.Current.HasCloudCredentials
                    ? "Stored password configured. Type to replace, or leave unchanged."
                    : "Enter myopenHAB password";
            }
            if (VersionText is not null)
            {
                var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
                VersionText.Text = $"openHAB Windows App v{version}";
            }
            localTokenEdited = false;
            cloudTokenEdited = false;
            cloudUserNameEdited = false;
        }
        finally
        {
            suppressTokenEditTracking = false;
            suppressFlyoutWidthChange = false;
            isRefreshingSettingsBindings = false;
        }
    }

    private async void SkinCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ComboBox skinCombo || skinCombo.SelectedItem is not SitemapSkinKind skin)
        {
            return;
        }

        settingsController.SetSkin(skin);
        await refreshRuntimeAsync();
    }

    private async void EndpointModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ComboBox endpointModeCombo || endpointModeCombo.SelectedItem is not EndpointMode endpointMode)
        {
            return;
        }

        settingsController.SetEndpointMode(endpointMode);
        await refreshRuntimeAsync();
    }

    private async void EndpointText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings || LocalEndpointText is null || CloudEndpointText is null)
        {
            return;
        }

        if (!Uri.TryCreate(LocalEndpointText.Text, UriKind.Absolute, out var localEndpoint)
            || !Uri.TryCreate(CloudEndpointText.Text, UriKind.Absolute, out var cloudEndpoint))
        {
            RefreshSettingsBindings();
            return;
        }

        try
        {
            settingsController.SetEndpoints(localEndpoint, cloudEndpoint);
            await refreshRuntimeAsync();
        }
        catch (ArgumentException)
        {
            RefreshSettingsBindings();
        }
    }

    private async void TokenBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || box.Tag is not string tag) return;
        if (isRefreshingSettingsBindings) return;

        var wasEdited = IsTokenBoxEdited(tag);
        if (!wasEdited)
        {
            return;
        }

        var transportKind = tag == "Local" ? TransportKind.Local : TransportKind.Cloud;
        var token = box.Password;

        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                await settingsController.ClearApiTokenAsync(transportKind, CancellationToken.None);
                await refreshRuntimeAsync();
            }
            else
            {
                await settingsController.SetApiTokenAsync(transportKind, token, CancellationToken.None);
                await refreshRuntimeAsync();
            }
        }
        catch (Exception ex)
        {
            setStatusText($"Failed to save token: {ex.Message}");
            RefreshSettingsBindings();
        }
    }

    private void TokenBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || box.Tag is not string tag || suppressTokenEditTracking || isRefreshingSettingsBindings)
        {
            return;
        }

        SetTokenBoxEdited(tag, false);
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box || box.Tag is not string tag || suppressTokenEditTracking || isRefreshingSettingsBindings)
        {
            return;
        }

        SetTokenBoxEdited(tag, true);
    }

    private bool IsTokenBoxEdited(string tag) => tag == "Local" ? localTokenEdited : cloudTokenEdited;

    private void SetTokenBoxEdited(string tag, bool edited)
    {
        if (tag == "Local")
        {
            localTokenEdited = edited;
            return;
        }

        cloudTokenEdited = edited;
    }

    private void CloudUserNameText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressTokenEditTracking || isRefreshingSettingsBindings)
        {
            return;
        }

        cloudUserNameEdited = true;
    }

    private void CloudPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (suppressTokenEditTracking || isRefreshingSettingsBindings)
        {
            return;
        }

        cloudTokenEdited = true;
    }

    private async void CloudCredentials_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings)
        {
            return;
        }

        if (CloudUserNameText is null || CloudPasswordBox is null)
        {
            return;
        }

        var activeCloudUserNameText = CloudUserNameText;
        var activeCloudPasswordBox = CloudPasswordBox;

        await Task.Yield();
        if (!ReferenceEquals(activeCloudUserNameText, CloudUserNameText)
            || !ReferenceEquals(activeCloudPasswordBox, CloudPasswordBox))
        {
            return;
        }

        if (activeCloudUserNameText.FocusState != FocusState.Unfocused
            || activeCloudPasswordBox.FocusState != FocusState.Unfocused)
        {
            return;
        }

        if (!cloudUserNameEdited && !cloudTokenEdited)
        {
            return;
        }

        var userName = activeCloudUserNameText.Text.Trim();
        var password = activeCloudPasswordBox.Password;

        try
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                await settingsController.ClearCloudCredentialsAsync(CancellationToken.None);
                await refreshRuntimeAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                setStatusText("Cloud password is required when username is set. Existing credentials were not changed.");
                RefreshSettingsBindings();
                return;
            }

            await settingsController.SetCloudCredentialsAsync(userName, password, CancellationToken.None);
            await refreshRuntimeAsync();
        }
        catch (Exception ex)
        {
            setStatusText($"Failed to save cloud credentials: {ex.Message}");
            RefreshSettingsBindings();
        }
    }

    private async void SettingsBreadcrumbRootButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToSettingsPageWithDiscardConfirmationAsync(SettingsPage.Root);
    }

    private void AppColorThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isRefreshingSettingsBindings
            && sender is ComboBox combo
            && combo.SelectedItem is AppColorThemeOption option)
        {
            settingsController.SetAppColorTheme(option.Theme);
        }
    }

    private void UseWin11IconsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!isRefreshingSettingsBindings && sender is ToggleSwitch toggle)
        {
            settingsController.SetUseWindows11Icons(toggle.IsOn);
        }
    }

    private async void LaunchAtStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var enabled = toggle.IsOn;
        settingsController.SetLaunchAtStartup(enabled);
        await StartupManager.SetEnabledAsync(enabled);
    }

    private void FlyoutWidthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (isRefreshingSettingsBindings || suppressFlyoutWidthChange || double.IsNaN(args.NewValue))
        {
            return;
        }

        var width = (int)Math.Round(args.NewValue);
        if (width < AppSettingsController.MinFlyoutWidth || width > AppSettingsController.MaxFlyoutWidth)
        {
            return;
        }

        settingsController.SetFlyoutWidth(width);
    }

    private void NotificationPollBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (isRefreshingSettingsBindings || suppressFlyoutWidthChange || double.IsNaN(args.NewValue))
        {
            return;
        }

        var seconds = (int)args.NewValue;
        if (seconds < AppSettingsController.MinNotificationPollIntervalSeconds
            || seconds > AppSettingsController.MaxNotificationPollIntervalSeconds)
        {
            return;
        }

        settingsController.SetNotificationPollInterval(seconds);
    }

    private void ImportantNotificationTagsText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not TextBox textBox)
        {
            return;
        }

        var tags = textBox.Text
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        settingsController.SetImportantNotificationTags(tags);
        textBox.Text = string.Join(", ", settingsController.Current.ImportantNotificationTags);
    }

    private void DeviceInfoSyncEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ToggleSwitch toggle)
        {
            return;
        }

        SaveDeviceInfoSyncSettings(enabledOverride: toggle.IsOn);
        NavigateToSettingsPage(SettingsPage.DeviceInfoSync);
    }

    private void CommandMenuEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        settingsController.SetShortcutSettings(shortcuts with
        {
            CommandMenu = shortcuts.CommandMenu with
            {
                Enabled = toggle.IsOn
            }
        });
    }

    private void CommandMenuActivationModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ComboBox combo || combo.SelectedItem is not RadialActivationMode mode)
        {
            return;
        }

        var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        settingsController.SetShortcutSettings(shortcuts with
        {
            CommandMenu = shortcuts.CommandMenu with
            {
                RadialActivationMode = mode
            }
        });
    }

    private ShortcutRecorderControl CreateCommandMenuShortcutRecorder(ShortcutBinding? binding)
    {
        CommandMenuShortcutRecorder = new ShortcutRecorderControl
        {
            Binding = binding,
            Error = null
        };
        CommandMenuShortcutRecorder.BindingChanged += CommandMenuShortcutRecorder_BindingChanged;
        return CommandMenuShortcutRecorder;
    }

    private void CommandMenuShortcutRecorder_BindingChanged(object? sender, ShortcutBinding? binding)
    {
        if (isRefreshingSettingsBindings || sender is not ShortcutRecorderControl recorder)
        {
            return;
        }

        var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        var existingBindings = shortcuts.Actions
            .Where(action => action.GlobalShortcut is not null)
            .Select(action => new ShortcutBindingOwner($"Action: {action.Name}", action.GlobalShortcut!))
            .ToArray();
        var validation = ShortcutValidation.ValidateBinding(
            binding,
            "openHAB Command Menu",
            existingBindings,
            allowUnassigned: false);
        if (!validation.IsValid)
        {
            recorder.Error = string.Join(Environment.NewLine, validation.Errors);
            recorder.Binding = shortcuts.CommandMenu.Binding;
            return;
        }

        recorder.Error = null;
        settingsController.SetShortcutSettings(shortcuts with
        {
            CommandMenu = shortcuts.CommandMenu with
            {
                Binding = binding
            }
        });
    }

    private static void AddActionTableHeaderCell(Grid row, string text, int column)
    {
        var cell = new TextBlock
        {
            Text = text,
            Opacity = 0.7,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private FrameworkElement CreateShortcutActionRow(ShortcutAction action)
    {
        var row = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(12, 8, 12, 8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

        AddActionTableCell(row, DescribeActionIcon(action.IconId), 0);
        AddActionTableCell(row, action.Name, 1);
        AddActionTableCell(row, DescribeActionAvailability(action), 2);
        AddActionTableCell(row, ShortcutBindingFormatter.Format(action.GlobalShortcut), 3);
        AddActionTableCell(row, action.TargetItem, 4);
        AddActionTableCell(row, action.CommandType.ToString(), 5);
        AddActionTableCell(row, action.CommandValue ?? "-", 6);

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        var editButton = new Button
        {
            Content = "Edit",
            Tag = action.Id,
            MinWidth = 56
        };
        editButton.Click += EditShortcutActionButton_Click;
        var deleteButton = new Button
        {
            Content = "Delete",
            Tag = action.Id,
            MinWidth = 64
        };
        deleteButton.Click += DeleteShortcutActionButton_Click;
        actionsPanel.Children.Add(editButton);
        actionsPanel.Children.Add(deleteButton);
        Grid.SetColumn(actionsPanel, 7);
        row.Children.Add(actionsPanel);

        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = row
        };
    }

    private static void AddActionTableCell(Grid row, string text, int column)
    {
        var cell = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(text) ? "-" : text,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static string DescribeActionIcon(string iconId)
    {
        var icon = ShortcutIconCatalog.All.FirstOrDefault(entry => string.Equals(entry.Id, iconId, StringComparison.Ordinal));
        return icon is null ? iconId : $"{icon.Label} ({icon.Id})";
    }

    private static string DescribeActionAvailability(ShortcutAction action)
    {
        var hasShortcut = action.GlobalShortcut is not null;
        if (action.ShowInCommandMenu && hasShortcut)
        {
            return "Shortcut + Command menu";
        }

        if (action.ShowInCommandMenu)
        {
            return "Command menu only";
        }

        return hasShortcut ? "Shortcut only" : "Unavailable";
    }

    private ShortcutAction? ResolveShortcutActionDraft(IEnumerable<ShortcutAction> actions)
    {
        if (creatingShortcutAction)
        {
            return new ShortcutAction(
                Guid.NewGuid().ToString("N"),
                string.Empty,
                "custom",
                ShowInCommandMenu: true,
                GlobalShortcut: null,
                TargetItem: string.Empty,
                CommandType: ShortcutCommandType.Toggle,
                CommandValue: null);
        }

        if (string.IsNullOrWhiteSpace(editingShortcutActionId))
        {
            return null;
        }

        return actions.FirstOrDefault(action => string.Equals(action.Id, editingShortcutActionId, StringComparison.Ordinal));
    }

    private async void AddShortcutActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardShortcutActionChangesIfNeededAsync())
        {
            return;
        }

        creatingShortcutAction = true;
        editingShortcutActionId = null;
        NavigateToSettingsPage(SettingsPage.Shortcuts);
    }

    private async void EditShortcutActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId } || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (!await ConfirmDiscardShortcutActionChangesIfNeededAsync())
        {
            return;
        }

        creatingShortcutAction = false;
        editingShortcutActionId = actionId;
        NavigateToSettingsPage(SettingsPage.Shortcuts);
    }

    private async void DeleteShortcutActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string actionId } || string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (!await ConfirmDiscardShortcutActionChangesIfNeededAsync())
        {
            return;
        }

        var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        var action = shortcuts.Actions.FirstOrDefault(candidate => string.Equals(candidate.Id, actionId, StringComparison.Ordinal));
        if (action is null)
        {
            return;
        }

        var actionName = string.IsNullOrWhiteSpace(action.Name) ? "Unnamed action" : action.Name.Trim();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete action",
            Content = $"Delete action '{actionName}'?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        settingsController.SetShortcutSettings(shortcuts with
        {
            Actions = shortcuts.Actions.Where(candidate => !string.Equals(candidate.Id, actionId, StringComparison.Ordinal)).ToImmutableArray()
        });
        if (string.Equals(editingShortcutActionId, actionId, StringComparison.Ordinal))
        {
            editingShortcutActionId = null;
            creatingShortcutAction = false;
        }

        NavigateToSettingsPage(SettingsPage.Shortcuts);
    }

    private void CancelShortcutActionButton_Click(object sender, RoutedEventArgs e)
    {
        ShortcutActionEditorErrorText?.SetValue(TextBlock.TextProperty, string.Empty);
        if (ShortcutActionEditorErrorText is not null)
        {
            ShortcutActionEditorErrorText.Visibility = Visibility.Collapsed;
        }

        creatingShortcutAction = false;
        editingShortcutActionId = null;
        NavigateToSettingsPage(SettingsPage.Shortcuts);
    }

    private void SaveShortcutActionButton_Click(object sender, RoutedEventArgs e)
    {
        var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        var selectedType = ShortcutActionTypeCombo?.SelectedItem is ShortcutCommandType commandType
            ? commandType
            : ShortcutCommandType.Toggle;
        var selectedIcon = ShortcutActionIconCombo?.SelectedItem as ShortcutIconDefinition;

        var actionId = creatingShortcutAction
            ? Guid.NewGuid().ToString("N")
            : editingShortcutActionId ?? Guid.NewGuid().ToString("N");
        var updated = new ShortcutAction(
            actionId,
            ShortcutActionNameText?.Text?.Trim() ?? string.Empty,
            selectedIcon?.Id ?? "custom",
            ShortcutActionShowInCommandMenuToggle?.IsOn ?? false,
            ShortcutActionGlobalShortcutRecorder?.Binding,
            ShortcutActionTargetItemText?.Text?.Trim() ?? string.Empty,
            selectedType,
            string.IsNullOrWhiteSpace(ShortcutActionValueText?.Text) ? null : ShortcutActionValueText!.Text.Trim());

        var errors = new List<string>();
        var actionValidation = ShortcutValidation.ValidateAction(updated);
        if (!actionValidation.IsValid)
        {
            errors.AddRange(actionValidation.Errors);
        }

        var existingBindings = new List<ShortcutBindingOwner>();
        if (shortcuts.CommandMenu.Binding is not null)
        {
            existingBindings.Add(new ShortcutBindingOwner("openHAB Command Menu", shortcuts.CommandMenu.Binding));
        }

        foreach (var action in shortcuts.Actions.Where(action => action.GlobalShortcut is not null && !string.Equals(action.Id, updated.Id, StringComparison.Ordinal)))
        {
            existingBindings.Add(new ShortcutBindingOwner($"Action: {action.Name}", action.GlobalShortcut!));
        }

        var bindingValidation = ShortcutValidation.ValidateBinding(
            updated.GlobalShortcut,
            $"Current action:{updated.Id}",
            existingBindings,
            allowUnassigned: true);
        if (!bindingValidation.IsValid)
        {
            errors.AddRange(bindingValidation.Errors);
        }

        if (errors.Count > 0)
        {
            if (ShortcutActionEditorErrorText is not null)
            {
                ShortcutActionEditorErrorText.Text = string.Join(Environment.NewLine, errors.Distinct(StringComparer.Ordinal));
                ShortcutActionEditorErrorText.Visibility = Visibility.Visible;
            }
            if (ShortcutActionGlobalShortcutRecorder is not null)
            {
                ShortcutActionGlobalShortcutRecorder.Error = bindingValidation.IsValid
                    ? null
                    : string.Join(Environment.NewLine, bindingValidation.Errors);
            }

            return;
        }

        if (ShortcutActionEditorErrorText is not null)
        {
            ShortcutActionEditorErrorText.Text = string.Empty;
            ShortcutActionEditorErrorText.Visibility = Visibility.Collapsed;
        }
        if (ShortcutActionGlobalShortcutRecorder is not null)
        {
            ShortcutActionGlobalShortcutRecorder.Error = null;
        }

        var hasExisting = shortcuts.Actions.Any(action => string.Equals(action.Id, updated.Id, StringComparison.Ordinal));
        var updatedActions = hasExisting
            ? shortcuts.Actions.Select(action => string.Equals(action.Id, updated.Id, StringComparison.Ordinal) ? updated : action).ToImmutableArray()
            : shortcuts.Actions.Add(updated);
        settingsController.SetShortcutSettings(shortcuts with { Actions = updatedActions });

        creatingShortcutAction = false;
        editingShortcutActionId = null;
        NavigateToSettingsPage(SettingsPage.Shortcuts);
    }

    private async Task<bool> ConfirmDiscardShortcutActionChangesIfNeededAsync()
    {
        var currentDraft = GetCurrentShortcutActionEditorDraft();
        if (currentDraft is null)
        {
            return true;
        }

        var savedDraft = ResolveShortcutActionDraft((settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized().Actions);
        if (savedDraft is not null && ShortcutActionDraftEquals(currentDraft, savedDraft))
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Discard unsaved changes?",
            Content = "You have unsaved changes in the action editor.",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private ShortcutAction? GetCurrentShortcutActionEditorDraft()
    {
        if (ShortcutActionNameText is null
            || ShortcutActionIconCombo is null
            || ShortcutActionShowInCommandMenuToggle is null
            || ShortcutActionTargetItemText is null
            || ShortcutActionTypeCombo is null
            || ShortcutActionValueText is null)
        {
            return null;
        }

        var actionId = creatingShortcutAction
            ? string.Empty
            : editingShortcutActionId ?? string.Empty;
        var selectedType = ShortcutActionTypeCombo.SelectedItem is ShortcutCommandType commandType
            ? commandType
            : ShortcutCommandType.Toggle;
        var selectedIcon = ShortcutActionIconCombo.SelectedItem as ShortcutIconDefinition;

        return new ShortcutAction(
            actionId,
            ShortcutActionNameText.Text.Trim(),
            selectedIcon?.Id ?? "custom",
            ShortcutActionShowInCommandMenuToggle.IsOn,
            ShortcutActionGlobalShortcutRecorder?.Binding,
            ShortcutActionTargetItemText.Text.Trim(),
            selectedType,
            string.IsNullOrWhiteSpace(ShortcutActionValueText.Text) ? null : ShortcutActionValueText.Text.Trim());
    }

    private static bool ShortcutActionDraftEquals(ShortcutAction current, ShortcutAction saved)
    {
        return string.Equals(current.Name, saved.Name, StringComparison.Ordinal)
            && string.Equals(current.IconId, saved.IconId, StringComparison.Ordinal)
            && current.ShowInCommandMenu == saved.ShowInCommandMenu
            && ShortcutBindingFormatter.Format(current.GlobalShortcut).Equals(ShortcutBindingFormatter.Format(saved.GlobalShortcut), StringComparison.Ordinal)
            && string.Equals(current.TargetItem, saved.TargetItem, StringComparison.Ordinal)
            && current.CommandType == saved.CommandType
            && string.Equals(current.CommandValue ?? string.Empty, saved.CommandValue ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task NavigateToSettingsPageWithDiscardConfirmationAsync(SettingsPage destination)
    {
        if (!await ConfirmDiscardShortcutActionChangesIfNeededAsync())
        {
            return;
        }

        NavigateToSettingsPage(destination);
    }

    private void DeviceInfoSyncField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings)
        {
            return;
        }

        SaveDeviceInfoSyncSettings(enabledOverride: null);

        if (ReferenceEquals(sender, DeviceInfoSyncIdentifierText))
        {
            NavigateToSettingsPage(SettingsPage.DeviceInfoSync);
        }
    }

    private void DeviceInfoSyncIntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (isRefreshingSettingsBindings || double.IsNaN(args.NewValue))
        {
            return;
        }

        SaveDeviceInfoSyncSettings(enabledOverride: null);
    }

    private void SaveDeviceInfoSyncSettings(bool? enabledOverride)
    {
        var current = settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default;
        var enabled = enabledOverride ?? DeviceInfoSyncEnabledToggle?.IsOn ?? current.IsEnabled;
        var deviceIdentifier = DeviceInfoSyncIdentifierText?.Text ?? current.DeviceIdentifier;
        var interval = DeviceInfoSyncIntervalBox is null || double.IsNaN(DeviceInfoSyncIntervalBox.Value)
            ? current.SyncIntervalMinutes
            : (int)Math.Round(DeviceInfoSyncIntervalBox.Value);

        var updated = current with
        {
            IsEnabled = enabled,
            DeviceIdentifier = deviceIdentifier,
            SyncIntervalMinutes = interval,
            BatteryLevelItem = GetDeviceInfoSyncMappingText("BatteryLevelItem", current.BatteryLevelItem),
            ChargingStateItem = GetDeviceInfoSyncMappingText("ChargingStateItem", current.ChargingStateItem),
            LockedStateItem = GetDeviceInfoSyncMappingText("LockedStateItem", current.LockedStateItem),
            SessionStateItem = GetDeviceInfoSyncMappingText("SessionStateItem", current.SessionStateItem),
            WifiConnectedItem = GetDeviceInfoSyncMappingText("WifiConnectedItem", current.WifiConnectedItem),
            WifiNameItem = GetDeviceInfoSyncMappingText("WifiNameItem", current.WifiNameItem),
            OpenHabConnectionItem = GetDeviceInfoSyncMappingText("OpenHabConnectionItem", current.OpenHabConnectionItem),
            FocusStateItem = GetDeviceInfoSyncMappingText("FocusStateItem", current.FocusStateItem)
        };

        try
        {
            settingsController.SetDeviceInfoSyncSettings(updated);
        }
        catch (ArgumentOutOfRangeException)
        {
            RefreshSettingsBindings();
        }
    }

    private void SetDeviceInfoSyncMappingText(string key, string? value)
    {
        if (deviceInfoSyncItemMappingTexts.TryGetValue(key, out var textBox))
        {
            textBox.Text = ToDeviceInfoSyncItemSuffix(value, GetDeviceInfoSyncIdentifier());
        }
    }

    private string? GetDeviceInfoSyncMappingText(string key, string? fallback)
    {
        if (!deviceInfoSyncItemMappingTexts.TryGetValue(key, out var textBox))
        {
            return fallback;
        }

        var suffix = textBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return null;
        }

        var identifier = GetDeviceInfoSyncIdentifier();
        return suffix.StartsWith(identifier, StringComparison.Ordinal) ? suffix : identifier + suffix;
    }

    private string GetDeviceInfoSyncIdentifier()
    {
        var current = settingsController.Current.DeviceInfoSync ?? DeviceInfoSyncSettings.Default;
        var textValue = DeviceInfoSyncIdentifierText?.Text;
        var value = string.IsNullOrWhiteSpace(textValue) ? current.DeviceIdentifier : textValue;
        return DeviceInfoSyncSettings.SanitizeDeviceIdentifier(value);
    }

    private static string ToDeviceInfoSyncItemSuffix(string? itemName, string identifier)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return string.Empty;
        }

        var trimmed = itemName.Trim();
        return trimmed.StartsWith(identifier, StringComparison.Ordinal) ? trimmed[identifier.Length..] : trimmed;
    }

    private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = DiagnosticLogger.LogPath;
            if (File.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            setStatusText($"Could not open logs: {ex.Message}");
        }
    }
}
