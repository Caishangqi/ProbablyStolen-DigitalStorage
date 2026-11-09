using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Network;

/// <summary>An undirected edge between two distinct scan-local grid node IDs.</summary>
public readonly struct GridConnection : IEquatable<GridConnection>
{
    /// <summary>Creates a normalized edge for a pair of distinct nodes.</summary>
    public GridConnection(int firstNodeId, int secondNodeId)
    {
        if (firstNodeId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstNodeId));
        }

        if (secondNodeId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondNodeId));
        }

        if (firstNodeId == secondNodeId)
        {
            throw new ArgumentException("A grid node cannot connect to itself.");
        }

        FirstNodeId = Math.Min(firstNodeId, secondNodeId);
        SecondNodeId = Math.Max(firstNodeId, secondNodeId);
    }

    /// <summary>Gets the lower normalized node ID.</summary>
    public int FirstNodeId { get; }

    /// <summary>Gets the higher normalized node ID.</summary>
    public int SecondNodeId { get; }

    /// <inheritdoc />
    public bool Equals(GridConnection other) =>
        FirstNodeId == other.FirstNodeId && SecondNodeId == other.SecondNodeId;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GridConnection other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((FirstNodeId * 397) ^ SecondNodeId);

    /// <summary>Compares two normalized undirected edges.</summary>
    public static bool operator ==(GridConnection left, GridConnection right) => left.Equals(right);

    /// <summary>Compares two normalized undirected edges.</summary>
    public static bool operator !=(GridConnection left, GridConnection right) => !left.Equals(right);
}
