using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenHab.App.Localization;
using OpenHab.App.Settings;
using OpenHab.App.Shortcuts;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Windows.Tray.Localization;
using OpenHab.Windows.Tray.Rendering;
using OpenHab.Windows.Tray.Shortcuts;
using OpenHab.Windows.Tray.Startup;

namespace OpenHab.Windows.Tray.Settings;

public sealed partial class SettingsPageControl : UserControl
{
    private const string CustomShortcutIconId = "custom";
    private const string CardStrokeBrushResourceKey = "CardStrokeColorDefaultBrush";
    private const string LocalTransportTag = "Local";

    private sealed record AppColorThemeOption(string Label, AppColorTheme Theme)
    {
        public override string ToString() => Label;
    }

    private sealed record SitemapSkinOption(string Label, SitemapSkinKind Skin)
    {
        public override string ToString() => Label;
    }

    private sealed record EndpointModeOption(string Label, EndpointMode Mode)
    {
        public override string ToString() => Label;
    }

    private sealed record RadialActivationModeOption(string Label, RadialActivationMode Mode)
    {
        public override string ToString() => Label;
    }

    private sealed record ShortcutCommandTypeOption(string Label, ShortcutCommandType CommandType)
    {
        public override string ToString() => Label;
    }

    private sealed record AppLanguageOption(string Label, AppLanguage Language)
    {
        public override string ToString() => Label;
    }

    private readonly AppSettingsController settingsController;
    private readonly Func<Task> refreshRuntimeAsync;
    private readonly Action<string> setStatusText;
    private readonly ITextLocalizer text;
    private readonly AppLanguage appliedAppLanguage;
    private SettingsPageKind currentSettingsPage = SettingsPageKind.Root;
    private bool isSettingsPageTransitionRunning;
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
    private ComboBox? AppLanguageCombo;
    private InfoBar? AppLanguageRestartInfoBar;
    private ToggleSwitch? UseWin11IconsToggle;
    private ToggleSwitch? LaunchAtStartupToggle;
    private NumberBox? FlyoutWidthBox;
    private NumberBox? NotificationPollBox;
    private TextBox? ImportantNotificationTagsText;
    private ToggleSwitch? DeviceInfoSyncEnabledToggle;
    private ToggleSwitch? CommandMenuEnabledToggle;
    private ShortcutRecorderControl? CommandMenuShortcutRecorder;
    private ComboBox? CommandMenuActivationModeCombo;
    private ContentControl? CommandMenuPreviewContent;
    private int shortcutActionsSectionStartIndex = -1;
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
    private Button? ViewSettingsFolderButton;
    private ToggleSwitch? VerboseDiagnosticsToggle;
    private TextBlock? VersionText;

    public SettingsPageControl(
        AppSettingsController settingsController,
        Func<Task> refreshRuntimeAsync,
        Action<string> setStatusText,
        AppLanguage appliedAppLanguage = AppLanguage.System,
        ITextLocalizer? text = null)
    {
        this.settingsController = settingsController;
        this.refreshRuntimeAsync = refreshRuntimeAsync;
        this.setStatusText = setStatusText;
        this.appliedAppLanguage = appliedAppLanguage;
        this.text = text ?? DefaultEnglishTextLocalizer.Instance;
        InitializeComponent();
        InitializeSettingsControls();
        RefreshSettingsBindings();
    }

    public void ShowRoot()
    {
        NavigateToSettingsPage(SettingsPageKind.Root);
    }

    public bool CanGoBack => currentSettingsPage != SettingsPageKind.Root;

    public async Task<bool> TryNavigateBackAsync()
    {
        if (!CanGoBack || isSettingsPageTransitionRunning)
        {
            return false;
        }

        var previousPage = currentSettingsPage;
        await NavigateToSettingsPageWithDiscardConfirmationAsync(SettingsPageKind.Root);
        return currentSettingsPage != previousPage;
    }

    private void InitializeSettingsControls()
    {
        NavigateToSettingsPage(SettingsPageKind.Root);
    }

    private void NavigateToSettingsPage(SettingsPageKind page)
    {
        currentSettingsPage = page;
        ResetSettingsControlReferences();
        SettingsContent.Children.Clear();

        switch (page)
        {
            case SettingsPageKind.Root:
                UpdateSettingsBreadcrumb(null);
                SettingsSubtitleText.Text = text.Get("Settings.Root.Subtitle");
                SettingsContent.Children.Add(CreateCategoryRow("\uE713", text.Get("Settings.Connection.Title"), text.Get("Settings.Connection.Subtitle"), SettingsPageKind.Connection));
                SettingsContent.Children.Add(CreateCategoryRow("\uE770", text.Get("Settings.General.Title"), text.Get("Settings.General.Subtitle"), SettingsPageKind.General));
                SettingsContent.Children.Add(CreateCategoryRow("\uE790", text.Get("Settings.Appearance.Title"), text.Get("Settings.Appearance.Subtitle"), SettingsPageKind.Appearance));
                SettingsContent.Children.Add(CreateCategoryRow("\uE7F4", text.Get("Settings.DeviceInfoSync.Title"), text.Get("Settings.DeviceInfoSync.Subtitle"), SettingsPageKind.DeviceInfoSync));
                SettingsContent.Children.Add(CreateCategoryRow("\uE765", text.Get("Settings.Shortcuts.Title"), text.Get("Settings.Shortcuts.Subtitle"), SettingsPageKind.Shortcuts));
                SettingsContent.Children.Add(CreateCategoryRow("\uE946", text.Get("Settings.About.Title"), text.Get("Settings.About.Subtitle"), SettingsPageKind.About));
                break;
            case SettingsPageKind.Connection:
                UpdateSettingsBreadcrumb(text.Get("Settings.Connection.Title"));
                SettingsSubtitleText.Text = text.Get("Settings.Connection.Subtitle");
                BuildConnectionSettingsPage();
                break;
            case SettingsPageKind.General:
                UpdateSettingsBreadcrumb(text.Get("Settings.General.Title"));
                SettingsSubtitleText.Text = text.Get("Settings.General.PageSubtitle");
                BuildGeneralSettingsPage();
                break;
            case SettingsPageKind.Appearance:
                UpdateSettingsBreadcrumb(text.Get("Settings.Appearance.Title"));
                SettingsSubtitleText.Text = text.Get("Settings.Appearance.PageSubtitle");
                BuildAppearanceSettingsPage();
                break;
            case SettingsPageKind.DeviceInfoSync:
                UpdateSettingsBreadcrumb(text.Get("Settings.DeviceInfoSync.Title"));
                SettingsSubtitleText.Text = text.Get("Settings.DeviceInfoSync.Subtitle");
                BuildDeviceInfoSyncSettingsPage();
                break;
            case SettingsPageKind.Shortcuts:
                UpdateSettingsBreadcrumb(text.Get("Settings.Shortcuts.Title"));
                SettingsSubtitleText.Text = text.Get("Settings.Shortcuts.PageSubtitle");
                BuildShortcutsSettingsPage();
                break;
            case SettingsPageKind.About:
                UpdateSettingsBreadcrumb(text.Get("Settings.About.Title"));
                SettingsSubtitleText.Text = text.Get("Settings.About.Subtitle");
                BuildAboutSettingsPage();
                break;
        }

        RefreshSettingsBindings();
    }

    private async Task NavigateToSettingsPageAsync(SettingsPageKind destination, bool animate)
    {
        if (isSettingsPageTransitionRunning)
        {
            return;
        }

        if (!animate
            || !SettingsPageTransitionPlanner.TryResolveDirection(currentSettingsPage, destination, out var direction))
        {
            NavigateToSettingsPage(destination);
            return;
        }

        var durationMs = SitemapPageTransitionAnimator.ResolveDurationMs(settingsController.GetFlyoutAnimationDurationMs());
        if (durationMs <= 0)
        {
            NavigateToSettingsPage(destination);
            return;
        }

        isSettingsPageTransitionRunning = true;
        MoveActiveSettingsContentToTransitionSlot();
        SettingsTransitionSlot.Visibility = Visibility.Visible;

        try
        {
            NavigateToSettingsPage(destination);
            SettingsScrollViewer.ChangeView(null, 0d, null, true);
            await SitemapPageTransitionAnimator.AnimateOverlapAsync(
                SettingsContentRoot,
                SettingsTransitionSlot,
                SettingsActiveSlot,
                ToNavigationDirection(direction),
                durationMs);
        }
        finally
        {
            SettingsTransitionContent.Children.Clear();
            SettingsTransitionSlot.Visibility = Visibility.Collapsed;
            isSettingsPageTransitionRunning = false;
        }
    }

    private static NavigationDirection ToNavigationDirection(SettingsPageTransitionDirection direction) =>
        direction == SettingsPageTransitionDirection.Back
            ? NavigationDirection.Back
            : NavigationDirection.Forward;

    private void MoveActiveSettingsContentToTransitionSlot()
    {
        SettingsTransitionContent.Width = SettingsContent.Width;
        SettingsTransitionContent.Children.Clear();
        var oldChildren = SettingsContent.Children.Cast<UIElement>().ToArray();
        SettingsContent.Children.Clear();
        foreach (var child in oldChildren)
        {
            SettingsTransitionContent.Children.Add(child);
        }
    }

    private void UpdateSettingsBreadcrumb(string? pageTitle)
    {
        var isRoot = string.IsNullOrWhiteSpace(pageTitle);
        SettingsBreadcrumbRootButton.Visibility = isRoot ? Visibility.Collapsed : Visibility.Visible;
        SettingsBreadcrumbChevron.Visibility = isRoot ? Visibility.Collapsed : Visibility.Visible;
        SettingsTitleText.Text = isRoot ? text.Get("Settings.Title") : pageTitle;
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

    private Button CreateCategoryRow(string glyph, string title, string subtitle, SettingsPageKind destination)
    {
        var button = new Button
        {
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources[CardStrokeBrushResourceKey],
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
        EndpointModeCombo.ItemsSource = CreateEndpointModeOptions();
        EndpointModeCombo.SelectionChanged += EndpointModeCombo_SelectionChanged;
        var endpointModeRow = CreateSettingsControlRow(
            "\uE713",
            text.Get("Settings.Connection.EndpointMode.Title"),
            text.Get("Settings.Connection.EndpointMode.Subtitle"),
            EndpointModeCombo);

        LocalEndpointText = new TextBox
        {
            Width = 520
        };
        LocalEndpointText.LostFocus += EndpointText_LostFocus;
        var localEndpointRow = CreateSettingsControlRow(
            "\uE839",
            text.Get("Settings.Connection.LocalEndpoint.Title"),
            text.Get("Settings.Connection.LocalEndpoint.Subtitle"),
            LocalEndpointText);

        CloudEndpointText = new TextBox
        {
            Width = 520
        };
        CloudEndpointText.LostFocus += EndpointText_LostFocus;
        var cloudEndpointRow = CreateSettingsControlRow(
            "\uE753",
            text.Get("Settings.Connection.CloudEndpoint.Title"),
            text.Get("Settings.Connection.CloudEndpoint.Subtitle"),
            CloudEndpointText);

        LocalTokenBox = new PasswordBox
        {
            PlaceholderText = text.Get("Settings.Connection.LocalToken.Placeholder"),
            Tag = LocalTransportTag,
            Width = 520
        };
        LocalTokenBox.GotFocus += TokenBox_GotFocus;
        LocalTokenBox.PasswordChanged += TokenBox_PasswordChanged;
        LocalTokenBox.LostFocus += TokenBox_LostFocus;
        var localTokenRow = CreateSettingsControlRow(
            "\uE72E",
            text.Get("Settings.Connection.LocalToken.Title"),
            text.Get("Settings.Connection.LocalToken.Subtitle"),
            LocalTokenBox);

        CloudUserNameText = new TextBox
        {
            PlaceholderText = text.Get("Settings.Connection.CloudUserName.Placeholder"),
            Width = 520
        };
        CloudUserNameText.TextChanged += CloudUserNameText_TextChanged;
        CloudUserNameText.LostFocus += CloudCredentials_LostFocus;
        var cloudUserNameRow = CreateSettingsControlRow(
            "\uE77B",
            text.Get("Settings.Connection.CloudUserName.Title"),
            text.Get("Settings.Connection.CloudUserName.Subtitle"),
            CloudUserNameText);

        CloudPasswordBox = new PasswordBox
        {
            PlaceholderText = text.Get("Settings.Connection.CloudPassword.Placeholder"),
            Width = 520
        };
        CloudPasswordBox.PasswordChanged += CloudPasswordBox_PasswordChanged;
        CloudPasswordBox.LostFocus += CloudCredentials_LostFocus;
        var cloudPasswordRow = CreateSettingsControlRow(
            "\uE72E",
            text.Get("Settings.Connection.CloudPassword.Title"),
            text.Get("Settings.Connection.CloudPassword.Subtitle"),
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
            text.Get("Settings.General.LaunchAtStartup.Title"),
            text.Get("Settings.General.LaunchAtStartup.Subtitle"),
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
            text.Get("Settings.General.FlyoutWidth.Title"),
            text.Get("Settings.General.FlyoutWidth.Subtitle"),
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
            text.Get("Settings.General.NotificationPoll.Title"),
            text.Get("Settings.General.NotificationPoll.Subtitle"),
            NotificationPollBox);

        ImportantNotificationTagsText = new TextBox
        {
            PlaceholderText = text.Get("Settings.General.ImportantTags.Placeholder"),
            Width = 320
        };
        ImportantNotificationTagsText.LostFocus += ImportantNotificationTagsText_LostFocus;
        var importantTagsRow = CreateSettingsControlRow(
            "\uE7BA",
            text.Get("Settings.General.ImportantTags.Title"),
            text.Get("Settings.General.ImportantTags.Subtitle"),
            ImportantNotificationTagsText);

        SettingsContent.Children.Add(CreateSettingsGroup(launchRow, flyoutWidthRow, notificationPollRow, importantTagsRow));
    }

    private void BuildAppearanceSettingsPage()
    {
        SkinCombo = new ComboBox
        {
            Width = 220
        };
        SkinCombo.ItemsSource = CreateSitemapSkinOptions();
        SkinCombo.SelectionChanged += SkinCombo_SelectionChanged;
        var skinRow = CreateSettingsControlRow(
            "\uE790",
            text.Get("Settings.Appearance.Skin.Title"),
            text.Get("Settings.Appearance.Skin.Subtitle"),
            SkinCombo);

        AppColorThemeCombo = new ComboBox
        {
            Width = 220,
            ItemsSource = CreateAppColorThemeOptions()
        };
        AppColorThemeCombo.SelectionChanged += AppColorThemeCombo_SelectionChanged;
        var themeRow = CreateSettingsControlRow(
            "\uE771",
            text.Get("Settings.Appearance.Theme.Title"),
            text.Get("Settings.Appearance.Theme.Subtitle"),
            AppColorThemeCombo);

        AppLanguageCombo = new ComboBox
        {
            Width = 220,
            ItemsSource = CreateAppLanguageOptions()
        };
        AppLanguageCombo.SelectionChanged += AppLanguageCombo_SelectionChanged;
        var languageRow = CreateSettingsControlRow(
            "\uE774",
            text.Get("Settings.Appearance.Language.Title"),
            text.Get("Settings.Appearance.Language.Subtitle"),
            AppLanguageCombo);

        AppLanguageRestartInfoBar = new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            IsClosable = false,
            Title = text.Get("Settings.Appearance.Language.RestartTitle"),
            Message = text.Get("Settings.Appearance.Language.RestartMessage"),
            IsOpen = false,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 4)
        };

        UseWin11IconsToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty
        };
        UseWin11IconsToggle.Toggled += UseWin11IconsToggle_Toggled;
        var iconStyleRow = CreateSettingsToggleRow(
            "\uE8A5",
            text.Get("Settings.Appearance.IconStyle.Title"),
            text.Get("Settings.Appearance.IconStyle.Subtitle"),
            UseWin11IconsToggle);

        SettingsContent.Children.Add(CreateSettingsGroup(skinRow, themeRow, languageRow));
        SettingsContent.Children.Add(AppLanguageRestartInfoBar);
        SettingsContent.Children.Add(CreateSettingsGroup(iconStyleRow));
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
                Text = text.Get("Settings.DeviceInfoSync.DisabledMessage"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            };
            syncContent.Children.Add(DeviceInfoSyncDisabledText);
            SettingsContent.Children.Add(CreateSettingsExpander(
                text.Get("Settings.DeviceInfoSync.Title"),
                text.Get("Settings.DeviceInfoSync.Description"),
                CreateExpanderRows(syncContent),
                enabledAction));
            return;
        }

        DeviceInfoSyncIdentifierText = new TextBox
        {
            Header = text.Get("Settings.DeviceInfoSync.Identifier.Header")
        };
        DeviceInfoSyncIdentifierText.LostFocus += DeviceInfoSyncField_LostFocus;
        syncContent.Children.Add(DeviceInfoSyncIdentifierText);

        DeviceInfoSyncIntervalBox = new NumberBox
        {
            Header = text.Get("Settings.DeviceInfoSync.Interval.Header"),
            Minimum = DeviceInfoSyncSettings.MinSyncIntervalMinutes,
            Maximum = DeviceInfoSyncSettings.MaxSyncIntervalMinutes,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        DeviceInfoSyncIntervalBox.ValueChanged += DeviceInfoSyncIntervalBox_ValueChanged;
        syncContent.Children.Add(DeviceInfoSyncIntervalBox);

        SettingsContent.Children.Add(CreateSettingsExpander(
            text.Get("Settings.DeviceInfoSync.Title"),
            text.Get("Settings.DeviceInfoSync.Description"),
            CreateExpanderRows(syncContent),
            enabledAction));

        AddSettingsSectionTitle(text.Get("Settings.DeviceInfoSync.Mappings.Title"));
        var mappingContent = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AddDeviceInfoSyncMappingTextBox(mappingContent, "BatteryLevelItem", text.Get("Settings.DeviceInfoSync.Mapping.BatteryLevel"), "BatteryLevel");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "ChargingStateItem", text.Get("Settings.DeviceInfoSync.Mapping.ChargingState"), "ChargingState");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "LockedStateItem", text.Get("Settings.DeviceInfoSync.Mapping.LockedState"), "LockedState");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "SessionStateItem", text.Get("Settings.DeviceInfoSync.Mapping.SessionState"), "SessionState");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "WifiConnectedItem", text.Get("Settings.DeviceInfoSync.Mapping.WifiConnected"), "WifiConnected");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "WifiNameItem", text.Get("Settings.DeviceInfoSync.Mapping.WifiName"), "WifiName");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "BluetoothConnectedItem", text.Get("Settings.DeviceInfoSync.Mapping.BluetoothConnected"), "BluetoothConnected");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "BluetoothDeviceNamesItem", text.Get("Settings.DeviceInfoSync.Mapping.BluetoothDeviceNames"), "BluetoothDeviceNames");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "OpenHabConnectionItem", text.Get("Settings.DeviceInfoSync.Mapping.OpenHabConnection"), "OpenHabConnection");
        AddDeviceInfoSyncMappingTextBox(mappingContent, "FocusStateItem", text.Get("Settings.DeviceInfoSync.Mapping.FocusState"), "FocusState");
        SettingsContent.Children.Add(CreateSettingsExpander(
            text.Get("Settings.DeviceInfoSync.ItemSuffixes.Title"),
            text.Get("Settings.DeviceInfoSync.ItemSuffixes.Subtitle"),
            CreateExpanderRows(mappingContent)));
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

    private static StackPanel CreateSettingsGroup(params FrameworkElement[] rows)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = 4
        };

        foreach (var row in rows)
        {
            stack.Children.Add(CreateSettingsCardForContent(row));
        }

        return stack;
    }

    private AppLanguageOption[] CreateAppLanguageOptions() =>
    [
        new(text.Get("Settings.Appearance.Language.System"), AppLanguage.System),
        new(text.Get("Settings.Appearance.Language.English"), AppLanguage.English),
        new(text.Get("Settings.Appearance.Language.Polish"), AppLanguage.Polish)
    ];

    private AppColorThemeOption[] CreateAppColorThemeOptions() =>
    [
        new(text.Get("Settings.Appearance.Theme.Dark"), AppColorTheme.Dark),
        new(text.Get("Settings.Appearance.Theme.Bright"), AppColorTheme.Bright),
        new(text.Get("Settings.Appearance.Theme.FollowSystem"), AppColorTheme.FollowSystemSettings)
    ];

    private SitemapSkinOption[] CreateSitemapSkinOptions() =>
    [
        new(text.Get("Settings.Appearance.Skin.Basic"), SitemapSkinKind.Basic),
        new(text.Get("Settings.Appearance.Skin.Windows11"), SitemapSkinKind.Windows11)
    ];

    private EndpointModeOption[] CreateEndpointModeOptions() =>
    [
        new(text.Get("Settings.Connection.EndpointMode.Automatic"), EndpointMode.Automatic),
        new(text.Get("Settings.Connection.EndpointMode.LocalOnly"), EndpointMode.LocalOnly),
        new(text.Get("Settings.Connection.EndpointMode.CloudOnly"), EndpointMode.CloudOnly)
    ];

    private RadialActivationModeOption[] CreateRadialActivationModeOptions() =>
    [
        new(text.Get("Settings.Shortcuts.ActivationMode.Toggle"), RadialActivationMode.Toggle),
        new(text.Get("Settings.Shortcuts.ActivationMode.Hold"), RadialActivationMode.Hold)
    ];

    private ShortcutCommandTypeOption[] CreateShortcutCommandTypeOptions() =>
    [
        new(text.Get("Settings.Shortcuts.CommandType.Toggle"), ShortcutCommandType.Toggle),
        new(text.Get("Settings.Shortcuts.CommandType.OnOff"), ShortcutCommandType.OnOff),
        new(text.Get("Settings.Shortcuts.CommandType.OpenClose"), ShortcutCommandType.OpenClose),
        new(text.Get("Settings.Shortcuts.CommandType.OpenSlider"), ShortcutCommandType.OpenSlider),
        new(text.Get("Settings.Shortcuts.CommandType.OpenColorPicker"), ShortcutCommandType.OpenColorPicker),
        new(text.Get("Settings.Shortcuts.CommandType.SendCommand"), ShortcutCommandType.SendCommand)
    ];

    private Grid CreateSettingsToggleRow(string glyph, string title, string subtitle, ToggleSwitch toggle)
    {
        return CreateSettingsControlRow(glyph, title, subtitle, CreateSettingsToggleAction(toggle));
    }

    private static List<SettingsCard> CreateExpanderRows(params FrameworkElement[] rows)
    {
        var cards = new List<SettingsCard>(rows.Length);
        foreach (var row in rows)
        {
            cards.Add(CreateSettingsCardForContent(row));
        }

        return cards;
    }

    private static SettingsCard CreateSettingsCardForContent(FrameworkElement content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new SettingsCard
        {
            Content = content,
            ContentAlignment = CommunityToolkit.WinUI.Controls.ContentAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsClickEnabled = false
        };
    }

    private static Grid CreateSettingsControlRow(
        string glyph,
        string title,
        string subtitle,
        FrameworkElement control,
        bool stretchControl = false)
    {
        var row = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            Opacity = 0.82,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var textPanel = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
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
        control.HorizontalAlignment = stretchControl ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        Grid.SetColumn(control, 2);
        row.Children.Add(control);

        return row;
    }

    private FrameworkElement CreateCommandMenuPreview(IEnumerable<ShortcutAction> actions)
    {
        var visibleActions = actions
            .Where(static action => action.ShowInCommandMenu && ShortcutValidation.ValidateAction(action).IsValid)
            .Take(6)
            .ToArray();

        if (visibleActions.Length == 0)
        {
            return new TextBlock
            {
                Text = text.Get("Settings.Shortcuts.Preview.NoActions"),
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
        var center = CreatePreviewNode("\uE711", text.Get("Common.Close"), 50, true);
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
            BorderBrush = (Brush)Application.Current.Resources[CardStrokeBrushResourceKey],
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

    private StackPanel CreateSettingsToggleAction(ToggleSwitch toggle)
    {
        var stateText = new TextBlock
        {
            Text = toggle.IsOn ? text.Get("Common.On") : text.Get("Common.Off"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 24
        };
        toggle.Toggled += (_, _) => stateText.Text = toggle.IsOn ? text.Get("Common.On") : text.Get("Common.Off");
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
            Content = text.Get("Settings.About.ViewLogs"),
            MinWidth = 120
        };
        ViewLogsButton.Click += ViewLogsButton_Click;
        ViewSettingsFolderButton = new Button
        {
            Content = text.Get("Settings.About.SettingsFolder"),
            MinWidth = 140
        };
        ViewSettingsFolderButton.Click += ViewSettingsFolderButton_Click;
        var diagnosticsActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        diagnosticsActions.Children.Add(ViewLogsButton);
        diagnosticsActions.Children.Add(ViewSettingsFolderButton);
        var logsRow = CreateSettingsControlRow(
            "\uE8A5",
            text.Get("Settings.About.DiagnosticLogs.Title"),
            text.Get("Settings.About.DiagnosticLogs.Subtitle"),
            diagnosticsActions);

        VerboseDiagnosticsToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty
        };
        VerboseDiagnosticsToggle.Toggled += VerboseDiagnosticsToggle_Toggled;
        var verboseDiagnosticsRow = CreateSettingsToggleRow(
            "\uE9D9",
            text.Get("Settings.About.VerboseDiagnostics.Title"),
            text.Get("Settings.About.VerboseDiagnostics.Subtitle"),
            VerboseDiagnosticsToggle);
        SettingsContent.Children.Add(CreateSettingsGroup(logsRow, verboseDiagnosticsRow));

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

        AddSettingsSectionTitle(text.Get("Settings.Shortcuts.BuiltIn.Title"));

        CommandMenuEnabledToggle = new ToggleSwitch
        {
            OnContent = string.Empty,
            OffContent = string.Empty,
            IsOn = settings.CommandMenu.Enabled
        };
        AutomationProperties.SetName(CommandMenuEnabledToggle, text.Get("Settings.Shortcuts.CommandMenu.EnableAutomationName"));
        CommandMenuEnabledToggle.Toggled += CommandMenuEnabledToggle_Toggled;
        var globalShortcutRow = CreateSettingsControlRow(
            "\uE765",
            text.Get("Settings.Shortcuts.GlobalShortcut.Title"),
            text.Get("Settings.Shortcuts.GlobalShortcut.Subtitle"),
            CreateCommandMenuShortcutRecorder(settings.CommandMenu.Binding));

        CommandMenuActivationModeCombo = new ComboBox
        {
            Width = 220,
            ItemsSource = CreateRadialActivationModeOptions()
        };
        CommandMenuActivationModeCombo.SelectedItem = CommandMenuActivationModeCombo.Items
            .OfType<RadialActivationModeOption>()
            .First(option => option.Mode == settings.CommandMenu.RadialActivationMode);
        AutomationProperties.SetName(CommandMenuActivationModeCombo, text.Get("Settings.Shortcuts.ActivationMode.AutomationName"));
        CommandMenuActivationModeCombo.SelectionChanged += CommandMenuActivationModeCombo_SelectionChanged;
        var activationModeRow = CreateSettingsControlRow(
            "\uE7C1",
            text.Get("Settings.Shortcuts.ActivationMode.Title"),
            text.Get("Settings.Shortcuts.ActivationMode.Subtitle"),
            CommandMenuActivationModeCombo);
        CommandMenuPreviewContent = new ContentControl
        {
            Content = CreateCommandMenuPreview(settings.Actions)
        };
        var previewRow = CreateSettingsControlRow(
            "\uE8FD",
            text.Get("Settings.Shortcuts.CommandMenuPreview.Title"),
            text.Get("Settings.Shortcuts.CommandMenuPreview.Subtitle"),
            CommandMenuPreviewContent);

        SettingsContent.Children.Add(CreateSettingsExpander(
            text.Get("Settings.Shortcuts.CommandMenu.Title"),
            text.Get("Settings.Shortcuts.CommandMenu.Subtitle"),
            CreateExpanderRows(globalShortcutRow, activationModeRow, previewRow),
            CreateSettingsToggleAction(CommandMenuEnabledToggle)));

        var voiceModeStateRow = CreateSettingsControlRow(
            "\uE720",
            text.Get("Settings.Shortcuts.VoiceMode.Title"),
            text.Get("Settings.Shortcuts.VoiceMode.Subtitle"),
            new TextBlock
            {
                Text = text.Get("Common.Disabled"),
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            });

        var voiceModeShortcutRow = CreateSettingsControlRow(
            "\uE765",
            text.Get("Settings.Shortcuts.Shortcut.Title"),
            text.Get("Settings.Shortcuts.Shortcut.Unassigned"),
            ShortcutSettingsControls.CreateShortcutChips(null));

        var voiceModeContent = CreateExpanderRows(voiceModeStateRow, voiceModeShortcutRow);
        foreach (var card in voiceModeContent)
        {
            card.Opacity = 0.72;
        }
        SettingsContent.Children.Add(CreateSettingsExpander(
            text.Get("Settings.Shortcuts.VoiceMode.ExpanderTitle"),
            text.Get("Settings.Shortcuts.VoiceMode.ExpanderSubtitle"),
            voiceModeContent,
            new TextBlock
            {
                Text = text.Get("Common.Disabled"),
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center
            },
            isExpanded: false));

        shortcutActionsSectionStartIndex = SettingsContent.Children.Count;
        BuildShortcutActionsSection(settings);
    }

    private void BuildShortcutActionsSection(ShortcutSettings settings)
    {
        var actionsHeader = new Grid
        {
            ColumnSpacing = 10
        };
        actionsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionsHeader.Children.Add(new TextBlock
        {
            Text = text.Get("Settings.Shortcuts.Actions.Title"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var addActionButton = new Button
        {
            Content = text.Get("Settings.Shortcuts.Actions.Add"),
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
        actionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.Icon"), 0);
        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.ActionName"), 1);
        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.Availability"), 2);
        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.Shortcut"), 3);
        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.TargetItem"), 4);
        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.ActionType"), 5);
        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.CommandValue"), 6);
        AddActionTableHeaderCell(actionHeader, text.Get("Settings.Shortcuts.Actions.Actions"), 7);
        actionsCardStack.Children.Add(actionHeader);

        if (settings.Actions.Length == 0)
        {
            actionsCardStack.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources[CardStrokeBrushResourceKey],
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 12, 12, 12),
                Child = new TextBlock
                {
                    Text = text.Get("Settings.Shortcuts.Actions.Empty"),
                    Opacity = 0.72,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }
        else
        {
            for (var i = 0; i < settings.Actions.Length; i++)
            {
                actionsCardStack.Children.Add(CreateShortcutActionRow(settings.Actions[i], i, settings.Actions.Length));
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
                BorderBrush = (Brush)Application.Current.Resources[CardStrokeBrushResourceKey],
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

        AddSettingsSectionTitle(creatingShortcutAction
            ? text.Get("Settings.Shortcuts.Actions.Add")
            : text.Get("Settings.Shortcuts.Actions.Edit"));
        ShortcutActionNameText = new TextBox
        {
            Text = draftAction.Name,
            MinWidth = 280
        };
        var nameRow = CreateSettingsControlRow(
            "\uE8D2",
            text.Get("Settings.Shortcuts.Editor.ActionName.Title"),
            text.Get("Settings.Shortcuts.Editor.ActionName.Subtitle"),
            ShortcutActionNameText,
            stretchControl: true);

        ShortcutActionIconCombo = new ComboBox
        {
            Width = 280
        };
        foreach (var icon in ShortcutIconCatalog.All)
        {
            ShortcutActionIconCombo.Items.Add(new ComboBoxItem
            {
                Content = CreateShortcutIconPresenter(icon, includeId: false),
                Tag = icon
            });
        }

        ShortcutActionIconCombo.SelectedItem = ShortcutActionIconCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is ShortcutIconDefinition icon && string.Equals(icon.Id, draftAction.IconId, StringComparison.Ordinal))
            ?? ShortcutActionIconCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is ShortcutIconDefinition icon && string.Equals(icon.Id, CustomShortcutIconId, StringComparison.Ordinal));
        var iconRow = CreateSettingsControlRow(
            "\uE8D4",
            text.Get("Settings.Shortcuts.Actions.Icon"),
            text.Get("Settings.Shortcuts.Editor.Icon.Subtitle"),
            ShortcutActionIconCombo);

        ShortcutActionShowInCommandMenuToggle = new ToggleSwitch
        {
            IsOn = draftAction.ShowInCommandMenu,
            OnContent = string.Empty,
            OffContent = string.Empty
        };
        var showInMenuRow = CreateSettingsControlRow(
            "\uE8FD",
            text.Get("Settings.Shortcuts.Editor.ShowInMenu.Title"),
            text.Get("Settings.Shortcuts.Editor.ShowInMenu.Subtitle"),
            CreateSettingsToggleAction(ShortcutActionShowInCommandMenuToggle));

        ShortcutActionGlobalShortcutRecorder = new ShortcutRecorderControl
        {
            Binding = draftAction.GlobalShortcut,
            AllowClear = true,
            Error = null
        };
        var globalShortcutEditorRow = CreateSettingsControlRow(
            "\uE765",
            text.Get("Settings.Shortcuts.GlobalShortcut.Title"),
            text.Get("Settings.Shortcuts.Editor.GlobalShortcut.Subtitle"),
            ShortcutActionGlobalShortcutRecorder);

        ShortcutActionTargetItemText = new TextBox
        {
            Text = draftAction.TargetItem,
            MinWidth = 280
        };
        var targetItemRow = CreateSettingsControlRow(
            "\uE7F4",
            text.Get("Settings.Shortcuts.Actions.TargetItem"),
            text.Get("Settings.Shortcuts.Editor.TargetItem.Subtitle"),
            ShortcutActionTargetItemText,
            stretchControl: true);

        ShortcutActionTypeCombo = new ComboBox
        {
            Width = 280,
            ItemsSource = CreateShortcutCommandTypeOptions()
        };
        ShortcutActionTypeCombo.SelectedItem = ShortcutActionTypeCombo.Items
            .OfType<ShortcutCommandTypeOption>()
            .First(option => option.CommandType == draftAction.CommandType);
        var typeRow = CreateSettingsControlRow(
            "\uE8EF",
            text.Get("Settings.Shortcuts.Actions.ActionType"),
            text.Get("Settings.Shortcuts.Editor.ActionType.Subtitle"),
            ShortcutActionTypeCombo);

        ShortcutActionValueText = new TextBox
        {
            Text = draftAction.CommandValue ?? string.Empty,
            MinWidth = 280
        };
        var commandValueRow = CreateSettingsControlRow(
            "\uE756",
            text.Get("Settings.Shortcuts.Actions.CommandValue"),
            text.Get("Settings.Shortcuts.Editor.CommandValue.Subtitle"),
            ShortcutActionValueText,
            stretchControl: true);

        var actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var saveButton = new Button { Content = text.Get("Common.Save") };
        saveButton.Click += SaveShortcutActionButton_Click;
        var cancelButton = new Button { Content = text.Get("Common.Cancel") };
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

    private void RefreshShortcutActionsSection()
    {
        if (currentSettingsPage != SettingsPageKind.Shortcuts
            || shortcutActionsSectionStartIndex < 0
            || shortcutActionsSectionStartIndex > SettingsContent.Children.Count)
        {
            NavigateToSettingsPage(SettingsPageKind.Shortcuts);
            return;
        }

        var settings = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        if (CommandMenuPreviewContent is not null)
        {
            CommandMenuPreviewContent.Content = CreateCommandMenuPreview(settings.Actions);
        }

        ResetShortcutActionEditorReferences();
        while (SettingsContent.Children.Count > shortcutActionsSectionStartIndex)
        {
            SettingsContent.Children.RemoveAt(SettingsContent.Children.Count - 1);
        }

        BuildShortcutActionsSection(settings);
    }

    private static SettingsExpander CreateSettingsExpander(
        string title,
        string subtitle,
        IEnumerable<SettingsCard> items,
        UIElement? action = null,
        bool isExpanded = true)
    {
        if (action is FrameworkElement actionElement)
        {
            actionElement.Margin = new Thickness(0, 0, 4, 0);
        }

        var expander = new SettingsExpander
        {
            Header = title,
            Description = subtitle,
            IsExpanded = isExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (action is not null)
        {
            expander.Content = action;
        }

        foreach (var item in items)
        {
            expander.Items.Add(item);
        }

        return expander;
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
        CommandMenuPreviewContent = null;
        shortcutActionsSectionStartIndex = -1;
        ResetShortcutActionEditorReferences();
        DeviceInfoSyncDisabledText = null;
        DeviceInfoSyncIdentifierText = null;
        DeviceInfoSyncIntervalBox = null;
        deviceInfoSyncItemMappingTexts.Clear();
        ViewLogsButton = null;
        ViewSettingsFolderButton = null;
        VerboseDiagnosticsToggle = null;
        VersionText = null;
    }

    private void ResetShortcutActionEditorReferences()
    {
        ShortcutActionNameText = null;
        ShortcutActionIconCombo = null;
        ShortcutActionShowInCommandMenuToggle = null;
        ShortcutActionGlobalShortcutRecorder = null;
        ShortcutActionTargetItemText = null;
        ShortcutActionTypeCombo = null;
        ShortcutActionValueText = null;
        ShortcutActionEditorErrorText = null;
    }

    private void RefreshSettingsBindings()
    {
        isRefreshingSettingsBindings = true;
        try
        {
            if (SkinCombo is not null)
            {
                SkinCombo.SelectedItem = SkinCombo.Items
                    .OfType<SitemapSkinOption>()
                    .First(option => option.Skin == settingsController.Current.Skin);
            }
            if (EndpointModeCombo is not null)
            {
                EndpointModeCombo.SelectedItem = EndpointModeCombo.Items
                    .OfType<EndpointModeOption>()
                    .First(option => option.Mode == settingsController.Current.EndpointMode);
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
                AppColorThemeCombo.SelectedItem = AppColorThemeCombo.Items
                    .OfType<AppColorThemeOption>()
                    .First(option => option.Theme == settingsController.Current.AppColorTheme);
            }
            if (AppLanguageCombo is not null)
            {
                AppLanguageCombo.SelectedItem = AppLanguageCombo.Items
                    .OfType<AppLanguageOption>()
                    .First(option => option.Language == settingsController.Current.AppLanguage);
            }
            RefreshAppLanguageRestartNotice();
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
                CommandMenuActivationModeCombo.SelectedItem = CommandMenuActivationModeCombo.Items
                    .OfType<RadialActivationModeOption>()
                    .First(option => option.Mode == shortcuts.CommandMenu.RadialActivationMode);
            }
            if (VerboseDiagnosticsToggle is not null)
            {
                VerboseDiagnosticsToggle.IsOn = settingsController.Current.VerboseDiagnostics;
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
            SetDeviceInfoSyncMappingText("BluetoothConnectedItem", deviceInfoSync.BluetoothConnectedItem);
            SetDeviceInfoSyncMappingText("BluetoothDeviceNamesItem", deviceInfoSync.BluetoothDeviceNamesItem);
            SetDeviceInfoSyncMappingText("OpenHabConnectionItem", deviceInfoSync.OpenHabConnectionItem);
            SetDeviceInfoSyncMappingText("FocusStateItem", deviceInfoSync.FocusStateItem);
            suppressFlyoutWidthChange = false;

            if (LocalTokenBox is not null)
            {
                LocalTokenBox.PlaceholderText = settingsController.Current.HasLocalToken
                    ? text.Get("Settings.Connection.LocalToken.StoredPlaceholder")
                    : text.Get("Settings.Connection.LocalToken.Placeholder");
            }
            if (CloudPasswordBox is not null)
            {
                CloudPasswordBox.PlaceholderText = settingsController.Current.HasCloudCredentials
                    ? text.Get("Settings.Connection.CloudPassword.StoredPlaceholder")
                    : text.Get("Settings.Connection.CloudPassword.Placeholder");
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
        if (isRefreshingSettingsBindings || sender is not ComboBox skinCombo || skinCombo.SelectedItem is not SitemapSkinOption option)
        {
            return;
        }

        settingsController.SetSkin(option.Skin);
        await refreshRuntimeAsync();
    }

    private async void EndpointModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ComboBox endpointModeCombo || endpointModeCombo.SelectedItem is not EndpointModeOption option)
        {
            return;
        }

        settingsController.SetEndpointMode(option.Mode);
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

        var transportKind = tag == LocalTransportTag ? TransportKind.Local : TransportKind.Cloud;
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
            setStatusText(SafeDiagnosticText.ForUserStatus(ex, text.Get("Settings.Connection.TokenSaveFailed")));
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

    private bool IsTokenBoxEdited(string tag) => tag == LocalTransportTag ? localTokenEdited : cloudTokenEdited;

    private void SetTokenBoxEdited(string tag, bool edited)
    {
        if (tag == LocalTransportTag)
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
                setStatusText(text.Get("Settings.Connection.CloudPasswordRequired"));
                RefreshSettingsBindings();
                return;
            }

            await settingsController.SetCloudCredentialsAsync(userName, password, CancellationToken.None);
            await refreshRuntimeAsync();
        }
        catch (Exception ex)
        {
            setStatusText(SafeDiagnosticText.ForUserStatus(ex, text.Get("Settings.Connection.CloudCredentialsSaveFailed")));
            RefreshSettingsBindings();
        }
    }

    private async void SettingsBreadcrumbRootButton_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToSettingsPageWithDiscardConfirmationAsync(SettingsPageKind.Root);
    }

    private void SettingsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SettingsContent.Width = Math.Max(0d, e.NewSize.Width);
        SettingsTransitionContent.Width = SettingsContent.Width;
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

    private void AppLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isRefreshingSettingsBindings
            && sender is ComboBox combo
            && combo.SelectedItem is AppLanguageOption option)
        {
            settingsController.SetAppLanguage(option.Language);
            RefreshAppLanguageRestartNotice();
        }
    }

    private void RefreshAppLanguageRestartNotice()
    {
        if (AppLanguageRestartInfoBar is null)
        {
            return;
        }

        var shouldShow = AppLanguageRuntime.ShouldShowRestartNotice(
            settingsController.Current.AppLanguage,
            appliedAppLanguage);
        AppLanguageRestartInfoBar.IsOpen = shouldShow;
        AppLanguageRestartInfoBar.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
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

    private void VerboseDiagnosticsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (isRefreshingSettingsBindings || sender is not ToggleSwitch toggle)
        {
            return;
        }

        settingsController.SetVerboseDiagnostics(toggle.IsOn);
        setStatusText(toggle.IsOn
            ? text.Get("Settings.About.VerboseDiagnostics.EnabledStatus")
            : text.Get("Settings.About.VerboseDiagnostics.DisabledStatus"));
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
        NavigateToSettingsPage(SettingsPageKind.DeviceInfoSync);
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
        if (isRefreshingSettingsBindings || sender is not ComboBox combo || combo.SelectedItem is not RadialActivationModeOption option)
        {
            return;
        }

        var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        settingsController.SetShortcutSettings(shortcuts with
        {
            CommandMenu = shortcuts.CommandMenu with
            {
                RadialActivationMode = option.Mode
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
            .Select(action => new ShortcutBindingOwner(text.Format("Shortcuts.ActionOwner", action.Name), action.GlobalShortcut!))
            .ToArray();
        var validation = ShortcutValidation.ValidateBinding(
            binding,
            text.Get("Shortcuts.CommandMenu.OwnerName"),
            existingBindings,
            allowUnassigned: false,
            text);
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

    private Border CreateShortcutActionRow(ShortcutAction action, int index, int actionCount)
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

        AddActionTableElement(row, CreateShortcutIconPresenter(action.IconId, includeId: true), 0);
        AddActionTableCell(row, action.Name, 1);
        AddActionTableCell(row, DescribeActionAvailability(action), 2);
        AddActionTableCell(row, FormatShortcutBinding(action.GlobalShortcut), 3);
        AddActionTableCell(row, action.TargetItem, 4);
        AddActionTableCell(row, DescribeShortcutCommandType(action.CommandType), 5);
        AddActionTableCell(row, action.CommandValue ?? "-", 6);

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        var moveUpButton = CreateActionIconButton("\uE70E", text.Get("Settings.Shortcuts.Actions.MoveUp"), action.Id);
        moveUpButton.IsEnabled = index > 0;
        moveUpButton.Click += MoveShortcutActionUpButton_Click;
        var moveDownButton = CreateActionIconButton("\uE70D", text.Get("Settings.Shortcuts.Actions.MoveDown"), action.Id);
        moveDownButton.IsEnabled = index < actionCount - 1;
        moveDownButton.Click += MoveShortcutActionDownButton_Click;
        var editButton = CreateActionIconButton("\uE70F", text.Get("Settings.Shortcuts.Actions.Edit"), action.Id);
        editButton.Click += EditShortcutActionButton_Click;
        var deleteButton = CreateActionIconButton("\uE74D", text.Get("Settings.Shortcuts.Actions.Delete"), action.Id);
        deleteButton.Click += DeleteShortcutActionButton_Click;
        actionsPanel.Children.Add(moveUpButton);
        actionsPanel.Children.Add(moveDownButton);
        actionsPanel.Children.Add(editButton);
        actionsPanel.Children.Add(deleteButton);
        Grid.SetColumn(actionsPanel, 7);
        row.Children.Add(actionsPanel);

        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources[CardStrokeBrushResourceKey],
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = row
        };
    }

    private static Button CreateActionIconButton(string glyph, string name, string actionId)
    {
        var button = new Button
        {
            Tag = actionId,
            Width = 34,
            Height = 32,
            MinWidth = 0,
            Padding = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14
            }
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
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

    private static void AddActionTableElement(Grid row, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        row.Children.Add(element);
    }

    private static StackPanel CreateShortcutIconPresenter(string iconId, bool includeId)
    {
        var icon = ShortcutIconCatalog.All.FirstOrDefault(entry => string.Equals(entry.Id, iconId, StringComparison.Ordinal))
            ?? new ShortcutIconDefinition(iconId, iconId, CustomShortcutIconId);
        return CreateShortcutIconPresenter(icon, includeId);
    }

    private static StackPanel CreateShortcutIconPresenter(ShortcutIconDefinition icon, bool includeId)
    {
        var label = includeId ? $"{icon.Label} ({icon.Id})" : icon.Label;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new FontIcon
                {
                    Glyph = RadialCommandMenuWindow.ResolveShortcutGlyph(icon.Id),
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Width = 20,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private string DescribeActionAvailability(ShortcutAction action)
    {
        var hasShortcut = action.GlobalShortcut is not null;
        if (action.ShowInCommandMenu && hasShortcut)
        {
            return text.Get("Settings.Shortcuts.Availability.ShortcutAndCommandMenu");
        }

        if (action.ShowInCommandMenu)
        {
            return text.Get("Settings.Shortcuts.Availability.CommandMenuOnly");
        }

        return hasShortcut
            ? text.Get("Settings.Shortcuts.Availability.ShortcutOnly")
            : text.Get("Settings.Shortcuts.Availability.Unavailable");
    }

    private string DescribeShortcutCommandType(ShortcutCommandType commandType) =>
        commandType switch
        {
            ShortcutCommandType.Toggle => text.Get("Settings.Shortcuts.CommandType.Toggle"),
            ShortcutCommandType.OnOff => text.Get("Settings.Shortcuts.CommandType.OnOff"),
            ShortcutCommandType.OpenClose => text.Get("Settings.Shortcuts.CommandType.OpenClose"),
            ShortcutCommandType.OpenSlider => text.Get("Settings.Shortcuts.CommandType.OpenSlider"),
            ShortcutCommandType.OpenColorPicker => text.Get("Settings.Shortcuts.CommandType.OpenColorPicker"),
            ShortcutCommandType.SendCommand => text.Get("Settings.Shortcuts.CommandType.SendCommand"),
            _ => commandType.ToString()
        };

    private string FormatShortcutBinding(ShortcutBinding? binding)
    {
        var formatted = ShortcutBindingFormatter.Format(binding);
        return string.Equals(formatted, "Unassigned", StringComparison.Ordinal)
            ? text.Get("Settings.Shortcuts.Shortcut.Unassigned")
            : formatted;
    }

    private ShortcutAction? ResolveShortcutActionDraft(IEnumerable<ShortcutAction> actions)
    {
        if (creatingShortcutAction)
        {
            return new ShortcutAction(
                Guid.NewGuid().ToString("N"),
                string.Empty,
                CustomShortcutIconId,
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
        RefreshShortcutActionsSection();
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
        RefreshShortcutActionsSection();
    }

    private async void MoveShortcutActionUpButton_Click(object sender, RoutedEventArgs e)
    {
        await MoveShortcutActionAsync(sender, -1);
    }

    private async void MoveShortcutActionDownButton_Click(object sender, RoutedEventArgs e)
    {
        await MoveShortcutActionAsync(sender, 1);
    }

    private async Task MoveShortcutActionAsync(object sender, int offset)
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
        var actions = shortcuts.Actions.ToList();
        var index = actions.FindIndex(candidate => string.Equals(candidate.Id, actionId, StringComparison.Ordinal));
        var targetIndex = index + offset;
        if (index < 0 || targetIndex < 0 || targetIndex >= actions.Count)
        {
            return;
        }

        (actions[index], actions[targetIndex]) = (actions[targetIndex], actions[index]);
        settingsController.SetShortcutSettings(shortcuts with
        {
            Actions = actions.ToImmutableArray()
        });

        RefreshShortcutActionsSection();
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

        var actionName = string.IsNullOrWhiteSpace(action.Name) ? text.Get("Settings.Shortcuts.Actions.Unnamed") : action.Name.Trim();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = text.Get("Settings.Shortcuts.DeleteDialog.Title"),
            Content = text.Format("Settings.Shortcuts.DeleteDialog.Message", actionName),
            PrimaryButtonText = text.Get("Common.Delete"),
            CloseButtonText = text.Get("Common.Cancel"),
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

        RefreshShortcutActionsSection();
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
        RefreshShortcutActionsSection();
    }

    private void SaveShortcutActionButton_Click(object sender, RoutedEventArgs e)
    {
        var shortcuts = (settingsController.Current.Shortcuts ?? ShortcutSettings.Default).Normalized();
        var selectedType = ShortcutActionTypeCombo?.SelectedItem is ShortcutCommandType commandType
            ? commandType
            : ShortcutActionTypeCombo?.SelectedItem is ShortcutCommandTypeOption option
                ? option.CommandType
                : ShortcutCommandType.Toggle;
        var selectedIcon = GetSelectedShortcutIcon(ShortcutActionIconCombo);

        var actionId = creatingShortcutAction
            ? Guid.NewGuid().ToString("N")
            : editingShortcutActionId ?? Guid.NewGuid().ToString("N");
        var updated = new ShortcutAction(
            actionId,
            ShortcutActionNameText?.Text?.Trim() ?? string.Empty,
            selectedIcon?.Id ?? CustomShortcutIconId,
            ShortcutActionShowInCommandMenuToggle?.IsOn ?? false,
            ShortcutActionGlobalShortcutRecorder?.Binding,
            ShortcutActionTargetItemText?.Text?.Trim() ?? string.Empty,
            selectedType,
            string.IsNullOrWhiteSpace(ShortcutActionValueText?.Text) ? null : ShortcutActionValueText!.Text.Trim());

        var errors = new List<string>();
        var actionValidation = ShortcutValidation.ValidateAction(updated, text);
        if (!actionValidation.IsValid)
        {
            errors.AddRange(actionValidation.Errors);
        }

        var existingBindings = new List<ShortcutBindingOwner>();
        if (shortcuts.CommandMenu.Binding is not null)
        {
            existingBindings.Add(new ShortcutBindingOwner(text.Get("Shortcuts.CommandMenu.OwnerName"), shortcuts.CommandMenu.Binding));
        }

        foreach (var action in shortcuts.Actions.Where(action => action.GlobalShortcut is not null && !string.Equals(action.Id, updated.Id, StringComparison.Ordinal)))
        {
            existingBindings.Add(new ShortcutBindingOwner(text.Format("Shortcuts.ActionOwner", action.Name), action.GlobalShortcut!));
        }

        var bindingValidation = ShortcutValidation.ValidateBinding(
            updated.GlobalShortcut,
            text.Format("Settings.Shortcuts.CurrentActionOwner", updated.Id),
            existingBindings,
            allowUnassigned: true,
            text);
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
        RefreshShortcutActionsSection();
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
            Title = text.Get("Settings.Shortcuts.DiscardDialog.Title"),
            Content = text.Get("Settings.Shortcuts.DiscardDialog.Message"),
            PrimaryButtonText = text.Get("Common.Continue"),
            CloseButtonText = text.Get("Common.Cancel"),
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
            : ShortcutActionTypeCombo.SelectedItem is ShortcutCommandTypeOption option
                ? option.CommandType
                : ShortcutCommandType.Toggle;
        var selectedIcon = GetSelectedShortcutIcon(ShortcutActionIconCombo);

        return new ShortcutAction(
            actionId,
            ShortcutActionNameText.Text.Trim(),
            selectedIcon?.Id ?? CustomShortcutIconId,
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

    private static ShortcutIconDefinition? GetSelectedShortcutIcon(ComboBox? combo)
    {
        return (combo?.SelectedItem as ComboBoxItem)?.Tag as ShortcutIconDefinition
            ?? combo?.SelectedItem as ShortcutIconDefinition;
    }

    private async Task NavigateToSettingsPageWithDiscardConfirmationAsync(SettingsPageKind destination)
    {
        if (!await ConfirmDiscardShortcutActionChangesIfNeededAsync())
        {
            return;
        }

        await NavigateToSettingsPageAsync(destination, animate: true);
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
            NavigateToSettingsPage(SettingsPageKind.DeviceInfoSync);
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
            BluetoothConnectedItem = GetDeviceInfoSyncMappingText("BluetoothConnectedItem", current.BluetoothConnectedItem),
            BluetoothDeviceNamesItem = GetDeviceInfoSyncMappingText("BluetoothDeviceNamesItem", current.BluetoothDeviceNamesItem),
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
                var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    UseShellExecute = false,
                    ArgumentList =
                    {
                        "/select,",
                        logPath
                    }
                });
            }
        }
        catch (Exception ex)
        {
            setStatusText(SafeDiagnosticText.ForUserStatus(ex, text.Get("Settings.About.OpenLogsFailed")));
        }
    }

    private void ViewSettingsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settingsDirectory = settingsController.SettingsDirectoryPath;
            Directory.CreateDirectory(settingsDirectory);
            Process.Start(new ProcessStartInfo(settingsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            setStatusText(SafeDiagnosticText.ForUserStatus(ex, text.Get("Settings.About.OpenSettingsFolderFailed")));
        }
    }
}
