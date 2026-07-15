using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpenHab.App.Localization;

namespace OpenHab.App.Tests.Localization;

public sealed partial class LocalizationResourceTests
{
    [Fact]
    public void EnglishResourcesContainRequiredInitialKeys()
    {
        var document = XDocument.Load(EnglishResourcesPath);
        var names = ReadResourceNames(document);

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("App.Name", names);
        Assert.Contains("App.Description", names);
        Assert.Contains("Common.Cancel", names);
        Assert.Contains("Common.Delete", names);
        Assert.Contains("MainWindow_SidebarCollapseButton.ToolTipService.ToolTip", names);
        Assert.Contains("Notifications.Empty.NoNotifications", names);
        Assert.Contains("Notifications.Elapsed.MinutesAgo", names);
        Assert.Contains("Settings.Appearance.Skin.Title", names);
        Assert.Contains("Settings.Appearance.Skin.Subtitle", names);
        Assert.Contains("Settings.Appearance.Skin.Basic", names);
        Assert.Contains("Settings.Appearance.Skin.Windows11", names);
        Assert.Contains("Settings.Appearance.Theme.Title", names);
        Assert.Contains("Settings.Appearance.Theme.Subtitle", names);
        Assert.Contains("Settings.Appearance.Theme.Dark", names);
        Assert.Contains("Settings.Appearance.Theme.Bright", names);
        Assert.Contains("Settings.Appearance.Theme.FollowSystem", names);
        Assert.Contains("Settings.Appearance.IconStyle.Title", names);
        Assert.Contains("Settings.Appearance.IconStyle.Subtitle", names);
        Assert.Contains("Settings.About.VersionAndAuthor", names);
    }

    [Fact]
    public void TranslatedResourcesPreserveEnglishPlaceholders()
    {
        var english = ReadResources(EnglishResourcesPath);
        foreach (var translatedPath in TranslatedResourcePaths())
        {
            var translated = ReadResources(translatedPath);
            foreach (var (key, englishValue) in english)
            {
                if (!translated.TryGetValue(key, out var translatedValue))
                {
                    continue;
                }

                Assert.Equal(Placeholders(englishValue), Placeholders(translatedValue));
            }
        }
    }

    [Fact]
    public void PolishResourcesContainEveryEnglishKey()
    {
        var englishKeys = ReadResources(EnglishResourcesPath).Keys.ToArray();
        var polish = ReadResources(Path.Combine(StringsRootPath, "pl-PL", "Resources.resw"));

        foreach (var englishKey in englishKeys)
        {
            Assert.Contains(englishKey, polish.Keys);
        }
    }

    [Fact]
    public void ReconnectingLiveUpdatesTextHasEnglishFallbackAndTranslatedResources()
    {
        const string key = "Runtime.LiveUpdates.Reconnecting";
        var english = ReadResources(EnglishResourcesPath);
        var polish = ReadResources(Path.Combine(StringsRootPath, "pl-PL", "Resources.resw"));

        Assert.Equal("Reconnecting to live updates...", DefaultEnglishTextLocalizer.Instance.Get(key));
        Assert.Equal("Reconnecting to live updates...", english[key]);
        Assert.Equal("Ponowne łączenie z aktualizacjami na żywo...", polish[key]);
    }

    [Fact]
    public void DynamicWindowsTextHasEnglishFallbackAndTranslatedResources()
    {
        var english = ReadResources(EnglishResourcesPath);
        var polish = ReadResources(Path.Combine(StringsRootPath, "pl-PL", "Resources.resw"));
        var keys = new[]
        {
            "Runtime.Sitemap.NoneDetected",
            "Runtime.Sitemap.MultipleDetected",
            "MainWindow.Sitemap.Show",
            "MainWindow.Sitemap.Hide",
            "MainWindow.Navigation.Expand",
            "MainWindow.Navigation.Collapse",
            "MainWindow.MainUiPages.Empty",
            "MainWindow.MainUiPages.RefreshFailed",
            "MainUi.Error.Unavailable",
            "MainUi.Error.CheckEndpointAndCredentials",
            "Voice.Status.RequiresOnline",
            "Voice.Status.InvalidAction",
            "Voice.Status.ServiceUnavailable",
            "Voice.Status.Listening",
            "Voice.Status.Canceled",
            "Voice.Status.OpeningWindowsSettings",
            "Voice.Confirmation.Title",
            "Voice.Confirmation.TranscriptLabel",
            "Voice.Confirmation.Send",
            "Voice.Confirmation.Cancel",
            "Voice.Confirmation.EmptyTranscript",
            "Shortcuts.Status.Disconnected",
            "Shortcuts.Status.ClientUnavailable",
            "Shortcuts.Status.SurfaceFailed"
        };

        foreach (var key in keys)
        {
            Assert.Equal(english[key], DefaultEnglishTextLocalizer.Instance.Get(key));
            Assert.Contains(key, polish.Keys);
        }
    }

    [Fact]
    public void ReviewedWindowsSurfacesDoNotKeepUserVisibleEnglishLiterals()
    {
        var sourcesByRelativePath = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Path.Combine("src", "OpenHab.Windows.Tray", "App.xaml.cs")] =
            [
                "No sitemaps were detected",
                "Multiple sitemaps detected — choose one",
                "Voice commands require an online openHAB connection.",
                "Voice command action is invalid.",
                "Voice command service is unavailable.",
                "Listening for voice command...",
                "Voice command canceled.",
                "Opening Windows Settings...",
                "Cannot execute action while disconnected.",
                "Client is unavailable.",
                "Command surface could not be opened."
            ],
            [Path.Combine("src", "OpenHab.Windows.Tray", "MainWindow.xaml.cs")] =
            [
                "Show sitemap",
                "Hide sitemap",
                "Expand navigation",
                "Collapse navigation",
                "No promoted pages",
                "Could not refresh pages. Showing cached links."
            ],
            [Path.Combine("src", "OpenHab.Windows.Tray", "MainUi", "MainUiWebViewHost.xaml")] =
            [
                "Loading openHAB Main UI",
                "Main UI could not be loaded",
                "Retry",
                "Open in browser"
            ],
            [Path.Combine("src", "OpenHab.Windows.Tray", "MainUi", "MainUiWebViewHost.xaml.cs")] =
            [
                "Main UI could not be loaded. WebView2 may be unavailable.",
                "Check the configured endpoint and credentials, then retry."
            ],
            [Path.Combine("src", "OpenHab.Windows.Tray", "Voice", "VoiceCommandConfirmationWindow.cs")] =
            [
                "Confirm voice command",
                "Recognized command",
                "Send",
                "Cancel",
                "(empty)"
            ]
        };

        foreach (var (relativePath, literals) in sourcesByRelativePath)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRootPath, relativePath));
            foreach (var literal in literals)
            {
                Assert.DoesNotContain($"\"{literal}\"", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void XamlResourceIdsHaveEnglishResourceEntries()
    {
        var document = XDocument.Load(EnglishResourcesPath);
        var resourceNames = ReadResourceNames(document);
        var xamlFiles = Directory.GetFiles(
            Path.Combine(RepositoryRootPath, "src", "OpenHab.Windows.Tray"),
            "*.xaml",
            SearchOption.AllDirectories);

        var resourceIds = xamlFiles
            .SelectMany(File.ReadLines)
            .SelectMany(line => XamlUidRegex().Matches(line).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var resourceId in resourceIds)
        {
            Assert.Contains(resourceNames, name => name.StartsWith(resourceId + ".", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AppearanceSettingsUseLocalizedLabelsAndDoNotWrapRestartNoticeInCard()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRootPath,
            "src",
            "OpenHab.Windows.Tray",
            "Settings",
            "SettingsPageControl.xaml.cs"));

        Assert.DoesNotContain("\"Skin\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Choose the sitemap rendering style\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"App color theme\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Choose the main window and flyout color mode\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Use Windows 11 style icons\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Prefer Fluent-style symbols for sitemap widgets\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateSettingsGroup(skinRow, themeRow, languageRow, AppLanguageRestartInfoBar, iconStyleRow)", source, StringComparison.Ordinal);
        Assert.Contains("SettingsContent.Children.Add(AppLanguageRestartInfoBar)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSubpagesDoNotKeepUserVisibleEnglishLabelsInCode()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRootPath,
            "src",
            "OpenHab.Windows.Tray",
            "Settings",
            "SettingsPageControl.xaml.cs"));

        var literals = new[]
        {
            "Endpoint mode",
            "Choose how the app selects local or cloud connectivity",
            "Local endpoint",
            "Cloud endpoint",
            "Launch at startup",
            "Notification check interval",
            "Device Info Sync is disabled.",
            "Device identifier",
            "openHAB Item mappings",
            "View logs",
            "Diagnostic logs",
            "openHAB Windows App v{version}",
            "Built-in shortcuts",
            "Global shortcut",
            "Command menu preview",
            "Voice mode",
            "Actions and shortcuts",
            "No actions yet.",
            "Action name",
            "Show in command menu",
            "Command value",
            "Delete action",
            "Discard unsaved changes?"
        };

        foreach (var literal in literals)
        {
            Assert.DoesNotContain(literal, source, StringComparison.Ordinal);
        }
    }

    private static string EnglishResourcesPath => Path.Combine(StringsRootPath, "en-US", "Resources.resw");

    private static string[] TranslatedResourcePaths() =>
        Directory.EnumerateFiles(StringsRootPath, "Resources.resw", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, EnglishResourcesPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string StringsRootPath => Path.Combine(RepositoryRootPath, "src", "OpenHab.Windows.Tray", "Strings");

    private static string RepositoryRootPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", ".."));

    private static string[] ReadResourceNames(XDocument document) =>
        document.Descendants("data")
            .Select(node => (string?)node.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

    private static Dictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path)
            .Descendants("data")
            .Select(node => new
            {
                Name = (string?)node.Attribute("name"),
                Value = node.Element("value")?.Value ?? string.Empty
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name!, item => item.Value, StringComparer.Ordinal);

    private static string[] Placeholders(string value) =>
        CompositePlaceholderRegex()
            .Matches(value)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    [GeneratedRegex("\\{\\d+[^}]*\\}")]
    private static partial Regex CompositePlaceholderRegex();

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex XamlUidRegex();
}
