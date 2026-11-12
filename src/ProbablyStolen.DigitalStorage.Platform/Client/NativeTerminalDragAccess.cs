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
