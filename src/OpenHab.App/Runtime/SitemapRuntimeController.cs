using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Sitemaps.Models;
using OpenHab.Sitemaps.Parsing;
using OpenHab.Sitemaps.Runtime;
using System.Linq;

namespace OpenHab.App.Runtime;

public sealed class SitemapRuntimeController
{
    private readonly AppSettingsController settingsController;
    private readonly SitemapRenderController renderController;
    private readonly Func<TransportKind, Uri, IOpenHabClient> clientFactory;
    private NormalizedSitemapPage? currentPage;
    private readonly Stack<NormalizedSitemapPage> backStack = new();
    public bool CanGoBack => backStack.Count > 0;

    public SitemapRuntimeController(
        AppSettingsController settingsController,
        SitemapRenderController renderController,
        Func<TransportKind, Uri, IOpenHabClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(settingsController);
        ArgumentNullException.ThrowIfNull(renderController);
        ArgumentNullException.ThrowIfNull(clientFactory);

        this.settingsController = settingsController;
        this.renderController = renderController;
        this.clientFactory = clientFactory;
    }

    public SitemapRuntimeSnapshot Current { get; private set; } = SitemapRuntimeSnapshot.Initial;

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Current = Current with { IsBusy = true, StatusText = "Loading homepage..." };
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
                HasError = false
            };
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
                    HasError = false
                };
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
                    HasError = true
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
                HasError = true
            };
        }
        finally
        {
            Current = Current with { IsBusy = false };
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

        var descriptor = renderController.BuildCurrentDescriptor(normalized);
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = $"Navigated to: {normalized.Label}"
        };
        return true;
    }

    public bool NavigateBack()
    {
        if (backStack.Count == 0) return false;
        currentPage = backStack.Pop();
        var descriptor = renderController.BuildCurrentDescriptor(currentPage);
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = currentPage.Label
        };
        return true;
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

        var descriptor = renderController.BuildCurrentDescriptor(currentPage);
        Current = Current with
        {
            Descriptor = descriptor,
            Breadcrumbs = BuildBreadcrumbTrail(),
            StatusText = currentPage.Label
        };
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
        return renderController.BuildCurrentDescriptor(normalized);
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
