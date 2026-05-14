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
    private static readonly AsyncLocal<LogCaptureScope?> ActiveLogCaptureScope = new();
    private static bool _verboseEventLogging;

    /// <summary>
    /// Gets the full path to the diagnostic log file.
    /// </summary>
    public static string LogPath => LogFilePath;

    /// <summary>When true, suppresses icon-fetch diagnostic messages (default true to reduce noise).</summary>
    public static bool SuppressIconLogging { get; set; } = true;

    /// <summary>When true, logs every SSE event received (default false — enable for debugging).</summary>
    public static bool VerboseEventLogging
    {
        get
        {
            lock (SyncRoot)
            {
                return _verboseEventLogging;
            }
        }
        set
        {
            lock (SyncRoot)
            {
                _verboseEventLogging = value;
            }
        }
    }

    /// <summary>
    /// Begins a scoped log capture for tests.
    /// The returned scope restores the previous logger state when disposed.
    /// </summary>
    internal static IDisposable BeginLogCapture(bool? verboseEventLogging, Action<string> onLine)
    {
        ArgumentNullException.ThrowIfNull(onLine);

        var previousScope = ActiveLogCaptureScope.Value;
        var scope = new LogCaptureScope(previousScope, verboseEventLogging, onLine);
        ActiveLogCaptureScope.Value = scope;
        return scope;
    }

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
    /// Writes an informational message only when verbose event logging is enabled.
    /// </summary>
    public static void Verbose(string message, [CallerMemberName] string caller = "")
    {
        var captureScope = GetActiveLogCaptureScope();
        var isVerboseEnabled = captureScope?.VerboseEventLoggingOverride ?? VerboseEventLogging;

        if (isVerboseEnabled)
        {
            Info(message, caller);
        }
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

            for (var captureScope = GetActiveLogCaptureScope(); captureScope is not null; captureScope = captureScope.Previous)
            {
                if (captureScope.TryBeginInvoke(out var onLine))
                {
                    onLine(line);
                    break;
                }
            }
        }
        catch
        {
            // Diagnostic logging must never throw — if we can't write, we silently skip.
        }
    }

    private static LogCaptureScope? GetActiveLogCaptureScope()
    {
        var captureScope = ActiveLogCaptureScope.Value;

        while (captureScope is not null && captureScope.IsDisposed)
        {
            captureScope = captureScope.Previous;
        }

        return captureScope;
    }

    private sealed class LogCaptureScope : IDisposable
    {
        private readonly LogCaptureScope? _previous;
        private readonly bool? _verboseEventLogging;
        private readonly Action<string> _onLine;
        private readonly object _syncRoot = new();
        private bool _disposed;

        internal LogCaptureScope(LogCaptureScope? previous, bool? verboseEventLogging, Action<string> onLine)
        {
            _previous = previous;
            _verboseEventLogging = verboseEventLogging;
            _onLine = onLine;
        }

        internal bool? VerboseEventLoggingOverride => _verboseEventLogging;

        internal LogCaptureScope? Previous => _previous;

        internal bool IsDisposed
        {
            get
            {
                lock (_syncRoot)
                {
                    return _disposed;
                }
            }
        }

        internal bool TryBeginInvoke(out Action<string> onLine)
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    onLine = null!;
                    return false;
                }

                onLine = _onLine;
                return true;
            }
        }

        public void Dispose()
        {
            lock (SyncRoot)
            {
                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                }

                if (ActiveLogCaptureScope.Value == this)
                {
                    ActiveLogCaptureScope.Value = _previous;
                }
            }
        }
    }
}
