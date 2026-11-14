using System;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>A button to draw: its id, resolved appearance and whether it shows as selected.</summary>
public sealed class ButtonState
{
    /// <summary>Creates a button state.</summary>
    public ButtonState(string id, ButtonAppearance appearance, bool selected)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("A button id is required.", nameof(id)) : id;
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        Selected = selected;
    }

    /// <summary>Gets the button id.</summary>
    public string Id { get; }

    /// <summary>Gets the label and tooltip.</summary>
    public ButtonAppearance Appearance { get; }

    /// <summary>Gets whether it shows as selected: an open selection button, or the current entry in a panel.</summary>
    public bool Selected { get; }
}
