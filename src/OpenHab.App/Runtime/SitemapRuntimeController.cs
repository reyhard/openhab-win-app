using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.App.Localization;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Events;
using OpenHab.Core.Profiles;
using OpenHab.Rendering.Descriptors;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Parsing;
using OpenHab.Sitemaps.Runtime;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace OpenHab.App.Runtime;

public sealed class SitemapRuntimeController
{
    private const string NullDiagnosticText = "<null>";

    private readonly AppSettingsController settingsController;
    private readonly SitemapRenderController renderController;
    private readonly Func<TransportKind, Uri, IOpenHabClient> clientFactory;
    private readonly IOpenHabEventStreamClient? sitemapEventStreamClient;
    private readonly ITextLocalizer text;
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
    private long _sitemapEventStreamAttempt;
    private string _activeSearchQuery = string.Empty;
    private string _searchInputQuery = string.Empty;
    private long _searchSequence;
    private IReadOnlyDictionary<string, SitemapSearchSource> _activeSearchSources = new Dictionary<string, SitemapSearchSource>(StringComparer.Ordinal);
    private static readonly TimeSpan WidgetEventReconcileDebounce = TimeSpan.FromMilliseconds(250);
    public bool CanGoBack => backStack.Count > 0;
    private bool HasActiveSearch => _activeSearchQuery.Length > 0;
    private string SearchSnapshotQuery => HasActiveSearch ? _searchInputQuery : string.Empty;
    private int SearchSnapshotResultCount => HasActiveSearch ? _activeSearchSources.Count : 0;

    private sealed record ResolvedSearchWidget(
        NormalizedSitemapPage Page,
        NormalizedSitemapWidget Widget,
        int WidgetIndex,
        IReadOnlyList<NormalizedSitemapPage> SourcePageChain);

    public SitemapRuntimeController(
        AppSettingsController settingsController,
        SitemapRenderController renderController,
        Func<TransportKind, Uri, IOpenHabClient> clientFactory,
        IOpenHabEventStreamClient? sitemapEventStreamClient = null,
        ITextLocalizer? text = null)
    {
        ArgumentNullException.ThrowIfNull(settingsController);
        ArgumentNullException.ThrowIfNull(renderController);
        ArgumentNullException.ThrowIfNull(clientFactory);

        this.settingsController = settingsController;
        this.renderController = renderController;
        this.clientFactory = clientFactory;
        this.sitemapEventStreamClient = sitemapEventStreamClient;
        this.text = text ?? DefaultEnglishTextLocalizer.Instance;
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

    public void ApplySearchQuery(string? query)
    {
        Interlocked.Increment(ref _searchSequence);
        var inputQuery = query ?? string.Empty;
        var descriptor = BuildDescriptorForQuery(query, out var normalizedQuery, out var resultCount, out var sources);
        _activeSearchQuery = normalizedQuery;
        _searchInputQuery = normalizedQuery.Length > 0 ? inputQuery : string.Empty;
        _activeSearchSources = sources;
        Current = Current with
        {
            Descriptor = descriptor,
            IsSearchActive = HasActiveSearch,
            SearchQuery = SearchSnapshotQuery,
            SearchResultCount = resultCount,
            ChangedRowIndices = []
        };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ApplySearchQueryAsync(string? query, CancellationToken cancellationToken = default)
    {
        var inputQuery = query ?? string.Empty;
        var normalizedQuery = inputQuery.Trim();
        if (normalizedQuery.Length == 0 || currentPage is null)
        {
            ApplySearchQuery(query);
            return Task.CompletedTask;
        }

        var searchSequence = Interlocked.Increment(ref _searchSequence);
        var pageSnapshot = currentPage;
        var stateVersionAtStart = Volatile.Read(ref _sitemapStateVersion);
        var refreshSequenceAtStart = Volatile.Read(ref _refreshSequence);

        _activeSearchQuery = normalizedQuery;
        _searchInputQuery = inputQuery;
        Current = Current with
        {
            IsSearchActive = true,
            SearchQuery = inputQuery,
            ChangedRowIndices = []
        };

        return ApplySearchQueryAsyncCore(
            inputQuery,
            normalizedQuery,
            pageSnapshot,
            searchSequence,
            stateVersionAtStart,
            refreshSequenceAtStart,
            cancellationToken);
    }

    private async Task ApplySearchQueryAsyncCore(
        string inputQuery,
        string normalizedQuery,
        NormalizedSitemapPage pageSnapshot,
        long searchSequence,
        long stateVersionAtStart,
        long refreshSequenceAtStart,
        CancellationToken cancellationToken)
    {
        SitemapSearchBuildResult search;
        try
        {
            search = await Task.Run(
                () => BuildSearchResultForPage(pageSnapshot, normalizedQuery),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested ||
            searchSequence != Volatile.Read(ref _searchSequence) ||
            !ReferenceEquals(pageSnapshot, currentPage) ||
            stateVersionAtStart != Volatile.Read(ref _sitemapStateVersion) ||
            refreshSequenceAtStart != Volatile.Read(ref _refreshSequence))
        {
            return;
        }

        _activeSearchQuery = search.Query;
        _searchInputQuery = search.Query.Length > 0 ? inputQuery : string.Empty;
        _activeSearchSources = new Dictionary<string, SitemapSearchSource>(search.SourcesByResultKey, StringComparer.Ordinal);
        Current = Current with
        {
            Descriptor = search.Descriptor,
            IsSearchActive = HasActiveSearch,
            SearchQuery = SearchSnapshotQuery,
            SearchResultCount = search.ResultCount,
            ChangedRowIndices = []
        };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSearch()
    {
        ApplySearchQuery(null);
    }

    private async Task RefreshAsyncInternal(string reason, CancellationToken cancellationToken = default)
    {
        var refreshId = Interlocked.Increment(ref _refreshSequence);
        var inProgress = Interlocked.Increment(ref _refreshInProgress);
        var sw = Stopwatch.StartNew();
        var previousPageId = currentPage?.Id;
        DiagnosticLogger.Info(
            $"Refresh#{refreshId} start reason={reason} inProgress={inProgress} " +
            $"activeTransport={Current.ActiveTransport?.ToString() ?? NullDiagnosticText} currentPage={previousPageId ?? NullDiagnosticText}");

        Current = Current with { IsBusy = true, StatusText = text.Get("Runtime.Status.Loading"), ChangedRowIndices = [] };
        var settings = settingsController.Current;

        if (string.IsNullOrWhiteSpace(settings.SitemapName))
        {
            DiagnosticLogger.Warn($"Refresh#{refreshId} skipped — no sitemap selected");
            SetBannerStatus(text.Get("Runtime.Sitemap.NoSitemapSelected"));
            var remaining = Interlocked.Decrement(ref _refreshInProgress);
            DiagnosticLogger.Info(
                $"Refresh#{refreshId} done (no sitemap) reason={reason} remainingInProgress={remaining}");
            return;
        }

        var primary = SelectPrimaryTransport(settings);

        try
        {
            var descriptor = await LoadDescriptorAsync(primary, settings.SitemapName, cancellationToken, previousPageId);
            DiagnosticLogger.Info(
                $"Refresh#{refreshId} primary success transport={primary.Kind} page={descriptor.PageId} rows={descriptor.Rows.Count}");
            var effectiveDescriptor = BuildEffectiveDescriptor(descriptor);
            var changedRowIndices = ComputeChangedRowIndices(Current.Descriptor, effectiveDescriptor);
            Current = Current with
            {
                Descriptor = effectiveDescriptor,
                ActiveTransport = primary.Kind,
                ConnectionState = ConnectionState.Online,
                Breadcrumbs = BuildBreadcrumbTrail(),
                StatusText = primary.Kind == TransportKind.Cloud
                    ? text.Get("Runtime.Connection.ConnectedViaCloudSimple")
                    : text.Get("Runtime.Connection.ConnectedViaLocalSimple"),
                IsBusy = false,
                HasError = false,
                ChangedRowIndices = changedRowIndices,
                IsSearchActive = HasActiveSearch,
                SearchQuery = SearchSnapshotQuery,
                SearchResultCount = SearchSnapshotResultCount
            };

            // Live updates via sitemap events only.
            StartSitemapEventStreamInBackground(primary.BaseUri, settings.SitemapName, descriptor.PageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.Warn($"Refresh#{refreshId} canceled reason={reason}");
            throw;
        }
        catch (Exception firstError) when (settings.EndpointMode == EndpointMode.Automatic && primary.Kind == TransportKind.Local)
        {
            DiagnosticLogger.Warn(
                $"Refresh#{refreshId} local load failed, trying cloud fallback: {SafeDiagnosticText.ForLog(firstError)}");
            var fallback = new TransportSelection(TransportKind.Cloud, settings.CloudEndpoint);
            try
            {
                var descriptor = await LoadDescriptorAsync(fallback, settings.SitemapName, cancellationToken, previousPageId);
                DiagnosticLogger.Info(
                    $"Refresh#{refreshId} fallback success transport={fallback.Kind} page={descriptor.PageId} rows={descriptor.Rows.Count}");
                var effectiveDescriptor = BuildEffectiveDescriptor(descriptor);
                var changedRowIndices = ComputeChangedRowIndices(Current.Descriptor, effectiveDescriptor);
                Current = Current with
                {
                    Descriptor = effectiveDescriptor,
                    ActiveTransport = fallback.Kind,
                    ConnectionState = ConnectionState.Online,
                    Breadcrumbs = BuildBreadcrumbTrail(),
                    StatusText = text.Get("Runtime.Connection.ConnectedViaCloudLocalFailed"),
                    IsBusy = false,
                    HasError = false,
                    ChangedRowIndices = changedRowIndices,
                    IsSearchActive = HasActiveSearch,
                    SearchQuery = SearchSnapshotQuery,
                    SearchResultCount = SearchSnapshotResultCount
                };

                StartSitemapEventStreamInBackground(fallback.BaseUri, settings.SitemapName, descriptor.PageId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                DiagnosticLogger.Warn($"Refresh#{refreshId} fallback canceled reason={reason}");
                throw;
            }
            catch (Exception fallbackError)
            {
                DiagnosticLogger.Warn($"Refresh#{refreshId} fallback failed: {SafeDiagnosticText.ForLog(fallbackError)}");
                Current = Current with
                {
                    ConnectionState = ConnectionState.Offline,
                    StatusText =
                        $"{SafeDiagnosticText.ForUserStatus(firstError, text.Get("Runtime.Connection.Failed"))} {SafeDiagnosticText.ForUserStatus(fallbackError, text.Get("Runtime.Connection.FallbackFailed"))}",
                    IsBusy = false,
                    HasError = true,
                    ChangedRowIndices = []
                };
            }
        }
        catch (Exception error)
        {
            DiagnosticLogger.Warn($"Refresh#{refreshId} failed: {SafeDiagnosticText.ForLog(error)}");
            Current = Current with
            {
                ConnectionState = ConnectionState.Offline,
                StatusText = SafeDiagnosticText.ForUserStatus(error, text.Get("Runtime.Connection.Failed")),
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
                $"remainingInProgress={remaining} status='{Current.StatusText}' page={Current.Descriptor?.PageId ?? NullDiagnosticText}");
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

    public Task<bool> ActivateRowByKeyAsync(string rowKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowKey);

        if (Current.IsSearchActive)
        {
            if (_activeSearchSources.TryGetValue(rowKey, out var source))
            {
                return ActivateSearchSourceAsync(source, cancellationToken);
            }

            RebuildActiveSearchSnapshot();
            return Task.FromResult(false);
        }

        return TryResolveCurrentDescriptorRow(rowKey, out var rowIndex)
            ? ActivateRowAsync(rowIndex, cancellationToken)
            : Task.FromResult(false);
    }

    public Task<bool> SendCommandForRowKeyAsync(string rowKey, string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowKey);

        if (Current.IsSearchActive)
        {
            if (_activeSearchSources.TryGetValue(rowKey, out var source))
            {
                return SendCommandForSearchSourceAsync(source, command, cancellationToken);
            }

            RebuildActiveSearchSnapshot();
            return Task.FromResult(false);
        }

        return TryResolveCurrentDescriptorRow(rowKey, out var rowIndex)
            ? SendCommandForRowAsync(rowIndex, command, cancellationToken)
            : Task.FromResult(false);
    }

    public Task<bool> NavigateRowByKeyAsync(string rowKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowKey);

        if (Current.IsSearchActive)
        {
            if (_activeSearchSources.TryGetValue(rowKey, out var source))
            {
                return NavigateToSearchSourceAsync(source, cancellationToken);
            }

            RebuildActiveSearchSnapshot();
            return Task.FromResult(false);
        }

        return TryResolveCurrentDescriptorRow(rowKey, out var rowIndex)
            ? NavigateToChildAsync(rowIndex, cancellationToken)
            : Task.FromResult(false);
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
        ClearSearchState();
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = $"Navigated to: {normalized.Label}",
            ChangedRowIndices = [],
            IsSearchActive = false,
            SearchQuery = string.Empty,
            SearchResultCount = 0
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
        ClearSearchState();
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = currentPage.Label,
            ChangedRowIndices = [],
            IsSearchActive = false,
            SearchQuery = string.Empty,
            SearchResultCount = 0
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
        StartSitemapEventStreamInBackground(endpoint, settingsController.Current.SitemapName, pageId, CancellationToken.None);
    }

    private void StartSitemapEventStreamInBackground(Uri endpoint, string sitemapName, string pageId, CancellationToken ct)
    {
        if (sitemapEventStreamClient is null) return;
        EnsureSitemapEventHandlersAttached();
        var attempt = PrepareSitemapEventStreamStart(sitemapName, pageId);
        if (attempt is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await StartSitemapEventStreamCoreAsync(endpoint, sitemapName, pageId, attempt.Value, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded navigation or shutdown canceled this background start.
            }
        }, CancellationToken.None);
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
            if (_activeSearchQuery.Length > 0 && currentPage is not null)
            {
                var currentDescriptor = renderController.BuildCurrentDescriptor(currentPage);
                ClearSearchState();
                Current = Current with
                {
                    Descriptor = currentDescriptor,
                    Breadcrumbs = BuildBreadcrumbTrail(),
                    StatusText = currentPage.Label,
                    ChangedRowIndices = [],
                    IsSearchActive = false,
                    SearchQuery = string.Empty,
                    SearchResultCount = 0
                };
            }

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
        ClearSearchState();
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = currentPage.Label,
            ChangedRowIndices = [],
            IsSearchActive = false,
            SearchQuery = string.Empty,
            SearchResultCount = 0
        };
        ReconnectForPage(currentPage.Id);
        return true;
    }

    public async Task<bool> ActivateRowAsync(int rowIndex, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

        if (currentPage is null)
        {
            return false;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rowIndex, currentPage.Widgets.Count);

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

    private async Task<bool> ActivateSearchSourceAsync(SitemapSearchSource source, CancellationToken cancellationToken)
    {
        if (!TryResolveSearchSource(source, out var resolved))
        {
            RebuildActiveSearchSnapshot();
            return false;
        }

        var widget = resolved.Widget;
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

    private async Task<bool> SendCommandForSearchSourceAsync(
        SitemapSearchSource source,
        string command,
        CancellationToken cancellationToken)
    {
        if (!TryResolveSearchSource(source, out var resolved))
        {
            RebuildActiveSearchSnapshot();
            return false;
        }

        if (string.IsNullOrWhiteSpace(resolved.Widget.ItemName))
        {
            return false;
        }

        var activeTransport = Current.ActiveTransport ?? throw new InvalidOperationException("No active transport.");
        var endpoint = activeTransport == TransportKind.Local
            ? settingsController.Current.LocalEndpoint
            : settingsController.Current.CloudEndpoint;
        var client = clientFactory(activeTransport, endpoint);
        await client.SendCommandAsync(resolved.Widget.ItemName, command, cancellationToken);
        await ReconcileCurrentPageAsync(cancellationToken);
        return true;
    }

    private Task<bool> NavigateToSearchSourceAsync(SitemapSearchSource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveSearchSource(source, out var resolved))
        {
            RebuildActiveSearchSnapshot();
            return Task.FromResult(false);
        }

        if (resolved.Widget.Children.Count == 0)
        {
            return Task.FromResult(false);
        }

        var sourceChain = resolved.SourcePageChain;
        if (sourceChain.Count == 0)
        {
            RebuildActiveSearchSnapshot();
            return Task.FromResult(false);
        }

        var existingAncestors = backStack.Reverse().ToArray();
        backStack.Clear();
        foreach (var ancestor in existingAncestors)
        {
            backStack.Push(ancestor);
        }

        foreach (var page in sourceChain)
        {
            backStack.Push(page);
        }

        var normalized = SitemapNormalizer.Normalize(resolved.Widget.Children[0]);
        currentPage = normalized;
        BuildItemIndexMap();

        var descriptor = renderController.BuildCurrentDescriptor(normalized);
        ClearSearchState();
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = $"Navigated to: {normalized.Label}",
            ChangedRowIndices = [],
            IsSearchActive = false,
            SearchQuery = string.Empty,
            SearchResultCount = 0
        };

        ReconnectForPage(normalized.Id);
        return Task.FromResult(true);
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

        EnsureSitemapEventHandlersAttached();
        var attempt = PrepareSitemapEventStreamStart(sitemapName, pageId);
        if (attempt is null)
        {
            return;
        }

        await StartSitemapEventStreamCoreAsync(localBaseUri, sitemapName, pageId, attempt.Value, ct);
    }

    private async Task StartSitemapEventStreamCoreAsync(
        Uri localBaseUri,
        string sitemapName,
        string pageId,
        long attempt,
        CancellationToken ct)
    {
        if (sitemapEventStreamClient is null) return;
        if (!IsCurrentSitemapEventStreamAttempt(attempt))
        {
            return;
        }

        try
        {
            DiagnosticLogger.Info($"Starting sitemap event stream to {localBaseUri} for sitemap '{sitemapName}' page '{pageId}'");

            var subscriptionId = await sitemapEventStreamClient.SubscribeToSitemapEventsAsync(localBaseUri, ct);
            if (!IsCurrentSitemapEventStreamAttempt(attempt))
            {
                DiagnosticLogger.Info($"Ignoring stale sitemap event subscription for page '{pageId}'");
                return;
            }

            if (subscriptionId is null)
            {
                DiagnosticLogger.Warn("Failed to subscribe to sitemap events — no subscription ID returned");
                if (ResetSitemapEventStreamStart(attempt))
                {
                    DegradeSitemapEventStreamIfOnline();
                }

                return;
            }

            if (!IsCurrentSitemapEventStreamAttempt(attempt))
            {
                DiagnosticLogger.Info($"Ignoring stale sitemap event stream start before subscription assignment for page '{pageId}'");
                return;
            }

            _subscriptionId = subscriptionId;
            DiagnosticLogger.Info($"Sitemap event subscription created: {_subscriptionId}");
            var sseUrl = new Uri(localBaseUri, $"rest/sitemaps/events/{_subscriptionId}?sitemap={Uri.EscapeDataString(sitemapName)}&pageid={Uri.EscapeDataString(pageId)}");

            if (!IsCurrentSitemapEventStreamAttempt(attempt))
            {
                DiagnosticLogger.Info($"Ignoring stale sitemap event stream start before connect for page '{pageId}'");
                return;
            }

            await sitemapEventStreamClient.ConnectAsync(sseUrl, ct);

            if (!IsCurrentSitemapEventStreamAttempt(attempt))
            {
                DiagnosticLogger.Info($"Ignoring stale sitemap event stream connect completion for page '{pageId}'");
                return;
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsCurrentSitemapEventStreamAttempt(attempt))
            {
                DiagnosticLogger.Info($"Ignoring stale sitemap event stream cancellation for page '{pageId}'");
                return;
            }

            ResetSitemapEventStreamStart(attempt);
            throw;
        }
        catch (Exception error)
        {
            DiagnosticLogger.Warn($"Failed to start sitemap event stream: {SafeDiagnosticText.ForLog(error)}");
            if (!ResetSitemapEventStreamStart(attempt))
            {
                return;
            }

            if (Current.ConnectionState == ConnectionState.Online)
            {
                DegradeSitemapEventStreamIfOnline();
            }
            else
            {
                Current = Current with { ChangedRowIndices = [] };
            }
        }
    }

    public void StopSitemapEventStream()
    {
        if (sitemapEventStreamClient is null)
        {
            return;
        }

        Interlocked.Increment(ref _sitemapEventStreamAttempt);
        _sitemapEventStreamStarted = false;
        _sitemapEventStreamSitemapName = null;
        _sitemapEventStreamPageId = null;
        _subscriptionId = null;
        sitemapEventStreamClient.Dispose();
    }

    private bool ResetSitemapEventStreamStart(long attempt)
    {
        if (!IsCurrentSitemapEventStreamAttempt(attempt))
        {
            return false;
        }

        _sitemapEventStreamStarted = false;
        _sitemapEventStreamSitemapName = null;
        _sitemapEventStreamPageId = null;
        _subscriptionId = null;
        return true;
    }

    private long? PrepareSitemapEventStreamStart(string sitemapName, string pageId)
    {
        if (_sitemapEventStreamStarted && _sitemapEventStreamSitemapName == sitemapName && _sitemapEventStreamPageId == pageId)
        {
            return null;
        }

        _sitemapEventStreamStarted = true;
        _sitemapEventStreamSitemapName = sitemapName;
        _sitemapEventStreamPageId = pageId;
        return Interlocked.Increment(ref _sitemapEventStreamAttempt);
    }

    private bool IsCurrentSitemapEventStreamAttempt(long attempt)
    {
        return Interlocked.Read(ref _sitemapEventStreamAttempt) == attempt;
    }

    private void DegradeSitemapEventStreamIfOnline()
    {
        if (Current.ConnectionState != ConnectionState.Online)
        {
            Current = Current with { ChangedRowIndices = [] };
            return;
        }

        Current = Current with
        {
            ConnectionState = ConnectionState.Degraded,
            StatusText = text.Get("Runtime.LiveUpdates.UnavailableRefreshManually"),
            ChangedRowIndices = []
        };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
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
                "disconnected" => text.Get("Runtime.LiveUpdates.UnavailableRefreshManually"),
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

        var normalDescriptor = renderController.BuildCurrentDescriptor(currentPage);
        var effectiveDescriptor = BuildEffectiveDescriptor(normalDescriptor);
        Current = Current with
        {
            Descriptor = effectiveDescriptor,
            ChangedRowIndices = HasActiveSearch ? [] : changedIndices,
            IsSearchActive = HasActiveSearch,
            SearchQuery = SearchSnapshotQuery,
            SearchResultCount = SearchSnapshotResultCount
        };

        DiagnosticLogger.Info(
            $"ApplyWidgetEvent applied id={e.WidgetId} indices=[{string.Join(",", changedIndices)}] item={e.ItemName ?? NullDiagnosticText} " +
            $"state={e.ItemState ?? NullDiagnosticText} vis={e.Visibility} descChanged={e.DescriptionChanged} " +
            $"threadId={Environment.CurrentManagedThreadId}");
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<int> ResolveTargetWidgetIndices(SitemapWidgetEvent e)
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
                        DiagnosticLogger.Warn($"Widget-event refresh failed: {SafeDiagnosticText.ForLog(ex)}");
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
            $"ReconcileCurrentPageAsync start transport={transport} sitemap={settings.SitemapName} page={currentPageId ?? NullDiagnosticText}");

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

        var previousPageId = currentPage?.Id;
        var normalized = SitemapNormalizer.Normalize(targetPage);
        currentPage = normalized;
        BuildItemIndexMap();

        var descriptor = renderController.BuildCurrentDescriptor(normalized);
        var effectiveDescriptor = BuildEffectiveDescriptor(descriptor);
        var changedRowIndices = ComputeChangedRowIndices(Current.Descriptor, effectiveDescriptor);
        var breadcrumbs = BuildBreadcrumbTrail();
        var hasPageChange = !string.Equals(previousPageId, normalized.Id, StringComparison.Ordinal);
        var hasBreadcrumbChange = !Current.Breadcrumbs.SequenceEqual(breadcrumbs, StringComparer.Ordinal);
        var hasDescriptorChange = changedRowIndices.Count > 0 || Current.Descriptor is null || Current.Descriptor.Rows.Count != effectiveDescriptor.Rows.Count;

        if (hasDescriptorChange || hasPageChange || hasBreadcrumbChange)
        {
            Current = Current with
            {
                Descriptor = effectiveDescriptor,
                Breadcrumbs = breadcrumbs,
                ChangedRowIndices = changedRowIndices,
                IsSearchActive = HasActiveSearch,
                SearchQuery = SearchSnapshotQuery,
                SearchResultCount = SearchSnapshotResultCount
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

        foreach (var child in current.Widgets.SelectMany(static widget => widget.Children))
        {
            if (FindPageChainWalk(child, targetId, path))
                return true;
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

    private bool TryResolveCurrentDescriptorRow(string rowKey, out int rowIndex)
    {
        rowIndex = -1;
        var rows = Current.Descriptor?.Rows;
        if (rows is null)
        {
            return false;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (string.Equals(BuildRuntimeRowKey(rows[index]), rowKey, StringComparison.Ordinal))
            {
                rowIndex = index;
                return true;
            }
        }

        return false;
    }

    private static string BuildRuntimeRowKey(SitemapRowDescriptor row)
    {
        if (!string.IsNullOrWhiteSpace(row.SearchResultKey))
        {
            return row.SearchResultKey;
        }

        if (!string.IsNullOrWhiteSpace(row.WidgetId))
        {
            return $"widget:{row.WidgetId}";
        }

        if (!string.IsNullOrWhiteSpace(row.ItemName))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"item:{row.ItemName}:{row.Control}:{row.Action}:{row.Label}:{row.IconName ?? string.Empty}:{row.Command ?? string.Empty}:{row.ReleaseCommand ?? string.Empty}:{row.Period ?? string.Empty}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"row:{row.Control}:{row.Action}:{row.IconName ?? string.Empty}:{row.Label}");
    }

    private bool TryResolveSearchSource(SitemapSearchSource source, out ResolvedSearchWidget resolved)
    {
        resolved = null!;
        if (currentPage is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(source.SourceWidgetId))
        {
            var matches = new List<ResolvedSearchWidget>();
            CollectSearchSourceWidgetIdMatches(currentPage, source, [currentPage], matches);
            if (matches.Count == 1)
            {
                resolved = matches[0];
                return true;
            }

            return TryResolveSearchSourcePath(source.SourceWidgetPath, out resolved) &&
                   IsMatchingSearchSource(resolved.Page, resolved.Widget, source);
        }

        if (TryResolveSearchSourcePath(source.SourceWidgetPath, out var pathResolved) &&
            IsMatchingSearchSource(pathResolved.Page, pathResolved.Widget, source))
        {
            resolved = pathResolved;
            return true;
        }

        return false;
    }

    private static void CollectSearchSourceWidgetIdMatches(
        NormalizedSitemapPage page,
        SitemapSearchSource source,
        IReadOnlyList<NormalizedSitemapPage> pageChain,
        List<ResolvedSearchWidget> matches)
    {
        for (var index = 0; index < page.Widgets.Count; index++)
        {
            var widget = page.Widgets[index];
            if (string.Equals(widget.WidgetId, source.SourceWidgetId, StringComparison.Ordinal) &&
                IsMatchingSearchSource(page, widget, source))
            {
                matches.Add(new ResolvedSearchWidget(page, widget, index, pageChain));
            }

            foreach (var child in widget.Children)
            {
                var normalizedChild = SitemapNormalizer.Normalize(child);
                CollectSearchSourceWidgetIdMatches(normalizedChild, source, Append(pageChain, normalizedChild), matches);
            }
        }
    }

    private bool TryResolveSearchSourcePath(string sourceWidgetPath, out ResolvedSearchWidget resolved)
    {
        resolved = null!;
        if (currentPage is null)
        {
            return false;
        }

        var segments = sourceWidgetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], currentPage.Id, StringComparison.Ordinal))
        {
            return false;
        }

        var page = currentPage;
        var pageChain = new List<NormalizedSitemapPage> { currentPage };
        for (var segmentIndex = 1; segmentIndex < segments.Length;)
        {
            if (!TryParsePathSegment(segments[segmentIndex], "idx:", out var widgetIndex) ||
                widgetIndex < 0 ||
                widgetIndex >= page.Widgets.Count)
            {
                return false;
            }

            var widget = page.Widgets[widgetIndex];
            segmentIndex++;
            if (segmentIndex == segments.Length)
            {
                resolved = new ResolvedSearchWidget(page, widget, widgetIndex, pageChain.ToArray());
                return true;
            }

            if (!TryParsePathSegment(segments[segmentIndex], "child:", out var childIndex) ||
                childIndex < 0 ||
                childIndex >= widget.Children.Count)
            {
                return false;
            }

            page = SitemapNormalizer.Normalize(widget.Children[childIndex]);
            pageChain.Add(page);
            segmentIndex++;
        }

        return false;
    }

    private static bool TryParsePathSegment(string segment, string prefix, out int value)
    {
        value = -1;
        return segment.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(segment[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsMatchingSearchSource(
        NormalizedSitemapPage page,
        NormalizedSitemapWidget widget,
        SitemapSearchSource source)
    {
        return widget.IsVisible &&
               string.Equals(page.Id, source.SourcePageId, StringComparison.Ordinal) &&
               string.Equals(widget.Label, source.SourceWidgetLabel, StringComparison.Ordinal) &&
               string.Equals(widget.ItemName, source.SourceItemName, StringComparison.Ordinal) &&
               widget.Type == source.SourceWidgetType;
    }

    private static NormalizedSitemapPage[] Append(
        IReadOnlyList<NormalizedSitemapPage> path,
        NormalizedSitemapPage segment)
    {
        var next = new NormalizedSitemapPage[path.Count + 1];
        for (var index = 0; index < path.Count; index++)
        {
            next[index] = path[index];
        }

        next[path.Count] = segment;
        return next;
    }

    private void RebuildActiveSearchSnapshot()
    {
        if (_activeSearchQuery.Length == 0 || currentPage is null)
        {
            return;
        }

        var normalDescriptor = renderController.BuildCurrentDescriptor(currentPage);
        var effectiveDescriptor = BuildEffectiveDescriptor(normalDescriptor);
        Current = Current with
        {
            Descriptor = effectiveDescriptor,
            ChangedRowIndices = [],
            IsSearchActive = HasActiveSearch,
            SearchQuery = SearchSnapshotQuery,
            SearchResultCount = SearchSnapshotResultCount
        };
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    private static List<int> ComputeChangedRowIndices(
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
            a.SearchResultKey != b.SearchResultKey ||
            a.SourcePageId != b.SourcePageId ||
            a.SourceWidgetId != b.SourceWidgetId ||
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

    private string[] BuildBreadcrumbTrail()
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
            ChangedRowIndices = [],
            IsSearchActive = false,
            SearchQuery = string.Empty,
            SearchResultCount = 0
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

    private void ClearSearchState()
    {
        _activeSearchQuery = string.Empty;
        _searchInputQuery = string.Empty;
        _activeSearchSources = new Dictionary<string, SitemapSearchSource>(StringComparer.Ordinal);
    }

    private SitemapRenderDescriptor? BuildDescriptorForQuery(
        string? query,
        out string normalizedQuery,
        out int resultCount,
        out IReadOnlyDictionary<string, SitemapSearchSource> sources)
    {
        if (currentPage is null)
        {
            normalizedQuery = string.Empty;
            resultCount = 0;
            sources = new Dictionary<string, SitemapSearchSource>(StringComparer.Ordinal);
            return null;
        }

        var search = BuildSearchResultForPage(currentPage, query);
        normalizedQuery = search.Query;
        resultCount = search.ResultCount;
        sources = new Dictionary<string, SitemapSearchSource>(search.SourcesByResultKey, StringComparer.Ordinal);
        return search.Descriptor;
    }

    private SitemapSearchBuildResult BuildSearchResultForPage(NormalizedSitemapPage page, string? query)
    {
        var normalDescriptor = renderController.BuildCurrentDescriptor(page);
        return SitemapSearchDescriptorBuilder.Build(page, normalDescriptor, query, renderController);
    }

    private SitemapRenderDescriptor BuildEffectiveDescriptor(SitemapRenderDescriptor normalDescriptor)
    {
        if (_activeSearchQuery.Length == 0 || currentPage is null)
        {
            return normalDescriptor;
        }

        var search = SitemapSearchDescriptorBuilder.Build(currentPage, normalDescriptor, _activeSearchQuery, renderController);
        _activeSearchQuery = search.Query;
        if (_activeSearchQuery.Length == 0)
        {
            _searchInputQuery = string.Empty;
        }
        else if (_searchInputQuery.Length == 0)
        {
            _searchInputQuery = _activeSearchQuery;
        }
        _activeSearchSources = new Dictionary<string, SitemapSearchSource>(search.SourcesByResultKey, StringComparer.Ordinal);
        return search.Descriptor;
    }
}
