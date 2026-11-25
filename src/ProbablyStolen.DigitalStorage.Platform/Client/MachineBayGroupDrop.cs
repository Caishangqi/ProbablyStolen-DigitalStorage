using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Machines;
using ProbablyStolen.DigitalStorage.Platform.Content;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Spreads a group drag dropped onto one empty cell bay of a drive or IO Port over the sibling bays of the same grid.
/// The native group drop offers every item to the single inventory under the pointer, and each bay holds one cell, so
/// only the first would move. After the native drop, each item it left where it was goes to the next empty sibling
/// through the same insertion the native drop uses, so every bay rule still applies. Items that already sit in this
/// grid are left alone, so rearranging bays stays native. Every entry point swallows its own failures: the native drag
/// must finish whatever happens here.
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
            // The same raycasts the native group drop makes first, in the same frame.
            if (RenderHandler.RaycastElement<GameItem>(position) != null) return null;
            if (RenderHandler.RaycastFindParentElement<GameInventory>(position) is not GameSlotInventory target ||
                target.childItem != null || !IsMachineBay(target) ||
                target.parents.OfType<GridPixelElement>().FirstOrDefault() is not { } grid) return null;
            var bays = grid.children.OfType<GameSlotInventory>().ToArray();
            var index = Array.IndexOf(bays, target);
            if (index < 0 || bays.Length < 2) return null;
            var items = NativeTerminalDragAccess.GetGroupItems(group)
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
                    if (bay.childItem != null || !GeneralHelper.MayPlayerInsertInto(bay) || !bay.MayHaveValidInventorySlot(item))
                        continue;
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
