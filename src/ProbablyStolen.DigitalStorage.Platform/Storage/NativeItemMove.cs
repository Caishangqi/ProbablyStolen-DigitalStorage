using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Platform.Diagnostics;

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
    private readonly Func<GridShape?>? _checkedPlacement;
    private readonly Func<GridShape?>? _checkedReturnPlacement;
    private GridShape? _checkedShape;

    private NativeItemMove(GameItem item, GameInventory? source, GridShape originalShape, GameInventory? target,
        GridShape? targetShape, Func<GridShape?>? checkedPlacement = null, Func<GridShape?>? checkedReturnPlacement = null)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _source = source;
        _originalShape = originalShape;
        _target = target;
        _targetShape = targetShape;
        _checkedPlacement = checkedPlacement;
        _checkedReturnPlacement = checkedReturnPlacement;
    }

    /// <summary>A move of an item from its current inventory into a target at a planned shape.</summary>
    internal static NativeItemMove Into(GameItem item, GameInventory target, GridShape targetShape) =>
        new(item, item.parentInventory, item.modifiedShape, target, targetShape);

    internal static NativeItemMove IntoCheckedSlot(GameItem item, GameSlotInventory target,
        Func<GridShape?> checkedPlacement, Func<GridShape?> checkedReturnPlacement) =>
        new(item, item.parentInventory, item.modifiedShape, target, null, checkedPlacement, checkedReturnPlacement);

    /// <summary>Recovery for an item that left <paramref name="source"/> at <paramref name="originalShape"/>.</summary>
    internal static NativeItemMove ReturnTo(GameItem item, GameInventory? source, GridShape originalShape) =>
        new(item, source, originalShape, null, null);

    // Callbacks of this mod's own moves can change the moved item; the move confirms that root itself.
    public bool Detach()
    {
        using var quiet = StoredItemChanges.Silence();
        return _source == null || _source.Expel(_item);
    }

    public bool CanAttach() => _checkedPlacement == null || (_checkedShape = _checkedPlacement()) != null;

    public void Attach()
    {
        if (_target == null || (_checkedShape ?? _targetShape) is not { } shape) return;
        using var quiet = StoredItemChanges.Silence();
        _item.modifiedShape = shape;
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
        if (_source == null || _item.parentInventory != null || _item is GameItemElement element && element.IsDestroyed()) return;
        var shape = _originalShape;
        if (_checkedReturnPlacement != null)
        {
            if (_checkedReturnPlacement() is not { } accepted) return;
            shape = accepted;
        }
        else if (_source is GameGridInventory grid)
        {
            if (!grid.CheckShapePlacement(_item, _originalShape, new List<GameItem>())) return;
        }
        else if (_source is GameSlotInventory slot)
        {
            if (slot.childItem != null) return;
        }
        else return;
        using var quiet = StoredItemChanges.Silence();
        _item.modifiedShape = shape;
        _source.UncheckedAccept(_item);
    }

    /// <summary>Logs a move that did not end cleanly; identifiers and counts only.</summary>
    internal static void Report(ItemMoveResult result, GameItem item, string operation, TransferOperation? aggregate = null)
    {
        if (result.Error == null && result.Outcome is ItemMoveOutcome.Moved or ItemMoveOutcome.Unchanged
                or ItemMoveOutcome.Returned or ItemMoveOutcome.AlreadySafe)
        {
            return;
        }

        if (aggregate != null)
        {
            if (result.Error != null) aggregate.Record(result.Error);
            if (result.Outcome == ItemMoveOutcome.Unowned) aggregate.RecordUnowned();
            return;
        }

        var message = $"Digital Storage: {operation} item={item.identifier} units={item.unitCount} " +
            $"outcome={result.Outcome} error={result.Error?.GetType().Name ?? "none"}";
        if (result.Outcome == ItemMoveOutcome.Unowned) UnityEngine.Debug.LogError(message);
        else UnityEngine.Debug.LogWarning(message);
    }
}
