using System.Diagnostics.CodeAnalysis;

namespace Spektra.Core;

/// Tiny generic LRU map. TryGet refreshes recency; Set inserts or replaces
/// (refreshing recency), evicting the least-recently-used entry once capacity
/// is exceeded. Not thread-safe: callers confine an instance to one thread.
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new(); // first = most recent

    public LruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
    }

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
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            node.Value = new KeyValuePair<TKey, TValue>(key, value);
            _order.AddFirst(node);
            return;
        }
        if (_map.Count == _capacity && _order.Last is { } evict)
        {
            _order.RemoveLast();
            _map.Remove(evict.Value.Key);
        }
        _map[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
