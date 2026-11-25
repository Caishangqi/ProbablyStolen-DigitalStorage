using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// A dock button that opens a panel of entries beside it. Shared by <see cref="TerminalSelectionButton"/> and
/// <see cref="ActionSelectionButton"/>; the open state belongs to <see cref="TerminalDocks"/> so at most one panel is
/// open across both sides.
/// </summary>
public abstract class SelectionButton : TerminalButton
{
    /// <summary>Creates a selection button on a bar.</summary>
    protected SelectionButton(string id, VerticalButtonBar bar) : base(id)
    {
        Bar = bar ?? throw new ArgumentNullException(nameof(bar));
    }

    /// <summary>Gets whether its panel is open.</summary>
    public bool MenuOpen => ReferenceEquals(Bar.OpenSelection, this);

    /// <summary>Gets the bar it sits on.</summary>
    public VerticalButtonBar Bar { get; }

    /// <summary>Gets the panel entries.</summary>
    public abstract IReadOnlyList<ButtonState> GetEntries(TerminalContext context);

    /// <summary>Toggles the panel.</summary>
    public override void OnPress(TerminalContext context, bool backwards) => Bar.Toggle(this);

    /// <summary>Handles a click on a panel entry.</summary>
    public abstract void Select(TerminalContext context, string entryId);
}
