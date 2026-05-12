using System.Diagnostics;

namespace OpenHab.Core.Diagnostics;

public static class OpenHabProfiling
{
    private static readonly ActivitySource Source = new("OpenHab.WinApp");

    public static Activity? StartScope(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Source.StartActivity(name, ActivityKind.Internal);
    }
}
