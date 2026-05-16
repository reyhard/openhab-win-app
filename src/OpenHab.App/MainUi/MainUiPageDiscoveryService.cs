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
        return MainUiPagePromotionPlanner.PlanPromotedLinks(pages);
    }
}
