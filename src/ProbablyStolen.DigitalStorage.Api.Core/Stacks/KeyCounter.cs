using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Api.Stacks;

/// <summary>Counts immutable item keys without owning physical items.</summary>
public sealed class KeyCounter
{
    private readonly Dictionary<ItemKey, long> _amounts = new();

    /// <summary>Adds a non-negative amount to a key.</summary>
    public void Add(ItemKey key, long amount)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (amount == 0)
        {
            return;
        }

        _amounts[key] = _amounts.TryGetValue(key, out var current) ? checked(current + amount) : amount;
    }

    /// <summary>Gets the amount for a key, or zero when absent.</summary>
    public long Get(ItemKey key) => key != null && _amounts.TryGetValue(key, out var amount) ? amount : 0;

    /// <summary>Gets a read-only snapshot of all positive amounts.</summary>
    public IReadOnlyDictionary<ItemKey, long> Snapshot() => new Dictionary<ItemKey, long>(_amounts);

    /// <summary>Gets the number of keys with positive amounts.</summary>
    public int Size() => _amounts.Count;

    /// <summary>Clears this counter.</summary>
    public void Clear() => _amounts.Clear();
}
