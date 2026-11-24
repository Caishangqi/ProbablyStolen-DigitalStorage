using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Restores native box selection over native panels that a mod window borrows, such as the module bay on the terminal's
/// module page and the IO Port's cell banks.
/// </summary>
/// <remarks>
/// <c>ItemMultiSelectHandler.IsPointerOverDragSurface</c> refuses to start a band over any object with a
/// <c>UIDragHandler</c> among its parents. A draggable mod window carries one on its root, so every borrowed panel
/// inside it counted as a drag surface. Native windows move without that component, which is why the same panel
/// selects normally in its own window. Over a borrowed panel the parent check is dropped and the native node rule
/// remains: a node that handles drags itself is still a drag surface.
/// </remarks>
public static class BorrowedPanelInput
{
    private static readonly List<RaycastResult> Hits = new();

    /// <summary>Returns the native answer, corrected for a point over a borrowed native panel.</summary>
    public static bool IsDragSurface(Vector2 screenPosition, bool nativeResult)
    {
        if (!nativeResult || EventSystem.current == null) return nativeResult;
        Hits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPosition }, Hits);
        var top = Hits.Count > 0 ? Hits[0].gameObject : null;
        Hits.Clear();
        if (top == null || !MachineryPanelHost.IsInsideShownPanel(top.transform)) return nativeResult;
        var node = top.GetComponent<TreeNodeRender>();
        var events = node == null ? null : node.eventHandler;
        return events != null && (events.mouseDragFunc.Count > 0 || events.mouseBeginDragFunc.Count > 0);
    }
}
