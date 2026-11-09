using System;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>The native steps of moving one item between inventories.</summary>
public interface IItemMovePort
{
    /// <summary>Removes the item from its source. False means nothing changed.</summary>
    bool Detach();

    /// <summary>Puts the detached item into the target. May throw.</summary>
    void Attach();

    /// <summary>Gets whether the item is in the target.</summary>
    bool IsAtTarget();

    /// <summary>
    /// Gets whether the item is safe: inside some inventory, or fully merged away (no units left). A free item with
    /// units is not safe: native drag handling destroys it.
    /// </summary>
    bool IsSafe();

    /// <summary>Puts the item back into its source at its original position. May throw.</summary>
    void ReturnToSource();
}

/// <summary>How a move ended.</summary>
public enum ItemMoveOutcome
{
    /// <summary>The item reached the target.</summary>
    Moved,

    /// <summary>The source kept the item; nothing changed.</summary>
    Unchanged,

    /// <summary>The move failed and the item went back to its source.</summary>
    Returned,

    /// <summary>No inventory took the item. It is free and at risk.</summary>
    Unowned,

    /// <summary>The item already had an owner or no units left; no return was attempted.</summary>
    AlreadySafe
}

/// <summary>The result of a move, with the first error raised by a native step, if any.</summary>
public readonly struct ItemMoveResult
{
    /// <summary>Creates a result.</summary>
    public ItemMoveResult(ItemMoveOutcome outcome, Exception? error)
    {
        Outcome = outcome;
        Error = error;
    }

    /// <summary>Gets how the move ended.</summary>
    public ItemMoveOutcome Outcome { get; }

    /// <summary>Gets the first error a native step raised, or null.</summary>
    public Exception? Error { get; }
}

/// <summary>
/// Moves an item after the caller checks placement. A failed move gets one direct return to the source.
/// </summary>
public static class ItemMove
{
    /// <summary>Runs a checked move and attempts a direct return on failure.</summary>
    public static ItemMoveResult Run(IItemMovePort port)
    {
        if (port == null) throw new ArgumentNullException(nameof(port));
        Exception? error = null;
        bool detached;
        try
        {
            detached = port.Detach();
        }
        catch (Exception exception)
        {
            error = exception;
            detached = false;
        }

        if (!detached)
        {
            return port.IsSafe() ? new ItemMoveResult(ItemMoveOutcome.Unchanged, error) : Recover(port, error);
        }

        try
        {
            port.Attach();
        }
        catch (Exception exception)
        {
            error = exception;
        }

        // The outcome follows where the item is, not whether a step threw: a callback that throws after the native
        // inventory took the item still leaves it moved.
        return port.IsAtTarget() ? new ItemMoveResult(ItemMoveOutcome.Moved, error) : Recover(port, error);
    }

    /// <summary>Attempts once to return a free item to its source.</summary>
    public static ItemMoveResult Recover(IItemMovePort port, Exception? error = null)
    {
        if (port == null) throw new ArgumentNullException(nameof(port));
        if (port.IsSafe()) return new ItemMoveResult(ItemMoveOutcome.AlreadySafe, error);
        try
        {
            port.ReturnToSource();
        }
        catch (Exception exception)
        {
            error ??= exception;
        }

        if (port.IsSafe()) return new ItemMoveResult(ItemMoveOutcome.Returned, error);
        return new ItemMoveResult(ItemMoveOutcome.Unowned, error);
    }

    /// <summary>Aborts a native drag callback when its positive remainder has no confirmed owner.</summary>
    public static void RequireSafeDrag(bool isSafe)
    {
        if (!isSafe) throw new InvalidOperationException("Dragged item has no confirmed owner.");
    }
}
