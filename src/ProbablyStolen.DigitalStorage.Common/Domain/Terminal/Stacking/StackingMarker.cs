using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProbablyStolen.DigitalStorage.Common.Domain.Terminal.Stacking;

/// <summary>The tint of the mark drawn on mixed groups, as a 0xRRGGBB color.</summary>
public sealed class StackingMarker : IEquatable<StackingMarker>
{
    private const int MaxRgb = 0xFFFFFF;

    /// <summary>Creates a marker.</summary>
    public StackingMarker(int rgb)
    {
        if (rgb < 0 || rgb > MaxRgb) throw new ArgumentOutOfRangeException(nameof(rgb));
        Rgb = rgb;
    }

    /// <summary>Gets the marker of a terminal that never chose one: green.</summary>
    public static StackingMarker Default { get; } = new(0x4CD964);

    /// <summary>Gets the preset colors offered next to the color input, the default first.</summary>
    public static IReadOnlyList<StackingMarker> Presets { get; } = new[]
    {
        Default, new StackingMarker(0x4DA3FF), new StackingMarker(0xB37FEB), new StackingMarker(0xFF9F43),
        new StackingMarker(0xFF5C5C), new StackingMarker(0xFFD84D), new StackingMarker(0xFFFFFF)
    };

    /// <summary>Gets the color as 0xRRGGBB.</summary>
    public int Rgb { get; }

    /// <summary>Reads <c>#RGB</c> or <c>#RRGGBB</c> in any letter case; anything else is rejected.</summary>
    public static bool TryParse(string? text, out StackingMarker marker)
    {
        marker = Default;
        if (text == null) return false;
        var value = text.Trim();
        if (value.Length == 0 || value[0] != '#') return false;
        var digits = value.Substring(1);
        if (digits.Length == 3)
            digits = new string(new[] { digits[0], digits[0], digits[1], digits[1], digits[2], digits[2] });
        if (digits.Length != 6) return false;
        foreach (var c in digits)
            if (!Uri.IsHexDigit(c)) return false;
        marker = new StackingMarker(int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>Formats the color as upper case <c>#RRGGBB</c>.</summary>
    public string Format() => "#" + Rgb.ToString("X6", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool Equals(StackingMarker? other) => other != null && Rgb == other.Rgb;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StackingMarker);

    /// <inheritdoc />
    public override int GetHashCode() => Rgb;
}
