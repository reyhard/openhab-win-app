using System.Threading;

namespace OpenHab.Windows.Tray.Rendering;

internal sealed class SitemapMediaPayloadCache
{
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recency = new();
    private readonly Lock syncRoot = new();
    private readonly int maxEntries;

    internal SitemapMediaPayloadCache(int maxEntries)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Cache capacity must be positive.");
        }

        this.maxEntries = maxEntries;
    }

    internal int Count
    {
        get
        {
            lock (syncRoot)
            {
                return entries.Count;
            }
        }
    }

    internal bool TryGet(string key, out SitemapMediaPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (syncRoot)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                payload = default;
                return false;
            }

            recency.Remove(entry.Node);
            recency.AddLast(entry.Node);
            payload = entry.Payload.Copy();
            return true;
        }
    }

    internal void AddOrUpdate(string key, byte[] bytes, string? mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(bytes);

        lock (syncRoot)
        {
            var payload = new SitemapMediaPayload(bytes.ToArray(), mediaType);
            if (entries.TryGetValue(key, out var existing))
            {
                existing.Payload = payload;
                recency.Remove(existing.Node);
                recency.AddLast(existing.Node);
                return;
            }

            while (entries.Count >= maxEntries && recency.First is { } oldest)
            {
                entries.Remove(oldest.Value);
                recency.RemoveFirst();
            }

            var node = recency.AddLast(key);
            entries[key] = new CacheEntry(payload, node);
        }
    }

    internal void Clear()
    {
        lock (syncRoot)
        {
            entries.Clear();
            recency.Clear();
        }
    }

    private sealed class CacheEntry(SitemapMediaPayload payload, LinkedListNode<string> node)
    {
        public SitemapMediaPayload Payload { get; set; } = payload;
        public LinkedListNode<string> Node { get; } = node;
    }
}

internal readonly record struct SitemapMediaPayload(byte[] Bytes, string? MediaType)
{
    public SitemapMediaPayload Copy() => new(Bytes.ToArray(), MediaType);
}
