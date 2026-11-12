using System;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Common.Application.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Storage;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>The result of <see cref="RootTransfer.Drain"/>.</summary>
public sealed class DrainResult
{
    internal DrainResult(int movedRoots, StorageChangeSet movedUnits, TransferStep lastStep)
    {
        MovedRoots = movedRoots;
        MovedUnits = movedUnits;
        LastStep = lastStep;
    }

    /// <summary>Gets how many roots moved.</summary>
    public int MovedRoots { get; }

    /// <summary>Gets the units moved per type; every value is positive.</summary>
    public StorageChangeSet MovedUnits { get; }

    /// <summary>
    /// Gets the step that ended the drain: <see cref="TransferOutcome.SourceEmpty"/> when the source was emptied,
    /// <see cref="TransferOutcome.Moved"/> when the step limit was reached, otherwise the failing step.
    /// </summary>
    public TransferStep LastStep { get; }

    /// <summary>Gets whether the source was emptied.</summary>
    public bool Completed => LastStep.Outcome == TransferOutcome.SourceEmpty;
}

/// <summary>
/// Moves item roots one at a time, each atomically from source to sink. There is deliberately no separate
/// "pop then store": a popped root with units and no owner is destroyed by native drag handling, would lose its
/// original position, and could not be refused by capacity rules after leaving the source.
/// </summary>
public static class RootTransfer
{
    /// <summary>Moves the source's first root into the sink: simulate first, and only then modulate.</summary>
    /// <exception cref="InvalidOperationException">The sink reported a move but the source still offers the root.</exception>
    public static TransferStep MoveFirst(IRootSource source, IRootSink sink, IActionSource by)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        if (by == null) throw new ArgumentNullException(nameof(by));
        return MoveFirst(source, sink, by, out _);
    }

    /// <summary>Repeats <see cref="MoveFirst(IRootSource, IRootSink, IActionSource)"/> until the source is empty, a step does not move, or <paramref name="maxSteps"/> roots moved.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The step limit is not positive.</exception>
    public static DrainResult Drain(IRootSource source, IRootSink sink, IActionSource by, int maxSteps = int.MaxValue)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        if (by == null) throw new ArgumentNullException(nameof(by));
        if (maxSteps < 1) throw new ArgumentOutOfRangeException(nameof(maxSteps), maxSteps, "A drain moves at least one root.");

        var moved = new StorageChangeSet();
        var roots = 0;
        while (true)
        {
            var step = MoveFirst(source, sink, by, out var root);
            if (!step.IsMoved) return new DrainResult(roots, moved, step);
            moved.Add(root.Key, root.Units);
            roots++;
            if (roots == maxSteps) return new DrainResult(roots, moved, step);
        }
    }

    private static TransferStep MoveFirst(IRootSource source, IRootSink sink, IActionSource by, out RootSnapshot root)
    {
        if (!source.TryPeekFirst(out root)) return TransferStep.SourceEmpty;

        var planned = sink.Accept(root, Actionable.Simulate, by);
        if (!planned.IsMoved) return planned;

        var step = sink.Accept(root, Actionable.Modulate, by);
        if (step.IsMoved && source.TryPeekFirst(out var next) && next.Equals(root))
            throw new InvalidOperationException($"The sink reported moving {root}, but the source still offers it.");
        return step;
    }
}
