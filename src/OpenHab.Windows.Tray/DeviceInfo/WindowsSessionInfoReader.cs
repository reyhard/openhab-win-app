using System.Diagnostics.CodeAnalysis;

namespace OpenHab.Windows.Tray.DeviceInfo;

[ExcludeFromCodeCoverage(Justification = "Windows device-state reader for live OS session state.")]
internal sealed class WindowsSessionInfoReader
{
    private volatile string sessionState = "active";
    private volatile bool isLocked;

    public bool IsLocked => isLocked;

    public string SessionState => sessionState;

    public void MarkActive()
    {
        isLocked = false;
        sessionState = "active";
    }

    public void MarkLocked()
    {
        isLocked = true;
        sessionState = "locked";
    }

    public void MarkSleep()
    {
        sessionState = "sleep";
    }

    public void MarkResume()
    {
        isLocked = false;
        sessionState = "resume";
    }
}
