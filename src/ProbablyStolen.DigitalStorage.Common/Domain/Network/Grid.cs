using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProbablyStolen.DigitalStorage.Common.Domain.Network.Pathing;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Network;

/// <summary>An immutable connected component from one current machine graph snapshot.</summary>
public sealed class Grid
{
    private readonly PathingService _pathingService;
    internal Grid(IReadOnlyList<GridNode> nodes, IReadOnlyList<GridConnection> connections)
    {
        if (nodes == null)
        {
            throw new ArgumentNullException(nameof(nodes));
        }

        if (connections == null)
        {
            throw new ArgumentNullException(nameof(connections));
        }

        var copiedNodes = nodes.OrderBy(node => node.Id).ToArray();
        var copiedConnections = connections.OrderBy(edge => edge.FirstNodeId).ThenBy(edge => edge.SecondNodeId).ToArray();
        Nodes = new ReadOnlyCollection<GridNode>(copiedNodes);
        Connections = new ReadOnlyCollection<GridConnection>(copiedConnections);
        ControllerCount = copiedNodes.Count(node => node.Kind == GridNodeKind.Controller);
        DriveCount = copiedNodes.Count(node => node.Kind == GridNodeKind.Drive);
        TerminalCount = copiedNodes.Count(node => node.Kind == GridNodeKind.Terminal);
        TotalPowerUsage = copiedNodes.Aggregate(0L, (total, node) => total + node.PowerUsage);
        _pathingService = new PathingService(this);
    }

    /// <summary>Gets this component's immutable machine snapshots.</summary>
    public IReadOnlyList<GridNode> Nodes { get; }

    /// <summary>Gets this component's immutable undirected edges.</summary>
    public IReadOnlyList<GridConnection> Connections { get; }

    /// <summary>Gets its controller count.</summary>
    public int ControllerCount { get; }

    /// <summary>Gets its drive count.</summary>
    public int DriveCount { get; }

    /// <summary>Gets its terminal count.</summary>
    public int TerminalCount { get; }

    /// <summary>Gets the sum of per-turn power usage for all machines in this component.</summary>
    public long TotalPowerUsage { get; }

    /// <summary>Gets controller structure state for this grid independently of energy.</summary>
    public PathingService GetPathingService() => _pathingService;
}
