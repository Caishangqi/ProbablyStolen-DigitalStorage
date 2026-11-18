using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

internal enum TerminalDragTargetState { None, Rejected, Pending }

internal sealed class TerminalDragTargetResolver
{
    private readonly CustomUIWindow _window;
    private readonly Func<bool> _isCurrentSession;
    private readonly Transform? _ghost;
    private readonly Transform? _band;
    private readonly TerminalDiagnostics? _diagnostics;

    internal TerminalDragTargetResolver(CustomUIWindow window, Func<bool> isCurrentSession,
        Transform? ghost = null, Transform? band = null, TerminalDiagnostics? diagnostics = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _isCurrentSession = isCurrentSession ?? throw new ArgumentNullException(nameof(isCurrentSession));
        _ghost = ghost;
        _band = band;
        _diagnostics = diagnostics;
    }

    internal bool TryResolveCandidate(PointerPoint pointer, out InventoryDropTarget? target,
        out TerminalDragTargetState state)
    {
        target = null;
        state = TerminalDragTargetState.Rejected;
        try
        {
            if (!pointer.IsValid || !_window.IsVisible || !_isCurrentSession() || EventSystem.current == null)
                return false;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
            {
                position = new Vector2(pointer.X, pointer.Y)
            }, hits);
            foreach (var hit in hits)
            {
                if (hit.gameObject == null) return false;
                var transform = hit.gameObject.transform;
                if (Within(transform, _ghost) || Within(transform, _band)) continue;
                if (Within(transform, _window.root.transform)) return false;
                var render = hit.gameObject.GetComponent<TreeNodeRender>();
                if (render == null) return false;
                var element = render.FindParentElement();
                GameInventory? candidate;
                GameItemElement? hoveredItem = null;
                if (element is GameGridInventory grid)
                    candidate = grid;
                else if (element is GameItemElement item && !item.IsDestroyed())
                {
                    candidate = ResolveContainerInventory(item.child);
                    hoveredItem = item;
                }
                else if (element is PixelWindow window)
                    candidate = ResolveContainerInventory(window);
                else if (element is GameSlotInventory slot)
                    candidate = slot;
                else
                    return false;
                if (candidate == null) return false;
                var eligible = InventoryDropTarget.IsUsable(candidate, hoveredItem);
                var validGeometry = true;
                GridDropAnchor? anchor = null;
                Vector2? worldPointer = null;
                if (candidate is GameGridInventory gridInventory && element is GameGridInventory)
                {
                    var rect = gridInventory.rectTransform;
                    var canvas = rect.GetComponentInParent<Canvas>();
                    var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    if (RectTransformUtility.ScreenPointToWorldPointInRectangle(rect,
                            new Vector2(pointer.X, pointer.Y), camera, out var world) &&
                        new PointerPoint(world.x, world.y).IsValid)
                    {
                        var local = rect.InverseTransformPoint(world);
                        if (GridDropAnchor.TryFromLocal(new PointerPoint(local.x, -local.y), UISettings.current.gridSize,
                                gridInventory.inventoryShape.width, gridInventory.inventoryShape.height, out var cell))
                        {
                            anchor = cell;
                            worldPointer = world;
                            validGeometry = !gridInventory.inventoryShape.Intersects(cell.X, cell.Y);
                        }
                        else
                        {
                            validGeometry = false;
                        }
                    }
                    else
                    {
                        validGeometry = false;
                    }
                }
                target = new InventoryDropTarget(candidate, anchor, worldPointer, _diagnostics, hoveredItem, validGeometry);
                state = eligible ? TerminalDragTargetState.Pending : TerminalDragTargetState.Rejected;
                return state == TerminalDragTargetState.Pending;
            }
            state = TerminalDragTargetState.None;
            return false;
        }
        catch
        {
            target = null;
            state = TerminalDragTargetState.Rejected;
            return false;
        }
    }

    private static bool Within(Transform child, Transform? root) =>
        root != null && (child == root || child.IsChildOf(root));

    internal static GameInventory? ResolveContainerInventory(GraphNodeStorage? root)
    {
        if (root == null) return null;
        var seen = new HashSet<GraphNodeStorage>();
        var pending = new Stack<GraphNodeStorage>();
        pending.Push(root);
        GameGridInventory? grid = null;
        GameSlotInventory? slot = null;
        var multipleSlots = false;
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node)) continue;
            if (node is GameGridInventory foundGrid)
            {
                if (grid != null) return null;
                grid = foundGrid;
                continue;
            }
            if (node is GameSlotInventory foundSlot)
            {
                multipleSlots |= slot != null;
                slot = foundSlot;
            }
            if (node is GameInventory or GameItem) continue;
            foreach (var child in node.children) pending.Push(child);
        }
        // A machine's auxiliary slots must not displace its original grid drop route.
        return (GameInventory?)grid ?? (multipleSlots ? null : slot);
    }
}
