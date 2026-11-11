using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
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

    internal CellRootPort(GameItem cell, Func<GameItem, ItemKey?> identify)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _identify = identify ?? throw new ArgumentNullException(nameof(identify));
    }

    /// <inheritdoc />
    public bool TryAppendStripPage(PageGeometry page) => CellPages.TryAppendStripPage(_cell, page);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The root cannot be found, or the target page or layout is not readable.</exception>
    public TransferStep MoveInto(RootSnapshot root, int page, int slot)
    {
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
            return item.parentInventory == target && item.modifiedShape.minX == x && item.modifiedShape.minY == y
                ? TransferStep.Moved
                : TransferStep.Unowned;
        }

        return item.parentInventory == source && source != null && NativeItemMove.IsSafe(item)
            ? TransferStep.Returned
            : TransferStep.Unowned;
    }

    /// <inheritdoc />
    public IRootSource StoredRoots(ItemKey key) => new CellRootSource(_cell, _identify, key, unslottedOnly: false);
}

/// <summary>
/// Roots of one cell in page, row, column order, optionally of one type or only on anchor and legacy pages. Each
/// call reads the live pages, so the order always follows the actual items. Types are cached for this source's
/// operation only.
/// </summary>
internal sealed class CellRootSource : INativeRootSource
{
    private readonly GameItem _cell;
    private readonly Func<GameItem, ItemKey?> _identify;
    private readonly ItemKey? _key;
    private readonly bool _unslottedOnly;
    private readonly Dictionary<GameItem, ItemKey?> _types = new();

    internal CellRootSource(GameItem cell, Func<GameItem, ItemKey?> identify, ItemKey? key, bool unslottedOnly)
    {
        _cell = cell;
        _identify = identify;
        _key = key;
        _unslottedOnly = unslottedOnly;
    }

    public bool TryPeekFirst(out RootSnapshot root)
    {
        foreach (var (index, page) in AllowedPages())
        {
            GameItem? first = null;
            foreach (var item in page.childItems)
            {
                if (!Matches(item)) continue;
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
        .SelectMany(entry => entry.Page.childItems.Where(Matches)
            .OrderBy(item => item.modifiedShape.minY).ThenBy(item => item.modifiedShape.minX)
            .Select(item => Snapshot(entry.Index, item)))
        .ToArray();

    public GameItem? Resolve(RootSnapshot root)
    {
        var pages = CellPages.Pages(_cell);
        return root.Page < pages.Count
            ? pages[root.Page].childItems.FirstOrDefault(item =>
                item.modifiedShape.minX == root.X && item.modifiedShape.minY == root.Y)
            : null;
    }

    private static bool Before(GameItem item, GameItem other) =>
        item.modifiedShape.minY < other.modifiedShape.minY ||
        item.modifiedShape.minY == other.modifiedShape.minY && item.modifiedShape.minX < other.modifiedShape.minX;

    private IEnumerable<(int Index, GameGridInventory Page)> AllowedPages()
    {
        var pages = CellPages.Pages(_cell);
        if (!_unslottedOnly) return pages.Select((page, index) => (index, page));
        if (!CellTags.TryReadLayout(_cell, out var layout) || layout!.Pages.Count != pages.Count)
            throw new InvalidOperationException("The cell layout does not match its pages.");
        return pages.Select((page, index) => (index, page)).Where(entry => layout.Pages[entry.index].Kind != PageKind.Strip);
    }

    private bool Matches(GameItem item) => _key == null || _key.Equals(TypeOf(item));

    private ItemKey? TypeOf(GameItem item)
    {
        if (!_types.TryGetValue(item, out var key)) _types[item] = key = _identify(item);
        return key;
    }

    private RootSnapshot Snapshot(int page, GameItem item)
    {
        var key = TypeOf(item) ?? throw new InvalidOperationException($"A root on page {page} has no type.");
        return new RootSnapshot(page, item.modifiedShape.minX, item.modifiedShape.minY, CellPages.SizeOf(item), key,
            item.unitCount, this);
    }
}

/// <summary>Items chosen by a player, offered in selection order while they stay where they were.</summary>
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
/// Destination for extraction into a native grid inventory, such as the player's inventory. The page is
/// remove-locked, so the native slot search only works once the root has left it; whatever the destination does
/// not take goes straight back to its page position.
/// </summary>
internal sealed class InventoryRootSink : IRootSink
{
    private readonly GameGridInventory _destination;

    internal InventoryRootSink(GameGridInventory destination) =>
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));

    /// <summary>Gets the units that actually moved, including part of a root the destination only partly took.</summary>
    internal long MovedUnits { get; private set; }

    /// <summary>
    /// Accepts one root. A simulation cannot run the native slot search without moving the root, so it always
    /// reports a move. A root the destination only partly takes ends as <see cref="TransferOutcome.Returned"/>.
    /// </summary>
    public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
    {
        if (mode == Actionable.Simulate) return TransferStep.Moved;
        var item = (root.Origin as INativeRootSource)?.Resolve(root)
            ?? throw new InvalidOperationException($"The root {root} cannot be resolved.");
        var page = item.parentInventory;
        if (page == null) throw new InvalidOperationException("The root has no page.");

        var original = item.modifiedShape;
        var requested = item.unitCount;
        var moved = 0;
        Exception? error = null;
        try
        {
            if (!page.Expel(item)) return TransferStep.Rejected(StoreRejection.PhysicalLimit);
            moved = _destination.TryFindOneValidInventorySlot(item, keepOrientation: true)?.TryAcceptOnce(requested) ?? 0;
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

        MovedUnits += moved;
        if (moved == requested) return TransferStep.Moved;
        var home = item.parentInventory == page && NativeItemMove.IsSafe(item);
        if (!home) return TransferStep.Unowned;
        return moved == 0 ? TransferStep.Rejected(StoreRejection.PhysicalLimit) : TransferStep.Returned;
    }
}
