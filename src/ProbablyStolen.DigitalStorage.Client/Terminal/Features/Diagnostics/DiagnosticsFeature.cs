using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Diagnostics;

/// <summary>
/// A placeholder view with one rail and one picker button of its own, so view switching, the current terminal button
/// and the function selection button can be checked in game before any real module exists. It is only available while
/// <see cref="TerminalContext.TestViewEnabled"/> is true.
/// </summary>
public static class DiagnosticsFeature
{
    /// <summary>The view name.</summary>
    public const string TerminalName = "diagnostics";

    /// <summary>The rail button id.</summary>
    public const string RailActionId = "diagnostics.rail";

    /// <summary>The picker button id.</summary>
    public const string PickerActionId = "diagnostics.picker";

    /// <summary>Registers the view and its two buttons.</summary>
    /// <param name="registry">The registry.</param>
    /// <param name="createView">Creates the placeholder view for a platform window.</param>
    /// <param name="createScreen">Creates the placeholder sub-screen for a platform window.</param>
    /// <param name="log">Receives the rail button's press; nothing else happens.</param>
    public static void Register(TerminalRegistry registry, Func<object, object> createView,
        Func<object, object> createScreen, Action<string> log)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (createScreen == null) throw new ArgumentNullException(nameof(createScreen));
        if (log == null) throw new ArgumentNullException(nameof(log));
        registry.AddTerminal(new TerminalDefinition(TerminalName, 1000, "terminal_view_diagnostics",
            context => context.TestViewEnabled, createView));
        var rail = new ButtonAppearance("button_placeholder", "terminal_button_diagnostics_rail_tip");
        registry.AddAction(new TerminalAction(RailActionId, 0, ActionPlacement.Rail, TerminalName,
            context => context.TestViewEnabled, _ => rail, _ => log("diagnostics_rail pressed")));
        var picker = new ButtonAppearance("button_placeholder", "terminal_button_diagnostics_picker_tip");
        registry.AddAction(new TerminalAction(PickerActionId, 0, ActionPlacement.Picker, TerminalName,
            context => context.TestViewEnabled, _ => picker,
            context => context.Commands.SwitchToScreen(createScreen(context.Window))));
    }
}
