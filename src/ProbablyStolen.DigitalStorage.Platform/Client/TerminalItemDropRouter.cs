using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyStolen.DigitalStorage.Platform.Client;

/// <summary>
/// Adds storing as the last step of a native drop onto a terminal item, after insert, stack and the dragged item's
/// own use. The native drop runs unchanged; an item it left exactly where it was is stored afterwards, so a use that
/// applies to the terminal, such as renaming it with the blue labeler, still wins. Single and group drags follow the
/// same rule item by item, as the native handlers do. Every entry point swallows its own failures: the native drag
/// must finish whatever happens here.
/// </summary>
public static class TerminalItemDropRouter
{
    private static GameItem? _paintedSource;
    private static GameItem? _paintedTerminal;
    private static bool _paintedAccepted;
    private static GameItem? _groupPainted;
    [ThreadStatic] private static int _groupDropDepth;

    internal static bool IsGroupDropInProgress => _groupDropDepth > 0;

    /// <summary>Called before <c>ItemMouseDragHandler.EndDrag</c>; returns the pending store, or null.</summary>
    public static object? BeginSingleDrop(ItemMouseDragHandler drag, bool cancelled)
    {
        try
        {
            ClearPaint();
            if (cancelled || drag == null || !drag.IsDraggingItem) return null;
            var drop = TerminalItemDrop.Find(NativeTerminalDragAccess.GetDropItem(drag));
            var source = drag.GetCurrentItem();
            if (drop == null || source == null || !drop.NeedsFallback(source)) return null;
            return new PendingDrop(drop, new[] { new PendingItem(source, NativeTerminalDragAccess.GetAmount(drag)) });
        }
        catch (Exception error)
        {
            Report("single drop check", error);
            return null;
        }
    }

    /// <summary>Called before <c>ItemMultiSelectHandler.EndGroupDrag</c>; opens release-only capacity gating and captures fallbacks.</summary>
    public static object? BeginGroupDrop(ItemMultiSelectHandler group)
    {
        _groupPainted = null;
        if (group == null || !group.IsGroupDragging) return null;
        var scope = new GroupDropScope();
        try
        {
            // The same raycast the native group drop makes first, in the same frame.
            var drop = TerminalItemDrop.Find(RenderHandler.RaycastElement<GameItem>(Input.mousePosition));
            if (drop == null) return scope;
            var items = NativeTerminalDragAccess.GetGroupItems(group);
            var pending = new List<PendingItem>();
            foreach (var item in items)
            {
                if (ReferenceEquals(item, drop.Terminal)) return scope;
                // The native group drop skips these items before trying any step.
                if (item.IsDestroyed() || item.parentInventory == null || !item.MayRemove() || item.MaxNumRemove() < 1) continue;
                if (drop.NeedsFallback(item)) pending.Add(new PendingItem(item, item.unitCount));
            }

            scope.PendingState = pending.Count == 0 ? null : new PendingDrop(drop, pending);
            return scope;
        }
        catch (Exception error)
        {
            Report("group drop check", error);
            return scope;
        }
    }

    /// <summary>Completes pending fallbacks and closes only the group release scope that owns this state.</summary>
    public static void CompleteDrop(object? pending)
    {
        if (pending is GroupDropScope groupScope)
        {
            try
            {
                CompletePendingDrop(groupScope.PendingState as PendingDrop);
            }
            finally
            {
                groupScope.Dispose();
            }
            return;
        }

        CompletePendingDrop(pending as PendingDrop);
    }

    internal sealed class GroupDropScope : IDisposable
    {
        private bool _disposed;
        internal object? PendingState { get; set; }

        internal GroupDropScope() => _groupDropDepth++;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _groupDropDepth--;
        }
    }

    private static void CompletePendingDrop(PendingDrop? drop)
    {
        if (drop == null) return;
        foreach (var item in drop.Items)
        {
            try
            {
                if (item.StillInPlace()) drop.Drop.StoreFallback(item.Item, item.Amount);
            }
            catch (Exception error)
            {
                Report("drop fallback", error);
            }
        }
    }

    internal static bool ShouldPaintGroupStoreFeedback(bool nativeAction, bool storageCandidate) =>
        !nativeAction && storageCandidate;

    /// <summary>
    /// Called after <c>ItemMouseDragHandler.Drag</c>. Over a terminal where the drop would store, shows the store
    /// tooltip and, for an item whose own use the native hover rejected, the matching slot colour.
    /// </summary>
    public static void AfterSingleHover(ItemMouseDragHandler drag)
    {
        try
        {
            var terminal = NativeTerminalDragAccess.GetDropItem(drag);
            var drop = TerminalItemDrop.Find(terminal);
            var source = drag.GetCurrentItem();
            if (!drag.IsDraggingItem || drop == null || source == null || !drop.EndsInStore(source))
            {
                ClearPaint();
                return;
            }

            var amount = NativeTerminalDragAccess.GetAmount(drag);
            if (drop.NeedsFallback(source))
            {
                var accepted = drop.Refusal(source, amount) == null;
                if (!ReferenceEquals(_paintedSource, source) || !ReferenceEquals(_paintedTerminal, terminal) ||
                    _paintedAccepted != accepted)
                {
                    NativeTerminalDragAccess.Highlight(drag, terminal!.GetIncompleteSlotInInventory(),
                        accepted ? RenderHandler.BackgroundColor.Green : RenderHandler.BackgroundColor.Red);
                    _paintedSource = source;
                    _paintedTerminal = terminal;
                    _paintedAccepted = accepted;
                }
            }

            // Placed as the native drag places its own tooltips: right of the dragged item.
            var width = source is GameItemElement element ? element.tempShape.globalWidth : 0;
            var offset = drag.tooltipTranslation + new Vector2(width * UISettings.current.gridSize / 2f, 0f);
            OverlayHandler.current?.ShowTooltip(Input.mousePosition, offset, drop.HoverHint(source, amount));
        }
        catch (Exception error)
        {
            Report("single hover", error);
        }
    }

    /// <summary>
    /// Called after <c>ItemMultiSelectHandler.UpdateDropTarget</c>. Native insertion, stacking, or use stays green;
    /// blue marks a storage-only candidate whose capacity will be checked on release.
    /// </summary>
    public static void AfterGroupHover(ItemMultiSelectHandler group)
    {
        try
        {
            var target = NativeTerminalDragAccess.GetGroupHoverItem(group);
            var drop = TerminalItemDrop.Find(target);
            if (drop == null)
            {
                _groupPainted = null;
                return;
            }

            if (ReferenceEquals(_groupPainted, target)) return;
            _groupPainted = target;
            var items = NativeTerminalDragAccess.GetGroupItems(group);
            var nativeAction = drop.HasNativeAction(items);
            if (ShouldPaintGroupStoreFeedback(nativeAction, !nativeAction && drop.AnyPotentiallyStorable(items)))
                NativeTerminalDragAccess.HighlightGroupTarget(group, target!, RenderHandler.BackgroundColor.Blue);
        }
        catch (Exception error)
        {
            Report("group hover", error);
        }
    }

    private static void ClearPaint()
    {
        _paintedSource = null;
        _paintedTerminal = null;
    }

    private static void Report(string step, Exception error) =>
        Debug.LogWarning($"Digital Storage: terminal {step} skipped ({error.GetType().Name}); the native drag is unchanged.");

    private sealed class PendingDrop
    {
        internal PendingDrop(TerminalItemDrop drop, IReadOnlyList<PendingItem> items)
        {
            Drop = drop;
            Items = items;
        }

        internal TerminalItemDrop Drop { get; }
        internal IReadOnlyList<PendingItem> Items { get; }
    }

    private sealed class PendingItem
    {
        private readonly GameInventory? _parent;
        private readonly int _units;

        internal PendingItem(GameItem item, int amount)
        {
            Item = item;
            Amount = amount;
            _parent = item.parentInventory;
            _units = item.unitCount;
        }

        internal GameItem Item { get; }
        internal int Amount { get; }

        // The native drop did nothing with the item exactly when it is still whole in its old inventory.
        internal bool StillInPlace() =>
            !(Item is GameItemElement element && element.IsDestroyed()) && _parent != null &&
            ReferenceEquals(Item.parentInventory, _parent) && Item.unitCount == _units;
    }
}
