using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Presentation;

/// <summary>Calculates page frame dimensions from natural content and the window chrome.</summary>
public static class SubScreenMetrics
{
    /// <summary>Fits content exactly or keeps a baseline floor for pages that need it.</summary>
    public static float GetLength(float baseline, float preferredContent, float chrome, bool fitContent)
    {
        if (!FinitePositive(baseline)) throw new ArgumentOutOfRangeException(nameof(baseline));
        if (float.IsNaN(preferredContent) || float.IsInfinity(preferredContent) || preferredContent < 0)
            throw new ArgumentOutOfRangeException(nameof(preferredContent));
        if (float.IsNaN(chrome) || float.IsInfinity(chrome) || chrome < 0)
            throw new ArgumentOutOfRangeException(nameof(chrome));
        var measured = Math.Max(1f, preferredContent + chrome);
        return fitContent ? measured : Math.Max(baseline, measured);
    }

    private static bool FinitePositive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
}
