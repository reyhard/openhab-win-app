using Microsoft.Win32;
using OpenHab.Core;
using Windows.ApplicationModel;

namespace OpenHab.Windows.Tray.Startup;

/// <summary>
/// Manages launch-at-startup behavior.
/// In MSIX (packaged) mode, uses the <c>Windows.ApplicationModel.StartupTask</c> API
/// which requires a <c>windows.startupTask</c> extension in Package.appxmanifest.
/// In unpackaged mode, falls back to the <c>HKCU\...\Run</c> registry key.
/// </summary>
public static class StartupManager
{
    private const string StartupTaskId = "openHABStartup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "openHAB";

    private static readonly bool IsPackaged = DetectPackaged();

    private static bool DetectPackaged()
    {
        try
        {
            // In an MSIX-packaged process, Package.Current is non-null.
            return Package.Current is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables or disables launch-at-startup. Safe to call from any thread;
    /// the MSIX API must be called on a UI thread if it shows consent UI,
    /// but <c>Disable</c> and <c>GetAsync</c> do not.
    /// </summary>
    public static async Task SetEnabledAsync(bool enabled)
    {
        if (IsPackaged)
        {
            await SetPackagedEnabledAsync(enabled);
        }
        else
        {
            SetUnpackagedEnabled(enabled);
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the app is currently configured to launch at startup.
    /// </summary>
    public static async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged)
        {
            return await IsPackagedEnabledAsync();
        }

        return IsUnpackagedEnabled();
    }

    // ── MSIX (packaged) path ──────────────────────────────────────

    private static async Task SetPackagedEnabledAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (task.State == StartupTaskState.DisabledByUser ||
                task.State == StartupTaskState.DisabledByPolicy)
            {
                // Cannot override user/system policy. Log and return.
                DiagnosticLogger.Warn(
                    $"Startup task '{StartupTaskId}' cannot be changed: state={task.State}");
                return;
            }

            if (enabled)
            {
                var result = await task.RequestEnableAsync();
                if (result == StartupTaskState.Enabled ||
                    result == StartupTaskState.EnabledByPolicy)
                {
                    DiagnosticLogger.Info("Startup task enabled.");
                }
                else
                {
                    DiagnosticLogger.Warn(
                        $"Startup task enable request returned: {result}");
                }
            }
            else
            {
                task.Disable();
                DiagnosticLogger.Info("Startup task disabled.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error("Failed to set startup task state.", ex);
        }
    }

    private static async Task<bool> IsPackagedEnabledAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return task.State == StartupTaskState.Enabled ||
                   task.State == StartupTaskState.EnabledByPolicy;
        }
        catch
        {
            return false;
        }
    }

    // ── Unpackaged (registry) path ─────────────────────────────────

    private static void SetUnpackagedEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                DiagnosticLogger.Warn("Cannot open registry Run key for writing.");
                return;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    DiagnosticLogger.Warn("Cannot determine executable path for startup registration.");
                    return;
                }

                key.SetValue(RunValueName, $"\"{exePath}\"");
                DiagnosticLogger.Info($"Registered in Run key: {exePath}");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                DiagnosticLogger.Info("Removed from Run key.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error("Failed to update registry Run key.", ex);
        }
    }

    private static bool IsUnpackagedEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
