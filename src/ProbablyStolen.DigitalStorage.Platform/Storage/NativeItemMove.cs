using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

/// <summary>
/// Native steps for <see cref="ItemMove"/>. Source return checks the original placement before bypassing locks.
/// </summary>
internal sealed class NativeItemMove : IItemMovePort
{
    private readonly GameItem _item;
    private readonly GameInventory? _source;
    private readonly GridShape _originalShape;
    private readonly GameInventory? _target;
    private readonly GridShape? _targetShape;

    private NativeItemMove(GameItem item, GameInventory? source, GridShape originalShape, GameInventory? target,
        GridShape? targetShape)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _source = source;
        _originalShape = originalShape;
        _target = target;
        _targetShape = targetShape;
    }

    /// <summary>A move of an item from its current inventory into a target at a planned shape.</summary>
    internal static NativeItemMove Into(GameItem item, GameInventory target, GridShape targetShape) =>
        new(item, item.parentInventory, item.modifiedShape, target, targetShape);

    /// <summary>Recovery for an item that left <paramref name="source"/> at <paramref name="originalShape"/>.</summary>
    internal static NativeItemMove ReturnTo(GameItem item, GameInventory? source, GridShape originalShape) =>
        new(item, source, originalShape, null, null);

    public bool Detach() => _source == null || _source.Expel(_item);

    public void Attach()
    {
        if (_target == null || _targetShape == null) return;
        _item.modifiedShape = _targetShape;
        _target.UncheckedAccept(_item);
    }

    public bool IsAtTarget() => _target != null && IsOwnedBy(_item, _target);

    public bool IsSafe() => IsSafe(_item);

    internal static bool IsSafe(GameItem item) => item.unitCount <= 0 ||
        (!(item is GameItemElement element) || !element.IsDestroyed()) &&
        item.parentInventory != null && IsOwnedBy(item, item.parentInventory);

    private static bool IsOwnedBy(GameItem item, GameInventory inventory) =>
        (!(item is GameItemElement element) || !element.IsDestroyed()) &&
        item.parentInventory == inventory && inventory.childItems.Contains(item);

    public void ReturnToSource()
    {
        if (_source == null || _item.parentInventory != null ||
            _item is GameItemElement element && element.IsDestroyed()) return;
        if (_source is GameGridInventory grid)
        {
            if (!grid.CheckShapePlacement(_item, _originalShape, new List<GameItem>())) return;
        }
        else if (_source is GameSlotInventory slot)
        {
            if (slot.childItem != null) return;
        }
        else return;
        _item.modifiedShape = _originalShape;
        _source.UncheckedAccept(_item);
    }

    /// <summary>Logs a move that did not end cleanly; identifiers and counts only.</summary>
    internal static void Report(ItemMoveResult result, GameItem item, string operation)
    {
        if (result.Error == null && result.Outcome is ItemMoveOutcome.Moved or ItemMoveOutcome.Unchanged
                or ItemMoveOutcome.Returned or ItemMoveOutcome.AlreadySafe)
        {
            return;
        }

        var message = $"Digital Storage: {operation} item={item.identifier} units={item.unitCount} " +
            $"outcome={result.Outcome} error={result.Error?.GetType().Name ?? "none"}";
        if (result.Outcome == ItemMoveOutcome.Unowned) UnityEngine.Debug.LogError(message);
        else UnityEngine.Debug.LogWarning(message);
    }
}
