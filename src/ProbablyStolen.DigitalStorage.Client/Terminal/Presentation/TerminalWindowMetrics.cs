using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Defines the compact terminal's design coordinates and viewport fit.</summary>
public static class TerminalWindowMetrics
{
    /// <summary>Gets the number of columns retained on each page.</summary>
    public const int Columns = 7;
    /// <summary>Gets the number of rows retained on each page.</summary>
    public const int Rows = 4;
    /// <summary>Gets the square slot size in design units.</summary>
    public const int CellPixels = 40;
    /// <summary>Gets the gap between slots in design units.</summary>
    public const int CellSpacing = 3;
    /// <summary>Gets the outer width in design units.</summary>
    public const int Width = 352;
    /// <summary>Gets the outer height in design units.</summary>
    public const int Height = 376;
    /// <summary>Gets the correction from doubled machinery controls to native window coordinates.</summary>
    public const float NativeScale = 0.5f;
    /// <summary>Gets the maximum width as a fraction of the game viewport.</summary>
    public const float MaximumWidthFraction = 0.4f;
    /// <summary>Gets the maximum height as a fraction of the game viewport.</summary>
    public const float MaximumHeightFraction = 0.65f;

    /// <summary>Fits the local window scale without changing any global UI scaling.</summary>
    public static float GetScale(float viewportWidth, float viewportHeight, float canvasScale)
    {
        if (!IsPositiveFinite(viewportWidth) || !IsPositiveFinite(viewportHeight) ||
            !IsPositiveFinite(canvasScale))
        {
            return NativeScale;
        }

        var widthScale = viewportWidth / Width / canvasScale * MaximumWidthFraction;
        var heightScale = viewportHeight / Height / canvasScale * MaximumHeightFraction;
        return Math.Min(NativeScale, Math.Min(widthScale, heightScale));
    }

    /// <summary>Clamps a centered window offset so the full window remains inside its viewport.</summary>
    public static float GetClampedOffset(float offset, float viewportLength, float windowLength, float margin = 0)
    {
        if (!IsPositiveFinite(viewportLength) || !IsPositiveFinite(windowLength) ||
            float.IsNaN(offset) || float.IsInfinity(offset))
        {
            return 0;
        }

        var padding = IsPositiveFinite(margin) ? margin : 0;
        var limit = Math.Max(0, (viewportLength - windowLength) / 2 - padding);
        return Math.Max(-limit, Math.Min(limit, offset));
    }

    private static bool IsPositiveFinite(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
}
