using OpenHab.Core;

namespace OpenHab.App.Notifications;

public sealed class NotificationMediaCache
{
    public const int DefaultMaxFiles = 64;
    public const long DefaultMaxBytes = 32L * 1024 * 1024;
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(30);

    private readonly string rootDirectory;
    private readonly int maxFiles;
    private readonly long maxBytes;
    private readonly TimeSpan maxAge;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly SemaphoreSlim gate = new(1, 1);

    public NotificationMediaCache(
        string? rootDirectory = null,
        int maxFiles = DefaultMaxFiles,
        long maxBytes = DefaultMaxBytes,
        TimeSpan? maxAge = null)
        : this(rootDirectory, maxFiles, maxBytes, maxAge, () => DateTimeOffset.UtcNow)
    {
    }

    internal NotificationMediaCache(
        string? rootDirectory,
        int maxFiles,
        long maxBytes,
        TimeSpan? maxAge,
        Func<DateTimeOffset> utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentNullException.ThrowIfNull(utcNow);

        var resolvedMaxAge = maxAge ?? DefaultMaxAge;
        if (resolvedMaxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        }

        this.rootDirectory = Path.GetFullPath(rootDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenHab.WinApp",
                "NotificationMedia"));
        this.maxFiles = maxFiles;
        this.maxBytes = maxBytes;
        this.maxAge = resolvedMaxAge;
        this.utcNow = utcNow;
    }

    public async Task<Uri?> StoreAsync(
        string cacheKey,
        string extension,
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentNullException.ThrowIfNull(data);

        if (data.LongLength > maxBytes)
        {
            return null;
        }

        string targetPath;
        try
        {
            targetPath = ResolveCachePath(cacheKey, extension);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            DiagnosticLogger.Warn($"Notification media cache path rejected: error='{ex.GetType().Name}'");
            return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(rootDirectory);
            temporaryPath = Path.Combine(rootDirectory, $".{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temporaryPath, data, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, targetPath, overwrite: true);
            temporaryPath = null;
            var retainedAtUtc = utcNow().UtcDateTime;
            File.SetLastWriteTimeUtc(targetPath, retainedAtUtc);
            File.SetLastAccessTimeUtc(targetPath, retainedAtUtc);

            PruneCore(targetPath);
            return new Uri(targetPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Notification media cache write failed: error='{ex.GetType().Name}'");
            return null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath, "temporary-file-cleanup");
            }

            gate.Release();
        }
    }

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PruneCore(retainedPath: null);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Notification media cache prune failed: error='{ex.GetType().Name}'");
        }
        finally
        {
            gate.Release();
        }
    }

    public void Clear()
    {
        gate.Wait();
        try
        {
            if (!Directory.Exists(rootDirectory))
            {
                return;
            }

            foreach (var path in EnumerateFiles())
            {
                TryDelete(path, "clear");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Notification media cache clear failed: error='{ex.GetType().Name}'");
        }
        finally
        {
            gate.Release();
        }
    }

    private string ResolveCachePath(string cacheKey, string extension)
    {
        if (cacheKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || cacheKey is "." or ".."
            || extension.Contains(Path.DirectorySeparatorChar)
            || extension.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("The cache name must not contain path separators or invalid filename characters.");
        }

        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var combinedPath = Path.GetFullPath(Path.Combine(rootDirectory, $"{cacheKey}{normalizedExtension}"));
        var rootPrefix = rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? rootDirectory
            : rootDirectory + Path.DirectorySeparatorChar;
        if (!combinedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The cache path must remain beneath the cache root.");
        }

        return combinedPath;
    }

    private void PruneCore(string? retainedPath)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        var expiryThreshold = utcNow().UtcDateTime - maxAge;
        foreach (var entry in ReadEntries())
        {
            if (!PathEquals(entry.Path, retainedPath) && entry.LastWriteTimeUtc < expiryThreshold)
            {
                TryDelete(entry.Path, "age-prune");
            }
        }

        var retainedEntries = ReadEntries()
            .OrderBy(entry => entry.LastWriteTimeUtc)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var retainedCount = retainedEntries.Count;
        var totalBytes = retainedEntries.Sum(entry => entry.Length);

        foreach (var entry in retainedEntries)
        {
            if (retainedCount <= maxFiles && totalBytes <= maxBytes)
            {
                break;
            }

            if (PathEquals(entry.Path, retainedPath))
            {
                continue;
            }

            if (TryDelete(entry.Path, "capacity-prune"))
            {
                retainedCount--;
                totalBytes -= entry.Length;
            }
        }
    }

    private IReadOnlyList<CacheEntry> ReadEntries()
    {
        var entries = new List<CacheEntry>();
        foreach (var path in EnumerateFiles())
        {
            try
            {
                var info = new FileInfo(path);
                entries.Add(new CacheEntry(path, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Warn($"Notification media cache entry inspection failed: error='{ex.GetType().Name}'");
            }
        }

        return entries;
    }

    private IEnumerable<string> EnumerateFiles()
    {
        try
        {
            return Directory.EnumerateFiles(rootDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn($"Notification media cache enumeration failed: error='{ex.GetType().Name}'");
            return [];
        }
    }

    private static bool TryDelete(string path, string operation)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Warn(
                $"Notification media cache cleanup failed: operation='{operation}', error='{ex.GetType().Name}'");
            return false;
        }
    }

    private static bool PathEquals(string path, string? otherPath)
    {
        return otherPath is not null && string.Equals(path, otherPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CacheEntry(string Path, long Length, DateTime LastWriteTimeUtc);
}
