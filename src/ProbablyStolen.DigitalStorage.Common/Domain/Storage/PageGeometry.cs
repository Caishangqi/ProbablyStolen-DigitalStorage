using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Role of one native page in a cell's layout descriptor.</summary>
public enum PageKind
{
    /// <summary>The factory-created first page; always kept empty.</summary>
    Anchor = 0,

    /// <summary>A schema 1 page awaiting migration.</summary>
    Legacy = 1,

    /// <summary>A single-row strip whose slots all share one bounding size.</summary>
    Strip = 2
}

/// <summary>Size and slot shape of one native page.</summary>
public readonly struct PageGeometry : IEquatable<PageGeometry>
{
    /// <summary>The side of the factory page and of schema 1 pages.</summary>
    public const int SquareSide = 12;

    private PageGeometry(PageKind kind, int width, int height, BoundingSize slotSize, int slotCount)
    {
        Kind = kind;
        Width = width;
        Height = height;
        SlotSize = slotSize;
        SlotCount = slotCount;
    }

    /// <summary>Gets the anchor page geometry.</summary>
    public static PageGeometry Anchor { get; } = new(PageKind.Anchor, SquareSide, SquareSide, default, 0);

    /// <summary>Gets the schema 1 page geometry.</summary>
    public static PageGeometry Legacy { get; } = new(PageKind.Legacy, SquareSide, SquareSide, default, 0);

    /// <summary>Creates a strip page of <paramref name="slotCount"/> slots of one bounding size.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The slot size or count is outside the strip limits.</exception>
    public static PageGeometry Strip(BoundingSize slotSize, int slotCount)
    {
        if (!StripLayout.Fits(slotSize))
            throw new ArgumentOutOfRangeException(nameof(slotSize), slotSize, "The slot size is outside the strip limits.");
        if (slotCount < 1 || slotCount > StripLayout.SlotsPerPage)
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "The slot count is outside the strip limits.");
        return new PageGeometry(PageKind.Strip, checked(slotSize.Width * slotCount), slotSize.Height, slotSize, slotCount);
    }

    /// <summary>Gets the page role.</summary>
    public PageKind Kind { get; }

    /// <summary>Gets the page width in grid cells.</summary>
    public int Width { get; }

    /// <summary>Gets the page height in grid cells.</summary>
    public int Height { get; }

    /// <summary>Gets the slot bounding size of a strip page; default for anchor and legacy pages.</summary>
    public BoundingSize SlotSize { get; }

    /// <summary>Gets the slot count of a strip page; zero for anchor and legacy pages.</summary>
    public int SlotCount { get; }

    /// <inheritdoc />
    public bool Equals(PageGeometry other) => Kind == other.Kind && Width == other.Width && Height == other.Height &&
        SlotSize.Equals(other.SlotSize) && SlotCount == other.SlotCount;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PageGeometry other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Width, Height, SlotSize, SlotCount);

    /// <inheritdoc />
    public override string ToString() => Kind == PageKind.Strip ? $"S{SlotSize}x{SlotCount}" : Kind.ToString();
}
