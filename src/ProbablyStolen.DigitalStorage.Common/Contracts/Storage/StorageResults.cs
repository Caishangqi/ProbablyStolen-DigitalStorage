using System;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Storage;

/// <summary>The result of <see cref="IStorage.Insert"/>.</summary>
public sealed class InsertResult
{
    /// <summary>Creates a result.</summary>
    public InsertResult(long insertedUnits, TransferStep stoppedBy, StorageChangeSet changes)
    {
        InsertedUnits = insertedUnits >= 0 ? insertedUnits : throw new ArgumentOutOfRangeException(nameof(insertedUnits));
        StoppedBy = stoppedBy;
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>Gets the units accepted.</summary>
    public long InsertedUnits { get; }

    /// <summary>Gets the step that ended the insertion; <see cref="TransferOutcome.SourceEmpty"/> when every root went in.</summary>
    public TransferStep StoppedBy { get; }

    /// <summary>Gets the net unit changes of this storage.</summary>
    public StorageChangeSet Changes { get; }
}

/// <summary>The result of <see cref="IStorage.Extract"/>.</summary>
public sealed class ExtractResult
{
    /// <summary>Creates a result.</summary>
    public ExtractResult(long extractedUnits, TransferStep stoppedBy, StorageChangeSet changes)
    {
        ExtractedUnits = extractedUnits >= 0 ? extractedUnits : throw new ArgumentOutOfRangeException(nameof(extractedUnits));
        StoppedBy = stoppedBy;
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>Gets the units moved out; whole roots, so it may exceed the request.</summary>
    public long ExtractedUnits { get; }

    /// <summary>Gets the step that ended the extraction; <see cref="TransferOutcome.Moved"/> when the request was met.</summary>
    public TransferStep StoppedBy { get; }

    /// <summary>Gets the net unit changes of this storage.</summary>
    public StorageChangeSet Changes { get; }
}
