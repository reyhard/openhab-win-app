using OpenHab.Core.Ui;

namespace OpenHab.Core.Api;

public sealed record SitemapInfo(string Name, string Label);
public sealed record OpenHabItemSummary(string Name, string Label, string Type, string? State);

public interface IOpenHabClient
{
    Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken);
    Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken);
    Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenHabItemSummary>> GetItemsAsync(CancellationToken cancellationToken);
    Task<string?> GetItemStateAsync(string itemName, CancellationToken cancellationToken);
    Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct);
    Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct);
}
