using System.Diagnostics;
using System.Text.Json;
using Windows.UI.Shell;

namespace OpenHab.Windows.Tray.DeviceInfo;

internal sealed class WindowsFocusInfoReader
{
    private const int CloudDataStoreReaderTimeoutMilliseconds = 2_000;

    private static readonly string[] QuietHoursSettingTypeNames =
    [
        "windows.data.donotdisturb.QuietHoursSettings",
        "windows.data.notifications.quiethourssettings"
    ];

    private readonly Func<string?> readQuietHoursProfile;
    private readonly Func<bool> isSupported;
    private readonly Func<bool> isFocusActive;

    public WindowsFocusInfoReader()
        : this(
            ReadQuietHoursProfileFromCloudDataStore,
            () => FocusSessionManager.IsSupported,
            () => FocusSessionManager.GetDefault().IsFocusActive)
    {
    }

    internal WindowsFocusInfoReader(Func<string?> readQuietHoursProfile, Func<bool> isSupported, Func<bool> isFocusActive)
    {
        this.readQuietHoursProfile = readQuietHoursProfile;
        this.isSupported = isSupported;
        this.isFocusActive = isFocusActive;
    }

    public string ReadState()
    {
        var quietHoursProfile = TryReadQuietHoursProfile();
        if (IsActiveQuietHoursProfile(quietHoursProfile))
        {
            return "ON";
        }

        try
        {
            if (!isSupported())
            {
                return IsUnrestrictedQuietHoursProfile(quietHoursProfile) ? "OFF" : "UNSUPPORTED";
            }

            return isFocusActive() ? "ON" : "OFF";
        }
        catch
        {
            return IsUnrestrictedQuietHoursProfile(quietHoursProfile) ? "OFF" : "UNSUPPORTED";
        }
    }

    internal static string? TryReadQuietHoursProfile(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var jsonStart = output.IndexOf('[', StringComparison.Ordinal);
        var jsonEnd = output.LastIndexOf(']');
        if (jsonStart < 0 || jsonEnd < jsonStart)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(output[jsonStart..(jsonEnd + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("Data", out var data)
                    && data.TryGetProperty("selectedProfile", out var selectedProfile)
                    && selectedProfile.ValueKind == JsonValueKind.String)
                {
                    return selectedProfile.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    internal static string? ReadQuietHoursProfileFromCloudDataStore(Func<string, string?> runReader)
    {
        foreach (var typeName in QuietHoursSettingTypeNames)
        {
            var output = runReader(typeName);
            if (output is null)
            {
                continue;
            }

            var profile = TryReadQuietHoursProfile(output);
            if (profile is not null)
            {
                return profile;
            }
        }

        return null;
    }

    private static string? ReadQuietHoursProfileFromCloudDataStore() =>
        ReadQuietHoursProfileFromCloudDataStore(RunCloudDataStoreReader);

    private static string? RunCloudDataStoreReader(string typeName)
    {
        var executablePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "readCloudDataSettings.exe");

        if (!File.Exists(executablePath))
        {
            executablePath = "readCloudDataSettings.exe";
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.StartInfo.ArgumentList.Add("get");
            process.StartInfo.ArgumentList.Add("-type:" + typeName);

            if (!process.Start())
            {
                return null;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(CloudDataStoreReaderTimeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore cleanup failures; this reader is best-effort.
                }

                return null;
            }

            _ = standardError.GetAwaiter().GetResult();
            return standardOutput.GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private string? TryReadQuietHoursProfile()
    {
        try
        {
            return readQuietHoursProfile();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUnrestrictedQuietHoursProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return false;
        }

        var trimmedProfile = profile.Trim();
        return trimmedProfile.EndsWith(".Unrestricted", StringComparison.OrdinalIgnoreCase)
            || trimmedProfile.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveQuietHoursProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return false;
        }

        var trimmedProfile = profile.Trim();
        return trimmedProfile.EndsWith(".PriorityOnly", StringComparison.OrdinalIgnoreCase)
            || trimmedProfile.EndsWith(".AlarmsOnly", StringComparison.OrdinalIgnoreCase)
            || trimmedProfile.Equals("PriorityOnly", StringComparison.OrdinalIgnoreCase)
            || trimmedProfile.Equals("AlarmsOnly", StringComparison.OrdinalIgnoreCase);
    }
}
