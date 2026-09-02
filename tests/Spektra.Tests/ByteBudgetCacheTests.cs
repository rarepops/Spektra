using Spektra.Core;

namespace Spektra.Tests;

/// The value IS its size, so a test reads as "insert 4 bytes under key a".
public class ByteBudgetCacheTests
{
    private static ByteBudgetCache<string, long> Cache(long budget) => new(budget, size => size);

    [Test]
    public async Task TryGet_OnEmpty_Misses()
    {
        var cache = Cache(10);
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.Bytes).IsEqualTo(0L);
    }

    [Test]
    public async Task SetThenGet_RoundTrips()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        await Assert.That(cache.TryGet("a", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(4L);
        await Assert.That(cache.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Set_AddsTheEntrySizeToBytes()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 3);
        await Assert.That(cache.Bytes).IsEqualTo(7L);
    }

    [Test]
    public async Task Set_FillingTheBudgetExactly_KeepsEverything()
    {
        var cache = Cache(8);
        cache.Set("a", 4);
        cache.Set("b", 4);
        await Assert.That(cache.Count).IsEqualTo(2);
        await Assert.That(cache.Bytes).IsEqualTo(8L);
    }

    [Test]
    public async Task Set_PastBudget_EvictsLeastRecentlyUsed()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 4);
        cache.Set("c", 4); // 12 > 10: "a" goes
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.TryGet("b", out _)).IsTrue();
        await Assert.That(cache.TryGet("c", out _)).IsTrue();
        await Assert.That(cache.Bytes).IsEqualTo(8L);
    }

    [Test]
    public async Task Set_PastBudget_EvictsAsManyAsItTakes()
    {
        var cache = Cache(10);
        cache.Set("a", 3);
        cache.Set("b", 3);
        cache.Set("c", 3);
        cache.Set("d", 6); // 15 > 10: "a" (12) then "b" (9)
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.TryGet("b", out _)).IsFalse();
        await Assert.That(cache.TryGet("c", out _)).IsTrue();
        await Assert.That(cache.TryGet("d", out _)).IsTrue();
        await Assert.That(cache.Bytes).IsEqualTo(9L);
    }

    [Test]
    public async Task TryGet_RefreshesRecency()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 4);
        await Assert.That(cache.TryGet("a", out _)).IsTrue(); // "b" is now the oldest
        cache.Set("c", 4);
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
        await Assert.That(cache.TryGet("b", out _)).IsFalse();
    }

    [Test]
    public async Task Set_SameKey_ReplacesWithoutGrowingCount()
    {
        var cache = Cache(100);
        cache.Set("a", 4);
        cache.Set("a", 6);
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.TryGet("a", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(6L);
    }

    [Test]
    public async Task Set_SameKey_SwapsTheOldSizeForTheNew()
    {
        var cache = Cache(100);
        cache.Set("a", 4);
        cache.Set("a", 6);
        await Assert.That(cache.Bytes).IsEqualTo(6L);
    }

    [Test]
    public async Task Set_SameKey_RefreshesRecency()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 4);
        cache.Set("a", 4); // "b" is now the oldest
        cache.Set("c", 4);
        await Assert.That(cache.TryGet("b", out _)).IsFalse();
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
    }

    [Test]
    public async Task Set_SameKeyGrowingPastBudget_EvictsOthersNotItself()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 4);
        cache.Set("b", 8); // 12 > 10: "a" goes, the entry being set stays
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.TryGet("b", out var value)).IsTrue();
        await Assert.That(value).IsEqualTo(8L);
    }

    [Test]
    public async Task Set_EntryLargerThanTheWholeBudget_IsNotKept()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("big", 12);
        await Assert.That(cache.TryGet("big", out _)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Set_EntryLargerThanTheWholeBudget_DisturbsNothing()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("big", 12);
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
        await Assert.That(cache.Bytes).IsEqualTo(4L);
    }

    [Test]
    public async Task Set_ReplacingWithAnOversizeValue_DropsTheKey()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("a", 12);
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.Bytes).IsEqualTo(0L);
    }

    [Test]
    public async Task Budget_Zero_KeepsNothing()
    {
        var cache = Cache(0);
        cache.Set("a", 1);
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Budget_Zero_StillAcceptsZeroSizedEntries()
    {
        var cache = Cache(0);
        cache.Set("a", 0);
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
    }

    [Test]
    public async Task Budget_LoweredLive_EvictsImmediately()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 4);
        cache.Budget = 5;
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.TryGet("b", out _)).IsTrue();
        await Assert.That(cache.Bytes).IsEqualTo(4L);
    }

    [Test]
    public async Task Budget_Raised_MakesRoomWithoutTouchingEntries()
    {
        var cache = Cache(4);
        cache.Set("a", 4);
        cache.Budget = 10;
        cache.Set("b", 4);
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
        await Assert.That(cache.TryGet("b", out _)).IsTrue();
        await Assert.That(cache.Budget).IsEqualTo(10L);
    }

    [Test]
    public async Task Remove_DropsTheEntryAndItsBytes()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 3);
        await Assert.That(cache.Remove("a")).IsTrue();
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.Bytes).IsEqualTo(3L);
    }

    [Test]
    public async Task Remove_Missing_ReturnsFalse()
    {
        var cache = Cache(10);
        await Assert.That(cache.Remove("a")).IsFalse();
    }

    [Test]
    public async Task Clear_Empties()
    {
        var cache = Cache(10);
        cache.Set("a", 4);
        cache.Set("b", 4);
        cache.Clear();
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.Bytes).IsEqualTo(0L);
        await Assert.That(cache.TryGet("a", out _)).IsFalse();
    }

    [Test]
    public async Task Budget_Negative_Throws()
    {
        await Assert.That(() => Cache(-1)).ThrowsExactly<ArgumentOutOfRangeException>();
        var cache = Cache(10);
        await Assert.That(() => cache.Budget = -1).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Set_NegativeSize_Throws()
    {
        var cache = Cache(10);
        await Assert.That(() => cache.Set("a", -1)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SizeOf_IsMeasuredOnceAtSet_NotOnGet()
    {
        var calls = 0;
        var cache = new ByteBudgetCache<string, long>(10, size => { calls++; return size; });
        cache.Set("a", 4);
        cache.TryGet("a", out _);
        cache.TryGet("a", out _);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task Keys_UseTheGivenComparer()
    {
        var cache = new ByteBudgetCache<string, long>(10, size => size, StringComparer.OrdinalIgnoreCase);
        cache.Set("A", 4);
        await Assert.That(cache.TryGet("a", out _)).IsTrue();
        cache.Set("a", 4);
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.Remove("A")).IsTrue();
        await Assert.That(cache.Count).IsEqualTo(0);
    }
}
