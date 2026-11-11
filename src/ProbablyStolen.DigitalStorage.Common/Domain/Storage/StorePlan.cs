using System;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Why a cell refuses an item root.</summary>
public enum StoreRejection
{
    /// <summary>Migration was refused, so the cell only releases items.</summary>
    ReadOnly = 0,

    /// <summary>The bounding size exceeds the largest strip slot.</summary>
    TooLarge = 1,

    /// <summary>The root would be a new type beyond the type limit.</summary>
    TypeLimit = 2,

    /// <summary>The root would exceed the byte capacity.</summary>
    Capacity = 3,

    /// <summary>The root would exceed its type's equal share.</summary>
    DistributionQuota = 4,

    /// <summary>No strip slot is free and no page may be appended.</summary>
    PhysicalLimit = 5,

    /// <summary>The partition excludes the type.</summary>
    PartitionRejected = 6
}

/// <summary>Which rules apply to a plan.</summary>
public enum PlanScope
{
    /// <summary>The root enters the cell from outside; every rule applies.</summary>
    Insert = 0,

    /// <summary>
    /// The root already belongs to the cell and only changes position; types, area units and the partition do not
    /// change, so only the size and physical placement are checked, up to the restorable page limit.
    /// </summary>
    Relocate = 1
}

/// <summary>One item root to plan: its stored-orientation bounding size, type and native units.</summary>
public readonly struct StoreCandidate
{
    /// <summary>Creates a candidate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The units are not positive.</exception>
    public StoreCandidate(BoundingSize size, ItemKey key, long units)
    {
        Size = size;
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Units = units > 0 ? units : throw new ArgumentOutOfRangeException(nameof(units), units, "A root has positive units.");
    }

    /// <summary>Gets the bounding size.</summary>
    public BoundingSize Size { get; }

    /// <summary>Gets the item type.</summary>
    public ItemKey Key { get; }

    /// <summary>Gets the native unit count of the whole root.</summary>
    public long Units { get; }
}

/// <summary>The outcome of planning one root: a strip slot, possibly on a page to append, or a rejection.</summary>
public readonly struct StorePlan
{
    private StorePlan(int page, int slot, PageGeometry? newPage, StoreRejection? rejection)
    {
        Page = page;
        Slot = slot;
        NewPage = newPage;
        Rejection = rejection;
    }

    /// <summary>Gets whether the root can be stored.</summary>
    public bool IsAccepted => Rejection == null;

    /// <summary>Gets the target page; when a page is appended it equals the current page count.</summary>
    public int Page { get; }

    /// <summary>Gets the target slot.</summary>
    public int Slot { get; }

    /// <summary>Gets the geometry of the page to append first, or null when an existing page is used.</summary>
    public PageGeometry? NewPage { get; }

    /// <summary>Gets the rejection, or null when accepted.</summary>
    public StoreRejection? Rejection { get; }

    internal static StorePlan At(int page, int slot) => new(page, slot, null, null);

    internal static StorePlan OnNewPage(int page, PageGeometry geometry) => new(page, 0, geometry, null);

    internal static StorePlan Reject(StoreRejection reason) => new(-1, -1, null, reason);
}
