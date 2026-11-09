using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Network;

/// <summary>Builds deterministic connected components from a current set of native machine edges.</summary>
public sealed class GridPropagator
{
    /// <summary>Propagates over all undirected edges and includes isolated nodes as one-node components.</summary>
    public IReadOnlyList<Grid> Propagate(IEnumerable<GridNode> nodes, IEnumerable<GridConnection> connections)
    {
        if (nodes == null)
        {
            throw new ArgumentNullException(nameof(nodes));
        }

        if (connections == null)
        {
            throw new ArgumentNullException(nameof(connections));
        }

        var nodeList = nodes.ToArray();
        var nodeById = new Dictionary<int, GridNode>();
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var node in nodeList)
        {
            if (node == null)
            {
                throw new ArgumentException("The graph cannot contain null nodes.", nameof(nodes));
            }

            if (!nodeById.TryAdd(node.Id, node))
            {
                throw new ArgumentException("Grid node IDs must be unique within one snapshot.", nameof(nodes));
            }

            adjacency.Add(node.Id, new List<int>());
        }

        var edgeSet = new HashSet<GridConnection>();
        var edgeList = connections.ToArray();
        foreach (var edge in edgeList)
        {
            if (!nodeById.ContainsKey(edge.FirstNodeId) || !nodeById.ContainsKey(edge.SecondNodeId))
            {
                throw new ArgumentException("Every edge endpoint must exist in the node snapshot.", nameof(connections));
            }

            if (!edgeSet.Add(edge))
            {
                throw new ArgumentException("Duplicate grid edges are not allowed.", nameof(connections));
            }

            adjacency[edge.FirstNodeId].Add(edge.SecondNodeId);
            adjacency[edge.SecondNodeId].Add(edge.FirstNodeId);
        }

        var visited = new HashSet<int>();
        var grids = new List<Grid>();
        foreach (var node in nodeList.OrderBy(candidate => candidate.Id))
        {
            if (!visited.Add(node.Id))
            {
                continue;
            }

            var componentIds = VisitNode(node.Id, adjacency, visited);
            var componentIdSet = new HashSet<int>(componentIds);
            var componentNodes = componentIds.Select(id => nodeById[id]).ToArray();
            var componentEdges = edgeList
                .Where(edge => componentIdSet.Contains(edge.FirstNodeId) && componentIdSet.Contains(edge.SecondNodeId))
                .ToArray();
            grids.Add(new Grid(componentNodes, componentEdges));
        }

        return grids.AsReadOnly();
    }

    private static IReadOnlyList<int> VisitNode(
        int startNodeId,
        IReadOnlyDictionary<int, List<int>> adjacency,
        ISet<int> visited)
    {
        var component = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(startNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            component.Add(current);
            foreach (var neighbor in adjacency[current])
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return component;
    }
}
