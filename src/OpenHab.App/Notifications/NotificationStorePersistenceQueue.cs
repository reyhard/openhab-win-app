using System.Text;
using OpenHab.Core;

namespace OpenHab.App.Notifications;

internal interface INotificationSnapshotWriter
{
    Task WriteAsync(string storageFilePath, string serializedSnapshot);
}

internal sealed class AtomicNotificationSnapshotWriter : INotificationSnapshotWriter
{
    public async Task WriteAsync(string storageFilePath, string serializedSnapshot)
    {
        var directory = Path.GetDirectoryName(storageFilePath)
            ?? throw new InvalidOperationException("Notification storage path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryFilePath = Path.Combine(
            directory,
            $".{Path.GetFileName(storageFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(serializedSnapshot).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(storageFilePath))
            {
                try
                {
                    File.Replace(temporaryFilePath, storageFilePath, destinationBackupFileName: null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryFilePath, storageFilePath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryFilePath, storageFilePath);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporaryFilePath);
            }
            catch
            {
                // The completed destination remains valid; temporary-file cleanup is best effort.
            }
        }
    }
}

internal sealed class NotificationStorePersistenceQueue
{
    private static readonly object CoordinatorsSyncRoot = new();
    private static readonly Dictionary<string, NotificationStorePersistenceQueue> Coordinators =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object syncRoot = new();
    private readonly string storageFilePath;
    private readonly INotificationSnapshotWriter snapshotWriter;
    private readonly List<FlushBarrier> flushBarriers = [];

    private PendingSnapshot? pendingSnapshot;
    private bool writerRunning;
    private long latestGeneration;
    private long attemptedGeneration;
    private Exception? latestAttemptFailure;

    internal NotificationStorePersistenceQueue(
        string storageFilePath,
        INotificationSnapshotWriter? snapshotWriter = null)
    {
        this.storageFilePath = Path.GetFullPath(storageFilePath);
        this.snapshotWriter = snapshotWriter ?? new AtomicNotificationSnapshotWriter();
    }

    internal static NotificationStorePersistenceQueue ForPath(string storageFilePath)
    {
        var normalizedPath = Path.GetFullPath(storageFilePath);
        lock (CoordinatorsSyncRoot)
        {
            if (!Coordinators.TryGetValue(normalizedPath, out var coordinator))
            {
                coordinator = new NotificationStorePersistenceQueue(normalizedPath);
                Coordinators[normalizedPath] = coordinator;
            }

            return coordinator;
        }
    }

    internal void Enqueue(string serializedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(serializedSnapshot);

        lock (syncRoot)
        {
            var generation = ++latestGeneration;
            pendingSnapshot = new PendingSnapshot(generation, serializedSnapshot);
            if (!writerRunning)
            {
                writerRunning = true;
                _ = Task.Run(ProcessWritesAsync);
            }
        }
    }

    internal Task FlushAsync()
    {
        lock (syncRoot)
        {
            var targetGeneration = latestGeneration;
            if (targetGeneration == 0)
            {
                return Task.CompletedTask;
            }

            if (attemptedGeneration >= targetGeneration)
            {
                return latestAttemptFailure is null
                    ? Task.CompletedTask
                    : Task.FromException(latestAttemptFailure);
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            flushBarriers.Add(new FlushBarrier(targetGeneration, completion));
            return completion.Task;
        }
    }

    private async Task ProcessWritesAsync()
    {
        while (true)
        {
            PendingSnapshot snapshot;
            lock (syncRoot)
            {
                if (pendingSnapshot is null)
                {
                    writerRunning = false;
                    return;
                }

                snapshot = pendingSnapshot;
                pendingSnapshot = null;
            }

            Exception? failure = null;
            try
            {
                await snapshotWriter.WriteAsync(storageFilePath, snapshot.SerializedSnapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
                DiagnosticLogger.Warn(
                    $"Notification history save failed: {ex.GetType().Name}: {SafeDiagnosticText.ForLog(ex)}");
            }

            List<FlushBarrier> completedBarriers;
            lock (syncRoot)
            {
                attemptedGeneration = snapshot.Generation;
                latestAttemptFailure = failure;
                completedBarriers = flushBarriers
                    .Where(barrier => barrier.TargetGeneration <= attemptedGeneration)
                    .ToList();
                flushBarriers.RemoveAll(barrier => barrier.TargetGeneration <= attemptedGeneration);
            }

            foreach (var barrier in completedBarriers)
            {
                if (failure is null)
                {
                    barrier.Completion.TrySetResult();
                }
                else
                {
                    barrier.Completion.TrySetException(failure);
                }
            }
        }
    }

    private sealed record PendingSnapshot(long Generation, string SerializedSnapshot);

    private sealed record FlushBarrier(long TargetGeneration, TaskCompletionSource Completion);
}
