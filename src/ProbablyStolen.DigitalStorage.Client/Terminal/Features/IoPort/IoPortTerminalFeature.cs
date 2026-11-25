using System;
using ProbablyStolen.DigitalStorage.Client.Machines.IoPort;
using ProbablyStolen.DigitalStorage.Client.Terminal.Features.Modules;
using ProbablyStolen.DigitalStorage.Client.Terminal.Host;
using ProbablyStolen.DigitalStorage.Client.Terminal.Widgets;
using ProbablyStolen.DigitalStorage.Common.Domain.Machines;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Features.IoPort;

/// <summary>
/// The IO Port window's registry: the machine view plus rail buttons for the eject condition and the module page.
/// Built through the same registration as the storage terminal, so the rail and sub-pages behave alike.
/// </summary>
public static class IoPortTerminalFeature
{
    /// <summary>The name of the machine view.</summary>
    public const string TerminalName = "io_port";

    /// <summary>The id of the eject-condition rail button.</summary>
    public const string CompletionActionId = "io_port_completion";

    /// <summary>Creates and freezes the registry of one IO Port window.</summary>
    /// <param name="config">Stores registered setting values.</param>
    /// <param name="diagnostic">Receives refused registrations.</param>
    /// <param name="createView">Creates the machine view for a window.</param>
    /// <param name="createModulesScreen">Creates the module page for a window.</param>
    /// <param name="readCompletion">Reads the window's completion mode, or null when its settings are unreadable.</param>
    /// <param name="setCompletion">Saves another completion mode for the window's machine.</param>
    public static TerminalRegistry CreateRegistry(IConfigManager config, Action<string> diagnostic,
        Func<object, object> createView, Func<object, ITerminalScreen> createModulesScreen,
        Func<TerminalContext, IoPortCompletionMode?> readCompletion, Action<TerminalContext, IoPortCompletionMode> setCompletion)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
        if (createView == null) throw new ArgumentNullException(nameof(createView));
        if (readCompletion == null) throw new ArgumentNullException(nameof(readCompletion));
        if (setCompletion == null) throw new ArgumentNullException(nameof(setCompletion));

        var registry = new TerminalRegistry(config, diagnostic, TerminalName);
        registry.AddDock(DockSide.Left, StorageTerminal.StorageTerminalFeature.LeftDockTitleKey);
        registry.AddTerminal(new TerminalDefinition(TerminalName, 0, "io_port_name", _ => true, createView));
        ModulesFeature.Register(registry, createModulesScreen);
        registry.AddAction(new TerminalAction(CompletionActionId, 1, DockSide.Left, ActionPlacement.Rail, ActionScope.AllViews,
            context => readCompletion(context).HasValue,
            context => readCompletion(context) is { } mode
                ? new ButtonAppearance(IoPortPanelState.CompletionIcon(mode), "io_finish_button", IoPortPanelState.CompletionKey(mode))
                : new ButtonAppearance("button_placeholder", "io_status_settings_unavailable"),
            context =>
            {
                if (readCompletion(context) is { } mode) setCompletion(context, IoPortPanelState.NextCompletion(mode));
            }));
        registry.Freeze();
        return registry;
    }
}
