using System;

namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>A normalized screen-space axis-aligned rectangle.</summary>
public readonly struct PointerRectangle : IEquatable<PointerRectangle>
{
    /// <summary>Creates a rectangle from normalized bounds.</summary>
    public PointerRectangle(float minX, float minY, float maxX, float maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>Gets the left bound.</summary>
    public float MinX { get; }
    /// <summary>Gets the bottom bound.</summary>
    public float MinY { get; }
    /// <summary>Gets the right bound.</summary>
    public float MaxX { get; }
    /// <summary>Gets the top bound.</summary>
    public float MaxY { get; }
    /// <summary>Gets whether bounds are finite and ordered.</summary>
    public bool IsValid => IsFinite(MinX) && IsFinite(MinY) && IsFinite(MaxX) && IsFinite(MaxY) &&
        MinX <= MaxX && MinY <= MaxY;
    /// <summary>Gets whether the rectangle has positive width and height.</summary>
    public bool HasArea => IsValid && MinX < MaxX && MinY < MaxY;

    /// <summary>Creates the normalized rectangle between two pointer positions.</summary>
    public static bool TryCreate(PointerPoint first, PointerPoint second, out PointerRectangle rectangle)
    {
        if (!first.IsValid || !second.IsValid)
        {
            rectangle = default;
            return false;
        }

        rectangle = new PointerRectangle(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
        return true;
    }

    /// <summary>Checks strict positive-area intersection; edge contact does not select either rectangle.</summary>
    public bool Intersects(PointerRectangle other) => HasArea && other.HasArea &&
        MinX < other.MaxX && MaxX > other.MinX && MinY < other.MaxY && MaxY > other.MinY;

    /// <inheritdoc />
    public bool Equals(PointerRectangle other) => MinX.Equals(other.MinX) && MinY.Equals(other.MinY) &&
        MaxX.Equals(other.MaxX) && MaxY.Equals(other.MaxY);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PointerRectangle other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MinX, MinY, MaxX, MaxY);
    /// <summary>Compares two rectangles.</summary>
    public static bool operator ==(PointerRectangle left, PointerRectangle right) => left.Equals(right);
    /// <summary>Compares two rectangles for inequality.</summary>
    public static bool operator !=(PointerRectangle left, PointerRectangle right) => !left.Equals(right);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
