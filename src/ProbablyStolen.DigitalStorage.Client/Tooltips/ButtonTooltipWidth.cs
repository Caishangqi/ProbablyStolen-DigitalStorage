using System;

namespace ProbablyStolen.DigitalStorage.Client.Tooltips;

internal static class ButtonTooltipWidth
{
    private const float MaximumWidth = 350f;
    private const float TextAllowance = 1f;

    internal static bool TryFit(float widestLine, int horizontalPadding, int viewportPixels, float canvasScale, out float width)
    {
        width = 0f;
        if (!float.IsFinite(widestLine) || widestLine < 0f || horizontalPadding < 0 ||
            viewportPixels <= 0 || !float.IsFinite(canvasScale) || canvasScale <= 0f)
            return false;

        // Round the desired width up and the viewport budget down so rounding cannot exceed either cap.
        var limit = Math.Min(MaximumWidth, Math.Floor(viewportPixels / (double)canvasScale));
        if (limit <= horizontalPadding) return false;
        width = (float)Math.Min(Math.Ceiling((double)widestLine + horizontalPadding + TextAllowance), limit);
        return true;
    }
}
