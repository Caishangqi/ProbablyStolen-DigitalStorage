using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

/// <summary>How one root transfer step ended.</summary>
public enum TransferOutcome
{
    /// <summary>The root reached the target (or, when simulating, would).</summary>
    Moved = 0,

    /// <summary>The source has no root left.</summary>
    SourceEmpty = 1,

    /// <summary>The target refused the root; nothing moved.</summary>
    Rejected = 2,

    /// <summary>The native move failed and the root went back to its original position.</summary>
    Returned = 3,

    /// <summary>The native move and its return both failed; the root has no verified owner.</summary>
    Unowned = 4
}

/// <summary>The result of one transfer step; only <see cref="TransferOutcome.Moved"/> lets a drain continue.</summary>
public readonly struct TransferStep
{
    private TransferStep(TransferOutcome outcome, StoreRejection? rejection)
    {
        Outcome = outcome;
        Rejection = rejection;
    }

    /// <summary>Gets a moved step.</summary>
    public static TransferStep Moved { get; } = new(TransferOutcome.Moved, null);

    /// <summary>Gets an empty-source step.</summary>
    public static TransferStep SourceEmpty { get; } = new(TransferOutcome.SourceEmpty, null);

    /// <summary>Gets a returned step.</summary>
    public static TransferStep Returned { get; } = new(TransferOutcome.Returned, null);

    /// <summary>Gets an unowned step.</summary>
    public static TransferStep Unowned { get; } = new(TransferOutcome.Unowned, null);

    /// <summary>Gets a rejected step with its reason.</summary>
    public static TransferStep Rejected(StoreRejection reason) => new(TransferOutcome.Rejected, reason);

    /// <summary>Gets how the step ended.</summary>
    public TransferOutcome Outcome { get; }

    /// <summary>Gets the rejection reason when <see cref="Outcome"/> is <see cref="TransferOutcome.Rejected"/>.</summary>
    public StoreRejection? Rejection { get; }

    /// <summary>Gets whether the root moved.</summary>
    public bool IsMoved => Outcome == TransferOutcome.Moved;

    /// <inheritdoc />
    public override string ToString() => Rejection is { } reason ? $"{Outcome}({reason})" : Outcome.ToString();
}
