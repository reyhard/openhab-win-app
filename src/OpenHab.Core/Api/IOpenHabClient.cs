namespace OpenHab.Core.Api;

public sealed record SitemapInfo(string Name, string Label);

public interface IOpenHabClient
{
    Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken);
    Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken);
    Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken);
    Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct);
}
