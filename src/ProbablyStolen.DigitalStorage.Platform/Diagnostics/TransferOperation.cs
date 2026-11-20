using System;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal sealed class TransferOperation
{
    internal TransferOperation(long id, string mode, long requestedPieces) =>
        (Id, Mode, RequestedPieces) = (id, mode, requestedPieces);

    internal long Id { get; }
    internal string Mode { get; }
    internal long RequestedPieces { get; }
    internal bool Unowned { get; private set; }

    internal void RecordUnowned() => Unowned = true;

    internal string? ExceptionType { get; private set; }
    internal StoredRootIdentityMismatch? Mismatch { get; private set; }

    internal void Record(Exception error)
    {
        if (error is StoredRootIdentityMismatch mismatch) Mismatch ??= mismatch;
        else ExceptionType ??= error.GetType().Name;
    }
}
