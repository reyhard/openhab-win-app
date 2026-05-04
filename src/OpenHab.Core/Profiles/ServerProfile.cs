namespace OpenHab.Core.Profiles;

public sealed record ServerProfile(string name, Uri? localEndpoint, Uri? cloudEndpoint, EndpointMode endpointMode)
{
    public string Name { get; init; } = name;

    public Uri? LocalEndpoint { get; init; } = localEndpoint;

    public Uri? CloudEndpoint { get; init; } = cloudEndpoint;

    public EndpointMode EndpointMode { get; init; } = endpointMode;
}
