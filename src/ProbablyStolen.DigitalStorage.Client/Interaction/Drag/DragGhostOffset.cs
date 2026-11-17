using System;

namespace ProbablyStolen.DigitalStorage.Client.Interaction.Drag;

/// <summary>Positions a slot picture relative to the pointer location at press.</summary>
public readonly struct DragGhostOffset
{
    private readonly float _x;
    private readonly float _y;

    /// <summary>Captures the displacement from the pointer to the slot center.</summary>
    public DragGhostOffset(PointerRectangle slot, PointerPoint origin)
    {
        if (!slot.HasArea || !origin.IsValid) throw new ArgumentException("A ghost requires valid slot bounds and origin.");
        _x = (slot.MinX + slot.MaxX) / 2f - origin.X;
        _y = (slot.MinY + slot.MaxY) / 2f - origin.Y;
    }

    /// <summary>Translates the captured slot center with the pointer.</summary>
    public PointerPoint At(PointerPoint pointer) => new(pointer.X + _x, pointer.Y + _y);
}
