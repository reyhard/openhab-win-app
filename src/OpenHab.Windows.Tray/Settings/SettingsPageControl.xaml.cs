using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
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
        button.Click += (_, _) => NavigateToSettingsPage(destination);
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

        var commandMenuEnabledToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            IsOn = settings.CommandMenu.Enabled
        };
        var commandMenuTitleRow = CreateSettingsControlRow(
            "\uE8FD",
            "openHAB Command Menu",
            "Built-in global shortcut for opening the command menu",
            new TextBlock
            {
                Text = "Built-in",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            });

        var commandMenuEnabledRow = CreateSettingsToggleRow("\uE8FD", "Enabled", "Turn command menu keyboard handling on or off", commandMenuEnabledToggle);

        var globalShortcutRow = CreateSettingsControlRow(
            "\uE765",
            "Global shortcut",
            "Keyboard shortcut for opening command menu from anywhere",
            ShortcutSettingsControls.CreateShortcutChips(settings.CommandMenu.Binding));

        var activationModeCombo = new ComboBox
        {
            Width = 220,
            ItemsSource = Enum.GetValues<RadialActivationMode>(),
            SelectedItem = settings.CommandMenu.RadialActivationMode
        };
        var activationModeRow = CreateSettingsControlRow(
            "\uE7C1",
            "Activation mode",
            "Choose whether the command menu toggles or stays open while held",
            activationModeCombo);

        SettingsContent.Children.Add(ShortcutSettingsControls.CreateSettingsCard(
            commandMenuTitleRow,
            commandMenuEnabledRow,
            globalShortcutRow,
            activationModeRow));

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

        SettingsContent.Children.Add(new TextBlock
        {
            Text = "Voice Mode",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 0)
        });
        var voiceModeCard = ShortcutSettingsControls.CreateSettingsCard(voiceModeStateRow, voiceModeShortcutRow);
        voiceModeCard.IsHitTestVisible = false;
        voiceModeCard.Opacity = 0.72;
        SettingsContent.Children.Add(voiceModeCard);

        AddSettingsSectionTitle("Actions and shortcuts");
        SettingsContent.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 14, 16, 14),
            Child = new TextBlock
            {
                Text = "No custom actions yet. Shortcut action editor is coming in a later task.",
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private Expander CreateSettingsExpander(string title, string subtitle, UIElement content, UIElement? action = null)
    {
        if (content is FrameworkElement contentElement)
        {
            contentElement.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        var contentHost = new Grid
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        contentHost.Children.Add(content);

        var expander = new Expander
        {
            Header = CreateSettingsHeader(title, subtitle, action),
            Content = contentHost,
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        expander.SizeChanged += (_, _) =>
        {
            contentHost.Width = Math.Max(0, expander.ActualWidth - 2);
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
            if (DeviceInfoSyncEnabledToggle is not null)
            {
                DeviceInfoSyncEnabledToggle.IsOn = deviceInfoSync.IsEnabled;
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

    private void SettingsBreadcrumbRootButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSettingsPage(SettingsPage.Root);
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
