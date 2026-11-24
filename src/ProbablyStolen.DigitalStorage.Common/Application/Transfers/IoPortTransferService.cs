using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;
using ProbablyStolen.DigitalStorage.Common.Domain.Network;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;
using ProbablyStolen.DigitalStorage.Common.Ports;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>Moves a bounded batch of whole roots for one IO Port cell job and bills it per block of roots.</summary>
public sealed class IoPortTransferService
{
    /// <summary>Roots covered by one integer power unit unless configured otherwise.</summary>
    public const int DefaultRootsPerFee = 100;

    /// <summary>Roots one machine moves per game second unless configured otherwise.</summary>
    public const int DefaultRootsPerSecond = 64;

    /// <summary>Gets why the connected network cannot run the machine, or null when its controller is online.</summary>
    public IoPortRuntimeStatus? GetControllerBlock(Grid? grid) => grid?.GetPathingService().GetControllerState() switch
    {
        ControllerState.Online => null,
        ControllerState.ControllerConflict => IoPortRuntimeStatus.ControllerConflict,
        _ => IoPortRuntimeStatus.NoController
    };

    /// <summary>
    /// Moves roots from <paramref name="source"/> to <paramref name="target"/>, type by type, until the source is empty,
    /// the target refuses every remaining type, <paramref name="maxRoots"/> roots moved, time runs out or power fails.
    /// </summary>
    /// <param name="job">The cell job; its count decides when the next fee is due and grows with each verified root.</param>
    /// <param name="source">Where roots leave.</param>
    /// <param name="target">Where roots go.</param>
    /// <param name="by">The acting machine.</param>
    /// <param name="energy">Reserves the fee of each block of roots.</param>
    /// <param name="powerBlock">The read-only fee check for an amount, including the network's base load.</param>
    /// <param name="maxRoots">The most roots this batch may move.</param>
    /// <param name="hasTime">False once the caller's time slice is spent; checked before each root.</param>
    /// <param name="cursor">
    /// Where a previous batch over the same, otherwise unchanged source and target stopped; null starts over. A batch
    /// resumes at the type it stopped on and never retries a type that already ran out or was refused.
    /// </param>
    public IoPortBatchResult Transfer(IoPortTransferJob job, IRootStorage source, IRootStorage target, IActionSource by,
        ITransferEnergyPort energy, Func<int, IoPortRuntimeStatus?> powerBlock, int maxRoots, Func<bool> hasTime,
        IoPortTransferCursor? cursor = null)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (by == null) throw new ArgumentNullException(nameof(by));
        if (energy == null) throw new ArgumentNullException(nameof(energy));
        if (powerBlock == null) throw new ArgumentNullException(nameof(powerBlock));
        if (hasTime == null) throw new ArgumentNullException(nameof(hasTime));
        if (maxRoots < 1) throw new ArgumentOutOfRangeException(nameof(maxRoots));

        var sink = new MeteredSink(new RootStorageSink(target), job, energy, powerBlock, maxRoots, hasTime);
        cursor ??= new IoPortTransferCursor();
        try
        {
            if (cursor.Keys == null)
            {
                var available = new KeyCounter();
                source.GetAvailablePieces(available);
                cursor.Keys = available.Snapshot().Keys.ToArray();
            }
            for (; cursor.Index < cursor.Keys.Count; cursor.Index++)
            {
                var step = source.Extract(cursor.Keys[cursor.Index], long.MaxValue, sink, Actionable.Modulate, by).StoppedBy;
                // A limit stops the batch inside this type; the next batch starts on it again.
                if (sink.Stop != null) break;
                switch (step.Outcome)
                {
                    case TransferOutcome.Moved:
                    case TransferOutcome.SourceEmpty:
                        continue;
                    case TransferOutcome.Rejected:
                        cursor.Refused = true;
                        continue;
                    case TransferOutcome.Returned:
                        return Fault(sink, IoPortTransferFault.RootReturned);
                    default:
                        return Fault(sink, IoPortTransferFault.OwnershipUnverified);
                }
            }
        }
        catch (RootTransferNotStartedException)
        {
            return Fault(sink, IoPortTransferFault.PreflightFailed);
        }
        catch (Exception)
        {
            return Fault(sink, sink.Moved == 0 && !sink.InFlight ? IoPortTransferFault.PreflightFailed : IoPortTransferFault.OwnershipUnverified);
        }

        if (sink.Fault is { } fault) return new IoPortBatchResult(sink.Moved, IoPortRuntimeStatus.Faulted, fault);
        if (sink.Stop is { } stop) return new IoPortBatchResult(sink.Moved, stop);
        // Nothing stopped the batch early, so every type either ran out or was refused by the target.
        return new IoPortBatchResult(sink.Moved, sink.Moved > 0 ? IoPortRuntimeStatus.Working :
            cursor.Refused ? IoPortRuntimeStatus.WaitingForCapacity : IoPortRuntimeStatus.NoEligibleContent, settled: true);
    }

    /// <summary>Checks whether an input cell is done under the completion mode.</summary>
    /// <param name="mode">The completion mode.</param>
    /// <param name="cell">The input cell as read for this batch.</param>
    /// <param name="batch">This callback's batch for the cell, or null when none ran.</param>
    public bool IsCompletionSatisfied(IoPortCompletionMode mode, BasicCellInventory cell, IoPortBatchResult? batch)
    {
        if (cell == null) throw new ArgumentNullException(nameof(cell));
        if (batch?.Fault != null) return false;
        return mode switch
        {
            IoPortCompletionMode.Empty => cell.GetStoredItemCount() == 0,
            IoPortCompletionMode.Full => cell.GetStatus() == CellState.Full,
            IoPortCompletionMode.WorkComplete => batch is { Settled: true, MovedRoots: 0 },
            _ => false
        };
    }

    private static IoPortBatchResult Fault(MeteredSink sink, IoPortTransferFault fault) =>
        new(sink.Moved, IoPortRuntimeStatus.Faulted, sink.Fault ?? fault);

    // Counts verified roots and holds the fee for each block of roots until the first root's move is confirmed.
    private sealed class MeteredSink : IRootSink
    {
        private readonly IRootSink _inner;
        private readonly IoPortTransferJob _job;
        private readonly ITransferEnergyPort _energy;
        private readonly Func<int, IoPortRuntimeStatus?> _powerBlock;
        private readonly int _maxRoots;
        private readonly Func<bool> _hasTime;

        internal MeteredSink(IRootSink inner, IoPortTransferJob job, ITransferEnergyPort energy,
            Func<int, IoPortRuntimeStatus?> powerBlock, int maxRoots, Func<bool> hasTime)
        {
            (_inner, _job, _energy, _powerBlock, _maxRoots, _hasTime) = (inner, job, energy, powerBlock, maxRoots, hasTime);
        }

        internal int Moved { get; private set; }
        internal IoPortRuntimeStatus? Stop { get; private set; }
        internal IoPortTransferFault? Fault { get; private set; }
        internal bool InFlight { get; private set; }

        public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
        {
            if (Stop != null) return Halt();
            if (mode == Actionable.Simulate)
            {
                if (Moved >= _maxRoots || !_hasTime()) return Halt(IoPortRuntimeStatus.Working);
                if (_job.CommittedRootCount == long.MaxValue) return Halt(IoPortRuntimeStatus.Faulted, IoPortTransferFault.RootCountOverflow);
                if (_powerBlock(_job.NextOperationFee) is { } blocked) return Halt(blocked);
                return _inner.Accept(root, mode, source);
            }

            ITransferEnergyReservation? reservation = null;
            var fee = _job.NextOperationFee;
            if (fee != 0)
            {
                bool reserved;
                try { reserved = _energy.TryReserve(fee, out reservation); }
                catch (Exception) { return Halt(IoPortRuntimeStatus.Faulted, IoPortTransferFault.ReservationUnverified); }
                if (reserved != (reservation != null)) return Halt(IoPortRuntimeStatus.Faulted, IoPortTransferFault.ReservationUnverified);
                if (!reserved) return Halt(IoPortRuntimeStatus.Unpowered);
            }

            InFlight = true;
            var step = _inner.Accept(root, mode, source);
            InFlight = false;
            if (!step.IsMoved)
            {
                if (!Cancel(reservation)) Halt(IoPortRuntimeStatus.Faulted, IoPortTransferFault.RefundUnverified);
                return step;
            }

            Moved++;
            _job.CommittedRootCount++;
            try { reservation?.Commit(); }
            catch (Exception) { Halt(IoPortRuntimeStatus.Faulted, IoPortTransferFault.ConfirmationFailed); }
            return step;
        }

        private TransferStep Halt(IoPortRuntimeStatus? status = null, IoPortTransferFault? fault = null)
        {
            Stop ??= status;
            Fault ??= fault;
            return TransferStep.Rejected(StoreRejection.RequestFilled);
        }

        private static bool Cancel(ITransferEnergyReservation? reservation)
        {
            try { return reservation == null || reservation.Cancel(); }
            catch (Exception) { return false; }
        }
    }
}

/// <summary>
/// The types one job still has to try within a session whose source and target no other operation changes: the type
/// list is read once and each batch resumes where the last stopped, so a drained or refused type is not tried again.
/// </summary>
public sealed class IoPortTransferCursor
{
    internal IReadOnlyList<ItemKey>? Keys { get; set; }
    internal int Index { get; set; }
    internal bool Refused { get; set; }
}

/// <summary>The fee counter of one input cell's job.</summary>
public sealed class IoPortTransferJob
{
    /// <summary>Creates a job, optionally restoring its verified count.</summary>
    /// <param name="committedRootCount">The verified roots already moved in this job.</param>
    /// <param name="rootsPerFee">The roots one power unit pays for; the unit is charged before the first root of each block.</param>
    public IoPortTransferJob(long committedRootCount = 0, int rootsPerFee = IoPortTransferService.DefaultRootsPerFee)
    {
        if (committedRootCount < 0) throw new ArgumentOutOfRangeException(nameof(committedRootCount));
        if (rootsPerFee < 1) throw new ArgumentOutOfRangeException(nameof(rootsPerFee));
        CommittedRootCount = committedRootCount;
        RootsPerFee = rootsPerFee;
    }

    /// <summary>Gets the verified roots moved in this job.</summary>
    public long CommittedRootCount { get; internal set; }

    /// <summary>Gets the roots one power unit pays for.</summary>
    public int RootsPerFee { get; }

    /// <summary>Gets the integer charge due before the next root.</summary>
    public int NextOperationFee => CommittedRootCount % RootsPerFee == 0 ? 1 : 0;
}

/// <summary>The outcome of one batch.</summary>
public sealed class IoPortBatchResult
{
    internal IoPortBatchResult(int movedRoots, IoPortRuntimeStatus status, IoPortTransferFault? fault = null, bool settled = false)
    {
        MovedRoots = movedRoots;
        Status = status;
        Fault = fault;
        Settled = settled;
    }

    /// <summary>Gets the verified roots moved.</summary>
    public int MovedRoots { get; }

    /// <summary>Gets the status the batch ended with.</summary>
    public IoPortRuntimeStatus Status { get; }

    /// <summary>Gets the fault, if the batch stopped on one.</summary>
    public IoPortTransferFault? Fault { get; }

    /// <summary>Gets whether every type ran out or was refused, rather than a limit or failure stopping the batch.</summary>
    public bool Settled { get; }
}
