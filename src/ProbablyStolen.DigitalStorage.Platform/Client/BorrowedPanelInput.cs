using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

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
/// <para>
/// What a band selects follows the running build's native rule (<see cref="Scope"/>). Builds that lock the window
/// at the press select every covered item in it. Builds that lock the first grid the band touches select only
/// within that grid, and a borrowed grid competes with native grids under the same rule.
/// </para>
/// </remarks>
public static class BorrowedPanelInput
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly List<RaycastResult> Hits = new();
    private static WeakReference<NativeWindowSizer>? _bandOrigin;
    private static readonly MethodInfo? ElementWorldRect =
        typeof(ItemMultiSelectHandler).GetMethod("GetElementWorldRect", PrivateStatic);
    private static readonly MethodInfo? OverlapsItemShape =
        typeof(ItemMultiSelectHandler).GetMethod("BandOverlapsItemShape", PrivateStatic);
    private static readonly FieldInfo? BandWindow = typeof(ItemMultiSelectHandler).GetField("bandWindow", PrivateInstance);
    private static readonly FieldInfo? BandInventory = typeof(ItemMultiSelectHandler).GetField("bandInventory", PrivateInstance);
    private static readonly FieldInfo? BandLocksOnTouch =
        typeof(ItemMultiSelectHandler).GetField("bandLocksOnTouch", PrivateInstance);
    private static readonly FieldInfo? PressPosition = typeof(ItemMultiSelectHandler).GetField("pressPosition", PrivateInstance);
    private static readonly MethodInfo? FirstGridTouched =
        typeof(ItemMultiSelectHandler).GetMethod("FindFirstGridTouched", PrivateInstance);

    private static readonly BandScope DetectedScope =
        ElementWorldRect == null || ElementWorldRect.GetParameters().Length != 1 ? BandScope.None
        : BandWindow?.FieldType == typeof(PixelWindow) && OverlapsItemShape?.GetParameters().Length == 5 ? BandScope.Window
        : BandInventory?.FieldType == typeof(GameInventory) && BandLocksOnTouch?.FieldType == typeof(bool) &&
          PressPosition?.FieldType == typeof(Vector3) && FirstGridTouched?.ReturnType == typeof(GameInventory) &&
          FirstGridTouched.GetParameters().Length == 2
            ? BandScope.Grid
            : BandScope.None;

    /// <summary>Gets the native band rule of the running build.</summary>
    public static BandScope Scope => DetectedScope;

    /// <summary>Returns the native answer, corrected for a point over a borrowed native panel.</summary>
    public static bool IsDragSurface(Vector2 screenPosition, bool nativeResult)
    {
        // The game checks the drag surface at the band press. Keep that window if the pointer later leaves it.
        var top = TopHit(screenPosition);
        var borrowed = top != null && MachineryPanelHost.IsInsideShownPanel(top.transform);
        var window = borrowed ? top!.GetComponentInParent<NativeWindowSizer>() : null;
        _bandOrigin = window == null ? null : new WeakReference<NativeWindowSizer>(window);
        if (!nativeResult || !borrowed) return nativeResult;
        var node = top!.GetComponent<TreeNodeRender>();
        var events = node == null ? null : node.eventHandler;
        return events != null && (events.mouseDragFunc.Count > 0 || events.mouseBeginDragFunc.Count > 0);
    }

    /// <summary>
    /// For <see cref="BandScope.Window"/>: extends the selection to borrowed panels while keeping the band in its
    /// origin window, which the native graph cannot identify as a window.
    /// </summary>
    public static void AddBorrowedItems(ItemMultiSelectHandler handler, Rect bandRect, List<GameItemElement> selected)
    {
        if (Scope != BandScope.Window || handler == null || _bandOrigin == null || selected == null) return;
        try
        {
            if (BandWindow!.GetValue(handler) != null) return;
            if (!_bandOrigin.TryGetTarget(out var origin) || origin == null || !origin.isActiveAndEnabled)
            {
                selected.Clear();
                return;
            }

            var visible = selected.FindAll(item => item.handler != null &&
                ReferenceEquals(item.handler.GetComponentInParent<NativeWindowSizer>(), origin) &&
                IsCovered(item, bandRect));
            var panels = MachineryPanelHost.ShownPanels;
            var candidates = 0;
            var added = 0;
            foreach (var (panel, window) in panels)
            {
                if (!ReferenceEquals(window, origin)) continue;
                var storages = new List<GraphNodeStorage>();
                panel.CollectStorage(storages);
                var inventories = new List<GameInventory>();
                foreach (var storage in storages) CollectInventories(storage, inventories, 0);
                foreach (var inventory in inventories)
                {
                    foreach (var item in inventory.childItems)
                    {
                        if (item is not GameItemElement element || visible.Contains(element)) continue;
                        candidates++;
                        if (!IsCovered(element, bandRect)) continue;
                        visible.Add(element);
                        added++;
                    }
                }
            }

            ReportMiss(panels.Count, candidates, added, bandRect);
            selected.Clear();
            selected.AddRange(visible);
        }
        catch (Exception error)
        {
            PlatformLogs.Current?.Ui.Warning("borrowed_panel_skipped", $"borrowed panel band skipped ({error.GetType().Name}); the native band is unchanged.");
        }
    }

    /// <summary>
    /// For <see cref="BandScope.Grid"/>: before the native band looks for the first grid it touches, offers the
    /// borrowed grids of the band's origin window under the same rule. Native grids live in visible native windows,
    /// so the native search never sees a borrowed one. The nearer of the two candidates is locked; the native code then
    /// selects within it as usual.
    /// </summary>
    public static void LockBorrowedGrid(ItemMultiSelectHandler handler, Rect bandRect)
    {
        if (Scope != BandScope.Grid || handler == null || _bandOrigin == null) return;
        try
        {
            if (BandInventory!.GetValue(handler) != null || BandLocksOnTouch!.GetValue(handler) is not true) return;
            if (!_bandOrigin.TryGetTarget(out var origin) || origin == null || !origin.isActiveAndEnabled) return;
            var press = (Vector2)(Vector3)PressPosition!.GetValue(handler);
            Vector2 pointer = Input.mousePosition;
            GameInventory? borrowed = null;
            var nearest = float.MaxValue;
            foreach (var (panel, window) in MachineryPanelHost.ShownPanels)
            {
                if (!ReferenceEquals(window, origin)) continue;
                var storages = new List<GraphNodeStorage>();
                panel.CollectStorage(storages);
                var inventories = new List<GameInventory>();
                foreach (var storage in storages) CollectInventories(storage, inventories, 0);
                foreach (var inventory in inventories)
                {
                    if (!TouchesOnTop(inventory, bandRect, press, pointer, out var distance) || distance >= nearest) continue;
                    borrowed = inventory;
                    nearest = distance;
                }
            }

            if (borrowed == null) return;
            // The native search runs first in its own order; a native grid at the same distance keeps the band.
            if (FirstGridTouched!.Invoke(handler, new object[] { bandRect, pointer }) is PixelElement native &&
                PressDistance(native, press) <= nearest) return;
            BandInventory.SetValue(handler, borrowed);
        }
        catch (Exception error)
        {
            PlatformLogs.Current?.Ui.Warning("borrowed_panel_skipped", $"borrowed panel band skipped ({error.GetType().Name}); the native band is unchanged.");
        }
    }

    // ItemMultiSelectHandler.FindFirstGridTouched's test for one grid: the band overlaps it, and the grid is on top at
    // the press point, the pointer or the centre of the overlap, each moved just inside the overlap.
    private static bool TouchesOnTop(GameInventory inventory, Rect bandRect, Vector2 press, Vector2 pointer, out float distance)
    {
        distance = float.MaxValue;
        if (inventory is not (GameGridInventory or GameGridScrollableInventory) || inventory is not PixelElement element ||
            element.handler == null || !element.handler.gameObject.activeInHierarchy) return false;
        var bounds = WorldRect(element);
        if (bounds.width <= 0f || bounds.height <= 0f) return false;
        var xMin = Mathf.Max(bandRect.xMin, bounds.xMin);
        var yMin = Mathf.Max(bandRect.yMin, bounds.yMin);
        var xMax = Mathf.Min(bandRect.xMax, bounds.xMax);
        var yMax = Mathf.Min(bandRect.yMax, bounds.yMax);
        if (xMax <= xMin || yMax <= yMin) return false;
        var insetX = Mathf.Min(1f, (xMax - xMin) / 2f);
        var insetY = Mathf.Min(1f, (yMax - yMin) / 2f);
        var overlap = Rect.MinMaxRect(xMin + insetX, yMin + insetY, xMax - insetX, yMax - insetY);
        if (!IsOnTop(inventory, Clamp(press, overlap)) && !IsOnTop(inventory, Clamp(pointer, overlap)) &&
            !IsOnTop(inventory, overlap.center)) return false;
        distance = (Clamp(press, bounds) - press).sqrMagnitude;
        return true;
    }

    private static float PressDistance(PixelElement element, Vector2 press) => (Clamp(press, WorldRect(element)) - press).sqrMagnitude;

    private static Rect WorldRect(PixelElement element) => (Rect)ElementWorldRect!.Invoke(null, new object[] { element });

    private static bool IsOnTop(GameInventory grid, Vector2 screenPosition) =>
        ReferenceEquals(RenderHandler.RaycastFindParentElement<GameInventory>(screenPosition), grid);

    private static Vector2 Clamp(Vector2 point, Rect rect) =>
        new(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax));

    // A panel's storage is often a grid element holding one inventory per bay; the items sit in those inventories. The
    // walk stops at items, so the pages inside a mounted cell are never offered.
    private static void CollectInventories(GraphNodeStorage node, List<GameInventory> inventories, int depth)
    {
        if (node is GameInventory inventory)
        {
            if (!inventories.Contains(inventory)) inventories.Add(inventory);
            return;
        }

        if (node is GameItem || depth > 4 || node.children == null) return;
        foreach (var child in node.children) CollectInventories(child, inventories, depth + 1);
    }

    private static float _nextMissReport;

    // Rate-limited, and only when items were found but none was covered, so a normal band writes nothing.
    private static void ReportMiss(int panels, int candidates, int added, Rect bandRect)
    {
        if (added > 0 || candidates == 0 || Time.unscaledTime < _nextMissReport) return;
        _nextMissReport = Time.unscaledTime + 1f;
        PlatformLogs.Current?.Ui.Verbose("borrowed_panel_miss", $"borrowed panel band panels={panels} candidates={candidates} added=0 " +
            $"band={bandRect.width:0}x{bandRect.height:0}");
    }

    // The same checks ItemMultiSelectHandler.ComputeBandItems makes for each candidate.
    private static bool IsCovered(GameItemElement element, Rect bandRect)
    {
        if (element.IsDestroyed() || element.parentInventory == null || element.isDebugMenu || !element.MayRemove() ||
            element.MaxNumRemove() < 1 || ItemPinHelper.IsPinned(element) || element.handler == null ||
            !element.handler.gameObject.activeInHierarchy) return false;
        var clipRect = default(Rect);
        if (element.parentInventory is PixelElement owner) clipRect = (Rect)ElementWorldRect!.Invoke(null, new object[] { owner });
        var hasClip = clipRect.width > 0f && clipRect.height > 0f;
        var arguments = new object[] { element, bandRect, clipRect, hasClip, Vector2.zero };
        if (!(bool)OverlapsItemShape!.Invoke(null, arguments)) return false;
        var top = TopHit((Vector2)arguments[4]);
        return top != null && ReferenceEquals(top.GetComponentInParent<TreeNodeRender>()?.FindParentElement(), element);
    }

    private static GameObject? TopHit(Vector2 screenPosition)
    {
        var events = EventSystem.current;
        if (events == null) return null;
        Hits.Clear();
        events.RaycastAll(new PointerEventData(events) { position = screenPosition }, Hits);
        var top = Hits.Count > 0 ? Hits[0].gameObject : null;
        Hits.Clear();
        return top;
    }
}

/// <summary>Which native band rule the running build uses.</summary>
public enum BandScope
{
    /// <summary>Neither rule's members are present; borrowed panels keep the unmodified native band.</summary>
    None,

    /// <summary>The band selects every covered item in the window under the press.</summary>
    Window,

    /// <summary>The band selects within the first grid it touches.</summary>
    Grid
}
