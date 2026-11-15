using System;
using ProbablyStolen.DigitalStorage.Api.Config;
using ProbablyStolen.DigitalStorage.Api.Networking.Security;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Application.Storage;

/// <summary>
/// Passes roots to an inner destination until it has taken a fixed number of them, then refuses with
/// <see cref="StoreRejection.RequestFilled"/>. Extraction stops at the first refusal, so wrapping a destination this
/// way moves at most that many physical items, whatever each one's unit count.
/// </summary>
public sealed class PieceLimitSink : IRootSink
{
    private readonly IRootSink _inner;
    private readonly long _limit;

    /// <summary>Creates a limited destination.</summary>
    public PieceLimitSink(IRootSink inner, long limit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _limit = limit > 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit), limit, "Take at least one item.");
    }

    /// <summary>Gets the roots the inner destination took completely.</summary>
    public long Accepted { get; private set; }

    /// <summary>
    /// Accepts one root. Only a real move counts: a transfer plans each root with a simulation before moving it, so
    /// counting simulations would stop after half the request.
    /// </summary>
    public TransferStep Accept(RootSnapshot root, Actionable mode, IActionSource source)
    {
        if (Accepted >= _limit) return TransferStep.Rejected(StoreRejection.RequestFilled);
        var step = _inner.Accept(root, mode, source);
        if (mode == Actionable.Modulate && step.IsMoved) Accepted++;
        return step;
    }
}
