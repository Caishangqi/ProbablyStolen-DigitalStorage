using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// Geometry of strip pages: one row of <see cref="SlotsPerPage"/> slots, all of one bounding size, so a page for a
/// <c>w×h</c> item is <c>(w·N)×h</c>. There is no separate page width limit; the widest page is
/// <c>MaxItemSide·N</c>.
/// </summary>
public static class StripLayout
{
    /// <summary>The fixed slot count N of a new strip page.</summary>
    public const int SlotsPerPage = 16;

    /// <summary>The largest item side a strip page accepts.</summary>
    public const int MaxItemSide = 12;

    /// <summary>Gets whether an item of this bounding size can be stored on a strip page.</summary>
    public static bool Fits(BoundingSize size) =>
        size.Width >= 1 && size.Height >= 1 && size.Width <= MaxItemSide && size.Height <= MaxItemSide;

    /// <summary>Gets the geometry of a new strip page for one bounding size.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The size does not <see cref="Fits"/>.</exception>
    public static PageGeometry PageFor(BoundingSize size) => PageGeometry.Strip(size, SlotsPerPage);

    /// <summary>Gets the top-left grid position of a strip slot.</summary>
    /// <exception cref="ArgumentException">The page is not a strip page.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The slot is outside the page.</exception>
    public static (int X, int Y) SlotOrigin(PageGeometry page, int slot)
    {
        RequireStrip(page);
        if (slot < 0 || slot >= page.SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "The slot is outside the page.");
        }

        return (checked(slot * page.SlotSize.Width), 0);
    }

    /// <summary>Gets the strip slot whose origin is at a grid position, or -1 when no slot starts there.</summary>
    /// <exception cref="ArgumentException">The page is not a strip page.</exception>
    public static int SlotIndexAt(PageGeometry page, int x, int y)
    {
        RequireStrip(page);
        if (x < 0 || y != 0 || x % page.SlotSize.Width != 0)
        {
            return -1;
        }

        var slot = x / page.SlotSize.Width;
        return slot < page.SlotCount ? slot : -1;
    }

    private static void RequireStrip(PageGeometry page)
    {
        if (page.Kind != PageKind.Strip)
        {
            throw new ArgumentException("Only strip pages have slots.", nameof(page));
        }
    }
}
