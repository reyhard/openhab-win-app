namespace OpenHab.Core.Profiles;

public sealed record ServerProfile(
    string Name,
    Uri? LocalEndpoint,
    Uri? CloudEndpoint,
    EndpointMode EndpointMode);
