using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Items;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Native page operations on one storage cell item. Pages are locked <see cref="GameGridInventory"/>s inside a
/// <see cref="MachineryTabs"/> on the cell's last machinery panel, so the native save graph stores their items.
/// Items move with the native precise-placement sequence (Expel → set <c>modifiedShape</c> → UncheckedAccept,
/// as in <c>ItemMultiSelectHandler.CommitPrecisePlacement</c>); those calls bypass the page locks.
/// </summary>
internal static class CellPages
{
    /// <summary>
    /// The pages panel role. It must not be <c>"storage"</c>: the native <c>StorageExpansionHelper.InitTabNames</c>
    /// looks that role up on every decoded item and would take over the tab captions.
    /// </summary>
    internal const string PanelRole = "digital_storage.pages";

    /// <summary>Gets the 1k layout with the configured page and type limits.</summary>
    internal static CellLayout Layout { get; private set; } = CellLayout.Cell1K;

    /// <summary>Applies configured limits; existing pages beyond a lowered page limit are kept (see CellTags).</summary>
    internal static void Configure(int maxPages, int maxTypes) =>
        Layout = CellLayout.Cell1K.WithLimits(maxPages, maxTypes);

    /// <summary>Creates the pages panel with its first page.</summary>
    internal static MachineryPanel CreatePanel(string label)
    {
        var tabs = new MachineryTabs();
        AddPage(tabs);
        return MachineHelper.Panel(label, tabs, PanelRole);
    }

    /// <summary>Gets the page tabs of a cell, or null when the item has no pages panel.</summary>
    internal static MachineryTabs? Tabs(GameItem cell) =>
        (cell.contentWindow?.childElement as MachineryPanelRow)?.GetPanel(PanelRole)?.content as MachineryTabs;

    /// <summary>Gets the pages in page order.</summary>
    internal static IReadOnlyList<GameGridInventory> Pages(GameItem cell)
    {
        var tabs = Tabs(cell);
        if (tabs == null)
        {
            return Array.Empty<GameGridInventory>();
        }

        var pages = new List<GameGridInventory>(tabs.count);
        for (var index = 0; index < tabs.count; index++)
        {
            if (tabs.GetTabInventory(index) is GameGridInventory page)
            {
                pages.Add(page);
            }
        }

        return pages;
    }

    /// <summary>Gets whether no page holds an item.</summary>
    internal static bool IsEmpty(GameItem cell) => Pages(cell).All(page => page.childItems.Count == 0);

    /// <summary>Gets every item root on every page.</summary>
    internal static IEnumerable<GameItem> Roots(GameItem cell) => Pages(cell).SelectMany(page => page.childItems);

    /// <summary>Appends one page and records the new page count on the cell.</summary>
    internal static GameGridInventory AppendPage(GameItem cell)
    {
        var tabs = Tabs(cell) ?? throw new InvalidOperationException("The cell has no pages panel.");
        var page = AddPage(tabs);
        CellTags.WritePageCount(cell, tabs.count);
        Refresh(cell);
        return page;
    }

    /// <summary>Adds pages until the cell has <paramref name="count"/> pages. Used while native decoding rebuilds a cell.</summary>
    internal static void EnsurePageCount(GameItem cell, int count)
    {
        var tabs = Tabs(cell);
        if (tabs == null || tabs.count >= count)
        {
            return;
        }

        while (tabs.count < count)
        {
            AddPage(tabs);
        }

        Refresh(cell);
    }

    /// <summary>Gets an item's bounding size in its current orientation.</summary>
    internal static BoundingSize SizeOf(GameItem item)
    {
        var shape = item.modifiedShape;
        return new BoundingSize(shape.globalWidth, shape.globalHeight);
    }

    /// <summary>Builds a fresh read-only projection of the cell.</summary>
    internal static CellView Read(GameItem cell, Func<GameItem, ItemKey?> identify)
    {
        var pages = new List<PageView>();
        var types = new HashSet<ItemKey>();
        foreach (var page in Pages(cell))
        {
            BoundingSize? label = null;
            var occupied = new List<int>();
            foreach (var root in page.childItems)
            {
                var size = SizeOf(root);
                label ??= size;
                var slot = Layout.SlotIndexAt(root.modifiedShape.minX, root.modifiedShape.minY,
                    size.Width, size.Height);
                if (size.Equals(label.Value) && slot >= 0)
                {
                    occupied.Add(slot);
                }

                var key = identify(root);
                if (key != null)
                {
                    types.Add(key);
                }
            }

            pages.Add(new PageView(label, occupied));
        }

        return new CellView(Layout, pages, types, CellTags.ReadPartition(cell), DrivePriorityTag.ReadForCell(cell));
    }

    /// <summary>
    /// Moves an item into the planned page slot. A failed move attempts one direct return to its source.
    /// </summary>
    internal static bool Place(GameItem cell, GameItem item, Placement placement)
    {
        if (!placement.IsAccepted)
        {
            return false;
        }

        var page = placement.AppendsPage ? AppendPage(cell) : Pages(cell)[placement.Page];
        var size = SizeOf(item);
        var (x, y) = Layout.SlotOrigin(placement.Slot, size.Width, size.Height);
        var target = new GridShapeBuilder(item.modifiedShape).SetPosition(x, y).Build();
        if (!page.CheckShapePlacement(item, target, new List<GameItem>()))
        {
            return false;
        }

        var result = ItemMove.Run(NativeItemMove.Into(item, page, target));
        NativeItemMove.Report(result, item, "store");
        return result.Outcome == ItemMoveOutcome.Moved;
    }

    /// <summary>
    /// Moves a page root into a destination inventory, keeping its orientation. Returns the units moved; whatever
    /// the destination did not take is offered back to its original page position once.
    /// </summary>
    internal static int MoveTo(GameItem root, GameGridInventory destination)
    {
        var page = root.parentInventory;
        if (page == null)
        {
            return 0;
        }

        var original = root.modifiedShape;
        var requested = root.unitCount;
        var moved = 0;
        Exception? error = null;
        try
        {
            if (!page.Expel(root)) return 0;
            // The page is remove-locked, so the native slot search only works once the root has left it.
            moved = destination.TryFindOneValidInventorySlot(root, keepOrientation: true)?.TryAcceptOnce(requested) ?? 0;
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            var result = ItemMove.Recover(NativeItemMove.ReturnTo(root, page, original), error);
            NativeItemMove.Report(result, root, "extract");
        }
        return moved;
    }

    private static GameGridInventory AddPage(MachineryTabs tabs)
    {
        var page = new GameGridInventory(Layout.PageWidth, Layout.PageHeight).SetMachineryStyle();
        // Players reach page items only through the terminal: no drag in or out, and no "use" from the cell window.
        // Insertion uses the native lock because it is checked before HopperHelper.MayBypassGridRules; removal uses
        // callbacks instead of the native lock, because a page locked both ways makes every item tooltip show
        // "locked, machine running" (GameItemElement.GetTooltipBasic). An inventory-level false vetoes activation
        // even when the item allows it (GameItem.MayActivateSlotItem). Expel/UncheckedAccept ignore all of these.
        page.overrideLockInsert = true;
        page.mayInventoryRemoveItemFunc = (_, _) => false;
        page.maxSlotRemoveItemFunc = (_, _, _) => 0;
        page.mayActivateSlotItemFunc = (_, _, _) => false;
        page.canActivateSlotItemFunc = (_, _, _) => false;
        tabs.AddTab((tabs.count + 1).ToString(), MachineHelper.WrapInIntake(page));
        return page;
    }

    private static void Refresh(GameItem cell)
    {
        (cell.contentWindow?.childElement as MachineryPanelRow)?.Validate();
        cell.contentWindow?.Validate();
    }
}
