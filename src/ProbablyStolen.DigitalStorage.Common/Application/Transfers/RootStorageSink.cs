using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>Offers one actual root to an <see cref="IRootStorage"/> through its existing insertion contract.</summary>
public sealed class RootStorageSink : IRootSink
{
    private readonly IRootStorage _destination;

    /// <summary>Creates a sink over the destination storage.</summary>
    public RootStorageSink(IRootStorage destination)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
    }

    /// <inheritdoc />
    public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
    {
        if (root.Key == null) throw new ArgumentException("A root snapshot is required.", nameof(root));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (mode != Actionable.Simulate && root.Origin == null) return TransferStep.Unowned;

        var origin = root.Origin ?? new DetachedRootSource(root);
        var result = _destination.Insert(new SingleRootSource(origin, root), mode, source);
        if (result.InsertedUnits > root.Units)
            throw new InvalidOperationException("The destination reported more units than the offered root contains.");

        switch (result.StoppedBy.Outcome)
        {
            case TransferOutcome.Rejected:
                if (result.InsertedUnits != 0)
                    throw new InvalidOperationException("The destination reported inserted units while rejecting the offered root.");
                return result.StoppedBy;
            case TransferOutcome.Returned:
                if (result.InsertedUnits != 0)
                    throw new InvalidOperationException("The destination reported inserted units after returning the offered root.");
                return TransferStep.Returned;
            case TransferOutcome.Unowned:
                if (result.InsertedUnits != 0)
                    throw new InvalidOperationException("The destination reported inserted units while root ownership is unresolved.");
                return TransferStep.Unowned;
            case TransferOutcome.SourceEmpty:
                if (result.InsertedUnits == root.Units) return TransferStep.Moved;
                if (result.InsertedUnits == 0) return TransferStep.SourceEmpty;
                throw new InvalidOperationException("The destination reported partial insertion without verifying the remaining root's owner.");
            case TransferOutcome.Moved:
                if (result.InsertedUnits == root.Units) return TransferStep.Moved;
                if (result.InsertedUnits == 0)
                    throw new InvalidOperationException("The destination reported a moved root without accepting units.");
                throw new InvalidOperationException("The destination reported a partial root move without an ownership result.");
            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }
    }

    private sealed class DetachedRootSource : IRootSource
    {
        private readonly RootSnapshot _root;

        internal DetachedRootSource(RootSnapshot root) => _root = root;

        public bool TryPeekFirst(out RootSnapshot root)
        {
            root = _root;
            return true;
        }

        public IEnumerable<RootSnapshot> Snapshot()
        {
            yield return _root;
        }
    }
}
