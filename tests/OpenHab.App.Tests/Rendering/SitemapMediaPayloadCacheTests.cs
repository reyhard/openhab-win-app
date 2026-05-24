using OpenHab.Windows.Tray.Rendering;

namespace OpenHab.App.Tests.Rendering;

public sealed class SitemapMediaPayloadCacheTests
{
    [Fact]
    public void Add_EvictsOldestEntryWhenCapacityIsExceeded()
    {
        var cache = new SitemapMediaPayloadCache(maxEntries: 2);

        cache.AddOrUpdate("one", [1], "image/png");
        cache.AddOrUpdate("two", [2], "image/png");
        cache.AddOrUpdate("three", [3], "image/png");

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("one", out _));
        Assert.True(cache.TryGet("two", out var two));
        Assert.Equal([2], two.Bytes);
        Assert.True(cache.TryGet("three", out var three));
        Assert.Equal([3], three.Bytes);
    }

    [Fact]
    public void TryGet_MarksEntryAsRecentlyUsedBeforeEviction()
    {
        var cache = new SitemapMediaPayloadCache(maxEntries: 2);

        cache.AddOrUpdate("one", [1], "image/png");
        cache.AddOrUpdate("two", [2], "image/png");
        Assert.True(cache.TryGet("one", out _));

        cache.AddOrUpdate("three", [3], "image/png");

        Assert.True(cache.TryGet("one", out _));
        Assert.False(cache.TryGet("two", out _));
        Assert.True(cache.TryGet("three", out _));
    }

    [Fact]
    public void Clear_RemovesCachedPayloads()
    {
        var cache = new SitemapMediaPayloadCache(maxEntries: 2);
        cache.AddOrUpdate("one", [1], "image/png");

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("one", out _));
    }
}
