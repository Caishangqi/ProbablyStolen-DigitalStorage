using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Page geometry and limits of one storage cell tier, plus the slot arithmetic used by page chains.
/// A page only holds items of one bounding size; slot <c>i</c> of a <c>w×h</c> chain sits at
/// <c>((i mod ⌊W/w⌋)·w, (i div ⌊W/w⌋)·h)</c>.
/// </summary>
public sealed class CellLayout
{
    /// <summary>The 1k tier: 12×12 pages, at most 16 pages and 63 item types.</summary>
    public static readonly CellLayout Cell1K = new(12, 12, 16);

    /// <summary>The largest recorded page count supported during restoration.</summary>
    public const int AbsoluteMaxPages = 32768;

    /// <summary>Gets whether a recorded page count can be restored without truncation.</summary>
    public static bool IsValidRecordedPageCount(int count) => count >= 1 && count <= AbsoluteMaxPages;

    /// <summary>The largest configurable 1k base page count.</summary>
    public const int MaxBasePages = 128;

    /// <summary>The fixed limit for distinct item types.</summary>
    public const int TypeLimit = 63;

    /// <summary>Creates a tier layout.</summary>
    public CellLayout(int pageWidth, int pageHeight, int maxPages)
    {
        PageWidth = pageWidth > 0 ? pageWidth : throw new ArgumentOutOfRangeException(nameof(pageWidth));
        PageHeight = pageHeight > 0 ? pageHeight : throw new ArgumentOutOfRangeException(nameof(pageHeight));
        MaxPages = maxPages > 0 ? maxPages : throw new ArgumentOutOfRangeException(nameof(maxPages));
    }

    /// <summary>Gets a tier layout from the configured 1k base, clamped before applying its multiplier.</summary>
    public static CellLayout ForTier(StorageCellTier tier, int basePages)
    {
        if (tier == null) throw new ArgumentNullException(nameof(tier));
        return new CellLayout(12, 12, Math.Max(1, Math.Min(basePages, MaxBasePages)) * tier.Multiplier);
    }

    /// <summary>Gets the 1k layout with a configured base page limit.</summary>
    public CellLayout WithLimits(int maxPages) => new(PageWidth, PageHeight,
        Math.Max(1, Math.Min(maxPages, MaxBasePages)));

    /// <summary>Gets the page width in grid cells.</summary>
    public int PageWidth { get; }

    /// <summary>Gets the page height in grid cells.</summary>
    public int PageHeight { get; }

    /// <summary>Gets the maximum number of pages one cell may hold.</summary>
    public int MaxPages { get; }

    /// <summary>Gets the maximum number of distinct item types one cell may hold.</summary>
    public int MaxTypes => TypeLimit;

    /// <summary>Gets the grid cells of one page.</summary>
    public int CellsPerPage => PageWidth * PageHeight;

    /// <summary>Gets the grid cells of a cell at its page limit.</summary>
    public int TotalCells => CellsPerPage * MaxPages;

    /// <summary>Gets whether an item with this bounding size fits on one page without rotation.</summary>
    public bool Fits(int width, int height) =>
        width >= 1 && height >= 1 && width <= PageWidth && height <= PageHeight;

    /// <summary>Gets the number of slots a page offers to one bounding size, or zero when it does not fit.</summary>
    public int SlotCount(int width, int height) =>
        Fits(width, height) ? PageWidth / width * (PageHeight / height) : 0;

    /// <summary>Gets the top-left grid position of a slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size does not fit or the index is outside the page.</exception>
    public (int X, int Y) SlotOrigin(int index, int width, int height)
    {
        if (index < 0 || index >= SlotCount(width, height))
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "The slot is outside the page.");
        }

        var columns = PageWidth / width;
        return (index % columns * width, index / columns * height);
    }

    /// <summary>Gets the slot whose origin is at a grid position, or -1 when the position is not a slot origin.</summary>
    public int SlotIndexAt(int x, int y, int width, int height)
    {
        if (!Fits(width, height) || x < 0 || y < 0 || x % width != 0 || y % height != 0)
        {
            return -1;
        }

        var columns = PageWidth / width;
        var column = x / width;
        var row = y / height;
        var index = row * columns + column;
        return column < columns && index < SlotCount(width, height) ? index : -1;
    }
}
