using System;

namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>The phase of one normalized pointer gesture.</summary>
public enum PointerGesturePhase
{
    /// <summary>Pointer button pressed.</summary>
    Press,
    /// <summary>Pointer moved.</summary>
    Move,
    /// <summary>Pointer wheel changed.</summary>
    Wheel,
    /// <summary>Pointer button released.</summary>
    Release,
    /// <summary>Gesture cancelled.</summary>
    Cancel
}

/// <summary>The button associated with a pointer gesture.</summary>
public enum PointerButton
{
    /// <summary>Primary pointer button.</summary>
    Primary,
    /// <summary>Secondary pointer button.</summary>
    Secondary,
    /// <summary>Middle pointer button.</summary>
    Middle
}

/// <summary>A finite screen coordinate.</summary>
public readonly struct PointerPoint : IEquatable<PointerPoint>
{
    /// <summary>Creates a screen coordinate. Use <see cref="IsValid"/> to check finite input.</summary>
    public PointerPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the horizontal coordinate.</summary>
    public float X { get; }
    /// <summary>Gets the vertical coordinate.</summary>
    public float Y { get; }
    /// <summary>Gets whether both coordinates are finite.</summary>
    public bool IsValid => IsFinite(X) && IsFinite(Y);

    /// <inheritdoc />
    public bool Equals(PointerPoint other) => X.Equals(other.X) && Y.Equals(other.Y);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PointerPoint other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);
    /// <summary>Compares two coordinates.</summary>
    public static bool operator ==(PointerPoint left, PointerPoint right) => left.Equals(right);
    /// <summary>Compares two coordinates for inequality.</summary>
    public static bool operator !=(PointerPoint left, PointerPoint right) => !left.Equals(right);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>A normalized pointer gesture containing no platform objects.</summary>
public readonly struct PointerGesture
{
    /// <summary>Creates a normalized pointer gesture.</summary>
    public PointerGesture(PointerGesturePhase phase, PointerButton button, PointerPoint position,
        float deltaX = 0, float deltaY = 0, float wheelSteps = 0, bool shift = false, bool alt = false,
        bool control = false)
    {
        Phase = phase;
        Button = button;
        Position = position;
        DeltaX = deltaX;
        DeltaY = deltaY;
        WheelSteps = wheelSteps;
        Shift = shift;
        Alt = alt;
        Control = control;
    }

    /// <summary>Gets the gesture phase.</summary>
    public PointerGesturePhase Phase { get; }
    /// <summary>Gets the pointer button.</summary>
    public PointerButton Button { get; }
    /// <summary>Gets the current pointer position.</summary>
    public PointerPoint Position { get; }
    /// <summary>Gets the horizontal movement delta.</summary>
    public float DeltaX { get; }
    /// <summary>Gets the vertical movement delta.</summary>
    public float DeltaY { get; }
    /// <summary>Gets the wheel movement in normalized steps.</summary>
    public float WheelSteps { get; }
    /// <summary>Gets whether Shift was held.</summary>
    public bool Shift { get; }
    /// <summary>Gets whether Alt was held.</summary>
    public bool Alt { get; }
    /// <summary>Gets whether Control was held.</summary>
    public bool Control { get; }
    /// <summary>Gets whether enum, coordinate and delta values are valid.</summary>
    public bool IsValid => Enum.IsDefined(typeof(PointerGesturePhase), Phase) &&
        Enum.IsDefined(typeof(PointerButton), Button) && Position.IsValid &&
        IsFinite(DeltaX) && IsFinite(DeltaY) && IsFinite(WheelSteps);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
