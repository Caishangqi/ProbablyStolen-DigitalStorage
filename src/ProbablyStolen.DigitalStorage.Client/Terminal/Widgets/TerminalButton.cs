using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>A button model on the function rail or in a selection panel; the platform only draws it.</summary>
public abstract class TerminalButton
{
    /// <summary>Creates a button.</summary>
    protected TerminalButton(string id)
    {
        Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("A button id is required.", nameof(id)) : id;
    }

    /// <summary>Gets the unique button id.</summary>
    public string Id { get; }

    /// <summary>Gets the label and tooltip for the current context.</summary>
    public abstract ButtonAppearance GetAppearance(TerminalContext context);

    /// <summary>Handles a press; <paramref name="backwards"/> asks a cycling button for the previous value.</summary>
    public abstract void OnPress(TerminalContext context, bool backwards);
}
