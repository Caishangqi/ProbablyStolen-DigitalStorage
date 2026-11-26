using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// Offers each root to several destinations in a fixed order and stops at the first that takes it, so the preferred
/// destination still receives every later root it has room for. Only a refusal passes the root on: a move that failed
/// and returned the root, or one that left it without a verified owner, ends the step there, because offering that root
/// again could move it twice.
/// </summary>
public sealed class FallbackRootSink : IRootSink
{
    private readonly IRootSink[] _destinations;

    /// <summary>Creates a destination that tries <paramref name="destinations"/> in order.</summary>
    public FallbackRootSink(IEnumerable<IRootSink> destinations)
    {
        if (destinations == null) throw new ArgumentNullException(nameof(destinations));
        _destinations = destinations.ToArray();
        if (_destinations.Length == 0) throw new ArgumentException("At least one destination is required.", nameof(destinations));
        if (_destinations.Any(destination => destination == null))
            throw new ArgumentException("Destinations cannot be null.", nameof(destinations));
    }

    /// <summary>Accepts one root at the first destination that takes it.</summary>
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
