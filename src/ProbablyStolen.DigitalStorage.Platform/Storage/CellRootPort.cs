using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>A root source whose snapshots can be resolved back to native items by Platform sinks.</summary>
internal interface INativeRootSource : IRootSource
{
    /// <summary>Gets the native item a snapshot from this source refers to, or null when it is gone.</summary>
    GameItem? Resolve(RootSnapshot root);
}

/// <summary>
/// Native <see cref="ICellPagesPort"/> of one cell. Moves go through <see cref="ItemMove"/> with
/// <see cref="NativeItemMove"/>, and every move is checked against the item's final owner and position.
/// </summary>
internal sealed class CellRootPort : ICellPagesPort
{
    private readonly GameItem _cell;
    private readonly Func<GameItem, ItemKey?> _identify;
    private readonly CellView? _view;
    private readonly Action? _storedIdentityChanged;

    /// <param name="cell">The cell whose pages this port moves roots on.</param>
    /// <param name="identify">Reads a root's type.</param>
    /// <param name="view">
    /// The view whose slot types locate roots of one type; without one, every root's type is read.
    /// </param>
    /// <param name="storedIdentityChanged">Marks the mounted views stale if native move callbacks change the key of a stored root.</param>
    internal CellRootPort(GameItem cell, Func<GameItem, ItemKey?> identify, CellView? view = null,
        Action? storedIdentityChanged = null)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _identify = identify ?? throw new ArgumentNullException(nameof(identify));
        _view = view;
        _storedIdentityChanged = storedIdentityChanged;
    }

    /// <inheritdoc />
    public bool TryAppendStripPage(PageGeometry page) =>
        _view?.WriteState != CellWriteState.ExtractOnly && CellPages.TryAppendStripPage(_cell, page);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The root cannot be found, or the target page or layout is not readable.</exception>
    public TransferStep MoveInto(RootSnapshot root, int page, int slot)
    {
        if (_view?.WriteState == CellWriteState.ExtractOnly)
            return TransferStep.Rejected(StoreRejection.ReadOnly);
        var item = (root.Origin as INativeRootSource)?.Resolve(root)
            ?? throw new InvalidOperationException($"The root {root} cannot be resolved.");
        if (!CellTags.TryReadLayout(_cell, out var layout) || page >= layout!.Pages.Count)
            throw new InvalidOperationException("The target page is not in the cell layout.");
        var pages = CellPages.Pages(_cell);
        if (page >= pages.Count) throw new InvalidOperationException("The target page does not exist.");

        var target = pages[page];
        var (x, y) = StripLayout.SlotOrigin(layout.Pages[page], slot);
        var shape = new GridShapeBuilder(item.modifiedShape).SetPosition(x, y).Build();
        // The native check also applies item state rules; a refusal leaves the item untouched.
        if (!target.CheckShapePlacement(item, shape, new List<GameItem>())) return TransferStep.Rejected(StoreRejection.PhysicalLimit);

        var source = item.parentInventory;
        var result = ItemMove.Run(NativeItemMove.Into(item, target, shape));
        NativeItemMove.Report(result, item, "store");
        if (result.Outcome == ItemMoveOutcome.Moved)
        {
            if (item.parentInventory != target || item.modifiedShape.minX != x || item.modifiedShape.minY != y)
                return TransferStep.Unowned;

            // Native detach and attach callbacks can change a root's features without changing its slot or units.
            if (_storedIdentityChanged != null && !root.Key.Equals(_identify(item)))
            {
                CellInventoryCache.Forget(_cell);
                _storedIdentityChanged();
            }
            return TransferStep.Moved;
        }

        return item.parentInventory == source && source != null && NativeItemMove.IsSafe(item)
            ? TransferStep.Returned
            : TransferStep.Unowned;
    }

    /// <inheritdoc />
    public IRootSource StoredRoots(ItemKey key) => new CellRootSource(_cell, _identify, key, unslottedOnly: false, _view);
}

/// <summary>
/// Roots of one cell in page, row, column order, optionally of one type or only on anchor and legacy pages. Each
/// call reads the live pages, so the order always follows the actual items. With a view, a strip root's type is the
/// one its slot records, and only a root about to move has its own type read to confirm it; other types are read and
/// cached for this source's operation only.
/// </summary>
internal sealed class CellRootSource : INativeRootSource
{
    private readonly GameItem _cell;
    private readonly Func<GameItem, ItemKey?> _identify;
    private readonly ItemKey? _key;
    private readonly bool _unslottedOnly;
    private readonly CellView? _view;
    private readonly Dictionary<GameItem, ItemKey?> _types = new();
    private readonly HashSet<GameItem> _confirmed = new();

    internal CellRootSource(GameItem cell, Func<GameItem, ItemKey?> identify, ItemKey? key, bool unslottedOnly,
        CellView? view = null)
    {
        _cell = cell;
        _identify = identify;
        _key = key;
        _unslottedOnly = unslottedOnly;
        _view = view;
    }

    public bool TryPeekFirst(out RootSnapshot root)
    {
        foreach (var (index, page) in AllowedPages())
        {
            GameItem? first = null;
            foreach (var item in page.childItems)
            {
                if (!Matches(index, item)) continue;
                if (first == null || Before(item, first)) first = item;
            }

            if (first != null)
            {
                root = Snapshot(index, first);
                return true;
            }
        }

        root = default;
        return false;
    }

    public IEnumerable<RootSnapshot> Snapshot() => AllowedPages()
        .SelectMany(entry => entry.Page.childItems.Where(item => Matches(entry.Index, item))
            .OrderBy(item => item.modifiedShape.minY).ThenBy(item => item.modifiedShape.minX)
            .Select(item => Snapshot(entry.Index, item)))
        .ToArray();

    /// <exception cref="InvalidOperationException">
    /// The root's own type is not the one its slot records; the cell's kept contents are dropped, and nothing moves.
    /// </exception>
    public GameItem? Resolve(RootSnapshot root)
    {
        var pages = CellPages.Pages(_cell);
        var item = root.Page < pages.Count
            ? pages[root.Page].childItems.FirstOrDefault(candidate =>
                candidate.modifiedShape.minX == root.X && candidate.modifiedShape.minY == root.Y)
            : null;
        if (item == null || _types.ContainsKey(item) || !_confirmed.Add(item)) return item;
        // Its type came from the slot record: the one root about to move is read to confirm it.
        if (root.Key.Equals(_identify(item))) return item;
        CellInventoryCache.Forget(_cell);
        throw new InvalidOperationException($"A stored root on page {root.Page} is no longer of its recorded type.");
    }

    private static bool Before(GameItem item, GameItem other) =>
        item.modifiedShape.minY < other.modifiedShape.minY ||
        item.modifiedShape.minY == other.modifiedShape.minY && item.modifiedShape.minX < other.modifiedShape.minX;

    private IEnumerable<(int Index, GameGridInventory Page)> AllowedPages()
    {
        var pages = CellPages.Pages(_cell);
        if (_view != null && pages.Count != _view.Pages.Count)
            throw new InvalidOperationException("The cell view no longer matches its pages.");
        if (!_unslottedOnly)
            return pages.Select((page, index) => (index, page)).Where(entry => IsAllowed(entry.index));
        if (!CellTags.TryReadLayout(_cell, out var layout) || layout!.Pages.Count != pages.Count)
            throw new InvalidOperationException("The cell layout does not match its pages.");
        return pages.Select((page, index) => (index, page)).Where(entry => IsAllowed(entry.index) &&
            layout.Pages[entry.index].Kind != PageKind.Strip);
    }

    private bool IsAllowed(int page) => _view == null || !_view.Pages[page].IsProtected;


    private bool Matches(int page, GameItem item) => _key == null || _key.Equals(TypeOf(page, item));

    private ItemKey? TypeOf(int page, GameItem item)
    {
        if (RecordedType(page, item) is { } recorded) return recorded;
        if (!_types.TryGetValue(item, out var key)) _types[item] = key = _identify(item);
        return key;
    }

    // The type the view records for a strip root's slot, or null when there is no view or no record.
    private ItemKey? RecordedType(int page, GameItem item)
    {
        if (_view == null || page >= _view.Pages.Count || _view.Pages[page].Kind != PageKind.Strip) return null;
        var slot = StripLayout.SlotIndexAt(_view.Pages[page].Geometry, item.modifiedShape.minX, item.modifiedShape.minY);
        return slot < 0 ? null : _view.KeyAt(page, slot);
    }

    private RootSnapshot Snapshot(int page, GameItem item)
    {
        var key = TypeOf(page, item) ?? throw new InvalidOperationException($"A root on page {page} has no type.");
        return new RootSnapshot(page, item.modifiedShape.minX, item.modifiedShape.minY, CellPages.SizeOf(item), key,
            item.unitCount, this);
    }
}

/// <summary>Player selected items offered in selection order while they stay where they were.</summary>
internal sealed class ItemListSource : INativeRootSource
{
    private readonly GameItem[] _items;
    private readonly GameInventory?[] _homes;
    private readonly ItemKey[] _keys;

    /// <summary>Creates a source; every item needs a type.</summary>
    internal ItemListSource(IReadOnlyList<GameItem> items, IReadOnlyList<ItemKey> keys)
    {
        _items = items.ToArray();
        _keys = keys.ToArray();
        _homes = _items.Select(item => item.parentInventory).ToArray();
    }

    public bool TryPeekFirst(out RootSnapshot root)
    {
        var first = Snapshot().Take(1).ToArray();
        root = first.FirstOrDefault();
        return first.Length == 1;
    }

    public IEnumerable<RootSnapshot> Snapshot()
    {
        for (var index = 0; index < _items.Length; index++)
        {
            var item = _items[index];
            if (item.unitCount > 0 && item.parentInventory == _homes[index] &&
                !(item is GameItemElement element && element.IsDestroyed()))
                yield return new RootSnapshot(index, 0, 0, CellPages.SizeOf(item), _keys[index], item.unitCount, this);
        }
    }

    public GameItem? Resolve(RootSnapshot root) => root.Page < _items.Length ? _items[root.Page] : null;
}

/// <summary>
/// Destination for extraction into a native player-accessible inventory. Locked source pages are released only during
/// acceptance, and any root the destination does not take goes back to its page position.
/// </summary>
internal sealed class InventoryRootSink : IRootSink
{
    private readonly GameInventory _destination;
    private readonly bool _wholeRootOnly;
    private readonly InventoryDropTarget? _pointPlacement;
    private readonly bool _useNativeDragSearch;
    private readonly GameItemElement? _containerTarget;
    private readonly IReadOnlyList<GameInventory> _itemInventories;

    /// <summary>Creates a sink into a native inventory.</summary>
    /// <param name="destination">The native inventory that receives roots.</param>
    /// <param name="wholeRootOnly">
    /// Whether a root the destination can only take in part stays stored. The native partial accept splits a new item
    /// off without copying the root's full state, so the terminal never asks for it.
    /// </param>
    /// <param name="pointPlacement">The release cell anchoring a single drop onto the visible grid.</param>
    /// <param name="useNativeDragSearch">Whether to use native orientation search within the resolved drop target.</param>
    /// <param name="containerTarget">The visible container icon authorizing insertion while its window is closed.</param>
    /// <param name="itemInventories">The supported inventories owned by <paramref name="containerTarget"/> before commit.</param>
    internal InventoryRootSink(GameInventory destination, bool wholeRootOnly = false,
        InventoryDropTarget? pointPlacement = null, bool useNativeDragSearch = false,
        GameItemElement? containerTarget = null, IReadOnlyList<GameInventory>? itemInventories = null)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        _wholeRootOnly = wholeRootOnly;
        _pointPlacement = pointPlacement;
        _useNativeDragSearch = useNativeDragSearch;
        _containerTarget = containerTarget;
        _itemInventories = itemInventories ?? Array.Empty<GameInventory>();
    }

    /// <summary>Whether a slot that can take <paramref name="transferable"/> units may receive a root of <paramref name="units"/>.</summary>
    internal static bool AcceptsWhole(int transferable, int units) => units > 0 && transferable >= units;

    /// <summary>Gets the units that actually moved, including part of a root the destination only partly took.</summary>
    internal long MovedUnits { get; private set; }

    /// <summary>
    /// Accepts one root. Simulation only checks target eligibility; complete capacity is checked after the root has
    /// been removed from its page. That native search is part of the formal transfer and is never a hover probe.
    /// </summary>
    public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
    {
        if (mode == Actionable.Simulate)
        {
            if (IsTargetUsable()) return TransferStep.Moved;
            _pointPlacement?.RecordPlacement("target_unavailable_preflight");
            return TransferStep.Rejected(StoreRejection.PhysicalLimit);
        }

        var item = (root.Origin as INativeRootSource)?.Resolve(root)
            ?? throw new InvalidOperationException($"The root {root} cannot be resolved.");
        var page = item.parentInventory;
        if (page == null || !Owns(page, item)) return TransferStep.Unowned;
        var original = item.modifiedShape;
        if (!IsTargetUsable())
        {
            _pointPlacement?.RecordPlacement("target_unavailable", original, requestedUnits: item.unitCount);
            return TransferStep.Rejected(StoreRejection.PhysicalLimit);
        }

        var requested = item.unitCount;
        var moved = 0;
        SlotMarker? slot = null;
        Exception? error = null;
        try
        {
            if (requested <= 0) return TransferStep.Rejected(StoreRejection.PhysicalLimit);
            if (!page.Expel(item))
            {
                _pointPlacement?.RecordPlacement("source_expel_rejected", original, requestedUnits: requested);
                return Owns(page, item) ? TransferStep.Rejected(StoreRejection.PhysicalLimit) : TransferStep.Unowned;
            }

            // The target can change between the initial qualification and the actual native move.
            if (IsTargetUsable())
            {
                var shape = item.modifiedShape;
                var pointCenter = _pointPlacement?.CenterFor(shape);
                if (_containerTarget != null)
                {
                    // Item targets own several native inventories (for example a gun's eight mod slots). Native
                    // chooses the actual child only after the stored root has been released from its locked page.
                    if (_containerTarget.MayHaveValidInventorySlot(item))
                        slot = _containerTarget.TryFindOneValidInventorySlot(item);
                    if (slot != null && IsValidItemSlot(slot) &&
                        (!_wholeRootOnly || AcceptsWhole(slot.numTransfer, requested)))
                        moved = slot.TryAcceptOnce(requested);
                }
                else
                {
                    slot = pointCenter is { } center
                        ? _destination.TryInventorySlot(item, requested, center, shape, item.modifiedState)
                        : _destination is GameGridInventory grid
                            ? grid.TryFindOneValidInventorySlot(item, keepOrientation: !_useNativeDragSearch)
                            : _destination.TryInventorySlot(item, requested, shape, item.modifiedState);
                    // Native search may return a slot inside an item in the grid. Drag targets name one inventory.
                    if (slot != null && (!_useNativeDragSearch || slot.inventory == _destination) &&
                        (!_wholeRootOnly || AcceptsWhole(slot.numTransfer, requested)))
                        moved = slot.TryAcceptOnce(requested);
                }
                _pointPlacement?.RecordPlacement("native_slot", shape, pointCenter, slot, requested, moved);
            }
            else
            {
                _pointPlacement?.RecordPlacement("target_unavailable_after_expel", original, requestedUnits: requested);
            }
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            var result = ItemMove.Recover(NativeItemMove.ReturnTo(item, page, original), error);
            NativeItemMove.Report(result, item, "extract");
        }

        var targetOwnsAcceptedItem = moved > 0 && OwnsTarget(slot, item);
        if (moved == requested && targetOwnsAcceptedItem)
        {
            MovedUnits += moved;
            return TransferStep.Moved;
        }

        // A whole-root transfer must never turn a short native acceptance into a successful step. The pre-check
        // prevents this in the normal path; an unexpected native shortfall is still stopped after recovery.
        var home = Owns(page, item) && NativeItemMove.IsSafe(item);
        if (_wholeRootOnly)
            return moved == 0 && home ? TransferStep.Rejected(StoreRejection.PhysicalLimit) : TransferStep.Unowned;

        if (targetOwnsAcceptedItem) MovedUnits += moved;
        if (!home) return TransferStep.Unowned;
        return moved == 0 ? TransferStep.Rejected(StoreRejection.PhysicalLimit) : TransferStep.Returned;
    }

    private bool IsValidItemSlot(SlotMarker slot)
    {
        if (_containerTarget == null || slot.inventory == null ||
            !InventoryDropTarget.IsSupportedItemInventory(slot.inventory, _itemInventories)) return false;
        return ReferenceEquals(GraphUtils.FindParentType<GameItem>(slot.inventory), _containerTarget) &&
            InventoryDropTarget.IsUsable(slot.inventory, _containerTarget);
    }

    private bool IsTargetUsable() => _containerTarget == null
        ? InventoryDropTarget.IsUsable(_destination, null)
        : InventoryDropTarget.IsItemTargetUsable(_containerTarget, _itemInventories);

    private static bool Owns(GameInventory inventory, GameItem item) =>
        item.parentInventory == inventory && inventory.childItems.Contains(item);

    private bool OwnsTarget(SlotMarker? slot, GameItem item)
    {
        if (slot == null || slot.inventory == null) return false;
        if (_containerTarget != null)
        {
            if (!IsValidItemSlot(slot)) return false;
            var accepted = slot.targetItem ?? slot.item;
            return accepted != null && Owns(slot.inventory, accepted);
        }

        if (slot.inventory != _destination) return false;
        return slot.targetItem != null
            ? Owns(_destination, slot.targetItem)
            : slot.item != null && Owns(_destination, slot.item);
    }
}
