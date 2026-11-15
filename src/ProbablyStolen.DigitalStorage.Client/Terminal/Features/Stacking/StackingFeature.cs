using System;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.Stacking;

/// <summary>
/// The stack display feature: a storage view rail button that opens the rules page, the grouping projector, the
/// mixed slot mark and the merged slot tooltip panel. Everything stays inactive until the module is installed in the terminal.
/// </summary>
public static class StackingFeature
{
    /// <summary>The rail button id.</summary>
    public const string ActionId = "stacking";

    /// <summary>Registers the button and the storage extensions.</summary>
    /// <param name="registry">The registry.</param>
    /// <param name="createScreen">Creates the platform rules page for a platform window.</param>
    /// <param name="diagnostic">Receives grouping diagnostics.</param>
    /// <param name="diagnosticsEnabled">Whether to explain why entries of one item stay apart.</param>
    public static void Register(TerminalRegistry registry, Func<object, object> createScreen, Action<string> diagnostic,
        Func<bool> diagnosticsEnabled)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (createScreen == null) throw new ArgumentNullException(nameof(createScreen));
        var appearance = new ButtonAppearance("button_stack", "terminal_button_stacking_tip");
        // After the sort order (0) and sort direction (10) buttons of the storage view.
        registry.AddAction(new TerminalAction(ActionId, 20, ActionPlacement.Rail, TerminalRegistry.StorageTerminalName,
            StackingModule.IsInstalled, _ => appearance,
            context => context.Commands.SwitchToScreen(createScreen(context.Window))));
        registry.AddStorageProjector(new StackingProjector(diagnostic, diagnosticsEnabled));
        registry.AddSlotDecorator(new MixedGroupDecorator());
        registry.AddTooltipPanel(StackingInfoTooltip.Panel);
        registry.AddTooltipContributor(new StackingInfoTooltip());
    }
}
