namespace OpenHab.Windows.Tray.DeviceInfo;

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
