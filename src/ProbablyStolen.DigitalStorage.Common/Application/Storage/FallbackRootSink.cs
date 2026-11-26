using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// Tries the preferred destination again for every item. Only a destination's refusal hands the item to the next
/// destination. A move that returned the item or left its ownership uncertain ends the transfer attempt to avoid
/// moving the same item twice.
/// </summary>
public sealed class FallbackRootSink : IRootSink
{
    private readonly IRootSink[] _destinations;

    /// <summary>Tries <paramref name="destinations"/> in the order supplied.</summary>
    public FallbackRootSink(IEnumerable<IRootSink> destinations)
    {
        if (destinations == null) throw new ArgumentNullException(nameof(destinations));
        _destinations = destinations.ToArray();
        if (_destinations.Length == 0) throw new ArgumentException("At least one destination is required.", nameof(destinations));
        if (_destinations.Any(destination => destination == null))
            throw new ArgumentException("Destinations cannot be null.", nameof(destinations));
    }

    /// <summary>Stops at the first destination that accepts the item or reports a move failure.</summary>
    public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
    {
        var step = TransferStep.Rejected(StoreRejection.PhysicalLimit);
        foreach (var destination in _destinations)
        {
            step = destination.Accept(root, mode, source);
            if (step.Outcome != TransferOutcome.Rejected) return step;
        }

        return step;
    }
}
