namespace SmartX.Api.Collections;

/// <summary>
/// A small GENERIC collection that remembers only the most recent <c>capacity</c> items — used for the
/// alert feed, the processing log and the user-action history so memory never grows without bound.
/// (Not thread-safe by itself; the owners lock around it.)
/// </summary>
public class BoundedLog<T>
{
    private readonly List<T> _items = new();
    private readonly int _capacity;

    public BoundedLog(int capacity) => _capacity = Math.Max(1, capacity);

    public int Count => _items.Count;

    public void Add(T item)
    {
        _items.Add(item);
        if (_items.Count > _capacity) _items.RemoveAt(0);
    }

    /// <summary>The newest <paramref name="take"/> items, NEWEST FIRST.</summary>
    public List<T> Latest(int take)
    {
        var result = new List<T>(Math.Min(take, _items.Count));
        for (int i = _items.Count - 1; i >= 0 && result.Count < take; i--)
            result.Add(_items[i]);
        return result;
    }
}
