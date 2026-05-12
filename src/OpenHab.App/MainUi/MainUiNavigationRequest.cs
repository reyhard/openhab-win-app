namespace OpenHab.App.MainUi;

public sealed record MainUiNavigationRequest(string Route)
{
    public static MainUiNavigationRequest Root { get; } = new("/");
}
