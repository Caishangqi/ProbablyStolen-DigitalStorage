using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Puts a custom window on the native window layer and keeps native item drags working over it; shared by the terminal
/// and the partition editor.
/// </summary>
internal static class NativeWindowLayer
{
    /// <summary>
    /// Moves the window under a private viewport in <c>RenderHandler.windowRoot</c>. Native dragged ghosts and lore are
    /// drawn in <c>overlayRoot</c>, above that layer, so a dragged item stays visible over the window instead of being
    /// covered by it. Returns the viewport, or null when the native layer is unavailable and the window is unchanged.
    /// </summary>
    /// <param name="window">The window to move.</param>
    /// <param name="viewportName">The viewport object name.</param>
    /// <param name="diagnostics">Records the window size and position, or null.</param>
    /// <param name="raised">
    /// Whether the window joins the raised layer of focused native windows. A native window that gains focus moves to
    /// <c>FloatingWindowRoot</c> on a canvas above <c>windowRoot</c> (UIFamilyLayer.BringWindowsToFront/Adopt), so a
    /// window opened from it would otherwise open behind it. In that root both order by sibling: this window opens in
    /// front, and a click on either brings it forward (the drag handler's <c>moveFrontObjects</c>, PixelWindow.ToFront).
    /// Falls back to <c>windowRoot</c> before the raised layer exists.
    /// </param>
    internal static RectTransform? Attach(CustomUIWindow window, string viewportName, TerminalDiagnostics? diagnostics = null,
        bool raised = false)
    {
        var native = RenderHandler.current;
        if (window == null || native == null || native.windowRoot == null) return null;
        var parent = (raised ? RaisedRoot() : null) ?? native.windowRoot.rectTransform;
        var viewport = new GameObject(viewportName, typeof(RectTransform)).GetComponent<RectTransform>();
        viewport.SetParent(parent, false);
        viewport.SetAsLastSibling();
        viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0.5f, 0.5f);
        window.root.transform.SetParent(viewport, false);
        window.Rect.anchorMin = window.Rect.anchorMax = window.Rect.pivot = new Vector2(0.5f, 0.5f);
        window.Rect.anchoredPosition = Vector2.zero;

        // UIDragHandler caches its Canvas in Awake, so bind a fresh instance after reparenting.
        var previousDrag = window.root.GetComponent<UIDragHandler>();
        if (previousDrag != null)
        {
            previousDrag.enabled = false;
            Object.Destroy(previousDrag);
        }

        var drag = window.root.AddComponent<UIDragHandler>();
        // The window layer's canvas scales differently from the overlay; NativeWindowSizer fits and clamps the window,
        // so the drag handler does not clamp.
        drag.ignoreClamp = true;
        drag.moveFrontObjects = new List<GameObject> { viewport.gameObject };
        if (window.frame != null) window.frame.dragHandle = drag;
        window.root.AddComponent<NativeWindowSizer>().Initialize(viewport, diagnostics);
        window.root.AddComponent<RaiseOnPress>().Initialize(viewport);
        return viewport;
    }

    private static RectTransform? RaisedRoot() =>
        UIFamilyLayer.RaisedCanvas == null ? null : UIFamilyLayer.RaisedCanvas.transform.Find("FloatingWindowRoot") as RectTransform;

    /// <summary>
    /// Adds a transparent native render node behind the window's controls. RenderHandler skips ordinary graphics, so
    /// without it the native drag search passes through the window to the inventory behind it; the node is not an
    /// inventory, so nothing can be placed on it. It stays behind the controls: native event interfaces on input or
    /// button children would steal their events.
    /// </summary>
    internal static TreeNodeRender AttachInputShield(CustomUIWindow window, string name)
    {
        var shield = TreeNodeRender.InstantiateAsRoot(window.Rect);
        shield.gameObject.name = name;
        shield.rectTransform.anchorMin = Vector2.zero;
        shield.rectTransform.anchorMax = Vector2.one;
        shield.rectTransform.offsetMin = shield.rectTransform.offsetMax = Vector2.zero;
        shield.rectTransform.SetAsFirstSibling();
        shield.image.sprite = null;
        shield.image.color = new Color(0f, 0f, 0f, 0.004f);
        shield.image.raycastTarget = true;
        // Render node prefabs start with the Image disabled; GraphicRaycaster skips disabled graphics.
        shield.image.enabled = true;
        shield.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return shield;
    }

    /// <summary>
    /// Keeps pointer drags that start on <paramref name="target"/> from moving the window. A draggable window's
    /// <c>UIDragHandler</c> sits on its root (CustomUIBuilder.SetDraggable), and Unity sends a drag to the nearest
    /// object that handles it, so a control without drag handlers would drag the whole window.
    /// </summary>
    internal static void BlockWindowDrag(GameObject target)
    {
        if (target != null && target.GetComponent<WindowDragBlocker>() == null) target.AddComponent<WindowDragBlocker>();
    }

    /// <summary>
    /// Brings the window forward when a press lands anywhere on it, as a native window comes to the front on any click
    /// (PixelWindow.ToFront). The drag handler alone raises it only for presses on the frame, since buttons and slots
    /// take the pointer press event themselves.
    /// </summary>
    private sealed class RaiseOnPress : MonoBehaviour
    {
        private readonly List<RaycastResult> _hits = new();
        private RectTransform? _viewport;

        internal void Initialize(RectTransform viewport) => _viewport = viewport;

        private void Update()
        {
            if (_viewport == null || !Input.GetMouseButtonDown(0) || EventSystem.current == null) return;
            _hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, _hits);
            // Only the topmost hit counts: a press on a window lying over this one belongs to that window.
            if (_hits.Count > 0 && _hits[0].gameObject.transform.IsChildOf(transform)) _viewport.SetAsLastSibling();
        }
    }

    private sealed class WindowDragBlocker : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
        }
    }
}
