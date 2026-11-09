using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Why an item cannot be placed in a cell.</summary>
public enum PlacementRejection
{
    /// <summary>The item would be a new type beyond the cell's type limit.</summary>
    TypeLimit = 0,

    /// <summary>The item's bounding size does not fit on a page.</summary>
    TooLarge = 1,

    /// <summary>No slot is free and no page can be added.</summary>
    NoSpace = 2
}

/// <summary>The outcome of planning one item: a page slot, possibly on a page to append, or a rejection.</summary>
public readonly struct Placement
{
    private Placement(int page, int slot, bool appendsPage, PlacementRejection? rejection)
    {
        Page = page;
        Slot = slot;
        AppendsPage = appendsPage;
        Rejection = rejection;
    }

    /// <summary>Gets whether the item can be placed.</summary>
    public bool IsAccepted => Rejection == null;

    /// <summary>Gets the target page index; when <see cref="AppendsPage"/> it equals the current page count.</summary>
    public int Page { get; }

    /// <summary>Gets the target slot on the page.</summary>
    public int Slot { get; }

    /// <summary>Gets whether a new page must be appended first.</summary>
    public bool AppendsPage { get; }

    /// <summary>Gets the rejection reason, or null when accepted.</summary>
    public PlacementRejection? Rejection { get; }

    internal static Placement At(int page, int slot) => new(page, slot, false, null);

    internal static Placement OnNewPage(int page) => new(page, 0, true, null);

    internal static Placement Reject(PlacementRejection reason) => new(-1, -1, false, reason);
}

/// <summary>
/// Plans where one item goes inside one cell. Same-size items share a page chain; a full chain reuses a free
/// page before a page is appended, up to the tier limit. Partition matching is the router's job.
/// </summary>
public static class CellPlacementPlanner
{
    /// <summary>Plans one item.</summary>
    public static Placement Plan(CellView cell, ItemFootprint item)
    {
        if (cell == null)
        {
            throw new ArgumentNullException(nameof(cell));
        }

        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        var layout = cell.Layout;
        if (!cell.Contains(item.Key) && cell.TypeCount >= layout.MaxTypes)
        {
            return Placement.Reject(PlacementRejection.TypeLimit);
        }

        var size = item.Size;
        var slots = layout.SlotCount(size.Width, size.Height);
        if (slots == 0)
        {
            return Placement.Reject(PlacementRejection.TooLarge);
        }

        for (var page = 0; page < cell.Pages.Count; page++)
        {
            var view = cell.Pages[page];
            if (view.Label?.Equals(size) != true)
            {
                continue;
            }

            for (var slot = 0; slot < slots; slot++)
            {
                if (!view.IsOccupied(slot))
                {
                    return Placement.At(page, slot);
                }
            }
        }

        for (var page = 0; page < cell.Pages.Count; page++)
        {
            if (cell.Pages[page].IsFree)
            {
                return Placement.At(page, 0);
            }
        }

        return cell.Pages.Count < layout.MaxPages
            ? Placement.OnNewPage(cell.Pages.Count)
            : Placement.Reject(PlacementRejection.NoSpace);
    }
}
