namespace ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

/// <summary>Why an IO Port batch stopped without a verified result.</summary>
public enum IoPortTransferFault
{
    /// <summary>Reading or simulating the move failed before anything moved.</summary>
    PreflightFailed,

    /// <summary>A native move failed and the root went back to its source.</summary>
    RootReturned,

    /// <summary>The outcome of a move could not be verified.</summary>
    OwnershipUnverified,

    /// <summary>The operation debit could not be verified.</summary>
    ReservationUnverified,

    /// <summary>Cancelling a debit could not be verified.</summary>
    RefundUnverified,

    /// <summary>Confirming the debit failed after a verified move.</summary>
    ConfirmationFailed,

    /// <summary>The job cannot count another root.</summary>
    RootCountOverflow
}
