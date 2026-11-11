using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Immutable partition list matched by exact identity.</summary>
public sealed class PrecisePriorityList : IPartitionList
{
    private readonly IReadOnlyList<ItemKey> _items;
    private readonly HashSet<ItemKey> _lookup;

    /// <summary>Creates a list from unique, non-null identities; order is kept.</summary>
    /// <exception cref="ArgumentException">An identity is null or repeated.</exception>
    public PrecisePriorityList(IEnumerable<ItemKey> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        var ordered = new List<ItemKey>();
        _lookup = new HashSet<ItemKey>();
        foreach (var key in items)
        {
            if (key == null) throw new ArgumentException("A listed identity cannot be null.", nameof(items));
            if (!_lookup.Add(key)) throw new ArgumentException("A listed identity cannot repeat.", nameof(items));
            ordered.Add(key);
        }

        _items = ordered.AsReadOnly();
    }

    /// <summary>Gets the list with no identities.</summary>
    public static PrecisePriorityList Empty { get; } = new(Array.Empty<ItemKey>());

    /// <inheritdoc />
    public bool IsListed(ItemKey key) => key != null && _lookup.Contains(key);

    /// <inheritdoc />
    public bool IsEmpty() => _items.Count == 0;

    /// <inheritdoc />
    public IReadOnlyList<ItemKey> GetItems() => _items;
}
