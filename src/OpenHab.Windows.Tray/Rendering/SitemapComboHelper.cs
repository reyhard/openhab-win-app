using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml.Controls;
using OpenHab.Core.Api;

namespace OpenHab.Windows.Tray.Rendering;

[ExcludeFromCodeCoverage(Justification = "WinUI ComboBox display glue.")]
public static class SitemapComboHelper
{
    public static void Populate(
        ComboBox combo,
        IReadOnlyList<SitemapInfo> sitemaps,
        string currentSitemapName,
        SelectionChangedEventHandler onSelectionChanged)
    {
        combo.SelectionChanged -= onSelectionChanged;
        combo.Items.Clear();

        foreach (var sitemap in sitemaps)
        {
            var item = new ComboBoxItem
            {
                Content = sitemap.Label,
                Tag = sitemap.Name
            };

            combo.Items.Add(item);

            if (string.Equals(sitemap.Name, currentSitemapName, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
            }
        }

        combo.SelectionChanged += onSelectionChanged;
    }
}
