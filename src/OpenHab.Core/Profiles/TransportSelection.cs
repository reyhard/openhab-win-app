namespace OpenHab.Core.Profiles;

public enum TransportKind
{
    Local,
    Cloud
}

public sealed record TransportSelection(TransportKind Kind, Uri BaseUri);
