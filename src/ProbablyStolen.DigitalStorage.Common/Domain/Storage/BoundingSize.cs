using System;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>The bounding width and height of an item in its stored orientation.</summary>
public readonly struct BoundingSize : IEquatable<BoundingSize>
{
    /// <summary>Creates a bounding size.</summary>
    public BoundingSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Gets the width in grid cells.</summary>
    public int Width { get; }

    /// <summary>Gets the height in grid cells.</summary>
    public int Height { get; }

    /// <inheritdoc />
    public bool Equals(BoundingSize other) => Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BoundingSize other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    /// <inheritdoc />
    public override string ToString() => $"{Width}x{Height}";
}
