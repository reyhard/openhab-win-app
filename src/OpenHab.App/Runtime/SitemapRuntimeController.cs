using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Events;
using OpenHab.Core.Profiles;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Parsing;
using OpenHab.Sitemaps.Runtime;

namespace OpenHab.App.Runtime;

public sealed class SitemapRuntimeController
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRenderController renderController;
    private readonly Func<TransportKind, Uri, IOpenHabClient> clientFactory;
    private readonly IOpenHabEventStreamClient? eventStreamClient;
    private NormalizedSitemapPage? currentPage;
    private readonly Stack<NormalizedSitemapPage> backStack = new();
    private Dictionary<string, int>? itemIndexMap;
    private Dictionary<string, int>? widgetIdMap;
    private string? _subscriptionId;
    public bool CanGoBack => backStack.Count > 0;

    public SitemapRuntimeController(
        AppSettingsController settingsController,
        SitemapRenderController renderController,
        Func<TransportKind, Uri, IOpenHabClient> clientFactory,
        IOpenHabEventStreamClient? eventStreamClient = null)
    {
        ArgumentNullException.ThrowIfNull(settingsController);
        ArgumentNullException.ThrowIfNull(renderController);
        ArgumentNullException.ThrowIfNull(clientFactory);

        this.settingsController = settingsController;
        this.renderController = renderController;
        this.clientFactory = clientFactory;
        this.eventStreamClient = eventStreamClient;
    }

    public SitemapRuntimeSnapshot Current { get; private set; } = SitemapRuntimeSnapshot.Initial;

    /// <summary>Fired when the snapshot changes, including SSE-driven delta updates.</summary>
    public event EventHandler? SnapshotChanged;

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Current = Current with { IsBusy = true, StatusText = "Loading homepage...", ChangedRowIndices = [] };
        var settings = settingsController.Current;
        var primary = SelectPrimaryTransport(settings);

        try
        {
            var descriptor = await LoadDescriptorAsync(primary, settings.SitemapName, cancellationToken);
            Current = Current with
            {
                Descriptor = descriptor,
                ActiveTransport = primary.Kind,
                ConnectionState = ConnectionState.Online,
                Breadcrumbs = BuildBreadcrumbTrail(),
                StatusText = $"Connected via {primary.Kind.ToString().ToLowerInvariant()}.",
                IsBusy = false,
                HasError = false,
                ChangedRowIndices = []
            };

            // Reconnect event stream on sitemap/page change
            _ = StartSitemapEventStreamAsync(primary.BaseUri, settings.SitemapName, descriptor.PageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception firstError) when (settings.EndpointMode == EndpointMode.Automatic && primary.Kind == TransportKind.Local)
        {
            var fallback = new TransportSelection(TransportKind.Cloud, settings.CloudEndpoint);
            try
            {
                var descriptor = await LoadDescriptorAsync(fallback, settings.SitemapName, cancellationToken);
                Current = Current with
                {
                    Descriptor = descriptor,
                    ActiveTransport = fallback.Kind,
                    ConnectionState = ConnectionState.Online,
                    Breadcrumbs = BuildBreadcrumbTrail(),
                    StatusText = "Connected via cloud (local failed).",
                    IsBusy = false,
                    HasError = false,
                    ChangedRowIndices = []
                };

                // Reconnect event stream (cloud mode — no SSE, but keeps method consistent)
                _ = StartSitemapEventStreamAsync(fallback.BaseUri, settings.SitemapName, descriptor.PageId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception fallbackError)
            {
                Current = Current with
                {
                    ConnectionState = ConnectionState.Offline,
                    StatusText = $"Connection failed: {firstError.Message}; fallback failed: {fallbackError.Message}",
                    IsBusy = false,
                    HasError = true,
                    ChangedRowIndices = []
                };
            }
        }
        catch (Exception error)
        {
            Current = Current with
            {
                ConnectionState = ConnectionState.Offline,
                StatusText = $"Connection failed: {error.Message}",
                IsBusy = false,
                HasError = true,
                ChangedRowIndices = []
            };
        }
        finally
        {
            Current = Current with { IsBusy = false, ChangedRowIndices = [] };
        }
    }

    public async Task<bool> SendCommandForRowAsync(int rowIndex, string command, CancellationToken ct = default)
    {
        if (currentPage is null || rowIndex < 0 || rowIndex >= currentPage.Widgets.Count) return false;
        var widget = currentPage.Widgets[rowIndex];
        if (string.IsNullOrWhiteSpace(widget.ItemName)) return false;
        var activeTransport = Current.ActiveTransport ?? throw new InvalidOperationException("No active transport.");
        var endpoint = activeTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var client = clientFactory(activeTransport, endpoint);
        await client.SendCommandAsync(widget.ItemName, command, ct);
        await RefreshAsync(ct);
        return true;
    }

    public async Task<bool> NavigateToChildAsync(int rowIndex, CancellationToken ct = default)
    {
        if (currentPage is null || rowIndex < 0 || rowIndex >= currentPage.Widgets.Count) return false;
        var widget = currentPage.Widgets[rowIndex];
        if (widget.Children.Count == 0) return false;

        backStack.Push(currentPage);
        var childPage = widget.Children[0];
        var normalized = SitemapNormalizer.Normalize(childPage);
        currentPage = normalized;
        BuildItemIndexMap();

        var descriptor = renderController.BuildCurrentDescriptor(normalized);
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = $"Navigated to: {normalized.Label}",
            ChangedRowIndices = []
        };

        ReconnectForPage(normalized.Id);
        return true;
    }

    public bool NavigateBack()
    {
        if (backStack.Count == 0) return false;
        currentPage = backStack.Pop();
        BuildItemIndexMap();
        var descriptor = renderController.BuildCurrentDescriptor(currentPage);
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = currentPage.Label,
            ChangedRowIndices = []
        };
        ReconnectForPage(currentPage.Id);
        return true;
    }

    private void ReconnectForPage(string pageId)
    {
        if (eventStreamClient is null) return;
        var endpoint = Current.ActiveTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        _ = StartSitemapEventStreamAsync(endpoint, settingsController.Current.SitemapName, pageId, CancellationToken.None);
    }

    public bool NavigateToBreadcrumb(int breadcrumbIndex)
    {
        var trail = BuildBreadcrumbPages();
        if (breadcrumbIndex < 0 || breadcrumbIndex >= trail.Count)
        {
            return false;
        }

        if (breadcrumbIndex == trail.Count - 1)
        {
            return true;
        }

        currentPage = trail[breadcrumbIndex];
        backStack.Clear();
        for (var index = 0; index < breadcrumbIndex; index++)
        {
            backStack.Push(trail[index]);
        }

        BuildItemIndexMap();
        var descriptor = renderController.BuildCurrentDescriptor(currentPage);
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = currentPage.Label,
            ChangedRowIndices = []
        };
        ReconnectForPage(currentPage.Id);
        return true;
    }

    public async Task<bool> ActivateRowAsync(int rowIndex, CancellationToken cancellationToken = default)
    {
        if (rowIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        if (currentPage is null)
        {
            return false;
        }

        if (rowIndex >= currentPage.Widgets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        var widget = currentPage.Widgets[rowIndex];
        if (widget.Type != SitemapWidgetType.Switch || string.IsNullOrWhiteSpace(widget.ItemName))
        {
            return false;
        }

        var activeTransport = Current.ActiveTransport
            ?? throw new InvalidOperationException("Cannot activate a row without an active transport.");
        var endpoint = activeTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var client = clientFactory(activeTransport, endpoint);
        var command = string.Equals(widget.State, "ON", StringComparison.OrdinalIgnoreCase) ? "OFF" : "ON";
        await client.SendCommandAsync(widget.ItemName, command, cancellationToken);
        await RefreshAsync(cancellationToken);
        return true;
    }

    private async Task<OpenHab.Rendering.Descriptors.SitemapRenderDescriptor> LoadDescriptorAsync(
        TransportSelection transport,
        string sitemapName,
        CancellationToken cancellationToken)
    {
        var client = clientFactory(transport.Kind, transport.BaseUri);
        var json = await client.GetSitemapJsonAsync(sitemapName, cancellationToken);
        var parsed = OpenHabSitemapJsonParser.ParseHomepage(json);
        var normalized = SitemapNormalizer.Normalize(parsed);
        backStack.Clear();
        currentPage = normalized;
        BuildItemIndexMap();
        return renderController.BuildCurrentDescriptor(normalized);
    }

    private bool _eventStreamStarted;
    private string? _eventStreamSitemapName;
    private string? _eventStreamPageId;

    public async Task StartSitemapEventStreamAsync(Uri localBaseUri, string sitemapName, string pageId, CancellationToken ct = default)
    {
        if (eventStreamClient is null) return;

        // If already connected to the same sitemap/page, skip
        if (_eventStreamStarted && _eventStreamSitemapName == sitemapName && _eventStreamPageId == pageId)
            return;

        _eventStreamStarted = true;
        _eventStreamSitemapName = sitemapName;
        _eventStreamPageId = pageId;

        DiagnosticLogger.Info($"Starting sitemap event stream to {localBaseUri} for sitemap '{sitemapName}' page '{pageId}'");

        eventStreamClient.ConnectionStateChanged += OnConnectionStateChanged;
        eventStreamClient.EventReceived += OnEventReceived;
        eventStreamClient.WidgetEventReceived += OnWidgetEventReceived;

        _subscriptionId = await eventStreamClient.SubscribeToSitemapEventsAsync(localBaseUri, ct);
        if (_subscriptionId is null)
        {
            DiagnosticLogger.Warn("Failed to subscribe to sitemap events — no subscription ID returned");
            _eventStreamStarted = false;
            return;
        }

        DiagnosticLogger.Info($"Sitemap event subscription created: {_subscriptionId}");
        var sseUrl = new Uri(localBaseUri, $"rest/sitemaps/events/{_subscriptionId}?sitemap={Uri.EscapeDataString(sitemapName)}&pageid={Uri.EscapeDataString(pageId)}");
        _ = eventStreamClient.ConnectAsync(sseUrl, ct);
    }

    public async Task ReconnectSitemapEventStreamAsync(Uri localBaseUri, string sitemapName, string pageId, CancellationToken ct = default)
    {
        if (eventStreamClient is null || _subscriptionId is null) return;

        DiagnosticLogger.Info($"Reconnecting sitemap event stream for page '{pageId}'");
        var sseUrl = new Uri(localBaseUri, $"rest/sitemaps/events/{_subscriptionId}?sitemap={Uri.EscapeDataString(sitemapName)}&pageid={Uri.EscapeDataString(pageId)}");
        _ = eventStreamClient.ConnectAsync(sseUrl, ct);
    }

    // Keep original StartEventStream for backward compat (raw events)
    public void StartEventStream(Uri localBaseUri, CancellationToken ct = default)
    {
        if (eventStreamClient is null) return;
        if (_eventStreamStarted) return;
        _eventStreamStarted = true;

        DiagnosticLogger.Info($"Starting event stream to {localBaseUri}");

        eventStreamClient.ConnectionStateChanged += OnConnectionStateChanged;
        eventStreamClient.EventReceived += OnEventReceived;

        _ = eventStreamClient.ConnectAsync(new Uri(localBaseUri, "rest/events?topics=openhab/items/*/state,openhab/items/*/command"), ct);
    }

    private void OnConnectionStateChanged(object? sender, string state)
    {
        Current = Current with
        {
            ConnectionState = state switch
            {
                "connected" => ConnectionState.Online,
                "disconnected" => ConnectionState.Degraded,
                _ => Current.ConnectionState
            },
            StatusText = state switch
            {
                "connected" => Current.StatusText,
                "disconnected" => "Live updates unavailable. Refresh manually.",
                "reconnecting" => "Reconnecting to live updates...",
                _ => Current.StatusText
            },
            ChangedRowIndices = []
        };
    }

    private void OnEventReceived(object? sender, OpenHabEvent e)
    {
        if (e is ItemStateChangedEvent stateChanged)
        {
            if (DiagnosticLogger.VerboseEventLogging)
                DiagnosticLogger.Info($"SSE state change: {stateChanged.ItemName} = {stateChanged.State}");
            ApplyItemState(stateChanged.ItemName, stateChanged.State);
        }
    }

    private void OnWidgetEventReceived(object? sender, SitemapWidgetEvent e)
    {
        DiagnosticLogger.Info($"SSE widget event: id={e.WidgetId} item={e.ItemName} state={e.ItemState} vis={e.Visibility}");
        ApplyWidgetEvent(e);
    }

    private void ApplyItemState(string itemName, string newState)
    {
        if (itemIndexMap is null || currentPage is null) return;
        if (!itemIndexMap.TryGetValue(itemName, out var index))
        {
            if (DiagnosticLogger.VerboseEventLogging)
                DiagnosticLogger.Info($"SSE: item '{itemName}' not on current page, skipping");
            return;
        }
        if (index < 0 || index >= currentPage.Widgets.Count) return;

        var widget = currentPage.Widgets[index];
        if (string.Equals(widget.State, newState, StringComparison.Ordinal)) return;

        var widgets = currentPage.Widgets.ToList();
        widgets[index] = widget with { State = newState };
        currentPage = currentPage with { Widgets = widgets.AsReadOnly() };

        Current = Current with
        {
            Descriptor = renderController.BuildCurrentDescriptor(currentPage),
            ChangedRowIndices = new[] { index }
        };

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyWidgetEvent(SitemapWidgetEvent e)
    {
        if (widgetIdMap is null || currentPage is null) return;

        // Try to find by widgetId first
        int? index = null;
        if (!string.IsNullOrEmpty(e.WidgetId) && widgetIdMap.TryGetValue(e.WidgetId, out var widIndex))
        {
            index = widIndex;
        }
        // Fallback: find by item name
        else if (!string.IsNullOrEmpty(e.ItemName) && itemIndexMap is not null &&
                 itemIndexMap.TryGetValue(e.ItemName, out var itemIndex))
        {
            index = itemIndex;
        }

        if (index is null || index.Value < 0 || index.Value >= currentPage.Widgets.Count)
        {
            DiagnosticLogger.Warn($"SSE widget event: widget not found by id='{e.WidgetId}' or item='{e.ItemName}'");
            return;
        }

        var widget = currentPage.Widgets[index.Value];
        var changed = false;

        // Apply state change
        if (!string.IsNullOrEmpty(e.ItemState) && !string.Equals(widget.State, e.ItemState, StringComparison.Ordinal))
        {
            widget = widget with { State = e.ItemState };
            changed = true;
        }

        // Apply label change if description changed
        if (e.DescriptionChanged && !string.IsNullOrEmpty(e.Label) && !string.Equals(widget.Label, e.Label, StringComparison.Ordinal))
        {
            widget = widget with { Label = e.Label };
            changed = true;
        }

        // Apply visibility change
        if (widget.IsVisible != e.Visibility)
        {
            widget = widget with { IsVisible = e.Visibility };
            changed = true;
        }

        if (!changed) return;

        var widgets = currentPage.Widgets.ToList();
        widgets[index.Value] = widget;
        currentPage = currentPage with { Widgets = widgets.AsReadOnly() };

        Current = Current with
        {
            Descriptor = renderController.BuildCurrentDescriptor(currentPage),
            ChangedRowIndices = new[] { index.Value }
        };

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildItemIndexMap()
    {
        if (currentPage is null) { itemIndexMap = null; widgetIdMap = null; return; }
        itemIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        widgetIdMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < currentPage.Widgets.Count; i++)
        {
            var itemName = currentPage.Widgets[i].ItemName;
            if (!string.IsNullOrWhiteSpace(itemName))
                itemIndexMap[itemName] = i;

            var widgetId = currentPage.Widgets[i].WidgetId;
            if (!string.IsNullOrEmpty(widgetId))
                widgetIdMap[widgetId] = i;
        }
    }

    public async Task<IReadOnlyList<SitemapInfo>> LoadSitemapListAsync(CancellationToken cancellationToken = default)
    {
        var settings = settingsController.Current;
        var primary = SelectPrimaryTransport(settings);
        var client = clientFactory(primary.Kind, primary.BaseUri);
        return await client.GetSitemapsAsync(cancellationToken);
    }

    private static TransportSelection SelectPrimaryTransport(AppSettings settings)
    {
        return settings.EndpointMode switch
        {
            EndpointMode.LocalOnly => new TransportSelection(TransportKind.Local, settings.LocalEndpoint),
            EndpointMode.CloudOnly => new TransportSelection(TransportKind.Cloud, settings.CloudEndpoint),
            EndpointMode.Automatic => new TransportSelection(TransportKind.Local, settings.LocalEndpoint),
            _ => throw new InvalidOperationException($"Unsupported endpoint mode '{settings.EndpointMode}'.")
        };
    }

    private IReadOnlyList<string> BuildBreadcrumbTrail()
    {
        return BuildBreadcrumbPages()
            .Select(page => page.Label)
            .ToArray();
    }

    private List<NormalizedSitemapPage> BuildBreadcrumbPages()
    {
        var trail = backStack.Reverse().ToList();
        if (currentPage is not null)
        {
            trail.Add(currentPage);
        }

        return trail;
    }
}
