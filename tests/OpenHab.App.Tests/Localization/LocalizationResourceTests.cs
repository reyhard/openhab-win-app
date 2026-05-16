using System.Text.RegularExpressions;
using System.Xml.Linq;

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
    }

    [Fact]
    public void TranslatedResourcesPreserveEnglishPlaceholders()
    {
        var english = ReadResources(EnglishResourcesPath);
        foreach (var translatedPath in Directory.EnumerateFiles(StringsRootPath, "Resources.resw", SearchOption.AllDirectories)
                     .Where(path => !string.Equals(path, EnglishResourcesPath, StringComparison.OrdinalIgnoreCase)))
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

    private static string EnglishResourcesPath => Path.Combine(StringsRootPath, "en-US", "Resources.resw");

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
