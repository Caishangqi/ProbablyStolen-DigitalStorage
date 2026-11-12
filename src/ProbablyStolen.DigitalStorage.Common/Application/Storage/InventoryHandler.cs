using System;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Transfers;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// Wraps one storage when it is mounted in a network: it gates insertion and
/// extraction and decides whether the storage is preferred for a type.
/// </summary>
public sealed class InventoryHandler : IRootStorage
{
    private readonly IRootStorage _inner;
    private readonly CellPartition _partition;

    /// <summary>Creates a handler.</summary>
    /// <param name="inner">The wrapped storage.</param>
    /// <param name="partition">The partition of the wrapped storage.</param>
    /// <param name="allowInsertion">False for cells whose migration was refused; they only release items.</param>
    /// <param name="allowExtraction">Whether items may leave.</param>
    public InventoryHandler(IRootStorage inner, CellPartition partition, bool allowInsertion = true, bool allowExtraction = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _partition = partition ?? throw new ArgumentNullException(nameof(partition));
        AllowInsertion = allowInsertion;
        AllowExtraction = allowExtraction;
    }

    /// <summary>Gets whether items may enter.</summary>
    public bool AllowInsertion { get; }

    /// <summary>Gets whether items may leave.</summary>
    public bool AllowExtraction { get; }

    /// <summary>
    /// Gets whether a whitelist lists the type or the storage already holds it. A blacklist never makes a storage
    /// preferred; only holding the type does.
    /// </summary>
    public bool IsPreferredStorageFor(ItemKey what, IActionSource source)
    {
        if (what == null) return false;
        if (_partition.IsPreferred(what)) return true;
        return _inner.IsPreferredStorageFor(what, source);
    }

    /// <inheritdoc />
    public InsertResult Insert(IRootSource roots, Actionable mode, IActionSource source) =>
        AllowInsertion
            ? _inner.Insert(roots, mode, source)
            : new InsertResult(0, TransferStep.Rejected(StoreRejection.ReadOnly), new StorageChangeSet());

    /// <inheritdoc />
    public ExtractResult Extract(ItemKey what, long amount, IRootSink destination, Actionable mode, IActionSource source) =>
        AllowExtraction
            ? _inner.Extract(what, amount, destination, mode, source)
            : new ExtractResult(0, TransferStep.SourceEmpty, new StorageChangeSet());

    /// <inheritdoc />
    public void GetAvailableStacks(KeyCounter output) => _inner.GetAvailableStacks(output);

    /// <inheritdoc />
    public string GetDescription() => _inner.GetDescription();
}
