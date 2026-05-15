using OpenHab.App.Settings;
using OpenHab.Core;
using OpenHab.Core.Api;
using OpenHab.Core.Profiles;
using OpenHab.Windows.Notifications;

namespace OpenHab.App.Notifications;

public sealed class NotificationActionExecutor
{
    private readonly HttpClient httpClient;
    private readonly Func<AppSettings> getSettings;
    private readonly Func<TransportKind, string?> getApiToken;
    private readonly Func<TransportKind, CloudCredentials?> getCloudCredentials;
    private readonly Func<Uri, Task> openExternal;

    public NotificationActionExecutor(
        HttpClient httpClient,
        Func<AppSettings> getSettings,
        Func<TransportKind, string?> getApiToken,
        Func<TransportKind, CloudCredentials?> getCloudCredentials,
        Func<Uri, Task> openExternal)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(getApiToken);
        ArgumentNullException.ThrowIfNull(getCloudCredentials);
        ArgumentNullException.ThrowIfNull(openExternal);

        this.httpClient = httpClient;
        this.getSettings = getSettings;
        this.getApiToken = getApiToken;
        this.getCloudCredentials = getCloudCredentials;
        this.openExternal = openExternal;
    }

    public async Task ExecuteAsync(NotificationAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        var actionType = action.Type.Trim().ToLowerInvariant();
        switch (actionType)
        {
            case "command":
                await ExecuteCommandAsync(action.Payload, cancellationToken);
                break;
            case "http":
            case "https":
                await OpenExternalUriAsync(action.Payload);
                break;
            case "ui":
                await ExecuteUiAsync(action.Payload);
                break;
            case "rule":
                DiagnosticLogger.Warn("Rule action not supported by NotificationActionExecutor.");
                break;
            case "app":
                DiagnosticLogger.Warn("App action not supported by NotificationActionExecutor.");
                break;
            default:
                DiagnosticLogger.Warn($"Unknown notification action type '{action.Type}'.");
                break;
        }
    }

    private async Task ExecuteCommandAsync(string payload, CancellationToken cancellationToken)
    {
        var colonIndex = payload.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= payload.Length - 1)
        {
            DiagnosticLogger.Warn("Invalid command notification action payload.");
            return;
        }

        var itemName = payload[..colonIndex].Trim();
        var commandValue = payload[(colonIndex + 1)..].Trim();
        if (itemName.Length == 0 || commandValue.Length == 0)
        {
            DiagnosticLogger.Warn("Invalid command notification action payload.");
            return;
        }
        var transport = SelectTransport(getSettings().EndpointMode);
        var client = CreateClientForTransport(transport);
        await client.SendCommandAsync(itemName, commandValue, cancellationToken);
    }

    private async Task ExecuteUiAsync(string payload)
    {
        var settings = getSettings();
        var endpoint = SelectEndpoint(settings);
        var target = BuildUiUri(endpoint, payload);
        if (target is null)
        {
            DiagnosticLogger.Warn("Invalid ui notification action payload.");
            return;
        }

        await openExternal(target);
    }

    private async Task OpenExternalUriAsync(string payload)
    {
        if (!Uri.TryCreate(payload, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            DiagnosticLogger.Warn("Invalid absolute URL notification action payload.");
            return;
        }

        await openExternal(uri);
    }

    private OpenHabHttpClient CreateClientForTransport(TransportKind transport)
    {
        var settings = getSettings();
        var endpoint = transport == TransportKind.Cloud ? settings.CloudEndpoint : settings.LocalEndpoint;
        if (transport == TransportKind.Local)
        {
            return new OpenHabHttpClient(httpClient, endpoint, apiToken: getApiToken(TransportKind.Local));
        }

        var creds = getCloudCredentials(TransportKind.Cloud);
        return new OpenHabHttpClient(
            httpClient,
            endpoint,
            basicUserName: creds?.UserName,
            basicPassword: creds?.Password);
    }

    private static TransportKind SelectTransport(EndpointMode endpointMode)
    {
        if (endpointMode == EndpointMode.CloudOnly)
        {
            return TransportKind.Cloud;
        }

        return TransportKind.Local;
    }

    private static Uri SelectEndpoint(AppSettings settings)
    {
        return settings.EndpointMode == EndpointMode.CloudOnly
            ? settings.CloudEndpoint
            : settings.LocalEndpoint;
    }

    private static Uri? BuildUiUri(Uri endpoint, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var trimmed = payload.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        if (trimmed.StartsWith("navigate:", StringComparison.OrdinalIgnoreCase))
        {
            var route = trimmed["navigate:".Length..].Trim();
            if (route.Length == 0)
            {
                return null;
            }

            return BuildEndpointUri(endpoint, route);
        }

        if (trimmed.StartsWith("popup:", StringComparison.OrdinalIgnoreCase))
        {
            var popupId = trimmed["popup:".Length..].Trim();
            if (popupId.Length == 0)
            {
                return null;
            }

            return BuildEndpointUri(endpoint, $"?notificationPopup={Uri.EscapeDataString(popupId)}");
        }

        return BuildEndpointUri(endpoint, trimmed);
    }

    private static Uri BuildEndpointUri(Uri endpointBaseUri, string relativePath)
    {
        var baseBuilder = new UriBuilder(endpointBaseUri);
        var basePath = baseBuilder.Path ?? string.Empty;
        if (!basePath.EndsWith('/'))
        {
            basePath += "/";
        }

        baseBuilder.Path = basePath;
        return new Uri(baseBuilder.Uri, relativePath.TrimStart('/'));
    }
}
