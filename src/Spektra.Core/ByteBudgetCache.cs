using System.Diagnostics.CodeAnalysis;

namespace Spektra.Core;

/// LRU map bounded by bytes rather than by entry count: the sibling of
/// LruCache for values whose sizes differ (a spectrogram at FFT 8192 is
/// sixteen times one at 2048). Each value is measured once, when set, by
/// the sizer given at construction; Set and a lowered Budget evict the
/// least-recently-used entries until Bytes <= Budget. A value that alone
/// exceeds the budget is not kept and disturbs nothing (setting it under an
/// existing key drops that key), and Budget 0 keeps only zero-sized values,
/// which is how "off" reads. Not thread-safe: callers confine an instance
/// to one thread.
public sealed class ByteBudgetCache<TKey, TValue> where TKey : notnull
{
    private readonly record struct Entry(TKey Key, TValue Value, long Size);

    private readonly Func<TValue, long> _sizeOf;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _order = new(); // first = most recent
    private long _budget;

    public ByteBudgetCache(long budgetBytes, Func<TValue, long> sizeOf, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        _budget = budgetBytes;
        _sizeOf = sizeOf;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    /// Lowering it evicts at once; raising it only makes room.
    public long Budget
    {
        get => _budget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _budget = value;
            EvictToFit();
        }
    }

    public long Bytes { get; private set; }
    public int Count => _map.Count;

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_map.TryGetValue(key, out var node)) { value = default; return false; }
        _order.Remove(node);
        _order.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        var size = _sizeOf(value);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Remove(key);
        if (size > _budget) return;
        _map[key] = _order.AddFirst(new Entry(key, value, size));
        Bytes += size;
        EvictToFit();
    }

    public bool Remove(TKey key)
    {
        if (!_map.Remove(key, out var node)) return false;
        _order.Remove(node);
        Bytes -= node.Value.Size;
        return true;
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
        Bytes = 0;
    }

    /// The entry just set is first in the order, so it is the last thing
    /// this would reach; it only goes when it alone is over budget, and Set
    /// refuses that case before inserting.
    private void EvictToFit()
    {
        while (Bytes > _budget && _order.Last is { } oldest)
        {
            _order.RemoveLast();
            _map.Remove(oldest.Value.Key);
            Bytes -= oldest.Value.Size;
        }
    }
}
