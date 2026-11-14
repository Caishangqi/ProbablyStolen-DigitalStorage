using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// The label and tooltip of a button as localization keys, resolved by the platform.
/// </summary>
public sealed class ButtonAppearance
{
    /// <summary>Creates an appearance.</summary>
    /// <param name="labelKey">The key of the short button label.</param>
    /// <param name="tooltipKey">The key of the tooltip, which may take one argument.</param>
    /// <param name="tooltipArgKey">The key whose text fills the tooltip argument, or null.</param>
    public ButtonAppearance(string labelKey, string tooltipKey, string? tooltipArgKey = null)
    {
        LabelKey = string.IsNullOrEmpty(labelKey) ? throw new ArgumentException("A label key is required.", nameof(labelKey)) : labelKey;
        TooltipKey = string.IsNullOrEmpty(tooltipKey) ? throw new ArgumentException("A tooltip key is required.", nameof(tooltipKey)) : tooltipKey;
        TooltipArgKey = tooltipArgKey;
    }

    /// <summary>Gets the key of the short button label.</summary>
    public string LabelKey { get; }

    /// <summary>Gets the key of the tooltip.</summary>
    public string TooltipKey { get; }

    /// <summary>Gets the key whose text fills the tooltip argument, or null.</summary>
    public string? TooltipArgKey { get; }
}
