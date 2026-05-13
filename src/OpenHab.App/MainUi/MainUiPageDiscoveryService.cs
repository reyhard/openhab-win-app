using OpenHab.Core.Api;
using OpenHab.Core.Ui;

namespace OpenHab.App.MainUi;

public sealed class MainUiPageDiscoveryService(IOpenHabClient client)
{
    public async Task<IReadOnlyList<MainUiPageLink>> DiscoverPromotedLinksAsync(CancellationToken cancellationToken)
    {
        var pages = await client.GetMainUiPageComponentsAsync(cancellationToken);
        return BuildPromotedLinks(pages);
    }

    public static IReadOnlyList<MainUiPageLink> BuildPromotedLinks(IEnumerable<MainUiPageComponent> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        return pages
            .Where(page => page.GetConfigBoolean("sidebar"))
            .Select(static page => page with { Uid = page.Uid.Trim() })
            .Where(static page => !string.IsNullOrWhiteSpace(page.Uid))
            .Select(ToLink)
            .OrderBy(link => link.Order ?? int.MaxValue)
            .ThenBy(link => link.Label, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(link => link.Uid, StringComparer.Ordinal)
            .ToArray();
    }

    private static MainUiPageLink ToLink(MainUiPageComponent page)
    {
        var label = page.GetConfigString("label");
        if (string.IsNullOrWhiteSpace(label))
        {
            label = page.Uid;
        }

        return new MainUiPageLink(
            page.Uid,
            label.Trim(),
            "/page/" + Uri.EscapeDataString(page.Uid),
            page.GetConfigString("icon"),
            page.Component,
            page.GetConfigInt32("order"));
    }
}
