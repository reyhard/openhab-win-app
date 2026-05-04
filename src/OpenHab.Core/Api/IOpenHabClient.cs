namespace OpenHab.Core.Api;

public interface IOpenHabClient
{
    Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken);
    Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken);
    Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken);
}
