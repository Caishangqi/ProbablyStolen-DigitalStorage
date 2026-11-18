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
                GameGridInventory? candidate;
                if (element is GameGridInventory grid)
                    candidate = grid;
                else if (element is GameItemElement item && !item.IsDestroyed() && GeneralHelper.MayPlayerInsertInto(item))
                    candidate = UniqueGrid(item.child);
                else if (element is PixelWindow window)
                    candidate = UniqueGrid(window);
                else
                    return false;
                if (candidate == null || candidate.handler == null ||
                    (element is GameGridInventory && !candidate.handler.gameObject.activeInHierarchy) ||
                    candidate.IsInsertLocked() || !GeneralHelper.MayPlayerInsertInto(candidate)) return false;
                GridDropAnchor? anchor = null;
                Vector2? worldPointer = null;
                if (element is GameGridInventory)
                {
                    var rect = candidate.rectTransform;
                    var canvas = rect.GetComponentInParent<Canvas>();
                    var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(rect,
                            new Vector2(pointer.X, pointer.Y), camera, out var world) ||
                        !new PointerPoint(world.x, world.y).IsValid) return false;
                    var local = rect.InverseTransformPoint(world);
                    if (!GridDropAnchor.TryFromLocal(new PointerPoint(local.x, -local.y), UISettings.current.gridSize,
                            candidate.inventoryShape.width, candidate.inventoryShape.height, out var cell) ||
                        candidate.inventoryShape.Intersects(cell.X, cell.Y)) return false;
                    anchor = cell;
                    worldPointer = world;
                }
                target = new InventoryDropTarget(candidate, anchor, worldPointer, _diagnostics);
                state = TerminalDragTargetState.Pending;
                return true;
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

    private static GameGridInventory? UniqueGrid(GraphNodeStorage? root)
    {
        if (root == null) return null;
        var seen = new HashSet<GraphNodeStorage>();
        var pending = new Stack<GraphNodeStorage>();
        pending.Push(root);
        GameGridInventory? found = null;
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!seen.Add(node)) continue;
            if (node is GameGridInventory grid)
            {
                if (found != null && !ReferenceEquals(found, grid)) return null;
                found = grid;
                continue;
            }
            if (node is GameInventory || node is GameItem) continue;
            foreach (var child in node.children) pending.Push(child);
        }
        return found;
    }
}
