using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Keeps items stored in a network out of the game's view of the items the player has at hand. The game finds those
/// through <c>PlayerStore.FindAllItem</c>, which walks into cell pages: without this, stored items would age every
/// night (spoiling, stolen heat, cold expiry) and stored hoppers, alarms and current converters would still work.
/// Stored items are still data: they keep the state they were stored with, and taking one out resumes it from there
/// without making up for the nights that passed.
/// </summary>
public static class StoredItemPresence
{
    /// <summary>Removes from <paramref name="items"/> every item lying on a cell page or inside such an item.</summary>
    public static void HideStored(List<GameItem>? items)
    {
        if (items == null || items.Count == 0) return;
        var known = new Dictionary<GameInventory, bool>(InventoryComparer.Instance);
        try
        {
            // Decided for every item before removing any, so a failure leaves the game's list whole.
            var hidden = new bool[items.Count];
            var any = false;
            for (var index = 0; index < items.Count; index++)
            {
                hidden[index] = items[index] != null && IsStored(items[index].parentInventory, known);
                any |= hidden[index];
            }

            if (!any) return;
            var kept = 0;
            for (var index = 0; index < items.Count; index++)
            {
                if (!hidden[index]) items[kept++] = items[index];
            }

            items.RemoveRange(kept, items.Count - kept);
        }
        catch (System.Exception error)
        {
            // The nightly cycle runs on this list; it must go on even if stored items then age this night.
            UnityEngine.Debug.LogWarning($"Digital Storage: stored items stayed visible to the game ({error.GetType().Name}).");
        }
    }

    /// <summary>Allows native liquid updates for containers outside storage cells.</summary>
    public static bool ShouldUpdateLiquid(GameItem? container)
    {
        try
        {
            return !IsStored(container?.parentInventory, known: null);
        }
        catch (System.Exception error)
        {
            UnityEngine.Debug.LogWarning($"Digital Storage: liquid update could not check storage ownership ({error.GetType().Name}).");
            return true;
        }
    }

    // Each inventory is remembered for bulk scans, so items sharing containers cost one walk.
    private static bool IsStored(GameInventory? inventory, Dictionary<GameInventory, bool>? known)
    {
        if (inventory == null) return false;
        if (known != null && known.TryGetValue(inventory, out var found)) return found;
        var owner = GraphUtils.FindParentType<GameItem>(inventory);
        var stored = IsPage(inventory, owner) || owner != null && IsStored(owner.parentInventory, known);
        if (known != null) known[inventory] = stored;
        return stored;
    }

    // Only storage pages have insertion locked by this mod; the page list confirms it belongs to a cell.
    private static bool IsPage(GameInventory inventory, GameItem? owner)
    {
        if (inventory is not GameGridInventory page || !page.overrideLockInsert || owner == null ||
            !BasicCellHandler.IsCell(owner)) return false;
        foreach (var candidate in CellPages.Pages(owner))
        {
            if (ReferenceEquals(candidate, page)) return true;
        }

        return false;
    }

    private sealed class InventoryComparer : IEqualityComparer<GameInventory>
    {
        internal static readonly InventoryComparer Instance = new();

        public bool Equals(GameInventory? left, GameInventory? right) => ReferenceEquals(left, right);

        public int GetHashCode(GameInventory inventory) => RuntimeHelpers.GetHashCode(inventory);
    }
}
