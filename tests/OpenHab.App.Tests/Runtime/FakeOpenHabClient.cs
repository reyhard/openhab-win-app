using OpenHab.Core.Api;
using OpenHab.Core.Ui;

namespace OpenHab.App.Tests.Runtime;

public sealed class FakeOpenHabClient : IOpenHabClient
{
    private readonly Queue<Func<string, CancellationToken, Task<string>>> sitemapResponses = new();

    public List<(string ItemName, string Command)> CommandsSent { get; } = new();
    public List<(string ItemName, string State)> StatesSet { get; } = new();
    public List<string> RequestedSitemaps { get; } = new();
    public List<SitemapInfo> Sitemaps { get; set; } = new();
    public List<MainUiPageComponent> MainUiPageComponents { get; set; } = new();
    public Exception? SetItemStateFailure { get; set; }
    public Dictionary<string, Exception> SetItemStateFailuresByItem { get; } = new();

    public void EnqueueSitemapJson(string json)
    {
        sitemapResponses.Enqueue((_, _) => Task.FromResult(json));
    }

    public void EnqueueSitemapFailure(Exception exception)
    {
        sitemapResponses.Enqueue((_, _) => Task.FromException<string>(exception));
    }

    public Task SendCommandAsync(string itemName, string command, CancellationToken cancellationToken)
    {
        CommandsSent.Add((itemName, command));
        return Task.CompletedTask;
    }

    public Task SetItemStateAsync(string itemName, string state, CancellationToken cancellationToken)
    {
        if (SetItemStateFailuresByItem.TryGetValue(itemName, out var perItemFailure))
        {
            return Task.FromException(perItemFailure);
        }

        if (SetItemStateFailure is not null)
        {
            return Task.FromException(SetItemStateFailure);
        }

        StatesSet.Add((itemName, state));
        return Task.CompletedTask;
    }

    public Task<string> GetSitemapJsonAsync(string sitemapName, CancellationToken cancellationToken)
    {
        RequestedSitemaps.Add(sitemapName);
        if (sitemapResponses.Count == 0)
        {
            throw new InvalidOperationException("No fake sitemap response enqueued.");
        }

        return sitemapResponses.Dequeue()(sitemapName, cancellationToken);
    }

    public Task<IReadOnlyList<SitemapInfo>> GetSitemapsAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<SitemapInfo>>(Sitemaps);
    }

    public Task<IReadOnlyList<MainUiPageComponent>> GetMainUiPageComponentsAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<MainUiPageComponent>>(MainUiPageComponents);
    }
}
