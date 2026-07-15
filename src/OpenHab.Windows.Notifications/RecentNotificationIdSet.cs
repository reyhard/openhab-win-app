namespace OpenHab.Windows.Notifications;

internal sealed class RecentNotificationIdSet
{
    private readonly int capacity;
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private readonly Queue<string> insertionOrder = new();

    public RecentNotificationIdSet(int capacity, IEnumerable<string>? oldestToNewestIds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;

        if (oldestToNewestIds is null)
        {
            return;
        }

        foreach (var id in oldestToNewestIds)
        {
            Add(id);
        }
    }

    public int Count => ids.Count;

    public bool Add(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!ids.Add(id))
        {
            return false;
        }

        insertionOrder.Enqueue(id);
        while (ids.Count > capacity)
        {
            ids.Remove(insertionOrder.Dequeue());
        }

        return true;
    }

    public bool Contains(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ids.Contains(id);
    }
}
