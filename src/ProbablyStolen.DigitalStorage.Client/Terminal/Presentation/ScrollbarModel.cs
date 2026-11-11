using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>
/// Scrollbar rules: a fixed-size handle, a range of whole rows, one page per wheel notch and per track click, and no effect while the range is empty.
/// Coordinates are measured downward from the top of the track.
/// </summary>
public sealed class ScrollbarModel
{
    /// <summary>Gets the first valid scroll value.</summary>
    public int Min { get; private set; }

    /// <summary>Gets the last valid scroll value.</summary>
    public int Max { get; private set; }

    /// <summary>Gets the step used by the wheel and track clicks.</summary>
    public int PageSize { get; private set; } = 1;

    /// <summary>Gets the current scroll value.</summary>
    public int Current { get; private set; }

    /// <summary>Gets the number of scroll steps; zero disables the bar.</summary>
    public int Range => Max - Min;

    /// <summary>Gets whether every row already fits and input is ignored.</summary>
    public bool IsDisabled => Range == 0;

    /// <summary>Sets the range, raising a maximum below the minimum to it, and clamps the current value.</summary>
    public void SetRange(int min, int max, int pageSize)
    {
        Min = min;
        Max = Math.Max(min, max);
        PageSize = Math.Max(1, pageSize);
        Current = Clamp(Current);
    }

    /// <summary>Moves to a clamped value, for example after the screen model clamped its own row.</summary>
    public void SetCurrent(int value) => Current = Clamp(value);

    /// <summary>Gets the handle's top offset in the track: <c>(current - min) * (trackHeight - handleHeight) / range</c>.</summary>
    public float GetHandleOffset(float trackHeight, float handleHeight)
    {
        if (IsDisabled) return 0f;
        var available = Math.Max(0f, trackHeight - handleHeight);
        return (Current - Min) * available / Range;
    }

    /// <summary>Applies one wheel event: any upward delta scrolls up one page, any downward delta one page down.</summary>
    /// <returns>Whether the value changed.</returns>
    public bool Wheel(float delta)
    {
        if (IsDisabled || delta == 0f) return false;
        var step = delta > 0f ? -1 : 1;
        return Move(Current + step * PageSize);
    }

    /// <summary>Pages toward a click on the track above or below the handle.</summary>
    /// <returns>Whether the value changed.</returns>
    public bool ClickTrack(bool above) => !IsDisabled && Move(Current + (above ? -PageSize : PageSize));

    /// <summary>
    /// Follows a handle drag: <c>min + round(clamp01((pointer - dragOffset) / (trackHeight - handleHeight)) * range)</c>,
    /// where <paramref name="pointer"/> is the pointer's offset from the track top.
    /// </summary>
    /// <returns>Whether the value changed.</returns>
    public bool DragTo(float pointer, float dragOffset, float trackHeight, float handleHeight)
    {
        if (IsDisabled) return false;
        var available = trackHeight - handleHeight;
        if (available <= 0f) return false;
        var position = Math.Max(0f, Math.Min(1f, (pointer - dragOffset) / available));
        return Move(Min + (int)Math.Round(position * Range, MidpointRounding.AwayFromZero));
    }

    private bool Move(int value)
    {
        var next = Clamp(value);
        if (next == Current) return false;
        Current = next;
        return true;
    }

    private int Clamp(int value) => Math.Max(Min, Math.Min(Max, value));
}
