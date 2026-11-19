using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using ProbablyStolen.DigitalStorage.Platform.Storage;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

internal enum TerminalDragTargetState { None, Rejected, Pending }
internal enum TerminalDropHitKind { Unsupported, Item, Inventory, Window }

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
                switch (ClassifyHit(element))
                {
                    case TerminalDropHitKind.Item:
                        var item = (GameItemElement)element!;
                        if (item.IsDestroyed() || !TryCreateItemTarget(item, _diagnostics, out target)) return false;
                        state = TerminalDragTargetState.Pending;
                        return true;
                    case TerminalDropHitKind.Unsupported:
                        return false;
                }

                // Match RenderHandler.RaycastFindParentElement<GameInventory>: the nearest semantic element
                // decides the route. Do not let an owning GameItem shadow a directly hit functional slot.
                GameInventory? candidate = element as GameInventory;
                var directInventory = candidate != null;
                GameItemElement? hoveredItem = null;
                if (candidate == null && element is PixelWindow window)
                {
                    if (window.parentItems != null && window.parentItems.Count > 0)
                    {
                        if (!TryFindUniqueContentOwner(window, out var owner) ||
                            !TryCreateItemTarget(owner, _diagnostics, out target)) return false;
                        state = TerminalDragTargetState.Pending;
                        return true;
                    }

                    candidate = ResolveContainerInventory(window);
                }
                if (candidate == null) return false;

                var eligible = InventoryDropTarget.IsUsable(candidate, hoveredItem);
                var validGeometry = true;
                GridDropAnchor? anchor = null;
                Vector2? worldPointer = null;
                if (directInventory && candidate is GameGridInventory gridInventory)
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

    internal static TerminalDropHitKind ClassifyHit(PixelElement? element) => element switch
    {
        GameItemElement => TerminalDropHitKind.Item,
        GameInventory => TerminalDropHitKind.Inventory,
        PixelWindow => TerminalDropHitKind.Window,
        _ => TerminalDropHitKind.Unsupported
    };

    internal static bool TryCreateItemTarget(GameItemElement item, TerminalDiagnostics? diagnostics,
        out InventoryDropTarget? target)
    {
        target = null;
        if (item == null || item.IsDestroyed() || item.child == null) return false;
        var inventories = ResolveSupportedInventories(item.child);
        if (inventories.Count == 0) return false;
        GameInventory? representative = null;
        foreach (var inventory in inventories)
        {
            if (!InventoryDropTarget.IsUsable(inventory, item)) continue;
            representative = inventory;
            break;
        }
        if (representative == null) return false;
        target = new InventoryDropTarget(representative, diagnostics: diagnostics, hoveredItem: item,
            itemInventories: inventories);
        return true;
    }

    private static bool TryFindUniqueContentOwner(PixelWindow window, out GameItemElement owner)
    {
        owner = null!;
        if (window.parentItems == null || window.parentItems.Count != 1) return false;
        if (window.parentItems[0] is not GameItemElement item || item.IsDestroyed() || !ReferenceEquals(item.child, window))
            return false;
        owner = item;
        return true;
    }

    private static IReadOnlyList<GameInventory> ResolveSupportedInventories(GraphNodeStorage? root)
    {
        if (root == null) return Array.Empty<GameInventory>();
        var seen = new HashSet<GraphNodeStorage>();
        var pending = new Stack<GraphNodeStorage>();
        var inventories = new List<GameInventory>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node)) continue;
            if (node is GameGridInventory or GameSlotInventory)
            {
                inventories.Add((GameInventory)node);
                continue;
            }
            if (node is GameInventory or GameItem) continue;
            foreach (var child in node.children) pending.Push(child);
        }

        return inventories;
    }

    private static bool Within(Transform child, Transform? root) =>
        root != null && (child == root || child.IsChildOf(root));

    internal static GameInventory? ResolveContainerInventory(GraphNodeStorage? root)
    {
        var inventories = ResolveSupportedInventories(root);
        if (inventories.Count == 1) return inventories[0];
        var grid = inventories.OfType<GameGridInventory>().ToArray();
        return grid.Length == 1 ? grid[0] : null;
    }
}
