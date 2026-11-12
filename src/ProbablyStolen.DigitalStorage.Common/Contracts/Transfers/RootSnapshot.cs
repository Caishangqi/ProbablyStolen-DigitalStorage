using System;
using ProbablyStolen.DigitalStorage.Api.Stacks;
using ProbablyStolen.DigitalStorage.Common.Domain.Storage;

namespace ProbablyStolen.DigitalStorage.Common.Contracts.Transfers;

/// <summary>
/// Read-only view of one item root located by page index and grid position. Common never holds the native object;
/// the Platform ports resolve the location.
/// </summary>
public readonly struct RootSnapshot : IEquatable<RootSnapshot>
{
    /// <summary>Creates a snapshot.</summary>
    /// <param name="page">The page index inside the origin.</param>
    /// <param name="x">The grid column of the root origin.</param>
    /// <param name="y">The grid row of the root origin.</param>
    /// <param name="size">The stored-orientation bounding size.</param>
    /// <param name="key">The item type.</param>
    /// <param name="units">The native units.</param>
    /// <param name="origin">
    /// The source that offered the root. Platform sinks ask it to resolve the native root; Common never does.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The location is negative or the units are not positive.</exception>
    public RootSnapshot(int page, int x, int y, BoundingSize size, ItemKey key, long units, IRootSource? origin = null)
    {
        Origin = origin;
        if (page < 0 || x < 0 || y < 0) throw new ArgumentOutOfRangeException(nameof(page), "A root location is not negative.");
        if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units), units, "A root has positive units.");
        Page = page;
        X = x;
        Y = y;
        Size = size;
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Units = units;
    }

    /// <summary>Gets the page index.</summary>
    public int Page { get; }

    /// <summary>Gets the grid column of the root origin.</summary>
    public int X { get; }

    /// <summary>Gets the grid row of the root origin.</summary>
    public int Y { get; }

    /// <summary>Gets the stored-orientation bounding size.</summary>
    public BoundingSize Size { get; }

    /// <summary>Gets the item type.</summary>
    public ItemKey Key { get; }

    /// <summary>Gets the native units of the root.</summary>
    public long Units { get; }

    /// <summary>Gets the source that offered the root, or null for detached snapshots.</summary>
    public IRootSource? Origin { get; }

    /// <summary>Gets the planner candidate for this root.</summary>
    public StoreCandidate ToCandidate() => new(Size, Key, Units);

    /// <summary>Orders roots by page, then row, then column; this is what "first" means for every source.</summary>
    public static int CompareOrder(RootSnapshot left, RootSnapshot right)
    {
        var order = left.Page.CompareTo(right.Page);
        if (order == 0) order = left.Y.CompareTo(right.Y);
        return order != 0 ? order : left.X.CompareTo(right.X);
    }

    /// <inheritdoc />
    public bool Equals(RootSnapshot other) => Page == other.Page && X == other.X && Y == other.Y &&
        Size.Equals(other.Size) && Equals(Key, other.Key) && Units == other.Units &&
        ReferenceEquals(Origin, other.Origin);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RootSnapshot other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Page, X, Y, Size, Key, Units);

    /// <inheritdoc />
    public override string ToString() => $"{Key}×{Units} at {Page}:{X},{Y} ({Size})";
}
