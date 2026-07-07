using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class LruCacheTests
{
    [Fact]
    public void TryGet_OnEmpty_Misses()
    {
        var cache = new LruCache<string, int>(2);
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void SetThenGet_RoundTrips()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(1, value);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Set_PastCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3); // evicts "a"
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void TryGet_RefreshesRecency()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _)); // "b" becomes least recent
        cache.Set("c", 3);                     // evicts "b"
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Set_SameKey_ReplacesValueWithoutGrowing()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("a", 5);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(5, value);
    }

    [Fact]
    public void Set_SameKey_RefreshesRecency()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("a", 9); // "b" is now least recent
        cache.Set("c", 3); // evicts "b"
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
    }

    [Fact]
    public void Clear_Empties()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void Capacity_BelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
    }
}
