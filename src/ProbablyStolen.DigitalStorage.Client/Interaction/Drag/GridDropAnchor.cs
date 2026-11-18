using System;

namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>A cell in a grid, measured from its top-left corner.</summary>
public readonly struct GridDropAnchor : IEquatable<GridDropAnchor>
{
    /// <summary>Creates a nonnegative grid-cell anchor.</summary>
    public GridDropAnchor(int x, int y)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0) throw new ArgumentOutOfRangeException(nameof(y));
        X = x;
        Y = y;
    }

    /// <summary>Gets the column from the grid's left edge.</summary>
    public int X { get; }
    /// <summary>Gets the row from the grid's top edge.</summary>
    public int Y { get; }

    /// <summary>Gets the center of a footprint whose top-left cell is this anchor, in grid-local coordinates.</summary>
    public PointerPoint FootprintCenter(float cellSize, int width, int height) =>
        new((X + width * 0.5f) * cellSize, (Y + height * 0.5f) * cellSize);

    /// <summary>Maps top-left-relative local coordinates, with Y increasing downwards, to a cell.</summary>
    public static bool TryFromLocal(PointerPoint point, float cellSize, int columns, int rows,
        out GridDropAnchor anchor)
    {
        anchor = default;
        if (!point.IsValid || float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0 ||
            columns <= 0 || rows <= 0 || point.X < 0 || point.Y < 0) return false;
        var x = (double)point.X / cellSize;
        var y = (double)point.Y / cellSize;
        if (x >= columns || y >= rows) return false;
        anchor = new GridDropAnchor((int)Math.Floor(x), (int)Math.Floor(y));
        return true;
    }

    /// <inheritdoc />
    public bool Equals(GridDropAnchor other) => X == other.X && Y == other.Y;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GridDropAnchor other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
