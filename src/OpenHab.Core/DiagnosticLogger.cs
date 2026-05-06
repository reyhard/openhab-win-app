using System.Runtime.CompilerServices;

namespace OpenHab.Core;

/// <summary>
/// Simple file-based diagnostic logger.
/// Writes timestamped lines to a log file in the app's local data directory.
/// Thread-safe via lock; designed for diagnostic/debugging, not high-throughput production logging.
/// </summary>
public static class DiagnosticLogger
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenHab.WinApp");

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "diagnostics.log");

    private static readonly object SyncRoot = new();

    /// <summary>
    /// Gets the full path to the diagnostic log file.
    /// </summary>
    public static string LogPath => LogFilePath;

    /// <summary>
    /// Writes an informational message to the log.
    /// </summary>
    public static void Info(string message, [CallerMemberName] string caller = "")
    {
        Write("INFO", message, caller);
    }

    /// <summary>
    /// Writes a warning message to the log.
    /// </summary>
    public static void Warn(string message, [CallerMemberName] string caller = "")
    {
        Write("WARN", message, caller);
    }

    /// <summary>
    /// Writes an error message to the log, optionally including exception details.
    /// </summary>
    public static void Error(string message, Exception? exception = null, [CallerMemberName] string caller = "")
    {
        var text = exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", text, caller);
    }

    private static void Write(string level, string message, string caller)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [{caller}] {message}{Environment.NewLine}";
            lock (SyncRoot)
            {
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // Diagnostic logging must never throw — if we can't write, we silently skip.
        }
    }
}
