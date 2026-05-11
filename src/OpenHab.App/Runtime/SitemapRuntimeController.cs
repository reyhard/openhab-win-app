using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Events;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Parsing;
using OpenHab.Sitemaps.Runtime;
using System.Diagnostics;
using System.Threading;

namespace OpenHab.App.Runtime;

public sealed class SitemapRuntimeController
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRenderController renderController;
    private readonly Func<TransportKind, Uri, IOpenHabClient> clientFactory;
    private readonly IOpenHabEventStreamClient? sitemapEventStreamClient;
    private NormalizedSitemapPage? currentPage;
    private readonly Stack<NormalizedSitemapPage> backStack = new();
    private Dictionary<string, int>? itemIndexMap;
    private Dictionary<string, List<int>>? itemIndicesMap;
    private Dictionary<string, int>? widgetIdMap;
    private string? _subscriptionId;
    private int _widgetRefreshQueued;
    private int _widgetRefreshRunning;
    private bool _sitemapEventHandlersAttached;
    private DateTimeOffset _lastReconcileRefreshUtc = DateTimeOffset.MinValue;
    private long _refreshSequence;
    private long _reconcileSequence;
    private long _sitemapStateVersion;
    private int _refreshInProgress;
    private bool _sitemapEventStreamStarted;
    private string? _sitemapEventStreamSitemapName;
    private string? _sitemapEventStreamPageId;
    private static readonly TimeSpan WidgetEventReconcileDebounce = TimeSpan.FromMilliseconds(250);
    public bool CanGoBack => backStack.Count > 0;

    public SitemapRuntimeController(
        AppSettingsController settingsController,
        SitemapRenderController renderController,
        Func<TransportKind, Uri, IOpenHabClient> clientFactory,
        IOpenHabEventStreamClient? sitemapEventStreamClient = null)
    {
        ArgumentNullException.ThrowIfNull(settingsController);
        ArgumentNullException.ThrowIfNull(renderController);
        ArgumentNullException.ThrowIfNull(clientFactory);

        this.settingsController = settingsController;
        this.renderController = renderController;
        this.clientFactory = clientFactory;
        this.sitemapEventStreamClient = sitemapEventStreamClient;
    }

    public SitemapRuntimeSnapshot Current { get; private set; } = SitemapRuntimeSnapshot.Initial;

    /// <summary>Fired when the snapshot changes, including SSE-driven delta updates.</summary>
    public event EventHandler? SnapshotChanged;

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsyncInternal("load", cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsyncInternal("manual", cancellationToken);
    }

    private async Task RefreshAsyncInternal(string reason, CancellationToken cancellationToken = default)
    {
        var refreshId = Interlocked.Increment(ref _refreshSequence);
        var inProgress = Interlocked.Increment(ref _refreshInProgress);
        var sw = Stopwatch.StartNew();
        var previousPageId = currentPage?.Id;
        DiagnosticLogger.Info(
            $"Refresh#{refreshId} start reason={reason} inProgress={inProgress} " +
            $"activeTransport={Current.ActiveTransport?.ToString() ?? "<null>"} currentPage={previousPageId ?? "<null>"}");

        Current = Current with { IsBusy = true, StatusText = "Loading...", ChangedRowIndices = [] };
        var settings = settingsController.Current;

        if (string.IsNullOrWhiteSpace(settings.SitemapName))
        {
            DiagnosticLogger.Warn($"Refresh#{refreshId} skipped — no sitemap selected");
            SetBannerStatus("No sitemap selected");
            var remaining = Interlocked.Decrement(ref _refreshInProgress);
            DiagnosticLogger.Info(
                $"Refresh#{refreshId} done (no sitemap) reason={reason} remainingInProgress={remaining}");
            return;
        }

        var primary = SelectPrimaryTransport(settings);

        try
        {
            var descriptor = await LoadDescriptorAsync(primary, settings.SitemapName, cancellationToken, previousPageId);
            var changedRowIndices = ComputeChangedRowIndices(Current.Descriptor, descriptor);
            DiagnosticLogger.Info(
                $"Refresh#{refreshId} primary success transport={primary.Kind} page={descriptor.PageId} rows={descriptor.Rows.Count}");
            Current = Current with
            {
                Descriptor = descriptor,
                ActiveTransport = primary.Kind,
                ConnectionState = ConnectionState.Online,
                Breadcrumbs = BuildBreadcrumbTrail(),
                StatusText = $"Connected via {primary.Kind.ToString().ToLowerInvariant()}.",
                IsBusy = false,
                HasError = false,
                ChangedRowIndices = changedRowIndices
            };

            // Live updates via sitemap events only.
            _ = StartSitemapEventStreamAsync(primary.BaseUri, settings.SitemapName, descriptor.PageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.Warn($"Refresh#{refreshId} canceled reason={reason}");
            throw;
        }
        catch (Exception firstError) when (settings.EndpointMode == EndpointMode.Automatic && primary.Kind == TransportKind.Local)
        {
            DiagnosticLogger.Warn($"Refresh#{refreshId} local load failed, trying cloud fallback: {firstError.Message}");
            var fallback = new TransportSelection(TransportKind.Cloud, settings.CloudEndpoint);
            try
            {
                var descriptor = await LoadDescriptorAsync(fallback, settings.SitemapName, cancellationToken, previousPageId);
                var changedRowIndices = ComputeChangedRowIndices(Current.Descriptor, descriptor);
                DiagnosticLogger.Info(
                    $"Refresh#{refreshId} fallback success transport={fallback.Kind} page={descriptor.PageId} rows={descriptor.Rows.Count}");
                Current = Current with
                {
                    Descriptor = descriptor,
                    ActiveTransport = fallback.Kind,
                    ConnectionState = ConnectionState.Online,
                    Breadcrumbs = BuildBreadcrumbTrail(),
                    StatusText = "Connected via cloud (local failed).",
                    IsBusy = false,
                    HasError = false,
                    ChangedRowIndices = changedRowIndices
                };

                _ = StartSitemapEventStreamAsync(fallback.BaseUri, settings.SitemapName, descriptor.PageId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DiagnosticLogger.Warn($"Refresh#{refreshId} fallback canceled reason={reason}");
                throw;
            }
            catch (Exception fallbackError)
            {
                DiagnosticLogger.Warn($"Refresh#{refreshId} fallback failed: {fallbackError.Message}");
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
            DiagnosticLogger.Warn($"Refresh#{refreshId} failed: {error.Message}");
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
            sw.Stop();
            var remaining = Interlocked.Decrement(ref _refreshInProgress);
            DiagnosticLogger.Info(
                $"Refresh#{refreshId} done reason={reason} durationMs={sw.ElapsedMilliseconds} " +
                $"remainingInProgress={remaining} status='{Current.StatusText}' page={Current.Descriptor?.PageId ?? "<null>"}");
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
        await ReconcileCurrentPageAsync(ct);
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
        if (sitemapEventStreamClient is null) return;
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
        await ReconcileCurrentPageAsync(cancellationToken);
        return true;
    }

    private async Task<OpenHab.Rendering.Descriptors.SitemapRenderDescriptor> LoadDescriptorAsync(
        TransportSelection transport,
        string sitemapName,
        CancellationToken cancellationToken,
        string? resumePageId = null)
    {
        var client = clientFactory(transport.Kind, transport.BaseUri);
        var json = await client.GetSitemapJsonAsync(sitemapName, cancellationToken);
        var homepage = OpenHabSitemapJsonParser.ParseHomepage(json);

        if (!string.IsNullOrEmpty(resumePageId) &&
            !string.Equals(homepage.Id, resumePageId, StringComparison.Ordinal))
        {
            var chain = FindPageChain(homepage, resumePageId);
            if (chain is { Count: > 1 })
            {
                // chain[0] = homepage, ..., chain[^1] = resume target
                // Rebuild backStack: clear old entries, then push ancestors
                // in order so reverse yields the trail.
                backStack.Clear();
                for (var i = 0; i < chain.Count - 1; i++)
                {
                    backStack.Push(SitemapNormalizer.Normalize(chain[i]));
                }

                currentPage = SitemapNormalizer.Normalize(chain[^1]);
                BuildItemIndexMap();
                DiagnosticLogger.Info(
                    $"LoadDescriptorAsync resumed to page '{resumePageId}' from chain depth {chain.Count}");
                return renderController.BuildCurrentDescriptor(currentPage);
            }

            DiagnosticLogger.Warn(
                $"LoadDescriptorAsync could not resume page '{resumePageId}', falling back to homepage");
        }

        backStack.Clear();
        currentPage = SitemapNormalizer.Normalize(homepage);
        BuildItemIndexMap();
        return renderController.BuildCurrentDescriptor(currentPage);
    }

    public async Task StartSitemapEventStreamAsync(Uri localBaseUri, string sitemapName, string pageId, CancellationToken ct = default)
    {
        if (sitemapEventStreamClient is null) return;

        // If already connected to the same sitemap/page, skip
        if (_sitemapEventStreamStarted && _sitemapEventStreamSitemapName == sitemapName && _sitemapEventStreamPageId == pageId)
            return;

        _sitemapEventStreamStarted = true;
        _sitemapEventStreamSitemapName = sitemapName;
        _sitemapEventStreamPageId = pageId;

        DiagnosticLogger.Info($"Starting sitemap event stream to {localBaseUri} for sitemap '{sitemapName}' page '{pageId}'");

        EnsureSitemapEventHandlersAttached();

        _subscriptionId = await sitemapEventStreamClient.SubscribeToSitemapEventsAsync(localBaseUri, ct);
        if (_subscriptionId is null)
        {
            DiagnosticLogger.Warn("Failed to subscribe to sitemap events — no subscription ID returned");
            _sitemapEventStreamStarted = false;
            return;
        }

        DiagnosticLogger.Info($"Sitemap event subscription created: {_subscriptionId}");
        var sseUrl = new Uri(localBaseUri, $"rest/sitemaps/events/{_subscriptionId}?sitemap={Uri.EscapeDataString(sitemapName)}&pageid={Uri.EscapeDataString(pageId)}");
        _ = sitemapEventStreamClient.ConnectAsync(sseUrl, ct);
    }

    public async Task ReconnectSitemapEventStreamAsync(Uri localBaseUri, string sitemapName, string pageId, CancellationToken ct = default)
    {
        if (sitemapEventStreamClient is null || _subscriptionId is null) return;

        DiagnosticLogger.Info($"Reconnecting sitemap event stream for page '{pageId}'");
        var sseUrl = new Uri(localBaseUri, $"rest/sitemaps/events/{_subscriptionId}?sitemap={Uri.EscapeDataString(sitemapName)}&pageid={Uri.EscapeDataString(pageId)}");
        _ = sitemapEventStreamClient.ConnectAsync(sseUrl, ct);
    }

    private void OnConnectionStateChanged(object? sender, string state)
    {
        var source = ReferenceEquals(sender, sitemapEventStreamClient) ? "sitemap" : "unknown";
        DiagnosticLogger.Info($"SSE connection state changed source={source} state={state} threadId={Environment.CurrentManagedThreadId}");
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

    private void OnWidgetEventReceived(object? sender, SitemapWidgetEvent e)
    {
        DiagnosticLogger.Info($"SSE widget event: id={e.WidgetId} item={e.ItemName} state={e.ItemState} vis={e.Visibility}");
        Interlocked.Increment(ref _sitemapStateVersion);
        ApplyWidgetEvent(e);

        // Always reconcile in background (coalesced): visibility/icon rules often affect
        // sibling widgets and are not fully represented in single-row widget events.
        QueueRefreshFromWidgetEvent();
    }

    private void ApplyWidgetEvent(SitemapWidgetEvent e)
    {
        if (widgetIdMap is null || currentPage is null) return;

        var targetIndices = ResolveTargetWidgetIndices(e);
        if (targetIndices.Count == 0)
        {
            DiagnosticLogger.Warn($"SSE widget event: widget not found by id='{e.WidgetId}' or item='{e.ItemName}'");
            return;
        }

        var widgets = currentPage.Widgets.ToList();
        var changedIndices = new List<int>();

        foreach (var index in targetIndices)
        {
            if (index < 0 || index >= widgets.Count)
            {
                continue;
            }

            var widget = widgets[index];
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

            // Apply icon updates from SSE event payload.
            if (!string.IsNullOrEmpty(e.Icon) && !string.Equals(widget.Icon, e.Icon, StringComparison.Ordinal))
            {
                widget = widget with { Icon = e.Icon };
                changed = true;
            }

            // Apply visibility change
            if (widget.IsVisible != e.Visibility)
            {
                widget = widget with { IsVisible = e.Visibility };
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            widgets[index] = widget;
            changedIndices.Add(index);
        }

        if (changedIndices.Count == 0) return;

        currentPage = currentPage with { Widgets = widgets.AsReadOnly() };

        Current = Current with
        {
            Descriptor = renderController.BuildCurrentDescriptor(currentPage),
            ChangedRowIndices = changedIndices
        };

        DiagnosticLogger.Info(
            $"ApplyWidgetEvent applied id={e.WidgetId} indices=[{string.Join(",", changedIndices)}] item={e.ItemName ?? "<null>"} " +
            $"state={e.ItemState ?? "<null>"} vis={e.Visibility} descChanged={e.DescriptionChanged} " +
            $"threadId={Environment.CurrentManagedThreadId}");
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<int> ResolveTargetWidgetIndices(SitemapWidgetEvent e)
    {
        // Prefer direct widget id mapping when present and found.
        if (!string.IsNullOrEmpty(e.WidgetId) && widgetIdMap is not null && widgetIdMap.TryGetValue(e.WidgetId, out var widIndex))
        {
            return [widIndex];
        }

        // For duplicate ON/OFF rows (same item, different visibility rules), update all matches.
        if (!string.IsNullOrEmpty(e.ItemName) && itemIndicesMap is not null &&
            itemIndicesMap.TryGetValue(e.ItemName, out var indices) && indices.Count > 0)
        {
            return indices;
        }

        // Backward-compatible fallback to single-item map.
        if (!string.IsNullOrEmpty(e.ItemName) && itemIndexMap is not null &&
            itemIndexMap.TryGetValue(e.ItemName, out var itemIndex))
        {
            return [itemIndex];
        }

        return [];
    }

    private void BuildItemIndexMap()
    {
        if (currentPage is null) { itemIndexMap = null; itemIndicesMap = null; widgetIdMap = null; return; }
        itemIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        itemIndicesMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        widgetIdMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < currentPage.Widgets.Count; i++)
        {
            var itemName = currentPage.Widgets[i].ItemName;
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                itemIndexMap[itemName] = i;
                if (!itemIndicesMap.TryGetValue(itemName, out var bucket))
                {
                    bucket = new List<int>();
                    itemIndicesMap[itemName] = bucket;
                }

                bucket.Add(i);
            }

            var widgetId = currentPage.Widgets[i].WidgetId;
            if (!string.IsNullOrEmpty(widgetId))
                widgetIdMap[widgetId] = i;
        }
    }

    private void EnsureSitemapEventHandlersAttached()
    {
        if (sitemapEventStreamClient is null || _sitemapEventHandlersAttached) return;
        sitemapEventStreamClient.ConnectionStateChanged += OnConnectionStateChanged;
        sitemapEventStreamClient.WidgetEventReceived += OnWidgetEventReceived;
        _sitemapEventHandlersAttached = true;
    }

    private void QueueRefreshFromWidgetEvent()
    {
        if (Interlocked.Exchange(ref _widgetRefreshQueued, 1) == 1)
        {
            DiagnosticLogger.Info("QueueRefreshFromWidgetEvent skipped (already queued)");
            return;
        }
        DiagnosticLogger.Info("QueueRefreshFromWidgetEvent queued");

        _ = Task.Run(async () =>
        {
            if (Interlocked.CompareExchange(ref _widgetRefreshRunning, 1, 0) != 0)
            {
                DiagnosticLogger.Info("QueueRefreshFromWidgetEvent runner already active");
                return;
            }

            try
            {
                while (Interlocked.Exchange(ref _widgetRefreshQueued, 0) == 1)
                {
                    try
                    {
                        var now = DateTimeOffset.UtcNow;
                        var sinceLast = now - _lastReconcileRefreshUtc;
                        if (sinceLast < WidgetEventReconcileDebounce)
                        {
                            await Task.Delay(WidgetEventReconcileDebounce - sinceLast).ConfigureAwait(false);
                        }

                        DiagnosticLogger.Info("QueueRefreshFromWidgetEvent executing reconcile refresh");
                        await ReconcileCurrentPageAsync().ConfigureAwait(false);
                        _lastReconcileRefreshUtc = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLogger.Warn($"Widget-event refresh failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _widgetRefreshRunning, 0);
                if (Interlocked.Exchange(ref _widgetRefreshQueued, 0) == 1)
                {
                    QueueRefreshFromWidgetEvent();
                }
                else
                {
                    DiagnosticLogger.Info("QueueRefreshFromWidgetEvent runner idle");
                }
            }
        });
    }

    private async Task ReconcileCurrentPageAsync(CancellationToken cancellationToken = default)
    {
        var reconcileId = Interlocked.Increment(ref _reconcileSequence);
        var stateVersionAtStart = Volatile.Read(ref _sitemapStateVersion);
        var activeTransport = Current.ActiveTransport;
        if (activeTransport is null)
        {
            DiagnosticLogger.Info("ReconcileCurrentPageAsync skipped (no active transport)");
            return;
        }

        var transport = activeTransport.Value;
        var settings = settingsController.Current;
        var endpoint = transport == TransportKind.Local ? settings.LocalEndpoint : settings.CloudEndpoint;
        var currentPageId = currentPage?.Id;

        DiagnosticLogger.Info(
            $"ReconcileCurrentPageAsync start transport={transport} sitemap={settings.SitemapName} page={currentPageId ?? "<null>"}");

        var client = clientFactory(transport, endpoint);
        var json = await client.GetSitemapJsonAsync(settings.SitemapName, cancellationToken).ConfigureAwait(false);
        var homepage = OpenHabSitemapJsonParser.ParseHomepage(json);

        if (reconcileId != Volatile.Read(ref _reconcileSequence) ||
            stateVersionAtStart != Volatile.Read(ref _sitemapStateVersion))
        {
            DiagnosticLogger.Info(
                $"ReconcileCurrentPageAsync skipped stale result reconcileId={reconcileId} " +
                $"latestReconcileId={Volatile.Read(ref _reconcileSequence)} " +
                $"stateVersionAtStart={stateVersionAtStart} latestStateVersion={Volatile.Read(ref _sitemapStateVersion)}");
            return;
        }

        var targetPage = homepage;
        var shouldReconnectToResolvedPage = false;
        if (!string.IsNullOrEmpty(currentPageId))
        {
            var foundPage = FindPageById(homepage, currentPageId);
            if (foundPage is not null)
            {
                targetPage = foundPage;
            }
            else
            {
                DiagnosticLogger.Warn(
                    $"ReconcileCurrentPageAsync page '{currentPageId}' not found in sitemap '{settings.SitemapName}', falling back to homepage");
                backStack.Clear();
                shouldReconnectToResolvedPage = true;
            }
        }

        var normalized = SitemapNormalizer.Normalize(targetPage);
        var descriptor = renderController.BuildCurrentDescriptor(normalized);
        var changedRowIndices = ComputeChangedRowIndices(Current.Descriptor, descriptor);
        var previousPageId = currentPage?.Id;

        currentPage = normalized;
        BuildItemIndexMap();

        var breadcrumbs = BuildBreadcrumbTrail();
        var hasPageChange = !string.Equals(previousPageId, normalized.Id, StringComparison.Ordinal);
        var hasBreadcrumbChange = !Current.Breadcrumbs.SequenceEqual(breadcrumbs, StringComparer.Ordinal);
        var hasDescriptorChange = changedRowIndices.Count > 0 || Current.Descriptor is null || Current.Descriptor.Rows.Count != descriptor.Rows.Count;

        if (hasDescriptorChange || hasPageChange || hasBreadcrumbChange)
        {
            Current = Current with
            {
                Descriptor = descriptor,
                Breadcrumbs = breadcrumbs,
                ChangedRowIndices = changedRowIndices
            };
        }

        if (shouldReconnectToResolvedPage || !string.Equals(currentPageId, normalized.Id, StringComparison.Ordinal))
        {
            ReconnectForPage(normalized.Id);
        }

        DiagnosticLogger.Info(
            $"ReconcileCurrentPageAsync done page={normalized.Id} rows={normalized.Widgets.Count} " +
            $"threadId={Environment.CurrentManagedThreadId}");
        if (hasDescriptorChange || hasPageChange || hasBreadcrumbChange)
        {
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static SitemapPage? FindPageById(SitemapPage page, string pageId)
    {
        if (string.Equals(page.Id, pageId, StringComparison.Ordinal))
        {
            return page;
        }

        foreach (var widget in page.Widgets)
        {
            foreach (var child in widget.Children)
            {
                var found = FindPageById(child, pageId);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the chain of pages from <paramref name="root"/> to the page with
    /// <paramref name="targetId"/>, inclusive. Returns <c>null</c> if not found.
    /// </summary>
    private static List<SitemapPage>? FindPageChain(SitemapPage root, string targetId)
    {
        var path = new List<SitemapPage>();
        return FindPageChainWalk(root, targetId, path) ? path : null;
    }

    private static bool FindPageChainWalk(SitemapPage current, string targetId, List<SitemapPage> path)
    {
        path.Add(current);
        if (string.Equals(current.Id, targetId, StringComparison.Ordinal))
            return true;

        foreach (var widget in current.Widgets)
        {
            foreach (var child in widget.Children)
            {
                if (FindPageChainWalk(child, targetId, path))
                    return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
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

    private static IReadOnlyList<int> ComputeChangedRowIndices(
        SitemapRenderDescriptor? oldDescriptor,
        SitemapRenderDescriptor newDescriptor)
    {
        if (oldDescriptor is null)
        {
            return [];
        }

        if (!string.Equals(oldDescriptor.PageId, newDescriptor.PageId, StringComparison.Ordinal) ||
            oldDescriptor.Rows.Count != newDescriptor.Rows.Count)
        {
            // Treat structural changes as full refresh in UI.
            return [];
        }

        var changed = new List<int>();
        for (var i = 0; i < newDescriptor.Rows.Count; i++)
        {
            if (!AreRowsEquivalent(oldDescriptor.Rows[i], newDescriptor.Rows[i]))
            {
                changed.Add(i);
            }
        }

        return changed;
    }

    private static bool AreRowsEquivalent(SitemapRowDescriptor a, SitemapRowDescriptor b)
    {
        if (a.Label != b.Label ||
            a.State != b.State ||
            a.RawState != b.RawState ||
            a.RawItemState != b.RawItemState ||
            a.IsVisible != b.IsVisible ||
            a.Control != b.Control ||
            a.Action != b.Action ||
            a.IconName != b.IconName ||
            a.LabelColor != b.LabelColor ||
            a.ValueColor != b.ValueColor ||
            a.IconColor != b.IconColor ||
            a.IsSectionHeader != b.IsSectionHeader ||
            a.Command != b.Command ||
            a.ReleaseCommand != b.ReleaseCommand ||
            a.Stateless != b.Stateless ||
            a.Url != b.Url ||
            a.Period != b.Period ||
            a.ItemName != b.ItemName ||
            a.WidgetId != b.WidgetId ||
            a.InputHint != b.InputHint ||
            a.GridRow != b.GridRow ||
            a.GridColumn != b.GridColumn)
        {
            return false;
        }

        if (a.SelectionOptions.Count != b.SelectionOptions.Count)
        {
            return false;
        }

        for (var i = 0; i < a.SelectionOptions.Count; i++)
        {
            var oa = a.SelectionOptions[i];
            var ob = b.SelectionOptions[i];
            if (oa.Command != ob.Command ||
                oa.Label != ob.Label ||
                oa.Row != ob.Row ||
                oa.Column != ob.Column ||
                oa.IsActive != ob.IsActive ||
                oa.ClickCommand != ob.ClickCommand ||
                oa.ReleaseCommand != ob.ReleaseCommand ||
                oa.Stateless != ob.Stateless ||
                oa.SourceRowIndex != ob.SourceRowIndex)
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<string> BuildBreadcrumbTrail()
    {
        return BuildBreadcrumbPages()
            .Select(page => page.Label)
            .ToArray();
    }

    /// <summary>
    /// Sets a banner/selector state without attempting to load a sitemap.
    /// Used when no sitemap is selected or available.
    /// </summary>
    public void SetBannerStatus(string statusText)
    {
        DiagnosticLogger.Info($"SetBannerStatus: {statusText}");
        Current = Current with
        {
            Descriptor = null,
            StatusText = statusText,
            IsBusy = false,
            HasError = false,
            ConnectionState = ConnectionState.Unknown,
            Breadcrumbs = [],
            ChangedRowIndices = []
        };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
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
