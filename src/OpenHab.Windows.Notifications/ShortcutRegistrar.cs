using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using OpenHab.Core;

namespace OpenHab.Windows.Notifications;

/// <summary>
/// Registers a Start menu shortcut with an AppUserModelId so that
/// <see cref="ToastService"/> can use <c>AppNotificationManager</c>
/// in an unpackaged (non-MSIX) deployment.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows notification registration integration.")]
public static class ShortcutRegistrar
{
    private const string AppUserModelId = "OpenHab.OpenHabWinApp";
    private const string ShortcutName = "openHAB.lnk";

    /// <summary>
    /// Ensures a Start menu shortcut with the correct <c>AppUserModelId</c> exists.
    /// Safe to call on every launch — no-op if the shortcut is already present and valid.
    /// </summary>
    /// <param name="exePath">Full path to the current executable.</param>
    public static void EnsureRegistered(string exePath)
    {
        var programsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "openHAB");
        var shortcutPath = Path.Combine(programsDir, ShortcutName);

        if (File.Exists(shortcutPath))
        {
            DiagnosticLogger.Info("Start menu shortcut already present — skipping registration");
            return;
        }

        try
        {
            Directory.CreateDirectory(programsDir);
            CreateShortcutWithAppId(shortcutPath, exePath, AppUserModelId);
            DiagnosticLogger.Info($"Start menu shortcut registered at: {shortcutPath}");
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error("Failed to register Start menu shortcut", ex);
        }
    }

    private static void CreateShortcutWithAppId(string shortcutPath, string exePath, string appId)
    {
        var shellLink = (IShellLinkW)new ShellLink();
        shellLink.SetPath(exePath);
        shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath)!);

        var propertyStore = (IPropertyStore)shellLink;
        var appUserModelKey = new PropertyKey(
            new Guid("{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}"),
            5);
        propertyStore.SetValue(ref appUserModelKey, new PropVariant(appId));
        propertyStore.Commit();

        var persistFile = (IPersistFile)shellLink;
        persistFile.Save(shortcutPath, true);
    }

    // ── COM interop for IShellLink + IPropertyStore ──

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);
        int GetAt(uint propertyIndex, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        private readonly ushort reserved1;
        private readonly ushort reserved2;
        private readonly ushort reserved3;
        private readonly IntPtr value;
        private readonly IntPtr reserved;

        public PropVariant(string value)
        {
            vt = 31; // VT_LPWSTR
            reserved1 = reserved2 = reserved3 = 0;
            this.value = Marshal.StringToCoTaskMemUni(value);
            reserved = IntPtr.Zero;
        }
    }
}
