using System.Collections.Generic;
using System.Reflection;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>Adapts the target game's single drag quantity and cached placement, without changing item state.</summary>
internal static class NativeTerminalDragAccess
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? Amount = typeof(ItemMouseDragHandler).GetField("currentRemove", PrivateInstance);
    private static readonly FieldInfo? LastItem = typeof(ItemMouseDragHandler).GetField("lastItem", PrivateInstance);
    private static readonly FieldInfo? LastInventory = typeof(ItemMouseDragHandler).GetField("lastInventory", PrivateInstance);
    private static readonly FieldInfo? LastSlot = typeof(ItemMouseDragHandler).GetField("lastSlot", PrivateInstance);
    private static readonly FieldInfo? ScrollIgnore = typeof(ItemMouseDragHandler).GetField("isScrollIgnore", PrivateInstance);
    private static readonly FieldInfo? Highlights = typeof(ItemMouseDragHandler).GetField("highlightManager", PrivateInstance);
    private static readonly FieldInfo? GroupSelection = typeof(ItemMultiSelectHandler).GetField("selectedItems", PrivateInstance);
    private static readonly FieldInfo? GroupHoverItem = typeof(ItemMultiSelectHandler).GetField("hoverItem", PrivateInstance);
    private static readonly FieldInfo? GroupHoverNodes =
        typeof(ItemMultiSelectHandler).GetField("hoverHighlightNodes", PrivateInstance);

    /// <summary>Gets the item under the pointer the single drag would drop onto, or null over an inventory.</summary>
    internal static GameItem? GetDropItem(ItemMouseDragHandler drag)
    {
        try
        {
            return LastInventory?.GetValue(drag) == null ? LastItem?.GetValue(drag) as GameItem : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Paints the single drag's hover highlight on one slot; the native handler clears it on leaving.</summary>
    internal static void Highlight(ItemMouseDragHandler drag, SlotMarker? slot, RenderHandler.BackgroundColor color)
    {
        try
        {
            if (Highlights?.GetValue(drag) is HighlightManager highlights) highlights.Try(slot, color);
        }
        catch
        {
            // Highlighting is feedback only; a missing field leaves the native colour.
        }
    }

    /// <summary>Gets the items of the current group drag, or an empty list when they cannot be read.</summary>
    internal static IReadOnlyList<GameItemElement> GetGroupItems(ItemMultiSelectHandler group)
    {
        try
        {
            return GroupSelection?.GetValue(group) is List<GameItemElement> items
                ? items.ToArray()
                : System.Array.Empty<GameItemElement>();
        }
        catch
        {
            return System.Array.Empty<GameItemElement>();
        }
    }

    /// <summary>Gets the item the group drag currently highlights as its drop target.</summary>
    internal static GameItem? GetGroupHoverItem(ItemMultiSelectHandler group)
    {
        try
        {
            return GroupHoverItem?.GetValue(group) as GameItem;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Paints the group drag's target slot; the nodes join the native list, which clears them on leaving.</summary>
    internal static void HighlightGroupTarget(ItemMultiSelectHandler group, GameItem target, RenderHandler.BackgroundColor color)
    {
        try
        {
            if (GroupHoverNodes?.GetValue(group) is not List<TreeNodeRender> nodes) return;
            var slot = target.GetIncompleteSlotInInventory();
            var painted = slot?.inventory?.HighlightSlot(slot, color, doReplace: true);
            if (painted != null) nodes.AddRange(painted);
        }
        catch
        {
            // Highlighting is feedback only; a missing field leaves the native colour.
        }
    }

    /// <summary>
    /// Suppresses native item rotation and quantity changes on scroll. Returns the previous state, or null when
    /// the field is unavailable and nothing changed.
    /// </summary>
    internal static bool? SetScrollIgnore(ItemMouseDragHandler drag, bool value)
    {
        try
        {
            if (ScrollIgnore?.GetValue(drag) is not bool previous) return null;
            ScrollIgnore.SetValue(drag, value);
            return previous;
        }
        catch
        {
            return null;
        }
    }

    internal static int GetAmount(ItemMouseDragHandler drag)
    {
        try
        {
            return drag.IsDraggingItem && Amount?.GetValue(drag) is int amount ? amount : 0;
        }
        catch
        {
            return 0;
        }
    }

    // Only call after the terminal's native raycast shield has won this pointer hit.
    // Cancel calls EndDrag; clearing stale placement prevents it committing to the previous hover.
    internal static bool CancelWithoutPlacement(ItemMouseDragHandler drag)
    {
        if (LastItem == null || LastInventory == null || LastSlot == null || !drag.IsDraggingItem)
        {
            return false;
        }

        LastItem.SetValue(drag, null);
        LastInventory.SetValue(drag, null);
        LastSlot.SetValue(drag, null);
        return drag.Cancel() && !drag.IsDraggingItem;
    }
}
