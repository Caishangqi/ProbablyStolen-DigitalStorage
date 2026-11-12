using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

namespace ProbablyStolen.DigitalStorage.Common.Application.Transfers;

/// <summary>
/// Offers exactly one root of a wrapped source, and only while the wrapped source still offers it. Lets
/// <see cref="RootTransfer.MoveFirst(IRootSource, IRootSink, ProbablyStolen.DigitalStorage.Api.Networking.Security.IActionSource)"/> move a chosen root instead of whatever root comes first.
/// </summary>
internal sealed class SingleRootSource : IRootSource
{
    private readonly IRootSource _roots;
    private readonly RootSnapshot _root;

    internal SingleRootSource(IRootSource roots, RootSnapshot root)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _root = root;
    }

    public bool TryPeekFirst(out RootSnapshot root)
    {
        root = _root;
        return _roots.Snapshot().Any(candidate => candidate.Equals(_root));
    }

    public IEnumerable<RootSnapshot> Snapshot()
    {
        if (TryPeekFirst(out var root)) yield return root;
    }
}
