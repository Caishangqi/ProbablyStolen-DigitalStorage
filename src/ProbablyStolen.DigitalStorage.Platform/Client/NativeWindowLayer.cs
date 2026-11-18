using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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
    /// <param name="back">Returns whether this window handled one back action.</param>
    /// <param name="diagnostics">Records the window size and position, or null.</param>
    /// <param name="raised">
    /// Whether the window joins the raised layer of focused native windows. A native window that gains focus moves to
    /// <c>FloatingWindowRoot</c> on a canvas above <c>windowRoot</c> (UIFamilyLayer.BringWindowsToFront/Adopt), so a
    /// window opened from it would otherwise open behind it. In that root both order by sibling: this window opens in
    /// front, and a click on either brings it forward (the drag handler's <c>moveFrontObjects</c>, PixelWindow.ToFront).
    /// Falls back to <c>windowRoot</c> before the raised layer exists.
    /// </param>
    /// <param name="leftExtent">
    /// Design width drawn left of the window outside its frame (the terminal dock), fitted and clamped with it.
    /// </param>
    internal static RectTransform? Attach(CustomUIWindow window, string viewportName, Func<bool> back, TerminalDiagnostics? diagnostics = null,
        bool raised = false, float leftExtent = 0f)
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

        // UIDragHandler.Awake creates one CanvasGroup per component, so release its owned group with the old handler.
        var previousDrag = window.root.GetComponent<UIDragHandler>();
        if (previousDrag != null)
        {
            previousDrag.enabled = false;
            var previousGroup = previousDrag.GetComponent<CanvasGroup>();
            Object.DestroyImmediate(previousDrag);
            if (previousGroup != null) Object.DestroyImmediate(previousGroup);
        }

        var drag = window.root.AddComponent<UIDragHandler>();
        // The window layer's canvas scales differently from the overlay; NativeWindowSizer fits and clamps the window,
        // so the drag handler does not clamp.
        drag.ignoreClamp = true;
        drag.moveFrontObjects = new List<GameObject> { viewport.gameObject };
        if (window.frame != null) window.frame.dragHandle = drag;
        window.root.AddComponent<NativeWindowSizer>().Initialize(viewport, diagnostics, leftExtent);
        window.root.AddComponent<RaiseOnPress>().Initialize(window, viewport, back);
        return viewport;
    }

    internal static bool IsFocused(CustomUIWindow window) => WindowBackBridge.IsFocused(window);

    internal static void Detach(CustomUIWindow? window)
    {
        if (window?.root != null)
        {
            window.root.GetComponent<RaiseOnPress>()?.Release();
            window.root.GetComponent<TerminalDragBlocks>()?.Release();
        }
    }

    internal static void RebindTerminalDrag(CustomUIWindow window)
    {
        if (window?.root == null) return;
        window.root.GetComponent<TerminalDragBlocks>()?.Rebind();
    }

    private static RectTransform? RaisedRoot() =>
        UIFamilyLayer.RaisedCanvas == null ? null : UIFamilyLayer.RaisedCanvas.transform.Find("FloatingWindowRoot") as RectTransform;

    /// <summary>
    /// Adds a transparent native render node behind the window's controls. RenderHandler skips ordinary graphics, so
    /// without it the native drag search passes through the window to the inventory behind it; the node is not an
    /// inventory, so nothing can be placed on it. It stays behind the controls: native event interfaces on input or
    /// button children would steal their events.
    /// </summary>
    internal static TreeNodeRender AttachInputShield(CustomUIWindow window, string name) =>
        AttachInputShield(window.Rect, name);

    /// <summary>
    /// Adds the same shield behind the controls of <paramref name="area"/>, for parts drawn outside the window frame
    /// such as the terminal's dock and selection panel.
    /// </summary>
    internal static TreeNodeRender AttachInputShield(RectTransform area, string name)
    {
        var shield = TreeNodeRender.InstantiateAsRoot(area);
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

    internal static void RestrictTerminalDrag(CustomUIWindow window, TreeNodeRender shield, Func<Vector2, bool> isMoveArea)
    {
        shield.gameObject.AddComponent<TerminalShieldFilter>().IsMoveArea = isMoveArea;
        BlockWindowDrag(shield.gameObject);
        window.root.AddComponent<TerminalDragBlocks>().Initialize(window, shield);
    }

    internal static bool AllowsTerminalGesture(CustomUIWindow window, Vector2 point)
    {
        if (EventSystem.current == null) return false;
        var filter = window.root.GetComponentInChildren<TerminalShieldFilter>(true);
        if (filter?.IsMoveArea?.Invoke(point) == true) return false;
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        return hits.Count > 0 && hits[0].gameObject != null &&
            hits[0].gameObject.transform.IsChildOf(window.root.transform);
    }

    private sealed class TerminalShieldFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        internal Func<Vector2, bool>? IsMoveArea;
        public bool IsRaycastLocationValid(Vector2 point, Camera camera) =>
            ItemMouseDragHandler.current?.IsDraggingItem == true ||
            ItemMultiSelectHandler.current?.IsGroupDragging == true || IsMoveArea?.Invoke(point) != true;
    }

    private sealed class TerminalDragBlocks : MonoBehaviour
    {
        private CustomUIWindow? _window;
        private TreeNodeRender? _shield;

        internal void Initialize(CustomUIWindow window, TreeNodeRender shield)
        {
            _window = window;
            _shield = shield;
        }

        private void LateUpdate() => Rebind();

        internal void Rebind()
        {
            if (_window == null || _shield == null) return;
            foreach (var graphic in _window.root.GetComponentsInChildren<Graphic>(true))
            {
                var node = graphic.transform;
                if (node == transform || node == _shield.transform || node.IsChildOf(_shield.transform)) continue;
                var header = _window.frame?.titleText?.transform.parent;
                var close = _window.frame?.closeButton?.transform;
                if (header != null && (node == header || node.IsChildOf(header)) &&
                    (close == null || node != close && !node.IsChildOf(close))) continue;
                var current = node;
                var hasOwnDrag = false;
                while (current != null && current != transform)
                {
                    if (current.GetComponent<IDragHandler>() != null) { hasOwnDrag = true; break; }
                    current = current.parent;
                }
                if (!hasOwnDrag) BlockWindowDrag(graphic.gameObject);
            }
        }

        internal void Release()
        {
            enabled = false;
            _window = null;
            _shield = null;
        }

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
        private WindowBackBridge.WindowRegistration? _registration;

        internal void Initialize(CustomUIWindow window, RectTransform viewport, Func<bool> back)
        {
            _viewport = viewport;
            _registration = (WindowBackBridge.WindowRegistration)WindowBackBridge.Register(window, viewport, back);
        }

        private void OnEnable() => _registration?.Focus();

        internal void Release()
        {
            _registration?.Dispose();
            _registration = null;
        }

        private void OnDestroy() => Release();

        private void Update()
        {
            if (_viewport == null || !Input.GetMouseButtonDown(0) || EventSystem.current == null) return;
            _hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Input.mousePosition }, _hits);
            // Only the topmost hit counts: a press on a window lying over this one belongs to that window.
            if (_hits.Count > 0 && _hits[0].gameObject.transform.IsChildOf(transform)) _registration?.Focus();
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
