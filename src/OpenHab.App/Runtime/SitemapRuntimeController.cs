using OpenHab.App.Settings;
using OpenHab.App.Sitemaps;
using OpenHab.Core.Api;
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
    private NormalizedSitemapPage? currentPage;

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
                StatusText = $"Connected via {primary.Kind.ToString().ToLowerInvariant()}.",
                IsBusy = false,
                HasError = false
            };
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
                    StatusText = "Connected via cloud (local failed).",
                    IsBusy = false,
                    HasError = false
                };
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
        currentPage = normalized;
        return renderController.BuildCurrentDescriptor(normalized);
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
}
