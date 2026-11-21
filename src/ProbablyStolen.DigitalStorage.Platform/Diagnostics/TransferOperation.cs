using System;
using System.Diagnostics;

namespace ProbablyStolen.DigitalStorage.Platform.Diagnostics;

internal sealed class TransferOperation
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    internal TransferOperation(long id, string mode, long requestedPieces) =>
        (Id, Mode, RequestedPieces) = (id, mode, requestedPieces);

    internal long Id { get; }
    internal string Mode { get; }
    internal long RequestedPieces { get; }
    internal long ElapsedMilliseconds => ElapsedMillisecondsSince(_startedAt);
    internal bool Unowned { get; private set; }

    internal static long ElapsedMillisecondsSince(long timestamp) =>
        (long)((Stopwatch.GetTimestamp() - timestamp) * (1000d / Stopwatch.Frequency));

    internal void RecordUnowned() => Unowned = true;

    internal string? ExceptionType { get; private set; }
    internal StoredRootIdentityMismatch? Mismatch { get; private set; }

    internal void Record(Exception error)
    {
        if (error is StoredRootIdentityMismatch mismatch) Mismatch ??= mismatch;
        else ExceptionType ??= error.GetType().Name;
    }
}
