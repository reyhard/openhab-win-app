namespace OpenHab.Core.Profiles;

public static class EndpointSelector
{
    public static TransportSelection Select(ServerProfile profile, bool localReachable)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.EndpointMode switch
        {
            EndpointMode.LocalOnly => SelectRequired(profile.Name, TransportKind.Local, profile.LocalEndpoint, "LocalOnly", "local"),
            EndpointMode.CloudOnly => SelectRequired(profile.Name, TransportKind.Cloud, profile.CloudEndpoint, "CloudOnly", "cloud"),
            EndpointMode.Automatic when localReachable && profile.LocalEndpoint is not null => new TransportSelection(TransportKind.Local, profile.LocalEndpoint),
            EndpointMode.Automatic => SelectRequired(profile.Name, TransportKind.Cloud, profile.CloudEndpoint, "Automatic", "cloud"),
            _ => throw new InvalidOperationException($"Profile '{profile.Name}' has unsupported endpoint mode '{profile.EndpointMode}'.")
        };
    }

    private static TransportSelection SelectRequired(string profileName, TransportKind kind, Uri? endpoint, string mode, string endpointName)
    {
        if (endpoint is null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' is {mode} but has no {endpointName} endpoint.");
        }

        return new TransportSelection(kind, endpoint);
    }
}
