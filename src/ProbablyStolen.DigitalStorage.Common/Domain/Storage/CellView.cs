using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage.Upgrades;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>Whether a cell accepts new items.</summary>
public enum CellWriteState
{
    /// <summary>The cell accepts and releases items.</summary>
    Writable = 0,

    /// <summary>Migration was refused; the cell only releases items.</summary>
    ExtractOnly = 1
}

/// <summary>Projection of one page: its geometry and which roots it holds.</summary>
/// <remarks>Strip pages track occupied slots; anchor and legacy pages only count their roots.</remarks>
public sealed class PageView
{
    private readonly bool[]? _occupied;

    private PageView(PageGeometry geometry, bool[]? occupied, int rootCount)
    {
        Geometry = geometry;
        _occupied = occupied;
        RootCount = rootCount;
    }

    /// <summary>Creates a strip page projection.</summary>
    /// <exception cref="ArgumentException">The geometry is not a strip page, or a slot is outside it or repeated.</exception>
    public static PageView Strip(PageGeometry geometry, IEnumerable<int> occupiedSlots)
    {
        if (geometry.Kind != PageKind.Strip) throw new ArgumentException("Only strip pages have slots.", nameof(geometry));
        if (occupiedSlots == null) throw new ArgumentNullException(nameof(occupiedSlots));
        var occupied = new bool[geometry.SlotCount];
        var count = 0;
        foreach (var slot in occupiedSlots)
        {
            if (slot < 0 || slot >= occupied.Length || occupied[slot])
                throw new ArgumentException($"Slot {slot} is outside the page or repeated.", nameof(occupiedSlots));
            occupied[slot] = true;
            count++;
        }

        return new PageView(geometry, occupied, count);
    }

    /// <summary>Creates an anchor or legacy page projection holding <paramref name="rootCount"/> roots.</summary>
    /// <exception cref="ArgumentException">The geometry is a strip page or the count is negative.</exception>
    public static PageView Unslotted(PageGeometry geometry, int rootCount)
    {
        if (geometry.Kind == PageKind.Strip) throw new ArgumentException("Strip pages track slots.", nameof(geometry));
        if (rootCount < 0) throw new ArgumentException("A root count cannot be negative.", nameof(rootCount));
        return new PageView(geometry, null, rootCount);
    }

    /// <summary>Gets the page geometry.</summary>
    public PageGeometry Geometry { get; }

    /// <summary>Gets the page role.</summary>
    public PageKind Kind => Geometry.Kind;

    /// <summary>Gets the number of item roots on the page.</summary>
    public int RootCount { get; private set; }

    /// <summary>Gets whether the page holds no item root.</summary>
    public bool IsEmpty => RootCount == 0;

    /// <summary>Gets whether a strip slot is occupied; always false for anchor and legacy pages.</summary>
    public bool IsOccupied(int slot) => _occupied != null && slot >= 0 && slot < _occupied.Length && _occupied[slot];

    /// <summary>Gets the lowest free strip slot, or -1 when the page is full or not a strip page.</summary>
    public int FirstFreeSlot()
    {
        if (_occupied == null) return -1;
        for (var slot = 0; slot < _occupied.Length; slot++)
            if (!_occupied[slot]) return slot;
        return -1;
    }

    internal PageView Clone() => new(Geometry, (bool[]?)_occupied?.Clone(), RootCount);

    internal void Occupy(int slot)
    {
        if (_occupied == null || slot < 0 || slot >= _occupied.Length || _occupied[slot])
            throw new InvalidOperationException($"Slot {slot} is not free on this page.");
        _occupied[slot] = true;
        RootCount++;
    }

    internal void Release(int slot)
    {
        if (_occupied == null)
        {
            if (slot != -1 || RootCount == 0) throw new InvalidOperationException("The page holds no such root.");
        }
        else
        {
            if (slot < 0 || slot >= _occupied.Length || !_occupied[slot])
                throw new InvalidOperationException($"Slot {slot} is not occupied on this page.");
            _occupied[slot] = false;
        }

        RootCount--;
    }
}

/// <summary>
/// Projection of one storage cell for one operation. It is built from the live pages and updated in place as
/// roots move during that operation; it is never kept across frames.
/// </summary>
public sealed class CellView
{
    private readonly List<PageView> _pages;
    private readonly Dictionary<BoundingSize, List<int>> _chains = new();

    /// <summary>Creates a cell projection.</summary>
    /// <exception cref="ArgumentException">The pages do not start with the anchor or the type limit is not positive.</exception>
    public CellView(CellRules rules, int typeLimit, IEnumerable<PageView> pages, CellUsage usage,
        CellPartition partition, int priority = DrivePriority.Default, CellWriteState writeState = CellWriteState.Writable,
        IUpgradeInventory? upgrades = null)
    {
        Upgrades = upgrades ?? UpgradeInventory.Empty;
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        if (typeLimit < 1) throw new ArgumentException("A type limit is positive.", nameof(typeLimit));
        TypeLimit = typeLimit;
        _pages = (pages ?? throw new ArgumentNullException(nameof(pages))).ToList();
        if (_pages.Count == 0 || _pages.Any(page => page == null) || _pages[0].Kind != PageKind.Anchor ||
            _pages.Skip(1).Any(page => page.Kind == PageKind.Anchor))
            throw new ArgumentException("Page 0, and only page 0, is the anchor.", nameof(pages));
        for (var index = 0; index < _pages.Count; index++) Index(index);
        Usage = usage ?? throw new ArgumentNullException(nameof(usage));
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        Priority = DrivePriority.Clamp(priority);
        WriteState = writeState;
    }

    /// <summary>Gets the tier page rules.</summary>
    public CellRules Rules { get; }

    /// <summary>Gets the effective type limit.</summary>
    public int TypeLimit { get; }

    /// <summary>Gets the pages in native page order.</summary>
    public IReadOnlyList<PageView> Pages => _pages;

    /// <summary>Gets the stored units and area units.</summary>
    public CellUsage Usage { get; }

    /// <summary>Gets the partition filter.</summary>
    public CellPartition Partition { get; }

    /// <summary>Gets the installed modules.</summary>
    public IUpgradeInventory Upgrades { get; }

    /// <summary>Gets whether an equal distribution module limits each type to an equal share.</summary>
    public bool EqualDistribution => Upgrades.IsInstalled(CellModuleKind.EqualDistribution);

    /// <summary>Gets the priority of the drive the cell is mounted in.</summary>
    public int Priority { get; }

    /// <summary>Gets whether the cell accepts new items.</summary>
    public CellWriteState WriteState { get; }

    /// <summary>Gets the number of stored types.</summary>
    public int TypeCount => Usage.TypeCount;

    /// <summary>Gets whether this cell stores a type.</summary>
    public bool Contains(ItemKey key) => Usage.Contains(key);

    /// <summary>Area units billed per byte.</summary>
    public const int AreaUnitsPerByte = 8;

    /// <summary>Gets the bytes stored types cost: <c>ceil(types * typeBudget / typeLimit)</c>.</summary>
    public static long TypeBytes(long types, long typeBudget, int typeLimit)
    {
        if (types < 0 || typeBudget < 0) throw new ArgumentOutOfRangeException(nameof(types));
        if (typeLimit < 1) throw new ArgumentOutOfRangeException(nameof(typeLimit), typeLimit, "A type limit is positive.");
        return checked((types * typeBudget + typeLimit - 1) / typeLimit);
    }

    /// <summary>
    /// Gets used bytes: <c>ceil(types * typeBudget / typeLimit) + ceil(areaUnits / 8)</c>. At the base limit of 128 this equals
    /// the former <c>types * bytesPerType + ceil(areaUnits / 8)</c> exactly.
    /// </summary>
    public static long UsedBytes(int types, long areaUnits, long typeBudget, int typeLimit)
    {
        if (areaUnits < 0) throw new ArgumentOutOfRangeException(nameof(areaUnits));
        return checked(TypeBytes(types, typeBudget, typeLimit) + (areaUnits + AreaUnitsPerByte - 1) / AreaUnitsPerByte);
    }

    /// <summary>Gets the bytes this cell uses now.</summary>
    public long UsedBytes() => UsedBytes(TypeCount, Usage.AreaUnits, Rules.Tier.GetTypeBudget(), TypeLimit);

    /// <summary>
    /// Gets the area units one type may hold under equal distribution: the bytes left once the limit's worth of types
    /// is paid for, shared over the effective type limit, <c>ceil((totalBytes - typeBudget) * 8 / limit)</c>, at least 0.
    /// The share follows the type limit, not the rule count, so one rule matching many types does not widen it.
    /// </summary>
    public long EqualShareQuota()
    {
        long shares = TypeLimit;
        var free = checked(Rules.Tier.GetBytes() - TypeBytes(shares, Rules.Tier.GetTypeBudget(), TypeLimit));
        if (free <= 0) return 0;
        var area = checked(free * AreaUnitsPerByte);
        return (area + shares - 1) / shares;
    }

    /// <summary>Gets an independent copy for simulation; changes to the copy never reach this view.</summary>
    public CellView Clone() => new(Rules, TypeLimit, _pages.Select(page => page.Clone()), Usage.Clone(), Partition,
        Priority, WriteState, Upgrades);

    /// <summary>Gets the strip slot a root occupies, or -1 for anchor and legacy pages.</summary>
    /// <exception cref="ArgumentException">The position is not a slot origin of a strip page.</exception>
    public int SlotAt(int page, int x, int y)
    {
        var view = PageAt(page);
        if (view.Kind != PageKind.Strip) return -1;
        var slot = StripLayout.SlotIndexAt(view.Geometry, x, y);
        return slot >= 0 ? slot : throw new ArgumentException($"({x},{y}) is not a slot origin on page {page}.");
    }

    /// <summary>Gets the strip pages for one slot size in page order, whatever their slot count.</summary>
    public IReadOnlyList<int> ChainOf(BoundingSize size) =>
        _chains.TryGetValue(size, out var chain) ? chain : (IReadOnlyList<int>)Array.Empty<int>();

    /// <summary>Records a newly appended page and returns its index.</summary>
    /// <exception cref="ArgumentException">The page is an anchor page.</exception>
    public int AppendPage(PageGeometry page)
    {
        if (page.Kind == PageKind.Anchor) throw new ArgumentException("Only page 0 may be the anchor.", nameof(page));
        if (_pages.Count >= LayoutDescriptor.MaxPages) throw new InvalidOperationException("The cell is at the descriptor page limit.");
        _pages.Add(page.Kind == PageKind.Strip ? PageView.Strip(page, Array.Empty<int>()) : PageView.Unslotted(page, 0));
        Index(_pages.Count - 1);
        return _pages.Count - 1;
    }

    /// <summary>Records a root stored in a strip slot.</summary>
    public void RecordStored(int page, int slot, ItemKey key, long units, BoundingSize size)
    {
        var area = CellUsage.AreaOf(size, units);
        PageAt(page).Occupy(slot);
        try
        {
            Usage.Add(key, units, area);
        }
        catch
        {
            _pages[page].Release(slot);
            throw;
        }
    }

    /// <summary>Records a root leaving a page; <paramref name="slot"/> is -1 for anchor and legacy pages.</summary>
    public void RecordRemoved(int page, int slot, ItemKey key, long units, BoundingSize size)
    {
        var area = CellUsage.AreaOf(size, units);
        Usage.Remove(key, units, area);
        try
        {
            PageAt(page).Release(slot);
        }
        catch
        {
            Usage.Add(key, units, area);
            throw;
        }
    }

    /// <summary>
    /// Records a root moving inside this cell from <paramref name="fromPage"/> to slot <paramref name="toSlot"/>
    /// of <paramref name="toPage"/>; usage does not change.
    /// </summary>
    /// <param name="fromPage">The source page.</param>
    /// <param name="fromSlot">The source slot, or -1 when the source is an anchor or legacy page.</param>
    /// <param name="toPage">The target strip page.</param>
    /// <param name="toSlot">The target slot.</param>
    public void RecordRelocated(int fromPage, int fromSlot, int toPage, int toSlot)
    {
        var target = PageAt(toPage);
        var source = PageAt(fromPage);
        target.Occupy(toSlot);
        try
        {
            source.Release(fromSlot);
        }
        catch
        {
            target.Release(toSlot);
            throw;
        }
    }

    private PageView PageAt(int page) =>
        page >= 0 && page < _pages.Count ? _pages[page] : throw new ArgumentOutOfRangeException(nameof(page));

    private void Index(int index)
    {
        var page = _pages[index];
        if (page.Kind != PageKind.Strip) return;
        if (!_chains.TryGetValue(page.Geometry.SlotSize, out var chain))
            _chains[page.Geometry.SlotSize] = chain = new List<int>();
        chain.Add(index);
    }
}
