namespace OpenHab.Core.Events;

public interface IOpenHabEventStreamClient : IDisposable
{
    event EventHandler<OpenHabEvent>? EventReceived;
    event EventHandler<SitemapWidgetEvent>? WidgetEventReceived;
    event EventHandler<string>? ConnectionStateChanged;
    bool IsConnected { get; }
    Task ConnectAsync(Uri baseUri, CancellationToken cancellationToken = default);
    Task<string?> SubscribeToSitemapEventsAsync(Uri baseUri, CancellationToken cancellationToken = default);
}
