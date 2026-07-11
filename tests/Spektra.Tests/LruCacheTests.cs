using Spektra.Core;

namespace Spektra.Tests;

public class LruCacheTests
{
    [Test]
    public async Task TryGet_OnEmpty_Misses()
    {
        var cache = new LruCache<string, int>(2);
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SetThenGet_RoundTrips()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        await Assert.That(cache.TryGet("a", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Set_PastCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3); // evicts "a"
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.TryGet("b", out _)).IsTrue();
        await Assert.That(cache.TryGet("c", out _)).IsTrue();
        await Assert.That(cache.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TryGet_RefreshesRecency()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        await Assert.That(cache.TryGet("a", out _)).IsTrue(); // "b" becomes least recent
        cache.Set("c", 3);                     // evicts "b"
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
        await Assert.That(cache.TryGet("b", out _)).IsFalse();
    }

    [Test]
    public async Task Set_SameKey_ReplacesValueWithoutGrowing()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("a", 5);
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.TryGet("a", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(5);
    }

    [Test]
    public async Task Set_SameKey_RefreshesRecency()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("a", 9); // "b" is now least recent
        cache.Set("c", 3); // evicts "b"
        await Assert.That(cache.TryGet("b", out _)).IsFalse();
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
    }

    [Test]
    public async Task Clear_Empties()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Clear();
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
    }

    [Test]
    public async Task Capacity_BelowOne_Throws()
    {
        await Assert.That(() => new LruCache<string, int>(0)).ThrowsExactly<ArgumentOutOfRangeException>();
    }
}
