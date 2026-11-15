using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// The icon and tooltip of a button, resolved by the platform.
/// </summary>
public sealed class ButtonAppearance
{
    /// <summary>Creates an appearance.</summary>
    /// <param name="iconKey">The icon resource key.</param>
    /// <param name="tooltipKey">The key of the tooltip, which may take one argument.</param>
    /// <param name="tooltipArgKey">The key whose text fills the tooltip argument, or null.</param>
    public ButtonAppearance(string iconKey, string tooltipKey, string? tooltipArgKey = null)
    {
        IconKey = string.IsNullOrWhiteSpace(iconKey) ? throw new ArgumentException("An icon key is required.", nameof(iconKey)) : iconKey;
        TooltipKey = string.IsNullOrEmpty(tooltipKey) ? throw new ArgumentException("A tooltip key is required.", nameof(tooltipKey)) : tooltipKey;
        TooltipArgKey = tooltipArgKey;
    }

    /// <summary>Gets the icon resource key.</summary>
    public string IconKey { get; }

    /// <summary>Gets the key of the tooltip.</summary>
    public string TooltipKey { get; }

    /// <summary>Gets the key whose text fills the tooltip argument, or null.</summary>
    public string? TooltipArgKey { get; }
}
