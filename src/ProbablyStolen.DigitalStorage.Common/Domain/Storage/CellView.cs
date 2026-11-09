using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>The bounding width and height of an item in its stored orientation.</summary>
public readonly struct BoundingSize : IEquatable<BoundingSize>
{
    /// <summary>Creates a bounding size.</summary>
    public BoundingSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Gets the width in grid cells.</summary>
    public int Width { get; }

    /// <summary>Gets the height in grid cells.</summary>
    public int Height { get; }

    /// <inheritdoc />
    public bool Equals(BoundingSize other) => Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BoundingSize other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    /// <inheritdoc />
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>An item to place: its stored-orientation bounding size and type.</summary>
public sealed class ItemFootprint
{
    /// <summary>Creates a footprint.</summary>
    public ItemFootprint(BoundingSize size, ItemKey key)
    {
        Size = size;
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    /// <summary>Gets the bounding size.</summary>
    public BoundingSize Size { get; }

    /// <summary>Gets the item type.</summary>
    public ItemKey Key { get; }
}

/// <summary>A read-only projection of one page: its size label (none when empty) and occupied slots.</summary>
public sealed class PageView
{
    private readonly HashSet<int> _occupied;

    /// <summary>Creates a page projection.</summary>
    public PageView(BoundingSize? label, IEnumerable<int> occupiedSlots)
    {
        _occupied = new HashSet<int>(occupiedSlots ?? throw new ArgumentNullException(nameof(occupiedSlots)));
        if (label == null && _occupied.Count != 0)
        {
            throw new ArgumentException("A page without a size label cannot have occupied slots.");
        }

        Label = _occupied.Count == 0 ? null : label;
    }

    /// <summary>An empty page.</summary>
    public static PageView Free => new(null, Array.Empty<int>());

    /// <summary>Gets the bounding size this page holds, or null when the page is free.</summary>
    public BoundingSize? Label { get; }

    /// <summary>Gets whether no item is on this page.</summary>
    public bool IsFree => Label == null;

    /// <summary>Gets whether a slot is occupied.</summary>
    public bool IsOccupied(int slot) => _occupied.Contains(slot);
}

/// <summary>A read-only projection of one storage cell, built fresh for each operation.</summary>
public sealed class CellView
{
    private readonly HashSet<ItemKey> _types;

    /// <summary>Creates a cell projection.</summary>
    public CellView(CellLayout layout, IEnumerable<PageView> pages, IEnumerable<ItemKey> storedTypes,
        CellPartition partition, int priority = DrivePriority.Default)
    {
        Priority = DrivePriority.Clamp(priority);
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        Pages = (pages ?? throw new ArgumentNullException(nameof(pages))).ToArray();
        _types = new HashSet<ItemKey>(storedTypes ?? throw new ArgumentNullException(nameof(storedTypes)));
        // A cell can hold more pages than the current limit after the limit was lowered. Those pages stay usable;
        // the planner only refuses to add another one.
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
    }

    /// <summary>Gets the tier layout.</summary>
    public CellLayout Layout { get; }

    /// <summary>Gets the pages in page order.</summary>
    public IReadOnlyList<PageView> Pages { get; }

    /// <summary>Gets the number of distinct stored types.</summary>
    public int TypeCount => _types.Count;

    /// <summary>Gets the partition filter.</summary>
    public CellPartition Partition { get; }

    /// <summary>Gets the priority of the drive the cell is mounted in.</summary>
    public int Priority { get; }

    /// <summary>Gets whether this cell already stores a type.</summary>
    public bool Contains(ItemKey key) => _types.Contains(key);
}
