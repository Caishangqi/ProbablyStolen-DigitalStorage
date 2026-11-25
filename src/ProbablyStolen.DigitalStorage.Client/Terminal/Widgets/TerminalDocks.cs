using System;
using System.Collections.Generic;
using System.Linq;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

/// <summary>
/// The docks of one terminal window, one bar per registered side. They share the open selection panel, so opening a
/// panel on one side closes the other side's.
/// </summary>
public sealed class TerminalDocks
{
    /// <summary>Creates a bar for every dock registered in <paramref name="registry"/>.</summary>
    public TerminalDocks(TerminalRegistry registry, UniversalTerminalHandler handler, Action<string> diagnostic)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
        Bars = registry.Docks.Select(dock => new VerticalButtonBar(registry, handler, dock, this, diagnostic)).ToArray();
    }

    /// <summary>Gets the bars, left first.</summary>
    public IReadOnlyList<VerticalButtonBar> Bars { get; }

    /// <summary>Gets the selection button whose panel is open on either side, or null.</summary>
    public SelectionButton? OpenSelection { get; private set; }

    /// <summary>Gets the bar of a side, or null when that side has no dock.</summary>
    public VerticalButtonBar? Find(DockSide side) => Bars.FirstOrDefault(bar => bar.Side == side);

    /// <summary>Opens the button's panel, closing any other, or closes it when it is open.</summary>
    public void Toggle(SelectionButton button)
    {
        if (button == null) throw new ArgumentNullException(nameof(button));
        OpenSelection = ReferenceEquals(OpenSelection, button) ? null : button;
    }

    /// <summary>Closes the open panel on either side.</summary>
    public void CloseSelection() => OpenSelection = null;
}
