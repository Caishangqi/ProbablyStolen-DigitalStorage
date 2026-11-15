using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Stacks;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Stored units and area units of one item type.</summary>
public readonly struct TypeUsage
{
    /// <summary>Creates a usage entry.</summary>
    public TypeUsage(long units, long areaUnits, long pieces)
    {
        Units = units;
        AreaUnits = areaUnits;
        Pieces = pieces;
    }

    /// <summary>Gets the stored native units.</summary>
    public long Units { get; }

    /// <summary>Gets the stored area units: units * bounding width * bounding height.</summary>
    public long AreaUnits { get; }

    /// <summary>Gets the stored roots, each one physical item whatever its unit count.</summary>
    public long Pieces { get; }
}

/// <summary>Per-type and total usage of one cell; updated in place while one operation runs.</summary>
public sealed class CellUsage
{
    private readonly Dictionary<ItemKey, TypeUsage> _types = new();

    /// <summary>Gets the area units of an item root: <c>units * w * h</c>, checked.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The units are negative.</exception>
    public static long AreaOf(BoundingSize size, long units)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units), units, "Units cannot be negative.");
        return checked(units * size.Width * size.Height);
    }

    /// <summary>Gets the stored types and their usage.</summary>
    public IReadOnlyDictionary<ItemKey, TypeUsage> Types => _types;

    /// <summary>Gets the number of stored types.</summary>
    public int TypeCount => _types.Count;

    /// <summary>Gets the total area units of the cell.</summary>
    public long AreaUnits { get; private set; }

    /// <summary>Gets whether a type is stored.</summary>
    public bool Contains(ItemKey key) => key != null && _types.ContainsKey(key);

    /// <summary>Gets the usage of a type, or zero when it is not stored.</summary>
    public TypeUsage Get(ItemKey key) => key != null && _types.TryGetValue(key, out var usage) ? usage : default;

    /// <summary>Gets an independent copy.</summary>
    public CellUsage Clone()
    {
        var copy = new CellUsage { AreaUnits = AreaUnits };
        foreach (var entry in _types) copy._types.Add(entry.Key, entry.Value);
        return copy;
    }

    /// <summary>Records an item root entering the cell.</summary>
    /// <exception cref="ArgumentException">The root is empty.</exception>
    public void Add(ItemKey key, long units, long areaUnits)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (units <= 0 || areaUnits <= 0) throw new ArgumentException("A stored root has positive units and area.");
        var current = Get(key);
        var total = checked(AreaUnits + areaUnits);
        _types[key] = new TypeUsage(checked(current.Units + units), checked(current.AreaUnits + areaUnits),
            checked(current.Pieces + 1));
        AreaUnits = total;
    }

    /// <summary>Records one whole root leaving the cell; the type stops counting once its last units leave.</summary>
    /// <exception cref="InvalidOperationException">More than the stored amount would be removed.</exception>
    public void Remove(ItemKey key, long units, long areaUnits)
    {
        var current = Require(key, units, areaUnits);
        if (current.Units == units)
        {
            if (current.AreaUnits != areaUnits)
                throw new InvalidOperationException("The last units of a type must carry its remaining area.");
            _types.Remove(key);
        }
        else
        {
            // A type with units left still has at least one root, even if earlier counts came from older layouts.
            _types[key] = new TypeUsage(current.Units - units, current.AreaUnits - areaUnits,
                Math.Max(1, current.Pieces - 1));
        }

        AreaUnits -= areaUnits;
    }

    /// <summary>Records units leaving a root that stays stored, so the root count does not change.</summary>
    public void RemoveUnits(ItemKey key, long units, long areaUnits)
    {
        var current = Require(key, units, areaUnits);
        if (current.Units == units)
            throw new InvalidOperationException("A root that stays stored keeps at least one unit.");
        _types[key] = new TypeUsage(current.Units - units, current.AreaUnits - areaUnits, current.Pieces);
        AreaUnits -= areaUnits;
    }

    private TypeUsage Require(ItemKey key, long units, long areaUnits)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (units <= 0 || areaUnits <= 0) throw new ArgumentException("A stored root has positive units and area.");
        if (!_types.TryGetValue(key, out var current) || current.Units < units || current.AreaUnits < areaUnits)
            throw new InvalidOperationException("The cell does not hold that many units of the type.");
        return current;
    }
}
