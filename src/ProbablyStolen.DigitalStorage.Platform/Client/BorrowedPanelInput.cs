using System;
using System.Collections.Generic;
using System.Reflection;
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
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly List<RaycastResult> Hits = new();
    private static WeakReference<NativeWindowSizer>? _bandOrigin;
    private static readonly MethodInfo? ElementWorldRect =
        typeof(ItemMultiSelectHandler).GetMethod("GetElementWorldRect", PrivateStatic);
    private static readonly MethodInfo? OverlapsItemShape =
        typeof(ItemMultiSelectHandler).GetMethod("BandOverlapsItemShape", PrivateStatic);

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

    /// <summary>Extends box selection to borrowed panels while keeping the band in its origin window.</summary>
    public static void AddBorrowedItems(PixelWindow? bandWindow, Rect bandRect, List<GameItemElement> selected)
    {
        if (bandWindow != null || _bandOrigin == null || selected == null || ElementWorldRect == null ||
            OverlapsItemShape == null) return;
        try
        {
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
            Debug.LogWarning($"Digital Storage: borrowed panel band skipped ({error.GetType().Name}); the native band is unchanged.");
        }
    }

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
        Debug.Log($"Digital Storage: borrowed panel band panels={panels} candidates={candidates} added=0 " +
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
