using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Machines;
using ProbablyStolen.DigitalStorage.Platform.Content;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Handles a group drag dropped onto a cell bay of a drive or IO Port. The native group drop offers every item to the
/// single inventory under the pointer, and each bay holds one cell, so only the first would move.
/// <list type="bullet">
/// <item>A group that already sits in this bay grid moves as a block before the native drop: the grabbed cell goes to
/// the bay under the pointer and the others keep their offsets, as the ghosts show. The native drop then finds that bay
/// taken and changes nothing. A block that does not fit is left to the native drop.</item>
/// <item>A group from elsewhere dropped onto an empty bay is spread after the native drop: each item it left where it
/// was goes to the next empty sibling.</item>
/// </list>
/// Every move uses the same insertion as the native drop, so every bay rule still applies. Every entry point swallows
/// its own failures: the native drag must finish whatever happens here.
/// </summary>
public static class MachineBayGroupDrop
{
    private static readonly string[] Machines = { DriveItem.ItemId, IoPortItem.ItemId };

    /// <summary>Called before <c>ItemMultiSelectHandler.EndGroupDrag</c>; returns the pending spread, or null.</summary>
    public static object? Begin(ItemMultiSelectHandler group)
    {
        try
        {
            if (group == null || !group.IsGroupDragging) return null;
            var position = Input.mousePosition;
            var selection = NativeTerminalDragAccess.GetGroupItems(group);
            // The same raycasts the native group drop makes first, in the same frame; it ignores a selected item there.
            if (RenderHandler.RaycastElement<GameItem>(position) is { } hit &&
                !(hit is GameItemElement element && selection.Contains(element))) return null;
            if (RenderHandler.RaycastFindParentElement<GameInventory>(position) is not GameSlotInventory target ||
                !IsMachineBay(target) || target.parents.OfType<GridPixelElement>().FirstOrDefault() is not { } grid)
                return null;

            var cells = Cells(grid);
            if (selection.Count >= 2 && selection.All(item => Array.IndexOf(cells, item.parentInventory) >= 0))
            {
                Shift(group, cells, grid.gridWidth, target, selection);
                return null;
            }

            if (target.childItem != null) return null;
            var bays = cells.Where(bay => bay != null).Select(bay => bay!).ToArray();
            var index = Array.IndexOf(bays, target);
            if (index < 0 || bays.Length < 2) return null;
            var items = selection
                .Where(item => item.parentInventory != null && !bays.Contains(item.parentInventory))
                .Select(item => new Pending(item, item.parentInventory)).ToArray();
            return items.Length < 2 ? null : new Spread(bays, index, items);
        }
        catch (Exception error)
        {
            Report("check", error);
            return null;
        }
    }

    /// <summary>Called after <c>ItemMultiSelectHandler.EndGroupDrag</c>; places what the native drop left behind.</summary>
    public static void Complete(object? state)
    {
        if (state is not Spread spread) return;
        foreach (var pending in spread.Items)
        {
            try
            {
                var item = pending.Item;
                if (item.IsDestroyed() || !ReferenceEquals(item.parentInventory, pending.Home) || !item.MayRemove() ||
                    item.MaxNumRemove() < 1) continue;
                foreach (var index in BayFillOrder.After(spread.Target, spread.Bays.Length))
                {
                    var bay = spread.Bays[index];
                    if (!MayEnter(bay, item)) continue;
                    GraphUtils.TryAcceptAll(bay, item);
                    if (!ReferenceEquals(item.parentInventory, pending.Home)) break;
                }
            }
            catch (Exception error)
            {
                Report("spread", error);
            }
        }
    }

    private static void Shift(ItemMultiSelectHandler group, GameSlotInventory?[] cells, int columns,
        GameSlotInventory target, IReadOnlyList<GameItemElement> selection)
    {
        if (selection.Any(item => item.IsDestroyed() || !item.MayRemove() || item.MaxNumRemove() < 1)) return;
        var anchor = NativeTerminalDragAccess.GetGroupAnchor(group);
        if (anchor == null || !selection.Contains(anchor)) return;

        var itemAt = selection.ToDictionary(item => Array.IndexOf(cells, item.parentInventory));
        var isBay = cells.Select(bay => bay != null).ToArray();
        var isBlocked = cells.Select(bay => bay?.childItem is { } child && !(child is GameItemElement element &&
            selection.Contains(element))).ToArray();
        var plan = BayGroupShift.Plan(columns, isBay, isBlocked, itemAt.Keys.ToArray(),
            Array.IndexOf(cells, anchor.parentInventory), Array.IndexOf(cells, target));
        if (plan == null) return;

        // Each destination is empty by the time its cell moves. A refused move stops the block, leaving every cell in
        // a bay it was accepted by.
        foreach (var (from, to) in plan)
        {
            var item = itemAt[from];
            var bay = cells[to]!;
            if (!MayEnter(bay, item)) return;
            GraphUtils.TryAcceptAll(bay, item);
            if (!ReferenceEquals(item.parentInventory, bay)) return;
        }
    }

    // Every cell of the grid, row by row, with null where the grid holds no bay.
    private static GameSlotInventory?[] Cells(GridPixelElement grid)
    {
        var cells = new GameSlotInventory?[grid.gridWidth * grid.gridHeight];
        for (var y = 0; y < grid.gridHeight; y++)
        for (var x = 0; x < grid.gridWidth; x++)
            cells[y * grid.gridWidth + x] = grid.GetElement(x, y) as GameSlotInventory;
        return cells;
    }

    private static bool MayEnter(GameSlotInventory bay, GameItem item) =>
        bay.childItem == null && GeneralHelper.MayPlayerInsertInto(bay) && bay.MayHaveValidInventorySlot(item);

    private static bool IsMachineBay(GameSlotInventory bay) =>
        bay.GetParentItem() is { } machine && Machines.Contains(machine.identifier);

    private static void Report(string stage, Exception error) =>
        Debug.LogWarning($"Digital Storage: machine bay group {stage} skipped ({error.GetType().Name}); the native drag is unchanged.");

    private sealed class Spread
    {
        internal Spread(GameSlotInventory[] bays, int target, Pending[] items) => (Bays, Target, Items) = (bays, target, items);

        internal GameSlotInventory[] Bays { get; }
        internal int Target { get; }
        internal Pending[] Items { get; }
    }

    private sealed class Pending
    {
        internal Pending(GameItemElement item, GameInventory home) => (Item, Home) = (item, home);

        internal GameItemElement Item { get; }
        internal GameInventory Home { get; }
    }
}
