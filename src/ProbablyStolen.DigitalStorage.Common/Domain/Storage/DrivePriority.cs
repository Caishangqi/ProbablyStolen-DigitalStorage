using System;
using System.Globalization;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Storage;

/// <summary>
/// The storage priority of a drive, shared by every cell mounted in it; cells
/// have none. Higher priorities receive items first and give them up last.
/// </summary>
public static class DrivePriority
{
    /// <summary>The lowest priority.</summary>
    public const int Min = -999;

    /// <summary>The highest priority.</summary>
    public const int Max = 999;

    /// <summary>The priority of a drive that never had one set.</summary>
    public const int Default = 0;

    /// <summary>Clamps a value to [<see cref="Min"/>, <see cref="Max"/>].</summary>
    public static int Clamp(long value) => (int)Math.Max(Min, Math.Min(Max, value));

    /// <summary>
    /// Parses player input as a clamped priority. Returns false for text that is not a whole number, so the caller
    /// keeps the previous value.
    /// </summary>
    public static bool TryParse(string? text, out int priority)
    {
        priority = Default;
        if (text == null) return false;
        var trimmed = text.Trim();
        if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            // Too many digits for a long is still a whole number; it clamps to the matching bound.
            if (trimmed.Length < 2 || !IsDigits(trimmed)) return false;
            value = trimmed[0] == '-' ? long.MinValue : long.MaxValue;
        }

        priority = Clamp(value);
        return true;
    }

    private static bool IsDigits(string text)
    {
        var start = text[0] == '-' || text[0] == '+' ? 1 : 0;
        if (start == text.Length) return false;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] < '0' || text[index] > '9') return false;
        }

        return true;
    }
}
